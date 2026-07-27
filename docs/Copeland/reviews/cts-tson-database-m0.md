# CTS-TSON-DATABASE-M0 experimental report

**Status:** experimental vertical slice complete. This is evidence for a bounded
immutable snapshot architecture, not a production database commitment.

## Hypothesis and result

The tested hypothesis was:

```text
Copeland logical record
    + declarative nested index
    + external deterministic columnar leaves
    + generated C# routing/query code
    = a typed compiled database without a database server
```

The answer for the bounded fixture is **yes**. A normal Copeland `Event` record
and a restricted TS-XML definition build a root index and three immutable leaf
segments. Generated C# compiles into a normal DLL. Its typed query routes through
`tenant` and `year`, opens one leaf, reads the `value` region, and returns `7.75`.
Tests replace every unrelated segment with invalid data and the query still
succeeds. A separate two-column fixture corrupts the unrequested `note` payload
and the value-only query still succeeds.

The answer is not “Copeland now has a general database.” M0 has no transactions,
mutation, WAL, recovery, concurrent writer, dynamic optimizer, SQL, joins,
migrations, or server.

## Existing architecture discovered

The closest existing abstractions were useful, but none was the database:

- Copeland records already provide logical nominal field declarations and
  lower to `MirRecordDefinition`.
- Copeland `record table` already establishes immutable column semantics and
  per-column generated C# storage. It is, however, one authored singleton value
  whose data enters MIR. Treating an external database snapshot as that value
  would force data into the assembly and conflate logical and physical schema.
- TSON already provides stable schema identities, canonical values, table
  semantics, limits, and asset boundaries. M0 adopts its explicit `$schema`
  authority law but does not duplicate its parser.
- The neutral TS-XML syntax tree plus profile binder pattern used by manifests
  is a clean fit for a logic-free index declaration.
- The C# backend demonstrates direct static generation without reflection or
  dynamic property dispatch.
- The CLI is the correct host for file I/O. Existing binder, MIR, and backend
  layers do not directly access the filesystem.

Database metadata remains in the bounded `Copeland.TS.Database` experiment
rather than general MIR. The physical layout and one compiled query are not yet
stable enough to impose as backend-neutral language IR. Logical record
definitions still enter through the real Copeland compiler and MIR.

## Final authored shape

The logical schema is:

```ts
const $schema: string = "copeland://experimental/events/v1";

export record Event {
    tenant: string;
    year: int;
    value: number;
}
```

The database profile is:

```tsx
export default defineDatabase(
    <Database name="Events">
        <Index field="tenant">
            <Index field="year">
                <Table type={Event} />
            </Index>
        </Index>
    </Database>
);
```

`<Table record={Event} />` from the sketch was tested and rejected by the real
neutral parser because `record` is a language keyword and is not a valid TS-XML
attribute-name token. The implemented profile uses `type={Event}` rather than
forking or preprocessing the parser. The definition accepts only the exact
`defineDatabase` envelope, string attributes, direct record reference, nested
`Index` elements, and one empty `Table` leaf. It cannot execute application
logic.

The CLI path is:

```console
tscl database build \
  --schema schema.ts \
  --definition index.tsx \
  --input events.json \
  --output events-db \
  --generated-source Generated/EventsDatabase.g.cs
```

The fixture uses JSON as the row-ingestion interchange so that JSON is also a
baseline. This is a tool-host decision, not the storage format or logical schema
law. A later milestone can bind a TSON table input without changing the database
model.

## Separation of concepts

M0 preserves these distinct values:

| Concept | M0 representation |
| --- | --- |
| Logical record schema | Copeland `record`, compiled to record MIR |
| Physical record-table layout | ordered stored-field column descriptors and payload codecs |
| Tree/partition index | restricted TS-XML `Index` chain and deterministic root entries |
| Binary segment format | versioned root and leaf files |
| Compiled query API | generated `EventsDatabase.SumValue(string, int)` |
| Snapshot/version | one immutable root plus its content-addressed leaf names |

`record table Event` is therefore not new source syntax and not a type
constructor in M0. “Record table” is the physical realization and generated API
metadata for a logical record at a leaf. Existing authored `record table`
language semantics remain unchanged.

## Identity law

The schema source must declare one nonblank `$schema` string. The logical
identity is SHA-256 over canonical metadata containing:

```text
format law
explicit schema authority
record name
ordered field names and scalar types
```

The index identity is SHA-256 over that logical metadata plus:

```text
database name
record name
ordered index depths and fields
leaf format and partition-key policy
```

Both full 256-bit identities are embedded in root, every leaf, and generated
source. Generated readers compare them in fixed time. Compatibility is not
keyed by a short type name.

## Physical layout

M0 writes:

```text
database/
├── root.index
└── leaves/
    ├── <sha256(encoded-partition-path)>.segment
    ├── ...
    └── ...
```

The root contains:

