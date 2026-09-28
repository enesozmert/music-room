using Core.Application.Pipelines.Validation;
using MusicRoom.Application.Pipelines.Auditing;
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

            // After validation on purpose: a command refused by a validator never happened, so it leaves
            // no audit record — SPEC-008. This behaviour also owns the transaction the record commits in.
            configuration.AddOpenBehavior(typeof(AuditBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(typeof(ApplicationServiceRegistration).Assembly);

        return services;
    }
}
