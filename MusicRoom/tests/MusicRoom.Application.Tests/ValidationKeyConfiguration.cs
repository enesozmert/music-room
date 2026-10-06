using System.Runtime.CompilerServices;
using FluentValidation;
using MusicRoom.Application.Validation;

namespace MusicRoom.Application.Tests;

/// <summary>
/// Applies the production key resolver to this assembly, before any test runs — SPEC-037.
/// </summary>
/// <remarks>
/// `ValidatorOptions.Global` is process-wide static state that `ApplicationServiceRegistration` sets, and these
/// tests construct validators with <c>new</c> rather than through a container. Without this, every validator
/// here would see FluentValidation's **default** resolver while the same validator behind an endpoint sees
/// ADR-0043's — so this suite would stay green on PascalCase keys for as long as nobody looked, which is exactly
/// the divergence the ADR's amendment recorded and told this unit to fix by configuration.
/// <para>
/// A module initializer rather than a fixture: the resolver is global, so making it the responsibility of any
/// one test class would leave the next class to remember it.
/// </para>
/// </remarks>
internal static class ValidationKeyConfiguration
{
    /// <summary>Runs once, when the test assembly loads.</summary>
    [ModuleInitializer]
    internal static void Apply() => ValidatorOptions.Global.PropertyNameResolver = ValidationKeyNaming.Resolve;
}
