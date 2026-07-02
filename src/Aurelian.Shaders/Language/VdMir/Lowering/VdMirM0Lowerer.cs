using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.VdMir.Lowering;

public static class VdMirM0Lowerer
{
    public static VdMirModule LowerModule(SdslvModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var lowerer = new Lowerer(module);
        return lowerer.Lower();
    }

    private sealed class Lowerer(SdslvModule module)
    {
        private readonly List<VdMirStruct> _structs = [];
        private readonly List<VdMirEntryPoint> _entryPoints = [];
        private readonly List<VdMirDiagnostic> _diagnostics = [];
        private readonly HashSet<string> _entryPointNames = new(StringComparer.Ordinal);

        public VdMirModule Lower()
        {
            foreach (var declaration in module.Declarations)
            {
                LowerDeclaration(declaration);
            }

            return new VdMirModule(module.Namespace, _structs, _entryPoints, _diagnostics);
        }

        private void LowerDeclaration(SdslvDecl declaration)
        {
            switch (declaration)
            {
                case SdslvRecordDecl record:
                    _structs.Add(new VdMirStruct(record.Name, LowerFields(record.Name, record.Fields), SdslvSpan.Unknown));
                    break;
                case SdslvShaderDecl shader:
                    LowerShader(shader);
                    break;
                case SdslvStreamDecl:
                case SdslvTypeAliasDecl:
                case SdslvEnumDecl:
                case SdslvFlowDecl:
                case SdslvCompileDecl:
                case SdslvInterfaceDecl:
                    Error(
                        VdMirDiagnosticCodes.UnsupportedDeclaration,
                        $"VD-MIR M0 does not support declaration shape '{declaration.GetType().Name}'.");
                    break;
                default:
                    Error(
                        VdMirDiagnosticCodes.UnsupportedDeclaration,
                        $"VD-MIR M0 does not recognize declaration shape '{declaration.GetType().Name}'.");
                    break;
            }
        }

        private IReadOnlyList<VdMirField> LowerFields(string aggregateName, IReadOnlyList<SdslvFieldDecl> fields)
        {
            var lowered = new List<VdMirField>(fields.Count);
            foreach (var field in fields)
            {
                var type = LowerType(field.TypeName, SdslvSpan.Unknown);
                lowered.Add(new VdMirField(field.Name, type, InferFieldSemantic(aggregateName, field.Name), SdslvSpan.Unknown));
            }

            return lowered;
        }

        private void LowerShader(SdslvShaderDecl shader)
        {
            if (shader.GenericParameters.Count > 0 ||
                shader.Implements.Count > 0 ||
                shader.Constraints.Count > 0 ||
                shader.MaterialFields.Count > 0 ||
                shader.Methods.Count > 0)
            {
                Error(
                    VdMirDiagnosticCodes.UnsupportedShaderShape,
                    $"VD-MIR M0 only supports smoke-triangle stage entry points in shader '{shader.Name}'.");
            }

            foreach (var stageMethod in shader.StageMethods)
            {
                var entryPoint = LowerEntryPoint(stageMethod);
                if (entryPoint is null)
                {
                    continue;
                }

                if (!_entryPointNames.Add(entryPoint.Name))
                {
                    Error(
                        VdMirDiagnosticCodes.DuplicateEntryPoint,
                        $"VD-MIR M0 does not allow duplicate entry point '{entryPoint.Name}'.",
                        entryPoint.Span);
                    continue;
                }

                _entryPoints.Add(entryPoint);
            }
        }

