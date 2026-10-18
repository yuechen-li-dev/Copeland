# Copeland TS examples

Start with the examples below. Directory milestone suffixes identify the existing
regression/history lineage; they do not rank examples by freshness. Commands run
from the repository root unless a linked README says otherwise. Each README
states prerequisites and what has actually been qualified.

| What to explore | Entry point | What it demonstrates |
| --- | --- | --- |
| First .NET application | [Hello](CopelandHello/README.md) | Normal SDK build and generated C# consumption |
| Current TableScript application | [Café editor](tablescript-rows-m15/README.md) | Typed domain errors, drafts, derived tables, keyed add/delete and empty snapshots |
| Compact table/C# example | [Product catalog](table-dogfood-m0/README.md) | Columnar tables, reference joins, immutable prices and asserted C# consumption |
| Mixed React/Vue components | [Framework islands](mixed-islands-m10/README.md) | One Copeland state owner, framework-owned controls and child lifetime |
| Vue comparison | [Vue and Copeland](vue-friction-m8/README.md) | Idiomatic SFC baseline beside source-owned state/layout/events |
| React language features | [React friction corpus](react-friction-m0/README.md) | Preserved baseline pairs, streams/layout/state/effects and ownership cases |
| Traditional TypeScript interop | [File profiles](file-profiles-m0/README.md) | Explicit Legacy/JavaScript/.NET ownership and real tsc forwarding |
| Compiled workbook consumption | [TSON workbook](tson-workbook-m0/README.md) | Authored tables projected through the SDK |

Run the current café and then inspect it interactively:

```powershell
./samples/copeland-ts/tablescript-rows-m15/run.ps1
npm run serve --prefix samples/copeland-ts/tablescript-rows-m15
```

Requires .NET 10, Node 26, npm, TSPack and Edge. The first command runs all three
JavaScript profiles at desktop/mobile sizes. The second serves the built page at
<http://127.0.0.1:4191/diagnostic/index.html> until Ctrl+C. It is a local preview;
authoritative persistence and remote commits are not implemented by this demo.

## Supporting fixtures and infrastructure

The current café reuses the [workbook](tablescript-friction-m12/README.md),
[application policy/projection](tablescript-application-m13/README.md),
[typed editing reducer](tablescript-editing-m14/README.md) and
[shared libraries](../../libraries/README.md). Those earlier directories retain
unique regression cases and live source dependencies, rather than independent
copies of the whole application. New runs write local ignored output and leave
committed historical evidence unchanged.

The three table browser runners delegate to
[shared qualification infrastructure](../../tools/Copeland.Qualification.Browser/README.md).
The two raw-text editors consume one React adapter and one component contract.
The numeric-only application bridge remains a deliberate earlier regression case.

Other `*-m*`, `machina-*`, browser, presentation and template folders are focused
integration fixtures. Consult their own README before using them as a starting
application; not every historical fixture is a current recommended template.
The [instinct benchmark](typescript-instinct-m26-benchmark/README.md) is measurement
infrastructure, with its stated comparison limits.

The redundant UUID-suffixed catalog project was retired after its unkeyed/manual
lookup behavior became `ManualCatalogTests` with C# and all-profile JavaScript
assertions. Its original files remain recoverable from Git revision
`1cef76c565b5cfaca5d8aa2c1d1526d625460223`. No current sample depends on that copy.

For new project creation, use the maintained
[SDK templates](../../src/Copeland/Copeland.TS.Templates/README.md) rather than copying
a historical qualification directory. See the
[documentation index](../../docs/Copeland/README.md) and
[repository maintenance rules](../../docs/Copeland/repository-maintenance.md).
