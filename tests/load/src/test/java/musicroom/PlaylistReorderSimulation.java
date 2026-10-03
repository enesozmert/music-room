package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ScenarioBuilder;
import io.gatling.javaapi.core.Simulation;

/**
 * The contended write of subject V.2.3: several collaborators reordering one playlist.
 *
 * <p>Each virtual user reads the list for its `ETag` and then moves a track with `If-Match`. Under
 * concurrency most of those tokens are stale by the time they arrive, so **`412` is the expected answer and
 * not a failure** — it is the guard working, and SPEC-015's twenty-way test is the functional proof. What
 * this scenario measures is what that guard costs: how much work the server does to refuse, and whether the
 * refusals stay fast as concurrency climbs.
 */
public final class PlaylistReorderSimulation extends Simulation {

    private static final String PLAYLIST_ID = QueueReadSimulation.seededId("playlist-id.txt");

    /**
     * The track every move aims in front of.
     *
     * <p>A move with no neighbour on either side is a `422` and measures the validator, not the write. So
     * each user asks for the head of the list, which is the contended destination: one position, every
     * caller wanting it.
     */
    private static final String ANCHOR_TRACK_ID = LoadProfile.readSeed("tracks.txt").get(0).get("trackId").toString();

    private final ScenarioBuilder scenario = scenario("playlist reorder by several collaborators")
        .feed(LoadProfile.tokens())
        .repeat(5).on(
            exec(http("GET tracks for ETag")
                .get("/api/v1/playlists/" + PLAYLIST_ID + "/tracks")
                .headers(LoadProfile.bearer())
                .check(status().is(200))
                .check(header("ETag").saveAs("etag")))
                .feed(QueueReadSimulation.listFeeder("tracks.txt"))
                .exec(http("PATCH move track")
                    .patch("/api/v1/playlists/" + PLAYLIST_ID + "/tracks/#{trackId}")
                    .headers(LoadProfile.bearer())
                    .header("If-Match", "#{etag}")
                    .body(StringBody("{\"afterTrackId\":null,\"beforeTrackId\":\"" + ANCHOR_TRACK_ID + "\"}"))

                    // 200 moved, 412 lost the race, 422 asked for no destination — all of them are the
                    // server answering correctly. A 5xx is the only failure.
                    .check(status().in(200, 412, 422)))
                .pause(java.time.Duration.ofMillis(100)));

    {
        setUp(scenario.injectClosed(LoadProfile.closedSteps()))
            .protocols(LoadProfile.protocol())
            .assertions(
                global().failedRequests().percent().lte(LoadProfile.MAXIMUM_FAILED_PERCENT),
                details("PATCH move track").responseTime().percentile3().lt(2_000));
    }
}
