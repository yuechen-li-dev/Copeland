(async () => {
    const params = new URLSearchParams(window.location.search);
    const mode = params.get("mode") ?? "reference";
    document.body.dataset.mode = mode;

    if (mode === "compare") {
        await renderCompare(params);
        return;
    }

    if (mode === "metrics") {
        await renderMetrics(params);
        return;
    }

    await renderReference(params);
})();

async function renderReference(params) {
    const config = readReferenceConfig(params);

    document.body.style.background = config.background;
    await ensureFontLoaded(config.fontFamily, config.fontUrl);

    const canvas = createCanvas(config.width, config.height);
    const context = canvas.getContext("2d", { alpha: false });
    applyTextRenderState(context, config);
    context.fillText(config.text, config.x, config.baselineY);
    drawBaselineGuide(context, config);
}

async function renderMetrics(params) {
    const config = readReferenceConfig(params);
    await ensureFontLoaded(config.fontFamily, config.fontUrl);

    const canvas = createCanvas(config.width, config.height);
    const context = canvas.getContext("2d", { alpha: false, willReadFrequently: true });
    applyTextRenderState(context, config);
    context.fillText(config.text, config.x, config.baselineY);
    drawBaselineGuide(context, config);

    const metrics = context.measureText(config.text);
    const result = {
        text: config.text,
        fontFamily: config.fontFamily,
        fontSize: config.fontSize,
        canvasWidth: config.width,
        canvasHeight: config.height,
        x: config.x,
        baselineY: config.baselineY,
        baselineGuideEnabled: config.showBaselineGuide,
        baselineGuideY: config.showBaselineGuide ? config.baselineY : null,
        baselineGuideColor: config.showBaselineGuide ? config.baselineGuideColor : null,
        textBaseline: context.textBaseline,
        textAlign: context.textAlign,
        metrics: {
            width: toNullableNumber(metrics.width),
            actualBoundingBoxLeft: toNullableNumber(metrics.actualBoundingBoxLeft),
            actualBoundingBoxRight: toNullableNumber(metrics.actualBoundingBoxRight),
            actualBoundingBoxAscent: toNullableNumber(metrics.actualBoundingBoxAscent),
            actualBoundingBoxDescent: toNullableNumber(metrics.actualBoundingBoxDescent),
            fontBoundingBoxAscent: toNullableNumber(metrics.fontBoundingBoxAscent),
            fontBoundingBoxDescent: toNullableNumber(metrics.fontBoundingBoxDescent),
            emHeightAscent: toNullableNumber(metrics.emHeightAscent),
            emHeightDescent: toNullableNumber(metrics.emHeightDescent),
            alphabeticBaseline: toNullableNumber(metrics.alphabeticBaseline),
            hangingBaseline: toNullableNumber(metrics.hangingBaseline),
            ideographicBaseline: toNullableNumber(metrics.ideographicBaseline),
        },
        coverage: computeCoverageMetrics(context, config),
    };

    const pre = document.createElement("pre");
    pre.id = "metrics-output";
    pre.textContent = JSON.stringify(result, null, 2);

    document.body.innerHTML = "";
    document.body.appendChild(pre);
}

