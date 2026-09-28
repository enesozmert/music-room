using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicRoom.Persistence.Migrations;

/// <summary>
/// Gives every account that predates <see cref="Profiles"/> an empty profile row — SPEC-011 escalation 4.
/// </summary>
/// <remarks>
/// **Data, not schema**, and separate from the migration that created the table because
/// `database-design-migrations` requires exactly that: a backfill is reviewed as data, reverted as data, and
/// must be idempotent.
/// <para>
/// Idempotent by construction: the <c>WHERE NOT EXISTS</c> means running it twice inserts nothing the second
/// time. EF generates no backfill, so the statement is written by hand, and the default tiers are the ones
/// <c>Profile</c> states — `real_name` and `location` friends-only, `bio` public, `birth_date` private. Anything
/// else would hand a different default to accounts created before and after this migration.
/// </para>
/// <para>
/// <c>Down</c> deletes only the rows it could have created — profiles with nothing filled in. A profile a user
/// has since edited is their data, and a rollback must not throw it away.
/// </para>
/// </remarks>
public partial class ProfileBackfill : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            """
            INSERT INTO profiles (
                id,
                user_id,
                real_name,
                real_name_visibility,
                bio,
                bio_visibility,
                location,
                location_visibility,
                birth_date,
                birth_date_visibility,
                created_at_utc,
                updated_at_utc)
            SELECT
                gen_random_uuid(),
                u.id,
                NULL,
                1,
                NULL,
                0,
                NULL,
                1,
                NULL,
                2,
                now(),
                now()
            FROM users AS u
            WHERE NOT EXISTS (SELECT 1 FROM profiles AS p WHERE p.user_id = u.id);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            """
            DELETE FROM profiles
            WHERE real_name IS NULL
              AND bio IS NULL
              AND location IS NULL
              AND birth_date IS NULL;
            """);
    }
}
