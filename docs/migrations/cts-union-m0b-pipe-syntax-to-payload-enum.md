# CTS-UNION-M0b migration record

## Change

Initial implementation adds declaration-only `type Name = Record | Record` syntax as nominal payload-enum sugar. The source declaration binds to an existing `EnumTypeSymbol` with generated `value` payload fields and lowers through existing enum MIR only.

## Files and validation

Production changes remain narrow. The original implementation stayed in the Copeland TS lexer, syntax model/parser, semantic type metadata, and binder. The closeout pass adds focused evidence plus one defect fix in shared MIR validation: malformed enum and match states reachable from union canonicalization are now rejected before either backend emits. The rest of the closeout remains expanded `NominalUnionTests`, larger `Language/Valid/tagged-data` and `Language/Invalid/tagged-data` fixture ownership, symbolic-union corpus retention, focused backend parity, and TSON/runtime proofs.

The strengthened evidence set validates with:

```powershell
dotnet build Copeland.TS.slnx --no-restore
dotnet test Copeland.TS.slnx --no-build
dotnet build Copeland.slnx --no-restore
dotnet test Copeland.slnx --no-build
dotnet build JointTaskForce.slnx --no-restore
dotnet test JointTaskForce.slnx --no-build
pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1
powershell -NoProfile -File tools/Validate-CopelandTsTopology.ps1
pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1
powershell -NoProfile -File tools/Validate-DependencyBoundaries.ps1
```

The focused test set now proves pipe tokenization, authored declaration order, reachable `COPE-UNION-*` diagnostics, explicit malformed-pipe boundaries, canonical enum MIR equivalence, contextual injection across existing expected-type paths, generic-inference non-widening, distinct same-shaped unions, recursive containment rejection, shared malformed-MIR rejection for union-authored canonical enum/match/TSON corruption, retained corpus byte/hash stability, Symbolic JavaScript retention, exact C#/Diagnostic-JS/Symbolic-JS execution parity, and canonical union-root TSON round-trips. Node version: `v26.2.0`.

## Follow-up boundary

The checked-in `cts-union-m0b` corpus now pins:

- `nominal-union.ts` 357 bytes `FE2E63779FDC9C6B2497C1E43B79446212F6CCDB5B3A8D49571F263B02361296`
- `nominal-union.cope` 608 bytes `69CEDD1030B756AFC481942309E7BF85D4E5AAEB7E636B5C0645EC364C051033`
- `nominal-union.g.cs` 1268 bytes `56EDE4777585B3886F37F86B48332556AFE78CEEA86AEF5EBDC2BA43AF2BC34C`
- `nominal-union.g.js` 6272 bytes `BBAAA7FA856306904D74F64947A072BFA80958A46DA8C8E274660E7ABB37AAEC`
- retained Symbolic JavaScript is pinned at `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/nominal-union.sym.js` (4219 bytes, `15284C1CA6911F2BA63F797A7EB3B6D8A557325EDB42C08199FA1F7BF8A73313`)

`COPE-UNION-0002` is currently an unreachable slot under the accepted law: `type Name = T;` remains an alias, and malformed single-arm pipe spellings stop at `COPE-UNION-0001` before any semantic union body exists. This closeout pass documents that boundary instead of inventing a second one-name union meaning.

Union-root `tsonAsset(...)` ingestion remains outside the accepted M0b boundary because the TSON document reader still rejects `NominalUnionDeclaration` inside assets. That exclusion is now explicitly documented and tested rather than widened with a second ingestion path.

CTS-UNION-M0b is now closed. The closeout ratifies nominal payload-enum sugar only; it does not authorize structural unions, narrowing/widening algebra, bitwise-or typing, new MIR/runtime carriers, or separate union metadata families.
