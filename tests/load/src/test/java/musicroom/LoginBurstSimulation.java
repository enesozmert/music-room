package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ScenarioBuilder;
import io.gatling.javaapi.core.Simulation;
import java.util.concurrent.atomic.LongAdder;

/**
 * The rate-limited path, which must degrade **correctly** rather than fall over.
 *
 * <p>`POST /api/v1/auth/sessions` carries a per-IP policy and a per-account counter (SPEC-006), and one load
 * generator is one IP, so this scenario trips the limiter on purpose. A `429` is therefore the measurement's
 * subject and not an error: what would be a failure is a `5xx`, a hang, or a `429` that arrives without
 * `Retry-After`. BCrypt at cost 12 makes every accepted attempt expensive by design, so this is also where
 * the CPU ceiling shows first.
 */
public final class LoginBurstSimulation extends Simulation {

    /**
     * Accepted and refused attempts, counted separately.
     *
     * <p>Gatling reports one line per request name, and both answers are the same request here, so the split
     * has to be counted. The report needs it: "two thousand attempts, twenty accepted" is the sentence the
     * limiter is accountable to, and a single total does not say it.
     */
    private static final LongAdder ACCEPTED = new LongAdder();

    private static final LongAdder REFUSED = new LongAdder();

    private final ScenarioBuilder scenario = scenario("login burst")
        .feed(QueueReadSimulation.listFeeder("accounts.txt"))
        .exec(http("POST session")
            .post("/api/v1/auth/sessions")
            .body(StringBody(session ->
                "{\"email\":\"" + session.getString("email") + "\",\"password\":\""
                    + LoadProfile.PASSWORD + "\"}"))

            // Accepted or refused by the limiter. Both are the server working as designed, and the report
            // separates them: a `429` is the limiter doing its job, not a failure of capacity.
            //
            // That a `429` carries `Retry-After` is asserted functionally by
            // `CredentialRateLimitingTests`, not here — a load assertion that only fires on a refused
            // request would pass on a run where nothing was refused, which is the opposite of evidence.
            .check(status().in(200, 429))
            .check(status().saveAs("code")))

        .exec(session -> {
            if (session.getInt("code") == 429) {
                REFUSED.increment();
            } else {
                ACCEPTED.increment();
            }

            return session;
        });

    {
        setUp(scenario.injectOpen(
                rampUsersPerSec(5).to(50).during(java.time.Duration.ofSeconds(30)),
                constantUsersPerSec(50).during(java.time.Duration.ofSeconds(30))))
            .protocols(LoadProfile.protocol())
            .assertions(
                // Every answer must be one the contract lists. `failedRequests` counts only the checks above
                // failing, so a 5xx or a missing `Retry-After` is what breaks this.
                global().failedRequests().percent().lte(LoadProfile.MAXIMUM_FAILED_PERCENT));
    }

    /** Prints the split the console summary cannot show. */
    @Override
    public void after() {
        System.out.println("login burst: accepted " + ACCEPTED.sum() + ", refused " + REFUSED.sum());
    }
}
