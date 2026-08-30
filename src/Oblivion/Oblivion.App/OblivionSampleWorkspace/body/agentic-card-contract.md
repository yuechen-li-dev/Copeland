# Agentic card contract

Every Oblivion card is a self-contained applet.

- The shell owns navigation, selection, scrolling, routing, persistence loading, and card ordering.
- The card kind owns its model, local state, actions, diagnostics, artifacts, compact view, inspector view, and future effects.
- Locality of change is the core design rule.
- Markdown rendering bugs should be fixable inside the Markdown card handler.
- Future CodeFact execution should route through shared action and effect contracts, not ad hoc shell branches.
