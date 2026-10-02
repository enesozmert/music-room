using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MusicRoom.Domain.Common;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The playback requests the three playback suites share — SPEC-021.
/// </summary>
/// <remarks>
/// Its own file: inlining them would put each suite past the 300-line limit the root .editorconfig enforces.
/// </remarks>
internal static class PlaybackRequests
{
    /// <summary>Where the playback routes live.</summary>
    internal const string SessionsPath = "/api/v1/playback-sessions";

    /// <summary>Starts a session and asserts the `201`.</summary>
    /// <param name="client">The device owner's client.</param>
    /// <param name="deviceId">The device.</param>
    /// <param name="sourceType">Event or playlist.</param>
    /// <param name="sourceId">The source.</param>
    /// <returns>The session and what it started on.</returns>
    internal static async Task<StartedBody> StartAsync(
        HttpClient client,
        Guid deviceId,
        PlaybackSourceType sourceType,
        Guid sourceId)
    {
        using HttpResponseMessage response = await PostStartAsync(client, deviceId, sourceType, sourceId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        StartedBody? body = await response.Content.ReadFromJsonAsync<StartedBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body;
    }

    /// <summary>Posts a start without asserting.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="deviceId">The device.</param>
    /// <param name="sourceType">Event or playlist.</param>
    /// <param name="sourceId">The source.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> PostStartAsync(
        HttpClient client,
        Guid deviceId,
        PlaybackSourceType sourceType,
        Guid sourceId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.PostAsJsonAsync(
            SessionsPath,
            new { deviceId, sourceType, sourceId },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Reads a session.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> GetAsync(HttpClient client, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.GetAsync(
            $"{SessionsPath}/{sessionId}",
            TestContext.Current.CancellationToken);
    }

    /// <summary>The session, for a caller who may see its source.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The session body.</returns>
    internal static async Task<SessionBody> SessionAsync(HttpClient client, Guid sessionId)
    {
        using HttpResponseMessage response = await GetAsync(client, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SessionBody? body = await response.Content.ReadFromJsonAsync<SessionBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body;
    }

    /// <summary>The `ETag` of a session, which a command sends back.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The token.</returns>
    internal static async Task<string> ETagAsync(HttpClient client, Guid sessionId)
    {
        using HttpResponseMessage response = await GetAsync(client, sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        return response.Headers.ETag.Tag;
    }

    /// <summary>Reports that the track ended.</summary>
    /// <param name="client">The controller's client.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="expectedNowPlayingId">What the client believes is playing.</param>
    /// <param name="etag">The token the caller holds.</param>
    /// <returns>The response.</returns>
    internal static Task<HttpResponseMessage> AdvanceAsync(
        HttpClient client,
        Guid sessionId,
        Guid? expectedNowPlayingId,
        string etag) =>
        SendAsync(
            client,
            HttpMethod.Post,
            $"{SessionsPath}/{sessionId}/advances",
            new { expectedNowPlayingId },
            etag);

    /// <summary>Jumps to a named item.</summary>
    /// <param name="client">The controller's client.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="nowPlayingId">The item to play.</param>
    /// <param name="etag">The token the caller holds.</param>
    /// <returns>The response.</returns>
    internal static Task<HttpResponseMessage> JumpAsync(
        HttpClient client,
        Guid sessionId,
        Guid nowPlayingId,
        string etag) =>
        SendAsync(client, HttpMethod.Patch, $"{SessionsPath}/{sessionId}", new { nowPlayingId }, etag);

    /// <summary>Stops playback.</summary>
    /// <param name="client">The device owner's client.</param>
    /// <param name="sessionId">The session.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> StopAsync(HttpClient client, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.DeleteAsync(
            $"{SessionsPath}/{sessionId}",
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object payload,
        string etag)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpRequestMessage request = new(method, path)
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>What `POST /playback-sessions` answers with.</summary>
    /// <param name="SessionId">The new session.</param>
    /// <param name="NowPlayingId">What it started on.</param>
    internal sealed record StartedBody(Guid SessionId, Guid? NowPlayingId);

    /// <summary>One session, as a client reads it.</summary>
    /// <param name="SessionId">The session.</param>
    /// <param name="DeviceId">The device.</param>
    /// <param name="SourceId">The source.</param>
    /// <param name="NowPlayingId">What is playing now.</param>
    /// <param name="EndedAtUtc">When it stopped, or null while live.</param>
    internal sealed record SessionBody(
        Guid SessionId,
        Guid DeviceId,
        Guid SourceId,
        Guid? NowPlayingId,
        DateTime? EndedAtUtc);
}
