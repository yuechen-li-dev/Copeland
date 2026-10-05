from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:60]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
b='src/Copeland/Copeland.TS/Semantics/Binder.cs'
edit(b,'                        : BindStructuralProjection(generic, anchor, missingId, missingPrefix),','                        : BindGenericClrOrStructuralType(generic, anchor, missingId, missingPrefix),')
edit(b,'        private BoundExpression BindGenericCall(GenericCallExpressionSyntax call, TypeSymbol? contextualType)\n        {','''        private BoundExpression BindGenericCall(GenericCallExpressionSyntax call, TypeSymbol? contextualType)
        {
            if (call.Target is NameExpressionSyntax clrName && IsImportedGenericClrName(clrName.IdentifierToken.Text))
            {
                Type? closed = CloseGenericClrType(clrName.IdentifierToken, call.TypeArguments);
                if (closed is null) return new BoundErrorExpression();
                return BindClrInvocation(call.Arguments, call.OpenParenToken,
                    closed.GetConstructors().Where(_clrResolver.IsMemberVisible), null, closed.FullName ?? closed.Name);
            }
''')
insert='''        private bool IsImportedGenericClrName(string name)
            => _clrImportedTypes.Keys.Any(key => key.StartsWith(name + "`", StringComparison.Ordinal));

        private TypeSymbol BindGenericClrOrStructuralType(GenericTypeSyntax syntax, SyntaxToken anchor, string missingId, string missingPrefix)
        {
            if (!IsImportedGenericClrName(syntax.Identifier.Text)) return BindStructuralProjection(syntax, anchor, missingId, missingPrefix);
            Type? closed = CloseGenericClrType(syntax.Identifier, syntax.TypeArguments);
            return closed is null ? PrimitiveTypeSymbol.Error : new ClrTypeSymbol(closed);
        }

        private Type? CloseGenericClrType(SyntaxToken name, IReadOnlyList<TypeSyntax> typeArguments)
        {
            if (!_clrImportedTypes.TryGetValue(name.Text + "`" + typeArguments.Count, out List<Type>? candidates))
            {
                Report("COPE-CLR-GENERIC-0001", $"CLR generic type '{name.Text}' does not accept {typeArguments.Count} type arguments.", name);
                return null;
            }
            if (candidates.Count != 1)
            {
                Report("COPE-CLR-0002", $"CLR generic type '{name.Text}' is ambiguous across imports.", name);
                return null;
            }
            var runtimeArguments = new List<Type>();
            foreach (TypeSyntax argumentSyntax in typeArguments)
            {
                TypeSymbol argument = BindType(argumentSyntax, name, "COPE-CLR-GENERIC-0002", "CLR type argument");
                if (argument == PrimitiveTypeSymbol.Error) return null;
                Type? runtime = argument switch
                {
                    ClrTypeSymbol clr => clr.RuntimeType,
                    PrimitiveTypeSymbol primitive when TypeFacts.IsInt(primitive) => typeof(int),
                    PrimitiveTypeSymbol primitive when TypeFacts.IsFloat(primitive) => typeof(double),
                    PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.String => typeof(string),
                    PrimitiveTypeSymbol primitive when primitive == PrimitiveTypeSymbol.Boolean => typeof(bool),
                    _ => null,
                };
                if (runtime is null)
                {
                    Report("COPE-CLR-GENERIC-0002", $"'{argument.Name}' has no admitted CLR generic type-argument mapping. Use int, float, string, boolean, or an imported CLR type.", name);
                    return null;
                }
                runtimeArguments.Add(runtime);
            }
            Type definition = candidates[0];
            if (definition.IsNested)
            {
                Report("COPE-CLR-GENERIC-0003", "Nested CLR generic owners are not yet supported. Use a non-nested imported CLR collection type.", name);
                return null;
            }
            try
            {
                Type closed = definition.MakeGenericType(runtimeArguments.ToArray());
                if (!_clrResolver.IsTypeVisible(closed))
                {
                    Report("COPE-CLR-0004", $"CLR generic type '{name.Text}' is inaccessible.", name);
                    return null;
                }
                return closed;
            }
            catch (ArgumentException)
            {
                Report("COPE-CLR-GENERIC-0002", $"Type arguments do not satisfy the CLR constraints of '{name.Text}'. Use arguments satisfying its declared CLR constraints.", name);
                return null;
            }
        }

'''
edit(b,'        private TypeSymbol BindMutableArrayType(',insert+'        private TypeSymbol BindMutableArrayType(')
n='src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs';p='src/Copeland/Copeland.TS/Syntax/Parser.cs'
edit(n,'    public override SyntaxKind Kind => SyntaxKind.NewExpression;','    public IReadOnlyList<TypeSyntax> TypeArguments { get; init; } = [];\n    public override SyntaxKind Kind => SyntaxKind.NewExpression;')
edit(p,'        CallExpressionSyntax call = ParseCallExpression(target);\n        return new NewExpressionSyntax(','        IReadOnlyList<TypeSyntax> typeArguments = [];\n        CallExpressionSyntax call;\n        if (Current.Kind == SyntaxKind.LessToken)\n        {\n            var generic = (GenericCallExpressionSyntax)ParseGenericFunctionExpression(target);\n            typeArguments = generic.TypeArguments;\n            call = new CallExpressionSyntax(target, generic.OpenParenToken, generic.Arguments, generic.CommaTokens, generic.CloseParenToken);\n        }\n        else\n        {\n            call = ParseCallExpression(target);\n        }\n        return new NewExpressionSyntax(')
edit(p,'            call.CloseParenToken);\n    }\n\n    private bool IsArrowExpressionAhead()', '            call.CloseParenToken) { TypeArguments = typeArguments };\n    }\n\n    private bool IsArrowExpressionAhead()')
edit(b,'            if (!TryResolveClrTypeReference(expression.Target, out Type? type))','''            Type? type = null;
            if (expression.TypeArguments.Count > 0 && expression.Target is NameExpressionSyntax genericName)
            {
                type = CloseGenericClrType(genericName.IdentifierToken, expression.TypeArguments);
                if (type is null) return new BoundErrorExpression();
            }
            else if (!TryResolveClrTypeReference(expression.Target, out type))''')
