const $schema: string = "copeland://sample/nuget-json";

record QuoteRequest {
    value: string;
}

record QuoteResponse {
    json: string;
}

record PackageError {
    message: string;
}

export async function Main(): string {
    const request: QuoteRequest = { value: "Ship the docs" };
    const pending: Async<QuoteResponse ! PackageError> = tsonCall<QuoteResponse, PackageError>("json:quote", request);
    const reply: QuoteResponse ! PackageError = await pending;
    return match reply {
        ok(value) => value.json,
        err(error) => error.message,
    };
}
