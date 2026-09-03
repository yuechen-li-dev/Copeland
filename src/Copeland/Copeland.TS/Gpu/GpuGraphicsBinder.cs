using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>Binds the bounded M2 vertex/pixel profile through ordinary Copeland syntax.</summary>
public static class GpuGraphicsBinder
{
    public static VdMirGraphicsModule Compile(GpuCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Binder(request).Bind();
    }

    private sealed class Binder
    {
        private static readonly HashSet<string> StreamAnnotations = new(StringComparer.Ordinal)
        {
            "location",
            "builtin",
            "target",
            "interpolation",
            "binding",
        };

        private readonly GpuCompilationRequest _request;
        private readonly List<VdMirDiagnostic> _diagnostics = [];
        private readonly Dictionary<string, StreamSource> _streamSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VdMirStream> _streams = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FunctionSource> _functionSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VdMirFunction> _functions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeFunctions = new(StringComparer.Ordinal);
        private readonly List<VdMirGraphicsEntryPoint> _entries = [];
        private VdMirGraphicsProgram? _program;

        public Binder(GpuCompilationRequest request)
        {
            _request = request;
        }

        public VdMirGraphicsModule Bind()
        {
            if (_request.Profile != CopelandCompilerProfile.Gpu)
            {
                Add("COPE-GPU-0001", "SDSL-V4000", "profile", "The GPU binder requires the explicit Gpu compiler profile.", new VdMirSourceSpan(string.Empty, 0, 0));
                return CreateModule();
            }

            ParseSources();
            foreach (StreamSource stream in _streamSources.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Syntax.Identifier.Position))
            {
                BindStream(stream);
            }
            BindEntries();
            LinkProgram();
            return CreateModule();
        }

        private void ParseSources()
        {
            foreach (GpuSourceFile source in _request.Sources.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                SyntaxTree tree = SyntaxTree.Parse(source.Source, source.Path);
                foreach (Copeland.TS.Diagnostics.Diagnostic diagnostic in tree.Diagnostics)
                {
                    Add(diagnostic.Id, "SDSL-V1000", "syntax", diagnostic.Message, new VdMirSourceSpan(source.Path, diagnostic.Position, diagnostic.Length));
                }
                foreach (ShaderStreamDeclarationSyntax stream in tree.Root.Members.OfType<ShaderStreamDeclarationSyntax>())
                {
                    if (!_streamSources.TryAdd(stream.Identifier.Text, new StreamSource(source.Path, stream)))
                    {
                        Add("COPE-GPU-SYMBOL-0001", "SDSL-V1509", "symbol", $"Duplicate stream '{stream.Identifier.Text}'.", Span(source.Path, stream.Identifier));
                    }
                }
                foreach (FunctionDeclarationSyntax function in tree.Root.Members.OfType<FunctionDeclarationSyntax>())
                {
                    if (!_functionSources.TryAdd(function.Identifier.Text, new FunctionSource(source.Path, function)))
                    {
                        Add("COPE-GPU-SYMBOL-0001", "SDSL-V1509", "symbol", $"Duplicate function '{function.Identifier.Text}'.", Span(source.Path, function.Identifier));
                    }
                }
            }
        }

