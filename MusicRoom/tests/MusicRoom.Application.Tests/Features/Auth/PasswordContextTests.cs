using MusicRoom.Application.Features.Auth;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Proves the context screen — ADR-0042's context word list, V6.1.2 and V6.2.11.
/// </summary>
public sealed class PasswordContextTests
{
    // Constants are internal rather than private because the .editorconfig naming rule requires an
    // underscore prefix on private fields, and a const is a field to that rule.
    internal const string Email = "ada.lovelace@example.com";
    internal const string DisplayName = "Ada Lovelace";

    /// <summary>Containment, not equality: the case ADR-0042's register row named.</summary>
    [Theory]
    [InlineData("musicroom2026")]
    [InlineData("Music-Room-2026")]
    [InlineData("my musicroomapp login")]
    [InlineData("adalovelace rules ok")]
    [InlineData("ada.lovelace is me now")]
    [InlineData("xxxxxxxxadalovelace")]
    public void ContainsAContextWord_RefusesAPasswordCarryingAContextWord(string password)
    {
        Assert.True(PasswordContext.ContainsAContextWord(password, Email, DisplayName));
    }

    /// <summary>What the screen must not refuse, including every password the suites use.</summary>
    /// <remarks>
    /// The four literals are the ones the test suites choose on a register, reset or change route. SPEC-035
    /// measured them against this screen before writing a line of it, and this is where that measurement lives
    /// from now on.
    /// </remarks>
    [Theory]
    [InlineData("correct horse battery")]
    [InlineData("a brand new passphrase")]
    [InlineData("a-replacement-password")]
    [InlineData("the attackers passphrase")]
    [InlineData("entirely unrelated words")]
    public void ContainsAContextWord_AcceptsAPasswordWithNoContextWord(string password)
    {
        Assert.False(PasswordContext.ContainsAContextWord(password, Email, DisplayName));
    }

    /// <summary>
    /// A context word of three characters or fewer refuses nothing — ADR-0042's four-character floor.
    /// </summary>
    /// <remarks>
    /// Without the floor, a display name of `Al` refuses `balance sheet entry` and a local part of `one` refuses
    /// `moneylender`. The floor is why a short name is not a password policy.
    /// </remarks>
    [Theory]
    [InlineData("al@example.com", "Al", "balance sheet entry")]
    [InlineData("one@example.com", "Bo", "moneylender heritage")]
    public void ContainsAContextWord_IgnoresAWordShorterThanFourCharacters(
        string email,
        string displayName,
        string password)
    {
        Assert.False(PasswordContext.ContainsAContextWord(password, email, displayName));
    }

    /// <summary>
    /// The project's own words are screened even when no account is reachable — the reset path's fallback.
    /// </summary>
    /// <remarks>
    /// A token that matches no account still chooses a password, and `musicroom2026` is refused on that path
    /// too. The write path, not this screen, decides what an unknown token answers.
    /// </remarks>
    [Fact]
    public void ContainsAContextWord_WithNoAccount_StillScreensTheProjectWords()
    {
        Assert.True(PasswordContext.ContainsAContextWord("musicroom2026", email: null, displayName: null));
        Assert.False(PasswordContext.ContainsAContextWord("correct horse battery", email: null, displayName: null));
    }

    /// <summary>Only the local part is screened: the domain belongs to everyone on it.</summary>
    [Fact]
    public void ContainsAContextWord_DoesNotScreenTheAddressDomain()
    {
        Assert.False(PasswordContext.ContainsAContextWord("example.com is a domain", Email, DisplayName));
    }

    /// <summary>An empty password is not the screen's refusal to make — the length rule answers it.</summary>
    [Fact]
    public void ContainsAContextWord_WithAnEmptyPassword_IsFalse()
    {
        Assert.False(PasswordContext.ContainsAContextWord(string.Empty, Email, DisplayName));
    }
}
