using System;
using System.Text.RegularExpressions;
function parseCsvIntegers(text: string): int[] {
    const parts = Regex.Split(text, ",");
    return batch parts as part {
        const trimmed = Regex.Replace(part, "^\\s+|\\s+$", "");
        return Int32.Parse(trimmed);
    };
}