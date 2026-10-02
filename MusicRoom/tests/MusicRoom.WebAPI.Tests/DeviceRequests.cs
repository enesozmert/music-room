using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace MusicRoom.WebAPI.Tests;

/// <summary>
/// The device and control requests the three device suites share — SPEC-020.
/// </summary>
/// <remarks>
/// Its own file: inlining them would put each suite past the 300-line limit the root .editorconfig enforces.
/// </remarks>
internal static class DeviceRequests
{
    /// <summary>Where the device routes live.</summary>
    internal const string DevicesPath = "/api/v1/devices";

    /// <summary>Where the caller's own grants are listed.</summary>
    internal const string GrantsPath = "/api/v1/me/control-grants";

    /// <summary>Registers the calling device and asserts the `201`.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <returns>The new device's id.</returns>
    internal static async Task<Guid> RegisterAsync(HttpClient client)
    {
        using HttpResponseMessage response = await PostAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        RegisteredBody? body = await response.Content.ReadFromJsonAsync<RegisteredBody>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        return body.DeviceId;
    }

    /// <summary>Posts a registration without asserting.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        // No body: the three values come from the mandatory client headers — SPEC-020 decision 7.
        return await client.PostAsync(DevicesPath, content: null, TestContext.Current.CancellationToken);
    }

    /// <summary>The caller's own devices.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <returns>The rows.</returns>
    internal static async Task<IReadOnlyList<DeviceBody>> ListAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync(
            DevicesPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<DeviceBody>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>Reads the control state, for a caller allowed to ask.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="deviceId">The device.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> GetControlAsync(HttpClient client, Guid deviceId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.GetAsync(
            $"{DevicesPath}/{deviceId}/control",
            TestContext.Current.CancellationToken);
    }

    /// <summary>The `ETag` of the control state, which a claim sends back.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="deviceId">The device.</param>
    /// <returns>The token.</returns>
    internal static async Task<string> ETagAsync(HttpClient client, Guid deviceId)
    {
        using HttpResponseMessage response = await GetControlAsync(client, deviceId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        return response.Headers.ETag.Tag;
    }

    /// <summary>Hands control to somebody, with an `If-Match`.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="deviceId">The device.</param>
    /// <param name="granteeUserId">Who is being given control.</param>
    /// <param name="etag">The token the caller holds.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> GrantAsync(
        HttpClient client,
        Guid deviceId,
        Guid granteeUserId,
        string etag)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpRequestMessage request = new(HttpMethod.Put, $"{DevicesPath}/{deviceId}/control")
        {
            Content = JsonContent.Create(new { granteeUserId }),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Returns a device to its owner's control.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <param name="deviceId">The device.</param>
    /// <returns>The response.</returns>
    internal static async Task<HttpResponseMessage> ReleaseAsync(HttpClient client, Guid deviceId)
    {
        ArgumentNullException.ThrowIfNull(client);

        return await client.DeleteAsync(
            $"{DevicesPath}/{deviceId}/control",
            TestContext.Current.CancellationToken);
    }

    /// <summary>The grants the caller holds.</summary>
    /// <param name="client">An authenticated client.</param>
    /// <returns>The rows.</returns>
    internal static async Task<IReadOnlyList<GrantBody>> GrantsAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync(
            GrantsPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<List<GrantBody>>(
            TestContext.Current.CancellationToken) ?? [];
    }

    /// <summary>What `POST /devices` answers with.</summary>
    /// <param name="DeviceId">The new device.</param>
    internal sealed record RegisteredBody(Guid DeviceId);

    /// <summary>One device, as its owner reads it.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="Platform">Its platform.</param>
    /// <param name="Model">Its model.</param>
    /// <param name="AppVersion">The version it last registered with.</param>
    /// <param name="ControllerUserId">Who holds delegated control, or null.</param>
    internal sealed record DeviceBody(
        Guid DeviceId,
        string Platform,
        string Model,
        string AppVersion,
        Guid? ControllerUserId);

    /// <summary>One grant the caller holds.</summary>
    /// <param name="DeviceId">The device.</param>
    /// <param name="OwnerId">Who owns it.</param>
    /// <param name="OwnerDisplayName">Their display name.</param>
    internal sealed record GrantBody(Guid DeviceId, Guid OwnerId, string OwnerDisplayName);
}
