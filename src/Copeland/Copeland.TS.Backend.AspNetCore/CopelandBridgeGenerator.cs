using System.Text.Json;
using System.Text.Json.Serialization;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Mir;

namespace Copeland.TS.Backend.AspNetCore;

public sealed record CopelandBridgeGeneration(
    string ContractJson,
    string EndpointSource,
    IReadOnlyDictionary<string, string> Routes);

public static class CopelandBridgeGenerator
{
    public static CopelandBridgeGeneration Generate(MirProjectGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var operations = new List<BridgeOperation>();
        foreach (MirProjectModule module in graph.Modules.OrderBy(module => module.Id.Value, StringComparer.Ordinal))
        {
            foreach (MirFunction function in module.Functions
                .Where(function => function.IsRemote)
                .OrderBy(function => function.Name, StringComparer.Ordinal))
            {
                MirModuleExport? export = module.Exports.FirstOrDefault(candidate => candidate.RuntimeName == function.Name);
                if (export is null)
                {
                    throw new InvalidOperationException(
                        $"Remote function '{function.Name}' in module '{module.Id}' must be exported.");
                }

                operations.Add(CreateOperation(graph.AggregateProgram, module, export, function));
            }
        }

        EnsureUnique(operations.Select(operation => operation.Id), "operation identity");
        EnsureUnique(operations.Select(operation => operation.Route), "route");

        var contract = new BridgeContract(
            MirBridgeContract.SchemaVersion,
            operations.Select(operation => operation.ToContract()).ToArray());
        string contractJson = JsonSerializer.Serialize(contract, JsonOptions);
        string endpointSource = EmitEndpointSource(operations);
        IReadOnlyDictionary<string, string> routes = operations.ToDictionary(
            operation => operation.Function.Name,
            operation => operation.Route,
            StringComparer.Ordinal);
        return new CopelandBridgeGeneration(contractJson, endpointSource, routes);
    }

    private static BridgeOperation CreateOperation(
        MirProgram program,
        MirProjectModule module,
        MirModuleExport export,
        MirFunction function)
    {
        if (function.IsAsync || function.IsGenerator || function.Parameters.Count != 1)
        {
            throw new InvalidOperationException(
                $"Remote operation '{module.Id}/{export.Name}' must have exactly one synchronous request parameter.");
        }

        if (function.Parameters[0].Type is not MirRecordType requestType)
        {
            throw new InvalidOperationException(
                $"Remote operation '{module.Id}/{export.Name}' requires a nominal record request.");
        }

        MirRecordDefinition request = program.Records.SingleOrDefault(
            candidate => candidate.Id == requestType.RecordTypeId)
            ?? throw new InvalidOperationException(
                $"Remote operation '{module.Id}/{export.Name}' request record is not defined.");

        if (function.ReturnType is not MirResultType
            {
                SuccessType: MirType { Identifier: "string" },
                ErrorType: MirRecordType errorType
            })
        {
            throw new InvalidOperationException(
                $"Remote operation '{module.Id}/{export.Name}' must return string ! <record-error>.");
        }

        MirRecordDefinition error = program.Records.SingleOrDefault(
            candidate => candidate.Id == errorType.RecordTypeId)
            ?? throw new InvalidOperationException(
                $"Remote operation '{module.Id}/{export.Name}' error record is not defined.");

        ValidateRecord(request, $"request '{module.Id}/{export.Name}'");
        ValidateRecord(error, $"error '{module.Id}/{export.Name}'");
        if (error.Fields.Count != 2
            || error.Fields[0].Name != "kind"
            || error.Fields[0].Type.Identifier != "string"
            || error.Fields[1].Name != "message"
            || error.Fields[1].Type.Identifier != "string")
        {
            throw new InvalidOperationException(
                $"Bridge M0 errors must be the nominal record {{ kind: string; message: string; }}; '{error.Name}' has a different shape.");
        }
        return new BridgeOperation(
            MirBridgeContract.CreateOperationId(module.Id, export.Name),
            MirBridgeContract.CreateRoute(module.Id, export.Name),
            module.Id,
            export.Name,
            function,
            request,
            error);
    }

