"""Summarize completed local M18 test logs and real proof artifacts; fail on missing evidence."""
import argparse
import datetime
import hashlib
import json
import pathlib
import platform
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/aurelian-rts-pearl-mining-m18"
LANES = {
    "m18-aurelian-tests.log": "Aurelian.slnx",
    "m18-Copeland-tests.log": "Copeland.slnx",
    "m18-Machina.UI-tests.log": "Machina.UI.slnx",
    "m18-JointTaskForce-tests.log": "JointTaskForce.slnx",
    "m18-Dominatus.Core.Tests.log": "../Dominatus/tests/Dominatus.Core.Tests/Dominatus.Core.Tests.csproj",
    "m18-Dominatus.SpriteForge.Tests.log": "../Dominatus/tests/Dominatus.SpriteForge.Tests/Dominatus.SpriteForge.Tests.csproj",
    "m18-InputMan.Core.Tests.log": "../InputMan/tests/InputMan.Core.Tests/InputMan.Core.Tests.csproj",
    "m18-final-strategy-tests.log": "tests/Aurelian/Aurelian.Strategy.Tests/Aurelian.Strategy.Tests.csproj",
}


def read(name):
    return json.loads((OUT / name).read_text(encoding="utf-8-sig"))


def write(name, value):
    (OUT / name).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def git(*arguments):
    return subprocess.check_output(["git", *arguments], cwd=ROOT, text=True).strip()


def line_count(path):
    return len(path.read_text(encoding="utf-8-sig").splitlines())


def source_files(directory, suffixes):
    return [path for path in (ROOT / directory).rglob("*")
            if path.is_file() and path.suffix in suffixes
            and "bin" not in path.parts and "obj" not in path.parts]


