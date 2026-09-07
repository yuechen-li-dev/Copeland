"""Rebuild source fingerprints and the reviewed M18 concept matrix. No benchmark code is executed."""
import argparse
import hashlib
import json
import pathlib
import re
import tempfile
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/aurelian-rts-pearl-mining-m18"
MODELS = ["Fable", "Opus", "Astra", "Sol", "Qwen", "Kimi"]
URLS = {
    "Fable": "https://gist.github.com/senko/2a51dfb52f5bb4bbca65ad628a0cb9d2",
    "Opus": "https://senko.net/vibecode-bench/2026/rts-opus-5.html",
    "Astra": "https://senko.net/vibecode-bench/2026/rts-gpt-6-astra.html",
    "Sol": "https://senko.net/vibecode-bench/2026/rts-gpt-5.6-sol.html",
    "Qwen": "https://senko.net/vibecode-bench/2026/rts-qwen-3.8-max.html",
    "Kimi": "https://senko.net/vibecode-bench/2026/rts-kimi-k3.html",
}

# These are reviewed observations, not inferred from token counts or a feature detector.
# Columns follow MODELS. A=existing owner, B=bounded reusable gap, C=sample policy, D=rejected implementation.
ROWS = [
    ("Simulation loop", "frame dt cap; update", "frame dt cap .05; tick", "frame dt cap .06; update", "frame dt cap .05; loop", "dt update and speed modes", "dt update", "A", "Aurelian.Simulation", "Existing integer CadenceScheduler; sample uses 10 Hz with 2 Hz reveal."),
    ("World/map model", "typed tile arrays / noise", "terrain/passable/blocked arrays", "48x48 terrain / nodes", "continuous top-down / fog cells", "tile arrays / blockers", "tile arrays / blockers", "C", "sample-local", "24x24 authored proof; application owns terrain meaning."),
    ("Unit/building model", "definitions plus mutable records", "definitions plus mutable records", "unit/building definitions", "TYPES plus records", "definitions plus records", "COSTS plus records", "C", "sample-local", "Typed IDs, unit/resource kinds and authoritative records."),
    ("Economy", "gold/wood carry/dropoff", "gold/wood return/retarget", "gold/wood carry; sawmill bonus", "aether cargo/dropoff", "gold/wood cargo phases", "gold/wood cargo phases", "C", "sample-local + Dominatus", "Crystal plus fresh wood; cargo kind preserved; no economy framework."),
    ("Point selection", "nearest unit then building", "unit/building/resource priority", "screen-space silhouette hit", "entity radius hit", "pickAt handlers", "pickAt / selectSingle", "A", "Aurelian.Spatial2D", "Existing PointQuery/Overlap; sample uses overlap plus eligibility."),
    ("Box selection", "owned units / additive", "units; building fallback", "projected screen rectangle", "selected entity set", "world-space box", "boxSelect", "A", "Aurelian.Spatial2D", "World AABB broad query then isometric screen filter."),
    ("Selection identity", "object references", "objects + selected flags", "kind/id records", "objects + selected flags", "selected object array", "unit selection + building slot", "B", "Aurelian kit/profile", "EntitySelection<TId> owns sorted detached snapshots, filtering and groups."),
    ("Shift/double-click", "shift toggle; viewport type set", "shift toggle; viewport type set", "shift toggle; all type", "shift union; no double-click handler found", "shift; same type", "shift; same type", "C", "sample-local", "Input controller chooses toggle/add and same-kind scope."),
    ("Control groups", "Ctrl digits; double recall centers", "Ctrl 1..9; recall", "not found", "not found", "not found", "not found", "B", "Aurelian kit/profile", "Stable group storage/pruning; sample binds groups 1..3."),
    ("Context right-click", "move/gather/build/attack/rally", "move/harvest/build/dropoff/attack/rally", "move/gather/build/rally", "move/gather", "move/gather/build/attack/rally", "move/gather/build/attack/rally", "C", "sample-local", "Typed orders, all-target validation; application retains command policy."),
    ("Move / stop", "both", "both", "both", "both", "both + hold", "move; state clear", "A", "Dominatus + Aurelian.Spatial2D", "Persistent HFSM and resolver sweep validation; blocked routes fail explicitly."),
    ("Attack / attack-move", "both; resume destination", "both; acquisition", "peaceful; no combat loop", "no enemy combat loop found", "both; hold behavior", "attack slimes; no attack-move found", "C", "sample-local + Dominatus", "Attack landed, ranger reuse proven. Attack-move deferred."),
    ("Production queues", "reserve cost/supply; cancellation", "reserve supply; rally/autoassign", "5 items; reservedPop", "queue; supply", "queue/cancel/spawn", "queue/cancel/spawn", "C", "sample-local + Aurelian.Simulation", "Four-item bounded queue, integer ticks, upfront charge; no second queue consumer yet."),
    ("Construction placement", "clear tile; push occupants out", "explored/passable; permits unit overlap", "full footprint; worker reach rollback", "circle/box checks", "footprint/cost", "canPlaceAt / tryPlace", "A", "Aurelian.Spatial2D", "Spatial preview; commit revalidates footprint, resources, idle worker and cost."),
    ("Supply/capacity", "live + queued reservation", "reservation at enqueue", "reservedPop includes queue", "supply function", "recomputeFood", "supplyUsed includes queue", "C", "sample-local", "Cap 12, queued people count against cap. No tech tree."),
    ("Fog/exploration", "0/1/2 states", "visible/explored/explorable", "visible/explored + soft strength", "visible/discovered", "visible/explored arrays", "fogE/fogV", "B", "Aurelian kit/profile", "VisibilityGrid integer disks; detached exploration; no occlusion or multifaction adapter."),
    ("Minimap", "semantic terrain/entities/fog", "baked terrain + live overlay", "diamond semantic terrain/fog/viewport", "semantic fog and markers", "terrain/fog/markers", "terrain/fog/live dots", "C", "sample-local", "Semantic grid/entities/viewport projection; no screenshot reuse; click/drag camera-only."),
    ("Camera", "pan/edge; no zoom found", "arrow/edge/middle/wheel", "WASD/drag/wheel; anchored zoom", "WASD/wheel; anchored zoom", "arrows/edges/zoom", "arrows/edges/wheel", "A", "Aurelian.GameWorld2D + InputMan", "Reuse Camera2D; logical pan/zoom/center and minimap jump."),
    ("Isometric projection", "top-down", "top-down", "diamond project/unproject", "top-down", "top-down", "top-down", "B", "Aurelian.GameWorld2D", "Small validated invertible renderer-neutral transform; no tile engine."),
    ("Idle worker UX", "comma cycles idle", "autoAssignWorker", "findIdle cycles and centers", "collector count", "worker command panel", "worker command panel", "C", "sample-local", "I selects and centers first idle worker."),
    ("HUD hierarchy", "resource/header + command panel", "resource/header + dynamic cards", "resource strip/objective/selection/actions/minimap", "resource strip/directive/command matrix/minimap", "topbar + command cards", "topbar + selection panel", "B", "Aurelian.Machina profile", "StrategyHudSnapshot/Style/Copy -> named Machina nodes; bounded logical viewport."),
    ("Objectives", "exploration / win state", "explorable total / loss", "gather/build/full exploration", "90% survey", "100% exploration", "95% exploration", "C", "sample-local", "Gather 20 crystal and complete lodge; no general quest engine."),
    ("Procedural art", "canvas circles/polygons", "cached sprite shapes and masonry", "isoBox/roof/facets/portrait reuse", "rounded hulls/crystal fans/glow", "canvas motifs and icons", "canvas stylized buildings/units", "B", "Copeland asset tooling templates", "Ordinary Profile composition; original first-party assets; canonical contour realization."),
    ("Terrain presentation", "cached tiles/coastline/noise", "baked terrain/coastline", "diamond patches/flowers/river/bridge", "hash patches/routes/grass", "tiles/noise/trees", "prerenderTerrain", "C", "sample-local", "Deterministic color variation and decorative foliage; no terrain editor."),
    ("UI layout/typography", "CSS/DOM", "CSS/DOM", "serif + sans; grid; responsive breakpoints", "CSS product hierarchy", "CSS/DOM", "CSS/DOM", "A", "Machina.UI", "Existing named nodes/style + direct outlines; forward text measurement through pipeline."),
    ("Persistence", "not found", "not found", "localStorage version1; 15s autosave", "not found", "not found", "not found", "A", "Deliverance", "Existing semantic save owner retained; sample only qualifies in-memory replay, not save files."),
    ("Input", "DOM callbacks mutate G", "DOM callbacks mutate G", "DOM callbacks mutate arrays", "DOM callbacks mutate state", "DOM callbacks mutate S", "DOM callbacks mutate globals", "A", "InputMan", "Physical adapter -> logical frame -> typed strategy intent; focus and HUD capture checked."),
    ("Audio/effects", "local visual effects", "projectiles/floaters", "command/float effects", "glow/activity hints", "particles/projectiles", "bursts/floaters", "A", "Aurelian.Audio / Effects2D", "Existing owners retained; no new audio/effect system required for this slice."),
    ("Architecture quality", "monolith; named subsystems", "monolith; stronger caches/indexes", "monolith; strong visual decomposition", "monolith; differentiated product", "monolith; conventional RTS", "monolith; conventional RTS", "D", "rejected", "Extract semantic laws, not global state, string dispatch or frame-owned behavior."),
    ("Map repair", "seeded noise + repeated reach/carve", "seeded map + floodReach", "fixed seed + designed corridors", "hash/decorative layout", "random walks + unseeded random", "Math.random generation", "C", "sample-local", "Guarantee usable start + connectivity repair is a candidate recipe, not a general generator."),
]

