using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Manifest;
using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

/// <summary>
/// Compiler-owned metadata for one root default sidecar. It intentionally has
/// no executable command fields: those remain on the manifest RunTarget.
/// </summary>
public sealed record CSharpSidecarContract(
    string LogicalBindingId,
    string ProtocolVersion,
    string ExpectedDigest,
    ManifestRunTarget RunTarget,
    string ProjectRoot);

public static class CSharpSidecarContracts
{
    public const string ProtocolVersion = "copeland-sidecar-m1";

    public static bool TryCreate(
        MirProgram program,
        CopelandManifest manifest,
        out CSharpSidecarContract? contract,
        out CSharpDiagnostic? diagnostic)
    {
        contract = null;
        diagnostic = null;
        ManifestSidecarBinding[] defaults = manifest.Sidecars.Where(binding => binding.IsDefault).ToArray();
        if (defaults.Length != 1)
        {
            diagnostic = new CSharpDiagnostic(
                "COPE-CS-SIDECAR-0001",
                "A compilation using targetless tsonCall requires exactly one root Sidecars default binding.");
            return false;
        }

        ManifestSidecarBinding binding = defaults[0];
        ManifestDeploymentBinding? targetIdentity = manifest.DeploymentBindings
            .SingleOrDefault(target => string.Equals(target.LogicalIdentity, binding.RunTargetIdentity, StringComparison.Ordinal));
        if (targetIdentity is null)
        {
            diagnostic = new CSharpDiagnostic("COPE-CS-SIDECAR-0002", $"Default sidecar '{binding.LogicalBindingId}' has no root RunTarget.");
            return false;
        }

        ManifestRunTarget? runTarget = manifest.Packages
            .SelectMany(package => package.RunTargets)
            .SingleOrDefault(target => string.Equals(target.PackageName, targetIdentity.PackageName, StringComparison.Ordinal)
                && string.Equals(target.Name, targetIdentity.RunTargetName, StringComparison.Ordinal));
        if (runTarget is null)
        {
            diagnostic = new CSharpDiagnostic("COPE-CS-SIDECAR-0002", $"Default sidecar '{binding.LogicalBindingId}' references a non-root RunTarget.");
            return false;
        }

        contract = new CSharpSidecarContract(
            binding.LogicalBindingId,
            ProtocolVersion,
            ComputeDigest(program),
            runTarget,
            Path.GetFullPath(manifest.ProjectRoot));
        return true;
    }

    internal static string ComputeDigest(MirProgram program)
    {
        var contract = new StringBuilder();
        contract.Append(ProtocolVersion).Append('\n');
        foreach (MirTsonEncodingPlan plan in program.TsonEncodingPlans.OrderBy(plan => plan.Id.Value, StringComparer.Ordinal))
        {
            contract.Append(plan.Id.Value).Append('\n');
            contract.Append(MirTsonCanonicalText.BuildDocumentPrefix(plan));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract.ToString())));
    }
}