    private static void ValidateRecord(MirRecordDefinition record, string description)
    {
        foreach (MirRecordFieldDefinition field in record.Fields)
        {
            if (field.Type.Identifier is not ("string" or "int" or "boolean"))
            {
                throw new InvalidOperationException(
                    $"Bridge M0 supports only string, int, and bool fields; {description} contains '{field.Name}: {field.Type.Name}'.");
            }
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string description)
    {
        string[] duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate bridge {description}: {string.Join(", ", duplicates)}.");
        }
    }

    private static string EmitEndpointSource(IReadOnlyList<BridgeOperation> operations)
    {
        var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        writer.WriteLine("// <auto-generated />");
        writer.WriteLine("#nullable enable");
        writer.WriteLine("using System.Text.Json;");
        writer.WriteLine("using System.Text.Json.Serialization;");
        writer.WriteLine("using Microsoft.AspNetCore.Builder;");
        writer.WriteLine("using Microsoft.AspNetCore.Http;");
        writer.WriteLine("using Microsoft.AspNetCore.Routing;");
        writer.WriteLine("using Copeland.Generated;");
        writer.WriteLine();
        writer.WriteLine("namespace Copeland.Generated.Bridge;");
        writer.WriteLine();
        writer.WriteLine("public static class GeneratedCopelandBridgeEndpoints");
        writer.WriteLine("{");
        writer.WriteLine("    private const int SchemaVersion = 1;");
        writer.WriteLine("    private const long MaxRequestBytes = 64 * 1024;");
        writer.WriteLine();
        writer.WriteLine("    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder endpoints)");
        writer.WriteLine("    {");
        foreach (BridgeOperation operation in operations)
        {
            EmitOperation(writer, operation);
        }
        writer.WriteLine("        return endpoints;");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine("    private static IResult BadRequest(string kind, string message)");
        writer.WriteLine("        => Results.Json(new BridgeFailure<BridgeError>(SchemaVersion, false, new BridgeError(kind, message)), statusCode: StatusCodes.Status400BadRequest);");
        writer.WriteLine();
        writer.WriteLine("    private static IResult UnsupportedMediaType()");
        writer.WriteLine(@"        => Results.Json(new BridgeFailure<BridgeError>(SchemaVersion, false, new BridgeError(""unsupported-content-type"", ""The bridge accepts application/json only."")), statusCode: StatusCodes.Status415UnsupportedMediaType);");
        writer.WriteLine();
        writer.WriteLine("    private static IResult RequestTooLarge()");
        writer.WriteLine(@"        => Results.Json(new BridgeFailure<BridgeError>(SchemaVersion, false, new BridgeError(""request-too-large"", ""The bridge request exceeds the M0 size limit."")), statusCode: StatusCodes.Status413PayloadTooLarge);");
        writer.WriteLine();
        writer.WriteLine("    private static IResult ServerFailure(Exception exception)");
        writer.WriteLine("    {");
        writer.WriteLine(@"        Console.Error.WriteLine($""Copeland bridge CLR operation failed: {exception}"");");
        writer.WriteLine(@"        return Results.Json(new BridgeFailure<BridgeError>(SchemaVersion, false, new BridgeError(""server-exception"", ""The remote operation failed."")), statusCode: StatusCodes.Status500InternalServerError);");
        writer.WriteLine("    }");
        writer.WriteLine();
        writer.WriteLine("    private sealed record BridgeSuccess<T>(");
        writer.WriteLine(@"        [property: JsonPropertyName(""schemaVersion"")] int SchemaVersion,");
        writer.WriteLine(@"        [property: JsonPropertyName(""ok"")] bool Ok,");
        writer.WriteLine(@"        [property: JsonPropertyName(""value"")] T Value);");
        writer.WriteLine();
        writer.WriteLine("    private sealed record BridgeFailure<T>(");
        writer.WriteLine(@"        [property: JsonPropertyName(""schemaVersion"")] int SchemaVersion,");
        writer.WriteLine(@"        [property: JsonPropertyName(""ok"")] bool Ok,");
        writer.WriteLine(@"        [property: JsonPropertyName(""error"")] T Error);");
        writer.WriteLine();
        writer.WriteLine("    public sealed record BridgeError(");
        writer.WriteLine(@"        [property: JsonPropertyName(""kind"")] string Kind,");
        writer.WriteLine(@"        [property: JsonPropertyName(""message"")] string Message);");
        foreach (BridgeOperation operation in operations)
        {
            EmitRequestDto(writer, operation);
        }
        writer.WriteLine("}");
        return writer.ToString();
    }

