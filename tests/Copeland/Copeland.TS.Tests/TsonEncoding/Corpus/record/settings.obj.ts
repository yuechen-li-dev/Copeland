const $schema: string = "copeland://corpus/runtime-encoding";

record Detail { label: string; }
enum Mode { Off, Named(detail: Detail), }
record Settings { enabled: boolean; count: number; mode: Mode; }

// Canonical runtime encoding deliberately drops this comment and layout.
const $value: Settings = {
    mode: Mode.Named({ label: "snow 雪 😀" }),
    count: $number("8000000000000000"),
    enabled: true,
};
