import { FormatBoolean, NugetError, Ordinalize, Quote } from "./generated/Nuget";

function DescribeError(error: NugetError): string {
    if (error.message.length > 0) {
        return "typed error";
    }
    return "missing error";
}

export async function Main(): string {
    const pendingQuote = Quote("Ship the docs");
    const pendingBoolean = FormatBoolean(true);
    const pendingOrdinal = Ordinalize("n/a");
    const quoted: string ! NugetError = await pendingQuote;
    const formatted: string ! NugetError = await pendingBoolean;
    const ordinal: string ! NugetError = await pendingOrdinal;

    const text: string = match quoted {
        ok(value) => value,
        err(error) => error.message,
    };
    const flag: string = match formatted {
        ok(value) => value,
        err(error) => error.message,
    };
    const checked: string = match ordinal {
        ok(value) => value,
        err(error) => DescribeError(error),
    };
    return text + " | " + flag + " | " + checked;
}
