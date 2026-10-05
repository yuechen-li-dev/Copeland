using System;
function collectEvenValues(values: int[]): int[] {
    return Array.FindAll<int>(values, (value: int) => value % 2 == 0);
}