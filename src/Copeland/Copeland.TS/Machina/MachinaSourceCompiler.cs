using Copeland.TS.Diagnostics;
using Copeland.TS.Mir.Machina;
using Copeland.TS.Syntax;
using MachinaFactory = Copeland.TS.Mir.Machina.Machina;

namespace Copeland.TS.MachinaSource;

/// <summary>
/// Bounded source profile that turns ordinary Copeland calls and TS-XML into
/// the compiler-owned Machina MIR. It intentionally does not create a JSX
/// runtime, browser object model, or a second view tree.
/// </summary>
public sealed class MachinaSourceCompilation(
    SyntaxTree syntaxTree,
    IReadOnlyList<Diagnostic> diagnostics,
    MachinaView? view)
{
    public SyntaxTree SyntaxTree { get; } = syntaxTree;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
    public MachinaView? View { get; } = view;
    public bool Success => Diagnostics.Count == 0 && View is not null;
}

public static class MachinaSourceCompiler
{
    public static MachinaSourceCompilation Compile(string sourceText, string sourcePath, string? entryFunction = null)
    {
        SyntaxTree syntaxTree = SyntaxTree.Parse(sourceText, sourcePath);
        var diagnostics = new List<Diagnostic>(syntaxTree.Diagnostics);
        var binder = new ProfileBinder(sourcePath, diagnostics, syntaxTree.Root);
        MachinaView? view = binder.BindEntry(entryFunction);
        return new MachinaSourceCompilation(syntaxTree, diagnostics, diagnostics.Count == 0 ? view : null);
    }

    private sealed class ProfileBinder
    {
        private readonly string _sourcePath;
        private readonly List<Diagnostic> _diagnostics;
        private readonly Dictionary<string, FunctionDeclarationSyntax> _functions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpressionSyntax> _constants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MachinaStyle> _styles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _enumCases = new(StringComparer.Ordinal);
        private ExportDefaultDeclarationSyntax? _defaultExport;

        public ProfileBinder(string sourcePath, List<Diagnostic> diagnostics, CompilationUnitSyntax root)
        {
            _sourcePath = sourcePath;
            _diagnostics = diagnostics;
            foreach (MemberSyntax member in root.Members)
            {
                switch (member)
                {
                    case FunctionDeclarationSyntax function:
                        _functions[function.Identifier.Text] = function;
                        break;
                    case GlobalStatementMemberSyntax { Statement: VariableDeclarationStatementSyntax variable }:
                        _constants[variable.Identifier.Text] = variable.Initializer;
                        break;
                    case EnumDeclarationSyntax @enum:
                        _enumCases[@enum.Identifier.Text] = @enum.Cases
                            .Select(@case => @case.Identifier.Text)
                            .ToHashSet(StringComparer.Ordinal);
                        break;
                    case ExportDefaultDeclarationSyntax exportDefault:
                        _defaultExport = exportDefault;
                        break;
                }
            }
        }

        public MachinaView? BindEntry(string? entryFunction)
        {
            ExpressionSyntax? entry = entryFunction is not null
                ? CreateEntryCall(entryFunction)
                : _defaultExport?.Expression;
            if (entry is null)
            {
                _diagnostics.Add(new Diagnostic(
                    "COPE-MACHINA-VIEW-0001",
                    "Machina source requires an entry function or an export default View expression.",
                    0,
                    1,
                    _sourcePath));
                return null;
            }

            MachinaView? view = BindView(entry);
            if (view is null)
            {
                return null;
            }
            if (view.Kind != MachinaViewKind.Root)
            {
                _diagnostics.Add(new Diagnostic(
                    "COPE-MACHINA-VIEW-0002",
                    "The Machina entry expression must return Root(...).",
                    Span(entry).Start,
                    Span(entry).Length,
                    _sourcePath));
                return null;
            }
            return view;
        }

        private ExpressionSyntax? CreateEntryCall(string entryFunction)
        {
            if (!_functions.TryGetValue(entryFunction, out FunctionDeclarationSyntax? function))
            {
                _diagnostics.Add(new Diagnostic("COPE-MACHINA-VIEW-0003", $"Machina entry function '{entryFunction}' was not found.", 0, Math.Max(1, entryFunction.Length), _sourcePath));
                return null;
            }

            if (function.Parameters.Count != 0)
            {
                _diagnostics.Add(new Diagnostic("COPE-MACHINA-VIEW-0004", "The bounded M1 Machina source profile supports parameterless static entry functions only.", function.Identifier.Position, function.Identifier.Text.Length, _sourcePath));
                return null;
            }

            ReturnStatementSyntax? statement = function.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
            if (statement?.Expression is null)
            {
                _diagnostics.Add(new Diagnostic("COPE-MACHINA-VIEW-0005", $"View function '{entryFunction}' must return a View expression.", function.Identifier.Position, function.Identifier.Text.Length, _sourcePath));
                return null;
            }
            return statement.Expression;
        }

