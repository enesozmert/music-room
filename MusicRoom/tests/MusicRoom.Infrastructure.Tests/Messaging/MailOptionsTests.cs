using MusicRoom.Infrastructure.Messaging;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Messaging;

/// <summary>
/// Which configurations describe a sender that may run here — SPEC-024 decision 2, T-26.
/// </summary>
/// <remarks>
/// This is the rule that replaced "the host must be Development". It is asserted on its own, rather than only
/// through the registration, because it is the one thing standing between a production host and a sender that
/// writes a live credential into a log.
/// </remarks>
public sealed class MailOptionsTests
{
    internal const string ApiKey = "a-key-that-stands-in-for-the-real-one";
    internal const string FromAddress = "sandbox@loadtest.invalid";

    /// <summary>The hosted sender, fully configured, runs anywhere.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_WithBrevoFullyConfigured_Passes(bool isDevelopment) =>
        Brevo(ApiKey, FromAddress).Validate(isDevelopment);

    /// <summary>A missing key fails at start-up, not on the first registration.</summary>
    [Fact]
    public void Validate_WithBrevoAndNoKey_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Brevo(apiKey: null, FromAddress).Validate(isDevelopment: false));

        Assert.Contains("MAIL_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>So does a missing sender address: nothing here invents one.</summary>
    [Fact]
    public void Validate_WithBrevoAndNoFromAddress_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Brevo(ApiKey, fromAddress: " ").Validate(isDevelopment: false));

        Assert.Contains("MAIL_FROM", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The key is named in the failure and its value is not — `secrets-management`.</summary>
    [Fact]
    public void Validate_WhenItThrows_NamesNoValue()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Brevo(apiKey: null, FromAddress).Validate(isDevelopment: false));

        Assert.DoesNotContain(FromAddress, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The logging sender runs on a Development host.</summary>
    [Fact]
    public void Validate_WithTheLoggingSenderOnADevelopmentHost_Passes() =>
        Logging().Validate(isDevelopment: true);

    /// <summary>And nowhere else — it writes the verification link and the reset token to the log.</summary>
    [Fact]
    public void Validate_WithTheLoggingSenderElsewhere_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Logging().Validate(isDevelopment: false));

        Assert.Contains("T-26", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown or empty provider is refused on every host, including Development.
    /// </summary>
    /// <remarks>
    /// The case that matters: a typo must not boot a host with no sender, and must not fall back to the one
    /// that logs tokens.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("resend")]
    [InlineData("Brevo ")]
    public void Validate_WithAnUnknownProvider_Throws(string provider)
    {
        MailOptions options = new(provider, ApiKey, FromAddress, null, null, null);

        Assert.Throws<InvalidOperationException>(() => options.Validate(isDevelopment: true));
        Assert.Throws<InvalidOperationException>(() => options.Validate(isDevelopment: false));
    }

    private static MailOptions Brevo(string? apiKey, string? fromAddress) =>
        new(MailOptions.BrevoProvider, apiKey, fromAddress, null, null, null);

    private static MailOptions Logging() =>
        new(MailOptions.LoggingProvider, null, null, null, null, null);
}
