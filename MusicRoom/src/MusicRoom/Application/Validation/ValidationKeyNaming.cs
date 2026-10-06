using System.Linq.Expressions;
using System.Reflection;

namespace MusicRoom.Application.Validation;

/// <summary>
/// Turns a validated member path into the field name the client sent — ADR-0043, SPEC-037.
/// </summary>
/// <remarks>
/// A `422` body is <c>{"errors":{"key":["message"]}}</c> and the key is whatever FluentValidation reported the failure
/// against, which by default is the **CLR property path**. Every request schema in `docs/api/openapi.json` is
/// camelCase, so by default a client sends `newPassword` and is told its `NewPassword` is wrong. This resolver
/// is the one place that fixes it, which is the decision: twenty `WithName` calls would make the convention a
/// thing each author has to remember, and that is what produced two casings in the first place.
/// <para>
/// **It only answers for member-access rules.** A rule declared over the whole command —
/// `RuleFor(command => command)` — has no property path, so this returns null and the rule names itself with
/// `OverridePropertyName`. That is also the primitive for the two routes whose field is spelled differently from
/// its property rather than cased differently: `GET /tracks` sends `q` for `Query`, and the event-code route
/// sends `ttlSeconds` for `TimeToLiveSeconds`. A resolver cannot know a name only the request schema holds,
/// which is why `ValidationKeyContractTests` exists.
/// </para>
/// <para>
/// `ValidatorOptions.Global` is process-wide static state, set once by
/// <see cref="ApplicationServiceRegistration"/>. A validator constructed with <c>new</c> in a test that never
/// calls that registration sees FluentValidation's default resolver instead — recorded in ADR-0043's amendment,
/// and the reason the Application suite is run before any expectation is touched.
/// </para>
/// </remarks>
public static class ValidationKeyNaming
{
    /// <summary>
    /// The key for a rule, or null when the rule has no member to name.
    /// </summary>
    /// <param name="type">The type being validated. Not used: the name comes from the member, not its owner.</param>
    /// <param name="member">The member the rule selected, when FluentValidation resolved one.</param>
    /// <param name="expression">The lambda the rule was declared with.</param>
    /// <returns>The camelCase field path, or null for a rule over the whole object.</returns>
    public static string? Resolve(Type type, MemberInfo? member, LambdaExpression? expression)
    {
        string? path = MemberPath(expression) ?? member?.Name;

        return path is null ? null : string.Join('.', path.Split('.').Select(CamelCase));
    }

    /// <summary>
    /// Walks the member chain of a lambda, so a nested field keeps its dotted path.
    /// </summary>
    /// <remarks>
    /// The walk stops at anything that is not a member access, which is what makes
    /// `RuleFor(command => licence(command).Geofence!.Latitude)` resolve to `Geofence.Latitude` rather than to
    /// the invocation in front of it: the shared event rules select through a function, and the client never
    /// sent that function.
    /// </remarks>
    private static string? MemberPath(LambdaExpression? expression)
    {
        Expression? body = expression?.Body;

        Stack<string> segments = new();

        while (body is MemberExpression memberExpression)
        {
            segments.Push(memberExpression.Member.Name);
            body = memberExpression.Expression;
        }

        return segments.Count == 0 ? null : string.Join('.', segments);
    }

    private static string CamelCase(string segment) =>
        segment.Length == 0 || char.IsLower(segment[0])
            ? segment
            : string.Create(
                segment.Length,
                segment,
                (destination, source) =>
                {
                    source.AsSpan().CopyTo(destination);
                    destination[0] = char.ToLowerInvariant(source[0]);
                });
}
