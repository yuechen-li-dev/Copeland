"""Summarize measured native M25 outputs and make evidence contact sheets.

Run with Pillow installed, after the four documented native proof commands.
This script never edits runtime art; its PNG outputs are evidence figures only.
"""

import hashlib
import json
import re
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "artifacts/tinyfarm-high-fidelity-presentation-m25"
ASSETS = ROOT / "src/TinyFarm/TinyFarm.Native/Assets"
POLICY = (ASSETS.parent / "TinyFarmPainterlyPolicy.cs").read_text()


def load(name):
    return json.loads((OUTPUT / name).read_text(encoding="utf-8-sig"))


def write(name, value):
    (OUTPUT / name).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def constant(name):
    return float(re.search(rf"{name} = ([0-9.]+)", POLICY).group(1))


def image_facts(path):
    with Image.open(path) as image:
        alpha = image.getchannel("A") if image.mode == "RGBA" else None
        return {
            "path": path.relative_to(ROOT).as_posix(),
            "width": image.width,
            "height": image.height,
            "fileBytes": path.stat().st_size,
            "fileSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "mode": image.mode,
            "alphaRange": list(alpha.getextrema()) if alpha else None,
        }


def footprint(source, height):
    width = height * source["width"] / source["height"]
    return {
        "width": round(width, 3),
        "height": round(height, 3),
        "sourcePixelsPerDisplayedPixel": round(source["width"] * source["height"] / (width * height), 3),
        "sourcePixelsPerDisplayedPixelAxis": round(source["height"] / height, 3),
    }


def sprite_audit(name, path, base_height, multiplier, semantic, old_path=None, old_height=None):
    source = image_facts(path)
    previous = image_facts(old_path) if old_path else source
    return {
        "asset": name,
        "source": source,
        "previousSource": previous,
        "semantic": semantic,
        "presentationHeightMetresIncludingHeightAndPadding": base_height * multiplier / 48,
        "visualMultiplier": multiplier,
        "before720p": footprint(previous, old_height or base_height),
        "after": {
            str(height): footprint(source, base_height * multiplier * (height / 13) / 48)
            for height in (720, 1080, 1440)
        },
        "samplingBefore": "Nearest",
        "samplingAfter": "Linear",
        "detailWasteBefore": "Strong minification; tree also clipped by original top framing.",
        "metricCaveat": "Full source rectangle includes transparent padding; this is utilization evidence, not a quality score.",
    }


def contact_sheet(prefix, title):
    sheet = Image.new("RGB", (1920, 1160), "#172923")
    draw = ImageDraw.Draw(sheet)
    for index, factor in enumerate((1.0, 1.5, 2.0, 2.5)):
        with Image.open(OUTPUT / f"{prefix}-scale-{factor:.1f}.png") as image:
            image = image.convert("RGB").resize((960, 540), Image.Resampling.LANCZOS)
            x = index % 2 * 960
            y = index // 2 * 580
            sheet.paste(image, (x, y + 40))
            draw.text((x + 18, y + 10), f"{title}: {factor:.1f}x presentation scale / unchanged collision", fill="white", font=FONT)
    sheet.save(OUTPUT / f"{prefix}-scale-comparison.png")


FONT = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 20)
OUTPUT.mkdir(parents=True, exist_ok=True)
tree = sprite_audit(
    "tree", ASSETS / "M24/tree.png", constant("TreeHeightAt48PixelsPerMetre"), constant("TreeVisualScale"),
    {"trunkRadiusMetres": 0.34, "canopy": "Independent existing semantic footprint; no change", "instances": [1.0, 1.08, 1.02, 0.94]},
)
house = sprite_audit(
    "farmhouse", ASSETS / "M25/farmhouse-three-quarter.png", constant("FarmhouseHeightAt48PixelsPerMetre"), constant("FarmhouseVisualScale"),
    {"wallsMetres": [3.6, 2.3], "roofOcclusionMetres": [4.6, 3.3], "heightMetres": 3.4, "changed": False},
    ASSETS / "M24/farmhouse.png", 238,
)
meadow_source = image_facts(ASSETS / "M24/meadow-slab.png")
meadow = {
    "asset": "meadow slab", "source": meadow_source, "semanticWorldMetres": [16, 10],
    "before720p": {"width": 768, "height": 480, "sourcePixelsPerDisplayedPixel": 1254 * 1254 / (768 * 480)},
    "after": {str(h): {"width": h * 16 / 9, "height": h, "sourcePixelsPerDisplayedPixel": 1254 * 1254 / (h * h * 16 / 9)} for h in (720, 1080, 1440)},
    "samplingBefore": "Nearest", "samplingAfter": "Linear",
    "note": "One decorative full-frame mapping; semantic ground stays 16x10 m. At 1080p/1440p the slab is magnified along X, so it is now the source-detail limit.",
}
atlas = image_facts(ASSETS / "M11/tinyfarm-sprite-atlas-source.png")
pixel_assets = []
for name, size in (("character", 70), ("well", 76), ("foreground lantern", 58), ("fence", 54)):
    source = {**atlas, "width": 312, "height": 312, "note": "One cell of shared 1248x1248 atlas; file bytes are shared, not additive."}
    pixel_assets.append({
        "asset": name, "source": source, "semantic": "Existing M11 gameplay position/footprint unchanged",
        "before720p": footprint(source, size),
        "after": {str(h): footprint(source, size * h / 13 / 48) for h in (720, 1080, 1440)},
        "samplingBefore": "Nearest", "samplingAfter": "Nearest (intentional pixel atlas)",
    })
