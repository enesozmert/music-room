using MusicRoom.Application.Abstractions.Messaging;
using MusicRoom.Infrastructure.Messaging;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Messaging;

/// <summary>
/// What the three messages look like — SPEC-052 decisions 7 and 8.
/// </summary>
/// <remarks>
/// A mail nobody can read is a mail nobody acts on, and a mail with `{{heading}}` in it is worse than
/// none. These assertions are about the parts a reader meets: the language, the code as text, the
/// contrast of the palette, and the absence of anything unrendered.
/// </remarks>
public sealed class MailTemplateTests
{
    private const string _linkBase = "https://app.musicroom.test/verify";

    /// <summary>No placeholder survives rendering, in any message, in either language.</summary>
    /// <remarks>
    /// The one defect in this area that is unambiguous: a template token reaching an inbox is a product
    /// that looks broken. The renderer throws on one, and this walks every message so the throw is proved
    /// to be unreachable rather than merely present.
    /// </remarks>
    [Fact]
    public void NoMessage_LeavesAPlaceholderUnrendered()
    {
        foreach (MailLanguage language in new[] { MailLanguage.English, MailLanguage.Turkish })
        {
            foreach (RenderedMail mail in Everything(language))
            {
                Assert.DoesNotContain("{{", mail.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("{{", mail.Text, StringComparison.Ordinal);
                Assert.NotEmpty(mail.Subject);
            }
        }
    }

    /// <summary>A Turkish request gets Turkish words and a Turkish `lang` attribute.</summary>
    /// <remarks>
    /// The attribute is not decoration: a screen reader chooses its pronunciation from it, so a Turkish
    /// message marked `lang="en"` is read aloud as nonsense — WCAG 3.1.1.
    /// </remarks>
    [Fact]
    public void Turkish_IsTurkishInTheWordsAndInTheMarkup()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.Turkish), _linkBase);

        Assert.Contains("lang=\"tr\"", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Adresini doğrula", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Doğrulama kodun", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>An English request gets English, which is also what anything unrecognised gets.</summary>
    [Fact]
    public void English_IsTheOtherLanguageAndTheDefault()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.English), _linkBase);

        Assert.Contains("lang=\"en\"", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Confirm your address", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>The code is in both bodies, as text.</summary>
    /// <remarks>
    /// Text, not an image and not a colour: a screen reader has to read it out, and a client that blocks
    /// images or refuses HTML still has to leave a way to verify.
    /// </remarks>
    [Fact]
    public void TheCode_IsReadableInBothBodies()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.English), _linkBase);

        Assert.Contains("ABC234", mail.Html, StringComparison.Ordinal);
        Assert.Contains("ABC234", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>The reset message carries a link and **no** code — SPEC-052's refusal.</summary>
    /// <remarks>
    /// A six-character code that replaces a password is a weak credential for the strongest action in the
    /// system. This is the test that stops somebody adding one for symmetry.
    /// </remarks>
    [Fact]
    public void TheResetMessage_CarriesNoCode()
    {
        PasswordResetMail message = new("reader@example.com", "Reader", "a-reset-token", MailLanguage.English);

        RenderedMail mail = CredentialMailTemplates.Reset(message, _linkBase);

        Assert.Contains("a-reset-token", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Your verification code", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>The welcome message carries no credential at all.</summary>
    [Fact]
    public void TheWelcomeMessage_CarriesNoCredential()
    {
        RenderedMail mail = CredentialMailTemplates.Welcome(
            new WelcomeMail("reader@example.com", "Reader", MailLanguage.English),
            _linkBase);

        Assert.Contains("Welcome", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("code", mail.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The palette is black on white, with a dark variant — SPEC-052 decision 8.</summary>
    /// <remarks>
    /// `#000000` on `#ffffff` is 21:1, the highest contrast there is and four times what WCAG AA asks of
    /// body text. The dark block matters for the same reason in reverse: a client that inverts a white
    /// background without being told produces grey on grey.
    /// </remarks>
    [Fact]
    public void ThePalette_IsBlackOnWhiteAndSurvivesDarkMode()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.English), _linkBase);

        Assert.Contains("background:#ffffff", mail.Html, StringComparison.Ordinal);
        Assert.Contains("color:#000000", mail.Html, StringComparison.Ordinal);
        Assert.Contains("prefers-color-scheme: dark", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>The button is big enough to hit and the preheader carries no credential.</summary>
    /// <remarks>
    /// 44 pixels is the target size WCAG 2.5.8 asks for. The preheader is what a notification shows on a
    /// locked screen, which is the one place a code must not appear.
    /// </remarks>
    [Fact]
    public void TheButtonIsTouchable_AndThePreviewCarriesNoCode()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.English), _linkBase);

        Assert.Contains("min-height:44px", mail.Html, StringComparison.Ordinal);

        string preview = mail.Html[..mail.Html.IndexOf("</div>", StringComparison.Ordinal)];

        Assert.DoesNotContain("ABC234", preview, StringComparison.Ordinal);
    }

    /// <summary>With no link configured, the code still gets through.</summary>
    /// <remarks>
    /// The rule `LoggingMailService` recorded first: a link pointing at a machine nobody intended is worse
    /// than no link. Without a base URL the message is the code, and the flow completes through the API.
    /// </remarks>
    [Fact]
    public void WithNoLinkBase_TheMessageIsStillUsable()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.English), null);

        Assert.Contains("ABC234", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"\"", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>The footer names the product, not the school — owner's correction, 2026-10-12.</summary>
    /// <remarks>
    /// Mail goes to people who do not know what École 42 is and have no reason to. A test because the
    /// footer is in a layout nobody opens twice, and the next person to edit it should be told what it
    /// has to say.
    /// </remarks>
    [Fact]
    public void TheFooter_NamesTheProduct()
    {
        RenderedMail mail = CredentialMailTemplates.Verification(Verification(MailLanguage.English), _linkBase);

        Assert.Contains("musicroomaiapp", mail.Html, StringComparison.Ordinal);
        Assert.Contains("musicroomaiapp", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("École", mail.Html, StringComparison.Ordinal);
    }

    private static VerificationMail Verification(MailLanguage language) =>
        new("reader@example.com", "Reader", "a-verification-token", "ABC234", language);

    private static IEnumerable<RenderedMail> Everything(MailLanguage language)
    {
        yield return CredentialMailTemplates.Verification(Verification(language), _linkBase);
        yield return CredentialMailTemplates.Verification(Verification(language), null);
        yield return CredentialMailTemplates.Reset(
            new PasswordResetMail("reader@example.com", "Reader", "a-reset-token", language), _linkBase);
        yield return CredentialMailTemplates.Welcome(
            new WelcomeMail("reader@example.com", "Reader", language), _linkBase);
    }
}
