using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Actuation.World;
using Aurelian.Actuation.World.Requests;
using Aurelian.Rendering.Contracts;
using Aurelian.Rendering.Contracts.CommandPlans;
using Aurelian.Rendering.Contracts.Snapshots;
using Aurelian.Rendering.Null;
using Aurelian.Runtime.Rendering;
using Aurelian.Runtime.Sessions;
using Aurelian.World.Stores;
using Aurelian.World.Units;

namespace Aurelian.Cli;

public sealed record AurelianAgentEntity(
    ulong Id,
    string Kind,
    string? Name,
    double X,
    double Y,
    double RotationRadians,
    double ScaleX,
    double ScaleY,
    string? Mesh,
    string? Material,
    bool Visible,
    int SortOrder,
    ulong? ParentId = null,
    string? Slot = null);

public sealed record AurelianAgentSessionDocument(
    string Schema,
    string SessionId,
    ulong TickIndex,
    IReadOnlyList<AurelianAgentEntity> Entities)
{
    public const string CurrentSchema = "aurelian.agent-session.v1";

    public static AurelianAgentSessionDocument Create(string? sessionId = null)
    {
        return new AurelianAgentSessionDocument(
            CurrentSchema,
            sessionId ?? Guid.NewGuid().ToString("N"),
            0,
            [new AurelianAgentEntity(1, "root", "Root", 0, 0, 0, 1, 1, null, null, true, 0)]);
    }
}

public sealed record AurelianAgentDiagnostic(string Code, string Severity, string Message);

public sealed record AurelianAgentResult<T>(
    bool Succeeded,
    T? Value,
    IReadOnlyList<AurelianAgentDiagnostic> Diagnostics);

public sealed record AurelianDrawDescription(
    ulong FrameId,
    int PassCount,
    int DrawCount,
    NullRenderTrace Trace,
    string FrameHash);

public sealed record AurelianTickSummary(
    string SessionId,
    ulong TickIndex,
    int FramesTicked,
    string StateHash,
    AurelianDrawDescription Draw,
    IReadOnlyList<AurelianAgentDiagnostic> Diagnostics);

public sealed record AurelianReplayEnvelope(
    string Schema,
    AurelianAgentSessionDocument Session,
    string ExpectedStateHash,
    string ExpectedFrameHash)
{
    public const string CurrentSchema = "aurelian.replay.v1";
}

public sealed class AurelianAgentSessionStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AurelianAgentResult<AurelianAgentSessionDocument> Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(fullPath))
            {
                return Failure<AurelianAgentSessionDocument>(
                    "AURELIAN-CLI-SESSION-NOT-FOUND",
                    $"Session document '{fullPath}' was not found.");
            }

            AurelianAgentSessionDocument? document = JsonSerializer.Deserialize<AurelianAgentSessionDocument>(
                File.ReadAllText(fullPath),
                JsonOptions);
            if (document is null || document.Schema != AurelianAgentSessionDocument.CurrentSchema)
            {
                return Failure<AurelianAgentSessionDocument>(
                    "AURELIAN-CLI-SESSION-SCHEMA",
                    "The session document schema is missing or unsupported.");
            }

            return new(true, document, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failure<AurelianAgentSessionDocument>(
                "AURELIAN-CLI-SESSION-READ",
                $"Session document could not be read: {exception.Message}");
        }
    }

    public AurelianAgentResult<AurelianAgentSessionDocument> Save(
        string path,
        AurelianAgentSessionDocument document)
    {
        string fullPath = Path.GetFullPath(path);
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, overwrite: true);
            return new(true, document, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            return Failure<AurelianAgentSessionDocument>(
                "AURELIAN-CLI-SESSION-WRITE",
                $"Session document could not be committed: {exception.Message}");
        }
    }

    internal static AurelianAgentResult<T> Failure<T>(string code, string message)
    {
        return new(false, default, [new AurelianAgentDiagnostic(code, "error", message)]);
    }
}

