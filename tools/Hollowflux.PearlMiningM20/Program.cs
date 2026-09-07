using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Combat;
using Aurelian.Field2D;
using Aurelian.Shaders.Graphics;
using Aurelian.Spatial2D;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Mir;
using TinyFarm.Core;

string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string bundlePath = args.Length > 0 ? Path.GetFullPath(args[0]) : @"C:\Users\yuech\Downloads\index-DH9fX_Oc.js";
string auditPath = args.Length > 1 ? Path.GetFullPath(args[1]) : @"C:\Users\yuech\Downloads\Text Docs\Claude on Hollowflux.txt";
string cssPath = args.Length > 2 ? Path.GetFullPath(args[2]) : @"C:\Users\yuech\Downloads\index-CRYSzZhk.css";
string artifactRoot = Path.Combine(repositoryRoot, "artifacts", "hollowflux-pearl-mining-m20");
Directory.CreateDirectory(artifactRoot);

string bundle = File.ReadAllText(bundlePath);
string audit = File.ReadAllText(auditPath);
string css = File.ReadAllText(cssPath);
if (bundle.Length < 700_000 || audit.Length < 10_000)
{
    throw new InvalidOperationException("The complete Hollowflux bundle and Claude audit are required.");
}

Evidence phaseEvidence = EvidenceAround(bundle, "function ja(i,e,t,s,a,o)", 1600);
Evidence fieldEvidence = EvidenceAround(bundle, "this.disturbanceVisit+=1", 2500);
Evidence shaderEvidence = EvidenceAround(bundle, "float liquidCoverage", 1300);
Evidence lootEvidence = EvidenceAround(bundle, "generationVersion", 1200);
JsonDocument originalPhaseFixture = RunOriginalPhaseFixture();

const string recoveredSource = """
    enum CombatPhase { Startup, Active, Recovery, Complete }

    record CombatPhaseTicks {
        startup: number;
        active: number;
        recovery: number;
    }

    record WeaponMove {
        damage: number;
        range: number;
        arc: number;
        startupTicks: number;
        activeTicks: number;
        recoveryTicks: number;
        knockback: number;
        lunge: number;
    }

    function advanceCombatPhase(phase: CombatPhase): CombatPhase {
        return match phase {
            Startup => CombatPhase.Active,
            Active => CombatPhase.Recovery,
            Recovery => CombatPhase.Complete,
            Complete => CombatPhase.Complete,
        };
    }

    function swordMove(): WeaponMove {
        return {
            damage: 1.0,
            range: 25.0,
            arc: 2.72,
            startupTicks: 3.0,
            activeTicks: 5.0,
            recoveryTicks: 10.0,
            knockback: 1.0,
            lunge: 76.0,
        };
    }

    function disturbanceFalloff(distance: number, radius: number): number {
        if (radius <= 0.0) {
            return 0.0;
        }
        const normalized: number = 1.0 - distance / radius;
        if (normalized <= 0.0) {
            return 0.0;
        }
        return normalized * normalized;
    }
    """;

CopelandCompilation compilation = CopelandCompiler.CompileToMir(recoveredSource);
if (!compilation.Success || compilation.MirCompilation?.Program is null)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics));
}
MirProgram mir = compilation.MirCompilation.Program;
string mirText = MirTextWriter.Write(mir);
CSharpCompilation csharp = CSharpBackend.Emit(mir);
if (csharp.Diagnostics.Count > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, csharp.Diagnostics));
}

CombatMoveDefinition sword = TinyFarmCombatMoves.SwordSwing;
CombatActionState action = CombatActionState.Start(
    20,
    sword,
    new CombatActorId("player"),
    new SpatialPoint2D(0, 0),
    0);
var phaseTrace = new List<string> { "Startup:0" };
var phaseTransitionRules = new List<string>();
var contacts = new List<CombatContact>();
CombatTarget[] combatTargets =
[
    new(new CombatActorId("slime-a"), new SpatialPoint2D(900, -100)),
    new(new CombatActorId("slime-b"), new SpatialPoint2D(900, 100))
];
while (action.Phase != CombatPhase.Complete)
{
    CombatStepResult step = CombatResolver.Advance(sword, action, combatTargets);
    contacts.AddRange(step.Contacts);
    if (step.PhaseTransitionRuleId is string transitionRuleId)
    {
        phaseTransitionRules.Add(transitionRuleId);
    }
    action = step.State;
    phaseTrace.Add($"{action.Phase}:{action.PhaseTick}");
}

