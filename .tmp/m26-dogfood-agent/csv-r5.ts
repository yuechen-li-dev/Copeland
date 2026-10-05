using System;

function parseCsvItem(part: string): int {
    return Int32.Parse(part);
}

function parseCsvIntegers(text: string): int[] {
    const parts = String.Split(text, ",");
    return batch parts as part {
        return parseCsvItem(part);
    };
}
