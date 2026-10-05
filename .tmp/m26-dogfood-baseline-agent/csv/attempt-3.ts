using System;
function parseCsvIntegers(text: string): int[] {
    const parts = String.Split(text, ",");
    return batch parts as part {
        return Int32.Parse(String.Trim(part));
    };
}