public sealed class AurelianAgentSessionService
{
    private readonly AurelianAgentSessionStore _store = new();

    public AurelianAgentResult<AurelianAgentSessionDocument> Start(string path, string? sessionId)
    {
        if (File.Exists(Path.GetFullPath(path)))
        {
            return AurelianAgentSessionStore.Failure<AurelianAgentSessionDocument>(
                "AURELIAN-CLI-SESSION-EXISTS",
                "Refusing to overwrite an existing session document.");
        }

        return _store.Save(path, AurelianAgentSessionDocument.Create(sessionId));
    }

    public AurelianAgentResult<AurelianAgentSessionDocument> Act(string path, JsonElement request)
    {
        AurelianAgentResult<AurelianAgentSessionDocument> loaded = _store.Load(path);
        if (!loaded.Succeeded || loaded.Value is null)
        {
            return loaded;
        }

        if (!request.TryGetProperty("kind", out JsonElement kindElement))
        {
            return AurelianAgentSessionStore.Failure<AurelianAgentSessionDocument>(
                "AURELIAN-CLI-ACT-KIND",
                "Actuation JSON requires a 'kind'.");
        }

        string? kind = kindElement.GetString();
        if (kind != "SpawnUnitRequest")
        {
            return AurelianAgentSessionStore.Failure<AurelianAgentSessionDocument>(
                "AURELIAN-CLI-ACT-UNSUPPORTED",
                $"Actuation kind '{kind}' is not supported by the bounded agent surface.");
        }

        try
        {
            JsonElement unitJson = request.GetProperty("unit");
            AurelianAgentEntity entity = new(
                unitJson.GetProperty("id").GetUInt64(),
                unitJson.GetProperty("kind").GetString()!,
                unitJson.TryGetProperty("name", out JsonElement name) ? name.GetString() : null,
                Number(unitJson, "x", 0),
                Number(unitJson, "y", 0),
                Number(unitJson, "rotationRadians", 0),
                Number(unitJson, "scaleX", 1),
                Number(unitJson, "scaleY", 1),
                unitJson.TryGetProperty("mesh", out JsonElement mesh) ? mesh.GetString() : null,
                unitJson.TryGetProperty("material", out JsonElement material) ? material.GetString() : null,
                !unitJson.TryGetProperty("visible", out JsonElement visible) || visible.GetBoolean(),
                unitJson.TryGetProperty("sortOrder", out JsonElement sort) ? sort.GetInt32() : 0,
                unitJson.TryGetProperty("parentId", out JsonElement parent) ? parent.GetUInt64() : 1,
                unitJson.TryGetProperty("slot", out JsonElement slot) ? slot.GetString() : null);

            WorldDataDocument document = Hydrate(loaded.Value);
            var spawnRequest = new SpawnUnitRequest(new WorldUnitDescriptor(
                new UnitId(entity.Id),
                new UnitKindId(entity.Kind),
                UnitComposition.Empty,
                Name: entity.Name));
            WorldActuationResult spawned = WorldUnitActuator.Apply(document.World, spawnRequest);
            if (!spawned.Success)
            {
                return ActuationFailure(spawned.Diagnostics);
            }

            document = document.WithWorld(spawned.Document);
            WorldActuationResult attached = WorldUnitActuator.Apply(
                document.World,
                new AttachChildRequest(
                    new UnitId(entity.ParentId ?? document.World.RootId.Value),
                    new UnitChild(new UnitId(entity.Id), entity.Slot)));
            if (!attached.Success)
            {
                return ActuationFailure(attached.Diagnostics);
            }
            document = document.WithWorld(attached.Document);
            WorldActuationResult<WorldDataDocument> transformed = WorldDataActuator.Apply(
                document,
                new SetUnitTransform2Request(
                    new UnitId(entity.Id),
                    new Transform2(entity.X, entity.Y, entity.RotationRadians, entity.ScaleX, entity.ScaleY)));
            if (!transformed.Success)
            {
                return ActuationFailure(transformed.Diagnostics);
            }

            document = transformed.Document;
            if (!string.IsNullOrWhiteSpace(entity.Name))
            {
                WorldActuationResult<WorldDataDocument> named = WorldDataActuator.Apply(
                    document,
                    new SetUnitNameRequest(new UnitId(entity.Id), new UnitName(entity.Name)));
                if (!named.Success)
                {
                    return ActuationFailure(named.Diagnostics);
                }
                document = named.Document;
            }

            if (!string.IsNullOrWhiteSpace(entity.Mesh) && !string.IsNullOrWhiteSpace(entity.Material))
            {
                WorldActuationResult<WorldDataDocument> rendered = WorldDataActuator.Apply(
                    document,
                    new SetRenderable2DRequest(
                        new UnitId(entity.Id),
                        new Renderable2DData(
                            new WorldMeshRef(entity.Mesh),
                            new WorldMaterialRef(entity.Material),
                            entity.Visible,
                            entity.SortOrder)));
                if (!rendered.Success)
                {
                    return ActuationFailure(rendered.Diagnostics);
                }
                document = rendered.Document;
            }

            AurelianAgentSessionDocument next = Dehydrate(loaded.Value, document);
            return _store.Save(path, next);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return AurelianAgentSessionStore.Failure<AurelianAgentSessionDocument>(
                "AURELIAN-CLI-ACT-INVALID",
                $"SpawnUnitRequest JSON is invalid: {exception.Message}");
        }
    }

