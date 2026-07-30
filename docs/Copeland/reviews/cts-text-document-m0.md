# CTS-TEXT-DOCUMENT-M0

> Layouts assign regions. Text documents flow inside them.

> XML-shaped syntax describes block structure. Markdown-style shorthand describes ordinary inline meaning.

> A text block is a local document, not a miniature page layout.

This M0 introduces a bounded immutable document tree for React-profile Copeland
source. `Text("plain text")` creates a Body paragraph. A structured document
uses `Document`, `Heading`, `Paragraph`, `List`, `Item`, `CodeBlock`, `Quote`,
`Callout`, and `Break`. `Document` contains blocks; `List` contains `Item`;
an `Item` contains paragraphs, lists, code, quotes, callouts, or breaks;
headings and paragraphs contain inline prose only.

Inline prose supports compiler-owned `**strong**`, `*emphasis*`,
`[link](target)`, inline `` `code` ``, and escaping. It is lowered directly to
safe semantic DOM (`strong`, `em`, `a`, and `code`); there is no runtime
Markdown library, raw HTML, `dangerouslySetInnerHTML`, or arbitrary component
interpolation. Relative, fragment, `http`, `https`, and `mailto` links are
accepted; executable schemes are rejected. Code blocks preserve literal text.

M0's `BoundTextDocument` ownership was superseded in CTS-TEXT-DOCUMENT-IR-M1:
it is now a source/host binding around the canonical Copeland
`DocumentMir`, rather than a second tree of `BoundTextBlock` and
`BoundTextInline`. Roles such as
`HeroHeading`, `CardHeading`, `Body`, `Eyebrow`, and `Caption` are semantic
identities. M0 projects a `CopelandText` theme identity rather than creating a
general style-table language.

Inspection exposes read-only `text::Documents`, `text::Blocks`, and
`text::Inlines` alongside the earlier `text::Regions`. A bound document row
records its owning stable layout box, binding, theme, fit mode, overflow policy,
and project-relative source. Block and inline rows retain parent relations,
authored order, roles, targets, and source provenance. They are projections,
not editable source tables.

The website dogfoods plain text, structured hero copy, feature-card documents,
the canonical pipeline list, starter command code block, footer link, bold,
and inline code. Browser shaping and the existing bounded fit/overflow host
remain local to their layout box. The existing cross-profile browser proof
confirms fixed outer boxes, visible actions, local code scrolling, no page
horizontal overflow, and reachable footer at Desktop, Tablet, and Mobile.

Future intrinsic-height article/document support is deliberately deferred.

M1 closure adds `TextPresentationBinding`: class assignments, theme, semantic
host, and eventual layout fit/overflow attachment are presentation facts. They
are not `DocumentMir` structure. React renders canonical document nodes and
LSP locates canonical nodes by source span.
The shared future Copeland TS / MachinaLayout.JS / Machina.UI model is the
block kind, inline kind, semantic role, local fit/overflow policy, stable
identity/source, and safe renderer contract—not source syntax or browser
measurement as compiler truth.
