using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private readonly List<(ContributionStreamDeclarationSyntax Syntax, ContributionStreamSymbol Symbol)> _moduleStreams = [];
        private readonly Dictionary<ContributionStreamTypeSymbol, IReadOnlyList<BoundRecordFieldInitializer>> _contributionInitializers = [];
        private int _nextContributionBindingId;

        private void PredeclareContributionStreams(CompilationUnitSyntax root)
        {
            foreach (ContributionStreamDeclarationSyntax declaration in root.Members.OfType<ContributionStreamDeclarationSyntax>())
            {
                ContributionStreamSymbol stream = CreateModuleStream(declaration, null, true);
                if (!_global.TryDeclare(stream))
                {
                    Report("COPE-STREAM-CONTENT-0001", $"Contribution stream '{stream.Name}' conflicts with an existing declaration.", declaration.Identifier);
                    continue;
                }
                _moduleStreams.Add((declaration, stream));
            }

            foreach (ClassDeclarationSyntax declaration in root.Members.OfType<ClassDeclarationSyntax>())
            {
                if (!_classTypes.TryGetValue(declaration.Identifier.Text, out ClassTypeSymbol? owner)) continue;
                foreach (ClassContributionStreamSyntax member in declaration.Members.OfType<ClassContributionStreamSyntax>())
                {
                    ValidateClassVisibility(member.VisibilityKeyword, member.NameToken);
                    if (member.Modifiers.Any(modifier => modifier.Text != "static"))
                    {
                        Report("COPE-STREAM-CONTENT-0001", "A class contribution stream is associated immutable data; only the optional static modifier is supported.", member.NameToken);
                    }
                    if (owner.Fields.Any(field => field.Name == member.NameToken.Text)
                        || owner.FindAssociatedFunction(member.NameToken.Text) is not null
                        || owner.FindContributionStream(member.NameToken.Text) is not null)
                    {
                        Report("COPE-CLASS-0003", $"Duplicate class member '{member.NameToken.Text}' in '{owner.Name}'.", member.NameToken);
                        continue;
                    }
                    ContributionStreamSymbol stream = CreateModuleStream(member.Declaration, owner, member.VisibilityKeyword?.Text != "private");
                    owner.AddContributionStream(stream);
                    _moduleStreams.Add((member.Declaration, stream));
                }
            }
        }

        private ContributionStreamSymbol CreateModuleStream(ContributionStreamDeclarationSyntax declaration, ClassTypeSymbol? owner, bool isPublic)
        {
            string identity = (_moduleIdentity ?? _sourcePath ?? "<standalone>")
                + "::contribution::" + (owner is null ? "" : owner.Name + "::") + declaration.Identifier.Text;
            ContributionStreamTypeSymbol type = CreateContributionType(declaration, identity);
            string name = "__cope_stream_" + SanitizeJavaScriptIdentifier(identity);
            var accessor = new FunctionSymbol(name + "_read", [], type, stableIdentity: identity + "::read");
            var initializer = new FunctionSymbol(name + "_initialize", [], type, stableIdentity: identity + "::initialize");
            return new ContributionStreamSymbol(declaration.Identifier.Text, type, accessor, initializer,
                new VariableSymbol(name + "_value", type, true), owner, isPublic, declaration.Identifier);
        }

        private ContributionStreamTypeSymbol CreateContributionType(ContributionStreamDeclarationSyntax declaration, string identity)
        {
            var type = new ContributionStreamTypeSymbol("__cope_stream_" + SanitizeJavaScriptIdentifier(identity),
                new RecordTypeId(_nextRecordTypeId++), identity);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutBindingEntrySyntax entry in declaration.Entries)
            {
                if (!names.Add(entry.SlotIdentifier.Text))
                {
                    Report("COPE-STREAM-CONTENT-0002", $"Channel '{entry.SlotIdentifier.Text}' is declared more than once in stream '{declaration.Identifier.Text}'.", entry.SlotIdentifier);
                    continue;
                }
                type.AddField(new RecordFieldSymbol(entry.SlotIdentifier.Text, new RecordFieldId(type.Id, type.Fields.Count), PresentationNodeType));
            }
            _records.Add(new BoundRecordDeclaration(type));
            return type;
        }

        private BoundRecordConstructionExpression BindContributions(ContributionStreamDeclarationSyntax syntax, ContributionStreamTypeSymbol type)
        {
            var values = new List<BoundRecordFieldInitializer>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (LayoutBindingEntrySyntax entry in syntax.Entries)
            {
                if (!names.Add(entry.SlotIdentifier.Text)) continue;
                BoundExpression value = BindExpression(entry.Value, PresentationNodeType);
                if (value.Type != PresentationNodeType && value.Type != PrimitiveTypeSymbol.Error)
                {
                    Report("COPE-STREAM-CONTENT-0003", $"Channel '{syntax.Identifier.Text}.{entry.SlotIdentifier.Text}' requires one '{PresentationNodeType.Name}'; got '{value.Type.Name}'.", entry.SlotIdentifier);
                }
                RecordFieldSymbol field = type.Fields.Single(field => field.Name == entry.SlotIdentifier.Text);
                values.Add(new BoundRecordFieldInitializer(field, value));
            }
            _contributionInitializers[type] = values;
            return new BoundRecordConstructionExpression(type, values);
        }

        private void BindModuleContributionStreams()
        {
            Scope previousScope = _scope;
            FunctionSymbol? previousFunction = _currentFunction;
            ClassTypeSymbol? previousClass = _currentClass;
            try
            {
                foreach ((ContributionStreamDeclarationSyntax syntax, ContributionStreamSymbol stream) in _moduleStreams.OrderBy(item => item.Syntax.Identifier.Position))
                {
                    _scope = new Scope(_global);
                    _currentFunction = stream.Initializer;
                    _currentClass = stream.ClassOwner;
                    BoundRecordConstructionExpression construction = BindContributions(syntax, stream.Type);
                    stream.Contributions = construction.Initializers;
                    _functions.Add(new BoundFunctionDeclaration(stream.Initializer, new BoundBlockStatement([new BoundReturnStatement(construction)])));
                    _functions.Add(new BoundFunctionDeclaration(stream.Accessor, new BoundBlockStatement([new BoundReturnStatement(new BoundVariableExpression(stream.Storage))])));
                }
            }
            finally
            {
                _scope = previousScope;
                _currentFunction = previousFunction;
                _currentClass = previousClass;
            }
        }

        private BoundStatement BindLocalContributionStream(LocalContributionStreamStatementSyntax statement)
        {
            ContributionStreamDeclarationSyntax declaration = statement.Declaration;
            string identity = (_currentFunction?.StableIdentity ?? "<global>") + "::contribution::"
                + declaration.Identifier.Text + "::" + declaration.Identifier.Position.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ContributionStreamTypeSymbol type = CreateContributionType(declaration, identity);
            BoundRecordConstructionExpression construction = BindContributions(declaration, type);
            var variable = new VariableSymbol(declaration.Identifier.Text, type, true);
            if (!_scope.TryDeclare(variable))
            {
                Report("COPE-STREAM-CONTENT-0001", $"Contribution stream '{variable.Name}' conflicts with an existing local declaration.", declaration.Identifier);
            }
            return new BoundVariableDeclaration(variable, construction);
        }

        private BoundExpression BindContributionBinding(ContributionBindingExpressionSyntax syntax)
        {
            BoundExpression contributions = BindExpression(syntax.Contributions);
            if (contributions.Type is not ContributionStreamTypeSymbol type)
            {
                if (contributions.Type != PrimitiveTypeSymbol.Error)
                {
                    Report("COPE-STREAM-CONTENT-0004", $"Binding requires a contribution stream value; got '{contributions.Type.Name}'.", syntax.UsingKeyword);
                }
                return new BoundErrorExpression();
            }
            if (!_scope.TryLookup(syntax.LayoutIdentifier.Text, out Symbol? target) || target is not LayoutSymbol { BoundLayout: not null } layout)
            {
                Report("COPE-LAYOUT-BIND-0002", $"'{syntax.LayoutIdentifier.Text}' is not a concrete compiler-known layout.", syntax.LayoutIdentifier);
                return new BoundErrorExpression();
            }
            if (!TryGetPresentationFactory(out NpmFunctionSymbol createElement))
            {
                Report("COPE-LAYOUT-BIND-0010", $"Binding '{layout.Name}' requires the bounded '{PresentationFactoryName}' import.", syntax.BindKeyword);
                return new BoundErrorExpression();
            }

            foreach (RecordFieldSymbol channel in type.Fields)
            {
                if (!layout.Slots.TryGetValue(channel.Name, out LayoutSlotSymbol? slot) || !slot.IsBindable)
                {
                    Report("COPE-STREAM-CONTENT-0005", $"Channel '{channel.Name}' does not name a singular slot in layout '{layout.Name}'.", syntax.UsingKeyword);
                }
            }
            foreach (LayoutSlotSymbol slot in layout.Slots.Values.Where(slot => slot.IsBindable))
            {
                if (!type.Fields.Any(field => field.Name == slot.Name))
                {
                    Report("COPE-STREAM-CONTENT-0006", $"Contribution stream is missing required channel '{slot.SemanticPath}'.", syntax.UsingKeyword);
                }
            }
            if (type.Fields.Any(field => !layout.Slots.TryGetValue(field.Name, out LayoutSlotSymbol? slot) || !slot.IsBindable)
                || layout.Slots.Values.Any(slot => slot.IsBindable && !type.Fields.Any(field => field.Name == slot.Name)))
            {
                return new BoundErrorExpression();
            }

            var parameter = new ParameterSymbol("__contributions", type);
            var receiver = new BoundVariableExpression(new VariableSymbol(parameter.Name, type, true));
            IReadOnlyList<BoundRecordFieldInitializer>? authored = _contributionInitializers.GetValueOrDefault(type);
            if (authored is null)
            {
                authored = _imports?.Declarations.Values.OfType<ContributionStreamSymbol>().FirstOrDefault(stream => stream.Type == type)?.Contributions;
            }
            var entries = type.Fields.Select(field =>
            {
                var token = new SyntaxToken(SyntaxKind.IdentifierToken, syntax.UsingKeyword.Position, field.Name, null);
                var entrySyntax = new LayoutBindingEntrySyntax(token, token, syntax.Contributions, null);
                return new BoundLayoutBindingEntry(layout.Slots[field.Name], entrySyntax,
                    new BoundRecordFieldAccessExpression(receiver, type, field),
                    authored?.FirstOrDefault(value => value.Field == field)?.Value);
            }).ToArray();

            BoundLayoutTypeDeclaration shape = new(layout.Name + "ContributionShape", ContributionContractNode(layout.BoundLayout.Root));
            var contract = new LayoutTypeSymbol(shape.Name, layout.StableIdentity + "::contribution-shape", shape);
            if (layout.BoundLayout.Satisfaction is { IsSatisfied: true } satisfaction
                && _scope.TryLookup(satisfaction.ContractName, out Symbol? declaredContract)
                && declaredContract is LayoutTypeSymbol declaredType
                && (satisfaction.BoundContract is null || ReferenceEquals(satisfaction.BoundContract, declaredType.BoundLayoutType)))
            {
                contract = declaredType;
            }
            string identity = (_currentFunction?.StableIdentity ?? _moduleIdentity ?? "<global>") + "::bind::"
                + _nextContributionBindingId++.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var function = new FunctionSymbol("__cope_bind_" + SanitizeJavaScriptIdentifier(identity), [parameter], PresentationNodeType, stableIdentity: identity);
            bool fixedRoot = layout.BoundLayout.Root.Dimensions.TryGetValue("width", out BoundLayoutDimension? width)
                && layout.BoundLayout.Root.Dimensions.TryGetValue("height", out BoundLayoutDimension? height)
                && width.Kind == LayoutDimensionKind.Fixed && height.Kind == LayoutDimensionKind.Fixed;
            IReadOnlyDictionary<string, string> classes;
            try
            {
                classes = fixedRoot
                    ? LayoutDataCompiler.ProjectReact(layout.BoundLayout).ClassesBySlot
                    : BuildPrivatePresentationClasses(layout.BoundLayout, layout.StableIdentity);
            }
            catch (InvalidOperationException exception)
            {
                Report("COPE-LAYOUT-BIND-0012", $"Binding '{layout.Name}' cannot realize its layout hosts: {exception.Message}", syntax.LayoutIdentifier);
                return new BoundErrorExpression();
            }
            BoundLayoutNode root = LayoutDataCompiler.ProjectionRoot(layout.BoundLayout.Root);
            var bindingSyntax = new LayoutBindingDeclarationSyntax(syntax.BindKeyword, syntax.LayoutIdentifier, syntax.BindKeyword, [], syntax.UsingKeyword);
            _layoutBindings.Add(new BoundLayoutBinding(layout, contract, bindingSyntax, function, createElement.Name,
                new BoundLayoutReactRealization(root, classes), entries,
                owningComponent: _currentFunction?.ReturnType == PresentationNodeType ? _currentFunction : null,
                isContributionBinding: true));
            return CreateDirectCall(function, [contributions], syntax.BindKeyword);
        }

        private static BoundLayoutTypeNode ContributionContractNode(BoundLayoutNode node)
            => new(node.Name, node.Kind, node.Columns, node.Children.Select(ContributionContractNode).ToArray(), node.Source);
    }
}
