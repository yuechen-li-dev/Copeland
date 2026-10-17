# TableScript M14: typed editing

The stock and unit-price editors now keep raw draft text in ordinary Copeland
records and invoke application code only after successful parsing. The same
reducer executes on C# and JavaScript. Frontend editing no longer requires a
second draft in the React control, an effect to synchronize numeric props, or a
generation counter to force Reset.

## Reusable library and ownership

[`Copeland.Editing`](../../../libraries/Copeland.Editing/README.md) is ordinary
Copeland source with `Draft<T, Error>`, `beginDraft`, `editDraft`, `revealDraft`,
`cancelDraft`, `isDirty`, and integer/cents conversion functions. Generic source
is included by the consumer and specializes through the existing compiler. Its
MSBuild project also produces a normal .NET assembly whose codecs and typed
Results are consumed by a C# test through a real project reference. This does not
claim a published package or emitted CLR generic draft API.

```typescript
const stock: Draft<int, ParseError> = editDraft<int, ParseError>(state.stock, text, parseIntText);
return match stock.value {
    err(error) => state with { stock, stockError: None },
    ok(units) => match applyStockEdit(state.view, state.selected.productId, units) {
        err(error) => state with { stock, stockError: Some(error) },
        ok(view) => state with {
            view, selected: editableProduct(view, state.selected.productId)!,
            stock: beginDraft<int, ParseError>(units, formatInt), stockError: None,
        },
    },
};
```

The actual Apply handler first reveals feedback. Drafts contain ordinary immutable
data rather than an opaque proof: their parsed value establishes numeric syntax,
not business validity. A successful domain Result causes one accepted-view update
and rebases only the applied field. The other field's unsaved text survives.

The sample includes M12's exact workbook source and M13's exact application source
as logical modules. `PriceApplication.ts` adds only price validation and a keyed
product projection. The café permits 0..1000000 cents/unit; the parser knows
nothing about this rule. Tables, joins, calculation, summaries, stock rejection,
and immutable snapshot replacement stay in their application owners.

The foreign `TextField` accepts string value, callback, input mode, label, and
accessibility state. It forwards all text unchanged. `PreviewForm` adapts native
submit, including Enter, into its typed callback. Neither component calculates
prices or rejects text using native numeric validity.

## Explicit editing behavior

| Action | Result |
| --- | --- |
| Type empty text, `-`, or `1.` | Preserve text and typed parse failure. Accepted rows and totals remain intact. |
| Apply malformed text | Reveal field-specific parse feedback; never submit the previous valid number. |
| Apply syntactically valid negative stock/price | Match a separate domain error; preserve the accepted snapshot. |
| Apply valid value | Recalculate through the application, normalize this field's accepted text, retain the other draft. |
| Cancel | Restore this field's latest accepted value and clear its feedback. |
| Select a product | Discard both pending drafts and load the latest accepted keyed values. This policy is stated in the UI. |
| Reset preview | Restore all original data and both canonical drafts, even when accepted values had never changed. |

Dirty means current text differs from accepted canonical text. `+0005.0` is a
valid unapplied edit; successful application canonicalizes it to `5.00`.

Numeric grammars are ASCII whole integers and decimal currency with up to two
places, stored as signed 32-bit units/cents. No whitespace trimming, grouping,
locale symbols, exponent conversion, binary floating intermediate, or rounding
is involved. The parser reports typed Empty, Incomplete, InvalidCharacter,
FractionTooLong, or OutOfRange errors. Signed extrema, including the asymmetric
negative bound, are exercised under both backends. Full policy is documented
with the library.

## Compiler fixes exposed by the consumer

Two modules independently using an imported `Draft<int, ParseError>` previously
received different nominal instances, producing `COPE-REC-0015` with identical
type names. Closed record instances now belong to their shared generic
definition within the project. Unrelated declarations remain nominal; a negative
regression checks two distinct `Box<T>` definitions.

Calling the same imported generic function from two modules also produced
duplicate MIR function identities. Specialization emission names now include the
consuming module in their deterministic hash. Each ESM module retains its own
specialized body and dependencies, while record carriers retain their definition
identity. A three-module regression executes both copies through C# and every
JavaScript profile. No spelling-based or structural relaxation was introduced.

## Observed results

| Accepted scenario | Cents | Units | Orders |
| --- | ---: | ---: | ---: |
| Original café | 31210 | 67 | 3 |
| Product 100 stock: 4 → 12 | 12710 | 47 | 2 |
| Then product 102 unit price: 3.10 → 5.00 | 15750 | 47 | 2 |
| Product 102 unit price: 0 | 7750 | 47 | 2 |
| Product 102 unit price: 10000.00 | 16007750 | 47 | 2 |
| Reset | 31210 | 67 | 3 |

Invalid input and refused domain edits leave accepted rows intact. Application of
one field preserves the other incomplete draft. Switching away and back restores
the latest accepted keyed values. The canonical global workbook remains 31210
cents throughout.

React StrictMode workflows pass at 1280 and 390 pixels in Diagnostic, Symbolic,
and Production. They exercise empty/incomplete/invalid text, domain rejection,
normalization, zero/maximum prices, Enter, cancellation, selection and reset.
There are no console warnings, page errors, or failed HTTP responses. Feedback
description IDs resolve, table scrolling is keyboard accessible, and the page
does not overflow horizontally. Desktop and mobile screenshots were inspected.

## Reproduction and evidence

```powershell
./samples/copeland-ts/tablescript-editing-m14/run.ps1
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx --no-build -m:1 --nologo
```

The sample runner checks C# application execution, emits all three ESM/layout
profiles, runs strict TypeScript checking for host tooling, bundles, and executes
six browser workflows. The solution tests additionally exercise 36 numeric input
vectors, 16 draft/format observations, nominal import rejection, actual .NET
library consumption and the café reducer on C#/JavaScript.

Evidence: [source/emission hashes](evidence/tablescript-editing-m14/compiler-report.json),
[browser observations](evidence/tablescript-editing-m14/browser-report.json), and
[validation summary](evidence/tablescript-editing-m14/validation-report.json).
Generated code, bundles and screenshots remain in the sample's ignored
`artifacts/` and `dist/` directories.

- Full solution build: zero warnings and errors.
- Full solution tests: 2446 passed, zero failures or skips, including four new
  editing/library tests.
- Original café qualification: 66 checks and its managed consumer assertions
  pass; baseline/scenario totals remain 31210/12710 through the canonical
  `csharp-relation-plan` executor.
- Strict host TypeScript checking, C# reducer execution, and all six browser
  workflows pass. Frozen Zig lanes are skipped: no legacy Zig code changed.

## Remaining friction and boundaries

Selection intentionally discards pending drafts; retaining per-row work or asking
about navigation would be a separate product policy. Generic calls still have
explicit type arguments in several places. This milestone improves correctness
without adding syntax to shorten them.

This remains a local preview. A persistence endpoint must accept typed commands
and revalidate against its own inputs. Server commit/conflict handling, variable
row cardinality, general form schemas, locale-aware money input, IME behavior,
Native AOT performance and large-table performance are not qualified here.
Transparent aliases do not become currency or range proofs.