        private MachinaView? BindView(ExpressionSyntax expression)
        {
            if (expression is TsXmlElementExpressionSyntax element)
            {
                return BindTsXml(element);
            }
            if (expression is CallExpressionSyntax call)
            {
                string? name = CallName(call);
                return name switch
                {
                    "Root" => BindRoot(call),
                    "Container" => BindContainer(call),
                    "VStack" => BindStack(call, MachinaAxis.Vertical),
                    "HStack" => BindStack(call, MachinaAxis.Horizontal),
                    "Text" => BindText(call),
                    "Button" => BindButton(call),
                    "Toggle" => BindToggle(call),
                    _ => BindUserViewCall(call, name),
                };
            }

            Report("COPE-MACHINA-VIEW-0006", "Expected a View constructor call or a TS-XML View element.", expression);
            return null;
        }

        private MachinaView? BindRoot(CallExpressionSyntax call)
        {
            if (!RequireArgumentCount(call, 1, "Root")) return null;
            IReadOnlyList<MachinaView>? children = BindChildren(call.Arguments[0]);
            return children is null ? null : MachinaFactory.Root(children, source: Source(call));
        }

        private MachinaView? BindContainer(CallExpressionSyntax call)
        {
            if (!RequireArgumentCount(call, 2, "Container")) return null;
            IReadOnlyList<MachinaView>? children = BindChildren(call.Arguments[0]);
            MachinaOptions? options = BindOptions(call.Arguments[1]);
            if (children is null || options is null || options.Frame is null) return null;
            return MachinaFactory.Container(children, options.Frame, options.Style, options.Offset, Source(call));
        }

        private MachinaView? BindStack(CallExpressionSyntax call, MachinaAxis axis)
        {
            string name = axis == MachinaAxis.Vertical ? "VStack" : "HStack";
            if (!RequireArgumentCount(call, 2, name)) return null;
            IReadOnlyList<MachinaView>? children = BindChildren(call.Arguments[0]);
            MachinaOptions? options = BindOptions(call.Arguments[1]);
            if (children is null || options is null || options.Gap is null) return null;
            return axis == MachinaAxis.Vertical
                ? MachinaFactory.VStack(children, options.Frame, options.Gap.Value, options.Padding, options.Style, options.Offset, options.MainTrack, options.CrossTrack, Source(call))
                : MachinaFactory.HStack(children, options.Frame, options.Gap.Value, options.Padding, options.Style, options.Offset, options.MainTrack, options.CrossTrack, Source(call));
        }

        private MachinaView? BindText(CallExpressionSyntax call)
        {
            if (!RequireArgumentCount(call, 2, "Text")) return null;
            string? text = BindString(call.Arguments[0]);
            MachinaOptions? options = BindOptions(call.Arguments[1]);
            if (text is null || options is null) return null;
            return MachinaFactory.Text(text, options.Frame, options.Style, options.Offset, options.Wrap, options.MainTrack, options.CrossTrack, Source(call));
        }

        private MachinaView? BindButton(CallExpressionSyntax call)
        {
            if (!RequireArgumentCount(call, 3, "Button")) return null;
            string? label = BindString(call.Arguments[0]);
            string? eventName = BindEvent(call.Arguments[1]);
            MachinaOptions? options = BindOptions(call.Arguments[2]);
            if (label is null || eventName is null || options is null) return null;
            return MachinaFactory.Button(label, eventName, options.Frame, options.Style, options.Offset, options.MainTrack, options.CrossTrack, Source(call));
        }

        private MachinaView? BindToggle(CallExpressionSyntax call)
        {
            if (!RequireArgumentCount(call, 3, "Toggle")) return null;
            bool? value = BindBoolean(call.Arguments[0]);
            string? eventName = BindEvent(call.Arguments[1]);
            MachinaOptions? options = BindOptions(call.Arguments[2]);
            if (value is null || eventName is null || options is null) return null;
            return MachinaFactory.Toggle(value.Value, eventName, options.Frame, options.Style, options.Offset, options.MainTrack, options.CrossTrack, Source(call));
        }

