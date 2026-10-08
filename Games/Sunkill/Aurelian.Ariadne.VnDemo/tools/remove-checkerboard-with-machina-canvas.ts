import { readFileSync, writeFileSync } from "node:fs";
import { deriveAlphaMapPixels } from "../../../../../machina-canvas/src/tools/generateAlphaMap";

const [inputPath, outputPath, widthText, heightText] = process.argv.slice(2);
const width = Number.parseInt(widthText ?? "", 10);
const height = Number.parseInt(heightText ?? "", 10);
if (!inputPath || !outputPath || !Number.isInteger(width) || !Number.isInteger(height)) {
  throw new Error("Usage: <rgba-input> <alpha-output> <width> <height>");
}

const bytes = readFileSync(inputPath);
const rgba = new Uint8ClampedArray(bytes.buffer, bytes.byteOffset, bytes.byteLength);
const alphaMap = deriveAlphaMapPixels({ width, height, data: rgba }, { threshold: 32 });
const alpha = new Uint8Array(width * height);
for (let index = 0; index < alpha.length; index += 1) {
  alpha[index] = alphaMap.data[index * 4];
}
writeFileSync(outputPath, alpha);
