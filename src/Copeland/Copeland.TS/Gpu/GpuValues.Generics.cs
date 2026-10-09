using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Gpu;


internal sealed partial class GpuValues
{
    private readonly Dictionary<string, VdMirValueType> openValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeApplication> applications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> parameterFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypePlan> typePlans = new(StringComparer.Ordinal);
    private readonly HashSet<string> activePlans = new(StringComparer.Ordinal);
    private readonly HashSet<string> closedGenericRecords = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeInterfaceRequirements = new(StringComparer.Ordinal);
    private Dictionary<string, string> typeEnvironment = new(StringComparer.Ordinal);
    private Dictionary<string, VdMirExpression> valueEnvironment = new(StringComparer.Ordinal);
    internal Func<string, ExpressionSyntax, VdMirExpression>? BindStaticExpression { get; set; }
    internal Func<IEnumerable<VdMirFunction>> StaticFunctions { get; set; } = () => [];

    internal sealed record Environment(IReadOnlyDictionary<string, string> Types, IReadOnlyDictionary<string, VdMirExpression> Values);
    private sealed record TypeApplication(
        string Kind,
        string Declaration,
        string? Element,
        IReadOnlyList<VdMirExpression> Dimensions,
        Environment? Arguments = null
    );
    private sealed record TypePlan(
        string Path,
        GenericParameterListSyntax Parameters,
        Environment Open,
        string Target,
        IReadOnlyList<(string Name, string Type, VdMirSourceSpan Source)>? Fields
    );
    public IDisposable Enter(Environment environment)
    {
        var previousTypes = typeEnvironment;
        var previousValues = valueEnvironment;
        typeEnvironment = new(environment.Types, StringComparer.Ordinal);
        valueEnvironment = new(environment.Values, StringComparer.Ordinal);
        return new Restore(() =>
            {
                typeEnvironment = previousTypes;
                valueEnvironment = previousValues;
            });
    }

    public static Environment Empty => new(new Dictionary<string, string>(), new Dictionary<string, VdMirExpression>());

    public bool IsOpen(string type) => parameterFields.ContainsKey(type) || openValues.ContainsKey(type);
    public VdMirExpression? StaticParameter(string name) => valueEnvironment.GetValueOrDefault(name);
    public bool IsOpen(VdMirExpression value) => value.Kind == "generic-static" || (value.Operands ?? []).Any(IsOpen);
    public Environment OpenEnvironment(string path, string identity, GenericParameterListSyntax parameters, Func<string, TypeSyntax, string> bindOther)
    {
        if (parameters.Types.Count + parameters.Values.Count > 8)
        {
            error("COPE-GPU-GENERIC-0001", "A declaration admits at most eight type/static parameters.", Span(path, parameters));
        }

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new Dictionary<string, VdMirExpression>(StringComparer.Ordinal);
        int index = 0;
        foreach (TypeParameterSyntax parameter in parameters.Types)
        {
            string key = StableName("Open", identity + ":" + index++);
            if (!types.TryAdd(parameter.Identifier.Text, key))
            {
                error("COPE-GPU-GENERIC-0001", "Duplicate generic parameter.", Span(path, parameter));
            }

            parameterFields.TryAdd(key, new(StringComparer.Ordinal));
        }

        foreach (TemplateParameterSyntax parameter in parameters.Values)
        {
            string type = bindOther(path, parameter.Type);
            if (type is not ("u32" or "f32" or "bool") || types.ContainsKey(parameter.Identifier.Text) ||
                !values.TryAdd(
                    parameter.Identifier.Text,
                    new("generic-static", type, Span(path, parameter), StableName("Static", identity + ":" + index++))
                ))
            {
                error(
                    "COPE-GPU-GENERIC-0001",
                    "Static parameters require unique names and scalar u32/f32/bool types.",
                    Span(path, parameter)
                );
            }
        }

        var environment = new Environment(types, values);
        using (Enter(environment))
        {
            foreach (TypeParameterSyntax parameter in parameters.Types)
            {
                Dictionary<string, string> fields = parameterFields[types[parameter.Identifier.Text]];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (TypeSyntax requirement in Requirements(parameter))
                {
                    string display = string.Join("", Tokens(requirement).Select(token => token.Text));
                    if (!seen.Add(display))
                    {
                        error("COPE-GPU-REQUIREMENT-0001", "Repeated interface constraint.", Span(path, requirement));
                    }

                    foreach (var field in InterfaceFields(path, requirement, bindOther))
                    {
                        if (fields.TryGetValue(field.Name, out string? previous) && previous != field.Type)
                        {
                            error("COPE-GPU-REQUIREMENT-0001", "Conflicting constraint field: " + field.Name, field.Source);
                        }

                        fields[field.Name] = field.Type;
                    }
                }

                if (fields.Count > 32)
                {
                    error("COPE-GPU-REQUIREMENT-0001", "A parameter admits at most 32 requirement fields.", Span(path, parameter));
                }
            }
        }

        return environment;
    }

