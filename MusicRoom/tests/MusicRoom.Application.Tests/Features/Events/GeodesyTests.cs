using MusicRoom.Application.Features.Events.Presence;
using Xunit;

namespace MusicRoom.Application.Tests.Features.Events;

/// <summary>
/// The great-circle distance — ADR-0014's geofence arithmetic, SPEC-018.
/// </summary>
/// <remarks>
/// A pure function, so it can be tested exhaustively, which is why it is separate from the verifier. The cases
/// that matter are the ones a naive implementation gets wrong: the same point (floating-point error can make it
/// `NaN`), the antimeridian (where subtracting longitudes gives 358° instead of 2°), and a pole.
/// </remarks>
public sealed class GeodesyTests
{
    /// <summary>Two identical points are zero apart, and never `NaN`.</summary>
    [Fact]
    public void DistanceInMetres_BetweenAPointAndItself_IsZero()
    {
        // Arrange, Act
        double distance = Geodesy.DistanceInMetres(48.8566, 2.3522, 48.8566, 2.3522);

        // Assert
        Assert.Equal(0, distance, 6);
    }

    /// <summary>One degree of latitude is about 111 km anywhere.</summary>
    [Fact]
    public void DistanceInMetres_OneDegreeOfLatitude_IsAboutOneHundredAndElevenKilometres()
    {
        // Arrange, Act
        double distance = Geodesy.DistanceInMetres(0, 0, 1, 0);

        // Assert
        Assert.InRange(distance, 111_000, 111_400);
    }

    /// <summary>
    /// Across the antimeridian the two points are two degrees apart, not 358 — the bug a longitude
    /// subtraction without a cosine produces.
    /// </summary>
    [Fact]
    public void DistanceInMetres_AcrossTheAntimeridian_IsShort()
    {
        // Arrange, Act
        double distance = Geodesy.DistanceInMetres(0, 179, 0, -179);

        // Assert — two degrees at the equator, not most of the way round the planet.
        Assert.InRange(distance, 222_000, 222_800);
    }

    /// <summary>At a pole, longitude stops meaning distance.</summary>
    [Fact]
    public void DistanceInMetres_AtThePole_IgnoresLongitude()
    {
        // Arrange, Act
        double distance = Geodesy.DistanceInMetres(90, 0, 90, 123);

        // Assert
        Assert.Equal(0, distance, 3);
    }

    /// <summary>The boundary counts as inside: a radius is inclusive.</summary>
    [Fact]
    public void IsWithin_OnTheBoundary_IsTrue()
    {
        // Arrange — one degree of latitude north of the centre, with a radius that just covers it.
        Geofence centre = new(0, 0, 112_000);

        // Act, Assert
        Assert.True(Geodesy.IsWithin(centre, 1, 0));
    }

    /// <summary>Beyond the radius is outside.</summary>
    [Fact]
    public void IsWithin_BeyondTheRadius_IsFalse()
    {
        // Arrange
        Geofence centre = new(48.8566, 2.3522, 250);

        // Act, Assert — about 1.3 km away.
        Assert.False(Geodesy.IsWithin(centre, 48.8686, 2.3522));
    }

    /// <summary>Inside the radius is inside, which is the case the feature exists for.</summary>
    [Fact]
    public void IsWithin_InsideTheRadius_IsTrue()
    {
        // Arrange
        Geofence centre = new(48.8566, 2.3522, 250);

        // Act, Assert — about 20 m away.
        Assert.True(Geodesy.IsWithin(centre, 48.85678, 2.3522));
    }
}