analytic = {
    "asset": "bridge and bank", "source": None, "sourceFileBytes": 0, "sourceDimensions": "Analytic geometry, no PNG",
    "bridgeWorldMetres": [2.4, 1.5], "bridgeBeforePixels": [115.2, 72],
    "bridgeAfter1080pPixels": [2.4 * 1080 / 13, 1.5 * 1080 / 13],
    "sampling": "Analytic reconstruction; bank wet coverage comes from linearly sampled field metadata",
    "detailRatio": None,
}
audits = [tree, house, meadow, analytic, *pixel_assets]
write("asset-scale-audit.json", {"assets": audits, "treeSweep": [1, 1.5, 2, 2.5], "farmhouseSweep": [1, 1.5, 2, 2.5],
    "selection": "Tree 1.1x; house 1.05x with a projection-correct source and 210px-at-48px/m base. Larger sweeps clip northern canopies/roof and overwhelm the path; collision is never resized."})
write("source-detail-utilization.json", {"metric": "source rectangle pixels / displayed rectangle pixels", "assets": audits})

native = load("texture-1080p-native.json")
sprite_bytes = sum(item["rgbaBytes"] for item in native["painterly"]) + native["pixelAtlas"]["rgbaBytes"]
field_bytes = native["field"]["width"] * native["field"]["height"] * 4
write("texture-memory.json", {
    "nativeResources": native, "residentSpriteTexelBytes": sprite_bytes, "residentFieldTexelBytes": field_bytes,
    "spriteMiB": sprite_bytes / 1024**2, "spriteUploadsAtAttach": native["SpriteTextureUploads"],
    "warmSpriteUploads": {str(h): load(f"performance-{h}p.json")["spriteUploadsDuringMeasurement"] for h in (720, 1080, 1440)},
    "renderTargetTexelBytes": {str(h): int(h * h * 16 / 9 * 4) for h in (720, 1080, 1440)},
    "scope": "Exact RGBA payload/texel accounting for resident game art and field, not driver allocation telemetry. Font atlases, Vulkan allocation alignment, descriptors, swapchain images and buffers are excluded from the sprite total.",
    "runtimeVariants": "None added. Reuse source art once per resource; no uploads for HUD toggle or resize. M24 alternate house and rejected M25 studies are not resident.",
})
write("sampler-policy.json", {
    "nativeResources": native, "policyOwner": "TinyFarmPainterlyPolicy.cs + SpriteAtlasResource.Sampling",
    "painterlyClasses": {"PainterlySlab": "Linear", "PainterlyObject": "Linear"},
    "otherClasses": {"PixelAtlas": "Nearest", "MSDF": "Existing derivative-aware reconstruction", "SemanticField": "Linear UNORM"},
    "ordering": "Contiguous sampler runs over one existing feet-Y ordered projection; no grouping that reorders overlapping art.",
    "freshTest": "Changed only TreeSampling to Nearest, built and captured the real native path, then restored Linear. sampler-nearest-tree-native.json records the realized resource policy; capture predates the final house-art revision.",
    "mipmaps": {"implemented": False, "reason": "Minification remains measurable (tree about 3.8 source texels per displayed texel per axis at 1080p). Linear native captures are readable; no shimmering gate was established. Alpha-aware deterministic mip generation is deferred, not claimed unnecessary."},
})
write("bank-mask-proof.json", {
    "qualified": True, "source": "Existing ReactiveFluid2D liquid mask in blue channel",
    "implementation": "Copy all interior field RGBA bytes unchanged into a one-texel dry border, expand visual field bounds by that texel, sample linearly. Gameplay wet/dry data is never changed.",
    "validation": "BankPaddingPreservesInteriorAndMakesOnlyTheBorderDry test; HUD/resize hashes and byte-identical save test",
    "blend": "TinyFarm PainterlyWater.v.ts outputs straight RGB and wet-derived alpha; removed the old second RGB-by-wet multiplication.",
    "order": ["meadow", "soil feather and paths/patches", "semantic wet alpha with live water", "bridge", "feet-Y ordered objects and actors", "occluded-player silhouette", "effects"],
    "remainingSeam": "The soft edge still outlines a rectangular low-resolution field. Alpha transport is fixed; painterly shoreline composition is not complete.",
})
write("alpha-edge-proof.json", {
    "sources": [image_facts(ASSETS / "M24/tree.png"), image_facts(ASSETS / "M25/farmhouse-three-quarter.png")],
    "nativeEvidence": ["alpha-dark-background.png", "world-only-1080p-after.png", "world-only-1440p-after.png", "riverbank-after.png"],
    "inspection": "Tree foliage and selected farmhouse were inspected on dark and meadow backgrounds, with the northern tree adjacent to the bank. No opaque checkerboard, white matte, or broad bright fringe survives in the selected native asset. Fine foliage is softer under linear minification. The water alpha ramp is still visually too uniform/rectangular.",
    "measurementLimits": "Visual native edge inspection plus exact RGBA alpha ranges; not a pixel-perfect image matte certification.",
})

