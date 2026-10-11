using System.Net;
using Microsoft.Extensions.Configuration;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Infrastructure.Configuration;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Configuration;

/// <summary>
/// Where a secret comes from when the vault is in play — ADR-0029, SPEC-047.
/// </summary>
/// <remarks>
/// Five things nothing else checks: the vault outranks the file, the file is still the floor underneath it, a
/// vault that is not there stops the host instead of quietly falling back, a refused identity says so without
/// printing anything, and one provider costs one login however many times it is read.
/// </remarks>
public sealed class InfisicalSecretProviderTests
{
    private const string _loginAnswer = """{"accessToken":"a-short-lived-vault-token"}""";

    /// <summary>A name the vault holds comes from the vault, not from the environment.</summary>
    [Fact]
    public async Task TheVault_OutranksTheEnvironmentAsync()
    {
        ISecretProvider provider = await VaultBackedAsync(
            secrets: """{"secrets":[{"secretKey":"YOUTUBE_API_KEY","secretValue":"from-the-vault"}]}""",
            environment: new Dictionary<string, string?> { ["YOUTUBE_API_KEY"] = "from-the-file" });

        Assert.Equal("from-the-vault", provider.Read("YOUTUBE_API_KEY"));
    }

    /// <summary>A name only the environment holds still answers.</summary>
    /// <remarks>
    /// Not a hedge — a requirement. `INFISICAL_CLIENT_SECRET` is what this provider logs in with, so it
    /// cannot live in the vault it opens; ADR-0029 wrote that down before any of this was built.
    /// </remarks>
    [Fact]
    public async Task TheEnvironment_IsStillTheFloorAsync()
    {
        ISecretProvider provider = await VaultBackedAsync(
            secrets: """{"secrets":[]}""",
            environment: new Dictionary<string, string?> { ["DATABASE_URL"] = "Host=localhost" });

        Assert.Equal("Host=localhost", provider.Read("DATABASE_URL"));
    }

    /// <summary>A secret stored blank configures nothing, so the environment answers instead.</summary>
    /// <remarks>
    /// The rule `EnvSecretProvider` already applies, extended to the vault for the same reason: otherwise
    /// adding an empty secret to a project would silently unset a working setting, which is a configuration
    /// change nobody made.
    /// </remarks>
    [Fact]
    public async Task ABlankSecret_ReadsAsAbsentAsync()
    {
        ISecretProvider provider = await VaultBackedAsync(
            secrets: """{"secrets":[{"secretKey":"MAIL_PROVIDER","secretValue":""}]}""",
            environment: new Dictionary<string, string?> { ["MAIL_PROVIDER"] = "logging" });

        Assert.Equal("logging", provider.Read("MAIL_PROVIDER"));
    }

    /// <summary>A name neither holds is a refusal that names the project to add it to.</summary>
    [Fact]
    public async Task AMissingName_RefusesWithWhereToPutItAsync()
    {
        ISecretProvider provider = await VaultBackedAsync("""{"secrets":[]}""", []);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => provider.Require("MAIL_API_KEY"));

        Assert.Contains("MAIL_API_KEY", refused.Message, StringComparison.Ordinal);
        Assert.Contains("INFISICAL_PROJECT_ID", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Numbers and switches parse out of the vault exactly as they do out of `.env`.</summary>
    [Fact]
    public async Task NumbersAndSwitches_ParseFromTheVaultAsync()
    {
        ISecretProvider provider = await VaultBackedAsync(
            secrets: """
                {"secrets":[{"secretKey":"BCRYPT_WORK_FACTOR","secretValue":"13"},
                            {"secretKey":"OTEL_EXPORTER_ENABLED","secretValue":"true"}]}
                """,
            environment: []);

        Assert.Equal(13, provider.ReadInteger("BCRYPT_WORK_FACTOR", 12));
        Assert.True(provider.ReadBoolean("OTEL_EXPORTER_ENABLED", false));

        // And an unparseable value stops start-up rather than being treated as absent — SPEC-030's rule.
        ISecretProvider broken = await VaultBackedAsync(
            """{"secrets":[{"secretKey":"BCRYPT_WORK_FACTOR","secretValue":"abc"}]}""",
            []);

        Assert.Throws<InvalidOperationException>(() => broken.ReadInteger("BCRYPT_WORK_FACTOR", 12));
    }

    /// <summary>One login and one fetch, however many reads follow.</summary>
    [Fact]
    public async Task OneProvider_CostsOneLoginAsync()
    {
        VaultTransport transport = new(
            _loginAnswer,
            """{"secrets":[{"secretKey":"YOUTUBE_API_KEY","secretValue":"from-the-vault"}]}""");

        using HttpClient client = new(transport) { BaseAddress = new Uri("http://vault.test") };

        IReadOnlyDictionary<string, string> secrets = await new InfisicalVaultClient(client, Options())
            .FetchAsync(TestContext.Current.CancellationToken);

        InfisicalSecretProvider provider = new(secrets, Environment([]));

        for (int read = 0; read < 5; read++)
        {
            Assert.Equal("from-the-vault", provider.Read("YOUTUBE_API_KEY"));
        }

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(1, provider.Count);

        // The fetch carried the token the login answered with, which is the only thing that makes it a
        // fetch and not an unauthenticated read somebody forgot to sign.
        Assert.Equal("a-short-lived-vault-token", transport.PresentedToken);
    }

    /// <summary>
    /// A vault that is not running stops the host. It does **not** fall back to the file.
    /// </summary>
    /// <remarks>
    /// The security property of this unit, asserted directly: a fallback would silently return the system to
    /// reading API keys out of a plain file, and nothing in a log would say the posture had changed.
    /// </remarks>
    [Fact]
    public async Task AnUnreachableVault_StopsTheHostAsync()
    {
        using HttpClient client = new(new UnreachableVaultTransport())
        {
            BaseAddress = new Uri("http://vault.test"),
        };

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InfisicalVaultClient(client, Options())
                .FetchAsync(TestContext.Current.CancellationToken));

        Assert.Contains("SECRET_PROVIDER", refused.Message, StringComparison.Ordinal);
        Assert.Contains("INFISICAL_SITE_URL", refused.Message, StringComparison.Ordinal);
        Assert.Contains("does not fall back", Fallbackless(refused.Message), StringComparison.Ordinal);
    }

