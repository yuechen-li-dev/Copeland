# CTS-TSXML-M1 neutral TS-XML frontend migration record

**Status:** complete bounded frontend slice.

CTS-TSXML-M1 adds an extension-selected `.tsx` parser mode and backend-neutral syntax nodes. It accepts nested and self-closing named elements, matched closing names, presence/string/braced-expression attributes, raw text and braced-expression children, nested children, and fragments. The parser preserves raw TS-XML text source positions and keeps ordinary Copeland expression parsing inside braces.

The implementation deliberately keeps TS-XML separate from React and execution: no JSX runtime, `React.createElement`, `JSX.Element`, profile-specific tag vocabulary, manifest schema, component semantics, MIR node, or backend machinery was added. Binding reports `COPE-TSXML-0101` until a future profile selects an interpretation. `.ts` does not recognize TS-XML and `.jsx` is rejected.

The fixtures under `tests/Copeland/Copeland.TS.Tests/TsXml` and the focused `TsXmlSyntaxTests` prove the accepted and rejected syntax, spans, extension selection, and no-profile boundary. The positive fixture is intentionally compatible with the structural element/attribute style observed in TSPack's root `manifest.tsx`; it does not bind TSPack names or APIs.

Deferred: manifest and xtest profiles, CTS-SIDECAR-M1a project binding, typed components, React compatibility, broader TSX ambiguity/type-argument work, module loading, and every runtime/backend behavior.