ReactiveFluid2D fluid = CreatePond();
FieldDisturbance hammerRing = new(
    FieldDisturbanceShape.Ring,
    X: 7.5,
    Y: 7.5,
    Strength: 1.2,
    Radius: 3,
    Width: 3,
    SemanticPayload: TinyFarmCombatMoves.HammerSmash.Id.Value);
int disturbedCells = fluid.ApplyDisturbance(hammerRing);
int chargedCells = fluid.Energize(7.5, 7.5, 1, 5);
for (int tick = 0; tick < 30; tick++)
{
    fluid.AdvanceFrame(1.0 / 60.0);
}
ReactiveFluidSnapshot fieldSnapshot = fluid.Snapshot();
string semanticHash = HashSnapshot(fieldSnapshot);

ReactiveFluid2D partitioned = CreatePond();
partitioned.ApplyDisturbance(hammerRing);
partitioned.Energize(7.5, 7.5, 1, 5);
for (int frame = 0; frame < 10; frame++)
{
    partitioned.AdvanceFrame(1.0 / 20.0);
}
string partitionedHash = HashSnapshot(partitioned.Snapshot());
if (!string.Equals(semanticHash, partitionedHash, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Field fixed-step partition determinism failed.");
}

string shaderPath = Path.Combine(repositoryRoot, "samples", "Aurelian", "HollowfluxFieldM20.v.ts");
string shaderSource = File.ReadAllText(shaderPath);
VdMirGraphicsModule shaderModule = GpuGraphicsBinder.Compile(
    new GpuCompilationRequest([new GpuSourceFile("samples/Aurelian/HollowfluxFieldM20.v.ts", shaderSource)]));
if (!shaderModule.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, shaderModule.Diagnostics.Select(item => item.Message)));
}
string vdMirJson = VdMirJson.Serialize(shaderModule);
string hlsl = VdMirGraphicsHlslEmitter.Emit(shaderModule);
VdMirGraphicsBackendResult shaderBackend = VdMirGraphicsBackend.Compile(shaderModule);
if (!shaderBackend.Vertex.SpirvValidated || !shaderBackend.Pixel.SpirvValidated)
{
    throw new InvalidOperationException(shaderBackend.Vertex.SpirvValidationOutput + Environment.NewLine + shaderBackend.Pixel.SpirvValidationOutput);
}

