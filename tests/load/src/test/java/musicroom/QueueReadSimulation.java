package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ScenarioBuilder;
import io.gatling.javaapi.core.Simulation;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * The hot authenticated read: `GET /api/v1/events/{eventId}/queue`.
 *
 * <p>The most frequent call during an event, and the one whose cost is a `GROUP BY` over the vote rows —
 * SPEC-018 decided to count votes on every read rather than keep a counter, so this scenario is the
 * measurement that decision is accountable to.
 */
public final class QueueReadSimulation extends Simulation {

    private static final String EVENT_ID = seededId("event-id.txt");

    private final ScenarioBuilder scenario = scenario("authenticated queue read")
        .feed(LoadProfile.tokens())
        .repeat(10).on(
            exec(http("GET queue")
                .get("/api/v1/events/" + EVENT_ID + "/queue?page=1&pageSize=20")
                .headers(LoadProfile.bearer())
                .check(status().is(200)))
                .pause(java.time.Duration.ofMillis(200)));

    {
        setUp(scenario.injectClosed(LoadProfile.closedSteps()))
            .protocols(LoadProfile.protocol())
            .assertions(
                global().failedRequests().percent().lte(LoadProfile.MAXIMUM_FAILED_PERCENT),
                details("GET queue").responseTime().percentile3().lt(1_000));
    }

    /** A single id the seed wrote, such as the event every scenario shares. */
    static String seededId(String fileName) {
        Path file = LoadProfile.SEED_DIRECTORY.resolve(fileName);

        try {
            return Files.readString(file).trim();
        } catch (Exception cause) {
            throw new IllegalStateException(
                "The load seed is missing: " + file.toAbsolutePath()
                    + ". Run tests/load/seed/seed.sh against a running stack first.",
                cause);
        }
    }

    /** A feeder that cycles the seeded lines, so every virtual user has its own account. */
    static io.gatling.javaapi.core.FeederBuilder<Object> listFeeder(String fileName) {
        return io.gatling.javaapi.core.CoreDsl.listFeeder(LoadProfile.readSeed(fileName)).circular();
    }
}
