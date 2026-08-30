# Machina / Oblivion Host Boundary M18c

## Roles

`Machina.UI` is the reusable native C# UI stack. Presenter is a development palette, playback runner, Avalonia window host, Aurelian adapter, and export/diagnostic tool. Oblivion is a first-class product. Avalonia is a fallback host capability, not a product model.

```text
Presenter may host Oblivion.
Oblivion must never depend on Presenter.
```

Presenter translates palette navigation, surface sizing, pointer capture, and development export concerns into product application/session calls. Oblivion returns semantic Machina UI and typed application outcomes. Layout and raster output stay below product semantics.

The temporary `PresenterOblivionHostAdapter` owns the compatibility translation required by the established playback paths. It may reference Oblivion and Presenter types; no Oblivion project may reference it. M18d owns its elimination by moving product page composition and interaction maps inward while retaining a thin presenter translation.

Avalonia classification:

| Capability | Classification |
| --- | --- |
| desktop window and lifecycle | `AVALONIA_FALLBACK` |
| backend input collection | `AVALONIA_FALLBACK` |
| current semantic product cards and reading surfaces | `NATIVE_NOW` |
| future editor or complex widget without a mature Machina control | `NATIVE_LATER` |
| OS integration with no product-semantic value in replacement | `NO_REASON_TO_REPLACE_YET` |

No control-mirroring abstraction, widget factory, lifecycle system, or broad Avalonia replacement is introduced by M18c.
