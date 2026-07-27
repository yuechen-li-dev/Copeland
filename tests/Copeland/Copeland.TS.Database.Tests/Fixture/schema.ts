const $schema: string = "copeland://experimental/events/v1";

export record Event {
    tenant: string;
    year: int;
    value: number;
}
