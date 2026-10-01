using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The suggestion, vote and code requests the event-queue suites share — SPEC-018.
/// </summary>
/// <remarks>
/// Its own file: inlining them would put each suite past the 300-line limit the root .editorconfig enforces.
/// </remarks>
internal static class EventQueueRequests
{
    /// <summary>A well-formed YouTube video id, used where the id is not what is under test.</summary>
    internal const string SomeVideoId = "dQw4w9WgXcQ";

    /// <summary>Suggests a track and asserts the `201`.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="youtubeVideoId">The video.</param>
    /// <returns>The new suggestion's id.</returns>
    internal static async Task<Guid> SuggestAsync(
        HttpClient client,
        Guid eventId,
        string youtubeVideoId = SomeVideoId)
    {
        using HttpResponseMessage response = await PostSuggestionAsync(client, eventId, youtubeVideoId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        SuggestedBody? body = await response.Content.ReadFromJsonAsync<SuggestedBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body.SuggestionId;
    }

    /// <summary>Posts a suggestion without asserting, for the cases that must be refused.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="youtubeVideoId">The video.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> PostSuggestionAsync(
        HttpClient client,
        Guid eventId,
        string youtubeVideoId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PostAsJsonAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks",
            new { youtubeVideoId },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Votes, with or without a presence proof.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="suggestionId">The suggestion.</param>
    /// <param name="presence">The proof, or null when the licence requires none.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> VoteAsync(
        HttpClient client,
        Guid eventId,
        Guid suggestionId,
        object? presence = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PutAsJsonAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}/vote",
            new { presence },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Withdraws the caller's vote.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="suggestionId">The suggestion.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> WithdrawVoteAsync(
        HttpClient client,
        Guid eventId,
        Guid suggestionId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.DeleteAsync(
            $"{EventRequests.EventsPath}/{eventId}/tracks/{suggestionId}/vote",
            TestContext.Current.CancellationToken);
    }

    /// <summary>The queue, in order.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <returns>The rows.</returns>
    internal static async Task<IReadOnlyList<QueueBody>> QueueAsync(HttpClient client, Guid eventId)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync(
            $"{EventRequests.EventsPath}/{eventId}/queue",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<QueueBody>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>Issues a venue code and asserts the `201`.</summary>
    /// <param name="client">The owner's client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="ttlSeconds">How long it should live.</param>
    /// <returns>The plaintext code.</returns>
    internal static async Task<string> IssueCodeAsync(
        HttpClient client,
        Guid eventId,
        int ttlSeconds = 900)
    {
        using HttpResponseMessage response = await PostCodeAsync(client, eventId, ttlSeconds);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CodeBody? body = await response.Content.ReadFromJsonAsync<CodeBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body.Code;
    }

    /// <summary>Posts a code request without asserting.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="eventId">The event.</param>
    /// <param name="ttlSeconds">How long it should live.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> PostCodeAsync(
        HttpClient client,
        Guid eventId,
        int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PostAsJsonAsync(
            $"{EventRequests.EventsPath}/{eventId}/event-codes",
            new { ttlSeconds },
            TestContext.Current.CancellationToken);
    }

    /// <summary>What `POST .../tracks` answers with.</summary>
    /// <param name="SuggestionId">The new suggestion.</param>
    internal sealed record SuggestedBody(Guid SuggestionId);

    /// <summary>What `POST .../event-codes` answers with.</summary>
    /// <param name="Code">The plaintext code, returned once.</param>
    internal sealed record CodeBody(string Code);

    /// <summary>One queue row, as a client reads it.</summary>
    /// <param name="SuggestionId">The suggestion.</param>
    /// <param name="YoutubeVideoId">The video.</param>
    /// <param name="Votes">How many accounts voted for it.</param>
    /// <param name="CallerVoted">Whether the caller did.</param>
    internal sealed record QueueBody(
        Guid SuggestionId,
        string YoutubeVideoId,
        int Votes,
        bool CallerVoted);
}
