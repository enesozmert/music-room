using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace MusicRoom.WebAPI.HealthChecks;

/// <summary>
/// Readiness probe for the PostgreSQL instance behind <c>DATABASE_URL</c>.
/// S0 to S3 point at the local compose container, S4 onward at Supabase — ADR-0026. The engine and
/// this check are identical either way; only the connection string differs.
/// </summary>
internal sealed class DatabaseReadinessCheck : IHealthCheck
{
    private readonly string _connectionString;

    internal DatabaseReadinessCheck(string connectionString) =>
        _connectionString = BuildConnectionString(connectionString);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using NpgsqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("reachable");
        }
        catch (NpgsqlException)
        {
            // The reason is logged, never returned. A readiness body that carries the host, the
            // server version or an exception message is an information disclosure — SPEC-001 gate 5.
            return HealthCheckResult.Unhealthy("not reachable");
        }
        catch (TimeoutException)
        {
            return HealthCheckResult.Unhealthy("not reachable");
        }
    }

    private static string BuildConnectionString(string connectionString)
    {
        // Wait this long for a connection before reporting not ready.
        const int connectTimeoutSeconds = 3;

        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            Timeout = connectTimeoutSeconds,
        };

        return builder.ConnectionString;
    }
}