function computeCoverageMetrics(context, config) {
    const imageData = context.getImageData(0, 0, config.width, config.height);
    const pixels = imageData.data;
    const foreground = parseHexColor(config.foreground);
    const background = parseHexColor(config.background);
    const baselineGuide = config.showBaselineGuide
        ? parseHexColor(config.baselineGuideColor)
        : null;

    let inkTop = config.height;
    let inkBottom = -1;
    let inkLeft = config.width;
    let inkRight = -1;
    let alphaCoverageCountAbove001 = 0;
    let alphaCoverageCountAbove010 = 0;
    let alphaCoverageCountAbove050 = 0;
    let maxAlpha = 0;
    let nonZeroAlphaSum = 0;
    let nonZeroAlphaCount = 0;

    for (let y = 0; y < config.height; y += 1) {
        for (let x = 0; x < config.width; x += 1) {
            const pixelIndex = (y * config.width + x) * 4;
            const pixel = {
                r: pixels[pixelIndex],
                g: pixels[pixelIndex + 1],
                b: pixels[pixelIndex + 2],
                a: pixels[pixelIndex + 3],
            };

            if (isIgnoredPixel(pixel, background, baselineGuide)) {
                continue;
            }

            const coverage = deriveCoverage(pixel, foreground, background);
            if (coverage <= 0) {
                continue;
            }

            maxAlpha = Math.max(maxAlpha, coverage);
            nonZeroAlphaSum += coverage;
            nonZeroAlphaCount += 1;

            if (coverage > 0.01) {
                alphaCoverageCountAbove001 += 1;
                inkTop = Math.min(inkTop, y);
                inkBottom = Math.max(inkBottom, y);
                inkLeft = Math.min(inkLeft, x);
                inkRight = Math.max(inkRight, x);
            }

            if (coverage > 0.10) {
                alphaCoverageCountAbove010 += 1;
            }

            if (coverage > 0.50) {
                alphaCoverageCountAbove050 += 1;
            }
        }
    }

    if (inkBottom < 0 || inkRight < 0) {
        return {
            inkTop: null,
            inkBottom: null,
            inkLeft: null,
            inkRight: null,
            inkHeight: 0,
            inkWidth: 0,
            alphaCoverageCountAbove001,
            alphaCoverageCountAbove010,
            alphaCoverageCountAbove050,
            maxAlpha: 0,
            averageAlphaNonZero: 0,
            baselineY: config.baselineY,
            descentBelowBaseline: null,
        };
    }

    return {
        inkTop,
        inkBottom,
        inkLeft,
        inkRight,
        inkHeight: inkBottom - inkTop + 1,
        inkWidth: inkRight - inkLeft + 1,
        alphaCoverageCountAbove001,
        alphaCoverageCountAbove010,
        alphaCoverageCountAbove050,
        maxAlpha,
        averageAlphaNonZero: nonZeroAlphaCount === 0 ? 0 : nonZeroAlphaSum / nonZeroAlphaCount,
        baselineY: config.baselineY,
        descentBelowBaseline: inkBottom - config.baselineY,
    };
}

async function renderCompare(params) {
    const title = params.get("title") ?? "Machina font comparison";
    const referenceLabel = params.get("referenceLabel") ?? "Reference";
    const machinaLabel = params.get("machinaLabel") ?? "Machina MSDF";
    const referenceUrl = params.get("referenceUrl") ?? "";
    const machinaUrl = params.get("machinaUrl") ?? "";

    const root = document.createElement("div");
    root.className = "compare-root";

    const titleNode = document.createElement("div");
    titleNode.className = "compare-title";
    titleNode.textContent = title;
    root.appendChild(titleNode);

    const panels = document.createElement("div");
    panels.className = "compare-panels";
    panels.appendChild(createImagePanel(referenceLabel, referenceUrl));
    panels.appendChild(createImagePanel(machinaLabel, machinaUrl));
    root.appendChild(panels);

    document.body.appendChild(root);

    const images = Array.from(document.querySelectorAll("img"));
    await Promise.all(images.map(waitForImage));
}

function createImagePanel(label, url) {
    const panel = document.createElement("section");
    panel.className = "compare-panel";

    const heading = document.createElement("p");
    heading.className = "compare-label";
    heading.textContent = label;
    panel.appendChild(heading);

    const image = document.createElement("img");
    image.className = "compare-image";
    image.src = url;
    panel.appendChild(image);

    return panel;
}

function waitForImage(image) {
    return new Promise((resolve, reject) => {
        if (image.complete && image.naturalWidth > 0) {
            resolve();
            return;
        }

        image.addEventListener("load", () => resolve(), { once: true });
        image.addEventListener("error", () => reject(new Error(`Failed to load image: ${image.src}`)), { once: true });
    });
}

