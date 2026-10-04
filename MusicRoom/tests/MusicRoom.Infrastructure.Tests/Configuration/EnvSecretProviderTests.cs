using Microsoft.Extensions.Configuration;
using MusicRoom.Application.Abstractions.Configuration;
using MusicRoom.Infrastructure.Configuration;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Configuration;

/// <summary>
/// The default secret provider — ADR-0029, SPEC-030.
/// </summary>
/// <remarks>
/// Four behaviours carry the whole abstraction: a present value arrives, an absent optional is null, an absent
/// required stops start-up, and a blank value counts as absent. The fourth is the one that used to be decided at
/// thirteen call sites.
/// </remarks>
public sealed class EnvSecretProviderTests
{
    internal const string Name = "SOME_SETTING";

    /// <summary>A configured value is read.</summary>
    [Fact]
    public void Read_WithAConfiguredValue_ReturnsIt() =>
        Assert.Equal("a-value", Provider((Name, "a-value")).Read(Name));

    /// <summary>An absent optional is null, not an exception.</summary>
    [Fact]
    public void Read_WithNothingConfigured_ReturnsNull() =>
        Assert.Null(Provider().Read(Name));

    /// <summary>
    /// Blank is absent — a host that writes `MAIL_PROVIDER=` has configured nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_WithABlankValue_ReturnsNull(string configured) =>
        Assert.Null(Provider((Name, configured)).Read(Name));

    /// <summary>A required value that is present is returned.</summary>
    [Fact]
    public void Require_WithAConfiguredValue_ReturnsIt() =>
        Assert.Equal("a-value", Provider((Name, "a-value")).Require(Name));

    /// <summary>
    /// A required value that is missing stops start-up, and the message names the variable.
    /// </summary>
    [Fact]
    public void Require_WithNothingConfigured_ThrowsNamingTheVariable()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Provider().Require(Name));

        Assert.Contains(Name, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failure says what is missing and never what was found — `secrets-management`.
    /// </summary>
    /// <remarks>
    /// The case that matters: a blank value is treated as absent, so the throw path can be reached while a
    /// value *is* configured. Nothing about it may reach the message.
    /// </remarks>
    [Fact]
    public void Require_WithABlankValue_ThrowsWithoutEchoingIt()
    {
        const string blankButPresent = "  ";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Provider((Name, blankButPresent)).Require(Name));

        Assert.Contains(Name, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A configured number is read, and an absent one falls back to the approved value.</summary>
    [Theory]
    [InlineData("42", 42)]
    [InlineData("", 7)]
    public void ReadInteger_UsesTheValueOrTheFallback(string configured, int expected) =>
        Assert.Equal(expected, Provider((Name, configured)).ReadInteger(Name, fallback: 7));

    /// <summary>
    /// A number that is not a number stops start-up, which is what it did before this abstraction existed.
    /// </summary>
    /// <remarks>
    /// Deliberately not a silent fallback: a limiter permit written as `abc` is a mistake a host should hear
    /// about at boot. The callers clamp what they get, so a *valid* but useless value — a non-positive permit —
    /// is the case that is ignored instead.
    /// </remarks>
    [Fact]
    public void ReadInteger_WithAValueThatIsNotANumber_Throws() =>
        Assert.ThrowsAny<Exception>(() => Provider((Name, "not a number")).ReadInteger(Name, fallback: 7));

    /// <summary>The same for a switch, which is how the exporter stays off unless it is turned on.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void ReadBoolean_UsesTheValueOrTheFallback(string configured, bool expected) =>
        Assert.Equal(expected, Provider((Name, configured)).ReadBoolean(Name, fallback: false));

    private static ISecretProvider Provider(params (string Key, string Value)[] settings) =>
        new EnvSecretProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(
                setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build());
}
