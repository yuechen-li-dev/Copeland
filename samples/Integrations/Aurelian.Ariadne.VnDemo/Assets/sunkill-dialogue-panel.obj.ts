// Authoritative programmable SUNKILL UI asset. Generate projections with:
// tscl asset build manifest.tsx

record AssetTexture {
    id: string;
    source: string;
    width: int;
    height: int;
}

record AssetEdgeSegment {
    id: string;
    region: string;
    allocation: string;
    length: int;
    weight: int;
    sampling: string;
}

record AssetEdge {
    segments: AssetEdgeSegment[];
}

record AssetPadding {
    left: int;
    top: int;
    right: int;
    bottom: int;
}

record AssetPanel {
    id: string;
    topLeftRegion: string;
    topRightRegion: string;
    bottomRightRegion: string;
    bottomLeftRegion: string;
    top: AssetEdge;
    right: AssetEdge;
    bottom: AssetEdge;
    left: AssetEdge;
    centerPolicy: string;
    centerRegion: string;
    borderScale: number;
    contentPadding: AssetPadding;
}

record AssetObject {
    schemaVersion: int;
    id: string;
    texture: AssetTexture;
    panels: AssetPanel[];
}

record table AssetRegions {
    id: string = [
        "dialogue.corner.top-left",
        "dialogue.corner.top-right",
        "dialogue.corner.bottom-right",
        "dialogue.corner.bottom-left",
        "dialogue.top.cap-left",
        "dialogue.top.clamp",
        "dialogue.top.glow",
        "dialogue.top.center",
        "dialogue.top.cap-right",
        "dialogue.bottom.cap-left",
        "dialogue.bottom.clamp",
        "dialogue.bottom.glow",
        "dialogue.bottom.center",
        "dialogue.bottom.cap-right",
        "dialogue.left.cap-top",
        "dialogue.left.clamp",
        "dialogue.left.glow",
        "dialogue.left.center",
        "dialogue.left.cap-bottom",
        "dialogue.right.cap-top",
        "dialogue.right.clamp",
        "dialogue.right.glow",
        "dialogue.right.center",
        "dialogue.right.cap-bottom",
        "dialogue.center",
    ];
    x: int = [
        26, 920, 920, 26,
        102, 186, 294, 400, 836,
        102, 186, 294, 400, 836,
        26, 26, 26, 26, 26,
        920, 920, 920, 920, 920,
        102,
    ];
    y: int = [
        34, 34, 900, 900,
        34, 34, 34, 34, 34,
        900, 900, 900, 900, 900,
        110, 194, 350, 440, 816,
        110, 194, 350, 440, 816,
        110,
    ];
    width: int = [
        76, 76, 76, 76,
        84, 14, 82, 224, 84,
        84, 14, 82, 224, 84,
        76, 76, 76, 76, 76,
        76, 76, 76, 76, 76,
        818,
    ];
    height: int = [
        76, 76, 76, 76,
        76, 76, 76, 76, 76,
        76, 76, 76, 76, 76,
        84, 14, 82, 224, 84,
        84, 14, 82, 224, 84,
        790,
    ];
}

// Notebook-only semantic scaffolding. The compiler retains these concepts for
// inspection and erases them from SpriteForge/runtime TOML.
record table AssetConcepts {
    path: string = [
        "guide.dialogue.content-safe-area",
        "guide.dialogue.datum.text-baseline",
        "blockout.dialogue.content",
    ];
    kind: string = ["guide", "datum", "blockout"];
    x: int = [102, 102, 102];
    y: int = [110, 650, 110];
    width: int = [818, 818, 818];
    height: int = [790, 0, 790];
    axis: string = ["none", "horizontal", "none"];
    visible: boolean = [true, true, true];
}

function fixed(id: string, region: string, length: int): AssetEdgeSegment {
    return {
        id: id,
        region: region,
        allocation: "fixed",
        length: length,
        weight: 0,
        sampling: "crop",
    };
}

function flex(id: string, region: string, minimum: int, weight: int, sampling: string): AssetEdgeSegment {
    return {
        id: id,
        region: region,
        allocation: "flex",
        length: minimum,
        weight: weight,
        sampling: sampling,
    };
}

