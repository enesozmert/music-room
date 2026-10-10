using Microsoft.Extensions.Configuration;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Infrastructure.Identity;

namespace MusicRoom.WebAPI;

/// <summary>
/// Reads the provider rows out of configuration — ADR-0008's amendment, SPEC-041.
/// </summary>
/// <remarks>
/// *"A provider is a row of configuration — its discovery URL, client id, and how its ID token arrives"*. This
/// is where a row comes from: two environment variables per provider, and the constants that are properties of
/// the protocol rather than of this deployment.
/// <para>
/// **The discovery URL and the issuer are configuration too**, not constants: ADR-0008's amendment says a
/// provider *is* a row, and a row with two of its four fields compiled into the binary is a row only by
/// half. It also keeps absolute URLs out of the code, which `S1075` asks for and which means a second
/// provider needs no edit here at all — only four more variables.
/// </para>
/// <para>
/// **A provider with no credentials is absent, not broken.** A host that has not been given
/// `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` boots, signs people in with a password, and answers the
/// social route with "no provider is configured under that name". Refusing to start would make the
/// credentials a requirement for running the project at all, which S1's surface does not need.
/// </para>
/// <para>
/// Its own file because `Program.cs` is at S104's 300-line cap — issue O24, and the fourth unit in a row to
/// answer that wall with a file rather than a raised severity.
/// </para>
/// </remarks>
internal static class SocialProviderConfiguration
{
    /// <summary>The row's name, which is what a request body spells.</summary>
    internal const string GoogleName = "google";

    /// <summary>Every provider this host is configured for.</summary>
    /// <param name="configuration">The layered configuration the composition root holds.</param>
    /// <returns>The rows, which may be empty.</returns>
    /// <remarks>
    /// Takes the configuration rather than an <see cref="ISecretProvider" /> so the composition root calls it
    /// in one line: `Program.cs` is at S104's cap and two lines do not fit — issue O24.
    /// </remarks>
    internal static IReadOnlyList<SocialProviderOptions> Read(IConfiguration configuration)
    {
        ISecretProvider secrets = EnvironmentConfiguration.For(configuration);

        string? clientId = secrets.Read("GOOGLE_CLIENT_ID");
        string? clientSecret = secrets.Read("GOOGLE_CLIENT_SECRET");
        string? discoveryUrl = secrets.Read("GOOGLE_DISCOVERY_URL");
        string? issuer = secrets.Read("GOOGLE_ISSUER");

        if (clientId is null || clientSecret is null || discoveryUrl is null || issuer is null)
        {
            return [];
        }

        return
        [
            new SocialProviderOptions(
                GoogleName,
                new Uri(discoveryUrl),
                issuer,
                clientId,
                clientSecret),
        ];
    }
}
