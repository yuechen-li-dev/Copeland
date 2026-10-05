from pathlib import Path
root=Path.cwd()
def edit(path, old, new):
 p=root/path
 s=p.read_text(encoding='utf-8-sig')
 assert old in s, (path,old[:90])
 p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
b='src/Copeland/Copeland.TS/Semantics/Binder.cs'
edit(b,'            if (TryBindNumericConversion(c, out BoundExpression? conversion))','            if (TryBindNativeStringCall(c, out BoundExpression? nativeString))\n            {\n                return nativeString!;\n            }\n\n            if (TryBindNumericConversion(c, out BoundExpression? conversion))')
# All value receivers are resolved once, before dispatch. The original table and instance
# paths each rebound the receiver and then fell through to enum construction.
p=root/b
s=p.read_text()
start=s.index('            if (c.Target is MemberAccessExpressionSyntax unresolvedClrMember')
end=s.index('            if (c.Target is NameExpressionSyntax tsonEncodeName',start)
s=s[:start]+s[end:]
marker='''                    return BindColumnAggregateCall(c, tableMember, columnAccess);
                }
            }'''
replacement='''                    return BindColumnAggregateCall(c, tableMember, columnAccess);
                }
                if (tableReceiver.Type == PrimitiveTypeSymbol.Error)
                {
                    return new BoundErrorExpression();
                }
                if (tableReceiver.Type is MutableArrayTypeSymbol mutableArray && tableMember.NameToken.Text == "freeze")
                {
                    if (c.Arguments.Count != 0)
                    {
                        Report("COPE-ARRAY-0007", "MutableArray.freeze() does not accept arguments.", c.OpenParenToken);
                        return new BoundErrorExpression();
                    }
                    return new BoundMutableArrayFreezeExpression(tableReceiver, new ArrayTypeSymbol(mutableArray.ElementType));
                }
                if (tableReceiver.Type is ClrTypeSymbol clrReceiver)
                {
                    return BindClrMethodCall(c, clrReceiver.RuntimeType, tableReceiver, tableMember.NameToken);
                }
                ReportReceiverCall(c, tableMember, tableReceiver.Type);
                return new BoundErrorExpression();
            }'''
assert marker in s
s=s.replace(marker,replacement)
s=s.replace('Report("COPE-STRING-0001", $"String values support only the \'length\' property; \'{m.NameToken.Text}\' is not available.", m.NameToken);','''if (m.NameToken.Text == "Length")
                {
                    ReportRepair("COPE-STRING-0001", "string does not expose .Length. Use .length.", m.NameToken, ".length");
                }
                else
                {
                    Report("COPE-STRING-0001", $"String values support only the 'length' property; '{m.NameToken.Text}' is not available.", m.NameToken);
                }''')
