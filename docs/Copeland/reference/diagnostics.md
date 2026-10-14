# Copeland TS diagnostics catalog

Diagnostics are deterministic IDs emitted as close as practical to the
semantic owner. Stable IDs are not renumbered by this consolidation.

| Family | Owner | Meaning / examples |
| --- | --- | --- |
| `COPE-PARSE-*` | lexer/parser | malformed syntax and recovery |
| `COPE-BIND-*` | binder | declarations, symbols, names, scopes |
| `COPE-TYPE-*` | binder/type rules | assignments, calls, conditions, Result handling |
| `COPE-INIT-*` | parser/initializer binding | postfix repetition counts, remainder extent, admission limit, unsupported spread |
| `COPE-ROWS-*` | table authoring binder | missing row initializer, mixed authoring forms, non-object rows, unknown/duplicate/missing fields |
| `COPE-DERIVE-*` | relation binder/parser | derived relation types and purity; mutable locals (`0010`), locals after output columns (`0011`), local name conflicts (`0012`) |
| `COPE-SUMMARY-*` | typed summary binder | missing source metadata (`0001`), record type (`0002`), descriptor shape (`0003`), fields (`0004`), types (`0005`), group keys (`0006`), unsupported aggregate (`0007`), direct input/key copy (`0008`), missing aggregate (`0009`), stored views/standalone grouping (`0010`), direct async/generator lowering (`0011`) |
| `COPE-JS-SUMMARY-0001` | JavaScript backend | typed summary execution requires the C# backend |
| `COPE-RECALC-*` | snapshot-relative relation binder | derived declaration receiver (`0001`), nonempty input literal (`0002`), unknown/derived/unrelated/duplicate input (`0003`), nominal snapshot type (`0004`), direct async/generator lowering (`0005`) |
| `COPE-PROFILE-*` | binder profile checks | unsupported TypeScript-shaped syntax (`var`, `null`, ternary, optional chain) |
| `COPE-REC-*`, `COPE-ENUM-*`, `COPE-MATCH-*` | record/enum/match binding | nominal data construction and exhaustiveness |
| `COPE-TRY-*` | fallibility binder | `try` / `except` shape and targeted propagation |
| `COPE-CLR-*` | CLR binding | namespace/type/member/overload and directive errors |
| `COPE-LAYOUT-*`, `COPE-TEXT-*` | normalized layout/document binding | layout, stream, text/document facts |
| `COPE-COMPONENT-*`, `COPE-RENDERER-*`, `COPE-ATTACHMENT-*` | component/attachment planning | component identity, adapter compatibility, attachment plan creation |
| `COPE-PROJECT-*`, `COPE-TSCL-*`, `COPE-CLI-*` | context/build/CLI | project descriptor, build contract, command usage |
| `COPE-CS-*`, JavaScript backend diagnostics | backend emitters | a validated fact cannot be emitted by that backend |
| `COPE-ATTACHMENT-PLAN-*`, `COPE-COMPONENT-STATE-BROWSER-*` | TSPack/browser runtime | transport, host, lifecycle, frame, and runtime context |
| `COPE-COMPONENT-EFFECT-*` | component state runtime | effect phase and completion lifecycle |

## Rules

- Binder/type errors should win over a backend failure whenever the issue can
  be known from canonical semantic facts.
- Compiler diagnostics use authored source provenance. Artifact/runtime
  diagnostics include attachment, component instance, state, adapter, host,
  and event context rather than a generated-file position alone.
- CLI/LSP preserve diagnostic IDs and source mapping; neither reconstructs
  meaning from formatted tables or tokens.
- `docs/diagnostics.md` remains a historical M1 overview. This page is the
  current catalog map; detailed code-level inventories remain test/source
  searchable until a generated catalog is warranted.

Noted follow-up: the catalog families are coherent, but no single checked-in
registry currently prevents accidental numeric collisions across every domain.
Add a lightweight source/test collision check before introducing another large
diagnostic family; do not renumber existing codes.
