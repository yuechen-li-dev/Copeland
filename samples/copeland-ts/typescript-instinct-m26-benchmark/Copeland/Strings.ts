using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

export function runStrings(rounds: int, count: int): int {
    let total: int = 0;
    for (let round: int = 0; round < rounds; round = round + 1) {
        const builder: StringBuilder = new StringBuilder();
        for (let i: int = 0; i < count; i = i + 1) {
            if (i > 0) {
                builder.Append(",");
            }
            builder.Append(`item${i}`);
        }
        const joined: string = builder.ToString();
        const back: string[] = Regex.Split(joined, ",");
        let hash: int = 0;
        for (let i: int = 0; i < joined.length; i = i + 1) {
            hash = hash * 31 + Char.ConvertToUtf32(joined, i);
        }
        const culture: CultureInfo = CultureInfo.InvariantCulture;
        const compare: CompareInfo = culture.CompareInfo;
        const at: int = compare.IndexOf(joined, "item99999");
        total = total + back.length + hash % 256 + at;
    }
    return total;
}