s=s.replace('Report("COPE-ENUM-0010", "Expected enum type name.", enumName.IdentifierToken);','Report("COPE-BIND-0001", $"Undefined name \'{enumName.IdentifierToken.Text}\'.", enumName.IdentifierToken);')
insert='''        private bool TryBindNativeStringCall(CallExpressionSyntax call, out BoundExpression? result)
        {
            result = null;
            if (call.Target is not MemberAccessExpressionSyntax
                { Target: NameExpressionSyntax { IdentifierToken.Text: "String" } } member
                || _scope.TryLookup("String", out _))
            {
                return false;
            }
            Copeland.TS.Mir.MirNativeOperation? operation = member.NameToken.Text switch
            {
                "Split" => Copeland.TS.Mir.MirNativeOperation.StringSplit,
                "IndexOf" => Copeland.TS.Mir.MirNativeOperation.StringIndexOf,
                "CodeAt" => Copeland.TS.Mir.MirNativeOperation.StringCodeAt,
                "Slice" => Copeland.TS.Mir.MirNativeOperation.StringSlice,
                "Join" => Copeland.TS.Mir.MirNativeOperation.StringJoin,
                _ => null,
            };
            if (operation is null)
            {
                return false;
            }
            var signature = Copeland.TS.Mir.MirNativeOperations.Signature(operation.Value);
            TypeSymbol Project(Copeland.TS.Mir.MirType type)
            {
                if (type is Copeland.TS.Mir.MirArrayType)
                {
                    return new ArrayTypeSymbol(PrimitiveTypeSymbol.String);
                }
                return type.Name == "int" ? PrimitiveTypeSymbol.Int : PrimitiveTypeSymbol.String;
            }
            if (call.Arguments.Count != signature.Parameters.Count)
            {
                Report("COPE-STRING-0002", $"String.{member.NameToken.Text} expects {signature.Parameters.Count} arguments.", member.NameToken);
                result = new BoundErrorExpression();
                return true;
            }
            var parameters = signature.Parameters.Select((type, index) => new ParameterSymbol("argument" + index, Project(type))).ToArray();
            var arguments = new List<BoundExpression>();
            bool failed = false;
            for (int index = 0; index < parameters.Length; index++)
            {
                BoundExpression argument = BindExpression(call.Arguments[index], parameters[index].Type);
                arguments.Add(argument);
                if (argument.Type == PrimitiveTypeSymbol.Error)
                {
                    failed = true;
                }
                else if (!IsAssignable(parameters[index].Type, argument.Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", parameters[index].Type, argument.Type, InferenceAnchor(call.Arguments[index]));
                    failed = true;
                }
            }
            var function = new FunctionSymbol("String." + member.NameToken.Text, parameters, Project(signature.Result))
            {
                NativeOperation = operation,
            };
            result = failed ? new BoundErrorExpression() : new BoundCallExpression(function, arguments);
            return true;
        }

        private string AuthoredText(SyntaxNode node)
        {
            IEnumerable<SyntaxToken> Tokens(SyntaxNode current)
            {
                if (current is SyntaxToken token)
                {
                    yield return token;
                }
                else
                {
                    foreach (SyntaxNode child in current.GetChildren())
                    {
                        foreach (SyntaxToken nested in Tokens(child)) yield return nested;
                    }
                }
            }
            SyntaxToken[] tokens = Tokens(node).ToArray();
            int start = tokens.Min(token => token.Position);
            int end = tokens.Max(token => token.Position + token.Text.Length);
            return _tree.Text[start..end];
        }

        private void ReportReceiverCall(CallExpressionSyntax call, MemberAccessExpressionSyntax member, TypeSymbol receiverType)
        {
            string method = member.NameToken.Text;
            if (receiverType == PrimitiveTypeSymbol.String && method is "Split" or "IndexOf" or "CodeAt" or "Slice" or "Join")
            {
                string arguments = string.Join(", ", call.Arguments.Select(AuthoredText));
                string replacement = $"String.{method}({AuthoredText(member.Target)}{(arguments.Length == 0 ? "" : ", " + arguments)})";
                ReportRepair("COPE-CALL-0021", $"'{method}' is not a receiver method of '{AuthoredText(member.Target)}' of type 'string'. Use {replacement}.", member.NameToken, replacement);
                return;
            }
            string repair = receiverType is ArrayTypeSymbol && method == "push"
                ? "Build a fixed MutableArray<T>(length), assign elements, then freeze(); growable native lists are not yet supported."
                : "Pass the value to a Copeland function.";
            ReportRepair("COPE-CALL-0021", $"'{method}' is not a receiver method on '{receiverType.Name}'. {repair}", member.NameToken, repair);
        }

        private void ReportRepair(string id, string message, SyntaxToken token, string replacement)
        {
            _diagnostics.Report(id, message, token.Position, token.Text.Length, suggestedReplacement: replacement, repairKind: "canonical-form");
        }

'''
marker='        private bool TryReportValueMemberCall('
s=s.replace(marker,insert+marker)
p.write_text(s,encoding='utf-8',newline='\n')
edit('src/Copeland/Copeland.TS/Diagnostics/Diagnostic.cs','    string? SourcePath = null);','    string? SourcePath = null,\n    string? SuggestedReplacement = null,\n    string? RepairKind = null);')
edit('src/Copeland/Copeland.TS/Diagnostics/DiagnosticBag.cs','string? sourcePath = null)','string? sourcePath = null, string? suggestedReplacement = null, string? repairKind = null)')
edit('src/Copeland/Copeland.TS/Diagnostics/DiagnosticBag.cs','new Diagnostic(id, message, position, length, sourcePath)','new Diagnostic(id, message, position, length, sourcePath, suggestedReplacement, repairKind)')
edit('src/Copeland/Copeland.TS/Lowering/MirLowerer.cs','new MirCallExpression(call.FunctionName, arguments, call.Type)','call with { Arguments = arguments }')
edit('src/Copeland/Copeland.TS/Lowering/MirLowerer.cs','ToMirType(c.Type)),\n            BoundFunctionReferenceExpression','ToMirType(c.Type)) { NativeOperation = c.Function.NativeOperation },\n            BoundFunctionReferenceExpression')
edit('src/Copeland/Copeland.TS.Mir/MirValidator.cs','''            case MirCallExpression call:
                foreach (var argument in call.Arguments) ValidateCallableExpression(argument, functions, diagnostics);''','''            case MirCallExpression call:
                if (call.NativeOperation is not null && !MirNativeOperations.IsValid(call))
                {
                    diagnostics.Add(new MirValidationDiagnostic("Native call does not match its typed signature."));
                }
                foreach (var argument in call.Arguments) ValidateCallableExpression(argument, functions, diagnostics);''')
edit('src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs','''        if (!functions.TryGetValue(call.FunctionName, out MirFunction? target))''','''        if (call.NativeOperation is not null)
        {
            if (!MirNativeOperations.IsValid(call)) AddInvalid(diagnostics, "invalid native call signature");
        }
        else if (!functions.TryGetValue(call.FunctionName, out MirFunction? target))''')
