# Machina and Avalonia product pressure — M19a

M19a encountered no blocking widget gap. The current visual projection remained useful, while exact work was better performed through the semantic product surface.

| Capability | Classification | Needed now? | Evidence and decision |
|---|---|---:|---|
| Markdown reading | NATIVE_NOW | Yes | Existing Copeland Markdown → `DocumentMir` → Machina path made technical cards readable. Keep it. |
| Page/card navigation | NATIVE_NOW | Yes | Existing tabs/list and semantic page/card IDs were sufficient. No tree control is required. |
| Split card/inspector panes | NATIVE_NOW | Yes | The inspector remains useful for human scanning; no replacement pressure appeared. |
| Expanded-card reading | NATIVE_NOW | Yes | Meaningful reading mode for long Markdown; it remains session state. |
| Plain text input | NATIVE_LATER | No | The actual edit was cleaner through a code editor. Add only after a product-owned metadata edit proves useful. |
| Rich text/Markdown editing | NO_REASON_TO_REPLACE_YET | No | Explicitly out of scope and unnecessary in the trial. |
| Tree navigation | NO_REASON_TO_REPLACE_YET | No | Four pages and stable filtered card lists did not justify a tree. |
| Menus | NO_REASON_TO_REPLACE_YET | No | Semantic commands and existing controls cover current actions. |
| Dialogs | NO_REASON_TO_REPLACE_YET | No | No modal workflow was needed. |
| Tables/data grids | NO_REASON_TO_REPLACE_YET | No | Diagnostics and artifact lists are small; structured JSON/text is better for agents. |
| Charts | NO_REASON_TO_REPLACE_YET | No | No product evidence called for charts. |
| File dialogs | AVALONIA_FALLBACK | Later | A human `open-source`/`open-artifact` capability may use the host platform. It does not justify a native picker now. |

Avalonia OSS can remain the host fallback for inherently platform-integrated file operations. Nothing encountered was commercial-only. Simple card/list/inspector composition avoids complex widgets, and building them now would not add product value.
