# TinyFarm MVP Gate A — playable opening review build

Date: October 3, 2026. Scope: the approved [MVP design](../design/tinyfarm-mvp-design-spec.md), Gate A only.

The normal native launch now presents a coherent opening route with diagonal movement, gardening, useful food, an attacking enemy, a defensive move, recovery and a return home. The implementation is ready for human review. **Gate A's product exit remains open:** no fresh human player has yet completed the loop without coaching, approved its art, or judged its combat feel. Automated execution proves functionality; it does not prove fun or ten to twenty minutes of engaging content. Gate B has not started.

## Launch and review

```powershell
dotnet run --project src/TinyFarm/TinyFarm.Native -c Release
```

Alternatively double-click `Play-TinyFarm.cmd`. Enter begins; N continues. WASD moves, E interacts, J swings, Space dodges, 1 then K plants, R eats broth, I opens pockets/plans, Escape pauses. F saves, N loads. F9 hides the HUD, F10 toggles the independent inspector, F11 captures a clean frame. [Player instructions](../../src/TinyFarm/README.md) cover persistence and reproduction commands.

Review evidence:

- [World frame](../../artifacts/tinyfarm-gate-a/garden-world.png) and [ordinary HUD frame](../../artifacts/tinyfarm-gate-a/garden-hud.png).
- [Continuous native recording](../../artifacts/tinyfarm-gate-a/opening-loop.mp4), [Mara conversation](../../artifacts/tinyfarm-gate-a/mara-conversation.png), [home interior](../../artifacts/tinyfarm-gate-a/hearth-house.png), [approach](../../artifacts/tinyfarm-gate-a/riverwood-approach.png), [attack telegraph](../../artifacts/tinyfarm-gate-a/slime-telegraph.png) and [return home](../../artifacts/tinyfarm-gate-a/home-after-adventure.png).
- [1440p world](../../artifacts/tinyfarm-gate-a/2560x1440/garden-world.png) and [1440p recording](../../artifacts/tinyfarm-gate-a/2560x1440/opening-loop.mp4).
- [Default native window](../../artifacts/tinyfarm-gate-a/native-window-1920x1080.png), [actual resize to 1600×1000](../../artifacts/tinyfarm-gate-a/native-resized-1600x1000.png), and [presentation inspector](../../artifacts/tinyfarm-gate-a/presentation-inspector.png).

For the next review, begin a new game without a walkthrough. Observe whether the player discovers the stove and bed, reads the slime's jump, uses food as expedition preparation, and returns to harvest a crop they planted. Record actual duration and confusion. The automated video is an optimized roughly half-minute route at ten recorded frames per second with no audio; it is not that playtest.

## What changed

The earlier supper scenario demonstrated subsystem integration: planting immediately checked an objective, the one-health slime could not hurt the player, and the best art lived in Riverside. Gate A instead has a ripe starter turnip and an empty plot, a one-turnip healing recipe, one-night watered growth, an explicit player bed and one four-health attacking slime. The two-plot garden and six seeds are a deliberately smaller demonstration than the full campaign's planned garden.

The garden, Hearth House, Riverwood Approach and Old Burrow form the playable route. Other roads reject player travel and their local prompts identify them as unfinished. NPCs retain access to their own route graph; closing the player slice does not redefine their navigation authority. Mara stays near the garden and gives short opening advice. Her conversation does not start the closed prototype letter quest. The former supper scenario remains available through `--legacy-supper`; historical proof modes retain their original definitions.

The curated content is a typed C# overlay over existing compiled Copeland TS content in `TinyFarmSliceContent`. Its identity is `tinyfarm-sleeping-spring-gate-a-v1` plus the source identity. The inherited source tables remain provenance for the base, rather than pretending to serialize the C# overlay. Future content changes must version this overlay identity; this is the first unshipped version. No additional content parser or reflection-based authoring framework was introduced.

## Movement, combat and animation

Normalized diagonal movement and a bounded dodge both use the existing spatial sweep. The opening movement speed is about three metres per second. Dodge lasts fifteen 60 Hz ticks, travels about 1.54 metres, and has a thirty-six-tick cooldown. It does not tunnel through walls or the blocked river. The authored bridge crossing remains traversable.

Sword input starts an eighteen-tick swing without selecting an enemy. The active window checks range and facing, hits once, and deals two damage. One subsequent input may be buffered; dodge cancels it. The slime watches, commits to a fixed direction during its 42-tick amber telegraph, lunges, then recovers. Contact deals two damage with 42-tick hurt invulnerability. Dodge prevents contact damage. Two connected swings clear the encounter; defeated enemies do not keep attacking.