        private VdMirEntryPoint? LowerEntryPoint(SdslvFunctionDecl function)
        {
            if (function.Body is null)
            {
                Error(
                    VdMirDiagnosticCodes.MissingFunctionBody,
                    $"VD-MIR M0 requires a function body for entry point '{function.Name}'.");
                return null;
            }

            if (function.ErrorType is not null)
            {
                Error(
                    VdMirDiagnosticCodes.UnsupportedEntryPointShape,
                    $"VD-MIR M0 does not support fallible stage method '{function.Name}'.",
                    function.Body.Span);
                return null;
            }

            var stage = InferStageKind(function);
            if (stage == VdMirStageKind.Unknown)
            {
                Error(
                    VdMirDiagnosticCodes.UnsupportedStageMethod,
                    $"VD-MIR M0 could not infer stage kind for entry point '{function.Name}'.",
                    function.Body.Span);
                return null;
            }

            if (function.Parameters.Count != 1)
            {
                Error(
                    VdMirDiagnosticCodes.UnsupportedEntryPointShape,
                    $"VD-MIR M0 expects exactly one parameter for entry point '{function.Name}'.",
                    function.Body.Span);
                return null;
            }

            var parameters = function.Parameters
                .Select(parameter => new VdMirParameter(
                    parameter.Name,
                    LowerType(parameter.TypeName, function.Body.Span),
                    function.Body.Span))
                .ToArray();

            var statements = new List<VdMirStatement>(function.Body.Statements.Count);
            foreach (var statement in function.Body.Statements)
            {
                var lowered = LowerStatement(statement, function.Body.Span);
                if (lowered is null)
                {
                    return null;
                }

                statements.Add(lowered);
            }

            return new VdMirEntryPoint(
                function.Name,
                stage,
                parameters,
                LowerType(function.ReturnType, function.Body.Span),
                InferReturnSemantic(function, stage),
                statements,
                function.Body.Span);
        }

        private VdMirStatement? LowerStatement(SdslvStatement statement, SdslvSpan fallbackSpan)
        {
            return statement switch
            {
                SdslvLetStatement let => new VdMirLocalStatement(
                    let.Name,
                    LowerType(let.TypeName, fallbackSpan),
                    let.Initializer is null ? null : LowerExpression(let.Initializer, fallbackSpan),
                    fallbackSpan),
                SdslvAssignStatement assign => new VdMirAssignStatement(
                    LowerExpression(assign.Target, fallbackSpan),
                    LowerExpression(assign.Value, fallbackSpan),
                    fallbackSpan),
                SdslvReturnStatement ret => new VdMirReturnStatement(
                    LowerExpression(ret.Value, fallbackSpan),
                    fallbackSpan),
                SdslvExpressionStatement expression => new VdMirExpressionStatement(
                    LowerExpression(expression.Value, fallbackSpan),
                    fallbackSpan),
                SdslvEmptyStatement => new VdMirExpressionStatement(
                    new VdMirIdentifierExpression(string.Empty, fallbackSpan),
                    fallbackSpan),
                _ => UnsupportedStatement(statement, fallbackSpan),
            };
        }

        private VdMirStatement? UnsupportedStatement(SdslvStatement statement, SdslvSpan span)
        {
            Error(
                VdMirDiagnosticCodes.UnsupportedStatement,
                $"VD-MIR M0 does not support statement shape '{statement.GetType().Name}'.",
                span);
            return null;
        }

        private VdMirExpression LowerExpression(SdslvExpression expression, SdslvSpan fallbackSpan)
        {
            return expression switch
            {
                SdslvIdentifierExpression identifier => new VdMirIdentifierExpression(identifier.Name, fallbackSpan),
                SdslvIntegerLiteralExpression integer => new VdMirIntegerLiteralExpression(integer.Value, fallbackSpan),
                SdslvFloatLiteralExpression floating => new VdMirFloatLiteralExpression(floating.Value, fallbackSpan),
                SdslvBoolLiteralExpression boolean => new VdMirBoolLiteralExpression(boolean.Value, fallbackSpan),
                SdslvFieldAccessExpression fieldAccess => new VdMirFieldAccessExpression(
                    LowerExpression(fieldAccess.Base, fallbackSpan),
                    fieldAccess.Field,
                    fallbackSpan),
                SdslvCallExpression call => new VdMirCallExpression(
                    LowerExpression(call.Callee, fallbackSpan),
                    call.Arguments.Select(argument => LowerExpression(argument, fallbackSpan)).ToArray(),
                    fallbackSpan),
                SdslvBinaryExpression binary => new VdMirBinaryExpression(
                    LowerExpression(binary.Left, fallbackSpan),
                    binary.Operator,
                    LowerExpression(binary.Right, fallbackSpan),
                    fallbackSpan),
                SdslvUnaryExpression unary => new VdMirUnaryExpression(
                    unary.Operator,
                    LowerExpression(unary.Operand, fallbackSpan),
                    fallbackSpan),
                _ => UnsupportedExpression(expression, fallbackSpan),
            };
        }

