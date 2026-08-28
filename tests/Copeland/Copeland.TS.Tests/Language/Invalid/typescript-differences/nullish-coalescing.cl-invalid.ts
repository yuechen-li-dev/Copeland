// expect: COPE-OPTION-0006
function main(): string { const value: string = "fallback"; return value ?? "other"; }
