# CTS-M0a Copeland TS language doctrine audit

## Audit record

- **Repository revision audited:** `10f0124eadee2d91f511262d10a48140853f8296`.
- **Scope:** documentation-only recovery and reconciliation; no production, project, solution, tooling, test, or fixture files changed.
- **Canonical result:** [Copeland TS language profile](../Copeland/language/copeland-ts-language-profile.md).

## Historical doctrine recovered

The closest authoritative original specification is historical blob `docs/language-profile.md` at commit `4575d9e` (`M1e hardening: docs refresh and profile/branching test coverage`). It explicitly describes the strict M1 profile, boolean branching, payload enums, explicit fallibility, and exclusions for `null`, `undefined`, `any`, truthiness, JavaScript object/prototype semantics, modules, and classes. It was moved into the current documentation tree as `docs/Copeland/architecture/language-profile.md` during later repository reorganization; it is now historical, not the current specification.

Additional historical evidence inspected:

| Commit | Path(s) | Finding |
| --- | --- | --- |
| `be8466d` | `docs/copeland-typescript-support.md` | Earlier support matrix: strict profile, payload enums/match, planned nominal classes/modules, explicit unknowns. |
| `4575d9e` | `docs/language-profile.md`, `docs/diagnostics.md` | Closest profile and stable diagnostic vocabulary. |
| `668f57a` | `docs/specs/cope-test-v0.md` | Historical Cope Test syntax experiment; not current language doctrine. |
| `1c98120`, `8332708`, `9aa5437`, `3561cec`, `ec58693` | implementation history | MIR, C# proof, facade, and enum/match runtime proof milestones. |
| `7020a13`, `10f0124` | current topology and migration records | Script rename/topology and JavaScript-first product direction. |

`git log --follow`, `git log -S`, path listings, and blob inspection were used without checkout, reset, restore, or other worktree rewrite. No separate older specification with stronger authority was found. `docs/specs/cope-test-v0.md` is explicitly a historical test-dialect experiment, not a recovered source-language specification.

## Current evidence examined

| Area | Evidence examined |
| --- | --- |
| Architecture and migration records | `docs/Copeland/README.md`; `architecture/language-profile.md`, `copeland-typescript-support.md`, `copeland-ts-compiler-topology-jtf-m6c.md`, `compiler-source-contract-jtf-m6b.md`; `docs/migrations/jtf-m6c-*`, `jtf-m6d-*`; root README. |
| Frontend and MIR | `Copeland.TS` syntax, binder, bound nodes, lowerer, compiler facade, diagnostics; `Copeland.TS.Mir/MirNodes.cs` and text writer. |
| Proof backend and CLI | `Copeland.TS.Backend.CSharp` emitter; `Copeland.Cli/Program.cs`. |
| Diagnostics and tests | `docs/diagnostics.md`; lexer/parser/binder/MIR/facade tests; C# backend corpus/runtime tests; CLI tests. |
| Fixtures | `tests/Copeland/Copeland.TS.Tests/TestData/Corpus`, including M0 lexical/parser/binder/MIR/C# families and M1 enum/match families. |

Searches covered historical names (`Copeland.Script`, Cope Test), doctrine terms (truthiness, coercion, `any`, `unknown`, nullability, payload/tagged enums, prototypes, numeric behavior, interop, TS7), and the requested compiler/test/fixture paths. Build outputs were excluded from language evidence.

## Reconciliation

The historical M1 profile is authoritative for product direction where it makes an explicit statement. It is not a statement that every listed feature remains implemented: it calls object/member access deferred while the current binder rejects it, and it names `undefined`/`any` as banned while the current implementation lacks dedicated diagnostics for them. The old support matrix also proposed C#-oriented class/module/async lowerings; those are historical planning direction, not settled JavaScript-backend semantics.

The canonical profile therefore adopts only the evidence-backed laws: boolean conditions, explicit typing in the present subset, `var`/`eval`/`null` exclusions, explicit fallibility, nominal payload enums and exhaustive match, and Cope MIR as the lane boundary. It records TypeScript 7 as a syntax/ecosystem reference rather than compatibility promise, JavaScript as the first planned product backend, and the C# backend as a proof backend. It does not use C# emission choices to decide equality, number, object, or runtime law.

## Adopted, rejected, and unresolved

