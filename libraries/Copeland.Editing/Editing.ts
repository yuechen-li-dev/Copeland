export type CopelandProfile = "Copeland";

// A draft is frontend data. Accept it only after application validation succeeds.
export record Draft<T, Error> {
    accepted: T;
    acceptedText: string;
    text: string;
    value: T ! Error;
    revealErrors: boolean;
}

export function beginDraft<T, Error>(value: T, format: (value: T) => string): Draft<T, Error> {
    const text: string = format(value);
    return { accepted: value, acceptedText: text, text, value: ok(value), revealErrors: false };
}

export function editDraft<T, Error>(draft: Draft<T, Error>, text: string, parse: (text: string) => T ! Error): Draft<T, Error> {
    return draft with { text, value: parse(text) };
}

export function revealDraft<T, Error>(draft: Draft<T, Error>): Draft<T, Error> {
    return draft with { revealErrors: true };
}

export function cancelDraft<T, Error>(draft: Draft<T, Error>): Draft<T, Error> {
    return draft with { text: draft.acceptedText, value: ok(draft.accepted), revealErrors: false };
}

export function isDirty<T, Error>(draft: Draft<T, Error>): boolean {
    return draft.text != draft.acceptedText;
}

export enum ParseError {
    Empty,
    Incomplete,
    InvalidCharacter(position: int),
    FractionTooLong(places: int),
    OutOfRange,
}

// ASCII decimal grammar only: no whitespace, grouping, exponent, or rounding.
// Accumulate negatively so int.MinValue never requires a positive magnitude.
function parseFixed(text: string, places: int): int ! ParseError {
    if (text.length == 0) {
        return err(ParseError.Empty);
    }
    let index: int = 0;
    let negative: boolean = false;
    const first: int = String.CodeAt(text, 0);
    if (first == 43 || first == 45) {
        negative = first == 45;
        index = 1;
    }
    if (index == text.length) {
        return err(ParseError.Incomplete);
    }
    let value: int = 0;
    let wholeDigits: int = 0;
    let fractionDigits: int = 0;
    let fraction: boolean = false;
    for (; index < text.length; index = index + 1) {
        const code: int = String.CodeAt(text, index);
        if (code == 46 && places > 0 && !fraction && wholeDigits > 0) {
            fraction = true;
        } else {
            if (code < 48 || code > 57) {
                return err(ParseError.InvalidCharacter(index));
            }
            if (fraction) {
                fractionDigits = fractionDigits + 1;
                if (fractionDigits > places) {
                    return err(ParseError.FractionTooLong(places));
                }
            } else {
                wholeDigits = wholeDigits + 1;
            }
            const digit: int = code - 48;
            let limit: int = 7;
            if (negative) {
                limit = 8;
            }
            if (value < -214748364 || (value == -214748364 && digit > limit)) {
                return err(ParseError.OutOfRange);
            }
            value = value * 10 - digit;
        }
    }
    if (fraction && fractionDigits == 0) {
        return err(ParseError.Incomplete);
    }
    for (let padding: int = fractionDigits; padding < places; padding = padding + 1) {
        if (value < -214748364) {
            return err(ParseError.OutOfRange);
        }
        value = value * 10;
    }
    if (negative) {
        return ok(value);
    }
    return ok(-value);
}

export function parseIntText(text: string): int ! ParseError {
    return parseFixed(text, 0);
}

export function parseCentsText(text: string): int ! ParseError {
    return parseFixed(text, 2);
}

export function formatInt(value: int): string {
    return String.From(value);
}

export function formatCents(value: int): string {
    let whole: int = value / 100;
    let remainder: int = value % 100;
    let sign: string = "";
    if (value < 0) {
        sign = "-";
        whole = -whole;
        remainder = -remainder;
    }
    let padding: string = "";
    if (remainder < 10) {
        padding = "0";
    }
    return sign + String.From(whole) + "." + padding + String.From(remainder);
}
