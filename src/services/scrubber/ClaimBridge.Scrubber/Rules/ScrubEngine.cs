namespace ClaimBridge.Scrubber.Rules;

public sealed class ScrubEngine(IEnumerable<IScrubRule> builtInRules)
{
    private readonly IReadOnlyList<IScrubRule> _builtIn = [.. builtInRules.OrderBy(r => r.Code)];

    public IReadOnlyList<IScrubRule> BuiltInRules => _builtIn;

    public ScrubResult Run(ScrubContext context, IReadOnlyList<IScrubRule>? payerRules = null)
    {
        var rules = payerRules is null ? _builtIn : [.. _builtIn, .. payerRules];
        var edits = rules.SelectMany(r => r.Evaluate(context))
            .OrderByDescending(e => e.Severity)
            .ThenBy(e => e.LineNumber ?? 0)
            .ThenBy(e => e.RuleCode)
            .ToList();
        return new ScrubResult(edits.All(e => e.Severity != Severity.Error), edits, rules.Count);
    }
}

public static class ScrubRuleRegistration
{
    public static IServiceCollection AddBuiltInScrubRules(this IServiceCollection services)
    {
        foreach (var type in typeof(IScrubRule).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IScrubRule).IsAssignableFrom(t)
                                 && t.Namespace?.EndsWith(".BuiltIn") == true))
            services.AddSingleton(typeof(IScrubRule), type);
        services.AddSingleton<ScrubEngine>();
        return services;
    }
}
