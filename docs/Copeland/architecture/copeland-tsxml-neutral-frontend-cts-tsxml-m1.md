# CTS-TSXML-M1: neutral TS-XML frontend

**Status:** implemented syntax-only frontend milestone. This record does not select a manifest, test, component, or React-compatible semantic profile.

## Scope

Copeland calls the XML-shaped TypeScript syntax **TS-XML**. It is available only when a source path ends in `.tsx`; `.ts` remains the ordinary Copeland TypeScript grammar and `.jsx` is explicitly rejected as a Copeland source extension. The parser owns an extension-selected mode, not a new runtime or backend target. Generic import and `export default` declaration wrappers are also preserved for future profile-owned documents; they confer no module/runtime semantics in the base language.

The accepted M1 grammar is intentionally small:

- named nested elements and self-closing elements;
- exact matching opening and closing names;
- string-valued, boolean/presence, and `{ expression }` attributes;
- raw text, `{ expression }`, nested element, and fragment children;
- empty fragments written `<>...</>`.

Expressions within braces reuse the ordinary Copeland expression parser. The `<` ambiguity is deliberate: TS-XML is recognized only at an expression start in `.tsx` mode; ordinary binary comparisons retain their existing parse path, and `.ts` never enters TS-XML mode.

## Neutral tree and diagnostics

`TsXmlElementExpressionSyntax`, `TsXmlFragmentExpressionSyntax`, `TsXmlAttributeSyntax`, and the dedicated text/expression/element child nodes preserve source tokens and raw text spans. They do not lower to calls or imply a particular element result type. Text is read from the original source contextually so punctuation and layout remain text rather than TypeScript tokens.

`COPE-TSXML-0001` rejects `.jsx`; `0002` reports malformed required TS-XML delimiters/names; `0003` reports invalid attribute values; `0005` reports a missing closing tag; and `0006` reports mismatched names. These diagnostics carry a non-empty source span. Lexical diagnostics wholly inside a recognized TS-XML text span are suppressed because that content is text, not TypeScript syntax.

Binding a TS-XML expression without an interpretation profile reports `COPE-TSXML-0101`. This is a deliberate boundary: parsing succeeds while executable meaning remains unavailable.

## Future profile boundary

The same neutral tree is reserved for independently selected profiles:

| Future profile | Potential interpretation | M1 decision |
| --- | --- | --- |
| TSPack manifest | root-level declarative project manifest | deferred |
| Copeland/TSPack xtest | declarative test syntax | deferred |
| Typed components | checked component/child/attribute model | deferred |
| React compatibility | opt-in compatibility projection | deferred |

No profile is inferred from tag names, filenames other than `.tsx`, imports, or backend. In particular, M1 emits no `React.createElement`, imports no JSX runtime, defines no universal `JSX.Element`, and creates no manifest schema, sidecar binding, component runtime, or backend helper.

## Evidence

`TsXmlSyntaxTests` reads TSX fixtures for nesting, self-closing elements, all M1 attribute forms, text/expression children, fragments, malformed syntax, mismatched names, exact positions, `.ts`/`.tsx`/`.jsx` selection, and the profile-required binding diagnostic. The positive fixture follows the nested declarative element shape used by TSPack's root `manifest.tsx`, without giving Copeland any TSPack vocabulary.

## Deferred work

Qualified names, spread attributes, entity decoding, comments-as-nodes, TSX type-argument disambiguation beyond the current bounded expression grammar, import/module/project loading, manifest validation, xtest semantics, components, React compatibility, and all runtime/backend execution remain deferred. CTS-SIDECAR-M1a is explicitly not started by this milestone.
