using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aurelian.Graphics.FramePlans;

public sealed record FramePlan(string Format, FrameGraph Graph, ImmutableArray<int> Order,
    ImmutableArray<FrameInstruction> Tape, ImmutableArray<FrameAllocation> Allocations,
    ImmutableArray<FrameBatch> Batches, string Hash)
{
    public const string CurrentFormat = "aurelian.frame-plan/1";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string Describe() => JsonSerializer.Serialize(this, Json) + "\n";

    public string ComputeHash()
    {
        string canonical = JsonSerializer.Serialize(this with { Hash = "" }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static FramePlan Load(string json)
    {
        var plan = JsonSerializer.Deserialize<FramePlan>(json, Json) ?? throw new InvalidDataException("AUR-PLAN-001: Empty plan.");
        FramePlanVerifier.Verify(plan);
        return plan;
    }
}

public sealed class FramePlanException(string code, string message) : InvalidOperationException(code + ": " + message)
{
    public string Code { get; } = code;
}