        private void BindStream(StreamSource source)
        {
            var members = new List<VdMirStreamMember>();
            var claimedLocations = new Dictionary<int, VdMirStreamMember>();
            var claimedTargets = new Dictionary<int, VdMirStreamMember>();
            var claimedBuiltins = new Dictionary<string, VdMirStreamMember>(StringComparer.Ordinal);
            var roles = new HashSet<VdMirStreamRole>();
            HashSet<int> reservedLocations = source.Syntax.Fields
                .Select(field => Find(field.Annotations, "location"))
                .Where(annotation => annotation is not null)
                .Select(annotation => NonnegativeInteger(annotation!))
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .ToHashSet();
            int nextLocation = 0;

            for (int order = 0; order < source.Syntax.Fields.Count; order++)
            {
                RecordFieldSyntax field = source.Syntax.Fields[order];
                IReadOnlyList<AnnotationSyntax> annotations = field.Annotations ?? [];
                foreach (AnnotationSyntax annotation in annotations)
                {
                    if (!StreamAnnotations.Contains(annotation.NameToken.Text))
                    {
                        Add("COPE-GPU-ANNOTATION-0001", "SDSL-V4103", "annotation", $"Unknown stream annotation '@{annotation.NameToken.Text}'.", Span(source.Path, annotation.NameToken));
                    }
                }

                AnnotationSyntax? locationAnnotation = Find(annotations, "location");
                AnnotationSyntax? builtinAnnotation = Find(annotations, "builtin");
                AnnotationSyntax? targetAnnotation = Find(annotations, "target");
                AnnotationSyntax? bindingAnnotation = Find(annotations, "binding");
                AnnotationSyntax? interpolationAnnotation = Find(annotations, "interpolation");
                int roleMarkers = new[] { locationAnnotation, builtinAnnotation, targetAnnotation, bindingAnnotation }.Count(annotation => annotation is not null);
                if (roleMarkers > 1)
                {
                    Add("COPE-GPU-STREAM-0001", "SDSL-V4102", "stream-role", $"Stream member '{field.Identifier.Text}' has conflicting role annotations.", Span(source.Path, annotations[1].NameToken), [new VdMirRelatedSpan("First role annotation.", Span(source.Path, annotations[0].NameToken))]);
                }

                string type = BindType(source.Path, field.Type);
                string? builtin = builtinAnnotation is null ? null : NameArgument(builtinAnnotation);
                VdMirStreamRole role = bindingAnnotation is not null
                    ? VdMirStreamRole.Resource
                    : builtinAnnotation is not null && builtin != "position"
                        ? VdMirStreamRole.Builtin
                        : VdMirStreamRole.StageValue;
                roles.Add(role);

                int? location = locationAnnotation is null ? null : NonnegativeInteger(locationAnnotation);
                int? target = targetAnnotation is null ? null : NonnegativeInteger(targetAnnotation);
                string interpolation = interpolationAnnotation is null ? "linear" : NameArgument(interpolationAnnotation) ?? string.Empty;
                if (interpolation is not ("linear" or "flat" or "noperspective"))
                {
                    Add("COPE-GPU-INTERPOLATION-0001", "SDSL-V4105", "graphics-interface", "Interpolation must be linear, flat, or noperspective.", Span(source.Path, interpolationAnnotation!));
                    interpolation = "linear";
                }
                if (locationAnnotation is not null && location is null)
                {
                    Add("COPE-GPU-LOCATION-0001", "SDSL-V4105", "graphics-interface", "Location requires one non-negative integer.", Span(source.Path, locationAnnotation));
                }
                if (targetAnnotation is not null && target is null)
                {
                    Add("COPE-GPU-TARGET-0001", "SDSL-V4108", "graphics-interface", "Target requires one non-negative integer.", Span(source.Path, targetAnnotation));
                }
                if (builtinAnnotation is not null && builtin is null)
                {
                    Add("COPE-GPU-BUILTIN-0001", "SDSL-V4109", "builtin", "Builtin requires one canonical name.", Span(source.Path, builtinAnnotation));
                }

                if (role == VdMirStreamRole.StageValue && target is null && location is null && builtin is null)
                {
                    while (claimedLocations.ContainsKey(nextLocation) || reservedLocations.Contains(nextLocation))
                    {
                        nextLocation++;
                    }
                    location = nextLocation++;
                }

                AnnotationSyntax? metadata = locationAnnotation ?? builtinAnnotation ?? targetAnnotation ?? bindingAnnotation ?? interpolationAnnotation;
                var member = new VdMirStreamMember(order, field.Identifier.Text, type, role, location, builtin, target, interpolation, Span(source.Path, field), metadata is null ? null : Span(source.Path, metadata));
                members.Add(member);
                ClaimInteger(source.Path, member, location, claimedLocations, "COPE-GPU-LOCATION-0002", "SDSL-V4105", "location");
                ClaimInteger(source.Path, member, target, claimedTargets, "COPE-GPU-TARGET-0002", "SDSL-V4108", "target");
                if (builtin is not null)
                {
                    if (claimedBuiltins.TryGetValue(builtin, out VdMirStreamMember? prior))
                    {
                        Add("COPE-GPU-BUILTIN-0002", "SDSL-V4110", "builtin", $"Builtin '{builtin}' is duplicated in stream '{source.Syntax.Identifier.Text}'.", member.Source, [new VdMirRelatedSpan("First builtin is here.", prior.Source)]);
                    }
                    else
                    {
                        claimedBuiltins.Add(builtin, member);
                    }
                }
            }

            if (roles.Count > 1)
            {
                Add("COPE-GPU-STREAM-0002", "SDSL-V4102", "stream-role", $"Stream '{source.Syntax.Identifier.Text}' mixes stage-value, resource, or builtin roles.", Span(source.Path, source.Syntax));
            }
            VdMirStreamRole streamRole = roles.Count == 1 ? roles.Single() : VdMirStreamRole.StageValue;
            _streams[source.Syntax.Identifier.Text] = new VdMirStream($"stream:{source.Syntax.Identifier.Text}", source.Syntax.Identifier.Text, streamRole, members, Span(source.Path, source.Syntax));
        }

