(async () => {
    const params = new URLSearchParams(window.location.search);
    const mode = params.get("mode") ?? "reference";
    document.body.dataset.mode = mode;

    if (mode === "compare") {
        await renderCompare(params);
        return;
    }

    await renderReference(params);
})();

async function renderReference(params) {
    const width = Number.parseInt(params.get("width") ?? "320", 10);
    const height = Number.parseInt(params.get("height") ?? "64", 10);
    const x = Number.parseFloat(params.get("x") ?? "8");
    const baseline = Number.parseFloat(params.get("baseline") ?? "40");
    const fontSize = Number.parseFloat(params.get("fontSize") ?? "32");
    const foreground = params.get("foreground") ?? "#f0f0f0";
    const background = params.get("background") ?? "#101018";
    const fontFamily = params.get("fontFamily") ?? "OracleFixtureFont";
    const fontUrl = params.get("fontUrl");
    const text = params.get("text") ?? "";

    document.body.style.background = background;

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    canvas.style.display = "block";
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    document.body.appendChild(canvas);

    if (fontUrl) {
        const fontFace = new FontFace(fontFamily, `url("${fontUrl}")`);
        await fontFace.load();
        document.fonts.add(fontFace);
        await document.fonts.ready;
    }

    const context = canvas.getContext("2d", { alpha: false });
    context.fillStyle = background;
    context.fillRect(0, 0, width, height);
    context.fillStyle = foreground;
    context.textBaseline = "alphabetic";
    context.font = `${fontSize}px "${fontFamily}"`;
    context.fillText(text, x, baseline);
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
