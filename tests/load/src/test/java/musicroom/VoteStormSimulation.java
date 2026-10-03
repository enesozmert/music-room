package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ScenarioBuilder;
import io.gatling.javaapi.core.Simulation;

/**
 * The contended write of subject V.2.1: many accounts voting on one event at once.
 *
 * <p>A vote is `PUT` and idempotent, so a repeated vote answers `200` where the first answered `201`.
 * Both are success here; what would be a failure is a `5xx`, or a `409` — there is no such answer on this
 * route, because the unique index makes a duplicate the same fact rather than a conflict (SPEC-018).
 *
 * <p>Each voter is paced at one vote every 1.2 seconds, which is fifty a minute against the sixty a minute
 * one account may cast (SPEC-018). Faster than that and every step would report the limiter refusing, which
 * is a measurement of the limiter and not of the contended write. The capacity statement this scenario
 * supports is therefore "N concurrent voters at a human rate", and the per-account ceiling is a separate,
 * already-decided number.
 */
public final class VoteStormSimulation extends Simulation {

    private static final String EVENT_ID = QueueReadSimulation.seededId("event-id.txt");

    private final ScenarioBuilder scenario = scenario("vote storm on one event")
        .feed(LoadProfile.tokens())
        .repeat(5).on(
            feed(QueueReadSimulation.listFeeder("suggestions.txt"))
                .exec(http("PUT vote")
                    .put("/api/v1/events/" + EVENT_ID + "/tracks/#{suggestionId}/vote")
                    .headers(LoadProfile.bearer())
                    .body(StringBody("{}"))
                    .check(status().in(200, 201)))
                .pause(java.time.Duration.ofMillis(1_200)));

    {
        setUp(scenario.injectClosed(LoadProfile.closedSteps()))
            .protocols(LoadProfile.protocol())
            .assertions(
                global().failedRequests().percent().lte(LoadProfile.MAXIMUM_FAILED_PERCENT),
                details("PUT vote").responseTime().percentile3().lt(2_000));
    }
}