mir='src/Copeland/Copeland.TS.Mir/MirNodes.cs'
edit(mir,'public sealed record MirClrType(string AssemblyIdentity, string Namespace, string MetadataName) : MirType(MetadataName)\n{\n    public override string Name => MetadataName;','''public sealed record MirClrType(string AssemblyIdentity, string Namespace, string MetadataName) : MirType(MetadataName)
{
    public IReadOnlyList<MirType> TypeArguments { get; init; } = [];
    public override string Name => TypeArguments.Count == 0 ? MetadataName : MetadataName + "<" + string.Join(", ", TypeArguments.Select(argument => argument.Name)) + ">";''')
edit(mir,'    IReadOnlyList<MirType> GenericArguments);\npublic sealed record MirClrInvocationExpression','''    IReadOnlyList<MirType> GenericArguments)
{
    public MirClrType? DeclaringTypeIdentity { get; init; }
}
public sealed record MirClrInvocationExpression''')
# Preserve type arguments in semantic equivalence, independent of carrier spelling.
edit(mir,'            (MirCallableType leftCallable, MirCallableType rightCallable)', '''            (MirClrType leftClr, MirClrType rightClr) => leftClr.AssemblyIdentity == rightClr.AssemblyIdentity
                && leftClr.MetadataName == rightClr.MetadataName
                && leftClr.TypeArguments.Count == rightClr.TypeArguments.Count
                && leftClr.TypeArguments.Zip(rightClr.TypeArguments).All(pair => AreEquivalent(pair.First, pair.Second)),
            (MirCallableType leftCallable, MirCallableType rightCallable)''')
lower='src/Copeland/Copeland.TS/Lowering/MirLowerer.cs'
edit(lower,'ClrTypeSymbol clr => new MirClrType(clr.AssemblyIdentity, clr.Namespace, clr.MetadataName),','ClrTypeSymbol clr => LowerClrType(clr.RuntimeType),')
edit(lower,'genericArguments.Select(ToMirType).ToArray());','genericArguments.Select(ToMirType).ToArray()) { DeclaringTypeIdentity = LowerClrType(declaringType) };')
edit(lower,'        return new MirClrType(type.Assembly.FullName ?? type.Assembly.GetName().Name ?? "<unknown>", type.Namespace ?? string.Empty, type.FullName?.Replace(\'+\', \'.\') ?? type.Name);','        return LowerClrType(type);')
insert='''    private static MirClrType LowerClrType(Type type)
    {
        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return new MirClrType(type.Assembly.FullName ?? type.Assembly.GetName().Name ?? "<unknown>",
            type.Namespace ?? string.Empty, definition.FullName?.Replace('+', '.') ?? definition.Name)
        {
            TypeArguments = type.IsGenericType ? type.GetGenericArguments().Select(ToMirTypeFromRuntimeType).ToArray() : [],
        };
    }

'''
edit(lower,'    private static MirRecordTypeId ToMirRecordTypeId(',insert+'    private static MirRecordTypeId ToMirRecordTypeId(')
c='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs'
edit(c,'MirClrType clr => "global::" + clr.MetadataName,','MirClrType clr => MapClrType(clr),')
edit(c,'        string declaringType = "global::" + member.DeclaringType;','        string declaringType = member.DeclaringTypeIdentity is null ? "global::" + member.DeclaringType : MapClrType(member.DeclaringTypeIdentity);')
edit(c,'        string target = property.IsStatic ? "global::" + property.DeclaringType : receiver','        string target = property.IsStatic ? (property.DeclaringTypeIdentity is null ? "global::" + property.DeclaringType : MapClrType(property.DeclaringTypeIdentity)) : receiver')
edit(c,'    private static string MapType(MirType type)', '''    private static string MapClrType(MirClrType type)
    {
        if (type.TypeArguments.Count == 0) return "global::" + type.MetadataName;
        string definition = type.MetadataName[..type.MetadataName.IndexOf('`')];
        return "global::" + definition + "<" + string.Join(", ", type.TypeArguments.Select(MapType)) + ">";
    }

    private static string MapType(MirType type)''')