function readReferenceConfig(params) {
    return {
        width: Number.parseInt(params.get("width") ?? "320", 10),
        height: Number.parseInt(params.get("height") ?? "64", 10),
        x: Number.parseFloat(params.get("x") ?? "8"),
        baselineY: Number.parseFloat(params.get("baseline") ?? "40"),
        fontSize: Number.parseFloat(params.get("fontSize") ?? "32"),
        foreground: params.get("foreground") ?? "#f0f0f0",
        background: params.get("background") ?? "#101018",
        showBaselineGuide: readBooleanParam(params.get("showBaselineGuide"), true),
        baselineGuideColor: params.get("baselineGuideColor") ?? "#ff0000",
        fontFamily: params.get("fontFamily") ?? "OracleFixtureFont",
        fontUrl: params.get("fontUrl"),
        text: params.get("text") ?? "",
    };
}

function createCanvas(width, height) {
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    canvas.style.display = "block";
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    document.body.appendChild(canvas);
    return canvas;
}

async function ensureFontLoaded(fontFamily, fontUrl) {
    if (!fontUrl) {
        return;
    }

    const fontFace = new FontFace(fontFamily, `url("${fontUrl}")`);
    await fontFace.load();
    document.fonts.add(fontFace);
    await document.fonts.ready;
}

function applyTextRenderState(context, config) {
    context.fillStyle = config.background;
    context.fillRect(0, 0, config.width, config.height);
    context.fillStyle = config.foreground;
    context.textBaseline = "alphabetic";
    context.textAlign = "left";
    context.font = `${config.fontSize}px "${config.fontFamily}"`;
}

function parseHexColor(value) {
    const normalized = value.trim();
    const hex = normalized.startsWith("#")
        ? normalized.slice(1)
        : normalized;

    if (hex.length !== 6) {
        throw new Error(`Expected a 6-digit hex color, got: ${value}`);
    }

    return {
        r: Number.parseInt(hex.slice(0, 2), 16),
        g: Number.parseInt(hex.slice(2, 4), 16),
        b: Number.parseInt(hex.slice(4, 6), 16),
        a: 255,
    };
}

function isIgnoredPixel(pixel, background, baselineGuide) {
    if (colorsEqual(pixel, background)) {
        return true;
    }

    return baselineGuide !== null && colorsEqual(pixel, baselineGuide);
}

function colorsEqual(left, right) {
    return left.r === right.r
        && left.g === right.g
        && left.b === right.b
        && left.a === right.a;
}

function deriveCoverage(pixel, foreground, background) {
    const channels = [];

    collectChannelCoverage(channels, pixel.r, foreground.r, background.r);
    collectChannelCoverage(channels, pixel.g, foreground.g, background.g);
    collectChannelCoverage(channels, pixel.b, foreground.b, background.b);

    if (channels.length === 0) {
        return 0;
    }

    const average = channels.reduce((sum, value) => sum + value, 0) / channels.length;
    return clamp01(average);
}

function collectChannelCoverage(target, pixelChannel, foregroundChannel, backgroundChannel) {
    const channelDelta = foregroundChannel - backgroundChannel;
    if (channelDelta === 0) {
        return;
    }

    target.push((pixelChannel - backgroundChannel) / channelDelta);
}

function drawBaselineGuide(context, config) {
    if (!config.showBaselineGuide) {
        return;
    }

    const y = Math.round(config.baselineY) + 0.5;
    context.save();
    context.strokeStyle = config.baselineGuideColor;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(0, y);
    context.lineTo(config.width, y);
    context.stroke();
    context.restore();
}

function readBooleanParam(value, defaultValue) {
    if (value === null) {
        return defaultValue;
    }

    return value.toLowerCase() !== "false";
}

function toNullableNumber(value) {
    return typeof value === "number" && Number.isFinite(value)
        ? value
        : null;
}

function clamp01(value) {
    if (value < 0) {
        return 0;
    }

    if (value > 1) {
        return 1;
    }

    return value;
}
