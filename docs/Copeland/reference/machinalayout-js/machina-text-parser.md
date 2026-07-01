# MachinaText Parser

MachinaText supports a narrow inline syntax: emphasis, strong, code spans, links, and bullets.

## Escapes (M3a)

Supported escapes in normal inline text:

- `\\` => `\`
- `\*` => `*`
- ``\` `` => `` ` ``
- `\[` => `[`
- `\]` => `]`
- `\(` => `(`
- `\)` => `)`
- `\-` => `-`

Escapes are processed before inline marker detection, so escaped markers stay literal (example: `\*not emphasis\*`).

Unsupported escapes (example: `\q`) emit diagnostic code `invalid_escape` and preserve visible content as literal text.

A trailing dangling backslash also emits `invalid_escape` and preserves a literal backslash.

Code spans do not process escapes. Content inside backticks is taken literally.
