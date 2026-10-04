using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Domain.Common;
using MusicRoom.Persistence.Contexts;
using Testcontainers.PostgreSql;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// Which bucket a vote is counted in — SPEC-018, ADR-0040, subject V.2.1.
/// </summary>
/// <remarks>
/// The vote limit is per account, and this suite exists because it was not: the limiter middleware ran before
/// authentication, so `HttpContext.User` carried no claims, the partition key fell back to the remote address,
/// and every voter behind one network shared sixty votes a minute. SPEC-023's load measurement is what
/// surfaced it — 98% of votes were refused at every step, from one address with two hundred accounts.
/// <para>
/// Two accounts, one address, is therefore the whole test: the first may exhaust its allowance and the second
/// must still be able to vote.
/// </para>
/// </remarks>
public sealed class EventVoteRateLimitingTests : IAsyncLifetime
{
    // Internal rather than private because the .editorconfig naming rule asks a private field for an
    // underscore prefix, and a const is a field to that rule — the same reason CredentialRateLimitingTests
    // declares its constants this way.

    /// <summary>The per-account allowance this suite spends, as `EventRateLimiterPolicies` sets it.</summary>
    internal const int VotePermits = 60;

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.2-alpine").Build();
    private readonly RecordingMailService _mail = new();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Starts the engine, replaces the mail sender, and brings the schema up.</summary>
    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync(TestContext.Current.CancellationToken);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("DATABASE_URL", _database.GetConnectionString());
            TestJwtSettings.Apply(webHost);
            TestMailSettings.Apply(webHost);

            // The credential limits are raised because signing two accounts in is not what is under test.
            // The vote limit is not raised: it is the subject of this suite.
            TestRateLimitSettings.Relax(webHost);
            webHost.ConfigureTestServices(services =>
                services.AddSingleton<IMailService>(_mail));
        });

        using IServiceScope scope = _factory.Services.CreateScope();
        MusicRoomDbContext context = scope.ServiceProvider.GetRequiredService<MusicRoomDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Tears the container down.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>
    /// One account spends its whole allowance and a second account, from the same address, still votes.
    /// </summary>
    /// <remarks>
    /// The assertion is about the partition and not about the number: that the first account is eventually
    /// refused proves the policy is attached, and that the second is not refused proves what it is keyed on.
    /// </remarks>
    [Fact]
    public async Task Votes_FromTwoAccountsOnOneAddress_AreCountedSeparatelyAsync()
    {
        // Arrange — one open event, two voters, one client address because the test host has only one.
        (HttpClient owner, Guid eventId, Guid suggestionId) =
            await OpenEventAsync("vote-limit-owner@example.com");
        using HttpClient first = owner;
        (HttpClient second, _) = await TestBearer.SignInAsync(_factory, _mail, "vote-limit-second@example.com");

        // Act — the first account votes past its allowance. A vote is idempotent, so the repeats are the
        // same fact and every one of them still costs a permit.
        HttpStatusCode last = HttpStatusCode.Unused;

        for (int attempt = 0; attempt <= VotePermits; attempt++)
        {
            using HttpResponseMessage response = await EventQueueRequests.VoteAsync(first, eventId, suggestionId);
            last = response.StatusCode;
        }

        using HttpResponseMessage other = await EventQueueRequests.VoteAsync(second, eventId, suggestionId);
        second.Dispose();

        // Assert — the first account is refused, and the second one is not.
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    private async Task<(HttpClient Owner, Guid EventId, Guid SuggestionId)> OpenEventAsync(string email)
    {
        (HttpClient owner, _) = await TestBearer.SignInAsync(_factory, _mail, email);

        Guid eventId = await EventRequests.CreateAsync(
            owner, "Open event", Visibility.Public, LicenceMode.Everyone);
        Guid suggestionId = await EventQueueRequests.SuggestAsync(owner, eventId);

        return (owner, eventId, suggestionId);
    }
}
