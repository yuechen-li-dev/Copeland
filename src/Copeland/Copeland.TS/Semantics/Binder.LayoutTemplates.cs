using Copeland.TS.Diagnostics;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private bool _bindingLayoutValues;
        private readonly Dictionary<string, BoundExpression> _layoutConstants = new(StringComparer.Ordinal);
        private readonly HashSet<string> _bindingLayoutConstants = new(StringComparer.Ordinal);

        private void EnsureLengthRecord()
        {
            if (_bindingLayoutValues) return;
            if (!_records.Any(record => ReferenceEquals(record.RecordType, LengthTypeSymbol.Instance)))
            {
                _records.Add(new BoundRecordDeclaration(LengthTypeSymbol.Instance));
            }
        }

        private bool TryBindLayoutConstant(SyntaxToken identifier, out BoundExpression? expression)
        {
            if (_layoutConstants.TryGetValue(identifier.Text, out expression)) return true;
            VariableDeclarationStatementSyntax? declaration = _tree.Root.Members
                .OfType<GlobalStatementMemberSyntax>()
                .Select(member => member.Statement)
                .OfType<VariableDeclarationStatementSyntax>()
                .FirstOrDefault(variable => variable.Identifier.Text == identifier.Text);
            if (declaration is null) return false;
            if (declaration.Keyword.Kind != SyntaxKind.ConstKeyword)
            {
                Report("COPE-STATIC-0001", $"Runtime value '{identifier.Text}' cannot configure a layout. Use an immutable const with static-safe values, or select prebuilt layouts at runtime.", identifier);
                expression = new BoundErrorExpression();
                return true;
            }
            if (!_bindingLayoutConstants.Add(identifier.Text))
            {
                Report("COPE-STATIC-0001", $"Static configuration '{identifier.Text}' is recursive.", identifier);
                expression = new BoundErrorExpression();
                return true;
            }
            Scope previousScope = _scope;
            _scope = new Scope(_global);
            try
            {
                TypeSymbol? type = declaration.Type is null ? null
                    : BindType(declaration.Type, declaration.Identifier, "COPE-TEMPLATE-0002", "static configuration");
                expression = BindExpression(declaration.Initializer, type);
                _layoutConstants.Add(identifier.Text, expression);
                return true;
            }
            finally
            {
                _bindingLayoutConstants.Remove(identifier.Text);
                _scope = previousScope;
            }
        }

        private BoundTemplateValue BindLayoutStaticValue(ExpressionSyntax syntax, TypeSymbol? context = null)
        {
            if (syntax is TemplateInstantiationExpressionSyntax invocation)
            {
                return BindTemplateInstantiation(invocation);
            }
            if (syntax is ObjectLiteralExpressionSyntax record && context is not RecordTypeSymbol)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (ObjectPropertySyntax property in record.Properties)
                {
                    if (!names.Add(property.NameToken.Text))
                    {
                        Report("COPE-LAYOUT-PROPERTY-0001", $"Field '{property.NameToken.Text}' is declared more than once.", property.NameToken);
                    }
                }
                var fields = record.Properties.Select(property => new BoundTemplateStructuralField(
                    property.NameToken.Text, BindLayoutStaticValue(property.ValueExpression))).ToArray();
                var type = new StructuralObjectTypeSymbol(fields.Select((field, index) =>
                    new StructuralFieldSymbol(field.Name, field.Value.Type, index, false, true)).ToArray());
                return new BoundTemplateStructuralObject(record.OpenBraceToken, type, fields);
            }
            if (syntax is NameExpressionSyntax name && IsSpatialKeyword(name.IdentifierToken.Text)
                && !_scope.TryLookup(name.IdentifierToken.Text, out _)
                && !_tree.Root.Members.OfType<GlobalStatementMemberSyntax>()
                    .Any(member => member.Statement is VariableDeclarationStatementSyntax variable
                        && variable.Identifier.Text == name.IdentifierToken.Text))
            {
                return new BoundTemplateLiteral(name.IdentifierToken,
                    new LayoutKeywordValue(name.IdentifierToken.Text), PrimitiveTypeSymbol.String);
            }
            BoundExpression expression = BindExpression(syntax, context);
            EnsureLayoutFunctionDependencies(expression);
            return new BoundTemplateOrdinaryExpression(FirstToken(syntax), expression);
        }

        private static bool IsSpatialKeyword(string name)
            => name is "fill" or "fit" or "visible" or "clip" or "auto" or "scroll"
                or "scrollX" or "scrollY" or "wrap" or "noWrap" or "none" or "scaleDown"
                or "ellipsis" or "overflow";

        private void EnsureLayoutFunctionDependencies(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundCallExpression call:
                    foreach (BoundExpression argument in call.Arguments) EnsureLayoutFunctionDependencies(argument);
                    if (_functions.Any(function => ReferenceEquals(function.Symbol, call.Function))) return;
                    FunctionDeclarationSyntax? syntax = _tree.Root.Members.OfType<FunctionDeclarationSyntax>()
                        .FirstOrDefault(function => ReferenceEquals(_globalLookup(function.Identifier.Text), call.Function));
                    if (syntax is null) return;
                    Scope previousScope = _scope;
                    bool previousMode = _bindingLayoutValues;
                    _bindingLayoutValues = false;
                    BoundFunctionDeclaration bound;
                    try
                    {
                        bound = BindFunction(syntax);
                        _functions.Add(bound);
                    }
                    finally
                    {
                        _bindingLayoutValues = previousMode;
                        _scope = previousScope;
                    }
                    VisitStatement(bound.Body);
                    break;
                case BoundRecordConstructionExpression record:
                    foreach (BoundRecordFieldInitializer field in record.Initializers) EnsureLayoutFunctionDependencies(field.Value);
                    break;
                case BoundRecordWithExpression update:
                    EnsureLayoutFunctionDependencies(update.Source);
                    foreach (BoundRecordFieldInitializer field in update.Replacements) EnsureLayoutFunctionDependencies(field.Value);
                    break;
                case BoundRecordFieldAccessExpression access:
                    EnsureLayoutFunctionDependencies(access.Receiver);
                    break;
                case BoundBinaryExpression binary:
                    EnsureLayoutFunctionDependencies(binary.Left);
                    EnsureLayoutFunctionDependencies(binary.Right);
                    break;
                case BoundUnaryExpression unary:
                    EnsureLayoutFunctionDependencies(unary.Operand);
                    break;
                case BoundIfExpression conditional:
                    EnsureLayoutFunctionDependencies(conditional.Condition);
                    EnsureLayoutFunctionDependencies(conditional.ThenExpression);
                    EnsureLayoutFunctionDependencies(conditional.ElseExpression);
                    break;
                case BoundArrayExpression array:
                    foreach (BoundExpression element in array.Elements) EnsureLayoutFunctionDependencies(element);
                    break;
            }

            void VisitStatement(BoundStatement statement)
            {
                switch (statement)
                {
                    case BoundBlockStatement block:
                        foreach (BoundStatement child in block.Statements) VisitStatement(child);
                        break;
                    case BoundReturnStatement { Expression: not null } returned:
                        EnsureLayoutFunctionDependencies(returned.Expression);
                        break;
                    case BoundVariableDeclaration variable:
                        EnsureLayoutFunctionDependencies(variable.Initializer);
                        break;
                    case BoundExpressionStatement value:
                        EnsureLayoutFunctionDependencies(value.Expression);
                        break;
                    case BoundIfStatement conditional:
                        EnsureLayoutFunctionDependencies(conditional.Condition);
                        VisitStatement(conditional.ThenStatement);
                        if (conditional.ElseStatement is not null) VisitStatement(conditional.ElseStatement);
                        break;
                }
            }
        }

        private BoundTemplateLayoutConstruction BindLayoutConstruction(LayoutDeclarationSyntax layout)
        {
            if (layout.Initializer is not null || layout.ComposedLayout is not null)
            {
                Report("COPE-LAYOUT-TEMPLATE-0001", "A layout template requires a spatial body.", layout.Identifier);
            }
            BoundTemplateValue Coordinate(ExpressionSyntax? value)
            {
                if (value is null) return new BoundTemplateLiteral(layout.Identifier, null, PrimitiveTypeSymbol.Error);
                return BindLayoutStaticValue(value, LengthTypeSymbol.Instance);
            }
            BoundLayoutPropertyPlan[] Properties(IReadOnlyList<LayoutPropertySyntax> properties)
                => properties.Select(property => BindProperty(property)).ToArray();
            BoundLayoutPropertyPlan BindProperty(LayoutPropertySyntax property)
            {
                if (property.Identifier.Text is "layer" or "layers")
                {
                    Report("COPE-LAYOUT-TEMPLATE-0001", "Semantic layer references in layout templates are deferred. Declare this layout concretely with its layer set.", property.Identifier);
                    return new BoundLayoutPropertyPlan(property.Identifier,
                        new BoundTemplateLiteral(property.Identifier, null, PrimitiveTypeSymbol.Error));
                }
                return new BoundLayoutPropertyPlan(property.Identifier, BindLayoutStaticValue(property.Value));
            }
            BoundLayoutNodePlan Node(LayoutNodeSyntax node)
            {
                if (node.Conditional is { } conditional)
                {
                    BoundTemplateValue condition = BindLayoutStaticValue(conditional.Condition);
                    if (condition.Type != PrimitiveTypeSymbol.Boolean && condition.Type != PrimitiveTypeSymbol.Error)
                    {
                        Report("COPE-TEMPLATE-0002", "A static layout condition must be boolean.", conditional.StaticKeyword);
                    }
                    return new BoundLayoutNodePlan(node.KindToken, node.Identifier, [],
                        conditional.ThenNodes.Select(Node).ToArray(), condition, conditional.ElseNodes.Select(Node).ToArray());
                }
                if (node.RelativeDerivations?.Count > 0)
                {
                    Report("COPE-LAYOUT-TEMPLATE-0001", "Relative derivations in layout templates are not supported yet.", node.Identifier);
                }
                return new BoundLayoutNodePlan(node.KindToken, node.Identifier, Properties(node.Properties), node.Children.Select(Node).ToArray());
            }
            BoundLayoutTypeDeclaration? contract = null;
            if (layout.ContractIdentifier is { } contractName)
            {
                LayoutDataCompilation types = LayoutDataCompiler.BindDeclarations(_tree, _sourcePath ?? "<memory>",
                    _tree.Root.Members.OfType<LayoutTypeDeclarationSyntax>().Cast<MemberSyntax>().ToArray());
                contract = types.LayoutTypes.GetValueOrDefault(contractName.Text);
                if (contract is null && _scope.TryLookup(contractName.Text, out Symbol? imported)
                    && imported is LayoutTypeSymbol layoutType)
                {
                    contract = layoutType.BoundLayoutType;
                }
                if (contract is null) Report("COPE-LAYOUT-TYPE-0009", $"Layout contract '{contractName.Text}' was not found.", contractName);
            }
            return new BoundTemplateLayoutConstruction(layout.Identifier, layout.Profile?.Text,
                Coordinate(layout.Origin?.X), Coordinate(layout.Origin?.Y), Properties(layout.Properties),
                layout.Nodes.Select(Node).ToArray(), contract, _sourcePath ?? "<memory>");
        }

        private BoundLayoutDeclaration? EvaluateLayoutInitializer(LayoutDeclarationSyntax layout)
        {
            bool previousMode = _bindingLayoutValues;
            _bindingLayoutValues = true;
            try
            {
                int diagnosticCount = _diagnostics.Diagnostics.Count;
                BoundTemplateValue plan = BindTemplateValue(layout.Initializer!);
                if (_diagnostics.Diagnostics.Count != diagnosticCount) return null;
                if (plan.Type != LayoutValueTypeSymbol.Instance && plan.Type != PrimitiveTypeSymbol.Error)
                {
                    Report("COPE-LAYOUT-TEMPLATE-0001", "A layout initializer must instantiate a template returning Layout.", layout.Identifier);
                    return null;
                }
                var diagnostics = new List<Diagnostic>();
                BoundTemplateDeclaration[] available = _templates.Concat(_imports?.Declarations.Values
                    .OfType<TemplateSymbol>().Select(symbol => symbol.Declaration).OfType<BoundTemplateDeclaration>() ?? [])
                    .Distinct().ToArray();
                object? result = TemplateCompiler.EvaluateValue(available, plan, diagnostics, _functions);
                foreach (Diagnostic diagnostic in diagnostics)
                {
                    _diagnostics.Report(diagnostic.Id, diagnostic.Message, diagnostic.Position, diagnostic.Length,
                        diagnostic.SourcePath ?? _sourcePath);
                }
                return diagnostics.Count == 0 ? result as BoundLayoutDeclaration : null;
            }
            finally
            {
                _bindingLayoutValues = previousMode;
            }
        }
    }
}