    private static IEnumerable<TypeSyntax> Requirements(TypeParameterSyntax parameter) => parameter.RequirementTypes ?? parameter.RequirementNames.Select(name => (TypeSyntax)new IdentifierTypeSyntax(name)).ToArray();
    public Environment Arguments(
        string path,
        GenericParameterListSyntax parameters,
        IReadOnlyList<TypeSyntax> arguments,
        Func<string, TypeSyntax, string> bindOther,
        string? declarationPath = null
    )
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new Dictionary<string, VdMirExpression>(StringComparer.Ordinal);
        int position = 0;
        bool named = false;
        foreach (TypeSyntax argument in arguments)
        {
            if (argument is GenericValueArgumentTypeSyntax { NameToken: { } name } value)
            {
                named = true;
                TemplateParameterSyntax? parameter = parameters.Values.FirstOrDefault(item => item.Identifier.Text == name.Text);
                if (parameter is null || values.ContainsKey(name.Text))
                {
                    error("COPE-GPU-GENERIC-0002", "Unknown or duplicate named static argument: " + name.Text, Span(path, argument));
                    continue;
                }

                values[name.Text] = StaticArgument(path, value, bindOther);
            }
            else if (named || position >= parameters.Types.Count + parameters.Values.Count)
            {
                error(
                    "COPE-GPU-GENERIC-0002",
                    "Positional arguments must precede named arguments and fit the parameter list.",
                    Span(path, argument)
                );
            }
            else if (position < parameters.Types.Count)
            {
                types[parameters.Types[position++].Identifier.Text] = BindType(path, argument, bindOther) ?? bindOther(path, argument);
            }
            else
            {
                values[parameters.Values[position++ - parameters.Types.Count].Identifier.Text] = StaticArgument(path, argument, bindOther);
            }
        }

