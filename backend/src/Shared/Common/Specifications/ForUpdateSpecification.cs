namespace Shared.Common.Specifications;

/// <summary>
/// Marks a composed query as tracked so the handler can mutate entities.
/// Combine with filter/include specs via <c>.And(ForUpdateSpecification&lt;T&gt;.Instance)</c>.
/// Combined <see cref="ISpecification{T}.AsNoTracking"/> is AND of both sides, so this flips tracking on.
/// </summary>
public sealed class ForUpdateSpecification<T> : SpecificationBase<T> where T : class
{
    private ForUpdateSpecification() => AsNoTracking = false;

    public static readonly ISpecification<T> Instance = new ForUpdateSpecification<T>();
}
