using System.Collections;

namespace Copeland.TS.Mir;

/// <summary>Compact initializer segments with an ordinary logical-cell view.</summary>
public sealed class InitializerSequence<T> : IReadOnlyList<T>
{
    public InitializerSequence(IReadOnlyList<T> values, IReadOnlyList<int> counts)
    {
        if (values.Count != counts.Count || counts.Any(count => count < 0))
        {
            throw new ArgumentException("Initializer values and nonnegative counts must align.");
        }
        Values = values;
        Counts = counts;
        Count = counts.Aggregate(0, (length, count) => checked(length + count));
    }

    public IReadOnlyList<T> Values { get; }
    public IReadOnlyList<int> Counts { get; }
    public int Count { get; }

    public int SegmentAt(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        for (int segment = 0; segment < Counts.Count; segment++)
        {
            if (index < Counts[segment])
            {
                return segment;
            }
            index -= Counts[segment];
        }
        throw new InvalidOperationException("Initializer segment mapping is inconsistent.");
    }

    public T this[int index] => Values[SegmentAt(index)];

    public InitializerSequence<TResult> Map<TResult>(Func<T, TResult> transform)
    {
        return new InitializerSequence<TResult>(Values.Select(transform).ToArray(), Counts);
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int segment = 0; segment < Values.Count; segment++)
        {
            for (int occurrence = 0; occurrence < Counts[segment]; occurrence++)
            {
                yield return Values[segment];
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