    /// <summary>A refused identity says which variables to check, and prints neither of them.</summary>
    [Fact]
    public async Task RejectedCredentials_SayWhatToCheckAndNothingElseAsync()
    {
        VaultTransport transport = new(
            """{"message":"unauthorized"}""",
            """{"secrets":[]}""",
            loginStatus: HttpStatusCode.Unauthorized);

        using HttpClient client = new(transport) { BaseAddress = new Uri("http://vault.test") };

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InfisicalVaultClient(client, Options())
                .FetchAsync(TestContext.Current.CancellationToken));

        Assert.Contains("INFISICAL_CLIENT_ID", refused.Message, StringComparison.Ordinal);
        Assert.Contains("401", refused.Message, StringComparison.Ordinal);

        // The vault's own sentence is carried, because "that identity does not exist here" and "that
        // secret is wrong" are the two cases an operator cannot otherwise tell apart — and the first
        // live run needed exactly this to diagnose a 404 (SPEC-047).
        Assert.Contains("unauthorized", refused.Message, StringComparison.Ordinal);

        // The credential itself never appears. `secrets-management` forbids printing a secret, and an
        // exception message is a log line, a stack trace and a screenshot.
        Assert.DoesNotContain("the-client-secret", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A login that answers no token is refused rather than used.</summary>
    [Fact]
    public async Task ALoginWithNoToken_IsRefusedAsync()
    {
        VaultTransport transport = new("""{"accessToken":""}""", """{"secrets":[]}""");

        using HttpClient client = new(transport) { BaseAddress = new Uri("http://vault.test") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InfisicalVaultClient(client, Options())
                .FetchAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A vault that refuses the project answers with the project variable to check.</summary>
    [Fact]
    public async Task ARefusedProject_NamesTheProjectVariableAsync()
    {
        VaultTransport transport = new(
            _loginAnswer,
            """{"message":"forbidden"}""",
            secretsStatus: HttpStatusCode.Forbidden);

        using HttpClient client = new(transport) { BaseAddress = new Uri("http://vault.test") };

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InfisicalVaultClient(client, Options())
                .FetchAsync(TestContext.Current.CancellationToken));

        Assert.Contains("INFISICAL_PROJECT_ID", refused.Message, StringComparison.Ordinal);
        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<ISecretProvider> VaultBackedAsync(
        string secrets,
        Dictionary<string, string?> environment)
    {
        VaultTransport transport = new(_loginAnswer, secrets);

        using HttpClient client = new(transport) { BaseAddress = new Uri("http://vault.test") };

        IReadOnlyDictionary<string, string> fetched = await new InfisicalVaultClient(client, Options())
            .FetchAsync(TestContext.Current.CancellationToken);

        return new InfisicalSecretProvider(fetched, Environment(environment));
    }

    private static EnvSecretProvider Environment(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    private static InfisicalOptions Options() =>
        new(
            new Uri("http://vault.test"),
            "a-machine-identity",
            "the-client-secret",
            "a-project",
            "dev");

    /// <summary>
    /// The sentence the refusal makes, with the phrasing this test depends on normalised.
    /// </summary>
    /// <remarks>
    /// The message says "Nothing falls back to .env on purpose"; this asserts the claim rather than the
    /// wording, so rewording the message does not fail a test about the behaviour.
    /// </remarks>
    private static string Fallbackless(string message) =>
        message.Replace("Nothing falls back", "does not fall back", StringComparison.Ordinal);
}
