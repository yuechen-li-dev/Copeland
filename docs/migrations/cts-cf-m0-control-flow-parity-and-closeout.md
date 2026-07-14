# CTS-CF-M0 control-flow parity and closeout

CTS-CF-M0 closes the existing statement-control-flow gap. Pre-change, syntax/binding/lowering already represented `if`, `while`, and C-style `for`, but both C# and JavaScript backends lacked loop realization, and `break`/`continue` were keywords without language nodes.

The implementation adds dedicated transfer syntax, bound nodes, MIR nodes, lexical loop-depth diagnostics (`COPE-CFLOW-0001` and `COPE-CFLOW-0002`), shared malformed-MIR rejection, structured C#/JavaScript emission, and filesystem language fixtures. Existing Diagnostic JavaScript corpus artifacts are unchanged.

## Validator portability closeout

The initial CTS-CF-M0 validation could not run the topology and dependency-boundary validators under Windows PowerShell because both scripts called the .NET `String.Contains(string, StringComparison)` overload. That overload is not available to Windows PowerShell 5.1's .NET Framework string implementation. Every ordinal and ordinal-ignore-case string containment check in those two scripts now uses the equivalent `IndexOf(needle, comparison) -ge 0` form; collection-membership `Contains` calls remain unchanged. The validators therefore support the repository's practical baseline of Windows PowerShell 5.1 and PowerShell 7+ without a compatibility framework.

This repair was executed under Windows PowerShell 5.1.26100.8737 and PowerShell 7.6.3. Both `Validate-CopelandTsTopology.ps1` and `Validate-DependencyBoundaries.ps1` passed under both hosts. No compiler, MIR, backend, control-flow semantic, fixture, or generated-artifact behavior changed.

The canonical law is recorded in [the language profile](../Copeland/language/copeland-ts-language-profile.md) and the implementation record is [CTS-CF-M0](../Copeland/architecture/copeland-ts-foundational-control-flow-cts-cf-m0.md).