        private void BindEntries()
        {
            FunctionSource[] vertex = Entries("vertex");
            FunctionSource[] pixel = Entries("pixel");
            ValidateEntryCount("vertex", vertex);
            ValidateEntryCount("pixel", pixel);
            if (vertex.Length == 1)
            {
                BindEntry(vertex[0], VdMirGraphicsStage.Vertex);
            }
            if (pixel.Length == 1)
            {
                BindEntry(pixel[0], VdMirGraphicsStage.Pixel);
            }
        }

        private FunctionSource[] Entries(string annotation)
            => _functionSources.Values.Where(item => Find(item.Syntax.Annotations, annotation) is not null).OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Syntax.Identifier.Position).ToArray();

        private void ValidateEntryCount(string stage, IReadOnlyList<FunctionSource> entries)
        {
            if (entries.Count == 1)
            {
                return;
            }
            VdMirSourceSpan span = entries.Count > 0 ? Span(entries[0].Path, entries[0].Syntax.Identifier) : new VdMirSourceSpan(_request.Sources.FirstOrDefault()?.Path ?? string.Empty, 0, 0);
            Add("COPE-GPU-ENTRY-0003", "SDSL-V4104", "entry-point", $"Graphics M2 requires exactly one @{stage} entry; found {entries.Count}.", span);
        }

        private void BindEntry(FunctionSource source, VdMirGraphicsStage stage)
        {
            if (source.Syntax.TypeParameters.Count > 0)
            {
                Add("COPE-GPU-MATERIALIZATION-0001", "SDSL-V4113", "materialization", "Graphics entries must be concrete.", Span(source.Path, source.Syntax.Identifier));
            }
            if (source.Syntax.Parameters.Count != 1)
            {
                Add("COPE-GPU-ENTRY-0004", "SDSL-V4104", "entry-point", "Graphics M2 entries require exactly one stage-value stream parameter.", Span(source.Path, source.Syntax));
                return;
            }

            ParameterSyntax parameter = source.Syntax.Parameters[0];
            string inputType = BindType(source.Path, parameter.Type);
            string outputType = BindType(source.Path, source.Syntax.ReturnType);
            if (!_streams.TryGetValue(inputType, out VdMirStream? input) || input.Role != VdMirStreamRole.StageValue || !_streams.TryGetValue(outputType, out VdMirStream? output) || output.Role != VdMirStreamRole.StageValue)
            {
                Add("COPE-GPU-ENTRY-0005", stage == VdMirGraphicsStage.Vertex ? "SDSL-V4106" : "SDSL-V4108", "entry-point", "Graphics entry input and output must be stage-value streams.", Span(source.Path, source.Syntax));
                return;
            }
            ValidateStageBoundary(stage, input, output, source);

            var scope = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [parameter.Identifier.Text] = inputType,
            };
            IReadOnlyList<VdMirStatement> statements = BindStatements(source, scope, outputType);
            var function = new VdMirFunction(source.Syntax.Identifier.Text, [new VdMirParameter(parameter.Identifier.Text, inputType, null, Span(source.Path, parameter))], outputType, statements, Span(source.Path, source.Syntax));
            _functions[source.Syntax.Identifier.Text] = function;
            _entries.Add(new VdMirGraphicsEntryPoint(source.Syntax.Identifier.Text, source.Syntax.Identifier.Text, stage, inputType, outputType, Span(source.Path, source.Syntax)));
        }

        private void ValidateStageBoundary(VdMirGraphicsStage stage, VdMirStream input, VdMirStream output, FunctionSource source)
        {
            if (stage == VdMirGraphicsStage.Vertex)
            {
                VdMirStreamMember[] positions = output.Members.Where(member => member.Builtin == "position").ToArray();
                if (positions.Length != 1)
                {
                    Add("COPE-GPU-CLIP-0001", positions.Length == 0 ? "SDSL-V4106" : "SDSL-V4107", "graphics-interface", $"Vertex output stream '{output.Name}' requires exactly one clip-position builtin.", output.Source);
                }
                else if (positions[0].Type != "float4")
                {
                    Add("COPE-GPU-CLIP-0002", "SDSL-V4110", "builtin", "Clip position must have type float4.", positions[0].Source);
                }
                foreach (VdMirStreamMember builtin in input.Members.Where(member => member.Builtin is not null))
                {
                    Add("COPE-GPU-BUILTIN-0003", "SDSL-V4109", "builtin", $"Builtin '{builtin.Builtin}' is not valid in the bounded vertex input stream.", builtin.Source);
                }
            }
            else
            {
                foreach (VdMirStreamMember member in output.Members)
                {
                    if (member.Target is null || member.Type != "float4")
                    {
                        Add("COPE-GPU-TARGET-0003", "SDSL-V4108", "graphics-interface", "Pixel output members require a target and float4 type.", member.Source);
                    }
                }
                foreach (VdMirStreamMember member in input.Members.Where(member => member.Builtin is not null))
                {
                    Add("COPE-GPU-BUILTIN-0003", "SDSL-V4109", "builtin", $"Builtin '{member.Builtin}' is not valid in the bounded pixel input stream.", member.Source);
                }
            }
        }

        private IReadOnlyList<VdMirStatement> BindStatements(FunctionSource source, Dictionary<string, string> scope, string returnType)
        {
            if (!_activeFunctions.Add(source.Syntax.Identifier.Text))
            {
                Add("COPE-GPU-RECURSION-0001", "SDSL-V4201", "recursion", "Reachable GPU recursion is unsupported.", Span(source.Path, source.Syntax.Identifier));
                return [];
            }
            var result = new List<VdMirStatement>();
            foreach (StatementSyntax statement in source.Syntax.Body.Statements)
            {
                if (statement is ReturnStatementSyntax returned && returned.Expression is not null)
                {
                    VdMirExpression expression = BindExpression(source.Path, returned.Expression, scope, returnType);
                    if (expression.Type != returnType)
                    {
                        TypeMismatch(source.Path, returned.Expression, returnType, expression.Type);
                    }
                    result.Add(new VdMirStatement("return", Span(source.Path, returned), Expression: expression));
                }
                else if (statement is VariableDeclarationStatementSyntax local)
                {
                    string declaredType = BindType(source.Path, local.Type);
                    VdMirExpression initializer = BindExpression(source.Path, local.Initializer, scope, declaredType);
                    if (initializer.Type != declaredType)
                    {
                        TypeMismatch(source.Path, local.Initializer, declaredType, initializer.Type);
                    }
                    scope[local.Identifier.Text] = declaredType;
                    result.Add(new VdMirStatement("local", Span(source.Path, local), local.Identifier.Text, declaredType, local.Keyword.Kind == SyntaxKind.VarKeyword, initializer));
                }
                else
                {
                    Add("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", $"Reachable '{statement.Kind}' has no graphics M2 semantics.", Span(source.Path, statement));
                }
            }
            _activeFunctions.Remove(source.Syntax.Identifier.Text);
            return result;
        }

        private VdMirExpression BindExpression(string path, ExpressionSyntax syntax, Dictionary<string, string> scope, string? expected = null)
        {
            switch (syntax)
            {
                case NameExpressionSyntax name when scope.TryGetValue(name.IdentifierToken.Text, out string? type):
                    return new VdMirExpression("name", type, Span(path, syntax), name.IdentifierToken.Text);
                case LiteralExpressionSyntax literal:
                    return new VdMirExpression("literal", literal.LiteralToken.Text.Contains('.', StringComparison.Ordinal) ? "f32" : "u32", Span(path, literal), literal.LiteralToken.Text);
                case ParenthesizedExpressionSyntax parenthesized:
                    return BindExpression(path, parenthesized.Expression, scope, expected);
                case MemberAccessExpressionSyntax member:
                {
                    VdMirExpression target = BindExpression(path, member.Target, scope);
                    string? memberType = VectorMemberType(target.Type, member.NameToken.Text);
                    if (memberType is null && _streams.TryGetValue(target.Type, out VdMirStream? stream))
                    {
                        memberType = stream.Members.FirstOrDefault(item => item.Name == member.NameToken.Text)?.Type;
                    }
                    if (memberType is null)
                    {
                        Add("COPE-GPU-MEMBER-0001", "SDSL-V1502", "member", $"Member '{member.NameToken.Text}' is not available on '{target.Type}'.", Span(path, member.NameToken));
                        return Error(path, syntax);
                    }
                    return new VdMirExpression("field", memberType, Span(path, member), member.NameToken.Text, [target]);
                }
                case CallExpressionSyntax call:
                    return BindCall(path, call, scope);
                case ObjectLiteralExpressionSyntax literal when expected is not null && _streams.TryGetValue(expected, out VdMirStream? stream):
                    return BindObject(path, literal, scope, stream);
                default:
                    Add("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", $"Reachable '{syntax.Kind}' has no graphics M2 semantics.", Span(path, syntax));
                    return Error(path, syntax);
            }
        }

        private VdMirExpression BindObject(string path, ObjectLiteralExpressionSyntax literal, Dictionary<string, string> scope, VdMirStream stream)
        {
            var values = new List<VdMirExpression>();
            var names = new List<string>();
            foreach (VdMirStreamMember member in stream.Members)
            {
                ObjectPropertySyntax? property = literal.Properties.FirstOrDefault(item => item.NameToken.Text == member.Name);
                if (property is null)
                {
                    Add("COPE-GPU-OBJECT-0001", "SDSL-V1503", "type", $"Stream value '{stream.Name}' is missing member '{member.Name}'.", Span(path, literal));
                    continue;
                }
                VdMirExpression value = BindExpression(path, property.ValueExpression, scope, member.Type);
                if (value.Type != member.Type)
                {
                    TypeMismatch(path, property.ValueExpression, member.Type, value.Type);
                }
                names.Add(member.Name);
                values.Add(value);
            }
            foreach (ObjectPropertySyntax property in literal.Properties.Where(property => stream.Members.All(member => member.Name != property.NameToken.Text)))
            {
                Add("COPE-GPU-OBJECT-0002", "SDSL-V1502", "member", $"Unknown member '{property.NameToken.Text}' for stream '{stream.Name}'.", Span(path, property.NameToken));
            }
            return new VdMirExpression("object", stream.Name, Span(path, literal), stream.Name, values, names);
        }

        private VdMirExpression BindCall(string path, CallExpressionSyntax call, Dictionary<string, string> scope)
        {
            if (call.Target is not NameExpressionSyntax name)
            {
                Add("COPE-GPU-CALL-0001", "SDSL-V4200", "host-only", "Only closed GPU calls are supported.", Span(path, call));
                return Error(path, call);
            }
            string target = name.IdentifierToken.Text;
            VdMirExpression[] arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            if (target is "float2" or "float3" or "float4")
            {
                if (!ValidConstructor(target, arguments.Select(argument => argument.Type).ToArray()))
                {
                    Add("COPE-GPU-CONSTRUCTOR-0001", "SDSL-V1503", "type", $"No bounded {target} constructor matches these arguments.", Span(path, call));
                    return Error(path, call);
                }
                return new VdMirExpression("call", target, Span(path, call), target, arguments);
            }
            if (!_functionSources.TryGetValue(target, out FunctionSource? helper) || Find(helper.Syntax.Annotations, "vertex") is not null || Find(helper.Syntax.Annotations, "pixel") is not null)
            {
                Add("COPE-GPU-CALL-0001", "SDSL-V4200", "host-only", $"Call '{target}' is not a closed GPU helper.", Span(path, call));
                return Error(path, call);
            }
            string returnType = BindType(helper.Path, helper.Syntax.ReturnType);
            if (arguments.Length != helper.Syntax.Parameters.Count)
            {
                Add("COPE-GPU-CALL-0002", "SDSL-V1503", "call", $"Function '{target}' expects {helper.Syntax.Parameters.Count} argument(s).", Span(path, call));
            }
            var helperScope = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < helper.Syntax.Parameters.Count; index++)
            {
                ParameterSyntax parameter = helper.Syntax.Parameters[index];
                string parameterType = BindType(helper.Path, parameter.Type);
                helperScope[parameter.Identifier.Text] = parameterType;
                if (index < arguments.Length && arguments[index].Type != parameterType)
                {
                    TypeMismatch(path, call.Arguments[index], parameterType, arguments[index].Type);
                }
            }
            if (!_functions.ContainsKey(target))
            {
                IReadOnlyList<VdMirStatement> statements = BindStatements(helper, helperScope, returnType);
                _functions[target] = new VdMirFunction(target, helper.Syntax.Parameters.Select(parameter => new VdMirParameter(parameter.Identifier.Text, BindType(helper.Path, parameter.Type), null, Span(helper.Path, parameter))).ToArray(), returnType, statements, Span(helper.Path, helper.Syntax));
            }
            return new VdMirExpression("call", returnType, Span(path, call), target, arguments);
        }

        private void LinkProgram()
        {
            VdMirGraphicsEntryPoint? vertex = _entries.SingleOrDefault(entry => entry.Stage == VdMirGraphicsStage.Vertex);
            VdMirGraphicsEntryPoint? pixel = _entries.SingleOrDefault(entry => entry.Stage == VdMirGraphicsStage.Pixel);
            if (vertex is null || pixel is null)
            {
                return;
            }
            VdMirStream vertexInput = _streams[vertex.InputStream];
            VdMirStream vertexOutput = _streams[vertex.OutputStream];
            VdMirStream pixelInput = _streams[pixel.InputStream];
            VdMirStream pixelOutput = _streams[pixel.OutputStream];
            VdMirStreamMember[] vertexVaryings = vertexOutput.Members.Where(member => member.Location is not null && member.Builtin is null).ToArray();
            VdMirStreamMember[] pixelVaryings = pixelInput.Members.Where(member => member.Location is not null && member.Builtin is null).ToArray();
            var links = new List<VdMirLinkedVarying>();
            foreach (VdMirStreamMember producer in vertexVaryings)
            {
                VdMirStreamMember? consumer = pixelVaryings.FirstOrDefault(member => member.Location == producer.Location);
                if (consumer is null)
                {
                    Add("COPE-GPU-LINK-0001", "SDSL-V4111", "stage-linkage", $"Vertex output location {producer.Location} is missing from pixel input.", producer.Source);
                    continue;
                }
                if (consumer.Type != producer.Type || consumer.Interpolation != producer.Interpolation)
                {
                    Add("COPE-GPU-LINK-0002", "SDSL-V4111", "stage-linkage", $"Varying location {producer.Location} disagrees in type or interpolation.", consumer.Source, [new VdMirRelatedSpan("Vertex output is here.", producer.Source)]);
                    continue;
                }
                links.Add(new VdMirLinkedVarying(producer.Location!.Value, producer.Type, producer.Interpolation, vertexOutput.Name, producer.Name, pixelInput.Name, consumer.Name));
            }
            foreach (VdMirStreamMember consumer in pixelVaryings.Where(consumer => vertexVaryings.All(producer => producer.Location != consumer.Location)))
            {
                Add("COPE-GPU-LINK-0001", "SDSL-V4111", "stage-linkage", $"Pixel input location {consumer.Location} is missing from vertex output.", consumer.Source);
            }
            _program = new VdMirGraphicsProgram("GraphicsProgram", vertex.Name, pixel.Name, links.OrderBy(link => link.Location).ToArray(), vertexInput.Members, pixelOutput.Members, []);
        }

        private VdMirGraphicsModule CreateModule()
        {
            return new VdMirGraphicsModule(
                VdMirComputeModule.CurrentSchema,
                VdMirComputeModule.CanonicalConformanceSchema,
                VdMirGraphicsModule.GraphicsM2FeatureLevel,
                _request.Sources.Select(source => source.Path).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                _streams.Values.SelectMany(stream => stream.Members.Select(member => member.Type)).Concat(["f32", "float2", "float3", "float4"]).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToArray(),
                _streams.Values.OrderBy(stream => stream.Name, StringComparer.Ordinal).ToArray(),
                _functions.Values.OrderBy(function => function.Name, StringComparer.Ordinal).ToArray(),
                _entries.OrderBy(entry => entry.Stage).ToArray(),
                _program,
                _diagnostics.OrderBy(diagnostic => diagnostic.PrimarySpan.File, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.PrimarySpan.Start).ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray());
        }

        private string BindType(string path, TypeSyntax? syntax)
        {
            string? type = syntax switch
            {
                IdentifierTypeSyntax identifier when identifier.Identifier.Text is "f32" or "u32" or "bool" or "float2" or "float3" or "float4" => identifier.Identifier.Text,
                IdentifierTypeSyntax identifier when _streamSources.ContainsKey(identifier.Identifier.Text) => identifier.Identifier.Text,
                _ => null,
            };
            if (type is null)
            {
                Add("COPE-GPU-TYPE-0001", "SDSL-V1502", "type", "Type is not part of the graphics M2 GPU subset.", Span(path, syntax ?? throw new InvalidOperationException("A graphics type is required.")));
                return "error";
            }
            return type;
        }

        private void ClaimInteger(string path, VdMirStreamMember member, int? value, Dictionary<int, VdMirStreamMember> claims, string code, string canonicalCode, string kind)
        {
            if (value is null)
            {
                return;
            }
            if (claims.TryGetValue(value.Value, out VdMirStreamMember? prior))
            {
                Add(code, canonicalCode, "graphics-interface", $"Stream {kind} {value.Value} collides.", member.Source, [new VdMirRelatedSpan($"First {kind} is here.", prior.Source)]);
            }
            else
            {
                claims.Add(value.Value, member);
            }
        }

        private static bool ValidConstructor(string target, IReadOnlyList<string> arguments)
        {
            return target switch
            {
                "float2" => arguments.SequenceEqual(["f32", "f32"]),
                "float3" => arguments.SequenceEqual(["f32", "f32", "f32"]),
                "float4" => arguments.SequenceEqual(["f32", "f32", "f32", "f32"]) || arguments.SequenceEqual(["float2", "f32", "f32"]) || arguments.SequenceEqual(["float3", "f32"]),
                _ => false,
            };
        }

        private static string? VectorMemberType(string type, string member)
        {
            int width = type switch { "float2" => 2, "float3" => 3, "float4" => 4, _ => 0 };
            if (width == 0 || member.Any(character => "xyzw".IndexOf(character) < 0 || "xyzw".IndexOf(character) >= width))
            {
                return null;
            }
            return member.Length switch { 1 => "f32", 2 => "float2", 3 => "float3", 4 => "float4", _ => null };
        }

        private static AnnotationSyntax? Find(IReadOnlyList<AnnotationSyntax>? annotations, string name)
            => annotations?.FirstOrDefault(annotation => annotation.NameToken.Text == name);

        private static string? NameArgument(AnnotationSyntax annotation)
            => annotation.Arguments.Count == 1 && annotation.Arguments[0] is NameExpressionSyntax name ? name.IdentifierToken.Text : null;

        private static int? NonnegativeInteger(AnnotationSyntax annotation)
            => annotation.Arguments.Count == 1 && annotation.Arguments[0] is LiteralExpressionSyntax literal && int.TryParse(literal.LiteralToken.Text, out int value) && value >= 0 ? value : null;

        private void TypeMismatch(string path, SyntaxNode syntax, string expected, string actual)
            => Add("COPE-GPU-TYPE-0002", "SDSL-V1503", "type", $"Expected '{expected}', got '{actual}'.", Span(path, syntax));

        private static VdMirExpression Error(string path, SyntaxNode syntax)
            => new("error", "error", Span(path, syntax));

        private void Add(string code, string canonicalCode, string category, string message, VdMirSourceSpan span, IReadOnlyList<VdMirRelatedSpan>? related = null)
            => _diagnostics.Add(new VdMirDiagnostic(code, canonicalCode, category, message, span, related ?? []));

        private static VdMirSourceSpan Span(string path, SyntaxToken token)
            => new(path, token.Position, Math.Max(1, token.Text.Length));

        private static VdMirSourceSpan Span(string path, SyntaxNode syntax)
        {
            SyntaxToken[] tokens = Tokens(syntax).ToArray();
            if (tokens.Length == 0)
            {
                return new VdMirSourceSpan(path, 0, 0);
            }
            int start = tokens.Min(token => token.Position);
            int end = tokens.Max(token => token.Position + token.Text.Length);
            return new VdMirSourceSpan(path, start, Math.Max(1, end - start));
        }

        private static IEnumerable<SyntaxToken> Tokens(SyntaxNode syntax)
        {
            foreach (object child in syntax.GetChildren())
            {
                if (child is SyntaxToken token)
                {
                    yield return token;
                }
                else if (child is SyntaxNode node)
                {
                    foreach (SyntaxToken nested in Tokens(node))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private sealed record StreamSource(string Path, ShaderStreamDeclarationSyntax Syntax);

        private sealed record FunctionSource(string Path, FunctionDeclarationSyntax Syntax);
    }
}
