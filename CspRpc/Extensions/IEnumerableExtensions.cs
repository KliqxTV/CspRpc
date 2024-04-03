namespace CspRpc.Extensions;

internal static class IEnumerableExtensions
{
    internal static IEnumerable<TResult> Cross<TSource, TOther, TResult>(this IEnumerable<TSource> first, IEnumerable<TOther> second, Func<TSource, TOther, TResult> resultSelector)
    {
        foreach (var f in first)
        {
            foreach (var s in second)
            {
                yield return resultSelector(f, s);
            }
        }
    }
    internal static IEnumerable<TResult> Cross<TSource, TOther1, TOther2, TResult>(this IEnumerable<TSource> first, IEnumerable<TOther1> second, IEnumerable<TOther2> third, Func<TSource, TOther1, TOther2, TResult> resultSelector)
    {
        foreach (var f in first)
        {
            foreach (var s in second)
            {
                foreach (var t in third)
                {
                    yield return resultSelector(f, s, t);
                }
            }
        }
    }
}