    private static void EmitOperation(StringWriter writer, BridgeOperation operation)
    {
        string requestDto = operation.RequestDtoName;
        string requestType = CSharpBackend.GeneratedRecordTypeName(operation.Request.Id);
        string functionName = CSharpBackend.GeneratedFunctionName(operation.Function.Name);
        writer.WriteLine($"        endpoints.MapPost({ToCSharpLiteral(operation.Route)}, async (HttpContext http, CancellationToken cancellationToken) =>");
        writer.WriteLine("        {");
        writer.WriteLine(@"            if (http.Request.ContentType is null || !http.Request.ContentType.StartsWith(""application/json"", StringComparison.OrdinalIgnoreCase))");
        writer.WriteLine("            {");
        writer.WriteLine("                return UnsupportedMediaType();");
        writer.WriteLine("            }");
        writer.WriteLine("            if (http.Request.ContentLength is > MaxRequestBytes)");
        writer.WriteLine("            {");
        writer.WriteLine("                return RequestTooLarge();");
        writer.WriteLine("            }");
        writer.WriteLine($"            {requestDto}? dto;");
        writer.WriteLine("            try");
        writer.WriteLine("            {");
        writer.WriteLine($"                dto = await JsonSerializer.DeserializeAsync<{requestDto}>(http.Request.Body, cancellationToken: cancellationToken);");
        writer.WriteLine("            }");
        writer.WriteLine("            catch (JsonException)");
        writer.WriteLine("            {");
        writer.WriteLine(@"                return BadRequest(""malformed-request"", ""The request is not valid JSON for the bridge contract."");");
        writer.WriteLine("            }");
        writer.WriteLine("            if (dto is null)");
        writer.WriteLine("            {");
        writer.WriteLine(@"                return BadRequest(""malformed-request"", ""The request body is required."");");
        writer.WriteLine("            }");
        foreach (MirRecordFieldDefinition field in operation.Request.Fields)
        {
            string property = CSharpBackend.GeneratedFunctionName(field.Name);
            writer.WriteLine($"            if (dto.{property} is null)");
            writer.WriteLine("            {");
            writer.WriteLine($@"                return BadRequest(""malformed-request"", ""The request field '{field.Name}' is required."");");
            writer.WriteLine("            }");
        }
        string arguments = string.Join(", ", operation.Request.Fields.Select(field =>
            field.Type.Identifier == "string"
                ? $"dto.{CSharpBackend.GeneratedFunctionName(field.Name)}!"
                : $"dto.{CSharpBackend.GeneratedFunctionName(field.Name)}!.Value"));
        string errorKind = CSharpBackend.GeneratedRecordFieldName(operation.Error.Fields[0].Id);
        string errorMessage = CSharpBackend.GeneratedRecordFieldName(operation.Error.Fields[1].Id);
        writer.WriteLine($"            {requestType} request = new({arguments});");
        writer.WriteLine("            try");
        writer.WriteLine("            {");
        writer.WriteLine($"                CopeResult<string, {CSharpBackend.GeneratedRecordTypeName(operation.Error.Id)}> result = CopelandModule.{functionName}(request);");
        writer.WriteLine("                if (result.IsOk)");
        writer.WriteLine("                {");
        writer.WriteLine("                    return Results.Json(new BridgeSuccess<string>(SchemaVersion, true, result.Value));");
        writer.WriteLine("                }");
        writer.WriteLine($"                return Results.Json(new BridgeFailure<BridgeError>(SchemaVersion, false, new BridgeError(result.Error.{errorKind}, result.Error.{errorMessage})), statusCode: StatusCodes.Status422UnprocessableEntity);");
        writer.WriteLine("            }");
        writer.WriteLine("            catch (Exception exception)");
        writer.WriteLine("            {");
        writer.WriteLine("                return ServerFailure(exception);");
        writer.WriteLine("            }");
        writer.WriteLine("        });");
        writer.WriteLine();
    }