    public async Task<AurelianAgentResult<AurelianTickSummary>> TickAsync(
        string path,
        int frameCount,
        string? traceOut,
        CancellationToken cancellationToken)
    {
        if (frameCount is < 1 or > 1_000_000)
        {
            return AurelianAgentSessionStore.Failure<AurelianTickSummary>(
                "AURELIAN-CLI-TICK-COUNT",
                "Frame count must be between 1 and 1,000,000.");
        }

        AurelianAgentResult<AurelianAgentSessionDocument> loaded = _store.Load(path);
        if (!loaded.Succeeded || loaded.Value is null)
        {
            return new(false, null, loaded.Diagnostics);
        }

        AurelianAgentSessionDocument current = loaded.Value;
        var runtime = new AurelianRuntimeSession();
        AurelianRuntimeResult started = runtime.Start();
        if (!started.Success)
        {
            return new(false, null, started.Diagnostics.Select(ToDiagnostic).ToArray());
        }

        var diagnostics = new List<AurelianAgentDiagnostic>();
        for (int index = 0; index < frameCount; index++)
        {
            ulong tickIndex = current.TickIndex + (ulong)index + 1;
            AurelianRuntimeTickResult tick = await runtime.TickAsync(
                new AurelianRuntimeTickInput(tickIndex, TimeSpan.FromSeconds(1d / 60d)),
                cancellationToken);
            diagnostics.AddRange(tick.Diagnostics.Select(ToDiagnostic));
            if (!tick.Success)
            {
                return new(false, null, diagnostics);
            }
        }
        runtime.Stop();

        AurelianAgentSessionDocument next = current with { TickIndex = current.TickIndex + (ulong)frameCount };
        AurelianAgentResult<AurelianAgentSessionDocument> saved = _store.Save(path, next);
        if (!saved.Succeeded)
        {
            return new(false, null, saved.Diagnostics);
        }

        AurelianDrawDescription draw = Describe(next);
        string stateHash = Hash(next);
        var summary = new AurelianTickSummary(
            next.SessionId,
            next.TickIndex,
            frameCount,
            stateHash,
            draw,
            diagnostics);
        if (!string.IsNullOrWhiteSpace(traceOut))
        {
            AurelianReplayEnvelope envelope = new(
                AurelianReplayEnvelope.CurrentSchema,
                next,
                stateHash,
                draw.FrameHash);
            WriteAtomic(traceOut, JsonSerializer.Serialize(envelope, AurelianAgentSessionStore.JsonOptions));
        }
        return new(true, summary, diagnostics);
    }

