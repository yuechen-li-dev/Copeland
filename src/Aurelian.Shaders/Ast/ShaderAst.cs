using Aurelian.Shaders.Diagnostics;
using Aurelian.Shaders.Lexing;

namespace Aurelian.Shaders.Ast;

public sealed record HlslDocument(string Source, IReadOnlyList<HlslFunction> Functions);
public sealed record HlslFunction(string Signature, string Body);

public sealed record SdslDocument(
    IReadOnlyList<SdslShader> Shaders,
    IReadOnlyList<SdslEffectBlock> EffectBlocks,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed record ShaderGenericParameter(string TypeText, string Name, SourceSpan Span);

public sealed record SdslShader(
    string Name,
    string? GenericParametersText,
    IReadOnlyList<ShaderGenericParameter> GenericParameters,
    IReadOnlyList<string> BaseShaders,
    IReadOnlyList<SdslStream> Streams,
    IReadOnlyList<SdslStageMethod> Methods,
    IReadOnlyList<string> Modifiers);


public sealed record SdslEffectBlock(
    string? NamespaceName,
    string EffectName,
    IReadOnlyList<string> UsingParams,
    IReadOnlyList<string> Mixins,
    string RawBodyText,
    SourceSpan BodySpan,
    SourceSpan Span);
public sealed record SdslStream(string Type, string Name, string Semantic, SourceSpan Span);

public sealed record SdslStageMethod(
    string ReturnType,
    string Name,
    string Parameters,
    string Body,
    IReadOnlyList<BaseCall> BaseCalls,
    IReadOnlyList<string> Modifiers,
    SourceSpan Span);

public sealed record BaseCall(
    string MethodName,
    string ArgumentText,
    int ArgumentCount,
    SourceSpan Span);