        private MachinaView? BindUserViewCall(CallExpressionSyntax call, string? name)
        {
            if (name is null || !_functions.TryGetValue(name, out FunctionDeclarationSyntax? function))
            {
                Report("COPE-MACHINA-VIEW-0007", $"Unknown Machina View function '{name ?? "<expression>"}'.", call.Target);
                return null;
            }
            if (call.Arguments.Count != 0 || function.Parameters.Count != 0)
            {
                Report("COPE-MACHINA-VIEW-0008", $"User View function '{name}' needs parameters; parameterized View composition is deferred in this static M1 profile.", call);
                return null;
            }
            ReturnStatementSyntax? result = function.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
            if (result?.Expression is null)
            {
                Report("COPE-MACHINA-VIEW-0009", $"User View function '{name}' must return a View.", function.Identifier);
                return null;
            }
            return BindView(result.Expression);
        }

        private IReadOnlyList<MachinaView>? BindChildren(ExpressionSyntax expression)
        {
            if (expression is not ArrayLiteralExpressionSyntax array)
            {
                Report("COPE-MACHINA-CHILD-0001", "View children must be a View[] expression.", expression);
                return null;
            }
            var children = new List<MachinaView>();
            foreach (ExpressionSyntax item in array.Elements)
            {
                MachinaView? child = BindView(item);
                if (child is null) return null;
                children.Add(child);
            }
            return children;
        }

        private MachinaOptions? BindOptions(ExpressionSyntax expression)
        {
            if (expression is not ObjectLiteralExpressionSyntax options)
            {
                Report("COPE-MACHINA-OPTION-0001", "Machina widget options must be an object record.", expression);
                return null;
            }

            var result = new MachinaOptions();
            foreach (ObjectPropertySyntax property in options.Properties)
            {
                string key = property.NameToken.Text;
                switch (key)
                {
                    case "frame": result.Frame = BindFrame(property.ValueExpression); break;
                    case "gap": result.Gap = BindLength(property.ValueExpression); break;
                    case "offset": result.Offset = BindOffset(property.ValueExpression); break;
                    case "main": result.MainTrack = BindTrack(property.ValueExpression); break;
                    case "cross": result.CrossTrack = BindTrack(property.ValueExpression); break;
                    case "style": result.Style = BindStyle(property.ValueExpression); break;
                    case "wrap": result.Wrap = BindSymbol(property.ValueExpression) is "TextWrap.Word"; break;
                    case "padding": result.Padding = BindPadding(property.ValueExpression); break;
                    default:
                        Report("COPE-MACHINA-OPTION-0002", $"'{key}' is not a supported Machina widget option.", property.NameToken);
                        break;
                }
            }
            return result;
        }

        private MachinaFrameIntent? BindFrame(ExpressionSyntax expression)
        {
            if (expression is not CallExpressionSyntax call || CallName(call) is not string name)
            {
                Report("COPE-MACHINA-FRAME-0001", "Frame must be Absolute({...}) or Anchor({...}).", expression);
                return null;
            }
            if (!RequireArgumentCount(call, 1, name) || call.Arguments[0] is not ObjectLiteralExpressionSyntax record) return null;
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return name switch
            {
                "Absolute" => BindAbsolute(values, call),
                "Anchor" => BindAnchor(values, call),
                _ => ReportFrameConstructor(call),
            };
        }

        private MachinaFrameIntent? BindAbsolute(Dictionary<string, ExpressionSyntax> values, ExpressionSyntax source)
        {
            MachinaLength? x = RequiredLength(values, "x", source);
            MachinaLength? y = RequiredLength(values, "y", source);
            MachinaLength? width = RequiredLength(values, "width", source);
            MachinaLength? height = RequiredLength(values, "height", source);
            return x is null || y is null || width is null || height is null ? null : MachinaFactory.Absolute(x.Value, y.Value, width.Value, height.Value);
        }

        private MachinaFrameIntent? BindAnchor(Dictionary<string, ExpressionSyntax> values, ExpressionSyntax source)
        {
            return MachinaFactory.Anchor(
                OptionalLength(values, "left"),
                OptionalLength(values, "right"),
                OptionalLength(values, "top"),
                OptionalLength(values, "bottom"),
                OptionalLength(values, "width"),
                OptionalLength(values, "height"));
        }

