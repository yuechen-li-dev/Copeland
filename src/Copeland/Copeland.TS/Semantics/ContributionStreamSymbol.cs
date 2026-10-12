using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public sealed class ContributionStreamSymbol(
    string name,
    ContributionStreamTypeSymbol type,
    FunctionSymbol accessor,
    FunctionSymbol initializer,
    VariableSymbol storage,
    ClassTypeSymbol? classOwner = null,
    bool isPublic = true,
    SyntaxToken? anchor = null) : Symbol(name)
{
    public ContributionStreamTypeSymbol Type { get; } = type;
    public FunctionSymbol Accessor { get; } = accessor;
    public FunctionSymbol Initializer { get; } = initializer;
    public VariableSymbol Storage { get; } = storage;
    public ClassTypeSymbol? ClassOwner { get; } = classOwner;
    public bool IsPublic { get; } = isPublic;
    public SyntaxToken? Anchor { get; } = anchor;
    public IReadOnlyList<BoundRecordFieldInitializer> Contributions { get; internal set; } = [];
}
