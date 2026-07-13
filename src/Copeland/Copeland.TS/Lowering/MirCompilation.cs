using Copeland.TS.Diagnostics;
using Copeland.TS.Mir;

namespace Copeland.TS.Lowering;

public sealed class MirCompilation(MirProgram? program, IReadOnlyList<Diagnostic> diagnostics)
{
    public MirProgram? Program { get; } = program;

    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}