SYMBOLS = {
    "Fable": ["generateMap", "ensureReachable", "updateGather", "trainUnit", "placeBuilding", "updateFog", "boxSelect", "selectSameType", "commandMove", "update"],
    "Opus": ["generateMap", "floodReach", "canPlaceAt", "updateFog", "drawUnitShape", "drawBuildingShape", "routeWorker", "updateUnit", "updateBuilding", "clickSelect", "rightClick", "tick"],
    "Astra": ["project", "unproject", "generate", "updateVision", "placeBuilding", "train", "deposit", "update", "isoBox", "drawRoof", "drawBuilding", "drawUnit", "drawTerrain", "makePortrait", "updateUI", "saveGame"],
    "Sol": ["train", "validBuild", "placeBuild", "orderGather", "moveToward", "updateUnit", "updateFog", "drawTerrain", "drawNode", "drawBuilding", "drawUnit", "drawMinimap", "updateUI", "loop"],
    "Qwen": ["genWorld", "updateGather", "updateReturn", "enqueueTrain", "cancelConstruction", "updateCreep", "cmdHold", "setSelection"],
    "Kimi": ["generateMap", "orderGather", "orderBuild", "cmdTrain", "canPlaceAt", "updateUnit", "update", "drawBuilding", "drawUnit", "updateMinimap", "renderPanel"],
}


