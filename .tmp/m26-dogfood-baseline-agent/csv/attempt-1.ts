import { Regex } from "System.Text.RegularExpressions";
import { Int32 } from "System";
function parseCsvIntegers(text: string): int[] {
    const parts = Regex.Split(text, ",");
    return batch parts as part {
        const trimmed = Regex.Replace(part, "^\\s+|\\s+$", "");
        return Int32.Parse(trimmed);
    };
}