At zero health the player is rescued to the burrow entrance with half health and temporary invulnerability. The encounter resets and inventory remains. This is Gate A's forgiving retry policy; the full campaign's proposed consumable/time penalty is deferred. Broth consumes one item and restores up to four health; it is not consumed at full health. Rest restores full health.

The gardener uses sixteen imported walking poses across four directions. The slime has four phase poses. Their presentation follows immutable simulation ticks. Sword trails and telegraphs use existing analytic/effect presentation. There is no separate animation clock, physics simulation or new package dependency. This follows the user's “laziest correct thing” instruction; dedicated action clips, a Dominatus animation machine or Bepu can be introduced if a demonstrated need justifies them.

## Farming, time and persistence

Starter crop ownership is distinct from the player's planted crop. Accepted player planting records provenance; only harvesting such a crop sets the own-harvest milestone. Cooking, sleeping, defeating the enemy and actually returning home have separate facts. Clearing the cave alone cannot complete the opening loop. Completion is a quiet message and leaves play running.

Outdoor time advances one game minute per second. Dungeon time pauses the calendar while movement and combat continue. Menus, dialogue, pause and focus loss stop play. Sleep is available only beside the player's bed, advances to next morning and resolves watered growth once. At 21:30 a status warning appears; crossing 22:00 outdoors returns the player home for the same overnight transition without a fee. Even a larger valid wait crossing midnight resolves that transition once.

Sleep and automatic overnight return save the new morning. Their checkpoint follows any earlier background manual save to the same slot. The opening uses a separate `sleeping-spring-gate-a` slot, so the supper save is preserved. Player energy, shipping and broader recovery economics are deferred; the existing energy model belongs to NPCs and was not silently repurposed.

Slice state and planted provenance are persisted in save version 11 and participate in semantic hashing. Presentation visibility and resolution do not. Save, replay and field-hash JSON use generated metadata with a closed type graph. The focused opening suite passes with `JsonSerializerIsReflectionEnabledByDefault=false`. A compatibility test checks that generated field serialization preserves the existing canonical hash bytes. No runtime reflection was added for these capabilities.

## Art, spatial authority and presentation

The approved elevated orthographic farmhouse, M24 tree and meadow are reused. Six new generated sources supply the gardener, slime, Mara, props, floors and turnip. Exact prompts, actual dimensions, file hashes, source filenames and review status are recorded in [asset provenance](../../src/TinyFarm/TinyFarm.Native/Assets/GateA/provenance.json). These selected outputs are pinned, but are **pending human art approval**. No source PNG was edited or assumed to match its requested aspect ratio.

Two reusable Aurelian tools were added:

- `GridSpriteSheet`: cold import of regular RGBA cells, alpha trimming, pixel pivots and a uniform pose scale. Integer grid boundaries accept sources such as a 1254-pixel-wide four-column sheet without stretching or rejecting it.
- `GroundBrushRasterizer`: deterministic cold realization of soft path/bank strokes with union coverage and restrained noise. Overlapping strokes do not accumulate dark bands. Realized textures are retained and uploaded once.

Source dimensions, display size and collision size remain distinct. The painterly tree uses a presentation scale of 0.72 in the garden; the house uses 0.78 and anchors to its door threshold. Gate A explicitly reauthors the cottage wall footprint to match that placement and moves the fence out of the walking route. The woodland river uses authored blocked regions with a dry bridge gap. These are ordinary changes to opening-map definitions, not PNG-derived collision or a renderer-owned authority path. Legacy M24/M25 definitions remain intact.

The native target remains 1920×1080, with 2560×1440 supported. The opening fits a sixteen-by-eleven-metre projection region with uniform scaling and restrained garden headroom. UI retains its scalable 1280×720 authoring canvas and overlays the world. Outdoor ground extends through aspect margins; small rooms are centered against quiet background. No resolution-specific object coordinates or new camera system were added. Actual 1600×1000 resize retargets the framebuffer and leaves the semantic hash unchanged. Raster targets use physical framebuffer dimensions; mixed-monitor DPI switching was not separately qualified.

Composition is ground/slabs → banks/river/paths/bridge → soil/crops/telegraphs → objects/actors in feet order → sword/effects → local prompt → optional HUD → optional inspector. All new painted resources use linear sRGB sampling and straight alpha. The retained pixel atlas keeps nearest sampling and text keeps derivative-aware MSDF reconstruction. Cached resources are submitted through their owning sampler renderer, including the occluded-player silhouette; this fixed an actual unknown-texture-handle failure.

Riverwood's river is a passive painted strip with soft bank edges and a wooden deck. It is not the M21 live Riverside field; that owner and its legacy proof path remain separate. No semantic wet-mask framework or new water simulation was added to this slice. Captures expose the remaining straight shoreline and flat bridge, recorded in the defect ledger.

## Detail, memory and performance

