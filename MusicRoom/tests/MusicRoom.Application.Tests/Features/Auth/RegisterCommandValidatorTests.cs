using System.Text;
using FluentValidation.Results;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Features.Auth.Commands.Register;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. Proves that the ADR-0007 password bounds are enforced
/// before a handler ever sees the command.
/// </summary>
public sealed class RegisterCommandValidatorTests
{
    internal const string ValidEmail = "player@example.com";
    internal const string ValidDisplayName = "Player One";

    private readonly RegisterCommandValidator _validator = new(new StubBreachedPasswordSet(BreachedPassword));

    /// <summary>One entry stands in for the bundled set — SPEC-035.</summary>
    /// <remarks>
    /// A stub rather than the real 46,146 entries: this suite proves that the rule consults the set, and
    /// `BundledBreachedPasswordSetTests` proves the set is the one that was measured. Asserting both in one
    /// place would make a validator test fail when a data file changed.
    /// </remarks>
    internal const string BreachedPassword = "iloveyouforever";

    /// <summary>A password under the ADR-0007 lower bound is refused.</summary>
    [Fact]
    public void Validate_PasswordShorterThan12Characters_Fails()
    {
        // Arrange — 11 characters, one below the bound.
        RegisterCommand command = new(ValidEmail, "shortpass11", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Password));
    }

    /// <summary>A password over the ADR-0007 upper bound is refused.</summary>
    [Fact]
    public void Validate_PasswordLongerThan64Characters_Fails()
    {
        // Arrange — 65 characters.
        RegisterCommand command = new(ValidEmail, new string('a', 65), ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Password));
    }

    /// <summary>
    /// The BCrypt truncation footgun: a password that passes the character bounds and still exceeds
    /// the 72-byte input window is refused rather than silently cut.
    /// </summary>
    [Fact]
    public void Validate_PasswordOver72BytesOfUtf8_Fails()
    {
        // Arrange — 20 four-byte characters: 40 UTF-16 chars, under the 64-character limit, and
        // 80 UTF-8 bytes, over BCrypt's 72.
        string multiBytePassword = string.Concat(Enumerable.Repeat("\U0001F3B5", 20));
        Assert.InRange(multiBytePassword.Length, 12, 64);
        Assert.True(Encoding.UTF8.GetByteCount(multiBytePassword) > 72);

        RegisterCommand command = new(ValidEmail, multiBytePassword, ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Password));
    }

    /// <summary>A malformed address is refused — subject V.1 input validation.</summary>
    [Fact]
    public void Validate_MalformedEmail_Fails()
    {
        // Arrange
        RegisterCommand command = new("not-an-address", "correct horse battery", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(RegisterCommand.Email));
    }

    /// <summary>A password in the breached set is refused, keyed on this command's own property.</summary>
    /// <remarks>ASVS V6.2.4 and V6.2.12 — ADR-0042.</remarks>
    [Fact]
    public void Validate_APasswordInTheBreachedSet_Fails()
    {
        // Arrange
        RegisterCommand command = new(ValidEmail, BreachedPassword, ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(RegisterCommand.Password)
                && failure.ErrorMessage == PasswordRules.BreachedMessage);
    }

    /// <summary>
    /// The same entry written as a passphrase is refused too: the set holds no spaces, callers type them.
    /// </summary>
    /// <remarks>
    /// The measurement behind this test is in SPEC-035: **no entry** in the bundled set contains a space, while
    /// 14,700 of them are a run of letters that a person would type as words. Comparing only the submitted form
    /// would leave every one of those unreachable.
    /// </remarks>
    [Fact]
    public void Validate_ABreachedPassphraseWithSeparators_Fails()
    {
        // Arrange
        RegisterCommand command = new(ValidEmail, "i love you forever", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.ErrorMessage == PasswordRules.BreachedMessage);
    }

    /// <summary>
    /// A password containing the caller's own words is refused — V6.1.2, V6.2.11.
    /// </summary>
    /// <remarks>
    /// This is the one path whose command carries the context words, so the screen is a rule rather than a
    /// handler check. `musicroom2026` is the case ADR-0042's register row named.
    /// </remarks>
    [Theory]
    [InlineData("musicroom2026 ok")]
    [InlineData("player one and more")]
    [InlineData("playerplayer at home")]
    public void Validate_APasswordCarryingAContextWord_Fails(string password)
    {
        // Arrange
        RegisterCommand command = new(ValidEmail, password, ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(RegisterCommand.Password)
                && failure.ErrorMessage == PasswordRules.ContextMessage);
    }

    /// <summary>A payload inside every bound passes, so the tests above fail for their own reason.</summary>
    [Fact]
    public void Validate_WellFormedCommand_Passes()
    {
        // Arrange
        RegisterCommand command = new(ValidEmail, "correct horse battery", ValidDisplayName);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }
}

/// <summary>
/// A breached set of exactly the entries a test names — SPEC-035.
/// </summary>
/// <remarks>
/// Declared beside this suite rather than in a fixture file: the Files table SPEC-035 was approved with creates
/// no helper, and `ResetPasswordCommandValidatorTests` shares this one through the assembly it is already in.
/// </remarks>
internal sealed class StubBreachedPasswordSet : IBreachedPasswordSet
{
    private readonly HashSet<string> _entries;

    /// <summary>Takes the entries this set will answer for.</summary>
    /// <param name="entries">The passwords to treat as breached.</param>
    internal StubBreachedPasswordSet(params string[] entries) =>
        _entries = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool Contains(string password) => _entries.Contains(password);
}