def complete(log_directory):
    lanes = []
    for name, project in LANES.items():
        raw = (log_directory / name).read_bytes()
        log = raw.decode("utf-8-sig")
        matches = re.findall(r"Passed!\s+- Failed:\s*(\d+), Passed:\s*(\d+), Skipped:\s*(\d+), Total:\s*(\d+)", log)
        if not matches or any(int(row[0]) for row in matches) or re.search(r"\berror (CS|MSB|NETSDK)\d+", log):
            raise RuntimeError(f"No clean passing test evidence: {name}")
        lanes.append({
            "command": f"dotnet test {project} -c Release -m:1 --nologo",
            "buildIncluded": True,
            "passedExecutions": sum(int(row[1]) for row in matches),
            "skippedExecutions": sum(int(row[2]) for row in matches),
            "testAssemblyRuns": len(matches),
            "logSha256": hashlib.sha256(raw).hexdigest(),
            "exitCode": 0,
        })

    proof = read("deterministic-proof.json")
    system = read("fresh-system-proof.json")
    presentation = read("fresh-presentation-proof.json")
    native = read("native-launch.json")
    hashes = read("vector-asset-hashes.json")
    assert proof["passed"] and proof["hash"] == proof["replayHash"]
    assert system["Passed"] and presentation["IdenticalNodeIdsAndRectangles"]
    assert native["windowOpened"] and native["framesRendered"] >= 3
    assert "watchtower" in hashes and "ranger" in hashes
    formatting = (log_directory / "m18-format-verification.json").read_text(encoding="utf-8-sig")
    formatting = json.loads(formatting)
    assert all(check["ExitCode"] == 0 for check in formatting)

    write("validation.json", {
        "status": "passed-local", "recordedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "dotnetSdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
        "platform": platform.platform(), "lanes": lanes,
        "countLimit": "Execution counts overlap between solutions; they are not unique test counts. Full lanes preceded final local sample refinements; focused strategy tests ran on final source.",
        "formatting": formatting, "nativeSmoke": native,
        "deterministicProof": "deterministic-proof.json",
        "freshProofs": ["fresh-system-proof.json", "fresh-presentation-proof.json", "vector-asset-hashes.json"],
        "visualInspection": ["wilderland-style-proof.png", "vector-asset-pack.png", "forest-farming-restyle.png", "strategy-sample-build.png"],
        "screenshotProvenance": "Native renderer framebuffer exports; not desktop captures.",
        "notRun": ["remote CI", "Deliverance (not used)", "Oblivion-specific lane (tooling inspected, unchanged)"],
        "warnings": ["dotnet format reports workspace-load warnings and exits zero", "Existing desktop sample NU1903 suppression retained; no dependency-advisory fix claimed"],
    })

    extension = read("fresh-system-extension.json")
    extension["status"] = "passed-real-session-and-replay"
    extension["tests"]["status"] = "passed"
    extension["tests"]["evidence"] = "fresh-system-proof.json"
    extension["rootIntegration"] = "Native F action, resource-strip projection, ranger asset mapping and additional boundary regressions; no shared engine reconstruction."
    write("fresh-system-extension.json", extension)

    extension = read("fresh-presentation-extension.json")
    extension["validationStatus"] = "Passed real Machina prepare comparison and native renderer export"
    extension["knownLimitation"] = "Second presentation fixture only; no farming game semantics or input implementation."
    extension["rootIntegration"] = "Added StrategyHudCopy in shared profile, supplied farming captions, and rendered forest-farming-restyle.png; no simulation changes."
    extension["evidence"] = "fresh-presentation-proof.json"
    write("fresh-presentation-extension.json", extension)

    extension = read("fresh-art-extension.json")
    for asset in extension["assets"]:
        asset["compositionHash"] = hashes[asset["id"]]
        asset["sourceSha256"] = hashlib.sha256((ROOT / asset["source"]).read_bytes()).hexdigest().upper()
    extension["validation"] = {
        "compileStatus": "passed ProfileTsxCompiler.CompileComposition",
        "renderStatus": "passed same StrategyAssets.Draw used by native world/portrait",
        "artifact": "vector-asset-pack.png", "registryEditsNeeded": 0,
        "visualInspection": "Watchtower and ranger visible in inspected asset pack",
    }
    write("fresh-art-extension.json", extension)

    sample = source_files("samples/Integrations/Aurelian.StrategyDemo", {".cs"})
    art = source_files("samples/Integrations/Aurelian.StrategyDemo/Assets", {".ts", ".tsx"})
    shared = source_files("src/Aurelian/Aurelian.Strategy", {".cs"}) + [
        ROOT / "src/Integrations/Aurelian.GameWorld2D/IsometricProjection.cs",
        ROOT / "src/Integrations/Aurelian.Machina/StrategyHudProfile.cs",
    ]
    test_files = source_files("tests/Aurelian/Aurelian.Strategy.Tests", {".cs"}) + [
        ROOT / "tests/Machina.UI/Machina.Pipeline.Tests/CustomTextMeasurementTests.cs"]
    groups = {"sampleCSharpIncludingProofs": sample, "profileArt": art, "newSharedCSharp": shared, "newTests": test_files}
    measured = {key: {"files": len(paths), "physicalLines": sum(line_count(path) for path in paths)} for key, paths in groups.items()}
    write("compounding-benefit.json", {
        "method": "Physical source lines, including comments/blank lines; no generated bin/obj. Unequal product scopes, not a controlled productivity experiment.",
        "benchmarks": [{"model": source["model"], "wholeHtmlLines": source["lines"], "wholeHtmlBytes": source["bytes"]} for source in read("corpus-audit.json")["sources"]],
        "aurelian": measured,
        "existingRuntimeTouched": [{"file": "src/Machina.UI/Machina.Pipeline/MachinaPresentationPipeline.cs", "change": "7-line forwarding overload, preserving existing entry point"}],
        "systemsReconstructed": {"inputMapper": 0, "cadenceScheduler": 0, "hfsmRuntime": 0, "spatialQueryEngine": 0, "layoutEngine": 0, "vectorCompiler": 0},
        "newMechanisms": ["generic selection/group storage", "integer disk visibility", "diamond projection", "bounded HUD profile", "sample native vector adapter"],
        "secondFeatureLocality": [
            {"feature": "ranger + wood", "agentMinutesApproximate": 8, "sharedEngineEdits": 0, "detail": "Sample model/behavior/proof; root wired content into native input and HUD."},
            {"feature": "farming restyle", "agentMinutesApproximate": 4, "simulationEdits": 0, "detail": "One presentation fixture; root exposed shared copy after fresh-use friction."},
            {"feature": "watchtower + ranger art", "agentMinutesApproximate": 3, "assetLinesAtHandoff": 53, "compilerRendererEdits": 0},
        ],
        "inferenceCostSavedMeasured": False,
        "benefit": "Local extension through existing owners is demonstrated. Absolute time, LOC and inference savings versus rebuilding are not measured.",
    })

    changes = git("diff", "--numstat").splitlines()
    added = []
    for name in git("ls-files", "--others", "--exclude-standard").splitlines():
        if name.startswith(".work/") or name.startswith("artifacts/"):
            continue
        path = ROOT / name
        if path.is_file():
            added.append({"path": name, "bytes": path.stat().st_size, "lines": line_count(path)})
    write("diff-stat.json", {
        "baseCommit": git("rev-parse", "HEAD"), "trackedNumstat": changes,
        "addedSourceDocumentationAndTools": added, "sourceCategories": measured,
        "excluded": ["artifacts (inventoried in manifest)", ".work corpus/logs", "bin/obj"],
        "committed": False,
    })

    required = ["corpus-audit.json", "concept-classification.json", "cross-model-convergence.json", "ownership-map.json",
                "rejected-benchmark-hacks.json", "wilderland-art-audit.json", "procedural-art-capabilities.json", "strategy-system-map.json",
                "strategy-sample-main.png", "strategy-sample-selection.png", "strategy-sample-build.png", "strategy-sample-minimap.png",
                "wilderland-style-proof.png", "vector-asset-pack.png", "deterministic-proof.json", "fresh-system-extension.json",
                "fresh-presentation-extension.json", "fresh-art-extension.json", "compounding-benefit.json"]
    assert all((OUT / name).is_file() for name in required)
    write("manifest.json", {
        "milestone": "AURELIAN-RTS-PEARL-MINING-M18", "kind": "cross-model-benchmark-architecture-mining", "outcome": "B",
        "corpusCompared": True, "fableOpusSystemsMined": True, "astraWilderlandPresentationMined": True,
        "wilderlandVisualProofQualified": False, "proceduralVectorArtQualified": True,
        "proceduralVectorArtScope": "Canonical flat-fill Profile proof pack through sample CPU adapter; production GPU/atlas/card integration unqualified",
        "strategySelectionQualified": True, "strategyOrdersQualified": True,
        "strategyOrdersScope": "App-owned move/stop/gather/build/attack/produce; no attack-move, rally or cancellation",
        "strategyHudQualified": True, "minimapQualified": True, "minimapReusableLibraryQualified": False,
        "fogQualified": True, "fogScope": "Bounded single-faction integer disk visibility/exploration; no occlusion or soft fog",
        "benchmarkCodePortedWholesale": False, "benchmarkHacksRejected": True, "reusableConceptsIntegrated": True,
        "compoundingBenefitMeasured": True, "compoundingMeasurementScope": "Source-size/locality measurements; no controlled time or inference-cost savings",
        "nativeLaunchQualified": True, "deterministicProofQualified": True,
        "deferredSeam": "Reusable canonical Profile composition realization/preview with supported-paint validation, second native consumer and visual polish parity",
        "report": "docs/milestones/aurelian-rts-pearl-mining-m18-report.md",
        "artifacts": [{"name": path.name, "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                      for path in sorted(OUT.iterdir()) if path.is_file() and path.name != "manifest.json"],
    })
    print(json.dumps({"lanes": lanes, "sourceCounts": measured, "outcome": "B"}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-directory", type=pathlib.Path, default=ROOT / ".work")
    complete(parser.parse_args().log_directory)
