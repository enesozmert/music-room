using Core.Application.Pipelines.Validation;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Features.Events.Presence;
using MusicRoom.Application.Features.Playback;
using MusicRoom.Application.Pipelines.Auditing;
using MusicRoom.Application.Pipelines.Realtime;
using MusicRoom.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace MusicRoom.Application;

/// <summary>
/// Registers everything this layer owns. The composition root calls it and nothing else —
/// clean-architecture-cqrs.
/// </summary>
public static class ApplicationServiceRegistration
{
    /// <summary>
    /// Registers the MediatR handlers, the validators, and the validation behaviour that makes the
    /// validators run.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, so calls chain.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ApplicationServiceRegistration).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehaviour<,>));

            // Outside the audit behaviour on purpose, and therefore outside its transaction: when the
            // inner pipeline returns, the change has committed and a broadcast is safe to send —
            // ADR-0035. Registration order is the mechanism, not a preference.
            configuration.AddOpenBehavior(typeof(BroadcastBehaviour<,>));

            // After validation on purpose: a command refused by a validator never happened, so it leaves
            // no audit record — SPEC-008. This behaviour also owns the transaction the record commits in.
            configuration.AddOpenBehavior(typeof(AuditBehaviour<,>));
        });

        // Once, globally: a validation error is keyed by the field the client sent — ADR-0043, SPEC-037. Here
        // rather than in the composition root because `Program.cs` is at S104's 300-line limit (issue O24), and
        // this is the method that already registers every validator, called by the root exactly once.
        ValidatorOptions.Global.PropertyNameResolver = ValidationKeyNaming.Resolve;

        services.AddValidatorsFromAssembly(typeof(ApplicationServiceRegistration).Assembly);

        // Request-scoped: one handler writes what became of the action, the audit behaviour reads it —
        // SPEC-009.
        services.AddScoped<IAuditContext, AuditContext>();

        // Request-scoped for the same reason: a handler fills it, the broadcast behaviour empties it once
        // the transaction has committed — ADR-0035.
        services.AddScoped<IBroadcastQueue, BroadcastQueue>();

        // Extracted from the handlers when the audit context pushed two constructors past the
        // five-parameter limit S107 enforces — SPEC-009.
        // Rules on the evidence a voter supplies — ADR-0014. Scoped because it spends a code through the
        // request's own repository.
        services.AddScoped<PresenceVerifier>();

        // The licence and the proof as one question, so no caller can ask one half and believe it asked both.
        services.AddScoped<EventVoteGate>();

        // Who may command a playback session, and the writes that honour it — ADR-0017, SPEC-021.
        services.AddScoped<PlaybackControlGate>();
        services.AddScoped<PlaybackSessionWriter>();

        services.AddScoped<SessionIssuer>();
        services.AddScoped<CredentialMailDispatcher>();

        // The one-per-account welcome message — SPEC-052. Scoped like the issuer that calls it, because
        // it writes a stamp in the request's own unit of work.
        services.AddScoped<WelcomeMailSender>();

        return services;
    }
}
