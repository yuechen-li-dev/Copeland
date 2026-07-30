# CTS-TEXT-DOCUMENT-IR-M1 investigation

> Copeland has one canonical document semantics and multiple source frontends.

> XML-shaped text syntax and Markdown are authoring projections, not separate document runtimes.

This investigation selects outcome A. `Copeland.Markdown.DocumentMir` is the
existing backend-neutral document representation and is suitable as the
canonical semantic document tree after bounded general-purpose extensions.
M1 begins that convergence by making the TS text frontend construct and bind
`DocumentMir`; `BoundTextDocument` is now only the TS host/frontend binding
(`DefinitionId`, owner, root source, and `DocumentMir`), not a parallel tree.

## Existing pipeline findings

| Topic | Finding |
| --- | --- |
| Source entry points | `MarkdownCompiler.Compile` is the Markdown entry point. TS documents are discovered from the parsed `SyntaxTree` by `TextDocumentCompiler`. |
| Parser ownership | `Copeland.Markdown` owns `MarkdownLexer`, `MarkdownParser`, and the bounded `MarkdownInlineParser`; no third-party runtime parser is used. |
| Existing vocabulary | Markdown had heading, paragraph, bullet/ordered list, list item, fenced code, thematic break, text, code span, emphasis, strong, and link. |
| Tree/parents | Lists own items and inline containers own children. The M1 binder adds parent semantic identities, including nested inline parents and nested list-item blocks. |
| Identity | Markdown originally had no stable semantic identity. `DocumentMirBinder` now supplies deterministic document/block/inline identities and authored order. |
| Spans/provenance | Markdown has immutable UTF-16 `SourceSpan` with line/column locations. M1 adds frontend kind, source path, offsets, and lengths as `DocumentProvenance`; TS retains its parsed-source spans. |
| Diagnostics/recovery | Markdown preserves malformed text and records diagnostics. TS similarly keeps legal surrounding blocks; link safety is now enforced by the shared binder. |
| Interpolation | Markdown has none. The TS semantic React binder has typed child expression support, but canonical document interpolation has not yet been unified; M1 reports it as deferred rather than silently inventing a second evaluator. |
| URI validation | M0 validated TS links independently; Markdown had no shared validation point. `DocumentMirBinder.IsSafeLinkTarget` now accepts relative/fragment/http/https/mailto only and replaces unsafe links with literal label text plus `COPE-DOC-0001`. |
| Code/list | Code is literal `CodeBlockMir`; Markdown lists are flat by design. M1 adds bounded nested blocks on `ListItemMir` for TS XML without changing the Markdown dialect. |
| Roles/style | M0 roles were TS-only strings. M1 carries a role on generic node metadata with defaults; there is no new style-table language. |
| Backend traversal | Markdown consumers traverse `DocumentMir`; the React binder still owns a separate source-to-DOM realization and remains the next integration target. |
| LSP | LSP provides token-based text hover and regular source diagnostics. It does not yet query canonical document facts. |
| Projected tables | `text::Documents`, `text::Blocks`, and `text::Inlines` now walk `BoundTextDocument.Document` and its canonical parent IDs. `layout::Sources` remains the project-relative source relation. |
| Serialization | `MarkdownDumpWriter` is the existing inspection/JSON boundary. M1 metadata is additive and deliberately not a breaking dump-format change. |
| Consumers | CLI Markdown inspection, Oblivion markdown previews, and the TS projected tables are the active consumers. Their existing block types remain source-compatible. |

## Compatibility matrix

```text
Feature                     Existing DocumentIR   BoundTextDocument       Action
Document root               DocumentMir           wrapper only            reuse DocumentMir
Heading                     HeadingMir            duplicate node          reuse
Paragraph                   ParagraphMir          duplicate node          reuse
List                        ListMir               duplicate node          reuse
Item                        ListItemMir           duplicate node          reuse; add child blocks
Code block                  CodeBlockMir          duplicate node          reuse
Quote                       absent                duplicate node          add QuoteMir
Callout                     absent                duplicate node          add CalloutMir
Break                       ThematicBreakMir      duplicate node          add BreakMir; retain thematic break
Text run                    TextMir               duplicate node          reuse
Strong                      StrongMir             duplicate node          reuse
Emphasis                    EmphasisMir           duplicate node          reuse
Inline code                 CodeSpanMir           duplicate node          reuse
Link                        LinkMir               duplicate node          reuse and shared safety
Source spans                SourceSpan            MachinaSourceSpan       retain both through provenance
Stable identity             absent                generated strings       bind once in DocumentMirBinder
Fit policy                  absent                layout text policy      remain host binding, not DocumentIR
Overflow policy             absent                layout box policy       remain host binding, not DocumentIR
Text role                   absent                role string             generic node metadata/defaults
Owning layout box           absent                projected attachment    remain TextDocumentBinding concern
Runtime fitting metadata    absent                layout text policy      remain layout host contract
Interpolation               absent                React-only path         explicitly deferred from canonical M1
```

