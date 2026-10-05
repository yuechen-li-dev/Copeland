using System;
function collectEvenValues(values: int[]): int[] {
    return Array.FindAll(values, (value: int) => value % 2 == 0);
}