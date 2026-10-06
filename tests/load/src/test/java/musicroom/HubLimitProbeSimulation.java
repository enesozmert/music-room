package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ScenarioBuilder;
import io.gatling.javaapi.core.Simulation;
import java.util.concurrent.atomic.LongAdder;

/**
 * The re-measurement ADR-0030 commissioned — SPEC-033.
 *
 * <p>ADR-0030 set three numbers on 2026-10-04 and said what they were: *"a starting point with a measurement
 * behind the shape rather than the value"*, with *"the first run after this lands says whether 60 is generous or
 * tight"*. SPEC-026 landed them. This asks the three questions separately, because the answers can differ:
 *
 * <ol>
 *   <li><b>Paced client.</b> What invocation rate does a realistic client reach? ADR-0030's own definition is a
 *       client that *"joins a group and then mostly listens"*, so this one enters a screen, stays a few seconds,
 *       leaves, and repeats. The rate it reaches is what 60 a minute has to be judged against.</li>
 *   <li><b>Unpaced client.</b> Where does the limiter actually refuse, and how? A `HubException` arrives as a
 *       completion carrying `error`, which is what this counts — a refusal the client can act on rather than a
 *       dropped connection.</li>
 *   <li><b>Multi-hub client.</b> How many connections does <em>one</em> client hold? `PlaylistHub`, `EventHub`
 *       and `ControlHub` are three separate connections, and the ceiling is five per account. The handshake
 *       refusal is a `429` from the middleware, so this probe needs no WebSocket to see it — it needs the
 *       earlier connections to still be open, which is why they are.</li>
 * </ol>
 *
 * <p>Beside `HubFanOutSimulation` rather than inside it, on purpose: that scenario injects two hundred
 * connections and one writer to time a broadcast arriving, and `docs/load/REPORT.md` §5.4 already cites its
 * numbers. `LoadProfile` is reused and never forked.
 */
public final class HubLimitProbeSimulation extends Simulation {

    /** SignalR's record separator: every frame on the wire ends with it. */
    private static final String SEPARATOR = "\u001e";

    /** The handshake a client must send before any invocation is accepted. */
    private static final String HANDSHAKE = "{\"protocol\":\"json\",\"version\":1}" + SEPARATOR;

    /** The three client values the middleware requires on a handshake — SPEC-016 decision 3. */
    private static final String CLIENT_CONTEXT = "platform=LoadTest&device=gatling&appVersion=0.0.0-load";

    private static final String PLAYLIST_ID = QueueReadSimulation.seededId("playlist-id.txt");

    /** How long a paced client stays on one screen before leaving it. */
    private static final java.time.Duration DWELL = java.time.Duration.ofSeconds(4);

    /** How many screens a paced client visits. Twelve pairs over ~60 s is the rate the report quotes. */
    private static final int PACED_VISITS = 12;

    /** How many invocations an unpaced client attempts. Above 60 on purpose: the point is where it stops. */
    private static final int UNPACED_ATTEMPTS = 90;

    /** How many connections the multi-hub probe tries to hold. Above the ceiling of five, for the same reason. */
    private static final int CONNECTION_ATTEMPTS = 6;

    // Counters rather than Gatling request names, because what is being measured is a count per connection and
    // Gatling reports per request name. LoginBurstSimulation established the shape.
    private static final LongAdder PACED_ACCEPTED = new LongAdder();
    private static final LongAdder PACED_REFUSED = new LongAdder();
    private static final LongAdder UNPACED_ACCEPTED = new LongAdder();
    private static final LongAdder UNPACED_REFUSED = new LongAdder();
    private static final LongAdder CONNECTIONS_ACCEPTED = new LongAdder();
    private static final LongAdder CONNECTIONS_REFUSED = new LongAdder();

    /**
     * Probe 1: a client that joins a screen, stays, leaves, and repeats.
     */
    private final ScenarioBuilder paced = scenario("paced client")
        .feed(LoadProfile.tokens())
        .exec(Hub.open("/hubs/playlists"))
        .repeat(PACED_VISITS).on(
            exec(Hub.invoke("JoinPlaylist", PACED_ACCEPTED, PACED_REFUSED))
                .pause(DWELL)
                .exec(Hub.invoke("LeavePlaylist", PACED_ACCEPTED, PACED_REFUSED))
                .pause(java.time.Duration.ofSeconds(1)))
        .exec(ws("close paced").close());

    /**
     * Probe 2: the same client with the pauses taken out.
     */
    private final ScenarioBuilder unpaced = scenario("unpaced client")
        .feed(LoadProfile.tokens())
        .exec(Hub.open("/hubs/playlists"))
        .repeat(UNPACED_ATTEMPTS).on(
            exec(Hub.invoke("JoinPlaylist", UNPACED_ACCEPTED, UNPACED_REFUSED)))
        .exec(ws("close unpaced").close());