        private MachinaFrameIntent? ReportFrameConstructor(ExpressionSyntax expression)
        {
            Report("COPE-MACHINA-FRAME-0002", "Only Absolute and Anchor are supported frame constructors.", expression);
            return null;
        }

        private MachinaTrack? BindTrack(ExpressionSyntax expression)
        {
            if (expression is not CallExpressionSyntax call || CallName(call) is not string name)
            {
                Report("COPE-MACHINA-TRACK-0001", "Track must be Fixed(length), Fill(weight?), or Content().", expression);
                return null;
            }
            if (name == "Fixed" && RequireArgumentCount(call, 1, name))
            {
                MachinaLength? length = BindLength(call.Arguments[0]);
                return length is null ? null : MachinaFactory.Fixed(length.Value);
            }
            if (name == "Fill" && call.Arguments.Count is <= 1)
            {
                double weight = call.Arguments.Count == 0 ? 1 : BindNumber(call.Arguments[0]) ?? double.NaN;
                try { return MachinaFactory.Fill(weight); }
                catch (MachinaLayoutException exception) { Report(exception.Code, exception.Message, call); return null; }
            }
            if (name == "Content" && RequireArgumentCount(call, 0, name))
            {
                return MachinaFactory.Content();
            }
            Report("COPE-MACHINA-TRACK-0002", "Track must be Fixed(length), Fill(weight?), or Content().", call);
            return null;
        }

        private MachinaOffset? BindOffset(ExpressionSyntax expression)
        {
            if (expression is not ObjectLiteralExpressionSyntax record)
            {
                Report("COPE-MACHINA-OFFSET-0001", "offset must be an object with x and/or y length fields.", expression);
                return null;
            }
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return new MachinaOffset(OptionalLength(values, "x"), OptionalLength(values, "y"));
        }

        private MachinaInsets BindPadding(ExpressionSyntax expression)
        {
            MachinaLength? length = BindLength(expression);
            if (length is null || length.Value.Ui != 0)
            {
                Report("COPE-MACHINA-PADDING-0001", "padding must be a static px length in M1.", expression);
                return MachinaInsets.None;
            }
            return MachinaInsets.All(length.Value.Px);
        }

        private MachinaStyle? BindStyle(ExpressionSyntax expression)
        {
            if (expression is NameExpressionSyntax name && _styles.TryGetValue(name.IdentifierToken.Text, out MachinaStyle? cached)) return cached;
            if (expression is NameExpressionSyntax styleName && _constants.TryGetValue(styleName.IdentifierToken.Text, out ExpressionSyntax? initializer))
            {
                MachinaStyle? style = BindStyle(initializer);
                if (style is not null) _styles[styleName.IdentifierToken.Text] = style;
                return style;
            }
            if (expression is WithExpressionSyntax withExpression)
            {
                MachinaStyle? basis = BindStyle(withExpression.Source);
                if (basis is null || withExpression.Replacements is null) return null;
                return ApplyStylePatch(basis, withExpression.Replacements);
            }
            if (expression is ObjectLiteralExpressionSyntax record)
            {
                return ApplyStylePatch(MachinaStyle.Empty, record);
            }
            Report("COPE-MACHINA-STYLE-0001", "style must be a static Style record literal, named static Style, or nested with expression.", expression);
            return null;
        }

        private MachinaStyle ApplyStylePatch(MachinaStyle basis, ObjectLiteralExpressionSyntax patch)
        {
            MachinaSurfaceStyle? surface = basis.Surface;
            MachinaTextStyle? text = basis.Text;
            MachinaBorderStyle? border = basis.Border;
            MachinaBoxStyle? box = basis.Box;
            MachinaEffectStyle? effect = basis.Effect;
            foreach (ObjectPropertySyntax property in patch.Properties)
            {
                switch (property.NameToken.Text)
                {
                    case "surface": surface = BindSurface(property.ValueExpression, surface); break;
                    case "text": text = BindTextStyle(property.ValueExpression, text); break;
                    case "border": border = BindBorder(property.ValueExpression, border); break;
                    case "box": box = BindBox(property.ValueExpression, box); break;
                    case "effect": effect = BindEffect(property.ValueExpression, effect); break;
                    default: Report("COPE-MACHINA-STYLE-0002", $"'{property.NameToken.Text}' is not a Machina Style group.", property.NameToken); break;
                }
            }
            return new MachinaStyle(box, surface, text, border, effect);
        }

