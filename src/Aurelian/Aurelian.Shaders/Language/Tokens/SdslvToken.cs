using Aurelian.Shaders.Language.Ast;

namespace Aurelian.Shaders.Language.Tokens;

public sealed record SdslvToken(SdslvTokenKind Kind, string Text, SdslvSpan Span);
