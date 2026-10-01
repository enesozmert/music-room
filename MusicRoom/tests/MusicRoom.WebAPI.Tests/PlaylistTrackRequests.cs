using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The guarded track requests of <c>docs/api/ENDPOINTS.md</c> §5, as three test classes make them.
/// </summary>
/// <remarks>
/// Its own file: the three track suites share every one of these, and inlining them would put each suite past
/// the 300-line limit the root .editorconfig enforces. Each helper reads the `ETag` when the caller gives none,
/// because the ordinary client flow is read-then-write and a test that skips the read is testing nothing.
/// </remarks>
internal static class TrackRequests
{
    /// <summary>Where the track routes live.</summary>
    internal const string PlaylistsPath = "/api/v1/playlists";

    /// <summary>Adds a track and asserts the `201`, reading the `ETag` first when none was given.</summary>
    internal static async Task<Guid> AddAsync(
        HttpClient client,
        Guid playlistId,
        string youtubeVideoId,
        string? etag = null)
    {
        using HttpResponseMessage response = await PostTrackAsync(
            client,
            playlistId,
            new { youtubeVideoId },
            etag ?? await ETagAsync(client, playlistId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        AddedBody? added = await response.Content.ReadFromJsonAsync<AddedBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(added);

        return added.PlaylistTrackId;
    }

    /// <summary>The track list, in order.</summary>
    internal static async Task<IReadOnlyList<TrackBody>> ListAsync(HttpClient client, Guid playlistId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{PlaylistsPath}/{playlistId}/tracks",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<TrackBody>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>The `ETag` of the track list, which is the playlist's own token.</summary>
    internal static async Task<string> ETagAsync(HttpClient client, Guid playlistId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"{PlaylistsPath}/{playlistId}/tracks",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        return response.Headers.ETag.Tag;
    }

    /// <summary>One guarded `POST` of a track payload.</summary>
    internal static Task<HttpResponseMessage> PostTrackAsync(
        HttpClient client,
        Guid playlistId,
        object payload,
        string etag) =>
        SendAsync(client, HttpMethod.Post, $"{PlaylistsPath}/{playlistId}/tracks", payload, etag);

    /// <summary>One guarded `PATCH` of a track's destination.</summary>
    internal static Task<HttpResponseMessage> PatchTrackAsync(
        HttpClient client,
        Guid playlistId,
        Guid trackId,
        object payload,
        string etag) =>
        SendAsync(
            client, HttpMethod.Patch, $"{PlaylistsPath}/{playlistId}/tracks/{trackId}", payload, etag);

    /// <summary>One guarded `DELETE` of a track.</summary>
    internal static Task<HttpResponseMessage> DeleteTrackAsync(
        HttpClient client,
        Guid playlistId,
        Guid trackId,
        string etag) =>
        SendAsync(
            client, HttpMethod.Delete, $"{PlaylistsPath}/{playlistId}/tracks/{trackId}", null, etag);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? payload,
        string etag)
    {
        using HttpRequestMessage request = new(method, path);

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>What `POST /playlists/{id}/tracks` answers with.</summary>
    /// <param name="PlaylistTrackId">The new track.</param>
    internal sealed record AddedBody(Guid PlaylistTrackId);

    /// <summary>One track, as a client reads it.</summary>
    /// <param name="PlaylistTrackId">The track.</param>
    /// <param name="YoutubeVideoId">The video.</param>
    /// <param name="Position">Where it sits.</param>
    internal sealed record TrackBody(Guid PlaylistTrackId, string YoutubeVideoId, long Position);
}
