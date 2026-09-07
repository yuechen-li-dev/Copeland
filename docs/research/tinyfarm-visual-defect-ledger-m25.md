# TinyFarm visual defect ledger — M25

Outcome **B**: world/HUD isolation, physical resolution, approved farmhouse perspective, and sampler routing work. The exact remaining fidelity seam is the Riverside shoreline: its corrected alpha edge still describes a softly feathered rectangular cyan field. `riverbank-after.png` makes this visible without HUD interference.

| ID / class | Observed defect | Evidence | Disposition |
|---|---|---|---|
| M25-C1 — path/bank/slab seam | Shoreline lacks painterly material structure and still reads as a low-resolution rectangular field | `riverbank-after.png` | Primary next pressure |
| M25-C2 — path/bank/slab seam | Flat path, crop patch, and bridge contrast with detailed foliage/house | `world-only-1080p-after.png` | Same ground-material pass |
| M25-A1 — world composition | Added headroom prevents clipping, but meadow extension and quiet water leave weakly composed space | `world-occupancy.json` | Refine within existing camera |
| M25-B1 — scale/style mismatch | Corrected house now fits the art convention; pixel props and tall M24 tree silhouettes remain inconsistent across the catalog | `world-only-1440p-after.png` | Retain assets; reassess after ground pass |
| M25-D1 — character readability | Translucent foreground silhouette keeps player locatable; pixel style and visibility treatment remain provisional | `world-only-1080p-after.png` | Later character pass |
| M25-E1 — interaction presentation | Prompt is local and smaller, but some labels remain wordy and targets less legible than their labels | `world-only-1080p-after.png` | Bounded future refinement |
| M25-F1 — UI | Full HUD remains visually large when enabled | `ui-on-1080p-after.png` | Deferred; optional HUD solves inspection now |
| M25-G1 — gameplay/content | Static evidence does not establish long-term play appeal | World-only ladder | No gameplay expansion in M25 |

All evidence paths above are relative to `artifacts/tinyfarm-high-fidelity-presentation-m25/`. The machine-readable counterpart is `visual-defect-ledger.json` there.

Choose **one primary next milestone: painterly ground-material composition, beginning with the Riverside shoreline and its connection to the path and bridge**. Preserve semantic wet/dry authority; make the visible shore follow that authority with coherent soil, grass, and water transitions. This directly addresses the largest defect revealed by the clean frame. UI redesign and wholesale asset regeneration should wait.
