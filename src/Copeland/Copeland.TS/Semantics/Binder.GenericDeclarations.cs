using Copeland.TS.Syntax;
using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Semantics;


public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private readonly Dictionary<string, RecordTypeSymbol> _genericRecords = new(StringComparer.Ordinal);
        private readonly Dictionary<RecordTypeSymbol, SyntaxToken> _genericRecordAnchors = [];
        private readonly HashSet<string> _activeGenericRecords = new(StringComparer.Ordinal);
        private readonly HashSet<FunctionSymbol> _activeGenericSpecializations = [];
        private bool _hostRequirementsReady;
        private bool _hostRecordsReady;
        private readonly HashSet<RecordTypeSymbol> _publishedHostRecords = [];
        private readonly HashSet<TypeParameterSymbol> _activeHostDefaults = [];
        private void RefreshHostDeclarationRequirements()
        {
            _hostRequirementsReady = true;
            foreach (var declaration in _tree.Root.Members.OfType<InterfaceDeclarationSyntax>())
            {
                if (_interfaces.TryGetValue(declaration.Identifier.Text, out var requirement))
                {
                    RefreshRequirements(declaration.GenericParameters, requirement.TypeParameters);
                }
            }
            foreach (var declaration in _tree.Root.Members.OfType<RecordDeclarationSyntax>())
            {
                if (_recordTypes.TryGetValue(declaration.Identifier.Text, out var record))
                {
                    RefreshRequirements(declaration.GenericParameters, record.TypeParameters);
                }
            }

            foreach (var declaration in _tree.Root.Members.OfType<TypeAliasDeclarationSyntax>())
            {
                if (_aliases.TryGetValue(declaration.Identifier.Text, out var alias))
                {
                    RefreshRequirements(declaration.GenericParameters, alias.TypeParameters);
                }
            }
        }

        private void RefreshRequirements(GenericParameterListSyntax? list, IReadOnlyList<TypeParameterSymbol> parameters)
        {
            using var context = EnterHostParameters(parameters);
            for (int index = 0; index < (list?.Types.Count ?? 0); index++)
            {
                parameters[index].Requirements = BindRequirements(list!.Types[index]);
            }
        }

        private void InitializeGenericDeclarations()
        {
            foreach (var declaration in _tree.Root.Members.OfType<InterfaceDeclarationSyntax>())
            {
                if (_interfaces.TryGetValue(declaration.Identifier.Text, out var symbol))
                {
                    symbol.TypeParameters = BindHostParameters(declaration.GenericParameters, [], "interface:" + declaration.Identifier.Text);
                }
            }

            foreach (var declaration in _tree.Root.Members.OfType<RecordDeclarationSyntax>())
            {
                if (_recordTypes.TryGetValue(declaration.Identifier.Text, out var symbol))
                {
                    _genericRecordAnchors[symbol] = declaration.Identifier;
                    symbol.TypeParameters = BindHostParameters(declaration.GenericParameters, [], "record:" + (symbol.StableIdentity ?? symbol.Name));
                }
            }

            foreach (var declaration in _tree.Root.Members.OfType<TypeAliasDeclarationSyntax>())
            {
                if (_aliases.TryGetValue(declaration.Identifier.Text, out var symbol))
                {
                    symbol.TypeParameters = BindHostParameters(declaration.GenericParameters, [], "alias:" + declaration.Identifier.Text);
                }
            }
        }

        private List<TypeParameterSymbol> BindHostParameters(GenericParameterListSyntax? list, IReadOnlyList<TypeParameterSyntax> fallback, string identity)
        {
            var result = new List<TypeParameterSymbol>();
            var types = list?.Types ?? fallback;
            foreach (TypeParameterSyntax syntax in types)
            {
                if (_aliases.ContainsKey(syntax.Identifier.Text) || _recordTypes.ContainsKey(syntax.Identifier.Text) ||
                    _enumTypes.ContainsKey(syntax.Identifier.Text) ||
                    _tableTypes.ContainsKey(syntax.Identifier.Text) ||
                    _interfaces.ContainsKey(syntax.Identifier.Text))
                {
                    Report("COPE-GENERIC-0002", "Type parameter cannot shadow a compilation-unit type declaration.", syntax.Identifier);
                }

                result.Add(new(syntax.Identifier.Text, new(syntax.Identifier.Text, result.Count, identity + ":" + result.Count), new([], [])));
            }

            using var context = EnterHostParameters(result);
            for (int index = 0; index < types.Count; index++)
            {
                if (_hostRequirementsReady)
                {
                    result[index].Requirements = BindRequirements(types[index]);
                }

                if (types[index].DefaultType is { } defaultType)
                {
                    result[index].DefaultTypeSyntax = defaultType;
                    if (_hostRequirementsReady)
                    {
                        result[index].DefaultType = BindType(defaultType, types[index].Identifier, "COPE-GENERIC-0008", "type default");
                    }
                }
            }

            foreach (TemplateParameterSyntax syntax in list?.Values ?? [])
            {
                TypeSymbol type = BindType(syntax.Type, syntax.Identifier, "COPE-GENERIC-0016", "static parameter");
                if (!TypeFacts.IsNumeric(type) && type != PrimitiveTypeSymbol.Boolean)
                {
                    Report("COPE-GENERIC-0016", "Host static generic parameters require int, float/number, or boolean.", syntax.Identifier);
                }

                result.Add(new(syntax.Identifier.Text, new(syntax.Identifier.Text, result.Count, identity + ":" + result.Count), new([], []))
                    {
                        StaticType = type,
                        StaticDefault = syntax.DefaultValue,

                    });
            }

            if (result.Count > MaxTypeParametersPerFunction)
            {
                Report(
                    "COPE-GENERIC-0011",
                    "Generic declaration exceeds the eight-parameter budget.",
                    types.FirstOrDefault()?.Identifier ?? list!.LessToken
                );
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TypeParameterSymbol parameter in result)
            {
                if (!seen.Add(parameter.Name))
                {
                    Report(
                        "COPE-GENERIC-0001",
                        "Duplicate generic parameter '" + parameter.Name + "'.",
                        types.FirstOrDefault()?.Identifier ?? list!.LessToken
                    );
                }
            }

            return result;
        }

        private IDisposable EnterHostParameters(IReadOnlyList<TypeParameterSymbol> parameters)
        {
            var previous = _activeTypeParameters;
            _activeTypeParameters = CreateTypeParameterScope(parameters);
            return new GenericScope(() => _activeTypeParameters = previous);
        }

        private TypeSymbol HostParameterType(TypeParameterSymbol parameter, SyntaxToken anchor)
        {
            if (parameter.StaticType is not null)
            {
                Report("COPE-GENERIC-0016", "A static value parameter cannot be used as a storage type.", anchor);
                return PrimitiveTypeSymbol.Error;
            }

            return parameter.Type;
        }

        private TypeSymbol[] BindHostArguments(IReadOnlyList<TypeParameterSymbol> parameters, IReadOnlyList<TypeSyntax> syntax, SyntaxToken anchor)
        {
            var result = new TypeSymbol?[parameters.Count];
            int position = 0;
            bool named = false;
            foreach (TypeSyntax argument in syntax)
            {
                int index;
                if (argument is GenericValueArgumentTypeSyntax { NameToken: { } name })
                {
                    named = true;
                    index = parameters.ToList().FindIndex(parameter => parameter.StaticType is not null && parameter.Name == name.Text);
                }
                else
                {
                    index = named ? -1 : position++;
                }

                if (index < 0 || index >= result.Length || result[index] is not null)
                {
                    Report("COPE-GENERIC-0007", "Unknown, duplicate, excess, or misplaced generic argument.", anchor);
                    continue;
                }

                result[index] = parameters[index].StaticType is { } staticType ? BindHostStaticArgument(argument, staticType, anchor) : BindType(argument, anchor, "COPE-GENERIC-0008", "type argument");
            }

            for (int index = 0; index < parameters.Count; index++)
            {
                if (result[index] is null)
                {
                    TypeParameterSymbol parameter = parameters[index];
                    if (parameter.DefaultType is not null || parameter.DefaultTypeSyntax is not null)
                    {
                        var map = parameters.Take(index).Select((item, slot) => (item.Type, Value: result[slot]!)).ToDictionary(item => (TypeSymbol)item.Type, item => item.Value);
                        result[index] = BindHostTypeDefault(parameters, parameter, map, anchor);
                    }
                    else if (parameter.StaticType is not null && parameter.StaticDefault is not null)
                    {
                        result[index] = BindHostStaticDefault(parameters, result, index, anchor);
                    }
                    else
                    {
                        Report("COPE-GENERIC-0007", "Missing generic argument '" + parameter.Name + "'.", anchor);
                        result[index] = PrimitiveTypeSymbol.Error;
                    }
                }
            }

            return result.Select(type => type!).ToArray();
        }

        private TypeSymbol BindHostTypeDefault(IReadOnlyList<TypeParameterSymbol> parameters, TypeParameterSymbol parameter,
            IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions, SyntaxToken anchor)
        {
            if (!_activeHostDefaults.Add(parameter) || _activeHostDefaults.Count > 16)
            {
                Report("COPE-GENERIC-0008", "Recursive or excessive generic type default.", anchor);
                return PrimitiveTypeSymbol.Error;
            }
            using var budgetContext = new GenericScope(() => _activeHostDefaults.Remove(parameter));
            using var context = EnterHostParameters(parameters);
            TypeSymbol type = parameter.DefaultType ?? BindType(parameter.DefaultTypeSyntax!, anchor, "COPE-GENERIC-0008", "type default");
            if (type == PrimitiveTypeSymbol.Error)
            {
                Report("COPE-GENERIC-0008", "Generic type default does not resolve to a value type.", anchor);
            }
            return SubstituteType(type, substitutions);
        }

        private TypeSymbol BindHostStaticArgument(TypeSyntax syntax, TypeSymbol expected, SyntaxToken anchor)
        {
            ExpressionSyntax? expression = syntax switch
            {
                GenericValueArgumentTypeSyntax argument => argument.Expression,
                LiteralTypeSyntax literal => new LiteralExpressionSyntax(literal.LiteralToken),
                IdentifierTypeSyntax identifier => new NameExpressionSyntax(identifier.Identifier),
                _ => null,
            };
            if (expression is null)
            {
                Report("COPE-GENERIC-0016", "Expected a typed compile-time scalar argument.", anchor);
                return PrimitiveTypeSymbol.Error;
            }

            if (expression is NameExpressionSyntax name &&
                _activeTypeParameters?.TryGetValue(name.IdentifierToken.Text, out var parameter) == true &&
                parameter.StaticType == expected)
            {
                return parameter.Type;
            }

            return BindHostStaticValue(expression, expected, anchor);
        }

        private TypeSymbol BindHostStaticValue(ExpressionSyntax syntax, TypeSymbol expected, SyntaxToken anchor)
        {
            BoundExpression expression = BindExpression(syntax, expected);
            if (!TypeFacts.AreEquivalent(expression.Type, expected))
            {
                Report("COPE-GENERIC-0016", "Static generic argument has the wrong scalar type.", anchor);
                return PrimitiveTypeSymbol.Error;
            }

            return EvaluateHostStaticArgument(expression, expected, anchor);
        }

        private TypeSymbol BindHostStaticDefault(IReadOnlyList<TypeParameterSymbol> parameters, IReadOnlyList<TypeSymbol?> arguments, int index, SyntaxToken anchor)
        {
            using var context = EnterHostParameters(parameters);
            Scope previousScope = _scope;
            _scope = new Scope(null);
            using var lexicalContext = new GenericScope(() => _scope = previousScope);
            TypeParameterSymbol parameter = parameters[index];
            var substitutions = parameters.Take(index).Select((item, slot) => (item.Type, Argument: arguments[slot]!)).ToDictionary(item => (TypeSymbol)item.Type, item => item.Argument);
            BoundExpression expression = BindExpression(parameter.StaticDefault!, parameter.StaticType);
            expression = new ClosedInstantiationRewriter(this, substitutions).RewriteExpression(expression);
            return EvaluateHostStaticArgument(expression, parameter.StaticType!, anchor);
        }

        private static string? OpenStaticIdentity(BoundExpression expression)
        {
            return expression switch
            {
                BoundGenericStaticExpression parameter => parameter.Parameter.StableIdentity,
                BoundUnaryExpression unary when OpenStaticIdentity(unary.Operand) is { } operand => unary.OperatorKind + "(" + operand + ")",
                BoundBinaryExpression binary when OpenStaticIdentity(binary.Left) is not null || OpenStaticIdentity(binary.Right) is not null => binary.OperatorKind + "(" + StaticOperandIdentity(binary.Left) + "," + StaticOperandIdentity(binary.Right) + ")",
                _ => null,
            };
        }

        private static string StaticOperandIdentity(BoundExpression expression)
        {
            return OpenStaticIdentity(expression) ?? (expression is BoundLiteralExpression literal ? new StaticArgumentTypeSymbol(literal.Type, literal.Value!).Name : "unsupported");
        }

        private TypeSymbol EvaluateHostStaticArgument(BoundExpression expression, TypeSymbol expected, SyntaxToken anchor)
        {
            if (OpenStaticIdentity(expression) is { } identity)
            {
                if (identity.Contains("unsupported", StringComparison.Ordinal))
                {
                    Report("COPE-GENERIC-0016", "Open static arguments support scalar literals, parameters, and unary/binary operations.", anchor);
                    return PrimitiveTypeSymbol.Error;
                }

                return new StaticExpressionArgumentTypeSymbol(expected, expression, identity);
            }

            try
            {
                var evaluator = new StaticEvaluator(_functions, FunctionEffectClassifier.Classify(_functions), StaticEvaluationLimits.M1);
                if (evaluator.Evaluate(expression) is StaticPrimitiveValue value)
                {
                    return new StaticArgumentTypeSymbol(expected, value.Value!);
                }
            }
            catch (StaticEvaluationException exception)
            {
                Report("COPE-GENERIC-0016", exception.Message, anchor);
            }

            return PrimitiveTypeSymbol.Error;
        }

        private static Dictionary<TypeSymbol, TypeSymbol> HostSubstitutions(IReadOnlyList<TypeParameterSymbol> parameters, IReadOnlyList<TypeSymbol> arguments) => parameters.Select((parameter, index) => (parameter.Type, Argument: arguments[index])).ToDictionary(item => (TypeSymbol)item.Type, item => item.Argument);
        private TypeSymbol? BindAuthoredGenericType(GenericTypeSyntax syntax)
        {
            if (_recordTypes.TryGetValue(syntax.Identifier.Text, out var record) && record.TypeParameters.Count > 0)
            {
                var arguments = BindHostArguments(record.TypeParameters, syntax.TypeArguments, syntax.Identifier);
                ValidateHostRequirements(record.TypeParameters, arguments, syntax.Identifier);
                return CloseHostRecord(record, arguments);
            }

            if (_aliases.TryGetValue(syntax.Identifier.Text, out var alias) && alias.TypeParameters.Count > 0)
            {
                var arguments = BindHostArguments(alias.TypeParameters, syntax.TypeArguments, syntax.Identifier);
                ValidateHostRequirements(alias.TypeParameters, arguments, syntax.Identifier);
                return SubstituteType(alias.CanonicalType, HostSubstitutions(alias.TypeParameters, arguments));
            }

            return null;
        }

        private void ValidateHostRequirements(IReadOnlyList<TypeParameterSymbol> parameters, IReadOnlyList<TypeSymbol> arguments, SyntaxToken anchor)
        {
            if (!_hostRequirementsReady)
            {
                return;
            }

            var substitutions = HostSubstitutions(parameters, arguments);
            for (int index = 0; index < parameters.Count; index++)
            {
                Satisfies(SubstituteHostRequirements(parameters[index].Requirements, substitutions), arguments[index], anchor);
            }
        }

        private RecordTypeSymbol CloseHostRecord(RecordTypeSymbol definition, IReadOnlyList<TypeSymbol> arguments)
        {
            string identity = (definition.StableIdentity ?? definition.Name) + "<" + string.Join(',', arguments.Select(type => ClosedTypeIdentity(type, MaxClosedTypeDepth))) + ">";
            if (_genericRecords.TryGetValue(identity, out var existing))
            {
                return existing;
            }

            if (_genericRecords.Count >= 128 ||
                _genericRecords.Values.Count(record => record.GenericDefinition == definition) >= 16)
            {
                Report(
                    "COPE-GENERIC-0012",
                    "Generic records exceed the 16-per-definition/128-total specialization budget.",
                    GenericRecordAnchor(definition)
                );
                return new RecordTypeSymbol("<error>", new(_nextRecordTypeId++));
            }

            string name = definition.Name + "__" + ComputeStableHashHex(identity);
            var result = new RecordTypeSymbol(name, new(_nextRecordTypeId++), identity)
            {
                GenericDefinition = definition,
                GenericArguments = arguments,

            };
            _genericRecords[identity] = result;
            PopulateHostRecord(result);
            return result;
        }

        private void PopulateHostRecord(RecordTypeSymbol record)
        {
            RecordTypeSymbol definition = record.GenericDefinition!;
            if (record.Fields.Count > 0 || (!_hostRecordsReady && definition.Fields.Count == 0)
                || _publishedHostRecords.Contains(record))
            {
                return;
            }

            if (!_activeGenericRecords.Add(record.StableIdentity!) || _activeGenericRecords.Count > 16)
            {
                Report(
                    "COPE-GENERIC-0015",
                    "Recursive or excessive generic record storage.",
                    GenericRecordAnchor(definition)
                );
                return;
            }

            var substitutions = HostSubstitutions(definition.TypeParameters, record.GenericArguments);
            foreach (RecordFieldSymbol field in definition.Fields)
            {
                record.AddField(new(
                        field.Name,
                        new(record.Id, record.Fields.Count),
                        SubstituteType(field.Type, substitutions),
                        field.IsPublic,
                        field.IsOptional
                    ));
            }

            _activeGenericRecords.Remove(record.StableIdentity!);
            if (!record.GenericArguments.Any(IsOpenOrIllegalTypeArgument))
            {
                _records.Add(new(record));
                _publishedHostRecords.Add(record);
            }
        }

        private void RefreshHostRecords()
        {
            _hostRecordsReady = true;
            foreach (RecordTypeSymbol record in _genericRecords.Values.ToArray())
            {
                PopulateHostRecord(record);
            }
            foreach (RecordTypeSymbol record in _genericRecords.Values.ToArray())
            {
                if (!record.GenericArguments.Any(IsOpenOrIllegalTypeArgument))
                {
                    ValidateHostRequirements(record.GenericDefinition!.TypeParameters, record.GenericArguments,
                        GenericRecordAnchor(record.GenericDefinition));
                }
            }
        }

        private SyntaxToken GenericRecordAnchor(RecordTypeSymbol definition)
        {
            return _genericRecordAnchors.GetValueOrDefault(definition)
                ?? new(SyntaxKind.IdentifierToken, 0, definition.Name, null);
        }

        private BoundExpression CloseStaticValue(BoundGenericStaticExpression expression, IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions)
        {
            TypeSymbol parameter = SubstituteType(expression.Parameter, substitutions);
            return parameter switch
            {
                StaticArgumentTypeSymbol value => new BoundLiteralExpression(value.Value, value.ValueType),
                TypeParameterTypeSymbol formal => new BoundGenericStaticExpression(formal, expression.Type),
                StaticExpressionArgumentTypeSymbol plan => plan.Expression,
                _ => new BoundErrorExpression(),
            };
        }

        private TypeSymbol CloseHostStaticArgument(StaticExpressionArgumentTypeSymbol argument, IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions)
        {
            var expression = new ClosedInstantiationRewriter(this, substitutions).RewriteExpression(argument.Expression);
            return EvaluateHostStaticArgument(expression, argument.ValueType, new(SyntaxKind.IdentifierToken, 0, "static", null));
        }

        private BoundExpression CloseRecordAccess(
            BoundRecordFieldAccessExpression expression,
            BoundExpression receiver,
            IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions
        )
        {
            var record = (RecordTypeSymbol)SubstituteType(expression.RecordType, substitutions);
            return new BoundRecordFieldAccessExpression(receiver, record, record.Fields.Single(field => field.Name == expression.Field.Name));
        }

        private BoundExpression CloseRecordConstruction(
            RecordTypeSymbol type,
            IReadOnlyList<BoundRecordFieldInitializer> fields,
            IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions
        )
        {
            var record = (RecordTypeSymbol)SubstituteType(type, substitutions);
            return new BoundRecordConstructionExpression(
                record,
                fields.Select(field => new BoundRecordFieldInitializer(record.Fields.Single(item => item.Name == field.Field.Name), field.Value)).ToArray()
            );
        }

        private BoundExpression CloseRecordWith(
            BoundExpression source,
            RecordTypeSymbol type,
            IReadOnlyList<BoundRecordFieldInitializer> fields,
            IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions
        )
        {
            var record = (RecordTypeSymbol)SubstituteType(type, substitutions);
            return new BoundRecordWithExpression(
                source,
                record,
                fields.Select(field => new BoundRecordFieldInitializer(record.Fields.Single(item => item.Name == field.Field.Name), field.Value)).ToArray()
            );
        }

        private BoundExpression CloseOpenGenericCall(
            BoundOpenGenericCallExpression expression,
            IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions,
            IReadOnlyList<BoundExpression> arguments
        )
        {
            var types = expression.TypeArguments.Select(type => SubstituteType(type, substitutions)).ToArray();
            return new BoundCallExpression(GetOrCreateClosedInstantiation(expression.Function, types, expression.Anchor).Symbol, arguments);
        }

        private RequirementSet SubstituteHostRequirements(RequirementSet requirements, IReadOnlyDictionary<TypeSymbol, TypeSymbol> substitutions) => new(
            requirements.Interfaces,
            requirements.Fields.Select(field => new RequirementFieldSymbol(field.Name, SubstituteType(field.Type, substitutions), field.Ordinal)).ToArray()
        );
        private BoundExpression BindHostOpenCall(
            FunctionSymbol function,
            IReadOnlyList<TypeSymbol> types,
            IReadOnlyList<ExpressionSyntax> arguments,
            SyntaxToken anchor
        )
        {
            var map = HostSubstitutions(function.TypeParameters, types);
            var values = arguments.Select((argument, index) => BindExpression(argument, index < function.Parameters.Count ? SubstituteType(function.Parameters[index].Type, map) : null)).ToArray();
            if (values.Length != function.Parameters.Count)
            {
                Report("COPE-TYPE-0004", "Generic forwarding argument count mismatch.", anchor);
            }

            for (int index = 0; index < Math.Min(values.Length, function.Parameters.Count); index++)
            {
                TypeSymbol expected = SubstituteType(function.Parameters[index].Type, map);
                if (!IsAssignable(expected, values[index].Type))
                {
                    ReportTypeMismatch("COPE-TYPE-0005", expected, values[index].Type, anchor);
                }
            }

            return new BoundOpenGenericCallExpression(function, types, values, SubstituteType(function.ReturnType, map), anchor);
        }

        private sealed class GenericScope(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
