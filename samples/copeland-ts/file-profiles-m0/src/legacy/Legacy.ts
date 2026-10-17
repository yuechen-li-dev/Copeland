export type CopelandProfile = "Legacy";

// Deliberately ordinary TypeScript semantics, outside the Copeland language.
const response: any = { value: 42 };
export const answer: number = response?.value ?? 0;
console.log(`PROFILE_LEGACY_PASSED answer=${answer}`);
