#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import zlib from "node:zlib";
import { performance } from "node:perf_hooks";
import { spawnSync } from "node:child_process";

const repoRoot = findRepoRoot(process.cwd());
const args = parseArgs(process.argv.slice(2));
const corpusRoot = path.resolve(repoRoot, args.corpus);

const cases = [
  corpusCase("primitive", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/main-returns-42", "main()"),
  corpusCase("payload-enum", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/payload-enum-match", "main()"),
  corpusCase("result-propagation", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/result-propagation", "(() => { const value = stored(); return value.$tag + ':' + String(value.$payload[0]); })()"),
  corpusCase("try-except", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/try-except-success", "main()"),
  corpusCase("record", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/record-basic", "main()"),
  corpusCase("record-with", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/record-order-with", "main()"),
  corpusCase("table", "Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/m2-table-basic", "main()"),
  corpusCase("tson-record", "Copeland.TS.Tests/TsonEncoding/Corpus/record/main", "(() => { const value = encode(); return value.$tag === 'ok' ? value.$payload[0] : 'ERR:' + value.$payload[0].$tag; })()"),
  corpusCase("tson-array", "Copeland.TS.Tests/TsonEncoding/Corpus/arrays/main", "(() => { const value = encode(); return value.$tag === 'ok' ? value.$payload[0] : 'ERR:' + value.$payload[0].$tag; })()"),
  corpusCase("tson-table", "Copeland.TS.Tests/TsonEncoding/Corpus/tables-m2/main", "(() => { const value = encode(); return value.$tag === 'ok' ? value.$payload[0] : 'ERR:' + value.$payload[0].$tag; })()"),
  corpusCase("tson-table-asset", "Copeland.TS.Tests/TsonTableAssets/Corpus/representative/main", "JSON.stringify([observation(), Object.is(negativeZero(), -0), nested(), emptyBounds()])"),
];

const measured = cases.map((entry) => measureCase(entry, args));
const aggregate = summarizeAggregate(measured);

if (args.json) {
  process.stdout.write(JSON.stringify({ node: process.version, cases: measured, aggregate }, null, 2) + "\n");
  process.exit(0);
}

printHeader("Copeland JavaScript Emission Measurements");
console.log(`Node: ${process.version}`);
console.log(`Corpus root: ${corpusRoot}`);
console.log("");
console.log("| Case | Diagnostic raw | Symbolic raw | Diagnostic gzip | Symbolic gzip | Diagnostic Brotli | Symbolic Brotli |");
console.log("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
for (const entry of measured) {
  console.log(`| ${entry.name} | ${entry.diagnostic.rawBytes} | ${entry.symbolic.rawBytes} | ${entry.diagnostic.gzipBytes} | ${entry.symbolic.gzipBytes} | ${entry.diagnostic.brotliBytes} | ${entry.symbolic.brotliBytes} |`);
}
console.log(`| aggregate | ${aggregate.diagnostic.rawBytes} | ${aggregate.symbolic.rawBytes} | ${aggregate.diagnostic.gzipBytes} | ${aggregate.symbolic.gzipBytes} | ${aggregate.diagnostic.brotliBytes} | ${aggregate.symbolic.brotliBytes} |`);
console.log("");
console.log("| Case | Parse ms D p50/mean/max | Parse ms S p50/mean/max | Warm runtime ms D p50/mean/max | Warm runtime ms S p50/mean/max | Output parity |");
console.log("| --- | --- | --- | --- | --- | --- |");
for (const entry of measured.filter((value) => value.runtime !== null)) {
  console.log(`| ${entry.name} | ${formatDistribution(entry.diagnostic.parseMs)} | ${formatDistribution(entry.symbolic.parseMs)} | ${formatDistribution(entry.diagnostic.warmRuntimeMs)} | ${formatDistribution(entry.symbolic.warmRuntimeMs)} | ${entry.runtime.parity ? "equal" : "DIFF"} |`);
}
console.log("");
console.log("| Aggregate | Generated-binding bytes D | Generated-binding bytes S | Symbol-description bytes D | Symbol-description bytes S | Raw whitespace bytes D | Raw whitespace bytes S |");
console.log("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
console.log(`| total | ${aggregate.diagnostic.generatedBindingBytes} | ${aggregate.symbolic.generatedBindingBytes} | ${aggregate.diagnostic.symbolDescriptionBytes} | ${aggregate.symbolic.symbolDescriptionBytes} | ${aggregate.diagnostic.whitespaceBytes} | ${aggregate.symbolic.whitespaceBytes} |`);

function parseArgs(argv) {
  let corpus = "tests/Copeland";
  let gzipLevel = 9;
  let brotliQuality = 11;
  let json = false;
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    switch (argument) {
      case "--corpus":
        corpus = requireValue(argv, ++index, argument);
        break;
      case "--gzip-level":
        gzipLevel = Number.parseInt(requireValue(argv, ++index, argument), 10);
        break;
      case "--brotli-quality":
        brotliQuality = Number.parseInt(requireValue(argv, ++index, argument), 10);
        break;
      case "--json":
        json = true;
        break;
      default:
        throw new Error(`Unknown option '${argument}'.`);
    }
  }

  return { corpus, gzipLevel, brotliQuality, json };
}

function requireValue(argv, index, option) {
  if (index >= argv.length) {
    throw new Error(`Option '${option}' requires a value.`);
  }

  return argv[index];
}

function findRepoRoot(start) {
  let current = path.resolve(start);
  for (;;) {
    if (fs.existsSync(path.join(current, "Copeland.slnx"))) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error("Could not locate repository root.");
    }

    current = parent;
  }
}

function corpusCase(name, relativeStem, runtimeExpression) {
  return {
    name,
    sourcePath: path.join(corpusRoot, relativeStem + ".ts"),
    diagnosticPath: path.join(corpusRoot, relativeStem + ".g.js"),
    symbolicPath: path.join(corpusRoot, relativeStem + ".sym.js"),
    runtimeExpression,
  };
}

function measureCase(entry, options) {
  const diagnosticSource = fs.readFileSync(entry.diagnosticPath, "utf8");
  const symbolicSource = fs.readFileSync(entry.symbolicPath, "utf8");
  const diagnostic = measureArtifact(diagnosticSource, entry.diagnosticPath, options);
  const symbolic = measureArtifact(symbolicSource, entry.symbolicPath, options);
  const runtime = measureRuntime(entry, diagnosticSource, symbolicSource);
  if (runtime !== null) {
    diagnostic.warmRuntimeMs = runtime.diagnosticWarmRuntimeMs;
    symbolic.warmRuntimeMs = runtime.symbolicWarmRuntimeMs;
  }

  return {
    name: entry.name,
    sourceBytes: fs.readFileSync(entry.sourcePath).length,
    diagnostic,
    symbolic,
    runtime,
  };
}

function measureArtifact(sourceText, filePath, options) {
  const bytes = Buffer.from(sourceText, "utf8");
  checkNodeParse(filePath);
  return {
    rawBytes: bytes.length,
    gzipBytes: zlib.gzipSync(bytes, { level: options.gzipLevel }).length,
    brotliBytes: zlib.brotliCompressSync(bytes, {
      params: { [zlib.constants.BROTLI_PARAM_QUALITY]: options.brotliQuality },
    }).length,
    parseMs: measureParse(sourceText, filePath),
    warmRuntimeMs: null,
    generatedBindingBytes: generatedBindingBytes(sourceText),
    symbolDescriptionBytes: symbolDescriptionBytes(sourceText),
    whitespaceBytes: whitespaceBytes(sourceText),
  };
}

function checkNodeParse(filePath) {
  const result = spawnSync(process.execPath, ["--check", filePath], {
    cwd: path.dirname(filePath),
    encoding: "utf8",
  });
  if (result.status !== 0) {
    throw new Error(`Node parse failed for '${filePath}': ${result.stderr}`);
  }
}

function measureParse(sourceText, filePath) {
  const times = [];
  for (let index = 0; index < 25; index += 1) {
    const start = performance.now();
    new vm.Script(sourceText, { filename: filePath });
    times.push(performance.now() - start);
  }

  return summarizeDistribution(times);
}

function measureRuntime(entry, diagnosticSource, symbolicSource) {
  if (!entry.runtimeExpression) {
    return null;
  }

  const diagnostic = runRuntimeCase(diagnosticSource, entry.runtimeExpression, entry.diagnosticPath);
  const symbolic = runRuntimeCase(symbolicSource, entry.runtimeExpression, entry.symbolicPath);

  return {
    parity: diagnostic.output === symbolic.output,
    diagnosticOutput: diagnostic.output,
    symbolicOutput: symbolic.output,
    diagnosticWarmRuntimeMs: diagnostic.warmRuntimeMs,
    symbolicWarmRuntimeMs: symbolic.warmRuntimeMs,
  };
}

function runRuntimeCase(sourceText, expression, filename) {
  const bootstrap = `${sourceText}\nglobalThis.__cope_measure = () => (${expression});`;
  const serializer = new vm.Script("(() => { const value = globalThis.__cope_measure(); if (value === undefined) return 'undefined'; return typeof value === 'string' ? value : JSON.stringify(value); })()");
  const context = vm.createContext({});
  new vm.Script(bootstrap, { filename }).runInContext(context);
  const output = serializer.runInContext(context);
  const warmTimes = [];
  for (let index = 0; index < 50; index += 1) {
    const start = performance.now();
    serializer.runInContext(context);
    warmTimes.push(performance.now() - start);
  }

  return {
    output,
    warmRuntimeMs: summarizeDistribution(warmTimes),
  };
}

function generatedBindingBytes(sourceText) {
  const diagnosticMatches = sourceText.match(/__cope_m3_[A-Za-z0-9_]+/g) ?? [];
  const symbolicMatches = sourceText.match(/\$[表行列录枚项载组果成错流函接型值存符印造验取更编源纲识界律终助运串数布域序配传解槽收支临计写返附甲乙丙丁戊己庚辛壬癸]+/g) ?? [];
  return [...diagnosticMatches, ...symbolicMatches].reduce((total, current) => total + Buffer.byteLength(current, "utf8"), 0);
}

function symbolDescriptionBytes(sourceText) {
  const matches = [...sourceText.matchAll(/Symbol\("([^"]*)"\)/g)];
  return matches.reduce((total, current) => total + Buffer.byteLength(current[0], "utf8"), 0);
}

function whitespaceBytes(sourceText) {
  let total = 0;
  for (const character of sourceText) {
    if (character === " " || character === "\n" || character === "\t") {
      total += Buffer.byteLength(character, "utf8");
    }
  }

  return total;
}

function summarizeDistribution(values) {
  const sorted = [...values].sort((left, right) => left - right);
  const sum = values.reduce((total, current) => total + current, 0);
  return {
    min: sorted[0],
    p50: sorted[Math.floor(sorted.length / 2)],
    mean: sum / values.length,
    max: sorted[sorted.length - 1],
  };
}

function summarizeAggregate(measured) {
  return {
    diagnostic: measured.reduce((aggregate, current) => addArtifact(aggregate, current.diagnostic), zeroArtifact()),
    symbolic: measured.reduce((aggregate, current) => addArtifact(aggregate, current.symbolic), zeroArtifact()),
  };
}

function zeroArtifact() {
  return {
    rawBytes: 0,
    gzipBytes: 0,
    brotliBytes: 0,
    generatedBindingBytes: 0,
    symbolDescriptionBytes: 0,
    whitespaceBytes: 0,
  };
}

function addArtifact(total, current) {
  return {
    rawBytes: total.rawBytes + current.rawBytes,
    gzipBytes: total.gzipBytes + current.gzipBytes,
    brotliBytes: total.brotliBytes + current.brotliBytes,
    generatedBindingBytes: total.generatedBindingBytes + current.generatedBindingBytes,
    symbolDescriptionBytes: total.symbolDescriptionBytes + current.symbolDescriptionBytes,
    whitespaceBytes: total.whitespaceBytes + current.whitespaceBytes,
  };
}

function formatDistribution(distribution) {
  return `${distribution.p50.toFixed(3)} / ${distribution.mean.toFixed(3)} / ${distribution.max.toFixed(3)}`;
}

function printHeader(text) {
  console.log(text);
  console.log("=".repeat(text.length));
}