for name, title in (("tree", "Painterly tree"), ("farmhouse", "Approved three-quarter house")):
    contact_sheet(name, title)
with Image.open(OUTPUT / "world-only-1080p-after.png") as image:
    image.crop((1030, 180, 1670, 1080)).save(OUTPUT / "riverbank-after.png")

occupancy = {}
for label, width, height, scale, left, top in (("before", 1280, 720, 48, 22, 24), ("after1080p", 1920, 1080, 1080 / 13, (1920 - 16 * 1080 / 13) / 2, 3 * 1080 / 13)):
    total = width * height
    ground = 16 * scale * 10 * scale / total
    mask = Image.new("1", (width, height))
    draw = ImageDraw.Draw(mask)
    draw.rectangle((left, top, left + 16 * scale - 1, top + 10 * scale - 1), fill=1)
    art = [(ASSETS / "M24/tree.png", 178, 1 if label == "before" else constant("TreeVisualScale"), x, y, local)
           for x, y, local in ((1.3, 5.25, 1), (7, 3.1, 1.08), (8.35, 1.25, 1.02), (6.4, 8.75, .94))]
    art.append((ASSETS / ("M24/farmhouse.png" if label == "before" else "M25/farmhouse-three-quarter.png"),
                238 if label == "before" else constant("FarmhouseHeightAt48PixelsPerMetre"),
                1 if label == "before" else constant("FarmhouseVisualScale"), 3.2, 3.3, 1))
    for path, base, multiplier, x, y, local in art:
        with Image.open(path) as source:
            asset_height = round(base * multiplier * local * scale / 48)
            asset_width = round(asset_height * source.width / source.height)
            alpha = source.getchannel("A").resize((asset_width, asset_height), Image.Resampling.LANCZOS).point(lambda value: 255 if value > 20 else 0)
            mask.paste(1, (round(left + x * scale - asset_width / 2), round(top + y * scale - asset_height)), alpha)
    active = (total - mask.histogram()[0]) / total
    occupancy[label] = {
        "semanticGroundPercent": round(ground * 100, 2),
        "groundPlusComposedArtCoveragePercent": round(active * 100, 2),
        "worldBackdropCoveragePercent": 40 if label == "before" else 100,
        "deadFlatBorderPercent": round((1 - active) * 100, 2) if label == "before" else 0,
        "decorativeExtensionBeyondActiveWorldPercent": 0 if label == "before" else round((1 - active) * 100, 2),
        "normalHudPanelRectanglePercent": round((1236 * 74 + 312 * 457 + 1236 * 120) / (1280 * 720) * 100, 2),
        "worldOnlyNormalHudPercent": 0,
    }
