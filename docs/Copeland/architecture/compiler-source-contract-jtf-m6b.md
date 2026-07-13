# Compiler source contract — JTF-M6b

## Decision

JTF-M6b rejects extraction for now. No `Copeland.Compiler.Source` project, production project reference, public API, package, or consumer migration was added.

The test-local probe at `tests/Aurelian/Aurelian.Shaders.Tests/CompilerSourceContractSpikeTests.cs` measured the active Markdown, Copeland Script, and SDSL-V implementations. All three use ordinary .NET string offsets (UTF-16 code units), but fewer than two consumers currently share the complete source-text contract required for graduation.

## Observed contracts before extraction

| Topic | Markdown | Copeland Script | Active SDSL-V |
| --- | --- | --- | --- |
| Source holder | Public immutable `MarkdownSourceText` retains a `string`. | `SyntaxTree.Text` and `Lexer` carry a raw `string`; no source object. | `SdslvLexer.Lex(string)` scans a raw `string`; no retained source object. |
| Offset basis | Zero-based UTF-16 index. | Zero-based UTF-16 token and diagnostic positions. | Zero-based UTF-16 `Start`/`End` offsets. |
| Line/column basis | One-based line and column, cached line starts. | No line/column API. | One-based token-start line and column while scanning. |
| Newlines | CR, LF, and CRLF. CRLF is one line break. | Whitespace is skipped but no location mapping is exposed. | LF only. A CR increments the column; CRLF advances at LF. |
| EOF | `GetLocation(Length)` is valid; a trailing recognized newline produces an empty final line. | EOF token position is `text.Length`. | EOF token has `Start == End == source.Length` and the scanner's current line/column. |
| Invalid offsets | `GetLocation` rejects values below zero and above length. | No arbitrary-offset location query. | No arbitrary-offset location query. |
| Spans | `CreateSpan` validates a source-bound start/length and returns locations; record construction itself is unconstrained. | Tokens/diagnostics carry unconstrained position/length integers. | `SdslvSpan(Start, End, Line, Column)` is unconstrained and also represents `Unknown`. |
| Map/lifetime | Eager cached line-start array; immutable and safe for reads. | No line map or source lifetime abstraction. | One-shot mutable scanner counters; no reusable map. |

The probe covers empty/ordinary text; LF, CRLF, bare CR, mixed, leading, consecutive, and trailing newlines; the empty final line; every valid offset including EOF; invalid Markdown offsets; ASCII, tabs, BMP Unicode, and surrogate pairs; zero/full/EOF spans; negative and source-exceeding spans; and overflow-sized values.

## Existing diagnostic, dump, and corpus observations

Markdown diagnostics and `DocumentMir` diagnostics retain `SourceSpan`, and `MarkdownDumpWriter` prints one-based `line:column` locations for token and diagnostic dumps exposed by the Markdown CLI. Changing its newline mapping would therefore change observable diagnostics and deterministic dump/corpus output.

Script diagnostics are `Position`/`Length` values only. The Script CLI prints diagnostic id/message without positions, while lexer/parser/binder corpus dump writers serialize the raw offsets and lengths. Its corpus reader normalizes CRLF to LF before lexing, so corpus expectations are not evidence of a general line-map contract.

SDSL-V diagnostics, shader manifests, and artifact JSON retain zero-based start/end offsets plus one-based scanner line/column. Those line/column values are observable in artifact output. The legacy shader lexer independently has the same LF-only line advance rule. Neither shader path has a reusable source-text map.

## Exact measured semantics

Offsets and columns are UTF-16 code units. A tab advances a column by one and a surrogate pair consumes two columns. This is not Unicode-scalar or grapheme-cluster accounting.

For Markdown, `"a\\r\\nb"` maps offsets 1 (`\\r`) and 2 (`\\n`) to line 1, columns 2 and 3, and offset 3 to line 2, column 1. `"a\\rb"` maps offset 2 to line 2, column 1. SDSL-V agrees with the CRLF observation but maps that bare-CR offset to line 1, column 3 because only LF advances its line counter.

Markdown's produced spans are conventional half-open ranges: `Start` plus `Length`, with `End` at the exclusive boundary. Zero-length spans and spans ending at EOF are accepted through `CreateSpan`. Its public `SourceSpan` record has no structural validation, and the current factory does not provide a checked-overflow structural contract; an overflow-sized request eventually fails through location validation. Script and SDSL-V do not provide a matching validated source-bound span operation. Therefore no shared span type graduated.

## Compatibility decision

| Required agreement | Result |
| --- | --- |
| UTF-16 offset basis | All three agree. |
| Line and column basis | Markdown and SDSL-V agree only for LF/CRLF; Script has no mapping. |
| Bare-CR recognition | Markdown and SDSL-V disagree. |
| EOF behavior | Each has an EOF representation, but only Markdown offers an arbitrary offset query and empty-final-line semantics. |
| Invalid-offset behavior | Only Markdown specifies it. |
| Empty-line behavior | Only Markdown caches/specifies it. |
| Span semantics | No two lanes share structural and source-bound validation semantics. |
| Source lifetime/performance | Only Markdown has a reusable immutable indexed source object. |

The shared raw-string/UTF-16-offset subset is too small to justify an indexed source-text SDK. Treating Script's absence of a line-map contract as agreement would invent requirements, and treating SDSL-V's CR behavior as an option would contaminate the proposed primitive with consumer policy.

## Rejected additions and next prerequisite

M6b deliberately adds no source identity, paths, encoding, file I/O, mutable edits, versioning, diagnostics, tokens, parsers, IR, backends, or consumer-specific newline switches. Cope MIR, VD-MIR, DocumentMir, diagnostics, CLI dumps, corpus formats, generated C#, HLSL/DXC/SPIR-V, and presenter lowering are unchanged.

Before revisiting `Copeland.Compiler.Source`, a second lane must independently need and test the full Markdown-compatible contract: immutable retained text, CR/LF/CRLF line starts, one-based UTF-16 locations at every offset including EOF, and explicitly checked half-open source-bound spans. The smallest credible prerequisite is a Script-local requirement for user-facing line/column diagnostics with those semantics; it should remain local until a second concrete consumer also needs it. Altering SDSL-V's bare-CR semantics solely to enable extraction is not a prerequisite.

Status after M6c: the proposed Cope Test source dialect was superseded; TSPack owns `*.xtest.tsx`, while `.cope` is reserved for Cope MIR text. Reconsider source infrastructure only when any two real lanes independently need compatible indexed-source behavior. Markdown's current indexed-source contract is not normative for another lane.
