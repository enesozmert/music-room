namespace MusicRoom.Application.Features.Events.Presence;

/// <summary>
/// The great-circle distance between two points on Earth — ADR-0014's geofence arithmetic.
/// </summary>
/// <remarks>
/// The haversine formula on a spherical Earth. A sphere is wrong by about 0.3% against the WGS-84 ellipsoid,
/// which for a geofence measured in hundreds of metres is far inside the error of the coordinate the client
/// reported in the first place — and the honest answer to "is this person at the venue" does not become more
/// honest with a more expensive ellipsoid.
/// <para>
/// A pure function: no clock, no I/O, no state. It is the only part of the presence check that can be tested
/// exhaustively, which is why it is separate from the verifier that calls it.
/// </para>
/// </remarks>
public static class Geodesy
{
    /// <summary>The mean radius of the Earth, in metres.</summary>
    public const double EarthRadiusMetres = 6_371_000;

    /// <summary>The distance between two points, in metres.</summary>
    /// <param name="fromLatitude">The first point's latitude, in degrees.</param>
    /// <param name="fromLongitude">The first point's longitude, in degrees.</param>
    /// <param name="toLatitude">The second point's latitude, in degrees.</param>
    /// <param name="toLongitude">The second point's longitude, in degrees.</param>
    /// <returns>The great-circle distance, in metres.</returns>
    /// <remarks>
    /// Correct across the antimeridian without special-casing it: haversine works on the difference of
    /// longitudes through a cosine, so 179° and -179° are two degrees apart rather than 358.
    /// </remarks>
    public static double DistanceInMetres(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude)
    {
        double fromLatitudeRadians = Radians(fromLatitude);
        double toLatitudeRadians = Radians(toLatitude);

        double latitudeDelta = toLatitudeRadians - fromLatitudeRadians;
        double longitudeDelta = Radians(toLongitude - fromLongitude);

        double haversine = (Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2))
            + (Math.Cos(fromLatitudeRadians)
                * Math.Cos(toLatitudeRadians)
                * Math.Sin(longitudeDelta / 2)
                * Math.Sin(longitudeDelta / 2));

        // Clamped before the square root: floating-point error can push the term a hair above 1 for two
        // identical points, and `Asin` of that is NaN — a distance of "not a number" would pass no check and
        // fail no check, which is the worst possible answer for a gate.
        return 2 * EarthRadiusMetres * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
    }

    /// <summary>Whether a point is within a radius of a centre.</summary>
    /// <param name="centre">The venue.</param>
    /// <param name="latitude">The submitted latitude, in degrees.</param>
    /// <param name="longitude">The submitted longitude, in degrees.</param>
    /// <returns>True when the point is inside the radius, boundary included.</returns>
    public static bool IsWithin(Geofence centre, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(centre);

        return DistanceInMetres(centre.Latitude, centre.Longitude, latitude, longitude)
            <= centre.RadiusMetres;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

/// <summary>A venue and how close counts as present.</summary>
/// <param name="Latitude">The centre's latitude, in degrees.</param>
/// <param name="Longitude">The centre's longitude, in degrees.</param>
/// <param name="RadiusMetres">How far from the centre still counts.</param>
public sealed record Geofence(double Latitude, double Longitude, int RadiusMetres);
