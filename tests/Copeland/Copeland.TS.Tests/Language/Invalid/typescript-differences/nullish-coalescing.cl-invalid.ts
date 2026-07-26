// expect: COPE-PROFILE-0013
function main(): string { const value: string = "fallback"; return value ?? "other"; }