function horizontalEdge(
    prefix: string,
    capLeft: string,
    clamp: string,
    glow: string,
    center: string,
    capRight: string,
    glowMinimum: int,
    glowWeight: int,
    centerMinimum: int,
    centerWeight: int,
    glowSampling: string,
    centerSampling: string): AssetEdge {
    return {
        segments: [
            fixed(prefix + ".cap-left", capLeft, 42),
            fixed(prefix + ".clamp-a", clamp, 7),
            flex(prefix + ".glow-left", glow, glowMinimum, glowWeight, glowSampling),
            fixed(prefix + ".clamp-b", clamp, 7),
            flex(prefix + ".center", center, centerMinimum, centerWeight, centerSampling),
            fixed(prefix + ".clamp-c", clamp, 7),
            flex(prefix + ".glow-right", glow, glowMinimum, glowWeight, glowSampling),
            fixed(prefix + ".clamp-d", clamp, 7),
            fixed(prefix + ".cap-right", capRight, 42),
        ],
    };
}

function verticalEdge(
    prefix: string,
    capTop: string,
    clamp: string,
    glow: string,
    center: string,
    capBottom: string,
    glowMinimum: int,
    glowWeight: int,
    centerMinimum: int,
    centerWeight: int,
    glowSampling: string,
    centerSampling: string): AssetEdge {
    return {
        segments: [
            fixed(prefix + ".cap-top", capTop, 42),
            fixed(prefix + ".clamp-a", clamp, 7),
            flex(prefix + ".glow-top", glow, glowMinimum, glowWeight, glowSampling),
            fixed(prefix + ".clamp-b", clamp, 7),
            flex(prefix + ".center", center, centerMinimum, centerWeight, centerSampling),
            fixed(prefix + ".clamp-c", clamp, 7),
            flex(prefix + ".glow-bottom", glow, glowMinimum, glowWeight, glowSampling),
            fixed(prefix + ".clamp-d", clamp, 7),
            fixed(prefix + ".cap-bottom", capBottom, 42),
        ],
    };
}

function buildSunkillPanel(): AssetObject {
    const top: AssetEdge = horizontalEdge(
        "dialogue.top",
        "dialogue.top.cap-left",
        "dialogue.top.clamp",
        "dialogue.top.glow",
        "dialogue.top.center",
        "dialogue.top.cap-right",
        30,
        1,
        44,
        3,
        "tile",
        "stretch");
    const bottom: AssetEdge = horizontalEdge(
        "dialogue.bottom",
        "dialogue.bottom.cap-left",
        "dialogue.bottom.clamp",
        "dialogue.bottom.glow",
        "dialogue.bottom.center",
        "dialogue.bottom.cap-right",
        30,
        1,
        30,
        2,
        "stretch",
        "stretch");
    const left: AssetEdge = verticalEdge(
        "dialogue.left",
        "dialogue.left.cap-top",
        "dialogue.left.clamp",
        "dialogue.left.glow",
        "dialogue.left.center",
        "dialogue.left.cap-bottom",
        10,
        1,
        10,
        2,
        "stretch",
        "stretch");
    const right: AssetEdge = verticalEdge(
        "dialogue.right",
        "dialogue.right.cap-top",
        "dialogue.right.clamp",
        "dialogue.right.glow",
        "dialogue.right.center",
        "dialogue.right.cap-bottom",
        10,
        1,
        10,
        2,
        "stretch",
        "stretch");
    return {
        schemaVersion: 1,
        id: "sunkill.ui",
        texture: {
            id: "sunkill.ui.atlas",
            source: "sunkill-ui-atlas.png",
            width: 1536,
            height: 1024,
        },
        panels: [{
            id: "dialogue",
            topLeftRegion: "dialogue.corner.top-left",
            topRightRegion: "dialogue.corner.top-right",
            bottomRightRegion: "dialogue.corner.bottom-right",
            bottomLeftRegion: "dialogue.corner.bottom-left",
            top: top,
            right: right,
            bottom: bottom,
            left: left,
            centerPolicy: "stretch-region",
            centerRegion: "dialogue.center",
            borderScale: 0.5,
            contentPadding: { left: 34, top: 34, right: 34, bottom: 28 },
        }],
    };
}

const $asset: AssetObject = static buildSunkillPanel();