        private MachinaSurfaceStyle? BindSurface(ExpressionSyntax expression, MachinaSurfaceStyle? basis)
        {
            ObjectLiteralExpressionSyntax? record = StyleRecord(expression, "surface");
            if (record is null) return basis;
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return (basis ?? new MachinaSurfaceStyle()) with
            {
                Fill = OptionalString(values, "fill") ?? basis?.Fill,
                Radius = OptionalLength(values, "radius") ?? basis?.Radius,
            };
        }

        private MachinaTextStyle? BindTextStyle(ExpressionSyntax expression, MachinaTextStyle? basis)
        {
            ObjectLiteralExpressionSyntax? record = StyleRecord(expression, "text");
            if (record is null) return basis;
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return (basis ?? new MachinaTextStyle()) with
            {
                Color = OptionalString(values, "color") ?? basis?.Color,
                Size = OptionalLength(values, "size") ?? basis?.Size,
                Weight = OptionalInt(values, "weight") ?? basis?.Weight,
            };
        }

        private MachinaBorderStyle? BindBorder(ExpressionSyntax expression, MachinaBorderStyle? basis)
        {
            ObjectLiteralExpressionSyntax? record = StyleRecord(expression, "border");
            if (record is null) return basis;
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return (basis ?? new MachinaBorderStyle()) with
            {
                Width = OptionalLength(values, "width") ?? basis?.Width,
                Color = OptionalString(values, "color") ?? basis?.Color,
                Style = OptionalString(values, "style") ?? basis?.Style,
            };
        }

        private MachinaBoxStyle? BindBox(ExpressionSyntax expression, MachinaBoxStyle? basis)
        {
            ObjectLiteralExpressionSyntax? record = StyleRecord(expression, "box");
            if (record is null) return basis;
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return (basis ?? new MachinaBoxStyle()) with { Padding = OptionalLength(values, "padding")?.Px ?? basis?.Padding };
        }

        private MachinaEffectStyle? BindEffect(ExpressionSyntax expression, MachinaEffectStyle? basis)
        {
            ObjectLiteralExpressionSyntax? record = StyleRecord(expression, "effect");
            if (record is null) return basis;
            Dictionary<string, ExpressionSyntax> values = ObjectValues(record);
            return (basis ?? new MachinaEffectStyle()) with { Shadow = OptionalString(values, "shadow") ?? basis?.Shadow };
        }

        private ObjectLiteralExpressionSyntax? StyleRecord(ExpressionSyntax expression, string group)
        {
            if (expression is ObjectLiteralExpressionSyntax record) return record;
            Report("COPE-MACHINA-STYLE-0003", $"Style group '{group}' must be an object record in the bounded M1 source profile.", expression);
            return null;
        }

        private MachinaView? BindTsXml(TsXmlElementExpressionSyntax element)
        {
            string tag = element.NameToken.Text;
            Dictionary<string, ExpressionSyntax> attributes = element.Attributes.ToDictionary(attribute => attribute.NameToken.Text, AttributeExpression, StringComparer.Ordinal);
            return tag switch
            {
                "Root" => MachinaFactory.Root(BindTsXmlChildren(element), source: Source(element)),
                "VStack" => BindTsXmlStack(element, attributes, MachinaAxis.Vertical),
                "HStack" => BindTsXmlStack(element, attributes, MachinaAxis.Horizontal),
                "Text" => BindTsXmlText(element, attributes),
                "Button" => BindTsXmlButton(element, attributes),
                "Toggle" => BindTsXmlToggle(element, attributes),
                _ => BindTsXmlUserFunction(element, tag),
            };
        }

        private MachinaView? BindTsXmlStack(TsXmlElementExpressionSyntax element, Dictionary<string, ExpressionSyntax> attributes, MachinaAxis axis)
        {
            if (!attributes.TryGetValue("frame", out ExpressionSyntax? frameExpression) || !attributes.TryGetValue("gap", out ExpressionSyntax? gapExpression))
            {
                Report("COPE-MACHINA-TSXML-0001", $"<{element.NameToken.Text}> requires frame and gap attributes.", element.NameToken);
                return null;
            }
            MachinaFrameIntent? frame = BindFrame(frameExpression);
            MachinaLength? gap = BindLength(gapExpression);
            if (frame is null || gap is null) return null;
            MachinaStyle? style = attributes.TryGetValue("style", out ExpressionSyntax? styleExpression) ? BindStyle(styleExpression) : null;
            return axis == MachinaAxis.Vertical
                ? MachinaFactory.VStack(BindTsXmlChildren(element), frame, gap.Value, style: style, source: Source(element))
                : MachinaFactory.HStack(BindTsXmlChildren(element), frame, gap.Value, style: style, source: Source(element));
        }

