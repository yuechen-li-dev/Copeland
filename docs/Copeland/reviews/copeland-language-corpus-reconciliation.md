# Copeland language corpus reconciliation

`tests/Copeland/Copeland.TS.Tests/Language/Valid` and `Language/Invalid` describe current Copeland language law, not historical milestone boundaries. Fixture suffixes remain the verdict authority; topical folders are organization only.

## Audit result

The current compiler was applied to every convention-named fixture through `LanguageFixtureTests`. Valid fixtures must parse, bind, and lower to ordinary MIR without diagnostics. Invalid fixtures must fail semantically, must not fail merely in lexer/parser recovery, and use their focused filename as the named law; high-value diagnostic ownership is pinned with `// expect: COPE-...`. Representative Option, class, generic table, immutable-record `with`, and tagged-data fixtures now emit through both the C# and JavaScript backends.

Counts include the TSXML fixtures held directly under `Language/tsx`:

| Verdict | Before | After |
|---|---:|---:|
| Valid | 67 | 68 |
| Invalid | 153 | 154 |

One stale name was reconciled without changing its verdict: `Invalid/typescript-differences/nullish-coalescing.cl-invalid.ts` moved to `Invalid/absence/coalescing-requires-option.cl-invalid.ts`. The fixture was never evidence that `??` itself is forbidden; it proves that a plain `string` is not an admissible left operand.

Two focused specimens were added:

- `Valid/absence/option-chaining-and-coalescing.cl-valid.ts` protects the intentional Option law.
- `Invalid/absence/optional-chaining-requires-option.cl-invalid.ts` pins `COPE-OPTION-0005`.

No fixture became invalid intentionally, no obsolete fixture was deleted, and the executable audit found no language regression.

## Absence law

`Option<T>` is a compiler-owned closed payload enum with `Some(T)` and `None` cases. `left ?? fallback` is not JavaScript nullish coalescing: the binder requires `left: Option<T>`, binds `fallback: T`, and produces a two-arm Option match whose `Some` arm returns the payload and whose `None` arm evaluates the fallback.

`receiver?.member` similarly requires `receiver: Option<T>` and produces an Option match. A non-Option projection is wrapped in `Some`; a projected Option is flattened by one layer. `null` and `undefined` remain excluded language values and retain explicit negative fixtures.

## Other audited law

The corpus and focused suites currently cover immutable inferred object records, `with` replacement without field addition, nominal records and unions, Results and exhaustive match, pure class construction without JavaScript `new`, generic requirements, record tables, TSON, fallibility, async/generator/batch profiles, FLOW purity, template constraint forwarding, and CLR/npm interop boundaries. FLOW purity and template forwarding remain owned by their focused semantic suites because they require multi-source or template execution context; duplicating them as weaker single-file corpus snippets would reduce diagnostic ownership.

The machine-readable per-fixture inventory is `artifacts/repo-hygiene/language-corpus-audit.json`.
