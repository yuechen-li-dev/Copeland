using System.Text.Json.Serialization;

namespace Aurelian.Runtime.Inspection;

public sealed record InspectedValue(string Key, string Type, string? Value, float? ExpiresAt, bool CheckpointSupported);
public sealed record InspectedAgent(string Id, string KernelId, string[] ActivePath, InspectedValue[] Blackboard);
public sealed record InspectedScore(string Id, float Score, string Target);
public sealed record InspectedDecision(string Phase, string? CurrentId, float CurrentScore,
    string BestId, float BestScore, bool Switched, string Reason, InspectedScore[] Scores);
public sealed record AgentTraceEntry(long Sequence, string Agent, float Time, string Kind,
    string State, string? Destination, string? Reason, InspectedDecision? Decision);
public sealed record InspectedDelta(string Agent, float Time, string Key, string Operation,
    string? OldValue, string? NewValue);
public sealed record AgentInspection(InspectedAgent[] Agents, AgentTraceEntry[] Trace,
    InspectedDelta[] Changes, long DroppedTraceEntries, long DroppedChanges);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AgentInspection))]
public partial class AgentInspectionJsonContext : JsonSerializerContext;