def write(name, value):
    (OUT / name).write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def fetch(directory):
    for model, url in URLS.items():
        if model == "Fable":
            with urllib.request.urlopen("https://api.github.com/gists/2a51dfb52f5bb4bbca65ad628a0cb9d2") as response:
                gist = json.load(response)
            url = gist["files"]["index.html"]["raw_url"]
        with urllib.request.urlopen(url) as response:
            (directory / (model.lower() + ".html")).write_bytes(response.read())


def build(directory):
    OUT.mkdir(parents=True, exist_ok=True)
    sources = []
    for model in MODELS:
        payload = (directory / (model.lower() + ".html")).read_bytes()
        source = payload.decode("utf-8")
        refs = []
        for symbol in SYMBOLS[model]:
            match = re.search(r"function\s+" + re.escape(symbol) + r"\s*\(", source)
            if match:
                refs.append({"symbol": symbol, "line": source[:match.start()].count("\n") + 1})
        sources.append({"model": model, "url": URLS[model], "sha256": hashlib.sha256(payload).hexdigest(),
                        "bytes": len(payload), "lines": len(source.splitlines()), "reviewedSymbols": refs,
                        "method": "subsystem-directed source inspection; Astra additionally viewed in browser", "runtimeQualified": False})
    concepts = []
    for row in ROWS:
        concept, *values = row
        cells, category, owner, decision = values[:6], values[6], values[7], values[8]
        concepts.append({"concept": concept, "observations": dict(zip(MODELS, cells)), "category": category,
                         "owner": owner, "decision": decision})
    write("corpus-audit.json", {"sources": sources, "subsystems": concepts,
          "limits": "No benchmark score reproduction; source findings distinguish absent handlers from unsupported behavior claims."})
    write("concept-classification.json", {"classes": {"A": "already solved by existing JTF owner", "B": "bounded reusable gap integrated", "C": "app/sample policy", "D": "implementation pattern rejected"}, "concepts": concepts})
    write("ownership-map.json", [{"concept": c["concept"], "owner": c["owner"], "decision": c["decision"]} for c in concepts])
    write("cross-model-convergence.json", {"allSix": ["point/box selection", "context command", "carry/dropoff economy", "production", "visible vs explored", "semantic minimap", "procedural art"],
          "partial": {"controlGroups": ["Fable", "Opus"], "combat": ["Fable", "Opus", "Qwen", "Kimi"], "isometric": ["Astra"], "save": ["Astra"]},
          "interpretation": "Convergence is evidence of recurring problems, not proof of design quality or independent invention; shared benchmark prompts confound model-family claims."})
    write("rejected-benchmark-hacks.json", [
        {"pattern": "Global state/one-file dispatch", "source": "all six", "replacement": "owner modules plus typed intents and authoritative session"},
        {"pattern": "Occupant teleport on build", "source": "Fable placeBuilding", "replacement": "reject overlapping footprint before charge"},
        {"pattern": "Permit occupied building footprint", "source": "Opus canPlaceAt", "replacement": "validate transient units as well as buildings"},
        {"pattern": "Frame-delta work remainder loss", "source": "Astra work=0; Opus trainT=0; Fable shift queue", "replacement": "integer semantic ticks and cadence partition proof"},
        {"pattern": "Unseeded random state", "source": "Qwen rand; Kimi rand", "replacement": "authored deterministic map; deterministic appearance hash"},
        {"pattern": "DOM/callback-owned semantic mutation", "source": "all six input/UI handlers", "replacement": "InputMan logical frame -> validated intent"},
        {"pattern": "Render/animation consumes game RNG", "source": "mixed presentation/model generators", "replacement": "appearance independent from semantic state"},
    ])
    write("wilderland-art-audit.json", {"source": URLS["Astra"], "mechanism": "Canvas2D world paths plus inline SVG UI symbols; generated canvas portraits reused from world draw functions; CSS chrome",
          "primitives": ["polygon", "ellipse", "line", "isoBox", "gabled roof", "translate/scale/rotate", "alpha", "faceted canopy", "repeated roof seams"],
          "projection": "screen diamond: ((x-y)*TW/2,(x+y)*TH/2), then camera; inverse used for input",
          "palette": "muted green field and panel, cream masonry, terracotta/teal roofs, restrained gold commands",
          "hierarchy": "resource header; upper-left objective card; lower selection/commands/build/minimap dock; low-contrast keyboard footer",
          "fog": "semantic explored/visible plus presentation visionStrength; softened composited mask",
          "reexpression": "Copeland named Profile layers + typed palette templates; Machina semantic HUD; Aurelian camera/isometric transform",
          "notReproduced": ["soft fog mask", "full responsive breakpoints", "roof seam density", "animated unit gait", "SVG icon set"]})
    write("procedural-art-capabilities.json", [
        {"pattern": "closed polygon/ellipse/tube", "kind": "existing reusable primitive", "owner": "Copeland.Profile"},
        {"pattern": "named painter layers", "kind": "existing reusable primitive", "owner": "Copeland.Profile"},
        {"pattern": "shaded Block / Roof / Figure / Lodge / Shadow", "kind": "new reusable template", "owner": "Copeland sample authoring library StrategyArt.ts"},
        {"pattern": "worker/heavy/ranger/HQ/lodge/tree/crystal/marker/watchtower", "kind": "new reusable assets", "owner": "first-party sample proof pack"},
        {"pattern": "Mossward palette and decorative terrain", "kind": "app-local style", "owner": "sample-local"},
        {"pattern": "gradient fill / animated detail / object-card vector integration", "kind": "deferred", "owner": "Copeland/Machina tooling; do not invent SVG parser"},
    ])
    write("strategy-system-map.json", {"input": "Avalonia callbacks -> AurelianInputAdapter -> InputManEngine -> StrategyInput -> StrategyIntent",
          "orders": "StrategySession validation -> accepted UnitOrder -> Dominatus HFSM -> staged proposals sorted by ID -> session mutation",
          "time": "host delta -> CadenceScheduler -> AdvanceIntent -> 10Hz world and 2Hz reveal",
          "presentation": "session facts -> StrategyHudSnapshot -> Machina nodes -> Aurelian CPU panels + Machina direct-outline text; Profile contours -> cached Skia paths",
          "camera": "world -> IsometricProjection -> Camera2D -> screen; inverse for input",
          "minimap": "visibility grid + semantic unit/resource positions + inverse camera viewport; camera intent only",
          "save": "in-memory semantic tape replay only; no Deliverance envelope claimed"})
    lines = ["# M18 RTS corpus audit", "", "Source review captured by `corpus-audit.json` SHA-256 fingerprints and symbol line references. No benchmark source is linked into the sample. `A/B/C/D` are concept classifications, not milestone outcomes.", "",
             "| Concept | Fable | Opus | Astra | Sol | Qwen | Kimi | Recurring? | JTF status |", "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for row in ROWS:
        concept, *values = row
        recurring = "partial" if concept in {"Control groups", "Attack / attack-move", "Persistence", "Isometric projection"} else "yes / variants"
        lines.append("| " + " | ".join([concept] + values[:6] + [recurring, values[6] + " — " + values[7] + ": " + values[8]]) + " |")
    lines += ["", "## Source provenance", ""]
    for source in sources:
        lines.append(f"- [{source['model']}]({source['url']}): {source['lines']} source lines, {source['bytes']} bytes; `{source['sha256']}`. Key symbols: " + ", ".join(f"`{r['symbol']}` (L{r['line']})" for r in source["reviewedSymbols"]) + ".")
    lines += ["", "Absence statements mean not found in the inspected source handlers. Only Wilderland received browser visual inspection; the six originals were not independently gameplay-tested. Model names follow the supplied corpus labels, not verified training provenance.", ""]
    path = ROOT / "docs/milestones/m18-rts-corpus-audit.md"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=pathlib.Path)
    args = parser.parse_args()
    if args.source_dir:
        build(args.source_dir)
    else:
        with tempfile.TemporaryDirectory(prefix="m18-corpus-") as temporary:
            directory = pathlib.Path(temporary)
            fetch(directory)
            build(directory)