## Converged source pipeline

```text
Markdown source -> Markdown parser -> Markdown AST -> DocumentMir lowerer
TS Text/plain  -> TS parser       -> structural Text frontend
                                      -> shared Markdown inline parser
                                      -> DocumentMir
                                                -> DocumentMirBinder
                                                -> immutable canonical document facts
                                                -> projected document tables / Markdown consumers
```

`DocumentMir` owns document structure: block vocabulary, inline vocabulary,
roles, provenance, stable node identity, nesting, and link safety. A Machina
text host owns spatial facts: owning layout box, fit policy, overflow policy,
wrapping, and runtime text fitting. Those facts must remain in a binding or
layout relation rather than being copied onto every document node.

## M1 implementation evidence

- `DocumentMir` now has generic `DocumentMetadata`, `DocumentNodeMetadata`,
  and source-kind provenance.
- `DocumentMirBinder` supplies stable identity/parent/order/default role and
  is the single safe-link validation point.
- Markdown calls that binder after normal AST lowering.
- Plain `Text("...")` synthesizes a `ParagraphMir`; TS XML structurally builds
  headings, paragraphs, lists/items, code, quote, callout, and break nodes.
- TS inline shorthand calls the Markdown-owned parser and then the existing
  Markdown-to-MIR inline lowerer. XML is never converted to Markdown text as a
  document and Markdown is never converted to fake TS XML.
- The projection provider walks canonical `DocumentMir` nodes, including
  nested inline and list-item relationships.

## Current bounded limitation

## Closure: presentation binding

`TextPresentationBinding` is the immutable boundary between `DocumentMir` and
host presentation. It records a stable binding/document ID, semantic host,
theme, document class, per-node authored class assignments, and assignment
provenance. Layout attachment remains separate and is projected with owning
box, fit, wrap, line-limit, and overflow facts in `text::Bindings`.

> DocumentMir owns document meaning. Presentation binding owns host behavior
> and visual attachment.

> `className` is presentation metadata attached during binding. It is not
> document structure.

The React binder precomputes document bindings once, finds a `Document` root by
source span, and traverses canonical blocks/inlines into safe semantic React
elements. It no longer parses inline shorthand or TS-XML document children.
The old source-specific helpers are unreachable for document roots. LSP now
locates a canonical node by provenance span and reports its node ID, role,
frontend, theme, host, and presentation assignment; it no longer uses the
former token-description branch for document meaning.

`text::Bindings` exposes binding ID, document ID, owning box, semantic host,
theme, fit/minimum/preferred sizes, line limit, wrapping, overflow, document
class, and source. Runtime-selected font size remains browser state and is not
compiler truth. Existing layout text-fit hosts remain the common local fitter
contract; no arbitrary React subtree is made eligible.

Canonical interpolation remains deferred. It requires a typed document inline
contribution, not a React-node escape hatch. Arbitrary components and HTML are
still prohibited.

## Final closure additions

Plain `Text("...")` is now intercepted only in the React profile after its
plain frontend has synthesized `Document → Paragraph → TextRun` and bound it.
It renders through the same canonical traversal as XML documents; there is no
raw string-to-DOM branch. Literal text remains React-escaped.

> Plain `Text(...)` is a source convenience, not a separate document runtime.

LSP definition first resolves the canonical node by provenance. When the
document owner is attached by a layout binding, it follows that canonical
function identity to the `LayoutSlotSymbol` and navigates to the owning box.
Bounded role identities have no fabricated definition target; role declaration
navigation is deferred until an authored role/theme language exists. Unknown
authored roles emit `COPE-TEXT-PRESENTATION-0002`; non-static presentation classes emit
`COPE-TEXT-PRESENTATION-0001`; missing canonical renderer bindings emit
`COPE-DOC-RENDER-0001`.

> Navigation follows canonical semantic identities from document to
> presentation binding to owning layout box.

> Every downstream consumer receives bound document meaning. None reconstructs
> it from source syntax.

## Validation run for this progression

- `dotnet build src/Copeland/Copeland.TS/Copeland.TS.csproj --no-restore`
- `dotnet build src/Copeland/Copeland.Cli/Copeland.Cli.csproj --no-restore`
- `dotnet test tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj --no-restore --filter TextDocument --nologo` (3 passed)
- `dotnet test tests/Copeland/Copeland.Markdown.Tests/Copeland.Markdown.Tests.csproj --no-restore --filter MarkdownPipeline --nologo` (35 passed)
- canonical React emission test (semantic heading/strong/code/link/list and
  presentation class preservation) passed
- LSP project compiled with canonical document lookup

The prior website browser proof source remains unchanged; screenshots and a
new browser pass are still required before visual closure. The full solution
is presently externally blocked by locked output files; the process was not
terminated.
