using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Tokens;

namespace Aurelian.Shaders.Language.Lexing;

public sealed record SdslvLexResult(
    IReadOnlyList<SdslvToken> Tokens,
    IReadOnlyList<SdslvDiagnostic> Diagnostics);
