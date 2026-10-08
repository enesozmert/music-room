using System.Text;
using FluentValidation;
using MusicRoom.Application.Validation;
using MusicRoom.Application.Abstractions.Security;

namespace MusicRoom.Application.Features.Auth;

/// <summary>
/// The password policy, stated once — ADR-0007, SPEC-034.
/// </summary>
/// <remarks>
/// Three validators used to state it separately: register, reset and the authenticated change each declared the
/// minimum, the maximum and the BCrypt byte ceiling, six references apiece. SPEC-028 is credited with unifying
/// them and did not; this is that unification, and it exists now because SPEC-035 adds a breached-password screen
/// to this policy. A security rule added to three places is a rule that will be in two of them after the next
/// path is written.
/// <para>
/// **A rule-builder extension rather than a validator of its own.** FluentValidation reports a failure against
/// the property the caller selected, and `CredentialEndpointResults.ToErrors` groups by that name, so the `422`
/// body is keyed `Password` on register and `NewPassword` on reset and change. A shared
/// <c>AbstractValidator&lt;string&gt;</c> would have to name the property itself and would rename a key clients
/// read — a contract change hiding inside a refactor, which is why this shape is the decision and not a
/// preference.
/// </para>
/// <para>
/// No message interpolates a submitted value. A validation response that quoted the password back would put it
/// in a log, a proxy and a crash report at once.
/// </para>
/// <para>
/// **Public, where the Files-table walk said internal.** The walk was right that every *rule* caller is in this
/// assembly, and wrong about the numbers: `ResetPasswordCommandValidatorTests` reads the BCrypt byte ceiling to
/// build its own case, and that test is in `MusicRoom.Application.Tests`. The two constants it replaced were
/// `public const` on two validators, so this exposes nothing that was not already exposed — it exposes it once
/// instead of three times. The extension method stays internal: only the validators beside it apply the policy.
/// </para>
/// </remarks>
public static class PasswordRules
{
    /// <summary>
    /// BCrypt truncates its input here, silently. Two long passwords would otherwise hash identically, so the
    /// limit is rejected at validation instead.
    /// </summary>
    public const int PasswordMaximumUtf8Bytes = 72;

    /// <summary>
    /// NIST SP 800-63B asks verifiers to accept at least 64 characters; the upper bound is what keeps every
    /// accepted password inside BCrypt's input window.
    /// </summary>
    public const int MinimumLength = 12;

    /// <summary>The longest password accepted, which is also the shortest NIST asks a verifier to allow.</summary>
    public const int MaximumLength = 64;

    /// <summary>
    /// What a caller is told when their password is in the bundled breached set — SPEC-035.
    /// </summary>
    /// <remarks>
    /// It names the screen and nothing else. A message that quoted the password would put it in a log, a proxy
    /// and a crash report at once, and one that named the entry it matched would be the same thing said twice.
    /// </remarks>
    public const string BreachedMessage = "Password is on a list of known breached passwords.";

    /// <summary>
    /// What a caller is told when their password contains one of their own words, or the application's.
    /// </summary>
    /// <remarks>
    /// It does not say which word matched. Naming it would confirm a display name or an address to whoever
    /// submitted the request, which on the reset path is not necessarily the account's owner.
    /// <para>
    /// Public and here rather than on <see cref="PasswordContext"/>, because the two handler paths refuse in
    /// `WebAPI` — an endpoint builds the `422` body from this constant, so client-visible text has one home.
    /// </para>
    /// </remarks>
    public const string ContextMessage =
        "Password must not contain your name, your email address or the application's name.";

    /// <summary>
    /// Applies the policy to whichever property carries the password being chosen.
    /// </summary>
    /// <typeparam name="T">The command being validated.</typeparam>
    /// <param name="rule">The rule builder for that command's password property.</param>
    /// <param name="breachedPasswords">The bundled breached set — ADR-0042. The validators take it from the
    /// container, which makes them the first validators in this repository with a dependency.</param>
    /// <returns>The same builder, so a caller may add rules of its own — the change path's "must differ from the
    /// current one" is one.</returns>
    /// <remarks>
    /// The messages are the ones the three validators carried, copied rather than reworded: the `422` body is a
    /// response three test suites assert on, so a changed sentence is a changed contract.
    /// </remarks>
    internal static IRuleBuilderOptions<T, string> ChosenPassword<T>(
        this IRuleBuilder<T, string> rule,
        IBreachedPasswordSet breachedPasswords)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(breachedPasswords);

        return rule
            .NotEmpty().WithErrorCode(ValidationCodes.PasswordRequired)
            .MinimumLength(MinimumLength).WithErrorCode(ValidationCodes.PasswordTooShort)
            .WithMessage($"Password must be at least {MinimumLength} characters.")
            .MaximumLength(MaximumLength).WithErrorCode(ValidationCodes.PasswordTooLong)
            .WithMessage($"Password must be at most {MaximumLength} characters.")
            .Must(FitsInBCryptInput).WithErrorCode(ValidationCodes.PasswordTooManyBytes)
            .WithMessage($"Password must be at most {PasswordMaximumUtf8Bytes} bytes when encoded as UTF-8.")
            .Must(password => !IsBreached(breachedPasswords, password)).WithErrorCode(ValidationCodes.PasswordBreached)
            .WithMessage(BreachedMessage);
    }

    private static bool FitsInBCryptInput(string? password) =>
        password is not null && Encoding.UTF8.GetByteCount(password) <= PasswordMaximumUtf8Bytes;

    /// <summary>
    /// Asks the set about the password as submitted, and about its separator-stripped form.
    /// </summary>
    /// <remarks>
    /// Both forms, because **no entry in the bundled set contains a space** — every passphrase in it is written
    /// as one run of letters, while a caller types `i love you forever`. A single literal comparison would leave
    /// the 14,700 passphrase-shaped entries of the set unreachable, which is a screen that passes its own tests
    /// and refuses nothing anybody would choose (SPEC-035, measured).
    /// </remarks>
    private static bool IsBreached(IBreachedPasswordSet breachedPasswords, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (breachedPasswords.Contains(password))
        {
            return true;
        }

        string stripped = PasswordContext.Normalise(password);

        return stripped.Length != password.Length && breachedPasswords.Contains(stripped);
    }
}
