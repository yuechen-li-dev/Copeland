using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;

namespace Copeland.Cli;

/// <summary>
/// Serializes the browser-safe transport projection of canonical attachment
/// MIR. This is deliberately not a projection of generated JavaScript or of
/// inspection-table text.
/// </summary>
internal static class AttachmentPlanArtifactEmitter
{
    public const int SchemaVersion = 1;
    public const string ArtifactFileName = "attachments.json";

    public static string Emit(CopelandProjectCompilation compilation, string projectRoot)
    {
        AttachmentPlan[] plans = compilation.Modules
            .SelectMany(module => (module.BoundCompilation?.Program.HostAttachments ?? [])
                .Select(attachment => CreatePlan(attachment, module, projectRoot)))
            .OrderBy(plan => plan.AttachmentId, StringComparer.Ordinal)
            .ToArray();

        Validate(plans);
        var artifact = new AttachmentPlanArtifact(
            SchemaVersion,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(projectRoot)),
            plans);
        return JsonSerializer.Serialize(artifact, Options) + "\n";
    }

    public static string Hash(string artifact)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact))).ToLowerInvariant();

    private static AttachmentPlan CreatePlan(
        HostAttachmentMir attachment,
        CopelandProjectModuleCompilation module,
        string projectRoot)
    {
        BoundComponentInstance instance = module.BoundCompilation!.Program.ComponentInstances
            .Single(candidate => candidate.StableIdentity == attachment.ComponentInstanceId);
        string relativePath = Path.GetRelativePath(projectRoot, module.Source.SourcePath).Replace('\\', '/');
        (int line, int column) = LineAndColumn(module.Source.SourceText, instance.ParentBinding.Syntax.LayoutIdentifier.Position);
        return new AttachmentPlan(
            attachment.AttachmentId,
            attachment.ComponentDefinitionId,
            attachment.ComponentInstanceId,
            attachment.ParentComponentInstanceId,
            attachment.HostBoxId,
            HostSelector(attachment.HostBoxId, attachment.Payload?.HostSelectorSuffix),
            attachment.AdapterId.ToString(),
            CapabilityNames(attachment.RequiredHostCapabilities),
            CapabilityNames(attachment.RequiredContentCapabilities),
            attachment.PayloadContract,
            attachment.Payload is null ? null : new AttachmentPayload(attachment.Payload.TagName, attachment.Payload.Label),
            new AttachmentLifecycle(true, true, true),
            new AttachmentSource(relativePath, line, column, attachment.SourceProvenance));
    }

    private static string HostSelector(string hostBoxId, string? suffix)
    {
        int layoutSeparator = hostBoxId.IndexOf('.');
        int boxSeparator = hostBoxId.LastIndexOf('.');
        if (layoutSeparator <= 0 || boxSeparator == hostBoxId.Length - 1)
        {
            throw new InvalidOperationException($"Attachment host identity '{hostBoxId}' cannot be projected to a semantic browser host.");
        }

        // A private component presentation retains its full semantic path for
        // diagnostics, but browser hosts are stamped by the public root
        // layout and final box name. Intermediate component paths are not DOM
        // layout identities and must not leak into the selector.
        string layout = hostBoxId[..layoutSeparator];
        string box = hostBoxId[(boxSeparator + 1)..];
        return $"[data-machina-layout='{layout}'][data-machina-box='{box}']" + (suffix ?? string.Empty);
    }

    private static string[] CapabilityNames<TCapability>(TCapability value) where TCapability : struct, Enum
        => Enum.GetValues<TCapability>()
            .Where(capability => Convert.ToUInt64(capability) != 0
                && (Convert.ToUInt64(value) & Convert.ToUInt64(capability)) == Convert.ToUInt64(capability))
            .Select(capability => capability.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static (int line, int column) LineAndColumn(string text, int position)
    {
        int line = 1;
        int column = 1;
        for (int index = 0; index < Math.Min(position, text.Length); index += 1)
        {
            if (text[index] == '\n')
            {
                line += 1;
                column = 1;
            }
            else
            {
                column += 1;
            }
        }
        return (line, column);
    }

    private static void Validate(IReadOnlyList<AttachmentPlan> plans)
    {
        string? duplicate = plans.GroupBy(plan => plan.AttachmentId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"COPE-ATTACHMENT-PLAN-0002: duplicate emitted attachment ID '{duplicate}'.");
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record AttachmentPlanArtifact(int SchemaVersion, string ProjectId, IReadOnlyList<AttachmentPlan> Plans);
    private sealed record AttachmentPlan(string AttachmentId, string ComponentDefinitionId, string ComponentInstanceId, string? ParentComponentInstanceId, string HostBoxId, string HostSelector, string AdapterId, string[] RequiredHostCapabilities, string[] RequiredContentCapabilities, string PayloadContract, object? Payload, AttachmentLifecycle Lifecycle, AttachmentSource Source);
    private sealed record AttachmentPayload(string TagName, string? Label);
    private sealed record AttachmentLifecycle(bool Mount, bool Update, bool Unmount);
    private sealed record AttachmentSource(string Path, int Line, int Column, string Provenance);
}
