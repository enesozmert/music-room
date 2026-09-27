using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Features.Auth.Commands.Login;
using MusicRoom.Application.Features.Auth.Commands.Register;
using MusicRoom.Application.Features.Auth.Commands.RequestEmailVerification;
using MusicRoom.Application.Features.Auth.Commands.RotateSession;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Features.Auth.Commands.VerifyEmail;
using MusicRoom.WebAPI.RateLimiting;

namespace MusicRoom.WebAPI.Endpoints;

/// <summary>
/// Identity endpoints under <c>/api/v1/auth</c>, as specified in <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>Maps the identity endpoints implemented so far.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The same builder, so calls chain.</returns>
    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/auth");

        MapRegistrations(group);
        MapEmailVerifications(group);
        MapVerificationRequests(group);
        MapSessions(group);
        MapTokens(group);

        return endpoints;
    }

    private static void MapRegistrations(RouteGroupBuilder group) =>
        group.MapPost("/registrations", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Registrations)
            .WithName("PostAuthRegistration")
            .WithSummary("Register an account")
            .WithDescription(
                "Accepts the registration and answers 202 whether or not the address is already "
                + "registered. A 409 here would tell an anonymous caller who holds an account; the "
                + "mailbox tells the real user what happened.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapEmailVerifications(RouteGroupBuilder group) =>
        group.MapPost("/email-verifications", VerifyEmailAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.EmailVerifications)
            .WithName("PostAuthEmailVerification")
            .WithSummary("Verify an email address")
            .WithDescription(
                "Spends the token from the verification mail exactly once and marks the address "
                + "verified. The description says nothing about what separates 400 from 410 for a "
                + "given token, because that is the caller's business only when they hold it.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapVerificationRequests(RouteGroupBuilder group) =>
        group.MapPost("/verification-requests", RequestEmailVerificationAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.VerificationRequests)
            .WithName("PostAuthVerificationRequest")
            .WithSummary("Ask for another email verification link")
            .WithDescription(
                "Answers 202 for any address that is a valid address. Whether a mail follows is "
                + "not stated and cannot be inferred from the response: a distinct answer here "
                + "would tell an anonymous caller which addresses hold a registered or confirmed "
                + "account.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapSessions(RouteGroupBuilder group) =>
        group.MapPost("/sessions", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Sessions)
            .WithName("PostAuthSession")
            .WithSummary("Sign in")
            .WithDescription(
                "Exchanges an address and a password for a token pair. Every failure — unknown "
                + "address, wrong password, unverified account — answers the same bodiless 401, and "
                + "the unknown-address path spends the same hashing work as the others, so the "
                + "response cannot be timed either.")
            .Produces<TokenPairResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static void MapTokens(RouteGroupBuilder group) =>
        group.MapPost("/tokens", RotateSessionAsync)
            .AllowAnonymous()
            .RequireRateLimiting(CredentialRateLimiterPolicies.Tokens)
            .WithName("PostAuthToken")
            .WithSummary("Rotate a session")
            .WithDescription(
                "Spends a refresh token exactly once and returns a rotated pair. Presenting an "
                + "already-rotated token revokes the whole family and answers 401: that is the "
                + "session-theft detector, and it answers exactly as an unknown or expired token "
                + "does.")
            .Produces<TokenPairResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

    private static async Task<IResult> LoginAsync(
        SessionRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        LoginCommand command = new(request.Email, request.Password);

        try
        {
            return await SendForTokenPairAsync(sender, command, cancellationToken);
        }
        catch (CredentialAttemptThrottledException exception)
        {
            return Throttled(httpContext, exception);
        }
    }

    private static async Task<IResult> RotateSessionAsync(
        TokenRotationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        RotateSessionCommand command = new(request.RefreshToken);

        // No per-account limit here: the request names no account, only a token. The per-IP policy is
        // the whole control on this route, and 256 bits of token entropy is the rest of the answer.
        return await SendForTokenPairAsync(sender, command, cancellationToken);
    }

    /// <summary>
    /// Runs a command that answers with a token pair, and turns every refusal into one bodiless 401.
    /// </summary>
    /// <remarks>
    /// A ValidationException becomes 401 here rather than the 422 registration answers: the contract
    /// rows for both endpoints list 401 and 429 only, and a rule message describing why a credential
    /// was rejected is a hint about its shape.
    /// </remarks>
    private static async Task<IResult> SendForTokenPairAsync(
        ISender sender,
        IRequest<AuthTokenPair?> command,
        CancellationToken cancellationToken)
    {
        AuthTokenPair? pair;

        try
        {
            pair = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException)
        {
            return Results.Unauthorized();
        }

        return pair is null
            ? Results.Unauthorized()
            : Results.Ok(new TokenPairResponse(pair.AccessToken, pair.RefreshToken, pair.ExpiresInSeconds));
    }

    private static async Task<IResult> RegisterAsync(
        RegistrationRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        RegisterCommand command = new(request.Email, request.Password, request.DisplayName);

        try
        {
            await sender.Send(command, cancellationToken);
        }
        catch (CredentialAttemptThrottledException exception)
        {
            return Throttled(httpContext, exception);
        }
        catch (ValidationException exception)
        {
            return Results.ValidationProblem(
                ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> RequestEmailVerificationAsync(
        VerificationRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        RequestEmailVerificationCommand command = new(request.Email);

        try
        {
            await sender.Send(command, cancellationToken);
        }
        catch (CredentialAttemptThrottledException exception)
        {
            return Throttled(httpContext, exception);
        }
        catch (ValidationException exception)
        {
            // 422 with the rule messages, as registration answers: the rules describe the shape of
            // an address, which is public knowledge, and say nothing about which addresses exist.
            return Results.ValidationProblem(
                ToErrors(exception),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> VerifyEmailAsync(
        EmailVerificationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        VerifyEmailCommand command = new(request.Token);

        VerifyEmailOutcome outcome;

        try
        {
            outcome = await sender.Send(command, cancellationToken);
        }
        catch (ValidationException)
        {
            // 400, not the 422 registration answers. The contract row for this endpoint lists 400
            // and 410 only, and the failures carry no body: a malformed token must not be echoed,
            // and the rule that rejected it is a hint about the token's shape.
            return Results.BadRequest();
        }

        return outcome switch
        {
            VerifyEmailOutcome.Verified => Results.NoContent(),
            VerifyEmailOutcome.Expired => Results.StatusCode(StatusCodes.Status410Gone),

            // Unknown, malformed and already spent share this answer on purpose — SPEC-003, owner
            // decision 2026-09-26.
            _ => Results.BadRequest(),
        };
    }

    /// <summary>
    /// Answers `429` with <c>Retry-After</c> when the per-account limiter refused the attempt.
    /// </summary>
    /// <remarks>
    /// The per-IP half of the limit is middleware and never reaches a handler; this is the half that
    /// knows which account was named (SPEC-006 escalation 2, option B). The body is empty for the same
    /// reason the middleware's is: a message saying which key tripped tells a caller which key to vary.
    /// </remarks>
    private static IResult Throttled(HttpContext httpContext, CredentialAttemptThrottledException exception)
    {
        httpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(exception.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Turns validation failures into the ProblemDetails error dictionary. Only rule messages cross
    /// this boundary — never the submitted value, which for one of these fields is a password.
    /// </summary>
    private static Dictionary<string, string[]> ToErrors(ValidationException exception) =>
        exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(ErrorMessage).ToArray(),
                StringComparer.Ordinal);

    private static string ErrorMessage(ValidationFailure failure) => failure.ErrorMessage;
}

/// <summary>
/// The registration payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Email">The address to register.</param>
/// <param name="Password">The plaintext password, 12 to 64 characters and at most 72 UTF-8 bytes.</param>
/// <param name="DisplayName">The name other users see.</param>
internal sealed record RegistrationRequest(string Email, string Password, string DisplayName);

/// <summary>
/// The email verification payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Token">The token from the verification mail.</param>
internal sealed record EmailVerificationRequest(string Token);

/// <summary>
/// The verification resend payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Email">The address to send another verification link to.</param>
internal sealed record VerificationRequest(string Email);

/// <summary>
/// The sign-in payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="Email">The address the account is known by.</param>
/// <param name="Password">The plaintext password. Never logged or echoed back.</param>
internal sealed record SessionRequest(string Email, string Password);

/// <summary>
/// The rotation payload — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="RefreshToken">The refresh token issued by the previous exchange.</param>
internal sealed record TokenRotationRequest(string RefreshToken);

/// <summary>
/// The token pair both session endpoints answer with — <c>docs/api/ENDPOINTS.md</c> §1.
/// </summary>
/// <param name="AccessToken">The signed, short-lived JWT.</param>
/// <param name="RefreshToken">The opaque refresh token, to be presented to
/// <c>POST /auth/tokens</c>. The server keeps only its digest.</param>
/// <param name="ExpiresIn">The access token's lifetime in seconds, for the client's own planning.</param>
internal sealed record TokenPairResponse(string AccessToken, string RefreshToken, int ExpiresIn);
