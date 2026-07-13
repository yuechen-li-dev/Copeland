# JTF-M6b — compiler source contract spike

## Result

M6b is an evidence-driven rejection of shared production source infrastructure. The three compiler lanes were measured through test-local adapters; their common raw UTF-16 string-offset behavior did not establish two compatible source-text contracts. `Copeland.Compiler.Source` was not created.

## Probe and behavior matrix

The probe is `CompilerSourceContractSpikeTests` in `Aurelian.Shaders.Tests`. It uses `MarkdownSourceText` directly, observes Script offsets by injecting an invalid token into `SyntaxTree.ParseTokens`, and observes SDSL-V scanner locations by injecting an invalid token into `SdslvLexer.Lex`. It covers every valid offset, including newline-character and EOF offsets, for empty, ordinary, LF, CRLF, bare-CR, mixed, leading, consecutive-empty-line, trailing-newline, ASCII, tab, BMP Unicode, and surrogate-pair inputs.

| Lane | Contract result |
| --- | --- |
| Markdown | Immutable public source holder with eager cached line starts; CR/LF/CRLF; one-based UTF-16 locations; valid EOF and empty-final-line locations; explicit invalid-offset and source-bound span rejection. |
| Copeland Script | Raw immutable strings and zero-based UTF-16 token/diagnostic offsets. No source-location query, line-map lifetime, span validator, or public newline policy exists. |
| SDSL-V | Raw strings scanned with one-based line/column counters and zero-based half-open token offsets. LF increments line; CR merely increments column. The public span record accepts invalid values and represents an unknown location. |

Markdown and SDSL-V agree for LF and CRLF positions—including `\\r` and `\\n` inside CRLF—but disagree for bare CR. Script cannot supply the absent semantics as agreement. Tabs and surrogate pairs confirm ordinary UTF-16 code-unit columns, not scalar-value or grapheme columns.

Markdown CLI token/diagnostic and corpus dumps print the resulting one-based locations. Script corpus dumps retain raw offset/length values and normalize CRLF to LF before lexing; its CLI diagnostics omit positions. SDSL-V artifact manifests retain both offsets and scanner line/column. These observable outputs were inspected and were not changed.

## Decision and topology

No source project, solution entry, package change, dependency edge, migration, or compatibility wrapper was added. The only project change is test-local: `Aurelian.Shaders.Tests` references the two Copeland lane projects to host a cross-lane probe. `Aurelian.Shaders` itself retains no Copeland dependency.

No dependency-policy exception or broad Aurelian-to-Copeland permission was introduced. The intended production edge `Aurelian.Shaders -> Copeland.Compiler.Source` remains unapproved because the target project does not exist.

## Span and invalid-input findings

Markdown accepts zero-length, full-source, and EOF-ending spans through its source-bound factory and rejects negative/out-of-range values. Its span record is still structurally unconstrained and has no checked-overflow invariant. Script's position/length records and SDSL-V's start/end location records also admit invalid integers. There is no two-consumer basis for a graduated half-open `TextSpan`.

## Non-goals retained

No IR, diagnostics, parser/token framework, artifact helper, compiler API, `.cope` parser, JavaScript backend, C# backend, shader host behavior, Markdown behavior, or package publishing changed. CLI dump, corpus, generated-C#, HLSL, DXC, SPIR-V, DocumentMir, Cope MIR, and VD-MIR behavior is untouched.

## Follow-up

M6c superseded the proposed Cope Test source-dialect/parser-verifier work. The future trigger is not Markdown compatibility: reconsider shared source infrastructure when any two real lanes independently need compatible indexed-source behavior.
