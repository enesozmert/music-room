using Core.Application.Pipelines.Validation;
using MusicRoom.Application.Abstractions.Auditing;
using MusicRoom.Application.Abstractions.Realtime;
using MusicRoom.Application.Features.Auth;
using MusicRoom.Application.Pipelines.Auditing;
using MusicRoom.Application.Pipelines.Realtime;
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

        services.AddValidatorsFromAssembly(typeof(ApplicationServiceRegistration).Assembly);

        // Request-scoped: one handler writes what became of the action, the audit behaviour reads it —
        // SPEC-009.
        services.AddScoped<IAuditContext, AuditContext>();

        // Request-scoped for the same reason: a handler fills it, the broadcast behaviour empties it once
        // the transaction has committed — ADR-0035.
        services.AddScoped<IPlaylistBroadcastQueue, PlaylistBroadcastQueue>();

        // Extracted from the handlers when the audit context pushed two constructors past the
        // five-parameter limit S107 enforces — SPEC-009.
        services.AddScoped<SessionIssuer>();
        services.AddScoped<CredentialMailDispatcher>();

        return services;
    }
}