        private VdMirExpression UnsupportedExpression(SdslvExpression expression, SdslvSpan span)
        {
            Error(
                VdMirDiagnosticCodes.UnsupportedExpression,
                $"VD-MIR M0 does not support expression shape '{expression.GetType().Name}'.",
                span);
            return new VdMirIdentifierExpression("/* unsupported */", span);
        }

        private VdMirType LowerType(SdslvTypeRef typeRef, SdslvSpan span)
        {
            if (typeRef is SdslvNamedTypeRef named)
            {
                var lastSegment = named.Path.Segments.Count == 0 ? named.Path.ToString() : named.Path.Segments[^1];
                return lastSegment switch
                {
                    "void" => new VdMirVoidType(),
                    "bool" => new VdMirScalarType(VdMirScalarKind.Bool),
                    "int" => new VdMirScalarType(VdMirScalarKind.Int),
                    "uint" => new VdMirScalarType(VdMirScalarKind.UInt),
                    "float" => new VdMirScalarType(VdMirScalarKind.Float),
                    "float2" => new VdMirVectorType(VdMirScalarKind.Float, 2),
                    "float3" => new VdMirVectorType(VdMirScalarKind.Float, 3),
                    "float4" => new VdMirVectorType(VdMirScalarKind.Float, 4),
                    "float4x4" => new VdMirMatrixType(VdMirScalarKind.Float, 4, 4),
                    _ => new VdMirStructType(lastSegment),
                };
            }

            Error(
                VdMirDiagnosticCodes.UnsupportedType,
                $"VD-MIR M0 does not support type shape '{typeRef.GetType().Name}'.",
                span);
            return new VdMirStructType("unsupported");
        }

        private static VdMirSemanticKind InferFieldSemantic(string aggregateName, string fieldName)
        {
            if (aggregateName.Equals("VertexInput", StringComparison.Ordinal) &&
                fieldName.Equals("Position", StringComparison.Ordinal))
            {
                return VdMirSemanticKind.Position;
            }

            if (aggregateName.Equals("VertexOutput", StringComparison.Ordinal) &&
                fieldName.Equals("Position", StringComparison.Ordinal))
            {
                return VdMirSemanticKind.SvPosition;
            }

            if (fieldName.Equals("Color", StringComparison.Ordinal))
            {
                return VdMirSemanticKind.Color0;
            }

            return VdMirSemanticKind.None;
        }

        private static VdMirStageKind InferStageKind(SdslvFunctionDecl function)
        {
            if (TryInferStageKind(function.Stage, out var explicitStage))
            {
                return explicitStage;
            }

            return TryInferStageKind(function.Name, out var inferredStage) ? inferredStage : VdMirStageKind.Unknown;
        }

        private static bool TryInferStageKind(string? text, out VdMirStageKind stage)
        {
            stage = VdMirStageKind.Unknown;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (text.StartsWith("VS", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Vertex", StringComparison.OrdinalIgnoreCase))
            {
                stage = VdMirStageKind.Vertex;
                return true;
            }

            if (text.StartsWith("PS", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Fragment", StringComparison.OrdinalIgnoreCase))
            {
                stage = VdMirStageKind.Pixel;
                return true;
            }

            return false;
        }

        private static VdMirSemanticKind InferReturnSemantic(SdslvFunctionDecl function, VdMirStageKind stage)
        {
            if (stage == VdMirStageKind.Pixel &&
                function.ReturnType is SdslvNamedTypeRef named &&
                named.Path.Segments.Count > 0 &&
                named.Path.Segments[^1].Equals("float4", StringComparison.Ordinal))
            {
                return VdMirSemanticKind.SvTarget0;
            }

            return VdMirSemanticKind.None;
        }

        private void Error(string code, string message, SdslvSpan span = default)
        {
            _diagnostics.Add(new VdMirDiagnostic(
                code,
                SdslvDiagnosticSeverity.Error,
                message,
                span == default ? SdslvSpan.Unknown : span));
        }
    }
}
