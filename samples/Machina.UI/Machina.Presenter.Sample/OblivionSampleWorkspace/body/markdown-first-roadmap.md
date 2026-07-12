# Markdown-first roadmap

Oblivion treats Markdown as a **text-card body language**, not as the whole page document model.
Markdown cards come first.

- Workspace root remains JSON.
- Cards remain TOML assets.
- Markdown bodies live in `.md` files.
- Use `DocumentMir` as the rendering seam into presenter UI nodes.
- Code execution is deferred to M13+.

1. Load `.md` body files through `Copeland.Markdown`.
2. Render headings, lists, code fences, and links clearly enough to inspect.
3. Keep code fences static text with no Roslyn or xUnit execution.

```text
Oblivion page -> stack of typed cards
Markdown body -> note-card language only
Single-file Markdown -> future export target
```

Use the [M12a frontend](../../../../docs/Copeland/history/copeland-markdown-frontend-m12a.md) as the compiler path and inspect the [M12b integration notes](../../../../docs/Machina.UI/history/machina-oblivion-markdown-body-integration-m12b.md) beside it.
