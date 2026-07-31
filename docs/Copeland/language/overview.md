# Copeland TS overview

Copeland TS is a closed-world, TypeScript-shaped language. It keeps familiar
declaration and expression syntax where that helps authoring, but does not
inherit JavaScript's dynamic object model, coercions, ambient globals, or DOM
semantics. The compiler owns language meaning; C#, JavaScript, projected
tables, and browser materialization are consumers of that meaning.

## Use the data structure that matches the domain

| Domain | Copeland representation | Canonical meaning |
| --- | --- | --- |
| Behavior and reusable UI | functions and components | bound symbols, component instances, and transitions |
| Structured values | records, classes, enums, and Results | validated bound values and MIR |
| Tabular data | `record table`, TSON tables, and projected relations | typed table model; projected relations are read-only views |
| Spatial relations | layouts, boxes, streams, and bindings | normalized Machina layout facts |
| Text | XML-shaped documents and Markdown-style inline syntax | `DocumentMir` / text document model |
| Application change | component state, events, and bounded effects | component frame and presentation delta |
| Renderer realization | attachment plans and renderer adapters | `HostAttachmentMir`; adapters own opaque subtrees only |

Components are functions with private local presentation domains. A caller owns
the component's outer host and semantic instance identity; a component owns its
captures, local layout/stream facts, state, and presentation. A renderer is an
adapter, not a component ontology and not an alternate source of application
state.

## Start here

- [Feature status](../reference/feature-status.md) answers what is currently
  implemented, bounded, experimental, or unsupported.
- [Feature inventory](../copeland-feature-inventory.md) links each major
  feature to its syntax, semantic owner, consumers, and tests.
- [Components](components.md), [layouts](layout.md), [text documents](text-documents.md),
  and the [browser runtime](browser-runtime.md) describe the current
  presentation-domain boundaries.
- [Authoring guide](../authoring/copeland-typescript-guide.md) is the detailed
  current syntax guide. Older `language/`, `architecture/`, and `reviews/`
  documents are decision or evidence records unless this overview links to them.

## Important boundaries

Parsing can vary by source frontend (`.ts`, `.tsx`, layout/CSV-shaped input,
or TSON), but canonical meaning must not. Source is parsed and bound before
backends consume it. Generated artifacts transport compiler facts; they do not
re-bind source or define new language semantics.

The browser has two deliberately separate kinds of state:

- compiler semantics: definitions, instances, attachment identity, host box,
  capability requirements, and compiler-emitted transition contracts;
- runtime realization: concrete DOM hosts, adapter roots, observers, browser
  event listeners, lifecycle counters, and cleanup.

The former is owned by Copeland. The latter is currently implemented by the
TSPack-generated `@copeland/browser-v1` runtime. No DOM node or React root is
a compiler identity.

## Small, real examples

The fixtures are the syntax authority. For example, an immutable record is
constructed only in an expected record context and updated with `with`:

```ts
record Settings {
    title: string;
    count: number;
}

function Rename(settings: Settings): Settings {
    return settings with { title: "Updated" };
}
```

Payload enums and `match` are the bounded tagged-data surface; `switch` is a
frontend alias where accepted, not a second runtime model. Fallible calls use
`Result`, postfix propagation `?`, `try`/`except`, and postfix unwrap `!`.
See the linked fixture-backed pages in the feature inventory for the exact
currently accepted forms.

## Non-goals that are easy to assume incorrectly

Copeland TS is not general TypeScript. `any`, `var`, `null`, ordinary
`undefined`, ternary expressions, optional chaining, strict-equality spellings,
general object literals, prototype behavior, arbitrary JavaScript execution,
and TypeScript structural typing are not product semantics. React and Custom
Elements are demonstrated renderer boundaries; Vue, Svelte, Lit, Blazor, SSR,
hydration, and browser effect execution are not implemented features.
