using System.Text.Json.Serialization;

namespace MusicRoom.Infrastructure.Catalog;

/// <summary>
/// The shapes YouTube Data API v3 answers with, cut down to what is read.
/// </summary>
/// <remarks>
/// Deliberately partial: only the fields a <c>TrackMetadata</c> needs are declared, so a provider change
/// elsewhere in the payload cannot break deserialisation. ADR-0023 forbids storing the raw payload, and these
/// types never leave this namespace.
/// </remarks>
internal sealed record SearchListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<SearchItem>? Items);

/// <summary>One search result, which carries an id and nothing worth keeping.</summary>
/// <param name="Id">The id envelope.</param>
internal sealed record SearchItem([property: JsonPropertyName("id")] SearchItemId? Id);

/// <summary>The id envelope of a search result.</summary>
/// <param name="VideoId">The video id, absent when the result is a channel or a playlist.</param>
internal sealed record SearchItemId([property: JsonPropertyName("videoId")] string? VideoId);

/// <summary>The answer to a `videos.list` by id.</summary>
/// <param name="Items">One entry per id the provider knows.</param>
internal sealed record VideoListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<VideoItem>? Items);

/// <summary>One video.</summary>
/// <param name="Id">The video id.</param>
/// <param name="Snippet">Title, channel and thumbnails.</param>
/// <param name="ContentDetails">Where the ISO 8601 duration lives.</param>
internal sealed record VideoItem(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("snippet")] VideoSnippet? Snippet,
    [property: JsonPropertyName("contentDetails")] VideoContentDetails? ContentDetails);

/// <summary>The descriptive part of a video.</summary>
/// <param name="Title">The title.</param>
/// <param name="ChannelTitle">The publishing channel.</param>
/// <param name="Thumbnails">The thumbnail sizes the provider offers.</param>
internal sealed record VideoSnippet(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("channelTitle")] string? ChannelTitle,
    [property: JsonPropertyName("thumbnails")] VideoThumbnails? Thumbnails);

/// <summary>The two thumbnail sizes worth asking for.</summary>
/// <param name="Medium">320 by 180, the list thumbnail.</param>
/// <param name="Default">120 by 90, the fallback when there is no medium.</param>
internal sealed record VideoThumbnails(
    [property: JsonPropertyName("medium")] VideoThumbnail? Medium,
    [property: JsonPropertyName("default")] VideoThumbnail? Default);

/// <summary>One thumbnail.</summary>
/// <param name="Url">Where the provider serves it. Never proxied or re-hosted.</param>
internal sealed record VideoThumbnail([property: JsonPropertyName("url")] string? Url);

/// <summary>The part that carries the duration.</summary>
/// <param name="Duration">An ISO 8601 duration, such as `PT4M33S`. Absent for a live stream.</param>
internal sealed record VideoContentDetails(
    [property: JsonPropertyName("duration")] string? Duration);