        return CompleteArguments(declarationPath ?? path, parameters, new(types, values), bindOther);
    }

    public Environment CompleteArguments(string path, GenericParameterListSyntax parameters, Environment arguments, Func<string, TypeSyntax, string> bindOther)
    {
        var types = new Dictionary<string, string>(arguments.Types, StringComparer.Ordinal);
        var values = new Dictionary<string, VdMirExpression>(arguments.Values, StringComparer.Ordinal);
        foreach (TypeParameterSyntax parameter in parameters.Types)
        {
            if (!types.ContainsKey(parameter.Identifier.Text))
            {
                using var context = Enter(new(types, values));
                types[parameter.Identifier.Text] = parameter.DefaultType is null ? Missing(path, parameter.Identifier) : BindType(path, parameter.DefaultType, bindOther) ?? bindOther(path, parameter.DefaultType);
            }
        }

        foreach (TemplateParameterSyntax parameter in parameters.Values)
        {
            if (!values.ContainsKey(parameter.Identifier.Text))
            {
                using var context = Enter(new(types, values));
                values[parameter.Identifier.Text] = parameter.DefaultValue is null ? new("error", Missing(path, parameter.Identifier), Span(path, parameter)) : StaticExpression(path, parameter.DefaultValue);
            }

            string expected = bindOther(path, parameter.Type);
            if (values[parameter.Identifier.Text].Type != expected)
            {
                error("COPE-GPU-GENERIC-0003", "Static argument requires exact type " + expected + ".", Span(path, parameter));
            }
        }

        // Schema order determines identity; caller ordering of named arguments does not.
        var result = new Environment(
            parameters.Types.ToDictionary(parameter => parameter.Identifier.Text, parameter => types[parameter.Identifier.Text], StringComparer.Ordinal),
            parameters.Values.ToDictionary(
                parameter => parameter.Identifier.Text,
                parameter => CanonicalStatic(values[parameter.Identifier.Text]),
                StringComparer.Ordinal
            )
        );
        using (Enter(result))
        {
            foreach (TypeParameterSyntax parameter in parameters.Types)
            {
                string candidate = types[parameter.Identifier.Text];
                string physical = physicalType(candidate);
                if (!IsOpen(candidate) && Find(candidate) is null
                    && physical is not ("f32" or "u32" or "bool" or "uint3" or "float2" or "float3" or "float4" or "float4x4" or "error"))
                {
                    error("COPE-GPU-GENERIC-0003", "Generic type arguments require admitted value types; opaque resources and streams are not generic values.", Span(path, parameter));
                }
                foreach (TypeSyntax requirement in Requirements(parameter))
                {
                    foreach (var field in InterfaceFields(path, requirement, bindOther))
                    {
                        if (MemberType(candidate, field.Name) != field.Type)
                        {
                            error(
                                "COPE-GPU-REQUIREMENT-0002",
                                "Type '" + candidate + "' lacks required field '" + field.Name + ": " + field.Type + "'.",
                                field.Source
                            );
                        }
                    }
                }
            }
        }

        return result;
    }

    private string Missing(string path, SyntaxToken name)
    {
        error(
            "COPE-GPU-GENERIC-0002",
            "Cannot infer parameter '" + name.Text + "'; provide an explicit argument or default.",
            new(path, name.Position, System.Math.Max(1, name.Text.Length))
        );
        return "error";
    }

    private VdMirExpression CanonicalStatic(VdMirExpression value)
    {
        if (value.Kind != "literal")
        {
            return value;
        }

        if (value.Type == "f32" &&
            float.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
        {
            if (!float.IsFinite(number))
            {
                return Fail("COPE-GPU-GENERIC-0003", "Static floating arguments must be finite.", value.Source);
            }

            string text = number.ToString("R", CultureInfo.InvariantCulture);
            if (!text.Contains('.') && !text.Contains('E'))
            {
                text += ".0";
            }
            return value with
            {
                Value = text
            };
        }

        if (value.Type == "u32" && uint.TryParse(value.Value, out uint integer))
        {
            return value with
            {
                Value = integer.ToString(CultureInfo.InvariantCulture)
            };
        }

        return value;
    }

    private VdMirExpression StaticArgument(string path, TypeSyntax syntax, Func<string, TypeSyntax, string> bindOther)
    {
        ExpressionSyntax expression = syntax switch
        {
            GenericValueArgumentTypeSyntax value => value.Expression,
            LiteralTypeSyntax literal => new LiteralExpressionSyntax(literal.LiteralToken),
            IdentifierTypeSyntax identifier => new NameExpressionSyntax(identifier.Identifier),
            _ => new MissingExpressionSyntax(Tokens(syntax).FirstOrDefault() ?? new(SyntaxKind.BadToken, 0, "?", null)),
        };
        return StaticExpression(path, expression);
    }

    private VdMirExpression StaticExpression(string path, ExpressionSyntax syntax)
    {
        if (syntax is NameExpressionSyntax name && StaticParameter(name.IdentifierToken.Text) is { } parameter)
        {
            return parameter;
        }

        if (syntax is ParenthesizedExpressionSyntax parenthesized)
        {
            return StaticExpression(path, parenthesized.Expression);
        }

        if (syntax is LiteralExpressionSyntax literal)
        {
            string type = literal.LiteralToken.Kind is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword ? "bool" : literal.LiteralToken.Value switch
            {
                int => "u32",
                double or float => "f32",
                _ => "error"
            };
            return new("literal", type, Span(path, syntax), literal.LiteralToken.Text);
        }

        VdMirExpression? bound = BindStaticExpression?.Invoke(path, syntax);
        if (bound is null)
        {
            return Fail("COPE-GPU-GENERIC-0003", "Static argument is not a compile-time scalar.", Span(path, syntax));
        }

        return FoldStatic(bound);
    }

    private VdMirExpression FoldStatic(VdMirExpression value)
    {
        if (IsOpen(value) || value.Kind is "literal" or "error")
        {
            return value;
        }

        try
        {
            return GpuStaticEvaluation.Fold(value, StaticFunctions());
        }
        catch (Semantics.StaticEvaluationException exception)
        {
            return Fail("COPE-GPU-GENERIC-0003", exception.Message, value.Source);
        }
    }

    private string? BindGenericType(string path, TypeSyntax? syntax, Func<string, TypeSyntax, string> bindOther)
    {
        if (syntax is IdentifierTypeSyntax identifier &&
            typeEnvironment.TryGetValue(identifier.Identifier.Text, out string? parameter))
        {
            return parameter;
        }

        string? sourceName = syntax switch
        {
            IdentifierTypeSyntax name => name.Identifier.Text,
            GenericTypeSyntax generic => generic.Identifier.Text,
            _ => null
        };
        if (sourceName is null)
        {
            return null;
        }

        string identity = modules.Resolve(path, sourceName);
        GenericParameterListSyntax? parameters = records.GetValueOrDefault(identity).Syntax?.GenericParameters ?? aliases.GetValueOrDefault(identity).Syntax?.GenericParameters;
        if (parameters is not null)
        {
            TypePlan? plan = GetTypePlan(identity, bindOther);
            if (plan is null)
            {
                return "error";
            }

            Environment arguments = Arguments(path, parameters, syntax is GenericTypeSyntax application ? application.TypeArguments : [], bindOther, plan.Path);
            return CloseType(plan.Target, Substitutions(plan.Open, arguments));
        }

        if (modules.Sources.Any(source => source.Tree.Root.Members.OfType<InterfaceDeclarationSyntax>().Any(item => modules.Declare(source.Source.Path, item.Identifier.Text) == identity)))
        {
            return Fail(
                "COPE-GPU-REQUIREMENT-0003",
                "Interfaces are constraint-only and cannot occupy storage or parameter positions.",
                Span(path, syntax!)
            ).Type;
        }

        if (syntax is GenericTypeSyntax shaped &&
            sourceName is "Array" or "NDArray" or "Tensor" or "Vector" or "Matrix")
        {
            string element = shaped.TypeArguments.Count == 0 ? "error" : BindType(path, shaped.TypeArguments[0], bindOther) ?? bindOther(path, shaped.TypeArguments[0]);
            VdMirExpression[] dimensions = shaped.TypeArguments.Skip(1).Select(item => StaticArgument(path, item, bindOther)).ToArray();
            if (dimensions.Any(value => value.Type != "u32"))
            {
                return Fail("COPE-GPU-SHAPE-0001", "Shape extents require exact u32 compile-time arguments.", Span(path, syntax)).Type;
            }

            if (shaped.TypeArguments.Skip(1).Any(item => item is GenericValueArgumentTypeSyntax { NameToken: not null }))
            {
                return Fail(
                    "COPE-GPU-SHAPE-0001",
                    "Built-in shapes use positional extents; name static arguments on authored generic declarations.",
                    Span(path, syntax)
                ).Type;
            }

            if (IsOpen(element) || dimensions.Any(IsOpen))
            {
                return OpenShape(sourceName, element, dimensions, Span(path, syntax));
            }

            // Normalize named and computed scalar dimensions back into the existing layout authority.
            if (shaped.TypeArguments.Skip(1).Any(item => item is not LiteralTypeSyntax))
            {
                TypeSyntax[] normalized = [shaped.TypeArguments[0], .. dimensions.Select(value => (TypeSyntax)new LiteralTypeSyntax(new(SyntaxKind.NumberToken, value.Source.Start, value.Value ?? "0", int.TryParse(value.Value, out int number) ? number : 0)))];
                return BindType(path, shaped with { TypeArguments = normalized }, bindOther);
            }
        }

        return null;
    }

    private TypePlan? GetTypePlan(string identity, Func<string, TypeSyntax, string> bindOther)
    {
        if (typePlans.TryGetValue(identity, out TypePlan? existing))
        {
            return existing;
        }

        var record = records.GetValueOrDefault(identity);
        var alias = aliases.GetValueOrDefault(identity);
        string path = record.Syntax is null ? alias.Path : record.Path;
        SyntaxNode declaration = record.Syntax is null ? alias.Syntax : record.Syntax;
        if (!activePlans.Add(identity) || activePlans.Count > 32)
        {
            error("COPE-GPU-GENERIC-0004", "Recursive or over-budget generic type dependency: " + identity, Span(path, declaration));
            return null;
        }
        GenericParameterListSyntax parameters = (record.Syntax?.GenericParameters ?? alias.Syntax.GenericParameters)!;
        Environment environment = OpenEnvironment(path, identity, parameters, bindOther);
        string target;
        List<(string Name, string Type, VdMirSourceSpan Source)>? fields = null;
        using (Enter(environment))
        {
            if (record.Syntax is null)
            {
                target = BindType(path, alias.Syntax.TargetType, bindOther) ?? bindOther(path, alias.Syntax.TargetType);
            }
            else
            {
                target = StableName("RecordPlan", identity);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in record.Syntax.Fields)
                {
                    if (!seen.Add(field.Identifier.Text) || field.Identifier.Text.StartsWith("Vts", StringComparison.Ordinal))
                    {
                        error("COPE-GPU-VALUE-0001", "Record fields must be unique and cannot use the Vts compiler prefix.", Span(path, field));
                    }
                }
                fields = record.Syntax.Fields.Select(field => (field.Identifier.Text, BindType(path, field.Type, bindOther) ?? bindOther(path, field.Type), Span(path, field))).ToList();
                openValues[target] = new(
                    target,
                    "record",
                    null,
                    [],
                    fields.Select(field => new VdMirValueField(field.Name, field.Type, 0, 0, 0, field.Source)).ToArray(),
                    0,
                    0,
                    "row-major",
                    Span(path, record.Syntax)
                );
                applications[target] = new("record", identity, null, [], environment);
            }
        }

        activePlans.Remove(identity);
        var plan = new TypePlan(path, parameters, environment, target, fields);
        typePlans[identity] = plan;
        return plan;
    }

    private IReadOnlyList<(string Name, string Type, VdMirSourceSpan Source)> InterfaceFields(string path, TypeSyntax syntax, Func<string, TypeSyntax, string> bindOther)
    {
        string name = syntax switch
        {
            IdentifierTypeSyntax identifier => identifier.Identifier.Text,
            GenericTypeSyntax generic => generic.Identifier.Text,
            _ => ""
        };
        string identity = modules.Resolve(path, name);
        foreach (var source in modules.Sources)
        {
            InterfaceDeclarationSyntax? declaration = source.Tree.Root.Members.OfType<InterfaceDeclarationSyntax>().FirstOrDefault(item => modules.Declare(source.Source.Path, item.Identifier.Text) == identity);
            if (declaration is null)
            {
                continue;
            }
            if (!activeInterfaceRequirements.Add(identity) || activeInterfaceRequirements.Count > 32)
            {
                error("COPE-GPU-REQUIREMENT-0001", "Recursive or excessive interface requirement expansion.", Span(path, syntax));
                return [];
            }
            using var requirementContext = new Restore(() => activeInterfaceRequirements.Remove(identity));

            Environment environment = declaration.GenericParameters is null ? Empty : Arguments(
                path,
                declaration.GenericParameters,
                syntax is GenericTypeSyntax generic ? generic.TypeArguments : [],
                bindOther,
                source.Source.Path
            );
            using var context = Enter(environment);
            if (declaration.Fields.Count is < 1 or > 32 ||
                declaration.Fields.Any(field => field.UnsupportedTokens.Count > 0 || !field.HasExplicitType || !field.HasTerminator))
            {
                error(
                    "COPE-GPU-REQUIREMENT-0001",
                    "Interfaces require 1–32 explicitly typed, readable fields.",
                    Span(source.Source.Path, declaration)
                );
            }

            return declaration.Fields.Select(field => (field.Identifier.Text, BindType(source.Source.Path, field.Type, bindOther) ?? bindOther(source.Source.Path, field.Type), Span(source.Source.Path, field))).ToArray();
        }

        error("COPE-GPU-REQUIREMENT-0001", "Unknown interface requirement: " + name, Span(path, syntax));
        return [];
    }

    private string OpenShape(string family, string element, IReadOnlyList<VdMirExpression> dimensions, VdMirSourceSpan source)
    {
        if (dimensions.Count is < 1 or > 4 || family is "Array" or "Vector" && dimensions.Count != 1 ||
            family == "Matrix" && dimensions.Count != 2 ||
            dimensions.Any(value => value.Type != "u32"))
        {
            return Fail("COPE-GPU-SHAPE-0001", "Invalid symbolic shape rank or non-u32 extent.", source).Type;
        }

        string kind = family switch
        {
            "Array" => "array",
            "NDArray" => "ndarray",
            _ => "tensor"
        };
        if (kind == "tensor" && element != "f32")
        {
            return Fail("COPE-GPU-SHAPE-0003", "Tensor arithmetic requires f32 elements.", source).Type;
        }

        string key = kind + "<" + element + "," + string.Join(',', dimensions.Select(ExpressionIdentity)) + ">";
        string name = StableName("ShapePlan", key);
        applications[name] = new(kind, family, element, dimensions);
        openValues[name] = new(
            name,
            kind,
            element,
            dimensions.Select(value => int.TryParse(value.Value, out int extent) ? extent : -1).ToArray(),
            [],
            0,
            0,
            "row-major",
            source
        );
        return name;
    }

    public string CloseType(string type, IReadOnlyDictionary<string, object> substitutions)
    {
        if (substitutions.TryGetValue(type, out object? replacement))
        {
            return (string)replacement;
        }

        if (!applications.TryGetValue(type, out TypeApplication? application) || !IsOpen(type))
        {
            return type;
        }

        if (application.Kind == "record")
        {
            Environment arguments = CloseEnvironment(application.Arguments!, substitutions);
            TypePlan plan = typePlans[application.Declaration];
            string identity = application.Declaration + ArgumentIdentity(arguments);
            string name = StableName("Record", identity);
            if (types.ContainsKey(name) || openValues.ContainsKey(name))
            {
                return name;
            }

            var innerSubstitutions = Substitutions(plan.Open, arguments);
            var fields = plan.Fields!.Select(field => (field.Name, Type: CloseType(field.Type, innerSubstitutions), field.Source)).ToArray();
            if (fields.Any(field => IsOpen(field.Type)))
            {
                openValues[name] = new(
                    name,
                    "record",
                    null,
                    [],
                    fields.Select(field => new VdMirValueField(field.Name, field.Type, 0, 0, 0, field.Source)).ToArray(),
                    0,
                    0,
                    "row-major",
                    openValues[plan.Target].Source
                );
                applications[name] = application with
                {
                    Arguments = arguments
                };
            }
            else
            {
                if (closedGenericRecords.Count >= 128
                    || closedGenericRecords.Count(key => key.StartsWith(application.Declaration + "<", StringComparison.Ordinal)) >= 16)
                {
                    return Fail("COPE-GPU-GENERIC-0004", "Generic records exceed the 16-per-definition/128-total specialization budget.", openValues[plan.Target].Source).Type;
                }
                closedGenericRecords.Add(identity);
                AddType(name, "record", null, [], fields, openValues[plan.Target].Source);
                applications[name] = application with
                {
                    Arguments = arguments
                };
            }

            return name;
        }

        string element = CloseType(application.Element!, substitutions);
        VdMirExpression[] dimensions = application.Dimensions.Select(value => SubstituteStatic(value, substitutions)).ToArray();
        if (IsOpen(element) || dimensions.Any(IsOpen))
        {
            return OpenShape(application.Declaration, element, dimensions, openValues[type].Source);
        }

        int[] extents = dimensions.Select(value => int.TryParse(value.Value, out int extent) ? extent : 0).ToArray();
        if (extents.Any(extent => extent is < 1 or > 256) ||
            extents.Aggregate(1L, (count, extent) => count * extent) > 256)
        {
            return Fail("COPE-GPU-SHAPE-0002", "Specialized shape exceeds positive extent/256-element limits.", openValues[type].Source).Type;
        }

        return Shaped(application.Kind, element, extents, openValues[type].Source);
    }

    public Environment CloseEnvironment(Environment environment, IReadOnlyDictionary<string, object> substitutions) => new(
        environment.Types.ToDictionary(item => item.Key, item => CloseType(item.Value, substitutions), StringComparer.Ordinal),
        environment.Values.ToDictionary(item => item.Key, item => SubstituteStatic(item.Value, substitutions), StringComparer.Ordinal)
    );
    public static Dictionary<string, object> Substitutions(Environment open, Environment closed)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var parameter in open.Types)
        {
            result[parameter.Value] = closed.Types[parameter.Key];
        }

        foreach (var parameter in open.Values)
        {
            result[parameter.Value.Value!] = closed.Values[parameter.Key];
        }

        return result;
    }

    public VdMirExpression SubstituteStatic(VdMirExpression expression, IReadOnlyDictionary<string, object> substitutions)
    {
        if (expression.Kind == "generic-static" &&
            substitutions.TryGetValue(expression.Value!, out object? value))
        {
            return (VdMirExpression)value;
        }

        return FoldStatic(expression with { Operands = expression.Operands?.Select(operand => SubstituteStatic(operand, substitutions)).ToArray() });
    }

    public static string ArgumentIdentity(Environment arguments) => "<" + string.Join(',', arguments.Types.Select(item => item.Key + "=" + item.Value)) + ";" + string.Join(',', arguments.Values.Select(item => item.Key + "=" + ExpressionIdentity(item.Value))) + ">";
    private static string ExpressionIdentity(VdMirExpression value) => value.Kind + ":" + value.Type + ":" + value.Value + "(" + string.Join(',', (value.Operands ?? []).Select(ExpressionIdentity)) + ")";
    internal static string StableName(string kind, string identity) => "Vts" + kind + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    public bool Infer(string pattern, string actual, Dictionary<string, object> bindings)
    {
        if (parameterFields.ContainsKey(pattern))
        {
            return Unify(pattern, actual, bindings);
        }

        if (!applications.TryGetValue(pattern, out TypeApplication? application))
        {
            return pattern == actual;
        }

        if (application.Kind == "record")
        {
            if (!applications.TryGetValue(actual, out TypeApplication? candidate) ||
                candidate.Declaration != application.Declaration)
            {
                return false;
            }

            foreach (var item in application.Arguments!.Types)
            {
                if (!Infer(item.Value, candidate.Arguments!.Types[item.Key], bindings))
                {
                    return false;
                }
            }

            foreach (var item in application.Arguments.Values)
            {
                if (!InferValue(item.Value, candidate.Arguments!.Values[item.Key], bindings))
                {
                    return false;
                }
            }

            return true;
        }

        VdMirValueType? shape = Find(actual);
        if (shape is null || shape.Kind != application.Kind ||
            !Infer(application.Element!, shape.ElementType!, bindings))
        {
            return false;
        }

        IReadOnlyList<VdMirExpression> dimensions = applications.TryGetValue(actual, out TypeApplication? open) ? open.Dimensions : shape.Shape.Select(extent => UInt(extent, shape.Source)).ToArray();
        return dimensions.Count == application.Dimensions.Count &&
        application.Dimensions.Zip(dimensions).All(pair => InferValue(pair.First, pair.Second, bindings));
    }

    private static bool InferValue(VdMirExpression pattern, VdMirExpression actual, Dictionary<string, object> bindings) => pattern.Kind == "generic-static" ? Unify(pattern.Value!, actual, bindings) : ExpressionIdentity(pattern) == ExpressionIdentity(actual);
    private static bool Unify(string key, object actual, Dictionary<string, object> bindings)
    {
        if (!bindings.TryGetValue(key, out object? previous))
        {
            bindings[key] = actual;
            return true;
        }

        return previous is VdMirExpression left && actual is VdMirExpression right ? ExpressionIdentity(left) == ExpressionIdentity(right) : Equals(previous, actual);
    }

    private VdMirExpression? BindOpenLiteral(string expected, ExpressionSyntax syntax, Func<ExpressionSyntax, string, VdMirExpression> bind, VdMirSourceSpan span)
    {
        VdMirValueType? type = Find(expected);
        if (type is null)
        {
            return null;
        }

        var values = new List<VdMirExpression>();
        var names = new List<string>();
        if (type.Kind == "record" && syntax is ObjectLiteralExpressionSyntax record)
        {
            foreach (var property in record.Properties)
            {
                string? field = MemberType(expected, property.NameToken.Text);
                if (field is null || names.Contains(property.NameToken.Text))
                {
                    return Fail("COPE-GPU-VALUE-0003", "Unknown/duplicate generic record field.", span);
                }

                names.Add(property.NameToken.Text);
                VdMirExpression value = bind(property.ValueExpression, field);
                if (value.Type != field)
                {
                    return Fail("COPE-GPU-VALUE-0004", "Generic record field type mismatch.", span);
                }

                values.Add(value);
            }

            if (names.Count != type.Fields.Count)
            {
                return Fail("COPE-GPU-VALUE-0003", "Every generic record field is required.", span);
            }
        }
        else if (type.Kind != "record" && syntax is ArrayLiteralExpressionSyntax array)
        {
            values.AddRange(array.Elements.Select(element => bind(element, type.ElementType!)));
            if (values.Any(value => value.Type != type.ElementType))
            {
                return Fail("COPE-GPU-VALUE-0004", "Generic array element type mismatch.", span);
            }
        }
        else
        {
            return null;
        }

        return new("generic-literal", expected, span, Operands: values, MemberNames: names);
    }

    private VdMirExpression BindOpenWith(
        VdMirExpression subject,
        WithExpressionSyntax syntax,
        Func<ExpressionSyntax, string, VdMirExpression> bind,
        VdMirSourceSpan span
    )
    {
        if (Find(subject.Type) is not { Kind: "record" })
        {
            return Fail("COPE-GPU-VALUE-0006", "with requires a concrete generic record, not an interface constraint.", span);
        }

        var values = new List<VdMirExpression>
        {
            subject
        };
        var names = new List<string>();
        foreach (var property in syntax.Replacements.Properties)
        {
            string? type = MemberType(subject.Type, property.NameToken.Text);
            if (type is null || names.Contains(property.NameToken.Text))
            {
                return Fail("COPE-GPU-VALUE-0003", "Unknown/duplicate with field.", span);
            }

            VdMirExpression value = bind(property.ValueExpression, type);
            if (value.Type != type)
            {
                return Fail("COPE-GPU-VALUE-0004", "with replacement type mismatch.", span);
            }

            values.Add(value);
            names.Add(property.NameToken.Text);
        }

        return new("generic-with", subject.Type, span, Operands: values, MemberNames: names);
    }

    private VdMirExpression? BindOpenRead(VdMirExpression subject, IReadOnlyList<VdMirExpression> indices, VdMirSourceSpan span)
    {
        if (Find(subject.Type) is not { ElementType: { } element } type)
        {
            return null;
        }

        if (indices.Count != type.Shape.Count || indices.Any(index => index.Type != "u32"))
        {
            return Fail("COPE-GPU-INDEX-0002", "Expected one u32 index per symbolic axis.", span);
        }

        return new("generic-read", element, span, Operands: [subject, .. indices]);
    }

    private VdMirExpression? BindOpenWrite(VdMirExpression subject, IReadOnlyList<VdMirExpression> indices, VdMirExpression value, VdMirSourceSpan span)
    {
        if (BindOpenRead(subject, indices, span) is not { } read)
        {
            return null;
        }

        if (read.Type != value.Type)
        {
            return Fail("COPE-GPU-VALUE-0004", "Indexed replacement has the wrong symbolic element type.", span);
        }

        return new("generic-write", subject.Type, span, Operands: [subject, .. indices, value]);
    }

    private VdMirExpression? BindOpenMath(string operation, IReadOnlyList<VdMirExpression> arguments, VdMirSourceSpan span)
    {
        if (arguments.Count != 2 || Find(arguments[0].Type) is not { Kind: "tensor" } left ||
            Find(arguments[1].Type) is not { Kind: "tensor" } right)
        {
            return null;
        }

        string result;
        if (operation == "Dot" && left.Shape.Count == 1 && left.Name == right.Name)
        {
            result = "f32";
        }
        else if (operation is "+" or "-" or "*" && left.Name == right.Name)
        {
            result = left.Name;
        }
        else if (operation == "MatMul" && left.Shape.Count == 2 && right.Shape.Count == 2)
        {
            IReadOnlyList<VdMirExpression> a = Dimensions(left);
            IReadOnlyList<VdMirExpression> b = Dimensions(right);
            if (ExpressionIdentity(a[1]) != ExpressionIdentity(b[0]))
            {
                return Fail("COPE-GPU-TENSOR-0001", "Symbolic matrix inner dimensions must be identical.", span);
            }

            result = OpenShape("Matrix", "f32", [a[0], b[1]], span);
        }
        else
        {
            return Fail("COPE-GPU-TENSOR-0001", "Symbolic tensor operation requires exact compatible shapes.", span);
        }

        return new("generic-math", result, span, operation, arguments);
    }

    private IReadOnlyList<VdMirExpression> Dimensions(VdMirValueType type) => applications.TryGetValue(type.Name, out TypeApplication? application) ? application.Dimensions : type.Shape.Select(extent => UInt(extent, type.Source)).ToArray();
    private VdMirExpression? BindOpenQuery(VdMirExpression subject, string name, VdMirSourceSpan span)
    {
        if (Find(subject.Type) is not { ElementType: not null } type ||
            name is not ("rank" or "length" or "shape"))
        {
            return null;
        }

        string result = name == "shape" ? Shaped("array", "u32", [type.Shape.Count], span) : "u32";
        return new("generic-query", result, span, name, [subject]);
    }

    public VdMirExpression LowerDeferred(VdMirExpression value, Action<VdMirFunction> add)
    {
        IReadOnlyList<VdMirExpression> operands = value.Operands ?? [];
        VdMirSourceSpan source = value.Source;
        switch (value.Kind)
        {
            case "generic-literal":
            VdMirValueType? type = Find(value.Type);
            if (type is null || IsOpen(value.Type))
            {
                return Fail("COPE-GPU-GENERIC-0004", "Unclosed literal type.", source);
            }

            if (type.Kind != "record" && operands.Count != type.Fields.Count)
            {
                return Fail("COPE-GPU-SHAPE-0004", "Specialized array initializer count does not match its shape.", source);
            }

            return Construct(type, operands, add, source, type.Kind == "record" ? value.MemberNames : null);
            case "generic-with":
            VdMirValueType record = Find(value.Type)!;
            string helper = Fresh("With");
            var parameters = operands.Select((operand, index) => new VdMirParameter("v" + index, operand.Type, null, source)).ToArray();
            var fields = record.Fields.Select(field =>
                {
                    int index = value.MemberNames!.ToList().IndexOf(field.Name);
                    return index < 0 ? Field(Name("v0", record.Name, source), field, source) : Name("v" + (index + 1), field.Type, source);
                }).ToArray();
            add(new(
                    helper,
                    parameters,
                    record.Name,
                    [new(
                            "return",
                            source,
                            Expression: new("object", record.Name, source, record.Name, fields, record.Fields.Select(field => field.Name).ToArray())
                        )],
                    source
                ));
            return new("call", record.Name, source, helper, operands);
            case "generic-read":
            return Read(operands[0], operands.Skip(1).ToArray(), add, source)!;
            case "generic-write":
            return Write(operands[0], operands.Skip(1).SkipLast(1).ToArray(), operands[^1], add, source)!;
            case "generic-math":
            return Math(value.Value!, operands, add, source)!;
            case "generic-query":
            return Query(operands[0], value.Value!, add, source)!;
            default:
            return value;
        }
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
