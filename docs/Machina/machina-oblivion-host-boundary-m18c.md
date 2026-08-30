# Machina / Oblivion Host Boundary M18c

## Roles

`Machina.UI` is the reusable native C# UI stack. Presenter is a development palette, playback runner, Avalonia window host, Aurelian adapter, and export/diagnostic tool. Oblivion is a first-class product. Avalonia is a fallback host capability, not a product model.

```text
Presenter may host Oblivion.
Oblivion must never depend on Presenter.
```

Presenter translates palette navigation, surface sizing, pointer capture, and development export concerns into product application/session calls. Oblivion returns semantic Machina UI and typed application outcomes. Layout and raster output stay below product semantics.

M18d deleted the temporary Presenter adapter. Machina.Runtime now owns generic
pointer and scrollbar mechanics, Oblivion owns product interaction/action/effect
meaning, and Presenter retains only generic host projection and platform work.
See `docs/Machina/machina-product-host-contracts-m18d.md`.

Avalonia classification:

| Capability | Classification |
| --- | --- |
| desktop window and lifecycle | `AVALONIA_FALLBACK` |
| backend input collection | `AVALONIA_FALLBACK` |
| current semantic product cards and reading surfaces | `NATIVE_NOW` |
| future editor or complex widget without a mature Machina control | `NATIVE_LATER` |
| OS integration with no product-semantic value in replacement | `NO_REASON_TO_REPLACE_YET` |

No control-mirroring abstraction, widget factory, lifecycle system, or broad Avalonia replacement is introduced by M18c.