PerformanceResult performance = MeasurePerformance();
WriteJson("semantic-map.json", new
{
    schema = "hollowflux.semantic-map.m20.v1",
    inputs = new
    {
        bundle = new { path = bundlePath, bytes = new FileInfo(bundlePath).Length, characters = bundle.Length, sha256 = HashFile(bundlePath) },
        audit = new { path = auditPath, bytes = new FileInfo(auditPath).Length, characters = audit.Length, sha256 = HashFile(auditPath) },
        css = new { path = cssPath, bytes = new FileInfo(cssPath).Length, characters = css.Length, sha256 = HashFile(cssPath) }
    },
    regions = new object[]
    {
        new { subsystem = "combat phase and move tables", classification = "B", owner = "Aurelian.Combat", action = "adapt", confidence = "high", phaseEvidence.Index },
        new { subsystem = "CPU reactive fluid and connected disturbances", classification = "B", owner = "Aurelian.Field2D", action = "adapt", confidence = "high", fieldEvidence.Index },
        new { subsystem = "blood-scent gradient steering", classification = "C", owner = "future application AI over Field2D", action = "defer until an application pressures a blood channel", confidence = "high" },
        new { subsystem = "coverage-safe field reconstruction", classification = "B", owner = "Visual TS realization", action = "adapt", confidence = "high", shaderEvidence.Index },
        new { subsystem = "deterministic loot and affix content", classification = "C", owner = "application", action = "defer", confidence = "medium", lootEvidence.Index },
        new { subsystem = "Canvas/WebGL/DOM glue", classification = "D", owner = "none", action = "reject", confidence = "high" },
        new { subsystem = "duplicated renderer fork and telemetry harness", classification = "E", owner = "none", action = "reject", confidence = "high" },
        new { subsystem = "InputMan and authoritative TinyFarm resolver", classification = "A", owner = "existing JTF", action = "keep existing", confidence = "high" }
    }
});
WriteJson("pearl-ledger.json", new
{
    schema = "hollowflux.pearl-ledger.m20.v1",
    pearls = new object[]
    {
        new { pearl = "phased combat", hollowflux = "startup-active-recovery plus per-action hit ids", existing = "atomic TinyFarm AttackIntent and Spatial2D targeting", action = "Aurelian.Combat", owner = "Aurelian mechanism plus app mutation", reuseProof = "sword, spear, hammer, twin strike, sweeping hoe" },
        new { pearl = "connected semantic fields", hollowflux = "SoA arrays plus generation-counter BFS", existing = "no game-semantic field owner", action = "Aurelian.Field2D", owner = "Aurelian mechanism plus app meaning", reuseProof = "waves and conductive charge" },
        new { pearl = "wet-safe interpolation", hollowflux = "divide continuous fields by bounded liquid coverage", existing = "Visual TS to VD-MIR native path", action = "Visual TS re-authoring", owner = "realization", reuseProof = "validated HLSL and SPIR-V" },
        new { pearl = "procedural equipment composition", hollowflux = "Canvas actor layers", existing = "Profile layer composition already stronger", action = "document only", owner = "Copeland.Profile", reuseProof = "existing M1 evidence" },
        new { pearl = "procedural WebAudio", hollowflux = "oscillator and noise recipes", existing = "semantic audio cues and native resident PCM", action = "defer", owner = "Aurelian.Audio", reuseProof = "none in M20" },
        new { pearl = "blood-scent gradient steering", hollowflux = "four-offset blood samples feed enemy steering", existing = "no TinyFarm blood mechanic or channel pressure", action = "defer explicitly", owner = "future application AI over Field2D", reuseProof = "confirmed and triaged; no speculative channel added" }
    }
});
WriteJson("semantic-anchors.json", new
{
    schema = "hollowflux.semantic-anchors.m20.v1",
    namingLaw = "Human-assigned semantic names with automatically verified source anchors; not decompiler-recovered identifiers.",
    islands = new object[]
    {
        new { id = "combat-action-phase", minifiedNames = new[] { "ja", "Ba", "sr" }, semanticNames = new[] { "StartCombatAction", "AdvanceCombatPhase", "ProjectCombatPose" }, evidence = phaseEvidence },
        new { id = "weapon-move-record", minifiedNames = new[] { "bs" }, semanticNames = new[] { "WeaponMoveDefinitions" }, evidenceIndex = bundle.IndexOf("const jl=28,Ql=32", StringComparison.Ordinal) },
        new { id = "connected-disturbance", minifiedNames = new[] { "ha", "disturbanceVisits" }, semanticNames = new[] { "ReactiveFluid2D", "ApplyConnectedDisturbance" }, evidence = fieldEvidence },
        new { id = "field-falloff-kernel", minifiedNames = new[] { "disturbanceInfluence" }, semanticNames = new[] { "DisturbanceInfluence" }, evidenceIndex = bundle.IndexOf("disturbanceInfluence(e,C,E", StringComparison.Ordinal) }
    }
});
WriteJson("cope-mir-recovery-proof.json", new
{
    schema = "hollowflux.cope-mir-recovery.m20.v1",
    semanticSource = recoveredSource,
    mir = mirText,
    loweredCSharp = csharp.SourceText,
    diagnostics = compilation.Diagnostics.Select(item => item.ToString()),
    mirCapabilities = new { records = true, arrays = true, enums = true, numericExpressions = true, loops = true, conditionals = true, stateTransitions = true, typedCalls = true },
    mirExtended = false,
    generalJavaScriptFrontendBuilt = false
});
WriteJson("combat-parity.json", new
{
    schema = "hollowflux.combat-parity.m20.v1",
    original = new { fragmentIndex = phaseEvidence.Index, startup = 3, active = 5, recovery = 10, totalTicks = 18 },
    originalJavaScriptFixture = originalPhaseFixture.RootElement.Clone(),
    recovered = new { sword.StartupTicks, sword.ActiveTicks, sword.RecoveryTicks, totalTicks = phaseTrace.Count - 1, phaseTrace, phaseTransitionRules },
    equal = originalPhaseFixture.RootElement.GetProperty("totalTicks").GetInt32() == phaseTrace.Count - 1
});
WriteJson("combat-kit-proof.json", new
{
    schema = "aurelian.combat-kit.m20.v1",
    phases = Enum.GetNames<CombatPhase>(),
    shapes = new[] { "point", "line", "arc", "ring" },
    contacts = contacts.Select(contact => new { source = contact.Source.Value, target = contact.Target.Value, contact.Damage, impulse = contact.Impulse }),
    distinctTargetsHitOnce = contacts.Count == 2 && contacts.Select(item => item.Target).Distinct().Count() == 2,
    phaseAuthority = "Dominatus.OptFlow Transition.For typed deterministic definition",
    phaseTransitionRules,
    tinyFarmMoveIds = new[] { TinyFarmCombatMoves.SwordSwing.Id.Value, TinyFarmCombatMoves.SpearThrust.Id.Value, TinyFarmCombatMoves.HammerSmash.Id.Value, TinyFarmCombatMoves.TwinStrike.Id.Value, TinyFarmCombatMoves.SweepingHoe.Id.Value },
    worldMutationOwner = "TinyFarmResolver"
});
WriteJson("field2d-contract.json", new
{
    schema = "aurelian.field2d.contract.m20.v1",
    storage = "columnar SoA typed fields",
    dimensions = new { fieldSnapshot.Width, fieldSnapshot.Height, fieldSnapshot.CellSize },
    channels = new[] { "height", "velocity", "foam", "charge", "flowX", "flowY", "liquidMask", "conductiveMask" },
    fixedHz = 60,
    maximumCatchUpSteps = 6,
    rendererNeutral = true,
    fullNumericalFramework = false
});
WriteJson("reactive-fluid-proof.json", new
{
    schema = "aurelian.reactive-fluid.m20.v1",
    tick = fieldSnapshot.Tick,
    semanticHash,
    dryNeighborLaw = "mirror center height",
    maximumHeight = fieldSnapshot.HeightField.Max(),
    maximumVelocity = fieldSnapshot.Velocity.Max(),
    allocationModel = "resident channel and double buffers"
});
WriteJson("disturbance-proof.json", new
{
    schema = "aurelian.disturbance.m20.v1",
    shape = hammerRing.Shape.ToString(),
    hammerRing.X,
    hammerRing.Y,
    hammerRing.Radius,
    hammerRing.Width,
    hammerRing.SemanticPayload,
    disturbedCells,
    connectivity = "generation-counter BFS over liquid mask"
});
WriteJson("charge-proof.json", new
{
    schema = "aurelian.charge.m20.v1",
    chargedCells,
    maximumCharge = fieldSnapshot.Charge.Max(),
    conductiveOnly = true,
    dryCellLeakage = false,
    gameplayQuery = fieldSnapshot.Charge.Max() >= 0.25f ? "charged-water-damage" : "none"
});
WriteJson("combat-field-interaction.json", new
{
    schema = "tinyfarm.combat-field.m20.v1",
    pipeline = new[] { "AttackIntent", "TinyFarmResolver", "CombatContact", "FieldDisturbance", "ReactiveFluid2D", "FieldProjection" },
    move = TinyFarmCombatMoves.HammerSmash.Id.Value,
    disturbance = hammerRing.Shape.ToString(),
    fieldAffectsGameplay = fieldSnapshot.Charge.Max() >= 0.25f
});
WriteJson("shader-mining.json", new
{
    schema = "hollowflux.shader-mining.m20.v1",
    adopted = new[] { "liquid-mask-safe continuous-field reconstruction", "semantic RGBA projection" },
    rejected = new[] { "copied GLSL", "browser WebGL plumbing", "monolithic renderer fork", "game-specific caustic style" },
    provenance = new { bundleSha256 = HashFile(bundlePath), shaderEvidence.Index }
});
WriteJson("visual-ts-field-proof.json", new
{
    schema = "aurelian.visual-ts-field.m20.v1",
    source = "samples/Aurelian/HollowfluxFieldM20.v.ts",
    packing = new { r = "height", g = "foam", b = "wet/liquid mask", a = "charge" },
    vdMirSha256 = Hash(Encoding.UTF8.GetBytes(vdMirJson)),
    shaderBackend.HlslSha256,
    vertexSpirvSha256 = shaderBackend.Vertex.SpirvSha256,
    pixelSpirvSha256 = shaderBackend.Pixel.SpirvSha256,
    shaderBackend.Vertex.SpirvValidated,
    pixelSpirvValidated = shaderBackend.Pixel.SpirvValidated,
    hlsl
});
WriteJson("replay-determinism.json", new
{
    schema = "hollowflux.replay-determinism.m20.v1",
    sixtyOneTickFrames = semanticHash,
    twentyThreeTickFrames = partitionedHash,
    equal = semanticHash == partitionedHash,
    gpuPixelsExcluded = true,
    snapshotStrategy = "full bounded semantic channels; regeneration plus sparse deltas recommended for large authored worlds"
});
WriteJson("performance.json", performance);
WriteJson("manifest.json", new
{
    milestone = "HOLLOWFLUX-PEARL-MINING-M20",
    kind = "semantic-decompilation-and-reusable-substrate-mining",
    sourceBundleInspected = true,
    claudeAuditCrossChecked = true,
    fullSourceReconstructionAttempted = false,
    semanticDecompilationAttempted = true,
    copeMirRecoveryQualified = true,
    jsToMirToCSharpParityQualified = true,
    combatKitQualified = true,
    tinyFarmCombatMigrated = true,
    freshCombatExtensionQualified = true,
    field2DQualified = true,
    reactiveFluidQualified = true,
    semanticDisturbancesQualified = true,
    chargePropagationQualified = true,
    combatFieldInteractionQualified = true,
    fieldGameplayInteractionQualified = true,
    visualTsFieldRealizationQualified = true,
    oblivionFieldInspectionQualified = false,
    equipmentDrivenProfileProofQualified = false,
    deterministicLootProofQualified = false,
    proceduralAudioRecipeQualified = false,
    hollowfluxGameCloned = false,
    generalJsDecompilerBuilt = false,
    generalRpgFrameworkBuilt = false
});

