namespace MusicRoom.Application.Validation;

/// <summary>
/// The vocabulary a refused request names itself with — ADR-0045.
/// </summary>
/// <remarks>
/// A client cannot translate a sentence: the server may reword *"Password must be at least 12 characters."*
/// for clarity and every client that matched on the old wording breaks silently. So the server names the rule
/// and the client owns the words. The message stays English, for a developer reading a log.
/// <para>
/// **Three rules bind this file, and they are the whole point of it being a file.**
/// </para>
/// <para>
/// **A code is a contract.** Once published it is never reworded, never recased and never reused for a
/// different meaning. Retiring one means publishing its replacement and keeping both until the client has
/// shipped.
/// </para>
/// <para>
/// **Every code lives here.** A literal typed at a rule is a second vocabulary nobody can grep, and two
/// vocabularies disagree the first time one of them is edited.
/// </para>
/// <para>
/// **No framework default ever ships.** FluentValidation fills <c>ErrorCode</c> with `NotEmptyValidator`,
/// `MinimumLengthValidator` and the like when nothing sets it. Those are implementation names, not a
/// contract: renaming a validator class would change a published code.
/// `ValidationCodeCoverageTests` fails if one reaches a response.
/// </para>
/// </remarks>
public static class ValidationCodes
{
    /// <summary>The address is absent or blank.</summary>
    public const string EmailRequired = "EMAIL.REQUIRED";

    /// <summary>The address is longer than the column allows.</summary>
    public const string EmailTooLong = "EMAIL.TOO_LONG";

    /// <summary>The address is not an address.</summary>
    public const string EmailInvalid = "EMAIL.INVALID";

    /// <summary>No password was sent.</summary>
    public const string PasswordRequired = "PASSWORD.REQUIRED";

    /// <summary>Below the minimum length — SPEC-034.</summary>
    public const string PasswordTooShort = "PASSWORD.TOO_SHORT";

    /// <summary>Above the maximum length.</summary>
    public const string PasswordTooLong = "PASSWORD.TOO_LONG";

    /// <summary>Above BCrypt's 72-byte input ceiling once encoded as UTF-8.</summary>
    public const string PasswordTooManyBytes = "PASSWORD.TOO_MANY_BYTES";

    /// <summary>Found in the bundled breached set — SPEC-035.</summary>
    public const string PasswordBreached = "PASSWORD.BREACHED";

    /// <summary>Contains the caller's own words — SPEC-035.</summary>
    public const string PasswordContextWord = "PASSWORD.CONTEXT_WORD";

    /// <summary>The new password is the current one — SPEC-028.</summary>
    public const string PasswordUnchanged = "PASSWORD.UNCHANGED";

    /// <summary>The current password was not sent.</summary>
    public const string CurrentPasswordRequired = "CURRENT_PASSWORD.REQUIRED";

    /// <summary>The current password is wrong — a handler outcome, SPEC-028.</summary>
    public const string CurrentPasswordIncorrect = "CURRENT_PASSWORD.INCORRECT";

    /// <summary>No display name was sent.</summary>
    public const string DisplayNameRequired = "DISPLAY_NAME.REQUIRED";

    /// <summary>The display name is too long.</summary>
    public const string DisplayNameTooLong = "DISPLAY_NAME.TOO_LONG";

    /// <summary>No token was sent.</summary>
    public const string TokenRequired = "TOKEN.REQUIRED";

    /// <summary>The token is longer than the one this system issues.</summary>
    public const string TokenTooLong = "TOKEN.TOO_LONG";

    /// <summary>No refresh token was sent.</summary>
    public const string RefreshTokenRequired = "REFRESH_TOKEN.REQUIRED";

    /// <summary>The refresh token is longer than the one this system issues.</summary>
    public const string RefreshTokenTooLong = "REFRESH_TOKEN.TOO_LONG";

    /// <summary>No search term was sent.</summary>
    public const string QueryRequired = "QUERY.REQUIRED";

    /// <summary>The search term is shorter than the catalog accepts.</summary>
    public const string QueryTooShort = "QUERY.TOO_SHORT";

    /// <summary>The search term is longer than the catalog accepts.</summary>
    public const string QueryTooLong = "QUERY.TOO_LONG";

    /// <summary>The page number is below one.</summary>
    public const string PageOutOfRange = "PAGE.OUT_OF_RANGE";

    /// <summary>The page size is outside the capped range — OWASP API4.</summary>
    public const string PageSizeOutOfRange = "PAGE_SIZE.OUT_OF_RANGE";

    /// <summary>No device was named.</summary>
    public const string DeviceRequired = "DEVICE.REQUIRED";

    /// <summary>No grantee was named.</summary>
    public const string GranteeRequired = "GRANTEE.REQUIRED";

