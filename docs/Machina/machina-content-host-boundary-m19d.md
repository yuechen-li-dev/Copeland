# Machina content host boundary — M19d

## Boundary

The content host is a strangler seam inside existing card-body geometry:

```text
OblivionCard + session reading state + resolved artifact facts
    -> OblivionContentPresentationPlan
    -> native fallback | Avalonia control | external Mermaid artifact
    -> existing Machina card body rectangle
```

It is not a new authoring model and does not change `OblivionCard`, page ordering, Machina layout authoring, or persistence.

## Owners

| Concern | Owner |
| --- | --- |
| body source, artifact declarations, identity, provenance | `Oblivion.Model` |
| presenter dispatch, reading/scroll/focus contract, diagnostics | `Oblivion.UI` |
| safe artifact resolution, external renderer process/provenance | `Oblivion.App` |
| card/body measurement rectangle, chrome, expansion, selection | Machina + Oblivion workbench projection |
| Avalonia controls, shaping, selection/copy, image decode | `Machina.Presenter.Sample` host integration |
| deterministic export/playback fallback | native Machina raster path |

Avalonia references remain in the host sample. Searches over `Oblivion.Model` and `Oblivion.Persistence` remain empty. `DocumentMir` remains a derived UI/compiler projection and is absent from the durable model.

## Measurement boundary

Machina resolves the body rectangle first. The foreign presenter receives that bounded rectangle as `Width`/`Height`; it does not re-layout the shell or card chrome. Code and artifact handlers provide a 420-pixel expanded preferred height. Avalonia measures internal content and supplies local scroll when it exceeds that bound.

The plan exposes stable content identity, content/presenter kind, source reference, artifact facts, scroll/focus contract, and diagnostics. It deliberately does not expose Avalonia `Control`, `Measure`, `Size`, or input event types.

## Input, focus, scroll, and event boundary

- Pointer input over card headers stays on the Machina image and owns select/expand/collapse.
- Pointer/wheel input inside an overlay goes to the hosted control and its `ScrollViewer`.
- Selectable text owns focus, selection, and copy only.
- Product actions continue through `OblivionUiActions` and `OblivionInteractionDispatcher`.
- The presenter has no reference to navigation/session dispatch.
- Future link activation must emit a semantic request outward; direct navigation mutation is forbidden.

The live host removes/rebuilds the overlay after every shell render. This keeps it a projection of current session state and prevents a control instance from becoming truth.

## Mermaid external boundary

`IOblivionDiagramRenderer` is intentionally narrow. The production adapter receives source and provenance, invokes an explicitly configured executable with `ProcessStartInfo.ArgumentList`, bounds execution to 30 seconds, and produces a source-hashed PNG. It never invokes a shell, browser, network request, or WebView. Tests inject a deterministic fake.

## Headless boundary

Neither selector nor artifact resolution initializes Avalonia. CLI inspect/show/artifact commands remain unchanged. Export and canonical playback render the existing native body. An unavailable renderer yields a typed warning, not a workspace-load failure.

## Diagnostics

The inspectable plan provides content ID, reading state, content type, presenter kind, source reference, scroll/focus contract, and diagnostics. The inspector reports the selected primary presenter. Missing PNG and Mermaid failures have stable codes. Exact platform glyph geometry remains the presenter's concern; there is no screenshot-golden or pixel-comparison gate.