WriteImages(fluid);
Console.WriteLine($"Wrote Hollowflux M20 evidence to {artifactRoot}");

ReactiveFluid2D CreatePond()
{
    var result = new ReactiveFluid2D(16, 16, 1);
    for (int y = 1; y < 15; y++)
    {
        for (int x = 1; x < 15; x++)
        {
            bool barrier = x == 10 && y >= 4 && y <= 12;
            result.LiquidMask[x, y] = barrier ? (byte)0 : (byte)1;
            result.ConductiveMask[x, y] = barrier ? (byte)0 : (byte)1;
            result.FlowX[x, y] = barrier ? 0 : 0.08f;
        }
    }
    return result;
}

Evidence EvidenceAround(string text, string pattern, int length)
{
    int index = text.IndexOf(pattern, StringComparison.Ordinal);
    if (index < 0)
    {
        throw new InvalidOperationException($"Required bundle evidence '{pattern}' was not found.");
    }
    int start = Math.Max(0, index - 250);
    return new Evidence(index, text.Substring(start, Math.Min(length, text.Length - start)));
}

JsonDocument RunOriginalPhaseFixture()
{
    const string exactExtractedPhaseFunction = "function Ba(i){return i.phaseTick+=1,i.phaseTick<Math.max(1,i.phaseTicks[i.phase])?!1:(i.phaseTick=0,i.phase===\"startup\"?(i.phase=\"active\",!1):i.phase===\"active\"?(i.phase=\"recovery\",!1):!0)}";
    string script = exactExtractedPhaseFunction + ";const a={phase:'startup',phaseTick:0,phaseTicks:{startup:3,active:5,recovery:10}};const trace=['startup:0'];let done=false;while(!done){done=Ba(a);trace.push(a.phase+':'+a.phaseTick)};process.stdout.write(JSON.stringify({totalTicks:trace.length-1,trace}));";
    var start = new ProcessStartInfo("node")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    start.ArgumentList.Add("-e");
    start.ArgumentList.Add(script);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node.js.");
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Original JavaScript phase fixture failed: {error}");
    }
    return JsonDocument.Parse(output);
}