```text
"CTSROOT1"
format version
logical schema identity
index identity
ordered partition field names/types
leaf count
for each canonical sorted route:
    typed partition key values
    leaf identity
    confined relative leaf path
```

Each leaf contains:

```text
"CTSLEAF1"
format version
logical schema identity
index identity
leaf identity
row count
column count
for each stored column:
    name
    scalar type
    absolute offset
    length
    SHA-256 payload checksum
contiguous column payloads
```

Booleans are one byte, `int` is little-endian `Int32`, and `number` is
little-endian IEEE binary64. A string column has `rowCount + 1` little-endian
`Int32` UTF-8 offsets followed by concatenated UTF-8 bytes. M0 uses ordinary
read-only streams. Memory mapping was not justified by this fixture and would
add lifetime and disposal complexity without evidence.

Readers bound root and leaf file sizes, row/leaf/column counts, verify magic,
versions and identities, confine paths, reject duplicate routes/columns,
validate every region against file length, require contiguous non-overlapping
regions with no undeclared trailing bytes, and checksum a payload before
decoding it. Truncation and corrupted requested-column tests fail with
`InvalidDataException`.

## Partition keys

`tenant` and `year` remain logical fields of `Event`, but they are omitted from
leaf payloads. Their values come from the index path. This was tested rather
than assumed: leaf bytes contain the `value` descriptor but contain neither
partition field names nor fixture tenant strings.

This is beneficial when partition values repeat for every row. A future row
projection must synthesize logical `tenant` and `year` properties from the
route. M0 exposes only the compiled aggregate, so it does not yet publish that
row-projection API.

## Generated assembly and query

Generated source contains:

- the public nominal `Event` CLR record;
- full schema and index identity constants;
- the root/leaf format reader;
- direct typed `RouteKey(string Tenant, int Year)` routing;
- path confinement and binary validation;
- the compiled `SumValue(string tenant, int year)` query;
- `DatabaseQueryTrace` instrumentation.

The consumer shape is:

```csharp
using var database = EventsDatabase.Open(path);
double sum = database.SumValue("tenant-a", 2026);
```

The query performs:

```text
typed tenant/year key
→ root dictionary lookup
→ exactly one segment open
→ descriptor lookup for "value"
→ exactly one payload read and checksum
→ direct numeric accumulation
```

It does not inspect schema through reflection, scan every leaf, reconstruct
rows, or filter after reconstruction.

## Fixture and pruning evidence

The seven rows are:

```text
tenant-a / 2025: 1000, 2000
tenant-a / 2026: 1.25, 2.50, 4.00
tenant-b / 2026: 10000, 20000
```

The expected target sum is `7.75`; scanning either unrelated route would make a
mistake conspicuous.

Evidence is behavioral, not inferred:

1. The generated consumer returns `7.75`.
2. Its trace reports one opened leaf and `ReadColumns == ["value"]`.
3. After the first query identifies that leaf, both unrelated leaf files are
   replaced by invalid one-byte files. Repeating the query still returns
   `7.75`.
4. In a second fixture with stored `value` and `note` columns, the `note`
   payload is corrupted. The query still returns `7.75`; only `value` is
   checksummed and read.
5. Corrupting the requested value payload causes a checksum failure.

## Baselines and measurements

Measurements are from the .NET 10 validation machine on the seven-row fixture.
They are repeated-loop orientation numbers, not a benchmark claim:

| Path | Total stored/input bytes | Bytes examined for warm query | Rows reconstructed | Mean query time |
| --- | ---: | ---: | --- | ---: |
| JSON row scan | 412 | 412 | all 7 JSON rows | 6.762 µs |
| simple row binary scan | 147 | 147 | all 7 rows | 0.226 µs |
| routed columnar leaf | 1,042 | 195 after open | none | 29.254 µs |

The root is 473 bytes. Opening and validating it averaged 46.370 µs. Five
separate CLI runs reported in-process builds between 9.752 and 10.800 ms; the
instrumented test run reported 11.192 ms.

The tree format loses badly on tiny-fixture latency and total size because it
opens a file and pays three leaf headers plus identities/checksums. Its first
real wins are structural: deterministic pruning, external data independent of
the DLL, no row reconstruction, and one-column I/O. Larger leaves and selective
queries are required before expecting a throughput or size win. Keeping a leaf
stream/cache open, packing segments, memory mapping, or compression are
unproven follow-ups, not M0 conclusions.

## Experimental questions answered

1. **Does the model fit?** Yes, as a new bounded storage/generation layer using
   Copeland record MIR and the neutral TS-XML profile seam. It does not fit by
   reinterpreting the existing authored table singleton.
2. **What is `record table` here?** A physical storage realization plus
   generated leaf metadata, not new syntax or a logical type constructor.
3. **Is `index.tsx` suitable?** Yes. The existing parser and profile-binder
   pattern produce a small, spanned, logic-free declaration. `type`, not
   `record`, is the workable leaf attribute.
