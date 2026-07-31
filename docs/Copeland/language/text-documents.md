# Text documents

Copeland text documents are structured trees, not HTML strings. XML-shaped
text blocks and the supported Markdown-style inline forms bind to the canonical
document/text model. A document may be bound to a layout text region with
explicit fitting, wrapping, fallback, and overflow facts.

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