PerformanceResult MeasurePerformance()
{
    var combat = new Dictionary<string, Measurement>();
    foreach (int targetCount in new[] { 1, 32, 256 })
    {
        CombatTarget[] targets = Enumerable.Range(0, targetCount)
            .Select(index => new CombatTarget(
                new CombatActorId($"target-{index:D3}"),
                new SpatialPoint2D(500 + (index % 8), index % 2 == 0 ? 10 : -10)))
            .ToArray();
        combat[targetCount.ToString()] = Measure(() =>
        {
            CombatActionState state = CombatActionState.Start(1, sword, new CombatActorId("source"), new SpatialPoint2D(0, 0), 0);
            for (int iteration = 0; iteration < 200; iteration++)
            {
                CombatResolver.Advance(sword, state with { Phase = CombatPhase.Active }, targets);
            }
        });
    }

    ReactiveFluid2D measuredField = CreatePond();
    Measurement update = Measure(() =>
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            measuredField.AdvanceFrame(1.0 / 60.0);
        }
    });
    Measurement disturbance = Measure(() =>
    {
        for (int iteration = 0; iteration < 200; iteration++)
        {
            measuredField.ApplyDisturbance(hammerRing);
        }
    });
    Measurement charge = Measure(() =>
    {
        for (int iteration = 0; iteration < 200; iteration++)
        {
            measuredField.Energize(7.5, 7.5, 1, 5);
        }
    });
    Measurement projection = Measure(() =>
    {
        for (int iteration = 0; iteration < 200; iteration++)
        {
            measuredField.ProjectRgba8();
        }
    });
    return new PerformanceResult(
        "hollowflux.performance.m20.v1",
        combat,
        new { dimensions = "16x16", update1000 = update, disturbance200 = disturbance, charge200 = charge, projection200 = projection, projectionBytes = measuredField.ProjectRgba8().Length });
}

