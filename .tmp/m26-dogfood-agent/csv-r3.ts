using System;

function parseCsvItem(part: string): int {
    return Int32.Parse(String.Trim(part));
}

function parseCsvIntegers(text: string): int[] {
    const parts = String.Split(text, ",");
    return batch parts as part {
        return parseCsvItem(part);
    };
}
