using MusicRoom.Application.Abstractions.Security;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// The six-character code — SPEC-052 decisions 2 and 3.
/// </summary>
/// <remarks>
/// What is asserted here is the shape, not the secrecy: a code is safe because of the attempt budget on
/// the row it belongs to, and that lives in the repository. What this file protects is the part a person
/// touches — an alphabet with no character they can misread, and a normaliser that accepts what a mail
/// client puts on the clipboard.
/// </remarks>
public sealed class VerificationCodeTests
{
    /// <summary>Six characters, every one of them from the alphabet.</summary>
    [Fact]
    public void Create_IsSixCharactersOfTheAlphabet()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string code = VerificationCode.Create();

            Assert.Equal(VerificationCode.Length, code.Length);
            Assert.True(VerificationCode.IsWellFormed(code), code);
        }
    }

    /// <summary>
    /// The alphabet contains nothing a person transcribes wrongly — the reason it is 32 and not 36.
    /// </summary>
    [Theory]
    [InlineData('I')]
    [InlineData('O')]
    [InlineData('0')]
    [InlineData('1')]
    public void TheAlphabet_HasNoConfusableCharacters(char confusable) =>
        Assert.DoesNotContain(confusable, VerificationCode.Alphabet);

    /// <summary>Thirty-two symbols exactly, which is five bits a character and no modulo bias.</summary>
    [Fact]
    public void TheAlphabet_IsThirtyTwoDistinctSymbols()
    {
        Assert.Equal(32, VerificationCode.Alphabet.Length);
        Assert.Equal(32, VerificationCode.Alphabet.Distinct().Count());
    }

    /// <summary>What a mail client puts on the clipboard still verifies.</summary>
    /// <remarks>
    /// Lower case, a trailing space, a dash in the middle: three things a person does when copying six
    /// characters, and refusing any of them would be refusing a correct code for a reason nobody can see.
    /// </remarks>
    [Theory]
    [InlineData("abc234", "ABC234")]
    [InlineData(" ABC234 ", "ABC234")]
    [InlineData("ABC-234", "ABC234")]
    [InlineData("a b c 2 3 4", "ABC234")]
    public void Normalise_AcceptsWhatAPersonPastes(string submitted, string expected) =>
        Assert.Equal(expected, VerificationCode.Normalise(submitted));

    /// <summary>Anything that is not six alphabet characters is refused before a query is made.</summary>
    /// <remarks>
    /// The budget exists to be spent by guesses at a code. A caller sending a sentence should cost a
    /// comparison, not an attempt somebody else's code pays for.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("ABC23")]
    [InlineData("ABC2345")]
    public void IsWellFormed_RefusesEverythingElse(string submitted) =>
        Assert.False(VerificationCode.IsWellFormed(VerificationCode.Normalise(submitted)));
}
