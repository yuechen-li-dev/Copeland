# Copeland.Editing

Ordinary Copeland source for immutable frontend drafts and explicit numeric text
conversion. It adds no grammar, renderer, state runtime, or validation schema.

Include `Editing.ts` as a source module in a Copeland project and import its public
exports. The café consumer maps it to logical `src/Editing.ts`. Generic functions
and records specialize when the consumer compiles; this is source reuse rather
than a published package or a CLR generic binary API.

```typescript
const draft: Draft<int, ParseError> = beginDraft<int, ParseError>(4, formatInt);
const changed: Draft<int, ParseError> = editDraft<int, ParseError>(draft, "-", parseIntText);
const revealed: Draft<int, ParseError> = revealDraft<int, ParseError>(changed);
const cancelled: Draft<int, ParseError> = cancelDraft<int, ParseError>(revealed);
```

`Draft<T, Error>` holds accepted value/text, current text, its `T ! Error` parse
result, and an explicit error-reveal flag. It uses ordinary records; it does not
claim opaque construction or domain validity. `isDirty` compares text with the
accepted canonical text, so `"04"` is dirty against accepted `4` until applied.

An edit preserves all text, including incomplete input. Reveal on attempted
Apply, match the parse result, then invoke an application function with its own
typed domain errors. Only after that function succeeds should the frontend
rebase with `beginDraft(acceptedValue, formatter)`. Cancel restores accepted text
and hides parse feedback. Selection and whole-editor Reset are application
decisions; the sample deliberately discards pending drafts on selection.

Numeric policy:

| Operation | Accepted grammar and representation |
| --- | --- |
| `parseIntText` | ASCII `[+-]?[0-9]+`, signed 32-bit integer. |
| `parseCentsText` | ASCII `[+-]?[0-9]+(\.[0-9]{1,2})?`, signed 32-bit integer cents. No rounding. |
| `formatInt` | Invariant whole integer text. |
| `formatCents` | Invariant decimal text with exactly two places, including `int.MinValue`. |

Empty input, incomplete signs/trailing decimal points, invalid character index
(zero-based UTF-16), excess fractional digits, and overflow have distinct
`ParseError` cases. There is no trimming, locale punctuation, exponent notation,
floating conversion, or leading decimal shorthand. Negative values parse
successfully; nonnegative stock and price limits belong to application code.

The parser reports the first failure encountered from left to right; it does not
promise a list of every defect in an input. Cents range is -21474836.48 through
21474836.47; the café applies its own narrower price policy.

```powershell
dotnet build libraries/Copeland.Editing/Copeland.Editing.csproj -m:1
```

The MSBuild project emits a normal .NET assembly. Its non-generic codecs and typed
Results/ParseError are directly callable from C# as
`Copeland.Libraries.Copeland.Editing.parseCentsText(...)`. The solution test suite
checks that actual project reference, source-level drafts across module imports,
and execution parity with all three JavaScript profiles. Native AOT performance
or a package distribution contract is not qualified by this milestone.