    /// <summary>No account was named.</summary>
    public const string TargetUserRequired = "TARGET_USER.REQUIRED";

    /// <summary>No event was named.</summary>
    public const string EventRequired = "EVENT.REQUIRED";

    /// <summary>The code's lifetime is outside the permitted range.</summary>
    public const string TimeToLiveOutOfRange = "TTL.OUT_OF_RANGE";

    /// <summary>No video id was sent.</summary>
    public const string VideoRequired = "VIDEO.REQUIRED";

    /// <summary>The video id is not eleven characters of the permitted alphabet.</summary>
    public const string VideoInvalid = "VIDEO.INVALID";

    /// <summary>No playlist was named.</summary>
    public const string PlaylistRequired = "PLAYLIST.REQUIRED";

    /// <summary>No playlist track was named.</summary>
    public const string PlaylistTrackRequired = "PLAYLIST_TRACK.REQUIRED";

    /// <summary>No name was sent.</summary>
    public const string NameRequired = "NAME.REQUIRED";

    /// <summary>The name is too long.</summary>
    public const string NameTooLong = "NAME.TOO_LONG";

    /// <summary>The visibility is not one this system knows.</summary>
    public const string VisibilityUnknown = "VISIBILITY.UNKNOWN";

    /// <summary>The licence is not one this system knows.</summary>
    public const string LicenceUnknown = "LICENCE.UNKNOWN";

    /// <summary>The licence is known and not permitted on this resource.</summary>
    public const string LicenceNotAllowed = "LICENCE.NOT_ALLOWED";

    /// <summary>Both a head insert and a neighbour were sent.</summary>
    public const string PositionConflicting = "POSITION.CONFLICTING";

    /// <summary>Neither or both neighbours were sent, where exactly one is required.</summary>
    public const string PositionNeighbourRequired = "POSITION.NEIGHBOUR_REQUIRED";

    /// <summary>A track cannot be moved relative to itself.</summary>
    public const string PositionSelfMove = "POSITION.SELF_MOVE";

    /// <summary>The genres list was absent where an empty list is the way to clear it.</summary>
    public const string GenresRequired = "GENRES.REQUIRED";

    /// <summary>More genres than the cap allows.</summary>
    public const string GenresTooMany = "GENRES.TOO_MANY";

    /// <summary>A genre entry is blank.</summary>
    public const string GenreRequired = "GENRE.REQUIRED";

    /// <summary>A genre entry is too long.</summary>
    public const string GenreTooLong = "GENRE.TOO_LONG";

    /// <summary>The artists list was absent where an empty list is the way to clear it.</summary>
    public const string ArtistsRequired = "ARTISTS.REQUIRED";

    /// <summary>More artists than the cap allows.</summary>
    public const string ArtistsTooMany = "ARTISTS.TOO_MANY";

    /// <summary>An artist entry is blank.</summary>
    public const string ArtistRequired = "ARTIST.REQUIRED";

    /// <summary>An artist entry is too long.</summary>
    public const string ArtistTooLong = "ARTIST.TOO_LONG";

    /// <summary>The real name is too long.</summary>
    public const string RealNameTooLong = "REAL_NAME.TOO_LONG";

    /// <summary>The biography is too long.</summary>
    public const string BioTooLong = "BIO.TOO_LONG";

    /// <summary>The location is too long.</summary>
    public const string LocationTooLong = "LOCATION.TOO_LONG";

    /// <summary>The birth date is outside the permitted range.</summary>
    public const string BirthDateOutOfRange = "BIRTH_DATE.OUT_OF_RANGE";

    /// <summary>A restricted licence was chosen without the presence proof it requires.</summary>
    public const string PresenceProofRequired = "PRESENCE_PROOF.REQUIRED";

    /// <summary>A geofence proof was chosen without a centre and a radius.</summary>
    public const string GeofenceRequired = "GEOFENCE.REQUIRED";

    /// <summary>The latitude is outside the permitted range.</summary>
    public const string GeofenceLatitudeOutOfRange = "GEOFENCE.LATITUDE_OUT_OF_RANGE";

    /// <summary>The longitude is outside the permitted range.</summary>
    public const string GeofenceLongitudeOutOfRange = "GEOFENCE.LONGITUDE_OUT_OF_RANGE";

    /// <summary>The radius is outside the permitted range.</summary>
    public const string GeofenceRadiusOutOfRange = "GEOFENCE.RADIUS_OUT_OF_RANGE";

    /// <summary>No start time was sent.</summary>
    public const string StartsAtRequired = "STARTS_AT.REQUIRED";

    /// <summary>The window ends before it starts.</summary>
    public const string EndsAtBeforeStart = "ENDS_AT.BEFORE_START";
}