        private MachinaView? BindTsXmlText(TsXmlElementExpressionSyntax element, Dictionary<string, ExpressionSyntax> attributes)
        {
            string? text = TsXmlText(element);
            if (text is null) return null;
            MachinaFrameIntent? frame = attributes.TryGetValue("frame", out ExpressionSyntax? frameExpression) ? BindFrame(frameExpression) : null;
            MachinaTrack? main = attributes.TryGetValue("main", out ExpressionSyntax? mainExpression) ? BindTrack(mainExpression) : null;
            MachinaTrack? cross = attributes.TryGetValue("cross", out ExpressionSyntax? crossExpression) ? BindTrack(crossExpression) : null;
            MachinaOffset? offset = attributes.TryGetValue("offset", out ExpressionSyntax? offsetExpression) ? BindOffset(offsetExpression) : null;
            MachinaStyle? style = attributes.TryGetValue("style", out ExpressionSyntax? styleExpression) ? BindStyle(styleExpression) : null;
            bool wrap = attributes.TryGetValue("wrap", out ExpressionSyntax? wrapExpression) && BindSymbol(wrapExpression) == "TextWrap.Word";
            return MachinaFactory.Text(text, frame, style, offset, wrap, main, cross, Source(element));
        }

        private MachinaView? BindTsXmlButton(TsXmlElementExpressionSyntax element, Dictionary<string, ExpressionSyntax> attributes)
        {
            string? text = TsXmlText(element);
            if (text is null || !attributes.TryGetValue("onClick", out ExpressionSyntax? eventExpression))
            {
                Report("COPE-MACHINA-TSXML-0002", "<Button> requires text content and an onClick attribute.", element.NameToken);
                return null;
            }
            string? eventName = BindEvent(eventExpression);
            MachinaTrack? main = attributes.TryGetValue("main", out ExpressionSyntax? mainExpression) ? BindTrack(mainExpression) : null;
            MachinaTrack? cross = attributes.TryGetValue("cross", out ExpressionSyntax? crossExpression) ? BindTrack(crossExpression) : null;
            MachinaFrameIntent? frame = attributes.TryGetValue("frame", out ExpressionSyntax? frameExpression) ? BindFrame(frameExpression) : null;
            MachinaStyle? style = attributes.TryGetValue("style", out ExpressionSyntax? styleExpression) ? BindStyle(styleExpression) : null;
            return eventName is null ? null : MachinaFactory.Button(text, eventName, frame, style, mainTrack: main, crossTrack: cross, source: Source(element));
        }

        private MachinaView? BindTsXmlToggle(TsXmlElementExpressionSyntax element, Dictionary<string, ExpressionSyntax> attributes)
        {
            if (!attributes.TryGetValue("value", out ExpressionSyntax? valueExpression) || !attributes.TryGetValue("onChange", out ExpressionSyntax? eventExpression))
            {
                Report("COPE-MACHINA-TSXML-0003", "<Toggle> requires value and onChange attributes.", element.NameToken);
                return null;
            }
            bool? value = BindBoolean(valueExpression);
            string? eventName = BindEvent(eventExpression);
            MachinaTrack? main = attributes.TryGetValue("main", out ExpressionSyntax? mainExpression) ? BindTrack(mainExpression) : null;
            return value is null || eventName is null ? null : MachinaFactory.Toggle(value.Value, eventName, mainTrack: main, source: Source(element));
        }

        private MachinaView? BindTsXmlUserFunction(TsXmlElementExpressionSyntax element, string name)
        {
            if (_functions.TryGetValue(name, out FunctionDeclarationSyntax? function) && function.Parameters.Count == 0 && element.Children.Count == 0)
            {
                ReturnStatementSyntax? result = function.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
                return result?.Expression is null ? null : BindView(result.Expression);
            }
            Report("COPE-MACHINA-TSXML-0004", $"Unknown or unsupported View element <{name}>.", element.NameToken);
            return null;
        }

