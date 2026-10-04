using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MusicRoom.Persistence.Contexts;

/// <summary>
/// Builds the context for `dotnet ef` without starting the application — SDD §8.5, ADR-0026.
/// </summary>
/// <remarks>
/// Migrating the schema and sending mail have nothing to do with each other, and before this they did.
/// `dotnet ef` falls back to the start-up project's host when no factory exists, so `make migrate` ran every
/// start-up guard the web host has: a `.env` with `MAIL_PROVIDER=brevo` and an empty `MAIL_FROM` stopped the
/// migration with *"MAIL_PROVIDER is brevo but MAIL_FROM is not set"*, and then with the real symptom —
/// *"Unable to create a 'DbContext' of type 'MusicRoomDbContext'"*. The guards are right (SPEC-024 closed
/// threat T-26 with them); what was wrong is that the schema tool needed them satisfied.
/// <para>
/// EF Core prefers a factory over the host, so this type is what `make migrate` and `make migration` now use.
/// It reads exactly one value, the same one the host reads and from the same place — the `Makefile` exports
/// `.env` before the call — and it has no default: a connection string nobody intended is worse than a
/// refusal, which is ADR-0026's rule and the reason the host has no fallback either.
/// </para>
/// </remarks>
public sealed class MusicRoomDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MusicRoomDbContext>
{
    /// <summary>The one variable this factory reads — ADR-0026.</summary>
    public const string ConnectionStringVariable = "DATABASE_URL";

    /// <summary>Builds a context against <c>DATABASE_URL</c>.</summary>
    /// <param name="args">Whatever `dotnet ef` was given. Unused: there is one source of this value.</param>
    /// <returns>A context configured for PostgreSQL, ready to read the model or run a migration.</returns>
    /// <exception cref="InvalidOperationException"><c>DATABASE_URL</c> is not set or is empty.</exception>
    public MusicRoomDbContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} is not set. `make migrate` exports `.env` before calling "
                + "`dotnet ef`, so an empty value here means the file is missing the key — see ADR-0026.");
        }

        DbContextOptionsBuilder<MusicRoomDbContext> options = new();
        options.UseNpgsql(connectionString);

        return new MusicRoomDbContext(options.Options);
    }
}
