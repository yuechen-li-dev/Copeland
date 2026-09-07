# TinyFarm presentation fidelity baseline — M25

The M24 native default was 1280×720, with a 1280×720 compositor/readback target, a fixed window, and an empty renderer resize implementation. World presentation used 48 pixels per metre inside a `(22,24,904,648)` viewport. HUD geometry and the historical decorative Profile tree dominated the frame. The sprite texture scope used nearest sampling, including painterly art.

The M25 legacy capture mode reproduces those resources, camera, and composition. Its control legend and reduced contextual prompt use the current implementation, so `ui-on-before.png` is a baseline reconstruction, not an untouched historical executable capture. `world-only-before.png` removes that HUD without improving the old camera or sampling.

## Measured resource and display audit

Screen rectangles include source padding. Ratios below are source texels per displayed pixel **per axis**, not a quality score. The JSON also reports the area ratio. PNG file bytes do not represent GPU residency.

| Asset | Source pixels; file bytes | Semantic size / presentation ownership | Original screen pixels | Final 1080p screen pixels | Axis ratio before → after | Sampling before → after |
|---|---|---|---|---|---|---|
| Tree, typical instance | 1218×1292; 1,844,497 | Trunk radius 0.34m; independent canopy; art base height 178px at 48px/m | 167.8×178 | 319.5×338.9 | 7.26 → 3.81 | Nearest → Linear |
| Farmhouse | M24 1374×1145; 1,924,238. M25 1408×1117; 2,215,427 | Walls 3.6×2.3m; roof occlusion 4.6×3.3m; height 3.4m, unchanged | 285.6×238 | 481.1×381.6 | 4.81 → 2.93 | Nearest → Linear |
| Meadow slab | 1254×1254; 2,188,465 | Authored 16×10m ground; final decorative backdrop fills framebuffer | 768×480 | 1920×1080 backdrop | 1.63×2.61 → 0.65×1.16 | Nearest → Linear |
| Bridge / bank | Analytic geometry and live field; no source PNG | Bridge 2.4×1.5m; wet/dry comes from field | Bridge 115.2×72 | Bridge 199.4×124.6 | Not applicable | Analytic; field linear UNORM |
| Character | 312×312 atlas cell; shared source file 1,766,923 | Existing actor footprint unchanged; art height 70px at 48px/m | 70×70 | 121.2×121.2 | 4.46 → 2.57 | Nearest → Nearest |
| Well | 312×312 cell, same shared atlas | Existing M11 prop semantics; art base 76px | 76×76 | 131.5×131.5 | 4.11 → 2.37 | Nearest → Nearest |
| Lantern | 312×312 cell, same shared atlas | Existing prop semantics; art base 58px | 58×58 | 100.4×100.4 | 5.38 → 3.11 | Nearest → Nearest |
| Fence | 312×312 cell, same shared atlas | Existing prop semantics; art base 54px | 54×54 | 93.5×93.5 | 5.78 → 3.34 | Nearest → Nearest |

Tree detail was visibly wasted by minification and clipping. The farmhouse lost material detail and had a more fundamental perspective mismatch. The approved replacement follows the [art projection contract](tinyfarm-art-projection-contract-m25.md). Pixel atlas minification is substantial but belongs to an intentionally crisp path. The slab now reaches its source-detail limit: filling 1440p cannot create new painted detail. Bridge/bank quality is constrained by the current analytic/field realization, not PNG resolution.

The complete per-resolution records, source SHA256 hashes, exact dimensions, and independent instance multipliers are in `artifacts/tinyfarm-high-fidelity-presentation-m25/asset-scale-audit.json` and `source-detail-utilization.json`. M24's unused alternative farmhouse remains approved historical art, but is not resident. Rejected M25 studies also stay out of runtime residency.

## Resolution, composition, and capture ownership

M25 defaults to a 1920×1080 physical framebuffer. UI retains 1280×720 layout units with uniform scale and centered aspect offsets. The world independently fits a 16×13 presentation region: the existing 16×10 ground plus three metres of canopy headroom. At 1080p this is 83.0769px/m, at 1440p 110.7692px/m. The decorative meadow fills remaining aspect space; it does not enlarge semantic navigation bounds.

The composition law is background/slab → paths/patches and bank feather → live semantic-masked water → bridge → feet-Y ordered objects and actors → player visibility treatment → effects → local prompt → optional normal HUD → optional inspector. Existing modal screens remain usable. HUD visibility never enters world layout, simulation, camera, or save/replay state.

`FramebufferSize` owns compositor and swapchain dimensions, including resize. The tested display reported 1:1 window-to-framebuffer scale. A real 150%/200% desktop DPI configuration was not tested; physical-target plumbing and exact readback sizes were tested. An OS work-area clamp initially returned 2560×1421 for a decorated window. The native launcher now detects clamping and reapplies the requested size with a borderless window. Historical 1421p captures remain diagnostic evidence, not 1440p qualification.

F9 hides normal HUD; F10 independently toggles inspection; F11 requests a clean native frame and restores previous visibility after capture. `--world-only` starts the normal executable directly in play with HUD hidden. Screenshots are explicit native readbacks, not resized browser images. See the [milestone report](../milestones/tinyfarm-high-fidelity-world-presentation-m25-report.md) for exact reproduction commands and limitations.
