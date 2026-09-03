using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>Binds the bounded M3 vertex/pixel profile through ordinary Copeland syntax.</summary>
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
        private readonly Dictionary<string, AliasSource> _aliasSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VdMirSemanticSpace> _semanticSpaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MaterialSource> _materialSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VdMirMaterial> _materials = new(StringComparer.Ordinal);
        private readonly List<VdMirGraphicsResource> _resources = [];
        private readonly Dictionary<string, FunctionSource> _functionSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VdMirFunction> _functions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _activeFunctions = new(StringComparer.Ordinal);
        private readonly List<VdMirGraphicsEntryPoint> _entries = [];
        private VdMirGraphicsProgram? _program;
        private VdMirGraphicsStage? _currentStage;

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
            BindSemanticSpaces();
            BindMaterials();
            foreach (StreamSource stream in _streamSources.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Syntax.Identifier.Position))
            {
                BindStream(stream);
            }
            BindEntries();
            BindResources();
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
                foreach (TypeAliasDeclarationSyntax alias in tree.Root.Members.OfType<TypeAliasDeclarationSyntax>())
                {
                    if (Find(alias.Annotations, "space") is not null && !_aliasSources.TryAdd(alias.Identifier.Text, new AliasSource(source.Path, alias)))
                    {
                        Add("COPE-GPU-SYMBOL-0001", "SDSL-V1509", "symbol", $"Duplicate semantic-space alias '{alias.Identifier.Text}'.", Span(source.Path, alias.Identifier));
                    }
                }
                foreach (RecordDeclarationSyntax record in tree.Root.Members.OfType<RecordDeclarationSyntax>())
                {
                    if (Find(record.Annotations, "material") is not null && !_materialSources.TryAdd(record.Identifier.Text, new MaterialSource(source.Path, record)))
                    {
                        Add("COPE-GPU-SYMBOL-0001", "SDSL-V1509", "symbol", $"Duplicate material '{record.Identifier.Text}'.", Span(source.Path, record.Identifier));
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

        private void BindSemanticSpaces()
        {
            foreach (AliasSource source in _aliasSources.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Syntax.Identifier.Position))
            {
                AnnotationSyntax annotation = Find(source.Syntax.Annotations, "space")!;
                string? space = DottedNameArgument(annotation);
                string? physicalType = source.Syntax.TargetType is IdentifierTypeSyntax identifier ? identifier.Identifier.Text : null;
                if (space is null || !space.Contains('.', StringComparison.Ordinal))
                {
                    Add("COPE-GPU-SPACE-0001", "SDSL-V4120", "semantic-space-declaration", "Semantic-space identity must be one dotted canonical name.", Span(source.Path, annotation));
                    continue;
                }
                if (physicalType is not ("float2" or "float3" or "float4"))
                {
                    Add("COPE-GPU-SPACE-0002", "SDSL-V4120", "semantic-space-declaration", "Semantic-space aliases require float2, float3, or float4 physical storage.", Span(source.Path, source.Syntax.TargetType));
                    continue;
                }
                _semanticSpaces[source.Syntax.Identifier.Text] = new VdMirSemanticSpace(space, physicalType, Span(source.Path, source.Syntax));
            }
        }

        private void BindMaterials()
        {
            foreach (MaterialSource source in _materialSources.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Syntax.Identifier.Position))
            {
                AnnotationSyntax? bindingAnnotation = Find(source.Syntax.Annotations, "binding");
                int? binding = bindingAnnotation is null ? null : NonnegativeInteger(bindingAnnotation);
                if (binding is null)
                {
                    VdMirSourceSpan span = bindingAnnotation is null ? Span(source.Path, source.Syntax) : Span(source.Path, bindingAnnotation);
                    Add("COPE-GPU-MATERIAL-0001", "SDSL-V4114", "material", "A material requires one explicit non-negative @binding.", span);
                    continue;
                }
                var fields = new List<VdMirMaterialField>();
                int offset = 0;
                for (int order = 0; order < source.Syntax.Fields.Count; order++)
                {
                    RecordFieldSyntax field = source.Syntax.Fields[order];
                    string type = BindType(source.Path, field.Type);
                    (int size, int alignment) = MaterialLayout(type);
                    if (size == 0)
                    {
                        Add("COPE-GPU-MATERIAL-0002", "SDSL-V4114", "material", $"Material field '{field.Identifier.Text}' has unsupported GPU type '{type}'.", Span(source.Path, field.Type));
                        continue;
                    }
                    offset = AlignUp(offset, alignment);
                    if (offset / 16 != (offset + size - 1) / 16)
                    {
                        offset = AlignUp(offset, 16);
                    }
                    fields.Add(new VdMirMaterialField(order, field.Identifier.Text, type, PhysicalType(type), offset, size, alignment, Span(source.Path, field)));
                    offset += size;
                }
                if (fields.Count != 2 || fields[0].Name != "tint" || fields[0].Type != "float4" || fields[1].Name != "roughness" || fields[1].Type != "f32")
                {
                    Add("COPE-GPU-MATERIAL-0003", "SDSL-V4114", "material", "The bounded M3 material is exactly 'tint: float4; roughness: f32;' in canonical order.", Span(source.Path, source.Syntax));
                }
                VdMirSourceSpan bindingSource = Span(source.Path, bindingAnnotation!);
                _materials[source.Syntax.Identifier.Text] = new VdMirMaterial(
                    $"material:{source.Syntax.Identifier.Text}", source.Syntax.Identifier.Text, fields, AlignUp(offset, 16), 0, binding.Value,
                    [], Span(source.Path, source.Syntax), bindingSource);
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
                if (bindingAnnotation is not null && NonnegativeInteger(bindingAnnotation) is null)
                {
                    Add("COPE-GPU-BINDING-0001", "SDSL-V4112", "resource-binding", "Binding requires one non-negative integer.", Span(source.Path, bindingAnnotation));
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
                var member = new VdMirStreamMember(
                    order,
                    field.Identifier.Text,
                    type,
                    role,
                    location,
                    builtin,
                    target,
                    interpolation,
                    Span(source.Path, field),
                    metadata is null ? null : Span(source.Path, metadata),
                    PhysicalType(type),
                    SemanticSpace(type));
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

        private void BindResources()
        {
            var claimedBindings = new Dictionary<int, VdMirGraphicsResource>();
            int order = 0;
            foreach (StreamSource source in _streamSources.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Syntax.Identifier.Position))
            {
                VdMirStream stream = _streams[source.Syntax.Identifier.Text];
                if (stream.Role != VdMirStreamRole.Resource)
                {
                    continue;
                }
                for (int index = 0; index < source.Syntax.Fields.Count; index++)
                {
                    RecordFieldSyntax field = source.Syntax.Fields[index];
                    AnnotationSyntax? bindingAnnotation = Find(field.Annotations, "binding");
                    int? binding = bindingAnnotation is null ? null : NonnegativeInteger(bindingAnnotation);
                    if (binding is null)
                    {
                        continue;
                    }
                    string type = stream.Members[index].Type;
                    VdMirGraphicsResourceKind? kind = ResourceKind(type);
                    if (kind is null)
                    {
                        Add("COPE-GPU-RESOURCE-0001", "SDSL-V4115", "resource", $"Resource '{field.Identifier.Text}' must be Texture2D<float4>, Sampler, or the canonical material.", Span(source.Path, field.Type));
                        continue;
                    }
                    string? elementType = kind == VdMirGraphicsResourceKind.Texture2D ? "float4" : null;
                    string? materialId = kind == VdMirGraphicsResourceKind.Material ? _materials[type].Id : null;
                    VdMirGraphicsStage[] visibility = _entries
                        .Where(entry => entry.ResourceStreams?.Contains(stream.Name, StringComparer.Ordinal) == true)
                        .Select(entry => entry.Stage)
                        .Distinct()
                        .OrderBy(stage => stage)
                        .ToArray();
                    var resource = new VdMirGraphicsResource(
                        order++, stream.Name, field.Identifier.Text, kind.Value, type, elementType, VdMirResourceAccess.Readonly,
                        0, binding.Value, visibility, materialId,
                        Span(source.Path, field), Span(source.Path, bindingAnnotation!));
                    if (claimedBindings.TryGetValue(binding.Value, out VdMirGraphicsResource? prior))
                    {
                        Add("COPE-GPU-BINDING-0002", "SDSL-V4112", "resource-binding", $"Resource binding {binding.Value} collides in set 0.", resource.BindingSource, [new VdMirRelatedSpan("First binding is here.", prior.BindingSource)]);
                    }
                    else
                    {
                        claimedBindings.Add(binding.Value, resource);
                    }
                    _resources.Add(resource);
                }
            }
            foreach (VdMirMaterial material in _materials.Values.ToArray())
            {
                VdMirGraphicsResource? owner = _resources.FirstOrDefault(resource => resource.MaterialId == material.Id);
                if (owner is null || owner.Binding != material.Binding)
                {
                    Add("COPE-GPU-MATERIAL-0004", "SDSL-V4114", "material", $"Material '{material.Name}' must appear once at its declared binding {material.Binding} in a resource stream.", material.Source);
                }
                else
                {
                    _materials[material.Name] = material with { Visibility = owner.Visibility };
                }
            }
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
            if (source.Syntax.Parameters.Count == 0)
            {
                Add("COPE-GPU-ENTRY-0004", "SDSL-V4104", "entry-point", "Graphics M3 entries require one stage-value stream parameter and may also declare builtin/resource streams.", Span(source.Path, source.Syntax));
                return;
            }

            var parameterTypes = source.Syntax.Parameters
                .Select(parameter => (Parameter: parameter, Type: BindType(source.Path, parameter.Type)))
                .ToArray();
            var stageParameters = parameterTypes.Where(item => _streams.TryGetValue(item.Type, out VdMirStream? stream) && stream.Role == VdMirStreamRole.StageValue).ToArray();
            var builtinParameters = parameterTypes.Where(item => _streams.TryGetValue(item.Type, out VdMirStream? stream) && stream.Role == VdMirStreamRole.Builtin).ToArray();
            var resourceParameters = parameterTypes.Where(item => _streams.TryGetValue(item.Type, out VdMirStream? stream) && stream.Role == VdMirStreamRole.Resource).ToArray();
            if (stageParameters.Length != 1 || stageParameters.Length + builtinParameters.Length + resourceParameters.Length != parameterTypes.Length)
            {
                Add("COPE-GPU-ENTRY-0004", "SDSL-V4104", "entry-point", "Graphics M3 entries require exactly one stage-value stream and only builtin/resource stream companions.", Span(source.Path, source.Syntax));
                return;
            }

            ParameterSyntax parameter = stageParameters[0].Parameter;
            string inputType = stageParameters[0].Type;
            string outputType = BindType(source.Path, source.Syntax.ReturnType);
            if (!_streams.TryGetValue(inputType, out VdMirStream? input) || input.Role != VdMirStreamRole.StageValue || !_streams.TryGetValue(outputType, out VdMirStream? output) || output.Role != VdMirStreamRole.StageValue)
            {
                Add("COPE-GPU-ENTRY-0005", stage == VdMirGraphicsStage.Vertex ? "SDSL-V4106" : "SDSL-V4108", "entry-point", "Graphics entry input and output must be stage-value streams.", Span(source.Path, source.Syntax));
                return;
            }
            ValidateStageBoundary(stage, input, output, source);
            foreach ((ParameterSyntax _, string builtinType) in builtinParameters)
            {
                ValidateBuiltinStream(stage, _streams[builtinType]);
            }

            var scope = parameterTypes.ToDictionary(item => item.Parameter.Identifier.Text, item => item.Type, StringComparer.Ordinal);
            _currentStage = stage;
            IReadOnlyList<VdMirStatement> statements = BindStatements(source, scope, outputType);
            _currentStage = null;
            var functionParameters = parameterTypes.Select(item => new VdMirParameter(item.Parameter.Identifier.Text, item.Type, null, Span(source.Path, item.Parameter))).ToArray();
            var function = new VdMirFunction(source.Syntax.Identifier.Text, functionParameters, outputType, statements, Span(source.Path, source.Syntax));
            _functions[source.Syntax.Identifier.Text] = function;
            _entries.Add(new VdMirGraphicsEntryPoint(
                source.Syntax.Identifier.Text,
                source.Syntax.Identifier.Text,
                stage,
                inputType,
                outputType,
                Span(source.Path, source.Syntax),
                builtinParameters.Select(item => item.Type).ToArray(),
                resourceParameters.Select(item => item.Type).ToArray()));
        }

        private void ValidateBuiltinStream(VdMirGraphicsStage stage, VdMirStream stream)
        {
            foreach (VdMirStreamMember member in stream.Members)
            {
                bool valid = stage switch
                {
                    VdMirGraphicsStage.Vertex => member.Builtin is "vertex_id" or "instance_id" && member.Type == "u32",
                    VdMirGraphicsStage.Pixel => member.Builtin == "front_face" && member.Type == "bool",
                    _ => false,
                };
                if (!valid)
                {
                    Add("COPE-GPU-BUILTIN-0003", "SDSL-V4109", "builtin", $"Builtin '{member.Builtin}' with type '{member.Type}' is not valid for {stage.ToString().ToLowerInvariant()}.", member.Source);
                }
            }
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
                else if (PhysicalType(positions[0].Type) != "float4" || SemanticSpace(positions[0].Type) is string space && space != "clip.position")
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
                foreach (VdMirStreamMember member in input.Members.Where(member => member.Builtin is not null && !(member.Builtin == "position" && PhysicalType(member.Type) == "float4" && SemanticSpace(member.Type) == "clip.position")))
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
                else if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
                    && assignment.Left is MemberAccessExpressionSyntax materialField)
                {
                    VdMirExpression target = BindExpression(source.Path, materialField.Target, scope);
                    if (_materials.TryGetValue(target.Type, out VdMirMaterial? material))
                    {
                        Add(
                            "COPE-GPU-MATERIAL-0005",
                            "SDSL-V3701",
                            "binding",
                            $"Material field '{materialField.NameToken.Text}' is immutable shader input.",
                            Span(source.Path, assignment.Left),
                            [new VdMirRelatedSpan("Material is declared here.", material.Source)]);
                    }
                    else
                    {
                        Add("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", "Reachable graphics assignment is unsupported.", Span(source.Path, assignment));
                    }
                }
                else if (statement is IfStatementSyntax conditional)
                {
                    VdMirExpression condition = BindExpression(source.Path, conditional.Condition, scope, "bool");
                    if (condition.Type != "bool")
                    {
                        TypeMismatch(source.Path, conditional.Condition, "bool", condition.Type);
                    }
                    IReadOnlyList<VdMirStatement> body = BindBranch(source.Path, conditional.ThenStatement, scope, returnType);
                    IReadOnlyList<VdMirStatement> elseBody = conditional.ElseStatement is null
                        ? []
                        : BindBranch(source.Path, conditional.ElseStatement, scope, returnType);
                    result.Add(new VdMirStatement("if", Span(source.Path, conditional), Expression: condition, Body: body, ElseBody: elseBody));
                }
                else
                {
                    Add("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", $"Reachable '{statement.Kind}' has no graphics M2 semantics.", Span(source.Path, statement));
                }
            }
            _activeFunctions.Remove(source.Syntax.Identifier.Text);
            return result;
        }

        private IReadOnlyList<VdMirStatement> BindBranch(string path, StatementSyntax syntax, Dictionary<string, string> scope, string returnType)
        {
            IReadOnlyList<StatementSyntax> statements = syntax is BlockStatementSyntax block ? block.Statements : [syntax];
            var result = new List<VdMirStatement>();
            foreach (StatementSyntax statement in statements)
            {
                if (statement is not ReturnStatementSyntax { Expression: not null } returned)
                {
                    Add("COPE-GPU-CLOSURE-0001", "SDSL-V4200", "host-only", "Bounded graphics branches support return statements only.", Span(path, statement));
                    continue;
                }
                VdMirExpression expression = BindExpression(path, returned.Expression, scope, returnType);
                if (expression.Type != returnType)
                {
                    TypeMismatch(path, returned.Expression, returnType, expression.Type);
                }
                result.Add(new VdMirStatement("return", Span(path, returned), Expression: expression));
            }
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
                    if (memberType is null && _materials.TryGetValue(target.Type, out VdMirMaterial? material))
                    {
                        memberType = material.Fields.FirstOrDefault(item => item.Name == member.NameToken.Text)?.Type;
                    }
                    if (memberType is null)
                    {
                        Add("COPE-GPU-MEMBER-0001", "SDSL-V1502", "member", $"Member '{member.NameToken.Text}' is not available on '{target.Type}'.", Span(path, member.NameToken));
                        return Error(path, syntax);
                    }
                    return new VdMirExpression("field", memberType, Span(path, member), member.NameToken.Text, [target]);
                }
                case CallExpressionSyntax call:
                    return BindCall(path, call, scope, expected);
                case GenericCallExpressionSyntax call:
                    return BindGenericCall(path, call, scope);
                case BinaryExpressionSyntax binary:
                {
                    VdMirExpression left = BindExpression(path, binary.Left, scope);
                    VdMirExpression right = BindExpression(path, binary.Right, scope);
                    if (binary.OperatorToken.Kind == SyntaxKind.StarToken && left.Type == right.Type && left.Type is "f32" or "float4")
                    {
                        return new VdMirExpression("binary", left.Type, Span(path, binary), "*", [left, right]);
                    }
                    if (binary.OperatorToken.Kind == SyntaxKind.PlusToken && left.Type == right.Type && left.Type is "u32" or "f32")
                    {
                        return new VdMirExpression("binary", left.Type, Span(path, binary), "+", [left, right]);
                    }
                    Add("COPE-GPU-OPERATOR-0001", "SDSL-V1503", "type", $"Operator '{binary.OperatorToken.Text}' is not defined for '{left.Type}' and '{right.Type}' in graphics M3.", Span(path, binary.OperatorToken));
                    return Error(path, binary);
                }
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

        private VdMirExpression BindCall(string path, CallExpressionSyntax call, Dictionary<string, string> scope, string? expected)
        {
            if (call.Target is not NameExpressionSyntax name)
            {
                Add("COPE-GPU-CALL-0001", "SDSL-V4200", "host-only", "Only closed GPU calls are supported.", Span(path, call));
                return Error(path, call);
            }
            string target = name.IdentifierToken.Text;
            VdMirExpression[] arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            if (target == "Sample")
            {
                if (arguments.Length != 3)
                {
                    Add("COPE-GPU-SAMPLE-0001", "SDSL-V4118", "texture-sampling", "Sample expects texture, sampler, and float2 coordinates.", Span(path, call));
                    return Error(path, call);
                }
                if (!TryTextureElement(arguments[0].Type, out string? elementType))
                {
                    Add("COPE-GPU-SAMPLE-0002", "SDSL-V4118", "texture-sampling", "Sample first argument must be Texture2D<T>.", arguments[0].Source);
                }
                if (arguments[1].Type != "Sampler")
                {
                    Add("COPE-GPU-SAMPLE-0003", "SDSL-V4118", "texture-sampling", "Sample second argument must be Sampler.", arguments[1].Source);
                }
                if (arguments[2].Type != "float2")
                {
                    Add("COPE-GPU-SAMPLE-0004", "SDSL-V4119", "texture-sampling", "Sample coordinates must be plain float2.", arguments[2].Source);
                }
                if (_currentStage is not (VdMirGraphicsStage.Vertex or VdMirGraphicsStage.Pixel))
                {
                    Add("COPE-GPU-SAMPLE-0005", "SDSL-V4118", "texture-sampling", "Sample is supported only in vertex and pixel stages.", Span(path, call));
                }
                return new VdMirExpression("intrinsic", elementType ?? "error", Span(path, call), "Sample2D", arguments);
            }
            if (target is "float2" or "float3" or "float4")
            {
                if (!ValidConstructor(target, arguments.Select(argument => PhysicalType(argument.Type)).ToArray()))
                {
                    Add("COPE-GPU-CONSTRUCTOR-0001", "SDSL-V1503", "type", $"No bounded {target} constructor matches these arguments.", Span(path, call));
                    return Error(path, call);
                }
                string resultType = expected is not null && PhysicalType(expected) == target ? expected : target;
                return new VdMirExpression("call", resultType, Span(path, call), target, arguments);
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

        private VdMirExpression BindGenericCall(string path, GenericCallExpressionSyntax call, Dictionary<string, string> scope)
        {
            VdMirExpression[] arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            bool isCanonicalConvert = call.Target is NameExpressionSyntax { IdentifierToken.Text: "Convert" }
                && call.TypeArguments.Count == 1
                && BindType(path, call.TypeArguments[0]) == "f32"
                && arguments.Length == 1
                && arguments[0].Type == "u32";
            if (isCanonicalConvert)
            {
                return new VdMirExpression("intrinsic", "f32", Span(path, call), "ConvertU32ToF32", arguments);
            }
            Add("COPE-GPU-CONVERT-0001", "SDSL-V1503", "type", "The bounded graphics profile supports only Convert<f32>(u32).", Span(path, call));
            return Error(path, call);
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
                    Add("COPE-GPU-LINK-0002", "SDSL-V4111", "stage-linkage", $"Varying location {producer.Location} disagrees in physical type, semantic space, or interpolation.", consumer.Source, [new VdMirRelatedSpan("Vertex output is here.", producer.Source)]);
                    continue;
                }
                links.Add(new VdMirLinkedVarying(producer.Location!.Value, producer.Type, producer.Interpolation, vertexOutput.Name, producer.Name, pixelInput.Name, consumer.Name, producer.PhysicalType, producer.SemanticSpace));
            }
            foreach (VdMirStreamMember consumer in pixelVaryings.Where(consumer => vertexVaryings.All(producer => producer.Location != consumer.Location)))
            {
                Add("COPE-GPU-LINK-0001", "SDSL-V4111", "stage-linkage", $"Pixel input location {consumer.Location} is missing from vertex output.", consumer.Source);
            }
            _program = new VdMirGraphicsProgram(
                "GraphicsProgram",
                vertex.Name,
                pixel.Name,
                links.OrderBy(link => link.Location).ToArray(),
                vertexInput.Members,
                pixelOutput.Members,
                _resources.OrderBy(resource => resource.Order).ToArray(),
                _materials.Values.SingleOrDefault());
        }

        private VdMirGraphicsModule CreateModule()
        {
            return new VdMirGraphicsModule(
                VdMirComputeModule.CurrentSchema,
                VdMirComputeModule.CanonicalConformanceSchema,
                _semanticSpaces.Count > 0 || _resources.Count > 0 || _materials.Count > 0 ? VdMirGraphicsModule.GraphicsM3FeatureLevel : VdMirGraphicsModule.GraphicsM2FeatureLevel,
                _request.Sources.Select(source => source.Path).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                _streams.Values.SelectMany(stream => stream.Members.Select(member => member.Type)).Concat(_materials.Keys).Concat(["f32", "float2", "float3", "float4"]).Distinct(StringComparer.Ordinal).OrderBy(type => type, StringComparer.Ordinal).ToArray(),
                _semanticSpaces.Values.OrderBy(space => space.Name, StringComparer.Ordinal).ToArray(),
                _streams.Values.OrderBy(stream => stream.Name, StringComparer.Ordinal).ToArray(),
                _materials.Values.OrderBy(material => material.Name, StringComparer.Ordinal).ToArray(),
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
                IdentifierTypeSyntax identifier when identifier.Identifier.Text == "Sampler" => "Sampler",
                IdentifierTypeSyntax identifier when _streamSources.ContainsKey(identifier.Identifier.Text) => identifier.Identifier.Text,
                IdentifierTypeSyntax identifier when _semanticSpaces.ContainsKey(identifier.Identifier.Text) => identifier.Identifier.Text,
                IdentifierTypeSyntax identifier when _materials.ContainsKey(identifier.Identifier.Text) => identifier.Identifier.Text,
                GenericTypeSyntax generic when generic.Identifier.Text == "Texture2D" && generic.TypeArguments.Count == 1 && BindType(path, generic.TypeArguments[0]) == "float4" => "Texture2D<float4>",
                _ => null,
            };
            if (type is null)
            {
                Add("COPE-GPU-TYPE-0001", "SDSL-V1502", "type", "Type is not part of the graphics M3 GPU subset.", Span(path, syntax ?? throw new InvalidOperationException("A graphics type is required.")));
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

        private string? VectorMemberType(string type, string member)
        {
            type = PhysicalType(type);
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

        private static string? DottedNameArgument(AnnotationSyntax annotation)
            => annotation.Arguments.Count == 1 ? DottedName(annotation.Arguments[0]) : null;

        private static string? DottedName(ExpressionSyntax expression)
        {
            return expression switch
            {
                NameExpressionSyntax name => name.IdentifierToken.Text,
                MemberAccessExpressionSyntax member when DottedName(member.Target) is string prefix => prefix + "." + member.NameToken.Text,
                _ => null,
            };
        }

        private string PhysicalType(string type)
            => _semanticSpaces.TryGetValue(type, out VdMirSemanticSpace? space) ? space.PhysicalType : type;

        private string? SemanticSpace(string type)
            => _semanticSpaces.TryGetValue(type, out VdMirSemanticSpace? space) ? space.Name : null;

        private VdMirGraphicsResourceKind? ResourceKind(string type)
        {
            if (type == "Texture2D<float4>")
            {
                return VdMirGraphicsResourceKind.Texture2D;
            }
            if (type == "Sampler")
            {
                return VdMirGraphicsResourceKind.Sampler;
            }
            return _materials.ContainsKey(type) ? VdMirGraphicsResourceKind.Material : null;
        }

        private static bool TryTextureElement(string type, out string? elementType)
        {
            if (type == "Texture2D<float4>")
            {
                elementType = "float4";
                return true;
            }
            elementType = null;
            return false;
        }

        private static (int Size, int Alignment) MaterialLayout(string type)
        {
            return type switch
            {
                "float4" => (16, 16),
                "float3" => (12, 16),
                "float2" => (8, 8),
                "f32" or "u32" or "bool" => (4, 4),
                _ => (0, 0),
            };
        }

        private static int AlignUp(int value, int alignment)
            => alignment == 0 ? value : ((value + alignment - 1) / alignment) * alignment;

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

        private sealed record AliasSource(string Path, TypeAliasDeclarationSyntax Syntax);

        private sealed record MaterialSource(string Path, RecordDeclarationSyntax Syntax);

        private sealed record FunctionSource(string Path, FunctionDeclarationSyntax Syntax);
    }
}