        private IReadOnlyList<MachinaView> BindTsXmlChildren(TsXmlElementExpressionSyntax element)
        {
            var result = new List<MachinaView>();
            foreach (TsXmlChildSyntax child in element.Children)
            {
                switch (child)
                {
                    case TsXmlTextSyntax text when string.IsNullOrWhiteSpace(text.TextToken.Text):
                        break;
                    case TsXmlElementChildSyntax nested:
                        MachinaView? view = BindTsXml(nested.Element as TsXmlElementExpressionSyntax ?? throw new InvalidOperationException());
                        if (view is not null) result.Add(view);
                        break;
                    case TsXmlExpressionChildSyntax expression:
                        MachinaView? expressionView = BindView(expression.Expression);
                        if (expressionView is not null) result.Add(expressionView);
                        break;
                    case TsXmlTextSyntax text:
                        Report("COPE-MACHINA-TSXML-0005", "Text children are allowed only inside <Text> or <Button>.", text.TextToken);
                        break;
                }
            }
            return result;
        }

        private string? TsXmlText(TsXmlElementExpressionSyntax element)
        {
            string text = string.Concat(element.Children.OfType<TsXmlTextSyntax>().Select(child => child.TextToken.Text)).Trim();
            if (text.Length == 0)
            {
                Report("COPE-MACHINA-TSXML-0006", $"<{element.NameToken.Text}> requires non-whitespace text content.", element.NameToken);
                return null;
            }
            if (element.Children.Any(child => child is TsXmlElementChildSyntax or TsXmlExpressionChildSyntax))
            {
                Report("COPE-MACHINA-TSXML-0007", $"<{element.NameToken.Text}> cannot mix text and View children in M1.", element.NameToken);
                return null;
            }
            return text;
        }

        private ExpressionSyntax AttributeExpression(TsXmlAttributeSyntax attribute)
        {
            if (attribute.ExpressionValue is not null) return attribute.ExpressionValue;
            if (attribute.StringValueToken is not null) return new LiteralExpressionSyntax(attribute.StringValueToken);
            Report("COPE-MACHINA-TSXML-0008", $"Attribute '{attribute.NameToken.Text}' requires a value.", attribute.NameToken);
            return new MissingExpressionSyntax(attribute.NameToken);
        }