    /**
     * Probe 3: one account opening a connection per hub, then more, until the ceiling refuses one.
     *
     * <p>Only the negotiate is needed to see the refusal — the ceiling is enforced there, as a `429` with
     * `Retry-After` — but the earlier connections have to be **established** for the count to grow, so each
     * accepted negotiate is followed by a real handshake and left open until the scenario ends.
     */
    private final ScenarioBuilder connections = scenario("multi-hub client")
        .feed(LoadProfile.tokens())
        .repeat(CONNECTION_ATTEMPTS, "attempt").on(
            exec(session -> session.set("hub", hubFor(session.getInt("attempt"))))
                .exec(http("POST negotiate (ceiling probe)")
                    .post("#{hub}/negotiate?negotiateVersion=1&" + CLIENT_CONTEXT)
                    .queryParam("access_token", "#{token}")
                    .check(status().in(200, 429))
                    .check(status().saveAs("negotiateStatus"))
                    .check(jmesPath("connectionToken").optional().saveAs("connectionToken")))
                .doIf(session -> session.getInt("negotiateStatus") == 200).then(
                    exec(ws("connect #{attempt}", "hub-#{attempt}")
                        .connect("#{hub}?access_token=#{token}&id=#{connectionToken}&" + CLIENT_CONTEXT))
                        .exec(ws("handshake #{attempt}", "hub-#{attempt}").sendText(HANDSHAKE)
                            .await(java.time.Duration.ofSeconds(10))
                            .on(ws.checkTextMessage("reply").check(regex("^\\{\\}.*").exists())))
                        .exec(session -> {
                            CONNECTIONS_ACCEPTED.increment();
                            return session;
                        }))
                .doIf(session -> session.getInt("negotiateStatus") == 429).then(
                    exec(session -> {
                        CONNECTIONS_REFUSED.increment();
                        return session;
                    })));

    {
        // One virtual user per probe, sequentially named: each measures a single client, and a second
        // concurrent client would spend the same per-account allowance and make the count unreadable.
        setUp(
            paced.injectOpen(atOnceUsers(1)),
            unpaced.injectOpen(nothingFor(java.time.Duration.ofSeconds(90)), atOnceUsers(1)),
            connections.injectOpen(nothingFor(java.time.Duration.ofSeconds(180)), atOnceUsers(1)))
            .protocols(LoadProfile.protocol())
            .assertions(global().failedRequests().percent().lte(LoadProfile.MAXIMUM_FAILED_PERCENT));
    }

    /** Which hub the nth attempt opens: the three a real client holds, then round again. */
    private static String hubFor(int attempt) {
        String[] hubs = {"/hubs/playlists", "/hubs/events", "/hubs/control"};

        return hubs[(attempt - 1) % hubs.length];
    }

    /** Prints what Gatling's per-request summary cannot: a count per probe. */
    @Override
    public void after() {
        System.out.println("probe 1, paced client:    accepted " + PACED_ACCEPTED.sum()
            + ", refused " + PACED_REFUSED.sum()
            + " (" + (2 * PACED_VISITS) + " attempted over ~" + (PACED_VISITS * 5) + " s)");
        System.out.println("probe 2, unpaced client:  accepted " + UNPACED_ACCEPTED.sum()
            + ", refused " + UNPACED_REFUSED.sum()
            + " (" + UNPACED_ATTEMPTS + " attempted as fast as the client could)");
        long established = CONNECTIONS_ACCEPTED.sum();
        long negotiateRefusals = CONNECTIONS_REFUSED.sum();

        System.out.println("probe 3, multi-hub:       connections established " + established
            + ", negotiate refusals " + negotiateRefusals
            + ", attempted " + CONNECTION_ATTEMPTS + " for one account across /hubs/playlists, /hubs/events"
            + " and /hubs/control");

        // An attempt that is neither established nor refused at the negotiate was refused at the hub filter —
        // `OnConnectedAsync` throwing, which SPEC-026 made the authoritative half of the ceiling. The count
        // this probe reports is therefore "connections one account can hold", and the layer that said no is in
        // the application log rather than here.
        System.out.println("probe 3, unaccounted:     " + (CONNECTION_ATTEMPTS - established - negotiateRefusals)
            + " (refused at the hub filter rather than the handshake)");
    }

    /**
     * The handshake and one invocation, shared by the two invocation probes.
     *
     * <p>Declared here rather than in `LoadProfile`, which carries what **every** scenario shares: the base
     * URL, the tokens, the protocol and the ramp. A SignalR frame shape is this simulation's business.
     */
    private static final class Hub {

        private Hub() {
        }

        /** Negotiates, connects and completes the handshake on one hub. */
        static io.gatling.javaapi.core.ChainBuilder open(String path) {
            return exec(http("POST negotiate")
                .post(path + "/negotiate?negotiateVersion=1&" + CLIENT_CONTEXT)
                .queryParam("access_token", "#{token}")
                .check(status().is(200))
                .check(jmesPath("connectionToken").saveAs("connectionToken")))
                .exec(ws("connect")
                    .connect(path + "?access_token=#{token}&id=#{connectionToken}&" + CLIENT_CONTEXT))
                .exec(ws("handshake").sendText(HANDSHAKE)
                    .await(java.time.Duration.ofSeconds(10))
                    .on(ws.checkTextMessage("handshake reply").check(regex("^\\{\\}.*").exists())));
        }

        /**
         * Invokes a hub method and counts whether it was accepted or refused.
         *
         * <p>A refusal is a completion carrying `error` — that is what `HubException` looks like on the wire,
         * and counting it is the measurement: the connection stays open, which is the behaviour ADR-0030 asked
         * for and the opposite of being dropped.
         */
        static io.gatling.javaapi.core.ChainBuilder invoke(String method, LongAdder accepted, LongAdder refused) {
            return exec(ws(method)
                .sendText("{\"type\":1,\"invocationId\":\"1\",\"target\":\"" + method + "\",\"arguments\":[\""
                    + PLAYLIST_ID + "\"]}" + SEPARATOR)
                .await(java.time.Duration.ofSeconds(10))
                .on(ws.checkTextMessage("completion")
                    .check(regex(".*\"invocationId\":\"1\".*").exists())
                    .check(regex(".*(\"error\").*").optional().saveAs("hubError"))))
                .exec(session -> {
                    if (session.contains("hubError") && session.getString("hubError") != null) {
                        refused.increment();
                    } else {
                        accepted.increment();
                    }

                    return session.remove("hubError");
                });
        }
    }
}