write("world-occupancy.json", {"measurements": occupancy,
    "method": "Semantic ground rectangle union alpha>20 of four trees and house; transparent padding excluded. HUD is an overlapping panel-rectangle estimate, not disjoint area. Decorative meadow extension is reported separately from active game-world coverage, not counted as extra traversable ground."})

defects = [
    {"id": "M25-C1", "class": "C", "priority": "primary", "defect": "Riverside still reads as a softly feathered rectangular cyan field against the meadow; shoreline material lacks the painterly structure of the assets.", "evidence": "riverbank-after.png"},
    {"id": "M25-C2", "class": "C", "priority": "next-pass", "defect": "Path, crop patch and bridge use broad flat analytic shapes with a strong style mismatch to the foliage and house.", "evidence": "world-only-1080p-after.png"},
    {"id": "M25-A1", "class": "A", "priority": "medium", "defect": "Canopy headroom prevents clipping, but decorative meadow extension and a large quiet water area are weakly composed.", "evidence": "world-occupancy.json"},
    {"id": "M25-B1", "class": "B", "priority": "medium", "defect": "House now follows approved north-up three-quarter geometry; pixel well/fence/character and taller M24 tree silhouettes still need a catalog-wide consistency pass.", "evidence": "world-only-1440p-after.png"},
    {"id": "M25-D1", "class": "D", "priority": "medium", "defect": "Player is locatable through foreground foliage via a translucent silhouette, but pixel styling and occlusion treatment remain visibly provisional.", "evidence": "world-only-1080p-after.png"},
    {"id": "M25-E1", "class": "E", "priority": "medium", "defect": "Local prompt is much smaller but still wordy for tool/plot interactions; some target art is less readable than its prompt.", "evidence": "world-only-1080p-after.png"},
    {"id": "M25-F1", "class": "F", "priority": "deferred", "defect": "Full HUD remains large when enabled. It is now optional and overlays the world, but a future UI pass can reduce chrome.", "evidence": "ui-on-1080p-after.png"},
    {"id": "M25-G1", "class": "G", "priority": "deferred", "defect": "No content/gameplay expansion attempted; static captures do not establish long-term play appeal.", "evidence": "world-only-1080p-after.png"},
]
write("visual-defect-ledger.json", {"outcome": "B", "defects": defects,
    "nextPrimaryPressure": "Painterly ground-material composition, beginning with the Riverside shoreline and its connection to the existing path/bridge. Preserve semantic wet/dry authority."})

provenance = {
    "selectedAsset": image_facts(ASSETS / "M25/farmhouse-three-quarter.png"),
    "geometryGuide": image_facts(ASSETS / "M25/farmhouse-three-quarter-guide.png"),
    "vectorGuide": "src/TinyFarm/TinyFarm.Native/Assets/M25/farmhouse-three-quarter-guide.svg",
    "materialReference": image_facts(ASSETS / "M24/farmhouse.png"),
    "approvedConvention": "Nominal 45 degree elevation, north-up, zero yaw, parallel projection; prominent roof plus shallow south facade",
    "source": "Built-in image_gen, guide-constrained paint-over followed by generated RGBA extraction",
    "finalGenerationFile": "exec-c90619a6-e123-4ccd-82c3-e4334e5d13f7.png",
    "rejectedStudies": ["farmhouse-topdown-guide.svg", "farmhouse-topdown.png", "farmhouse-topdown-paint-study.png", "farmhouse-three-quarter-paint-study.png"],
    "reasonForReplacement": "User identified original diagonal/isometric perspective mismatch, then explicitly approved the fixed three-quarter convention. Roof-plan study was rejected after clarification.",
    "semanticChanges": False,
}
write("generated-house-provenance.json", provenance)

baseline = load("presentation-baseline.json")
baseline.update({"originalLogicalExtent": [1280, 720], "originalFramebufferExtent": [1280, 720],
                 "originalWorldViewport": [22, 24, 904, 648], "originalPixelsPerMetre": 48,
                 "originalResize": "Fixed window; empty renderer Resize", "originalSampling": "One nearest-sampled sprite scope",
                 "captureCaveat": "Native legacy camera/resources/composition; current control legend and reduced prompt are retained by the reproduction mode."})
write("presentation-baseline.json", baseline)

print(json.dumps({"spriteMiB": sprite_bytes / 1024**2, "tree1080p": tree["after"]["1080"], "house1080p": house["after"]["1080"], "occupancy": occupancy}, indent=2))