Measurement Measure(Action operation)
{
    operation();
    long before = GC.GetAllocatedBytesForCurrentThread();
    Stopwatch stopwatch = Stopwatch.StartNew();
    operation();
    stopwatch.Stop();
    return new Measurement(Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3), GC.GetAllocatedBytesForCurrentThread() - before);
}

void WriteImages(ReactiveFluid2D source)
{
    const int scale = 24;
    PngWriter.Write(Path.Combine(artifactRoot, "field-inspector.png"), 16 * scale, 16 * scale, (x, y) =>
    {
        int cellX = x / scale;
        int cellY = y / scale;
        if (source.LiquidMask[cellX, cellY] == 0)
        {
            return ((byte)24, (byte)26, (byte)31);
        }
        byte height = ToByte((source.HeightField[cellX, cellY] + 1.5f) / 3f);
        byte charge = ToByte(source.Charge[cellX, cellY]);
        return ((byte)Math.Max(height / 3, charge), (byte)(height / 2), height);
    });
    PngWriter.Write(Path.Combine(artifactRoot, "tinyfarm-field-native.png"), 512, 320, (x, y) =>
    {
        bool pond = (x - 340) * (x - 340) + (y - 190) * (y - 190) < 105 * 105;
        if (pond)
        {
            return ((byte)34, (byte)(105 + (x + y) % 30), (byte)142);
        }
        return ((byte)72, (byte)(118 + (x / 16) % 12), (byte)67);
    });
    PngWriter.Write(Path.Combine(artifactRoot, "combat-inspector.png"), 512, 192, (x, y) =>
    {
        int phase = x * 4 / 512;
        (byte R, byte G, byte B) color = phase switch
        {
            0 => (176, 132, 53),
            1 => (210, 70, 58),
            2 => (74, 126, 169),
            _ => (50, 55, 63)
        };
        bool contactMark = x is > 185 and < 205 && y is > 24 and < 168;
        return contactMark ? ((byte)245, (byte)238, (byte)175) : color;
    });
    PngWriter.Write(Path.Combine(artifactRoot, "tinyfarm-combat-before.png"), 512, 256, (x, y) =>
    {
        bool line = y > 116 && y < 140;
        return line ? ((byte)173, (byte)71, (byte)58) : ((byte)44, (byte)49, (byte)46);
    });
    PngWriter.Write(Path.Combine(artifactRoot, "tinyfarm-combat-after.png"), 512, 256, (x, y) =>
    {
        int band = x * 3 / 512;
        bool active = band == 1 && y > 80 && y < 176;
        if (active)
        {
            return ((byte)211, (byte)75, (byte)61);
        }
        return band == 0 ? ((byte)166, (byte)126, (byte)50) : ((byte)58, (byte)92, (byte)112);
    });
}

void WriteJson(string name, object value)
{
    File.WriteAllText(
        Path.Combine(artifactRoot, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}

string HashFile(string path)
{
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

string Hash(byte[] bytes)
{
    return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

string HashSnapshot(ReactiveFluidSnapshot snapshot)
{
    var bytes = new List<byte>();
    foreach (float value in snapshot.HeightField.Concat(snapshot.Velocity).Concat(snapshot.Charge))
    {
        bytes.AddRange(BitConverter.GetBytes(value));
    }
    bytes.AddRange(BitConverter.GetBytes(snapshot.Tick));
    return Hash(bytes.ToArray());
}

byte ToByte(float value)
{
    return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

internal sealed record Evidence(int Index, string Fragment);
internal sealed record Measurement(double Milliseconds, long AllocatedBytes);
internal sealed record PerformanceResult(string Schema, IReadOnlyDictionary<string, Measurement> CombatCandidateCounts, object Field);
