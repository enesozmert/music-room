using Microsoft.EntityFrameworkCore;
using MusicRoom.Domain.Entities;
using MusicRoom.Persistence.Contexts;
using Xunit;

namespace MusicRoom.Persistence.Tests;

/// <summary>
/// Holds `dotnet ef` to one dependency: <c>DATABASE_URL</c> — SPEC-029.
/// </summary>
/// <remarks>
/// The factory exists so that migrating the schema does not start the web host, whose guards refuse a boot
/// over an unrelated setting — a `.env` with `MAIL_PROVIDER=brevo` and an empty `MAIL_FROM` stopped
/// `make migrate`. These tests are what keeps that true: the first proves the model is readable with nothing
/// configured but the connection string, the second proves a missing one is a refusal that names the key
/// rather than a connection to a machine nobody intended (ADR-0026).
/// <para>
/// The variable is process-wide, so each test sets it and puts back what it found. Nothing here opens the
/// connection: the model is built, not connected to, exactly as `SchemaConventionTests` does it.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactoryTests
{
    private const string _variable = MusicRoomDesignTimeDbContextFactory.ConnectionStringVariable;

    /// <summary>A connection string is the whole configuration the schema tool needs.</summary>
    [Fact]
    public void CreateDbContext_WithTheConnectionString_BuildsTheModel()
    {
        string? original = Environment.GetEnvironmentVariable(_variable);

        try
        {
            Environment.SetEnvironmentVariable(
                _variable, "Host=localhost;Port=5432;Database=musicroom;Username=musicroom;Password=unused");

            using MusicRoomDbContext context = new MusicRoomDesignTimeDbContextFactory().CreateDbContext([]);

            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
            Assert.NotNull(context.Model.FindEntityType(typeof(User)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(_variable, original);
        }
    }

    /// <summary>No fallback, and the refusal says which key is missing — ADR-0026.</summary>
    [Fact]
    public void CreateDbContext_WithoutTheConnectionString_RefusesAndNamesTheKey()
    {
        string? original = Environment.GetEnvironmentVariable(_variable);

        try
        {
            Environment.SetEnvironmentVariable(_variable, null);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => new MusicRoomDesignTimeDbContextFactory().CreateDbContext([]));

            Assert.Contains(_variable, refused.Message, StringComparison.Ordinal);
            Assert.Contains("ADR-0026", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(_variable, original);
        }
    }
}
