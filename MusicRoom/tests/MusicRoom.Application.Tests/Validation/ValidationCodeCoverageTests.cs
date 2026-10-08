using System.Reflection;
using FluentValidation;
using FluentValidation.Internal;
using MusicRoom.Application.Tests.Features.Auth;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Application.Validation;
using Xunit;

namespace MusicRoom.Application.Tests.Validation;

/// <summary>
/// Holds every rule in the application to ADR-0045's vocabulary.
/// </summary>
/// <remarks>
/// Two assertions, and the second is the one that matters as validators are added. A rule with no code
/// publishes whatever FluentValidation invented — `NotEmptyValidator`, `MinimumLengthValidator` — and an
/// implementation name is not a contract: renaming a validator class would change a published code. So a
/// missing code is a failure here rather than a surprise in a client months later.
/// <para>
/// The rules are read through <c>IValidatorDescriptor</c> rather than by provoking failures, because a
/// provoked failure only proves the rules somebody thought to provoke.
/// </para>
/// </remarks>
public sealed class ValidationCodeCoverageTests
{
    private static readonly string[] _vocabulary = [.. typeof(ValidationCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)];

    /// <summary>Every rule component names a code, and the code is one this project published.</summary>
    [Fact]
    public void EveryRule_NamesACodeFromTheVocabulary()
    {
        List<string> missing = [.. Components().Where(Unnamed).Select(Describe)];

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    /// <summary>A FluentValidation default never reaches a response — ADR-0045's third rule.</summary>
    /// <remarks>
    /// Asserted separately from the rule above so a failure says which of the two happened: a rule nobody
    /// gave a code, or a code somebody invented outside the vocabulary.
    /// </remarks>
    [Fact]
    public void NoRule_PublishesAFrameworkDefault()
    {
        List<string> defaults = [.. Components()
            .Select(component => component.Component.ErrorCode)
            .Where(code => code is not null && code.EndsWith("Validator", StringComparison.Ordinal))];

        Assert.Empty(defaults);
    }

    /// <summary>Whether a component reached a response without a code this project published.</summary>
    private static bool Unnamed((string Label, IRuleComponent Component) rule) =>
        string.IsNullOrEmpty(rule.Component.ErrorCode)
        || !_vocabulary.Contains(rule.Component.ErrorCode, StringComparer.Ordinal);

    /// <summary>What a failure says, so the message names the rule rather than a count.</summary>
    private static string Describe((string Label, IRuleComponent Component) rule) =>
        string.IsNullOrEmpty(rule.Component.ErrorCode)
            ? $"{rule.Label}: no code"
            : $"{rule.Label}: '{rule.Component.ErrorCode}' is not in ValidationCodes";

    /// <summary>Every rule component in the assembly, labelled by the validator and property it belongs to.</summary>
    private static IEnumerable<(string Label, IRuleComponent Component)> Components() =>
        Validators().SelectMany(validator => validator.CreateDescriptor().Rules
            .SelectMany(rule => rule.Components.Select(component =>
                ($"{validator.GetType().Name}.{rule.PropertyName ?? "<command>"}", component))));

    /// <summary>Every validator this assembly declares, built with substitutes for what it needs.</summary>
    private static IEnumerable<IValidator> Validators()
    {
        foreach (Type type in typeof(ValidationCodes).Assembly.GetTypes()
            .Where(candidate => candidate is { IsAbstract: false, IsPublic: true }
                && typeof(IValidator).IsAssignableFrom(candidate))
            .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal))
        {
            ConstructorInfo constructor = type.GetConstructors().Single();

            object?[] arguments = [.. constructor.GetParameters().Select(Argument)];

            yield return (IValidator)constructor.Invoke(arguments);
        }
    }

    /// <summary>What a validator's constructor needs, or a failure naming what this test cannot build.</summary>
    /// <remarks>
    /// Deliberately not a mocking library: this assembly references none, and adding one for a reflection
    /// helper would be a dependency added by a test. A validator that needs something new fails here with its
    /// own name, which is the right place to notice it.
    /// </remarks>
    private static object Argument(ParameterInfo parameter) =>
        parameter.ParameterType == typeof(IBreachedPasswordSet)
            ? new StubBreachedPasswordSet()
            : throw new InvalidOperationException(
                $"No stand-in for {parameter.ParameterType.Name}; add one here rather than skipping the validator.");
}
