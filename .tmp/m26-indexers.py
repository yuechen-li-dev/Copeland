from pathlib import Path
p=Path('src/Copeland/Copeland.TS.Mir/MirNodes.cs');s=p.read_text().replace('public MirClrType? DeclaringTypeIdentity { get; init; }','public MirClrType? DeclaringTypeIdentity { get; init; }\n    public bool IsIndexerGetter { get; init; }\n    public bool IsIndexerSetter { get; init; }');p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Lowering/MirLowerer.cs');s=p.read_text();old='genericArguments.Select(ToMirType).ToArray()) { DeclaringTypeIdentity = LowerClrType(declaringType) };';new='''genericArguments.Select(ToMirType).ToArray())
        {
            DeclaringTypeIdentity = LowerClrType(declaringType),
            IsIndexerGetter = declaringType.GetProperties().Any(property => property.GetIndexParameters().Length > 0 && property.GetMethod == member),
            IsIndexerSetter = declaringType.GetProperties().Any(property => property.GetIndexParameters().Length > 0 && property.SetMethod == member),
        };''';assert s.count(old)==2;s=s.replace(old,new,1);p.write_text(s)
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text()
# CLR indexers use existing reflected overload resolver, carrying accessor identity into MIR.
needle='''                if (receiver.Type is MutableArrayTypeSymbol mutableArray)
                {''';pos=s.index(needle,s.index('private BoundExpression BindAssignment'))
s=s[:pos]+'''                if (receiver.Type is ClrTypeSymbol clrIndexer)
                {
                    if (a.EqualsToken.Kind != SyntaxKind.EqualsToken)
                    {
                        ReportRepair("COPE-MUTATION-0001", "Compound CLR indexer updates require explicit indexed assignment with receiver and index bound once.", a.EqualsToken, "Bind receiver and index once; use explicit indexed assignment.");
                        return new BoundErrorExpression();
                    }
                    return BindClrIndexer(clrIndexer, receiver, indexed, a.Right);
                }
'''+s[pos:]
# Move bindIndex expression into non CLR branch so not bound twice.
start=s.index('private BoundExpression BindIndex(');pos=s.index('            var boundIndex =',start)
s=s[:pos]+'''            if (receiver.Type is ClrTypeSymbol clrIndexer) return BindClrIndexer(clrIndexer, receiver, index, null);
            if (receiver.Type == PrimitiveTypeSymbol.Error) return new BoundErrorExpression();
'''+s[pos:]
pos=s.index('        private BoundExpression ReportInvalidIndex(')
s=s[:pos]+'''        private BoundExpression BindClrIndexer(ClrTypeSymbol owner, BoundExpression receiver, IndexExpressionSyntax index, ExpressionSyntax? assignedValue)
        {
            IEnumerable<MethodBase> accessors = owner.RuntimeType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length > 0)
                .Select(property => assignedValue is null ? property.GetMethod : property.SetMethod)
                .OfType<MethodBase>()
                .Where(_clrResolver.IsMemberVisible);
            IReadOnlyList<ExpressionSyntax> arguments = assignedValue is null ? [index.Index] : [index.Index, assignedValue];
            return BindClrInvocation(arguments, index.OpenBracketToken, accessors, receiver, owner.Name + " indexer");
        }

'''+s[pos:]
# assignment currently binds index before entering CLR and binds twice: move after CLR conditional.
s=s.replace('''                var boundIndex = BindExpression(indexed.Index);
                if (receiver.Type is ClrTypeSymbol''','''                if (receiver.Type is ClrTypeSymbol''',1)
pos=s.index(needle,s.index('private BoundExpression BindAssignment'))
s=s[:pos]+'                var boundIndex = BindExpression(indexed.Index);\n'+s[pos:]
p.write_text(s)
p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs');s=p.read_text();s=s.replace('string arguments = string.Join(", ", invocation.Arguments.Select(argument => EmitAsyncExpression(argument, function)));','string[] arguments = invocation.Arguments.Select(argument => EmitAsyncExpression(argument, function)).ToArray();').replace('string arguments = string.Join(", ", EmitArguments(invocation.Arguments, writer, function, enumNames, ref tempIndex, diagnostics));','string[] arguments = EmitArguments(invocation.Arguments, writer, function, enumNames, ref tempIndex, diagnostics).ToArray();').replace('string? receiver, string arguments)\n    {\n        string declaringType','string? receiver, IReadOnlyList<string> argumentValues)\n    {\n        string arguments = string.Join(", ", argumentValues);\n        string declaringType')
needle='''        string genericSuffix = member.GenericArguments.Count''';pos=s.index(needle,s.index('private static string EmitClrInvocationCore'))
s=s[:pos]+'''        if (member.IsIndexerGetter) return $"({target})[{string.Join(", ", argumentValues)}]";
        if (member.IsIndexerSetter) return $"({target})[{string.Join(", ", argumentValues.Take(argumentValues.Count - 1))}] = {argumentValues[^1]}";
'''+s[pos:];p.write_text(s)
