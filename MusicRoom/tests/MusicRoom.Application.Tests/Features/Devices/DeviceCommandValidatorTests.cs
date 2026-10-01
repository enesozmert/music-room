using FluentValidation.Results;
using MusicRoom.Application.Features.Devices.Commands.GrantControl;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Devices;

/// <summary>
/// Validation layer for the control grant — testing standard, SPEC-020.
/// </summary>
/// <remarks>
/// Shape only. Whether the grantee is a **friend** is a read of the graph and therefore the handler's job: a
/// validator cannot reach the database, and faking the check here would make the `422` a guess.
/// </remarks>
public sealed class DeviceCommandValidatorTests
{
    private readonly GrantControlCommandValidator _grant = new();

    /// <summary>A grant naming a device and a grantee passes.</summary>
    [Fact]
    public void Validate_AGrantToSomebody_Passes()
    {
        // Arrange
        GrantControlCommand command = new(Guid.NewGuid(), Guid.NewGuid(), ExpectedVersion: 7);

        // Act
        ValidationResult result = _grant.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A grant naming nobody is refused.</summary>
    [Fact]
    public void Validate_AGrantWithNoGrantee_Fails()
    {
        // Arrange
        GrantControlCommand command = new(Guid.NewGuid(), Guid.Empty, ExpectedVersion: 7);

        // Act
        ValidationResult result = _grant.Validate(command);

        // Assert — the failure names the field, whichever casing FluentValidation resolved it to.
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName.Contains("grantee", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A grant naming no device is refused.</summary>
    [Fact]
    public void Validate_AGrantWithNoDevice_Fails()
    {
        // Arrange
        GrantControlCommand command = new(Guid.Empty, Guid.NewGuid(), ExpectedVersion: 7);

        // Act
        ValidationResult result = _grant.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
