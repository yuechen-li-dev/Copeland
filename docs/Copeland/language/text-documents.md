# Text documents

Copeland text documents are structured trees, not HTML strings. XML-shaped
text blocks and the supported Markdown-style inline forms bind to the canonical
document/text model. A document may be bound to a layout text region with
explicit fitting, wrapping, fallback, and overflow facts.

`.tsx` is Copeland syntax for XML-shaped typed values. React is one consumer,
not the definition of TS-XML. With the `text-m0` Text type available in a
Copeland project's composed type set, a `<Document>` expression has the first-class `Document`
type and does not require React or a browser. Its compiler meaning remains the
canonical `DocumentMir`; the C# backend materializes an immutable
`TextDocument` / `TextNode` tree with closed node kinds, attributes, and
ordered `TextContent` children.
`react-m0+text-m0` makes both type families available. In that composition,
`<Document>` resolves to the Text value; React components retain their normal
React resolution. JavaScript currently reports an explicit unavailable Text
materializer diagnostic rather than routing documents through React.

## Typed text slots

TypeScript computes; TS-XML describes. A Text inline may contain a normal
precomputed `string` value slot, such as `<Paragraph>Hello, {name}.</Paragraph>`.
The compiler preserves the slot's position in `DocumentMir` and evaluates the
ordinary Copeland expression during backend materialization. There is no
implicit object conversion or JavaScript-style truthiness: values other than
`string` must be explicitly converted before they enter Text TS-XML.

Text TS-XML intentionally has no XML conditionals, loops, matches, collection
flattening, directives, statements, mutation, or event logic. Select a string
or a complete `Document` with ordinary Copeland functions and `match` before
constructing the declarative XML tree.

`DocumentMir` and the text document model own document meaning. The
`text::Documents`, `text::Blocks`, `text::Inlines`, `text::Bindings`, and
`text::Regions` relations are read-only inspection views. Browser text fitting
and a renderer's DOM are realizations; neither may reinterpret the source tree
or alter compiler layout geometry.

The current supported subset is deliberately bounded by parser/model and
browser proofs. Use the fixtures and [Machina layout data](../machina-layout.md)
for exact authoring syntax. Rich renderer-specific inline styling, universal
Markdown compatibility, and semantic document editing are not implied by the
current implementation.
