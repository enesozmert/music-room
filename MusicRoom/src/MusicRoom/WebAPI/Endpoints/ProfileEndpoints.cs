using FluentValidation;
using MediatR;
using MusicRoom.Application.Features.Profiles.Commands.ReplaceMusicPreferences;
using MusicRoom.Application.Features.Profiles.Commands.UpdateProfile;
using MusicRoom.Application.Features.Profiles.Queries.GetOwnProfile;
using MusicRoom.Domain.Common;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// The caller's own profile, under <c>/api/v1/me</c> — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <remarks>
/// Every route here is <c>self</c>: the account comes from the validated token and no route names a user id, so
/// there is no path by which a caller reaches somebody else's profile.
/// </remarks>
internal static class ProfileEndpoints
{
    /// <summary>Maps the three routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/me");

        MapGetOwnProfile(group);
        MapUpdateProfile(group);
        MapReplaceMusicPreferences(group);

        return endpoints;
    }

    private static void MapGetOwnProfile(RouteGroupBuilder group) =>
        group.MapGet("/", GetOwnProfileAsync)
            .RequireAuthorization()
            .WithName("GetMe")
            .WithSummary("Read your own profile")
            .WithDescription(
                "Returns every profile field with the visibility tier that governs it, plus the caller's music "
                + "preferences. The owner is the one reader who sees every tier; what another user sees is "
                + "projected per field, which is a different endpoint.")
            .Produces<OwnProfileResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

    private static void MapUpdateProfile(RouteGroupBuilder group) =>
        group.MapPatch("/profile", UpdateProfileAsync)
            .RequireAuthorization()
            .WithName("PatchMeProfile")
            .WithSummary("Update your own profile")
            .WithDescription(
                "Updates any subset of the profile. Each field carries its value and its visibility tier in the "
                + "same object, so a field cannot exist without a tier — a value with no tier is 422, never a "
                + "silent default to public. An absent field is left untouched; sending a null value with a "
                + "tier clears it.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapReplaceMusicPreferences(RouteGroupBuilder group) =>
        group.MapPut("/music-preferences", ReplaceMusicPreferencesAsync)
            .RequireAuthorization()
            .WithName("PutMeMusicPreferences")
            .WithSummary("Replace your music preferences")
            .WithDescription(
                "Replaces the whole preference set, genres and artists. Preferences are private to their owner "
                + "and carry no visibility tier of their own. Both lists are bounded: an unbounded array behind "
                + "an authenticated endpoint is unrestricted resource consumption.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> GetOwnProfileAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        OwnProfile? profile = await sender.Send(new GetOwnProfileQuery(), cancellationToken);

        // Unreachable for any account that registered after SPEC-011, and for the ones before it after the
        // backfill. Answered rather than assumed away, because a 500 here would be a worse way to find out.
        return profile is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(profile));
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> missingTiers = MissingTiers(request);

        if (missingTiers.Count > 0)
        {
            // Refused here rather than in a validator, because by the time a command exists the tier has
            // already been defaulted to the enum's zero value. The contract's rule — "a missing tier is 422,
            // never a silent default to public" — has to be enforced on the wire model.
            return Results.ValidationProblem(
                missingTiers,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        UpdateProfileCommand command = new(
            Field(request.RealName),
            Field(request.Bio),
            Field(request.Location),
            request.BirthDate is null
                ? null
                : new ProfileDateField(request.BirthDate.Value, request.BirthDate.Visibility!.Value));

        try
        {
            UpdateProfileOutcome outcome = await sender.Send(command, cancellationToken);

            return outcome == UpdateProfileOutcome.Updated ? Results.Ok() : Results.NotFound();
        }
        catch (ValidationException exception)
        {
            return CredentialEndpointResults.Invalid(exception);
        }
    }

    private static async Task<IResult> ReplaceMusicPreferencesAsync(
        MusicPreferencesRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        ReplaceMusicPreferencesCommand command = new(
            request.Genres ?? [],
            request.Artists ?? []);

        try
        {
            await sender.Send(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return CredentialEndpointResults.Invalid(exception);
        }

        return Results.Ok();
    }

    /// <summary>
    /// Names every field the caller sent without a tier. An empty dictionary means the body is acceptable.
    /// </summary>
    private static Dictionary<string, string[]> MissingTiers(UpdateProfileRequest request)
    {
        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);

        void Require(string field, ProfileFieldVisibility? visibility)
        {
            if (visibility is null)
            {
                errors[field] = ["A visibility tier is required for every field you send."];
            }
        }

        if (request.RealName is not null)
        {
            Require(nameof(UpdateProfileRequest.RealName), request.RealName.Visibility);
        }

        if (request.Bio is not null)
        {
            Require(nameof(UpdateProfileRequest.Bio), request.Bio.Visibility);
        }

        if (request.Location is not null)
        {
            Require(nameof(UpdateProfileRequest.Location), request.Location.Visibility);
        }

        if (request.BirthDate is not null)
        {
            Require(nameof(UpdateProfileRequest.BirthDate), request.BirthDate.Visibility);
        }

        return errors;
    }

    /// <summary>Maps a wire field onto a command field, its tier now known to be present.</summary>
    private static ProfileField<string>? Field(ProfileFieldRequest<string>? field) =>
        field is null ? null : new ProfileField<string>(field.Value, field.Visibility!.Value);

    private static OwnProfileResponse ToResponse(OwnProfile profile) =>
        new(
            profile.DisplayName,
            new ProfileFieldResponse<string>(profile.RealName, profile.RealNameVisibility),
            new ProfileFieldResponse<string>(profile.Bio, profile.BioVisibility),
            new ProfileFieldResponse<string>(profile.Location, profile.LocationVisibility),
            new ProfileFieldDateResponse(profile.BirthDate, profile.BirthDateVisibility),
            profile.Genres,
            profile.Artists);
}

/// <summary>
/// The profile update payload — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <param name="RealName">The real name and its tier, or absent to leave the field alone.</param>
/// <param name="Bio">The bio and its tier, or absent.</param>
/// <param name="Location">The location and its tier, or absent.</param>
/// <param name="BirthDate">The date of birth and its tier, or absent.</param>
internal sealed record UpdateProfileRequest(
    ProfileFieldRequest<string>? RealName,
    ProfileFieldRequest<string>? Bio,
    ProfileFieldRequest<string>? Location,
    ProfileDateFieldRequest? BirthDate);

/// <summary>
/// One field as a client sends it: a value and a tier that is **optional on the wire so its absence can be
/// refused**.
/// </summary>
/// <typeparam name="T">The field's type.</typeparam>
/// <param name="Value">The value, or null to clear the field.</param>
/// <param name="Visibility">Who may see it. Absent means `422`: the binder would otherwise default a missing
/// enum to its zero value, which is `Public` — the exact silent default `docs/api/ENDPOINTS.md` forbids.</param>
internal sealed record ProfileFieldRequest<T>(T? Value, ProfileFieldVisibility? Visibility);

/// <summary>
/// The date-of-birth field as a client sends it.
/// </summary>
/// <param name="Value">The date, or null to clear the field.</param>
/// <param name="Visibility">Who may see it. Absent means `422`.</param>
internal sealed record ProfileDateFieldRequest(DateOnly? Value, ProfileFieldVisibility? Visibility);

/// <summary>
/// The music preferences payload — <c>docs/api/ENDPOINTS.md</c> §2.
/// </summary>
/// <param name="Genres">The genres the user names.</param>
/// <param name="Artists">The artists the user names.</param>
internal sealed record MusicPreferencesRequest(
    IReadOnlyList<string>? Genres,
    IReadOnlyList<string>? Artists);

/// <summary>
/// A profile as its owner reads it.
/// </summary>
/// <param name="DisplayName">The handle the account is known by. Always public.</param>
/// <param name="RealName">The real name and its tier.</param>
/// <param name="Bio">The bio and its tier.</param>
/// <param name="Location">The location and its tier.</param>
/// <param name="BirthDate">The date of birth and its tier.</param>
/// <param name="Genres">The genres the user named.</param>
/// <param name="Artists">The artists the user named.</param>
internal sealed record OwnProfileResponse(
    string DisplayName,
    ProfileFieldResponse<string> RealName,
    ProfileFieldResponse<string> Bio,
    ProfileFieldResponse<string> Location,
    ProfileFieldDateResponse BirthDate,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Artists);

/// <summary>
/// One field's value and the tier that governs it.
/// </summary>
/// <typeparam name="T">The field's type.</typeparam>
/// <param name="Value">The value, or null when the field is empty.</param>
/// <param name="Visibility">Who may see it.</param>
internal sealed record ProfileFieldResponse<T>(T? Value, ProfileFieldVisibility Visibility);

/// <summary>
/// The date-of-birth field and its tier.
/// </summary>
/// <param name="Value">The date, or null when the field is empty.</param>
/// <param name="Visibility">Who may see it.</param>
/// <remarks>
/// Its own record rather than <see cref="ProfileFieldResponse{T}"/> closed over a value type: an unconstrained
/// <c>T?</c> is "maybe-null reference", not <c>Nullable&lt;DateOnly&gt;</c>, and a generic constrained twice to
/// serve both cases is more machinery than one small record.
/// </remarks>
internal sealed record ProfileFieldDateResponse(DateOnly? Value, ProfileFieldVisibility Visibility);
