using FluentValidation.Results;
using MusicRoom.Application.Features.Auth.Commands.RequestEmailVerification;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Auth;

/// <summary>
/// Validation layer — testing standard. Input is rejected here, before the address reaches a
/// database read, so an anonymous caller cannot make the service query for a value it could never
/// have stored.
/// </summary>
public sealed class RequestEmailVerificationCommandValidatorTests
{
    private readonly RequestEmailVerificationCommandValidator _validator = new();

    /// <summary>An empty address never reaches the handler.</summary>
    [Fact]
    public void Validate_AnEmptyAddress_Fails()
    {
        // Arrange
        RequestEmailVerificationCommand command = new(string.Empty);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "email");
    }

    /// <summary>A value that is not an address is refused rather than looked up.</summary>
    [Fact]
    public void Validate_AMalformedAddress_Fails()
    {
        // Arrange
        RequestEmailVerificationCommand command = new("not-an-address");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "email");
    }

    /// <summary>
    /// An address longer than the column is refused. The bound is what stops an anonymous caller
    /// forcing a large allocation and an index scan per request.
    /// </summary>
    [Fact]
    public void Validate_AnAddressLongerThanTheColumn_Fails()
    {
        // Arrange — one character past the 320 of RFC 5321, keeping the shape of an address.
        const string domain = "@example.com";
        string local = new('a', RequestEmailVerificationCommandValidator.EmailMaximumLength + 1 - domain.Length);
        RequestEmailVerificationCommand command = new(local + domain);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == "email");
    }

    /// <summary>
    /// A well-formed address passes, so the tests above fail for their own reason.
    /// </summary>
    [Fact]
    public void Validate_AWellFormedAddress_Passes()
    {
        // Arrange
        RequestEmailVerificationCommand command = new("player@example.com");

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>
    /// No rule message quotes the submitted address back. A 422 body is logged by every proxy on
    /// the way out — application-security.
    /// </summary>
    [Fact]
    public void Validate_AMalformedAddress_DoesNotEchoTheSubmittedValue()
    {
        // Arrange
        const string submitted = "not-an-address";
        RequestEmailVerificationCommand command = new(submitted);

        // Act
        ValidationResult result = _validator.Validate(command);

        // Assert
        Assert.DoesNotContain(
            result.Errors,
            failure => failure.ErrorMessage.Contains(submitted, StringComparison.Ordinal));
    }
}
