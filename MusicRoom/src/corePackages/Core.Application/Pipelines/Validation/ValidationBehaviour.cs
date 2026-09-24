using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Core.Application.Pipelines.Validation;

/// <summary>
/// Runs every FluentValidation validator registered for a request before its handler sees it —
/// position 3 of the pipeline in clean-architecture-cqrs.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Validators do not execute themselves. Without this behaviour a validator is decoration, and the
/// rules it declares are documentation of a check nothing performs.
/// </remarks>
public sealed class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>Takes every validator the container found for <typeparamref name="TRequest"/>.</summary>
    /// <param name="validators">The registered validators; an empty sequence is normal.</param>
    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators) =>
        _validators = validators;

    /// <summary>Validates, then calls the next step in the pipeline.</summary>
    /// <param name="request">The request being dispatched.</param>
    /// <param name="next">The next step.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>Whatever the handler returns.</returns>
    /// <exception cref="ValidationException">Thrown when any rule fails. The transport layer maps it
    /// to a response; it never reaches the handler.</exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        ValidationContext<TRequest> context = new(request);

        ValidationResult[] results = await Task.WhenAll(
            _validators.Select(validator => validator.ValidateAsync(context, cancellationToken)))
            .ConfigureAwait(false);

        ValidationFailure[] failures = results
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToArray();

        return failures.Length > 0
            ? throw new ValidationException(failures)
            : await next(cancellationToken).ConfigureAwait(false);
    }
}
