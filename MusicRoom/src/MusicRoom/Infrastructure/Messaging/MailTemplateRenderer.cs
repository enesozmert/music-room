using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MusicRoom.Infrastructure.Messaging;

/// <summary>
/// Puts a message's words into the shared layout — SPEC-052 decision 7.
/// </summary>
/// <remarks>
/// **Substitution, not a templating engine.** `{{placeholder}}` is replaced and nothing else happens: no
/// loops, no conditionals, no expressions. Three messages do not justify a dependency, and ADR-0021 pins
/// the dependency set — adding one would be an amendment, for `string.Replace`.
/// <para>
/// **The layout is an embedded resource**, so a deployment cannot ship a message the build never saw, and
/// the words a person receives are reviewable in the same diff as the code that sends them — the reason
/// SPEC-024 refused a vendor dashboard template.
/// </para>
/// <para>
/// **An unresolved placeholder is a failure, not a mail.** `{{something}}` reaching a recipient is a defect
/// that looks like a prank, so rendering throws when one survives and a test asserts it for every message.
/// </para>
/// </remarks>
internal static partial class MailTemplateRenderer
{
    private static readonly Lazy<string> _html = new(() => Read("Message.html"));
    private static readonly Lazy<string> _text = new(() => Read("Message.txt"));

    /// <summary>Renders both bodies of one message.</summary>
    /// <param name="values">What each placeholder becomes. HTML-safe already, where it reaches HTML.</param>
    /// <returns>The HTML body and the plain-text body.</returns>
    /// <exception cref="InvalidOperationException">A placeholder had no value.</exception>
    internal static MailBody Render(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new MailBody(Fill(_html.Value, values), Fill(_text.Value, values));
    }

    private static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        string filled = values.Aggregate(
            template,
            (current, pair) => current.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.Ordinal));

        Match leftover = Placeholder().Match(filled);

        return leftover.Success
            ? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The mail template has no value for {leftover.Value}; a placeholder that reaches a recipient is a defect that looks like a prank — SPEC-052."))
            : filled;
    }

    private static string Read(string fileName)
    {
        string resource = $"MusicRoom.Infrastructure.Messaging.Templates.{fileName}";

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"The mail template {resource} is not in this assembly. It is an EmbeddedResource in "
                + "MusicRoom.Infrastructure.csproj, and a build without it would send empty mail.");

        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{[a-zA-Z]+\}\}")]
    private static partial Regex Placeholder();
}

/// <summary>
/// The two bodies of one message.
/// </summary>
/// <param name="Html">The HTML body.</param>
/// <param name="Text">The plain-text body. Beside the HTML rather than instead of it, because a client
/// that refuses HTML must still be able to verify an address — SPEC-052 decision 7.</param>
internal sealed record MailBody(string Html, string Text);
