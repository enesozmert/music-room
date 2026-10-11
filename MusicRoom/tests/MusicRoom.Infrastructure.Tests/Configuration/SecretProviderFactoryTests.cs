using Microsoft.Extensions.Configuration;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Infrastructure.Configuration;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Configuration;

/// <summary>
/// Which provider a host gets, and what a typo gets — SPEC-047 decision 1.
/// </summary>
/// <remarks>
/// The default path is the one every test host and every CI run takes, so it is asserted rather than assumed.
/// The typo is the interesting case: a misspelled selector must not silently become the default, because
/// that is a vault nobody noticed was not being used.
/// </remarks>
public sealed class SecretProviderFactoryTests
{
    /// <summary>No selector means the process environment — the default ADR-0029 named.</summary>
    [Fact]
    public void NoSelector_IsTheEnvironment() =>
        Assert.IsType<EnvSecretProvider>(SecretProviderFactory.For(Configuration([])));

    /// <summary>So does naming it, in either case.</summary>
    [Theory]
    [InlineData("env")]
    [InlineData("ENV")]
    [InlineData("Env")]
    public void TheEnvironmentSelector_IsCaseInsensitive(string selected) =>
        Assert.IsType<EnvSecretProvider>(SecretProviderFactory.For(Configuration(
            new Dictionary<string, string?> { [SecretProviderFactory.SelectorVariable] = selected })));

    /// <summary>A value that is not a provider stops start-up and names both that are.</summary>
    [Fact]
    public void AnUnknownSelector_RefusesAndNamesTheTwoThatExist()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => SecretProviderFactory.For(Configuration(
                new Dictionary<string, string?>
                {
                    [SecretProviderFactory.SelectorVariable] = "infisicl",
                })));

        Assert.Contains(SecretProviderFactory.SelectorVariable, refused.Message, StringComparison.Ordinal);
        Assert.Contains(SecretProviderFactory.EnvironmentProvider, refused.Message, StringComparison.Ordinal);
        Assert.Contains(SecretProviderFactory.VaultProvider, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The vault selector with no bootstrap credentials refuses, naming the first one missing.</summary>
    /// <remarks>
    /// Reached without a network: the five variables are read before anything is sent, so this asserts the
    /// guard rather than the vault. The vault's own refusals are `InfisicalSecretProviderTests`.
    /// </remarks>
    [Fact]
    public void TheVaultSelector_WithNoCredentials_Refuses()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => SecretProviderFactory.For(Configuration(
                new Dictionary<string, string?>
                {
                    [SecretProviderFactory.SelectorVariable] = SecretProviderFactory.VaultProvider,
                })));

        Assert.Contains("INFISICAL_SITE_URL", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One configuration gets one provider, which is what makes the root's several reads cost one login.
    /// </summary>
    [Fact]
    public void OneConfiguration_GetsOneProvider()
    {
        IConfiguration configuration = Configuration([]);

        ISecretProvider first = SecretProviderFactory.For(configuration);
        ISecretProvider second = SecretProviderFactory.For(configuration);

        Assert.Same(first, second);

        // And a different configuration is a different host: a test's provider must not be handed to the
        // next test, which is why the table is keyed on the configuration rather than held in a static.
        Assert.NotSame(first, SecretProviderFactory.For(Configuration([])));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
