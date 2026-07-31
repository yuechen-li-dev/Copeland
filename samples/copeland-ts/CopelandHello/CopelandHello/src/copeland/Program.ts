using System;
using CopelandHello;

import { camelCase } from "lodash-es";
import { normalizeName } from "./Greeting";

const $schema: string = "copeland://preview/hello";

record NpmError {
    message: string;
}

export function dotNetGreeting(name: string): string {
    return String.Concat(Helper.Decorate(normalizeName(name)), " through System.String");
}

export async function npmGreeting(value: string): string ! NpmError {
    const pending: Async<string ! NpmError> = camelCase(value);
    return await pending;
}
