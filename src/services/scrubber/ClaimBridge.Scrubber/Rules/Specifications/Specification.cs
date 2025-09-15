namespace ClaimBridge.Scrubber.Rules.Specifications;

public interface ISpecification<in T>
{
    bool IsSatisfiedBy(T candidate);
}

public static class Spec
{
    public static ISpecification<T> Where<T>(Func<T, bool> predicate, string description) => new LambdaSpec<T>(predicate, description);
    public static ISpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right) => new AllSpec<T>([left, right]);
    public static ISpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right) => new AnySpec<T>([left, right]);
    public static ISpecification<T> Not<T>(this ISpecification<T> inner) => new NotSpec<T>(inner);
    public static ISpecification<T> All<T>(IEnumerable<ISpecification<T>> specs) => new AllSpec<T>([.. specs]);
    public static ISpecification<T> Any<T>(IEnumerable<ISpecification<T>> specs) => new AnySpec<T>([.. specs]);
}

internal sealed record LambdaSpec<T>(Func<T, bool> Predicate, string Description) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) => Predicate(candidate);
    public override string ToString() => Description;
}

internal sealed record AllSpec<T>(ISpecification<T>[] Specs) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) => Specs.All(s => s.IsSatisfiedBy(candidate));
    public override string ToString() => "(" + string.Join(" AND ", Specs.Select(s => s.ToString())) + ")";
}

internal sealed record AnySpec<T>(ISpecification<T>[] Specs) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) => Specs.Any(s => s.IsSatisfiedBy(candidate));
    public override string ToString() => "(" + string.Join(" OR ", Specs.Select(s => s.ToString())) + ")";
}

internal sealed record NotSpec<T>(ISpecification<T> Inner) : ISpecification<T>
{
    public bool IsSatisfiedBy(T candidate) => !Inner.IsSatisfiedBy(candidate);
    public override string ToString() => "NOT " + Inner;
}
