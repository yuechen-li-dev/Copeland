export type CopelandProfile = "JavaScript";
record Value { value: int; }
export function answer(): int {
    const data: Value = { value: 42 };
    return data.value;
}
