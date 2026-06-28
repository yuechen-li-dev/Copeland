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
    const context = canvas.getContext("2d", { alpha: false });
    applyTextRenderState(context, config);

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
    };

    const pre = document.createElement("pre");
    pre.id = "metrics-output";
    pre.textContent = JSON.stringify(result, null, 2);

    document.body.innerHTML = "";
    document.body.appendChild(pre);
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
