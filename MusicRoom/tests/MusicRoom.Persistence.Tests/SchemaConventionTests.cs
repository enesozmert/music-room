using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MusicRoom.Persistence.Contexts;
using Xunit;

namespace MusicRoom.Persistence.Tests;

/// <summary>
/// Holds the built model to the naming conventions in
/// <c>docs/technical-guide/30-data/database-design-migrations/STANDARD.md</c>.
/// </summary>
/// <remarks>
/// Every table and column name is written out by hand in an <c>IEntityTypeConfiguration&lt;T&gt;</c>,
/// because the project pins no naming-convention package. Hand-written names drift: a forgotten
/// <c>HasColumnName</c> yields a PascalCase column that compiles, migrates and ships. These tests
/// are the gate that C# has in the analyzers and the schema had in nothing.
///
/// The model is built, not connected to. The connection string is never opened.
/// </remarks>
public sealed class SchemaConventionTests
{
    /// <summary>Lowercase words separated by single underscores.</summary>
    private static readonly Regex _snakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>Built once; the tests only read it.</summary>
    private static readonly IModel _model = BuildModel();

    /// <summary>Table names are snake_case — STANDARD.md "Naming".</summary>
    /// <remarks>Plurality is not asserted: no rule distinguishes a plural from a noun ending in s.</remarks>
    [Fact]
    public void Model_EveryTable_UsesASnakeCaseName()
    {
        foreach (IEntityType entityType in _model.GetEntityTypes())
        {
            string? table = entityType.GetTableName();

            Assert.True(
                table is not null && _snakeCase.IsMatch(table),
                $"{entityType.ClrType.Name} maps to table '{table}', which is not snake_case");
        }
    }

    /// <summary>Column names are snake_case — STANDARD.md "Naming".</summary>
    [Fact]
    public void Model_EveryColumn_UsesASnakeCaseName()
    {
        foreach (IProperty property in _model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()))
        {
            string column = property.GetColumnName();

            Assert.True(
                _snakeCase.IsMatch(column),
                $"{property.DeclaringType.ClrType.Name}.{property.Name} maps to column '{column}', which is not snake_case");
        }
    }

    /// <summary>A single-column primary key is called <c>id</c> — STANDARD.md "Naming".</summary>
    [Fact]
    public void Model_EverySingleColumnPrimaryKey_IsCalledId()
    {
        foreach (IEntityType entityType in _model.GetEntityTypes())
        {
            IKey? key = entityType.FindPrimaryKey();

            if (key is null || key.Properties.Count != 1)
            {
                continue;
            }

            Assert.Equal("id", key.Properties[0].GetColumnName());
        }
    }

    /// <summary>A foreign key column is <c>&lt;referenced_singular&gt;_id</c> — STANDARD.md "Naming".</summary>
    [Fact]
    public void Model_EveryForeignKeyColumn_EndsWithIdSuffix()
    {
        IEnumerable<IProperty> foreignKeyProperties = _model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .SelectMany(foreignKey => foreignKey.Properties);

        foreach (IProperty property in foreignKeyProperties)
        {
            string column = property.GetColumnName();

            Assert.True(
                column.EndsWith("_id", StringComparison.Ordinal),
                $"foreign key column '{column}' does not end in _id");
        }
    }

    /// <summary>
    /// An index is named <c>ix_&lt;table&gt;_&lt;columns&gt;</c>, a unique index <c>ux_</c> —
    /// STANDARD.md "Naming".
    /// </summary>
    [Fact]
    public void Model_EveryIndex_FollowsThePrefixConvention()
    {
        foreach (IEntityType entityType in _model.GetEntityTypes())
        {
            AssertIndexNames(entityType);
        }
    }

    /// <summary>
    /// Every table carries <c>id</c>, <c>created_at_utc</c> and <c>updated_at_utc</c> —
    /// STANDARD.md "Schema rules", SDD section 8.3.
    /// </summary>
    [Fact]
    public void Model_EveryTable_CarriesTheCommonColumns()
    {
        string[] required = ["id", "created_at_utc", "updated_at_utc"];

        foreach (IEntityType entityType in _model.GetEntityTypes())
        {
            HashSet<string> columns = entityType.GetProperties()
                .Select(property => property.GetColumnName())
                .ToHashSet(StringComparer.Ordinal);

            string missing = string.Join(", ", required.Where(column => !columns.Contains(column)));

            Assert.True(missing.Length == 0, $"table '{entityType.GetTableName()}' is missing {missing}");
        }
    }

    /// <summary>
    /// Instants are <c>timestamp with time zone</c> — STANDARD.md "Schema rules", SDD section 8.3.
    /// </summary>
    [Fact]
    public void Model_EveryInstantColumn_IsTimestampWithTimeZone()
    {
        IEnumerable<IProperty> instants = _model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => Nullable.GetUnderlyingType(property.ClrType) == typeof(DateTime)
                || property.ClrType == typeof(DateTime));

        foreach (IProperty property in instants)
        {
            Assert.Equal("timestamp with time zone", property.GetColumnType());
        }
    }

    /// <summary>Checks one entity's index names against the convention.</summary>
    /// <param name="entityType">The mapped entity.</param>
    private static void AssertIndexNames(IEntityType entityType)
    {
        string table = entityType.GetTableName() ?? entityType.ClrType.Name;

        foreach (IIndex index in entityType.GetIndexes())
        {
            string columns = string.Join('_', index.Properties.Select(property => property.GetColumnName()));
            string expected = $"{(index.IsUnique ? "ux" : "ix")}_{table}_{columns}";

            Assert.Equal(expected, index.GetDatabaseName());
        }
    }

    /// <summary>Builds the model without opening a connection.</summary>
    /// <returns>The mapped model.</returns>
    private static IModel BuildModel()
    {
        DbContextOptions<MusicRoomDbContext> options =
            new DbContextOptionsBuilder<MusicRoomDbContext>()
                .UseNpgsql("Host=schema-convention-tests;Database=musicroom")
                .Options;

        using MusicRoomDbContext context = new(options);

        return context.Model;
    }
}
