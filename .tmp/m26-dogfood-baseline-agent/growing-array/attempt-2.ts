using System.Linq;
function collectEvenValues(values: int[]): int[] {
    return Enumerable.ToArray(Enumerable.Where(values, (value: int) => value % 2 == 0));
}