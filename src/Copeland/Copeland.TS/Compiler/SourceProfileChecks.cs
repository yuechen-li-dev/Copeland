using Copeland.TS.Diagnostics;
using Copeland.TS.Workspace;

namespace Copeland.TS.Compiler;

internal static class SourceProfileChecks
{
    public static IReadOnlyList<Diagnostic> Validate(string source, string? path, CopelandBackendTarget? target)
    {
        SourceFileProfile profile = SourceFileProfiles.Read(source);
        if (target is null || profile.Diagnostics.Count > 0)
        {
            return [];
        }
        SourceProfileDiagnostic? diagnostic = profile.CheckTarget(target.BackendId, target.RuntimeId);
        if (diagnostic is null)
        {
            return [];
        }
        return [new Diagnostic(diagnostic.Code, diagnostic.Message, diagnostic.Position, diagnostic.Length, path)];
    }
}
