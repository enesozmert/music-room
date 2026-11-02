using MusicRoom.Application.Abstractions.Messaging;

namespace MusicRoom.Infrastructure.Messaging;

/// <summary>
/// What each message says, in each language — SPEC-052 decisions 6 and 7.
/// </summary>
/// <remarks>
/// The words live here rather than in a vendor dashboard, so they are reviewable in the same diff as the
/// code that sends them — SPEC-024's reasoning, extended to a second language.
/// <para>
/// **Two languages, because two are written.** `tr` when the request asked for Turkish, English otherwise.
/// A third would be a third column here and nothing else, which is the point of keeping the layout and the
/// words apart.
/// </para>
/// <para>
/// No message carries an account id, a session id or anything about the account beyond the name the reader
/// chose. ADR-0013's rule, unchanged.
/// </para>
/// </remarks>
internal static class MailCopyBook
{
    /// <summary>The words of the verification message.</summary>
    /// <param name="language">Which language to write in.</param>
    /// <param name="displayName">What to call the reader, or empty when the sender does not know.</param>
    /// <returns>The copy, ready for the layout.</returns>
    internal static MailCopy Verification(MailLanguage language, string displayName) =>
        language == MailLanguage.Turkish
            ? new MailCopy(
                Subject: "Music Room adresini doğrula",
                Preheader: "Hesabını tamamlamak için adresini doğrula.",
                Heading: "Adresini doğrula",
                Greeting: Greeting(language, displayName),
                Explanation: "Bu adresle Music Room'a kayıt olundu. Kaydı tamamlamak için aşağıdaki kodu "
                    + "uygulamaya gir ya da düğmeye bas.",
                CallToAction: "Adresi doğrula",
                Validity: "Kod ve bağlantı 15 dakika geçerlidir ve bir kez kullanılabilir.",
                Footer: "Bu kaydı sen yapmadıysan bu iletiyi yok sayabilirsin; hiçbir şey değişmez.")
            : new MailCopy(
                Subject: "Confirm your Music Room address",
                Preheader: "Confirm your address to finish setting up your account.",
                Heading: "Confirm your address",
                Greeting: Greeting(language, displayName),
                Explanation: "Someone registered this address with Music Room. Enter the code below in the "
                    + "app, or use the button, to finish signing up.",
                CallToAction: "Confirm address",
                Validity: "The code and the link are valid for 15 minutes and can be used once.",
                Footer: "If this was not you, ignore this message and nothing changes.");

    /// <summary>The words of the password reset message.</summary>
    /// <param name="language">Which language to write in.</param>
    /// <param name="displayName">What to call the reader, or empty.</param>
    /// <param name="hasLink">Whether this deployment has a reset screen to send the reader to.</param>
    /// <returns>The copy, ready for the layout.</returns>
    /// <remarks>
    /// **The words follow what is actually in the message** — SPEC-055. The English copy told every
    /// reader to "use the button" while deployments without a configured reset screen sent no button,
    /// so a message that could not be acted on also explained how to act on it. A deployment without a
    /// link says code only, and one with a link says both.
    /// </remarks>
    internal static MailCopy Reset(MailLanguage language, string displayName, bool hasLink)
    {
        // Pulled out of the record literals: a conditional inside one is a nested ternary, which S3358
        // refuses, and the sentence is easier to read on its own anyway.
        string turkish = hasLink
            ? "Bu adresin parolasının sıfırlanması istendi. Yeni bir parola seçmek için aşağıdaki kodu "
                + "uygulamaya gir ya da düğmeye bas."
            : "Bu adresin parolasının sıfırlanması istendi. Yeni bir parola seçmek için aşağıdaki kodu "
                + "uygulamaya gir.";

        string english = hasLink
            ? "Someone asked to reset the credentials for this address. Enter the code below in the app, "
                + "or use the button, to choose a new password."
            : "Someone asked to reset the credentials for this address. Enter the code below in the app "
                + "to choose a new password.";

        return language == MailLanguage.Turkish
            ? new MailCopy(
                Subject: "Music Room parolanı sıfırla",
                Preheader: "Yeni bir parola seçmek için aşağıdaki kodu kullan.",
                Heading: "Parolanı sıfırla",
                Greeting: Greeting(language, displayName),
                Explanation: turkish,
                CallToAction: "Yeni parola seç",
                Validity: "Kod 1 saat geçerlidir ve bir kez kullanılabilir.",
                Footer: "Bu isteği sen yapmadıysan bu iletiyi yok say; parolan değişmez.")
            : new MailCopy(
                Subject: "Reset your Music Room password",
                Preheader: "Use the code below to choose a new password.",
                Heading: "Reset your password",
                Greeting: Greeting(language, displayName),
                Explanation: english,
                CallToAction: "Choose a new password",
                Validity: "The code is valid for 1 hour and can be used once.",
                Footer: "If it was not you, ignore this message and nothing changes.");
    }