    public AurelianAgentResult<AurelianAgentEntity> Query(string path, ulong entityId)
    {
        AurelianAgentResult<AurelianAgentSessionDocument> loaded = _store.Load(path);
        if (!loaded.Succeeded || loaded.Value is null)
        {
            return new(false, null, loaded.Diagnostics);
        }
        AurelianAgentEntity? entity = loaded.Value.Entities.FirstOrDefault(candidate => candidate.Id == entityId);
        return entity is null
            ? AurelianAgentSessionStore.Failure<AurelianAgentEntity>(
                "AURELIAN-CLI-ENTITY-NOT-FOUND",
                $"Entity '{entityId}' was not found.")
            : new(true, entity, []);
    }

    public AurelianAgentResult<AurelianDrawDescription> Describe(string path)
    {
        AurelianAgentResult<AurelianAgentSessionDocument> loaded = _store.Load(path);
        return loaded.Succeeded && loaded.Value is not null
            ? new(true, Describe(loaded.Value), [])
            : new(false, null, loaded.Diagnostics);
    }

    public AurelianAgentResult<object> Replay(string path, bool assertIdentical)
    {
        try
        {
            AurelianReplayEnvelope? envelope = JsonSerializer.Deserialize<AurelianReplayEnvelope>(
                File.ReadAllText(Path.GetFullPath(path)),
                AurelianAgentSessionStore.JsonOptions);
            if (envelope is null || envelope.Schema != AurelianReplayEnvelope.CurrentSchema)
            {
                return AurelianAgentSessionStore.Failure<object>(
                    "AURELIAN-CLI-REPLAY-SCHEMA",
                    "The replay envelope schema is missing or unsupported.");
            }
            string actualStateHash = Hash(envelope.Session);
            string actualFrameHash = Describe(envelope.Session).FrameHash;
            bool identical = actualStateHash == envelope.ExpectedStateHash &&
                actualFrameHash == envelope.ExpectedFrameHash;
            object value = new
            {
                identical,
                asserted = assertIdentical,
                expectedStateHash = envelope.ExpectedStateHash,
                actualStateHash,
                expectedFrameHash = envelope.ExpectedFrameHash,
                actualFrameHash,
            };
            return new(!assertIdentical || identical, value, identical
                ? []
                : [new AurelianAgentDiagnostic(
                    "AURELIAN-CLI-REPLAY-DIVERGED",
                    "error",
                    "Replay state or frame identity diverged.")]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AurelianAgentSessionStore.Failure<object>(
                "AURELIAN-CLI-REPLAY-READ",
                $"Replay envelope could not be read: {exception.Message}");
        }
    }

    private static AurelianDrawDescription Describe(AurelianAgentSessionDocument session)
    {
        WorldDataDocument document = Hydrate(session);
        RenderSnapshotResult snapshot = WorldRenderSnapshotExtractor.Extract(
            document,
            new WorldRenderSnapshotOptions(new RenderFrameId(session.TickIndex)));
        RenderCommandPlan plan = RenderCommandPlanBuilder.FromSnapshot(
            snapshot.Snapshot,
            new RenderPipelineRef("agent-null-2d"),
            new RenderShaderRef("agent-null-shader"),
            new RenderTargetRef("agent-null-target"));
        NullRenderResult rendered = new NullRenderer().Render(plan);
        string canonical = JsonSerializer.Serialize(rendered.Trace, AurelianAgentSessionStore.JsonOptions);
        return new AurelianDrawDescription(
            session.TickIndex,
            rendered.Trace.Passes.Count,
            rendered.Trace.Passes.Sum(pass => pass.Draws.Count),
            rendered.Trace,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    private static WorldDataDocument Hydrate(AurelianAgentSessionDocument session)
    {
        Dictionary<UnitId, WorldUnitDescriptor> units = session.Entities.ToDictionary(
            entity => new UnitId(entity.Id),
            entity => new WorldUnitDescriptor(
                new UnitId(entity.Id),
                new UnitKindId(entity.Kind),
                new UnitComposition(session.Entities
                    .Where(child => child.ParentId == entity.Id)
                    .OrderBy(child => child.Id)
                    .Select(child => new UnitChild(new UnitId(child.Id), child.Slot))
                    .ToArray()),
                Name: entity.Name));
        WorldDataDocument document = WorldDataDocument.FromWorld(new WorldDocument(new UnitId(1), units));
        foreach (AurelianAgentEntity entity in session.Entities)
        {
            UnitId id = new(entity.Id);
            document = document.WithTransforms(document.Transforms.Set(
                id,
                new Transform2(entity.X, entity.Y, entity.RotationRadians, entity.ScaleX, entity.ScaleY)));
            if (!string.IsNullOrWhiteSpace(entity.Name))
            {
                document = document.WithNames(document.Names.Set(id, new UnitName(entity.Name)));
            }
            if (!string.IsNullOrWhiteSpace(entity.Mesh) && !string.IsNullOrWhiteSpace(entity.Material))
            {
                document = document.WithRenderables(document.Renderables.Set(
                    id,
                    new Renderable2DData(
                        new WorldMeshRef(entity.Mesh),
                        new WorldMaterialRef(entity.Material),
                        entity.Visible,
                        entity.SortOrder)));
            }
        }
        return document;
    }

    private static AurelianAgentSessionDocument Dehydrate(
        AurelianAgentSessionDocument session,
        WorldDataDocument document)
    {
        AurelianAgentEntity[] entities = document.World.Units.Values
            .OrderBy(unit => unit.Id.Value)
            .Select(unit =>
            {
                (UnitId ParentId, string? Slot)? parent = document.World.Units.Values
                    .SelectMany(candidate => candidate.Composition.Children.Select(child =>
                        (candidate.Id, child.UnitId, child.Slot)))
                    .Where(candidate => candidate.UnitId == unit.Id)
                    .Select(candidate => ((UnitId ParentId, string? Slot)?)(candidate.Id, candidate.Slot))
                    .FirstOrDefault();
                document.Transforms.Transforms.TryGetValue(unit.Id, out Transform2 transform);
                document.Names.Names.TryGetValue(unit.Id, out UnitName? name);
                document.Renderables.Renderables.TryGetValue(unit.Id, out Renderable2DData? renderable);
                return new AurelianAgentEntity(
                    unit.Id.Value,
                    unit.Kind.Value,
                    name?.Value ?? unit.Name,
                    transform.X,
                    transform.Y,
                    transform.RotationRadians,
                    transform.ScaleX,
                    transform.ScaleY,
                    renderable?.Mesh.Value,
                    renderable?.Material.Value,
                    renderable?.Visible ?? false,
                    renderable?.SortOrder ?? 0,
                    parent?.ParentId.Value,
                    parent?.Slot);
            })
            .ToArray();
        return session with { Entities = entities };
    }

    private static AurelianAgentResult<AurelianAgentSessionDocument> ActuationFailure(
        IReadOnlyList<WorldActuationDiagnostic> diagnostics)
    {
        return new(false, null, diagnostics.Select(diagnostic => new AurelianAgentDiagnostic(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Message)).ToArray());
    }

    private static AurelianAgentDiagnostic ToDiagnostic(AurelianRuntimeDiagnostic diagnostic)
    {
        return new AurelianAgentDiagnostic(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Message);
    }

    private static double Number(JsonElement element, string name, double fallback)
    {
        return element.TryGetProperty(name, out JsonElement value) ? value.GetDouble() : fallback;
    }

    private static string Hash(AurelianAgentSessionDocument document)
    {
        string canonical = JsonSerializer.Serialize(document, AurelianAgentSessionStore.JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void WriteAtomic(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(temporaryPath, content + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
