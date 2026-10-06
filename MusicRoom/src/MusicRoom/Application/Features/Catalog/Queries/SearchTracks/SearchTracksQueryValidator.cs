using FluentValidation;

namespace MusicRoom.Application.Features.Catalog.Queries.SearchTracks;

/// <summary>
/// What a search may ask for — `docs/api/ENDPOINTS.md` §3.
/// </summary>
/// <remarks>
/// Public, like every other validator here: `AddValidatorsFromAssembly` skips internal types unless it is told
/// not to, so an internal validator is a rule nothing runs — which is exactly what the first version of this
/// file was, and the endpoint answered `200` to a blank query.
/// <para>
/// A blank query is `422` rather than "everything": the provider would charge a hundred units to answer a
/// question nobody asked. The page size is capped because an unbounded list is OWASP API4, and because every
/// result beyond the cap is quota spent on rows the caller will not read.
/// </para>
/// </remarks>
public sealed class SearchTracksQueryValidator : AbstractValidator<SearchTracksQuery>
{
    /// <summary>The shortest query worth a provider call.</summary>
    public const int MinimumQueryLength = 2;

    /// <summary>The longest query accepted.</summary>
    public const int MaximumQueryLength = 100;

    /// <summary>The largest page a caller may ask for.</summary>
    public const int MaximumPageSize = 25;

    /// <summary>Builds the rules.</summary>
    public SearchTracksQueryValidator()
    {
        // The client sends `q`, not `query` — the resolver camelCases a property name and cannot know a name only
        // the request schema holds, so this route states it (ADR-0043 as amended, SPEC-037).
        RuleFor(query => query.Query)
            .NotEmpty()
            .OverridePropertyName("q")
            .MinimumLength(MinimumQueryLength)
            .MaximumLength(MaximumQueryLength)
            .WithMessage(
                $"A search query is {MinimumQueryLength} to {MaximumQueryLength} characters.");

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, MaximumPageSize)
            .WithMessage($"pageSize is 1 to {MaximumPageSize}.");
    }
}