    /// <summary>The words of the welcome message, which carries no credential.</summary>
    /// <param name="language">Which language to write in.</param>
    /// <param name="displayName">What to call the reader.</param>
    /// <returns>The copy, ready for the layout.</returns>
    internal static MailCopy Welcome(MailLanguage language, string displayName) =>
        language == MailLanguage.Turkish
            ? new MailCopy(
                Subject: "Music Room'a hoş geldin",
                Preheader: "Hesabın hazır. Buradan başlayabilirsin.",
                Heading: "Hoş geldin",
                Greeting: Greeting(language, displayName),
                Explanation: "Hesabın hazır. Music Room'da çalma listelerini birlikte düzenleyebilir, bir "
                    + "etkinlikte sıradaki parçayı oylayabilir ve müziği başka bir cihazdan "
                    + "yönetebilirsin.",
                CallToAction: "Music Room'u aç",
                Validity: string.Empty,
                Footer: "Bu iletiyi bir kez gönderiyoruz; hesabın açıldığı için aldın.")
            : new MailCopy(
                Subject: "Welcome to Music Room",
                Preheader: "Your account is ready. Here is where to start.",
                Heading: "Welcome",
                Greeting: Greeting(language, displayName),
                Explanation: "Your account is ready. In Music Room you can edit playlists together, vote "
                    + "for the next track at an event, and control playback from another device.",
                CallToAction: "Open Music Room",
                Validity: string.Empty,
                Footer: "We send this once, because your account was created.");

    /// <summary>
    /// How a message opens, with a name when there is one.
    /// </summary>
    /// <remarks>
    /// The resend and reset paths are keyed on an address and read no account — reading one would be a way
    /// to learn whether it exists — so they have no name to use, and the greeting has to work without one.
    /// </remarks>
    private static string Greeting(MailLanguage language, string displayName)
    {
        bool named = !string.IsNullOrWhiteSpace(displayName);

        return language switch
        {
            MailLanguage.Turkish when named => $"Merhaba {displayName.Trim()},",
            MailLanguage.Turkish => "Merhaba,",
            _ when named => $"Hello {displayName.Trim()},",
            _ => "Hello,",
        };
    }
}

/// <summary>
/// One message's words, before the layout wraps them.
/// </summary>
/// <param name="Subject">The subject line.</param>
/// <param name="Preheader">What a mail client previews beside the subject. Never a credential: a preview
/// pane is read over somebody's shoulder.</param>
/// <param name="Heading">The first line of the body.</param>
/// <param name="Greeting">How it opens.</param>
/// <param name="Explanation">Why the reader received this.</param>
/// <param name="CallToAction">The button's text.</param>
/// <param name="Validity">How long the credential lasts, in words. Empty for a message with none.</param>
/// <param name="Footer">What to do if this was not you.</param>
internal sealed record MailCopy(
    string Subject,
    string Preheader,
    string Heading,
    string Greeting,
    string Explanation,
    string CallToAction,
    string Validity,
    string Footer);