4. **Repeat partition keys?** No for M0. Keep them logically visible and
   synthesize them from the route in any future row reader.
5. **Separate files or one binary?** Separate files for M0. They make pruning,
   corruption tests, replacement, and inspection explicit. Header overhead and
   open cost are measurable disadvantages.
6. **Is the DLL the right home?** Yes for nominal types, identities, readers,
   routing and compiled queries. No for ordinary snapshot contents.
7. **What is fully compiled?** Key types/codecs, route shape, leaf format,
   numeric column decoder, validation, and the aggregate query.
8. **Where could dynamic querying fit?** Above validated root/column metadata
   as a separate planner/API. It should not weaken the direct generated path.
9. **First wins?** Leaf pruning, column pruning, no row materialization,
   deterministic snapshots, and typed deployment without a server—not
   tiny-fixture latency.
10. **Wrong/incomplete assumptions?** Existing `record table` cannot directly
    represent external snapshots; the illustrative `record` TS-XML attribute
    is syntactically invalid; a tiny multi-file store is larger and slower than
    both baselines; explicit schema authority was necessary in addition to
    hashing record shape; memory mapping was unnecessary.
11. **Smallest credible next milestone?** See below.
12. **Best workloads?** Immutable catalogs, analytics/log/scientific snapshots,
    generated lookup stores, embedded read-mostly application data, and local
    agent indexes with stable selective routes. It is inappropriate for OLTP,
    many tiny random mutations, concurrent writers, or general relational
    workloads.

## Diagnostics and validation

The profile reports spanned diagnostics for malformed envelopes/attributes,
invalid tree shape, missing records, unsupported scalar/key types, absent index
fields, duplicate index fields, and missing explicit schema identity. The
builder rejects missing/extra fields, invalid scalar values, non-finite numbers,
and unsupported keys. Generated readers reject schema/index mismatches, unsafe
paths, duplicate routes, malformed counts/regions, truncation, and corrupted
requested payloads.

Focused evidence covers:

- TS-XML profile binding and diagnostics;
- normal Copeland record binding through MIR;
- path-owned partition keys;
- deterministic artifact hashes and generated source;
- binary round trip;
- mismatch, truncation and checksum rejection;
- compiled generated DLL plus typed consumer;
- exact one-leaf routing;
- unrelated-leaf corruption survival;
- unrequested-column corruption survival;
- CLI repeatability;
- JSON, row-binary and tree measurements.

Repository validation on 2026-07-27:

- `dotnet build Copeland.slnx --no-restore`: passed, zero warnings/errors.
- `Copeland.TS.Database.Tests`: 11/11 passed.
- focused CLI database integration: passed.
- `git diff --check`: passed.
- full parallel solution tests: the database project passed 11/11, while 13
  unrelated existing tests failed in callable/async emission and pinned corpus
  hashes. The TSON asset CLI failure from that parallel run passed 3/3 when
  rerun in isolation. Representative C# corpus hash (3 failures) and JavaScript
  async emission (1 failure) tests failed again in isolation. No unrelated
  corpus baselines were updated, as required.

## Additional work performed

- Added `Copeland.TS.Database` as a bounded experimental package because mixing
  unstable storage/query metadata into general MIR would prematurely canonize
  the experiment. Semantic impact is isolated to callers of the new package.
- Added `tscl database build` because the CLI is the established filesystem
  host and a real build path was needed to test external contents.
- Added generated query traces because correct sums alone do not prove pruning.
- Required `$schema` authority after shape-only hashing was found insufficient
  for nominal identity.
- Added checksummed contiguous column regions and path confinement because
  malformed external binary data cannot be trusted.

## Failures and dead ends

- `<Table record={Event}/>` fails in the production TS-XML parser; `type` was
  adopted instead of a parser exception.
- Mapping external snapshots onto existing `MirTableDefinition` would embed
  constants in generated code and require rebuilding for every mutation. That
  route was rejected before implementation.
- Memory mapping was considered but not implemented: stream reads already
  demonstrate pruning, while mapping would introduce ownership and span
  lifetime questions without a measured need.
- A packed single file would reduce open/header overhead, but would make the
  first pruning and corruption proof less transparent.

## Recommendation

Proceed to a small M1 focused on scale evidence, not feature breadth:

1. accept an existing canonical TSON table asset as typed row input;
2. generate a typed leaf projection that synthesizes partition fields from the
   route;
3. test 10⁵–10⁶ rows with selective routes and multiple value columns;
4. compare per-leaf files with one packed immutable snapshot;
5. add bounded string readers and optional reusable open leaf handles;
6. decide only then whether stable database metadata belongs in MIR and whether
   an MSBuild generator should replace the explicit CLI source output.

Do not add transactions, in-place updates, SQL, a dynamic optimizer, or a
server in that milestone. If the larger selective workload does not overcome
the measured file/header complexity, stop rather than broadening the product.