- **Adopted/implemented:** boolean-only conditions; typed named calls/declarations; homogeneous arrays; explicit fallibility propagation; nominal payload enums and exhaustive match; Cope MIR boundary.
- **Rejected:** `var`, `eval`, `null`, ordinary object literals/member access in the current subset, implicit global assignment; directionally `any`, `undefined`, truthiness, implicit coercion, prototypes, and implicit host access.
- **Intended but incomplete:** explicit optionality/failure values, strict nominal classes, controlled host interop, and JavaScript distribution backend.
- **Unresolved:** `unknown`; equality; numeric kinds/conversions/overflow; canonical optionality; object/class identity and inheritance; array bounds; closures; evaluation order; exception/error construction; modules; JavaScript interop syntax and runtime boundary.

The rule-by-rule classification and JavaScript readiness view are in the canonical profile; that table is the governing checklist.

## Fixture inventory and CTS-M0b recommendation

| Existing fixture/test family | Current purpose | Language-law candidate? | Proposed destination/category | Keep in corpus? | Evidence |
| --- | --- | ---: | --- | ---: | --- |
| `m0-lex-valid`, `m0-lex-invalid`; lexer tests | tokenization and lexical diagnostics | No | Remain lexical regression corpus | Yes | `LexerCorpusTests`, token snapshots. |
| `m0-parse-valid`, `m0-parse-invalid`; parser tests | syntax trees and parser recovery | No | Remain parser regression corpus | Yes | tree/diagnostic snapshots. |
| `m0-bind-valid`, `m0-bind-invalid`; binder tests | declaration, const, type, profile diagnostics | Selectively | Later `Language/Valid/types` and `Language/Invalid/types` copies only where a law is chosen | Yes | binder snapshots; some cases are implementation-recovery tests. |
| `m0-mir-valid`, `m0-mir-invalid`, `m0-lowering`, `m0-valid` | MIR/gating/projection evidence | Selectively | Later `Language/Valid/core` only for chosen source contracts | Yes | `.cope` expected artifacts and MIR tests. |
| `m0-csharp-valid`; C# corpus/backend tests | generated C# proof output | No | Keep backend regression corpus | Yes | `.g.cs` expected artifacts. |
| `m1-enum-bind-*`, `m1-enum-parse-*` | enum parser/binder snapshots | Yes | Later `Language/Valid/tagged-data`, `Language/Invalid/tagged-data` | Yes | payload constructor and invalid enum cases. |
| `m1-match-bind-*`, `m1-match-parse-*`, `m1-enum-match-mir-*` | match analysis and MIR snapshots | Yes | Later `Language/Valid/tagged-data`, `Language/Invalid/tagged-data` | Yes | exhaustive/payload match evidence. |
| `m1-enum-match-csharp-valid`; runtime enum tests | C# proof/backend runtime behavior | Partly | Keep corpus; add runtime companions only when source observable semantics need proof | Yes | expected C# and Roslyn/runtime tests. |
| `CompilerFacadeTests`, `SmokeTests`, CLI tests | stage gating and command behavior | No | Retain implementation/integration tests | N/A | facade/CLI behavior, not language-law fixtures. |

CTS-M0b should create the proposed `Language/Valid/<semantic-area>` and `Language/Invalid/<semantic-area>` topology without moving existing corpus. Start with the settled rules: boolean conditions, `var`/`null` rejection, typed calls, fallibility, and payload-enum/match. Do not classify parser recovery, C# snapshots, or every existing `.ts` file as conformance evidence.

## Bounded next milestone recommendations

1. **CTS-M0b:** add the language fixture harness and curated fixtures for only the settled rules, ensuring invalid fixtures fail at validation rather than parser/backend accident.
2. **Later JavaScript semantic/lowering milestone:** consume the canonical table in rule order; first prove boolean conditions, declarations/calls, arrays, payload enums/match, and fallible flow from Cope MIR. Before lowering equality, numbers, optionality, objects/classes, bounds, closures, evaluation order, exceptions, modules, or interop, resolve their corresponding law rows. Do not introduce a generalized runtime or helper API until a specific resolved rule requires it.

## Files changed

- `docs/Copeland/language/copeland-ts-language-profile.md` (new canonical profile).
- `docs/migrations/cts-m0a-copeland-ts-language-doctrine-audit.md` (this audit record).
- Narrow historical-status links are added to the former M1 documents and Copeland docs index by this milestone.

## Validation

- Relative links in all changed documents resolve; Markdown tables have matching header/separator columns and code fences are balanced.
- `git diff --check` passed.
- The diff contains documentation only: the canonical profile, this audit, two historical-status notices, and the Copeland docs index link.
- The changed doctrine has no current-tense active `Copeland.Script` architecture claim; the two occurrences are explicitly historical-search/validation references.
- `pwsh ./tools/Validate-DependencyBoundaries.ps1` passed (26 production projects).
- `pwsh ./tools/Validate-CopelandTsTopology.ps1` passed.

Compiler build/tests were not run because the final diff is genuinely documentation-only.
