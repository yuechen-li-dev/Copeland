import { Parse } from "example/parser";
using Example.Runtime;

export function Run(): int {
    const parsed = Parse("hello");
    return parsed + RuntimeMarker.Value;
}
