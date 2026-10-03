package musicroom;

import static io.gatling.javaapi.core.CoreDsl.*;
import static io.gatling.javaapi.http.HttpDsl.*;

import io.gatling.javaapi.core.ClosedInjectionStep;
import io.gatling.javaapi.core.FeederBuilder;
import io.gatling.javaapi.http.HttpProtocolBuilder;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;

/**
 * What every simulation here shares: where the API is, what the ramp looks like, and how a virtual user
 * gets a bearer token.
 *
 * <p>The three client headers are set on the protocol rather than per request, because the middleware
 * requires them on every mutation and a scenario that forgot one would measure a `400` path instead of
 * the path under test.
 */
final class LoadProfile {

    /** Where the API answers. The compose stack publishes it on the host; a container run sees the service name. */
    static final String BASE_URL = System.getenv().getOrDefault("MUSICROOM_BASE_URL", "http://localhost:8080");

    /** Where the seed script wrote the ids and credentials the simulations feed on. */
    static final Path SEED_DIRECTORY = Path.of(System.getenv().getOrDefault("MUSICROOM_SEED_DIR", "seed/out"));

    /** The password every seeded account shares. Local, throwaway, and never a real credential. */
    static final String PASSWORD = "LoadTest-Passw0rd!";

    /** The concurrency steps every ramped scenario walks, so the report can say where latency degraded. */
    static final List<Integer> STEPS = List.of(10, 25, 50, 100, 200);

    /** How long each step holds, once it is at its target rate. */
    static final java.time.Duration STEP_HOLD = java.time.Duration.ofSeconds(20);

    /** The error-rate ceiling `load-testing` sets: above this, the step is over capacity. */
    static final double MAXIMUM_FAILED_PERCENT = 1.0;

    private LoadProfile() {
    }

    /** The protocol: JSON, the mandatory client headers, and no connection reuse surprises. */
    static HttpProtocolBuilder protocol() {
        return http.baseUrl(BASE_URL)

            // The WebSocket base is separate from the HTTP one, and a relative `connect` without it fails
            // before a frame is sent. Same host, same port, `ws` scheme.
            .wsBaseUrl(BASE_URL.replaceFirst("^http", "ws"))
            .acceptHeader("application/json")
            .contentTypeHeader("application/json")
            .header("X-Client-Platform", "LoadTest")
            .header("X-Client-Device", "gatling")
            .header("X-Client-App-Version", "0.0.0-load")
            .shareConnections();
    }

    /**
     * The tokens the seed minted, one per account, cycled so every virtual user carries its own.
     *
     * <p>No scenario but the login burst signs in. `POST /auth/sessions` allows twenty attempts per IP
     * per five minutes, and all virtual users share one address, so signing in at load would measure
     * the limiter rather than the endpoint under test — SPEC-023 decision 7.
     */
    static FeederBuilder<Object> tokens() {
        return listFeeder(readSeed("tokens.txt")).circular();
    }

    /** The bearer header, for a scenario whose session already holds a token. */
    static Map<String, String> bearer() {
        return Map.of("Authorization", "Bearer #{token}");
    }

    /**
     * One closed-model step per concurrency level in {@link #STEPS}.
     *
     * <p>Shared so every ramped scenario walks the same ladder: a report that compared a read measured to
     * two hundred users against a write measured to one hundred would be comparing two different runs.
     */
    static ClosedInjectionStep[] closedSteps() {
        return STEPS.stream()
            .map(users -> (ClosedInjectionStep) constantConcurrentUsers(users).during(STEP_HOLD))
            .toArray(ClosedInjectionStep[]::new);
    }

    /** One line per seeded value, as the seed script wrote them. */
    static List<Map<String, Object>> readSeed(String fileName) {
        Path file = SEED_DIRECTORY.resolve(fileName);

        try {
            return Files.readAllLines(file).stream()
                .map(String::trim)
                .filter(line -> !line.isEmpty())
                .map(line -> (Map<String, Object>) Map.<String, Object>of(columnOf(fileName), line))
                .toList();
        } catch (Exception cause) {
            throw new IllegalStateException(
                "The load seed is missing: " + file.toAbsolutePath()
                    + ". Run tests/load/seed/seed.sh against a running stack first — SPEC-023 step 2.",
                cause);
        }
    }

    /** The session key a seed file fills, named after the file so a simulation reads what it expects. */
    private static String columnOf(String fileName) {
        return switch (fileName) {
            case "accounts.txt" -> "email";
            case "tokens.txt" -> "token";
            case "suggestions.txt" -> "suggestionId";
            case "tracks.txt" -> "trackId";
            default -> throw new IllegalArgumentException("No session key is declared for " + fileName);
        };
    }
}