performance_lines = [
    "# Native M25 performance summary",
    "",
    "RTX 3070; Release; VSync off; 30 warm-up + 180 measured frames per row. CPU wall-clock host frame with synchronous Vulkan submissions, not GPU timestamp measurements.",
    "",
    "| Resolution | HUD | p50 ms | p95 ms | p99 ms | Worst ms | Mean draws | Field projection total ms | Sprite uploads |",
    "|---|---|---:|---:|---:|---:|---:|---:|---:|",
]
for height in (720, 1080, 1440):
    for hud in (False, True):
        suffix = "-hud" if hud else ""
        sample = load(f"performance-{height}p{suffix}.json")
        times = [sample[key] for key in (
            "p50Milliseconds", "p95Milliseconds", "p99Milliseconds", "worstMilliseconds"
        )]
        performance_lines.append(
            f"| {height}p | {'On' if hud else 'Off'} | "
            + " | ".join(f"{value:.3f}" for value in times)
            + f" | {sample['averageDrawCount']:.2f} | {sample['FieldProjectionMilliseconds']:.4f}"
            + f" | {sample['spriteUploadsDuringMeasurement']} |"
        )
(OUTPUT / "performance-summary.md").write_text("\n".join(performance_lines) + "\n", encoding="utf-8")

required = [
    "presentation-baseline.json", "asset-scale-audit.json", "resolution-policy.json",
    "hud-toggle-proof.json", "world-only-proof.json", "sampler-policy.json",
    "bank-mask-proof.json", "alpha-edge-proof.json", "source-detail-utilization.json",
    "texture-memory.json", "performance-720p.json", "performance-1080p.json",
    "performance-1440p.json", "world-occupancy.json", "ui-on-before.png",
    "world-only-before.png", "world-only-1080p-after.png", "world-only-1440p-after.png",
    "ui-on-1080p-after.png", "tree-scale-comparison.png", "farmhouse-scale-comparison.png",
    "riverbank-after.png", "oblivion-presentation-inspector.png", "visual-defect-ledger.json",
]
for name in required:
    if not (OUTPUT / name).is_file():
        raise FileNotFoundError(f"Missing required M25 evidence: {name}")
for height, width in ((720, 1280), (1080, 1920), (1440, 2560)):
    for prefix in ("world-only", "ui-on"):
        with Image.open(OUTPUT / f"{prefix}-{height}p-after.png") as capture:
            if capture.size != (width, height):
                raise ValueError(f"Incorrect physical capture size: {capture.size}")

inventory = []
for path in sorted(OUTPUT.iterdir()):
    if not path.is_file() or path.name == "manifest.json":
        continue
    if path.suffix == ".json":
        load(path.name)
    entry = {
        "file": path.name,
        "bytes": path.stat().st_size,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "required": path.name in required,
    }
    if path.suffix == ".png":
        with Image.open(path) as capture:
            entry["dimensions"] = list(capture.size)
            capture.verify()
    inventory.append(entry)

write("manifest.json", {
    "milestone": "TINYFARM-HIGH-FIDELITY-WORLD-PRESENTATION-M25",
    "kind": "world-first-painterly-presentation-isolation",
    "outcome": "B",
    "remainingSeam": "Riverside's corrected soft alpha still outlines a rectangular water field; painterly shoreline material composition remains incomplete.",
    "hudToggleQualified": True,
    "worldOnlyModeQualified": True,
    "defaultResolutionRaised": True,
    "aspectRatioQualified": True,
    "highDpiAudited": True,
    "highDpiPhysicalMultiScaleTested": False,
    "painterlyAssetScaleAudited": True,
    "treeScaleImproved": True,
    "farmhouseScaleImproved": True,
    "assetClassSamplingQualified": True,
    "painterlyLinearFilteringQualified": True,
    "semanticBankMaskQualified": True,
    "riversideSeamImproved": True,
    "shorelineArtComplete": False,
    "worldViewportIndependentOfHud": True,
    "semanticReplayUnchangedByPresentation": True,
    "1080pQualified": True,
    "1440pQualified": True,
    "visualDefectLedgerProduced": True,
    "uiRedesignPerformed": False,
    "gameplaySystemsAdded": False,
    "baselineCaveat": baseline["captureCaveat"],
    "nextPrimaryPressure": "Painterly ground-material composition, starting with Riverside shoreline and path/bridge transitions.",
    "requiredFilesVerified": len(required),
    "files": inventory,
})
