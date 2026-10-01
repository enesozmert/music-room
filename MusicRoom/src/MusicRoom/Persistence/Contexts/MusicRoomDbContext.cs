using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MusicRoom.Domain.Common;
using MusicRoom.Domain.Entities;

namespace MusicRoom.Persistence.Contexts;

/// <summary>
/// The mapped model. Schema ownership belongs to EF Core migrations and to nothing else — SDD §8.5.
/// </summary>
public sealed class MusicRoomDbContext : DbContext
{
    /// <summary>Takes the options the composition root configured.</summary>
    /// <param name="options">Provider and connection options.</param>
    public MusicRoomDbContext(DbContextOptions<MusicRoomDbContext> options)
        : base(options)
    {
    }

    /// <summary>The accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>The outstanding and spent email verification tokens.</summary>
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    /// <summary>The live, spent and revoked refresh tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>The outstanding and spent password reset tokens.</summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    /// <summary>The mandatory audit log — subject V.6, SDD §13.4.</summary>
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    /// <summary>One profile per account — subject V.1's tiered fields.</summary>
    public DbSet<Profile> Profiles => Set<Profile>();

    /// <summary>The genres and artists each account named — subject V.1.</summary>
    public DbSet<MusicPreference> MusicPreferences => Set<MusicPreference>();

    /// <summary>Pending, accepted and declined friend requests — the only way a friendship begins.</summary>
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();

    /// <summary>Symmetric friendships, one row per pair in canonical order.</summary>
    public DbSet<Friendship> Friendships => Set<Friendship>();

    /// <summary>Collaboratively edited playlists — subject V.2.3. Soft-deleted rows are filtered out.</summary>
    public DbSet<Playlist> Playlists => Set<Playlist>();

    /// <summary>The people named on a playlist, and whether each may write to it.</summary>
    public DbSet<PlaylistCollaborator> PlaylistCollaborators => Set<PlaylistCollaborator>();

    /// <summary>The tracks of a playlist, in sparse position order — SPEC-015.</summary>
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();

    /// <summary>Track-vote events — subject V.2.1. Soft-deleted rows are filtered out.</summary>
    public DbSet<Event> Events => Set<Event>();

    /// <summary>Who the owner invited to an event. On a private event this is the `member` gate.</summary>
    public DbSet<EventInvitation> EventInvitations => Set<EventInvitation>();

    /// <summary>Who is on an event's roster — a roster, never a gate (SPEC-017 decision 5).</summary>
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();

    /// <summary>The tracks suggested for an event — subject V.2.1.</summary>
    public DbSet<EventSuggestion> EventSuggestions => Set<EventSuggestion>();

    /// <summary>The votes. There is no counter: the queue counts these rows — SPEC-018 decision 2.</summary>
    public DbSet<EventVote> EventVotes => Set<EventVote>();

    /// <summary>The short-lived venue codes, stored as digests — ADR-0014.</summary>
    public DbSet<EventCode> EventCodes => Set<EventCode>();

    /// <summary>One row per account per code: single use, enforced by the database.</summary>
    public DbSet<EventCodeRedemption> EventCodeRedemptions => Set<EventCodeRedemption>();

    /// <summary>Registered devices and who holds playback control of each — subject V.2.2, ADR-0017.</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Who is playing what, on which device — ADR-0017. One live session per device.</summary>
    public DbSet<PlaybackSession> PlaybackSessions => Set<PlaybackSession>();

    /// <summary>Stamps the common columns, then saves — SDD §8.3.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The number of rows written.</returns>
    /// <remarks>
    /// Stamping here rather than in each handler means no aggregate can forget it, and
    /// <c>updated_at_utc</c> stays honest even for a write path added later.
    /// </remarks>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = DateTime.UtcNow;

        foreach (EntityEntry<Entity> entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = nowUtc;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Applies every <see cref="IEntityTypeConfiguration{TEntity}"/> in this assembly.</summary>
    /// <param name="modelBuilder">The model being built.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MusicRoomDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