        private MachinaLength? BindLength(ExpressionSyntax expression)
        {
            try
            {
                return expression switch
                {
                    LiteralExpressionSyntax { LiteralToken.Value: LengthLiteralTokenValue value } => value.Unit switch
                    {
                        "px" => MachinaLength.Pixels(value.Value),
                        "ui" => MachinaLength.Normalized(value.Value),
                        _ => ReportLengthUnit(expression),
                    },
                    UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } unary => BindLength(unary.Operand) is MachinaLength operand ? -operand : null,
                    BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.PlusToken } binary => CombineLength(binary, true),
                    BinaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.MinusToken } binary => CombineLength(binary, false),
                    _ => ReportUnitlessLength(expression),
                };
            }
            catch (MachinaLayoutException exception)
            {
                Report(exception.Code, exception.Message, expression);
                return null;
            }
        }

        private MachinaLength? CombineLength(BinaryExpressionSyntax binary, bool add)
        {
            MachinaLength? left = BindLength(binary.Left);
            MachinaLength? right = BindLength(binary.Right);
            return left is null || right is null ? null : add ? left.Value + right.Value : left.Value - right.Value;
        }

        private MachinaLength? ReportLengthUnit(ExpressionSyntax expression)
        {
            Report("COPE-MACHINA-LENGTH-0001", "Unsupported layout unit.", expression);
            return null;
        }

        private MachinaLength? ReportUnitlessLength(ExpressionSyntax expression)
        {
            Report("COPE-MACHINA-LENGTH-0002", "A Machina layout length requires an explicit px or ui unit; unitless numbers are not coerced.", expression);
            return null;
        }

        private string? BindString(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax { LiteralToken.Value: string value }) return value;
            Report("COPE-MACHINA-VALUE-0001", "Expected a static string value.", expression);
            return null;
        }

        private string? BindSymbol(ExpressionSyntax expression)
            => expression switch
            {
                NameExpressionSyntax name => name.IdentifierToken.Text,
                MemberAccessExpressionSyntax member => (BindSymbol(member.Target) ?? string.Empty) + "." + member.NameToken.Text,
                _ => ReportSymbol(expression),
            };

        private string? BindEvent(ExpressionSyntax expression)
        {
            string? symbol = BindSymbol(expression);
            if (symbol is null) return null;
            int separator = symbol.LastIndexOf('.');
            if (separator <= 0
                || !_enumCases.TryGetValue(symbol[..separator], out HashSet<string>? cases)
                || !cases.Contains(symbol[(separator + 1)..]))
            {
                Report("COPE-MACHINA-EVENT-0001", "Machina event bindings must name a declared enum case such as SettingsEvent.Save.", expression);
                return null;
            }
            return symbol;
        }

        private string? ReportSymbol(ExpressionSyntax expression)
        {
            Report("COPE-MACHINA-VALUE-0002", "Expected a static event, enum, or symbolic value.", expression);
            return null;
        }

        private bool? BindBoolean(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.TrueKeyword }) return true;
            if (expression is LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.FalseKeyword }) return false;
            Report("COPE-MACHINA-VALUE-0003", "Expected a static boolean value.", expression);
            return null;
        }

        private double? BindNumber(ExpressionSyntax expression)
        {
            return expression switch
            {
                LiteralExpressionSyntax { LiteralToken.Value: int value } => value,
                LiteralExpressionSyntax { LiteralToken.Value: double value } => value,
                _ => ReportNumber(expression),
            };
        }

        private double? ReportNumber(ExpressionSyntax expression)
        {
            Report("COPE-MACHINA-VALUE-0004", "Expected a unitless finite number.", expression);
            return null;
        }

        private static Dictionary<string, ExpressionSyntax> ObjectValues(ObjectLiteralExpressionSyntax record)
            => record.Properties.ToDictionary(property => property.NameToken.Text, property => property.ValueExpression, StringComparer.Ordinal);

        private MachinaLength? RequiredLength(Dictionary<string, ExpressionSyntax> values, string name, ExpressionSyntax source)
        {
            if (!values.TryGetValue(name, out ExpressionSyntax? expression))
            {
                Report("COPE-MACHINA-FRAME-0003", $"Frame requires '{name}'.", source);
                return null;
            }
            return BindLength(expression);
        }

        private MachinaLength? OptionalLength(Dictionary<string, ExpressionSyntax> values, string name)
            => values.TryGetValue(name, out ExpressionSyntax? expression) ? BindLength(expression) : null;

        private string? OptionalString(Dictionary<string, ExpressionSyntax> values, string name)
            => values.TryGetValue(name, out ExpressionSyntax? expression) ? BindString(expression) : null;

        private int? OptionalInt(Dictionary<string, ExpressionSyntax> values, string name)
            => values.TryGetValue(name, out ExpressionSyntax? expression) ? (int?)BindNumber(expression) : null;

        private bool RequireArgumentCount(CallExpressionSyntax call, int expected, string name)
        {
            if (call.Arguments.Count == expected) return true;
            Report("COPE-MACHINA-CALL-0001", $"{name} expects {expected} arguments in the bounded M1 source profile.", call);
            return false;
        }

        private static string? CallName(CallExpressionSyntax call)
            => call.Target is NameExpressionSyntax name ? name.IdentifierToken.Text : null;

        private void Report(string id, string message, SyntaxNode node)
        {
            MachinaSourceSpan span = Span(node);
            _diagnostics.Add(new Diagnostic(id, message, span.Start, span.Length, _sourcePath));
        }

        private void Report(string id, string message, SyntaxToken token)
            => _diagnostics.Add(new Diagnostic(id, message, token.Position, Math.Max(1, token.Text.Length), _sourcePath));

        private MachinaSourceSpan Source(SyntaxNode node) => Span(node);

        private MachinaSourceSpan Span(SyntaxNode node)
        {
            SyntaxToken[] tokens = Tokens(node).ToArray();
            if (tokens.Length == 0) return new MachinaSourceSpan(_sourcePath, 0, 1);
            int start = tokens.Min(token => token.Position);
            int end = tokens.Max(token => token.Position + token.Text.Length);
            return new MachinaSourceSpan(_sourcePath, start, Math.Max(1, end - start));
        }

        private static IEnumerable<SyntaxToken> Tokens(SyntaxNode node)
        {
            foreach (object child in node.GetChildren())
            {
                switch (child)
                {
                    case SyntaxToken token: yield return token; break;
                    case SyntaxNode nested:
                        foreach (SyntaxToken token in Tokens(nested)) yield return token;
                        break;
                }
            }
        }

        private sealed class MachinaOptions
        {
            public MachinaFrameIntent? Frame { get; set; }
            public MachinaLength? Gap { get; set; }
            public MachinaOffset? Offset { get; set; }
            public MachinaTrack? MainTrack { get; set; }
            public MachinaTrack? CrossTrack { get; set; }
            public MachinaStyle? Style { get; set; }
            public MachinaInsets Padding { get; set; } = MachinaInsets.None;
            public bool Wrap { get; set; }
        }
    }
}
