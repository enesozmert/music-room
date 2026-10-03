package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ScenarioBuilder;
import io.gatling.javaapi.core.Simulation;

/**
 * Realtime fan-out: N connections in one group, listening while the group's writes happen.
 *
 * <p>`load-testing` says concurrent **connections** and concurrent **requests** are different limits and both
 * must be stated, and that realtime is the scaling risk rather than REST. This scenario measures the first:
 * how many SignalR connections one instance holds in one group, and what a broadcast costs once they are
 * there.
 *
 * <p>It speaks SignalR's protocol by hand rather than through a client library, because Gatling's WebSocket
 * DSL is what reports the numbers. Three things have to be right: the `negotiate` POST, which the server
 * answers with a connection token; the handshake frame, which is JSON terminated by the record separator
 * `0x1E`; and the invocation frame that joins the group. The token travels in the query string, which is the
 * documented SignalR mechanism and is read only under `/hubs` (SPEC-016 decision 4).
 */
public final class HubFanOutSimulation extends Simulation {

    /** SignalR's record separator: every frame on the wire ends with it. */
    private static final String SEPARATOR = "\u001e";

    /** The handshake a client must send before any invocation is accepted. */
    private static final String HANDSHAKE = "{\"protocol\":\"json\",\"version\":1}" + SEPARATOR;

    private static final String PLAYLIST_ID = QueueReadSimulation.seededId("playlist-id.txt");

    /** The track the writer keeps moving, and the one it moves it in front of. */
    private static final String ANCHOR_TRACK_ID =
        LoadProfile.readSeed("tracks.txt").get(0).get("trackId").toString();

    private static final String MOVED_TRACK_ID =
        LoadProfile.readSeed("tracks.txt").get(1).get("trackId").toString();

    private final ScenarioBuilder scenario = scenario("hub fan-out in one group")
        .feed(LoadProfile.tokens())

        // The negotiate is an ordinary authenticated POST, and the only part of the handshake that is HTTP.
        .exec(http("POST negotiate")
            .post("/hubs/playlists/negotiate?negotiateVersion=1&platform=LoadTest&device=gatling&appVersion=0.0.0-load")
            .queryParam("access_token", "#{token}")
            .check(status().is(200))
            .check(jmesPath("connectionToken").saveAs("connectionToken")))

        .exec(ws("connect")
            .connect("/hubs/playlists?access_token=#{token}&id=#{connectionToken}"
                + "&platform=LoadTest&device=gatling&appVersion=0.0.0-load"))

        // The handshake reply is awaited and not ignored. SignalR answers it with `{}`, and a scenario that
        // leaves that frame unread has it matched by the next check instead of the reply that check is for.
        .exec(ws("handshake").sendText(HANDSHAKE)
            .await(java.time.Duration.ofSeconds(10))
            .on(ws.checkTextMessage("handshake reply").check(regex("^\\{\\}.*").exists())))

        // Join the group this scenario is measuring the fan-out of. The reply is the invocation's completion.
        .exec(ws("JoinPlaylist")
            .sendText("{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinPlaylist\",\"arguments\":[\""
                + PLAYLIST_ID + "\"]" + "}" + SEPARATOR)
            .await(java.time.Duration.ofSeconds(10))
            .on(ws.checkTextMessage("joined").check(regex(".*\"invocationId\":\"1\".*"))))

        // Ten broadcasts per listener. The writer below moves one track every 500 ms, so what this step
        // reports is the interval between two broadcasts arriving at one listener: around 500 ms means the
        // fan-out is keeping up with the writer, and a growing interval means it is falling behind. The ping
        // is what a real client sends to keep the connection alive; here it also gives the await something
        // to hang off, because the frame being measured is one the server sends unprompted.
        .repeat(10).on(
            exec(ws("TrackMoved interval").sendText("{\"type\":6}" + SEPARATOR)
                .await(java.time.Duration.ofSeconds(60))
                .on(ws.checkTextMessage("broadcast").check(regex(".*\"TrackMoved\".*").exists()))))

        .exec(ws("close").close());

    /**
     * One writer, moving one track over and over, so the listeners have something to hear.
     *
     * <p>A fan-out measurement with no writer measures idle connections. One writer is also the honest
     * shape: the broadcast cost that matters is one write multiplied by the number of listeners, not many
     * writes at once — that case is `PlaylistReorderSimulation`.
     */
    private final ScenarioBuilder writer = scenario("one writer the group hears")
        .feed(LoadProfile.tokens())
        .repeat(200).on(
            exec(http("GET tracks for ETag")
                .get("/api/v1/playlists/" + PLAYLIST_ID + "/tracks")
                .headers(LoadProfile.bearer())
                .check(status().is(200))
                .check(header("ETag").saveAs("etag")))
                .exec(http("PATCH move track")
                    .patch("/api/v1/playlists/" + PLAYLIST_ID + "/tracks/" + MOVED_TRACK_ID)
                    .headers(LoadProfile.bearer())
                    .header("If-Match", "#{etag}")
                    .body(StringBody("{\"afterTrackId\":null,\"beforeTrackId\":\"" + ANCHOR_TRACK_ID + "\"}"))
                    .check(status().in(200, 412, 422)))
                .pause(java.time.Duration.ofMillis(500)));

    {
        setUp(
            scenario.injectOpen(
                // Connections, not requests: ramped so the report can say where the handshake starts to
                // suffer, and held so the count is concurrent rather than cumulative.
                rampUsers(50).during(java.time.Duration.ofSeconds(20)),
                rampUsers(150).during(java.time.Duration.ofSeconds(40))),
            writer.injectOpen(atOnceUsers(1)))
            .protocols(LoadProfile.protocol())
            .assertions(
                global().failedRequests().percent().lte(LoadProfile.MAXIMUM_FAILED_PERCENT),
                details("POST negotiate").responseTime().percentile3().lt(2_000),
                details("TrackMoved interval").responseTime().percentile3().lt(5_000));
    }
}
