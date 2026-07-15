# CTS-UNION-M0b migration record

## Change

Initial implementation adds declaration-only `type Name = Record | Record` syntax as nominal payload-enum sugar. The source declaration binds to an existing `EnumTypeSymbol` with generated `value` payload fields and lowers through existing enum MIR only.

## Files and validation

Production changes are confined to the Copeland TS lexer, syntax model/parser, semantic type metadata, and binder. Focused coverage is in `NominalUnionTests` and the language fixtures under `Language/Valid/tagged-data` and `Language/Invalid/tagged-data`.

The initial implementation was built with:

```powershell
dotnet build Copeland.TS.slnx --no-restore
dotnet test Copeland.TS.slnx --no-build --filter FullyQualifiedName~NominalUnionTests
```

The focused test set proves pipe tokenization, authored declaration order, malformed pipe diagnostics, canonical enum MIR, contextual injection across existing expected-type paths, nominal distinction, alias-alternative rejection, resource rejection, TSON enum identities, recursive containment rejection, and C#/Diagnostic-JS/Symbolic-JS runtime execution. Node version: `v26.2.0`.

## Follow-up boundary

The checked-in `cts-union-m0b` corpus pins `nominal-union.ts` (357 bytes, `FE2E63779FDC9C6B2497C1E43B79446212F6CCDB5B3A8D49571F263B02361296`), canonical MIR (608 bytes, `69CEDD1030B756AFC481942309E7BF85D4E5AAEB7E636B5C0645EC364C051033`), C# (1268 bytes, `56EDE4777585B3886F37F86B48332556AFE78CEEA86AEF5EBDC2BA43AF2BC34C`), and Diagnostic JavaScript (6272 bytes, `BBAAA7FA856306904D74F64947A072BFA80958A46DA8C8E274660E7ABB37AAEC`).

This migration still does not claim the exhaustive diagnostic-inventory and every requested filesystem scenario necessary for closed M0b status. Those are evidence additions over the canonical enum path, not authorization for a separate union backend.
