using ClaimBridge.Gateway.Composition;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClaimBridge.Gateway.Endpoints;

public sealed record ClaimDetailsRequest(Guid Id, string? Include)
{
    public IReadOnlySet<string> Sections =>
        string.IsNullOrWhiteSpace(Include)
            ? Sources.Optional.ToHashSet()
            : Include.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
}

public sealed class ClaimDetailsRequestValidator : AbstractValidator<ClaimDetailsRequest>
{
    public ClaimDetailsRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Include)
            .Must(i => i is null || i.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(Sources.Optional.Contains))
            .WithMessage($"include may only list: {string.Join(", ", Sources.Optional)}");
    }
}

public static class ClaimDetailsEndpoint
{
    public static async Task<Results<Ok<ClaimDetailsResponse>, NotFound, ProblemHttpResult>> Handle(
        [AsParameters] ClaimDetailsRequest request, ClaimDetailsComposer composer, HttpContext http, CancellationToken ct)
    {
        var bearer = http.Request.Headers.Authorization.ToString() is { Length: > 7 } h && h.StartsWith("Bearer ") ? h[7..] : null;
        var result = await composer.ComposeAsync(request.Id, request.Sections, bearer, ct);
        return result.Outcome switch
        {
            CompositionOutcome.Ok => TypedResults.Ok(result.Response!),
            CompositionOutcome.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.Error, title: "Claim details unavailable", statusCode: StatusCodes.Status502BadGateway)
        };
    }
}