    private static void EmitRequestDto(StringWriter writer, BridgeOperation operation)
    {
        writer.WriteLine($"    private sealed record {operation.RequestDtoName}(");
        for (int index = 0; index < operation.Request.Fields.Count; index++)
        {
            MirRecordFieldDefinition field = operation.Request.Fields[index];
            string comma = index == operation.Request.Fields.Count - 1 ? string.Empty : ",";
            string type = field.Type.Identifier switch
            {
                "string" => "string?",
                "int" => "int?",
                "boolean" => "bool?",
                _ => throw new InvalidOperationException($"Unsupported bridge DTO type '{field.Type.Name}'."),
            };
            writer.WriteLine($"        [property: JsonPropertyName({ToCSharpLiteral(field.Name)})] {type} {CSharpBackend.GeneratedFunctionName(field.Name)}{comma}");
        }
        writer.WriteLine("    );");
        writer.WriteLine();
    }

    private static string ToCSharpLiteral(string value)
        => JsonSerializer.Serialize(value).Replace("\\/", "/", StringComparison.Ordinal);

    private sealed class BridgeOperation(
        string id,
        string route,
        MirModuleId moduleId,
        string authoredName,
        MirFunction function,
        MirRecordDefinition request,
        MirRecordDefinition error)
    {
        public string Id { get; } = id;
        public string Route { get; } = route;
        public MirModuleId ModuleId { get; } = moduleId;
        public string AuthoredName { get; } = authoredName;
        public MirFunction Function { get; } = function;
        public MirRecordDefinition Request { get; } = request;
        public MirRecordDefinition Error { get; } = error;
        public string RequestDtoName => CSharpBackend.GeneratedFunctionName($"BridgeRequest_{ModuleId.Value}_{AuthoredName}");

        public BridgeOperationContract ToContract()
            => new(
                Id,
                MirBridgeContract.PostMethod,
                Route,
                new BridgeTypeContract("record", Request.Name, Request.Id.Value, Request.Fields.Select(field => new BridgeFieldContract(field.Name, ToContractType(field.Type))).ToArray()),
                new BridgeTypeContract("string", "string", null, []),
                true,
                new BridgeErrorContract(Error.Name, Error.Id.Value, Error.Fields.Select(field => new BridgeFieldContract(field.Name, ToContractType(field.Type))).ToArray()));

        private static BridgeTypeContract ToContractType(MirType type)
            => new("primitive", type.Identifier, null, []);
    }

    private sealed record BridgeContract(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("operations")] IReadOnlyList<BridgeOperationContract> Operations);

    private sealed record BridgeOperationContract(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("route")] string Route,
        [property: JsonPropertyName("request")] BridgeTypeContract Request,
        [property: JsonPropertyName("response")] BridgeTypeContract Response,
        [property: JsonPropertyName("fallible")] bool Fallible,
        [property: JsonPropertyName("error")] BridgeErrorContract Error);

    private sealed record BridgeTypeContract(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("identity")] string? Identity,
        [property: JsonPropertyName("fields")] IReadOnlyList<BridgeFieldContract> Fields);

    private sealed record BridgeFieldContract(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] BridgeTypeContract Type);

    private sealed record BridgeErrorContract(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("identity")] string Identity,
        [property: JsonPropertyName("fields")] IReadOnlyList<BridgeFieldContract> Fields);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };
}