[Asset audit](../../artifacts/tinyfarm-gate-a/asset-audit.json) reports measured source sizes, alpha-trimmed cell dimensions, intended visual size and source pixels per displayed pixel. At 1080p the gardener's maximum height is about 131 pixels, the tree frame about 262, the house frame about 335 and the mature crop about 65. At 1440p these scale uniformly by 4/3. These are footprint metrics, not quality scores or collision dimensions.

Fourteen sprite textures remain cached after cold attach, including the retained historical atlas. Their RGBA8 payload totals **77.81 MiB**. [Memory accounting](../../artifacts/tinyfarm-gate-a/texture-memory.json) excludes driver alignment, fonts/profiles, portrait, field image, CPU copies and compositor/readback targets; it is not measured total GPU residency. No downsample variants or mipmaps were added. Small cutouts still have substantial source oversampling, so intentional runtime variants are a future memory refinement. Captures show no gross alpha fringe in the garden, wooden room or dark cave; a complete multi-background edge-diagnostic sweep is not claimed.

Performance evidence comes from 180 measured stationary home frames per HUD state after warmup, separate from screenshots and video readbacks. These are complete native CPU wall times through synchronous Vulkan submission/presentation, with VSync disabled in the request. They are not GPU timestamps or a long-session minimum-hardware qualification. The approximately 6.94 ms plateau may include display pacing. Exact p50/p95/p99 and draw counts are in [1080p performance](../../artifacts/tinyfarm-gate-a/performance.json) and [1440p performance](../../artifacts/tinyfarm-gate-a/2560x1440/performance.json). HUD on produces about 113 draws per frame; off about 53. Sprite uploads stay at fourteen.

Periodic full-frame recording readbacks cause large spikes, roughly 60 ms at 1080p and 100 ms at 1440p. [Native proof](../../artifacts/tinyfarm-gate-a/native-proof.json) reports them explicitly. Normal-frame measurements exclude this cost. Recording is developer evidence tooling, not a qualified smooth gameplay recorder. Windows lists an RTX 3070 driving a 2560×1440 display and an inactive AMD adapter; [hardware data](../../artifacts/tinyfarm-gate-a/display-hardware.json) records this without claiming selected-device GPU telemetry.

## Verification and resolved friction

- Release `TinyFarm.slnx` build: zero warnings and errors.
- Full solution tests: 27 Spatial2D and 362 TinyFarm tests pass; none skipped.
- Opening tests with JSON reflection disabled: 20 pass, including generated save/replay, mid-dodge restoration, exact crop provenance, enemy damage/dodge, sleep/autosave ordering and the opening conversation.
- Authorized InputMan sibling validation: 76 Core, 19 MonoGame connector and 7 Stride connector tests pass. Missing alphabet keys and Silk mappings were added; existing F1–F12 changes were preserved.
- Continuous native walks at 1080p and 1440p: actual keyboard bridge → game actions → resolver → Vulkan, with dialogue, harvest, cook, plant/water, sleep, own harvest, telegraph/dodge, damage/healing, defeat, retreat, home and exact save/load. No coordinate writes or fixture loads occur during play. Both resolutions produce the same final semantic hash.
- Default visible-window smoke: title, Enter, movement, pause blocking input, 1920×1080 physical framebuffer and actual 1600×1000 resize pass. F9's start-frame semantic assertion passes; [pixel comparison](../../artifacts/tinyfarm-gate-a/hud-world-proof.json) confirms an unchanged 1,155,200-pixel world region away from chrome.

Real failures were resolved at their owners: schedule validation retained required bedtime windows; the importer handles non-divisible grid dimensions; opening authoring removes NPC/item interaction interception and a blocking fence; portals target an actor already inside their trigger regardless of facing past its center; sampler ownership fixes occluded-player submission; manual/autosave ordering prevents stale checkpoints; and Mara's new advice no longer triggers a closed legacy quest. No native coordinate patch or special proof-only executable was used to make the walkthrough pass.

## Remaining pressure and next step

The [visual defect ledger](../../artifacts/tinyfarm-gate-a/visual-defect-ledger.json) names sparse cave composition, communal prototype beds, limited action animation, straight water/flat bridge, inherited sparkles, threshold discoverability, provisional sound and unmeasured first-play duration. These are visible limits, not hidden qualifications.

**One next pressure: fresh-player combat feel and comprehension within this opening loop.** Judge whether the jump can be read, dodging feels responsive, sword contact is clear, food helps, and returning to the garden feels worthwhile. Refine those findings in Gate A before expanding the dungeon, economy or campaign. This review build is the concrete result for that judgment.

The [manifest](../../artifacts/tinyfarm-gate-a/manifest.json) records artifact hashes, source snapshot and diff accounting, including newly added files that ordinary `git diff --stat` omits. No commit or publication was performed.
