using System.Runtime.InteropServices;

namespace Aurelian.Shaders.Language.External.Dxc;

public static class DxcExecutableResolver
{
    private const string PackageId = "microsoft.direct3d.dxc";

    public static DxcExecutableResolution Resolve()
    {
        var diagnostics = new List<DxcToolDiagnostic>();

        var configured = Environment.GetEnvironmentVariable("AURELIAN_DXC");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredPath = SafeFullPath(configured);
            if (configuredPath is not null && IsFile(configuredPath))
            {
                return Available(configuredPath, diagnostics);
            }

            diagnostics.Add(new DxcToolDiagnostic(
                DxcToolDiagnosticCodes.EnvironmentPathInvalid,
                "AURELIAN_DXC is set but does not point to an existing file.",
                configured));
        }

        var packaged = ResolvePackagedDxc(diagnostics);
        if (packaged is not null)
        {
            return Available(packaged, diagnostics);
        }

        var pathDxc = ResolvePathDxc(diagnostics);
        if (pathDxc is not null)
        {
            return Available(pathDxc, diagnostics);
        }

        diagnostics.Add(new DxcToolDiagnostic(
            DxcToolDiagnosticCodes.DxcNotFound,
            "DXC was not found via AURELIAN_DXC, Microsoft.Direct3D.DXC package content, or PATH."));

        return new DxcExecutableResolution(DxcToolStatus.Unavailable, null, diagnostics);
    }

    private static DxcExecutableResolution Available(string path, IReadOnlyList<DxcToolDiagnostic> diagnostics) =>
        new(DxcToolStatus.Available, path, diagnostics);

    private static string? ResolvePackagedDxc(List<DxcToolDiagnostic> diagnostics)
    {
        foreach (var packageRoot in GetNuGetPackageRoots())
        {
            try
            {
                var packageDirectory = Path.Combine(packageRoot, PackageId);
                if (!Directory.Exists(packageDirectory))
                {
                    continue;
                }

                foreach (var versionDirectory in Directory.EnumerateDirectories(packageDirectory).OrderDescending(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var candidate in GetPackagedCandidates(versionDirectory))
                    {
                        if (IsFile(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                diagnostics.Add(new DxcToolDiagnostic(
                    DxcToolDiagnosticCodes.PackageProbeFailed,
                    $"DXC package probe failed: {ex.Message}",
                    packageRoot));
            }
        }

        diagnostics.Add(new DxcToolDiagnostic(
            DxcToolDiagnosticCodes.PackageProbeFailed,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Microsoft.Direct3D.DXC package probing did not find dxc.exe for the current architecture."
                : "Microsoft.Direct3D.DXC package probing did not find a native dxc executable for this non-Windows platform."));

        return null;
    }

    private static IEnumerable<string> GetPackagedCandidates(string versionDirectory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        if (arch is not null)
        {
            yield return Path.Combine(versionDirectory, "build", "native", "bin", arch, "dxc.exe");
        }

        foreach (var fallbackArch in new[] { "x64", "arm64", "x86" })
        {
            if (!string.Equals(fallbackArch, arch, StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(versionDirectory, "build", "native", "bin", fallbackArch, "dxc.exe");
            }
        }
    }

    private static string? ResolvePathDxc(List<DxcToolDiagnostic> diagnostics)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            diagnostics.Add(new DxcToolDiagnostic(
                DxcToolDiagnosticCodes.PathProbeFailed,
                "PATH is empty or unset."));
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                foreach (var fileName in GetPathExecutableNames())
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (IsFile(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new DxcToolDiagnostic(
                    DxcToolDiagnosticCodes.PathProbeFailed,
                    $"DXC PATH probe skipped an invalid entry: {ex.Message}",
                    directory));
            }
        }

        return null;
    }

    private static IEnumerable<string> GetPathExecutableNames()
    {
        yield return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dxc.exe" : "dxc";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return "dxc";
        }
    }

    private static IEnumerable<string> GetNuGetPackageRoots()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".nuget", "packages");
        }
    }

    private static bool IsFile(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
