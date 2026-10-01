using FluentValidation.Results;
using MusicRoom.Application.Abstractions.Persistence;
using MusicRoom.Application.Features.Events;
using MusicRoom.Application.Features.Events.Commands.CreateEvent;
using MusicRoom.Application.Features.Events.Commands.SetEventLicence;
using MusicRoom.Domain.Common;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Events;

/// <summary>
/// Validation layer for the event aggregate — testing standard, ADR-0014, SPEC-017 decisions 3 and 4.
/// </summary>
/// <remarks>
/// The rule with teeth is decision 3: a `LocationAndTimeRestricted` event with nothing configured to check
/// would read, to an evaluator, as a licence that restricts by place and time, and behave as an open door.
/// It is refused at creation **and** on update, so the state is unreachable from either direction.
/// </remarks>
public sealed class EventCommandValidatorTests
{
    private static readonly DateTime _starts = new(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _ends = new(2026, 11, 1, 23, 0, 0, DateTimeKind.Utc);

    private readonly CreateEventCommandValidator _create = new();
    private readonly SetEventLicenceCommandValidator _licence = new();

    /// <summary>An ordinary private event passes.</summary>
    [Fact]
    public void Validate_APrivateEvent_Passes()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Launch party", _starts, _ends),
            new EventLicence(Visibility.Private, LicenceMode.InvitedOnly, null, null));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>An event with no name is not an event.</summary>
    [Fact]
    public void Validate_WithNoName_Fails()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("   ", _starts, _ends),
            new EventLicence(Visibility.Private, LicenceMode.InvitedOnly, null, null));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>An event that ends before it starts has no window at all — decision 4.</summary>
    [Fact]
    public void Validate_WithAnEndBeforeTheStart_Fails()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Backwards", _ends, _starts),
            new EventLicence(Visibility.Public, LicenceMode.Everyone, null, null));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// The restricted licence with no presence proof is refused — decision 3, the rule this spec exists for.
    /// </summary>
    [Fact]
    public void Validate_TheRestrictedLicenceWithNoProof_Fails()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Venue only", _starts, _ends),
            new EventLicence(Visibility.Public, LicenceMode.LocationAndTimeRestricted, null, null));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A geofence proof with no centre and radius is the same failure one level down.</summary>
    [Theory]
    [InlineData(PresenceProof.Geofence)]
    [InlineData(PresenceProof.Both)]
    public void Validate_AGeofenceProofWithNoCentre_Fails(PresenceProof proof)
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("No centre", _starts, _ends),
            new EventLicence(Visibility.Public, LicenceMode.LocationAndTimeRestricted, proof, null));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A code-only policy needs no geofence.</summary>
    [Fact]
    public void Validate_ACodeOnlyPolicy_Passes()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Code at the door", _starts, _ends),
            new EventLicence(
                Visibility.Public,
                LicenceMode.LocationAndTimeRestricted,
                PresenceProof.EventCode,
                null));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A full geofence policy passes.</summary>
    [Fact]
    public void Validate_AGeofencePolicy_Passes()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("At the venue", _starts, _ends),
            new EventLicence(
                Visibility.Public,
                LicenceMode.LocationAndTimeRestricted,
                PresenceProof.Geofence,
                new PresencePolicy(48.8566m, 2.3522m, 250)));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>A policy on a mode that checks nothing is a policy nobody will read.</summary>
    [Fact]
    public void Validate_APolicyOnAnUnrestrictedLicence_Fails()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Pointless policy", _starts, _ends),
            new EventLicence(
                Visibility.Public,
                LicenceMode.Everyone,
                PresenceProof.Geofence,
                new PresencePolicy(48.8566m, 2.3522m, 250)));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>A radius larger than a city is not a place.</summary>
    [Fact]
    public void Validate_ARadiusBeyondTheMaximum_Fails()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Half a country", _starts, _ends),
            new EventLicence(
                Visibility.Public,
                LicenceMode.LocationAndTimeRestricted,
                PresenceProof.Geofence,
                new PresencePolicy(48.8566m, 2.3522m, EventLicenceRules.MaximumRadiusMetres + 1)));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>An impossible coordinate is refused rather than stored.</summary>
    [Fact]
    public void Validate_AnImpossibleLatitude_Fails()
    {
        // Arrange
        CreateEventCommand command = new(
            new EventMetadata("Off the globe", _starts, _ends),
            new EventLicence(
                Visibility.Public,
                LicenceMode.LocationAndTimeRestricted,
                PresenceProof.Geofence,
                new PresencePolicy(91m, 2.3522m, 250)));

        // Act
        ValidationResult result = _create.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// `PUT .../licence` refuses what creation refuses, so an event cannot be edited into the state
    /// decision 3 forbids.
    /// </summary>
    [Fact]
    public void Validate_SettingTheRestrictedLicenceWithNoProof_Fails()
    {
        // Arrange
        SetEventLicenceCommand command = new(
            Guid.NewGuid(),
            new EventLicence(Visibility.Public, LicenceMode.LocationAndTimeRestricted, null, null));

        // Act
        ValidationResult result = _licence.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}
