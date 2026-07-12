using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Parsing;

namespace Aurelian.Shaders.Language.Validation;

public static class SdslvValidator
{
    public const string DuplicateDeclarationCode = "SV1001";
    public const string DuplicateUseCode = "SV1002";
    public const string DuplicateFieldCode = "SV1101";
    public const string DuplicateEnumVariantCode = "SV1201";
    public const string DuplicateGenericParameterCode = "SV1301";
    public const string DuplicateShaderMemberCode = "SV1302";
    public const string UnknownTypeCode = "SV1401";
    public const string InvalidArrayLengthCode = "SV1402";
    public const string DuplicateLocalCode = "SV1501";
    public const string UnsupportedValidationFeatureCode = "SV1901";

    public static SdslvValidationResult ValidateSource(string source, SdslvValidationOptions? options = null)
    {
        var parse = SdslvParser.ParseModule(source);
        if (parse.Module is null)
        {
            return new SdslvValidationResult(new SdslvModule(null, [], []), parse.Diagnostics);
        }

        var validation = ValidateModule(parse.Module, options);
        return new SdslvValidationResult(parse.Module, [..parse.Diagnostics, ..validation.Diagnostics]);
    }

    public static SdslvValidationResult ValidateModule(SdslvModule module, SdslvValidationOptions? options = null)
    {
        _ = options;
        var validator = new Validator(module);
        validator.Validate();
        return new SdslvValidationResult(module, validator.Diagnostics);
    }

    private sealed class Validator(SdslvModule module)
    {
        private readonly HashSet<string> _typeNames = new(SdslvBuiltinTypes.Names, StringComparer.Ordinal);
        private static readonly IReadOnlySet<string> EmptyTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<SdslvDiagnostic> _diagnostics = [];

        public IReadOnlyList<SdslvDiagnostic> Diagnostics => _diagnostics;

        public void Validate()
        {
            CollectTopLevelNames();
            ValidateUses();

            foreach (var declaration in module.Declarations)
            {
                ValidateDeclaration(declaration);
            }
        }

        private void CollectTopLevelNames()
        {
            var declarationsByName = new HashSet<string>(StringComparer.Ordinal);
            foreach (var declaration in module.Declarations)
            {
                var name = GetTopLevelName(declaration);
                if (name is null)
                {
                    continue;
                }

                if (!declarationsByName.Add(name))
                {
                    Error(DuplicateDeclarationCode, $"Duplicate top-level declaration '{name}'.");
                }

                _typeNames.Add(name);
            }
        }

        private void ValidateUses()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var use in module.Uses)
            {
                var path = use.Path.ToString();
                if (!paths.Add(path))
                {
                    Error(DuplicateUseCode, $"Duplicate use path '{path}'.");
                }
            }
        }

        private void ValidateDeclaration(SdslvDecl declaration)
        {
            switch (declaration)
            {
                case SdslvTypeAliasDecl alias:
                    ValidateTypeRef(alias.TargetType, EmptyTypeNames);
                    break;
                case SdslvRecordDecl record:
                    ValidateFields(record.Fields, "record", record.Name, EmptyTypeNames);
                    break;
                case SdslvStreamDecl stream:
                    ValidateFields(stream.Fields, "stream", stream.Name, EmptyTypeNames);
                    break;
                case SdslvEnumDecl enumDecl:
                    ValidateEnum(enumDecl);
                    break;
                case SdslvShaderDecl shader:
                    ValidateShader(shader);
                    break;
                case SdslvInterfaceDecl interfaceDecl:
                    ValidateInterface(interfaceDecl);
                    break;
                case SdslvFlowDecl flow:
                    ValidateFlow(flow);
                    break;
                case SdslvCompileDecl compile:
                    ValidateTypeRefs(compile.TypeArguments, EmptyTypeNames);
                    break;
                default:
                    Warning(UnsupportedValidationFeatureCode, $"Validation M0 does not recognize declaration shape '{declaration.GetType().Name}'.");
                    break;
            }
        }

        private void ValidateFields(IReadOnlyList<SdslvFieldDecl> fields, string ownerKind, string ownerName, IReadOnlySet<string> genericTypeNames)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (!names.Add(field.Name))
                {
                    Error(DuplicateFieldCode, $"Duplicate {ownerKind} field '{field.Name}' in {ownerKind} '{ownerName}'.");
                }

                ValidateTypeRef(field.TypeName, genericTypeNames);
            }
        }

        private void ValidateEnum(SdslvEnumDecl enumDecl)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variant in enumDecl.Variants)
            {
                if (!names.Add(variant.Name))
                {
                    Error(DuplicateEnumVariantCode, $"Duplicate enum variant '{variant.Name}' in enum '{enumDecl.Name}'.", variant.Span);
                }
            }
        }

        private void ValidateShader(SdslvShaderDecl shader)
        {
            var genericTypeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var generic in shader.GenericParameters)
            {
                if (!genericTypeNames.Add(generic))
                {
                    Error(DuplicateGenericParameterCode, $"Duplicate generic parameter '{generic}' in shader '{shader.Name}'.");
                }
            }

            var implements = new HashSet<string>(StringComparer.Ordinal);
            foreach (var implemented in shader.Implements)
            {
                var path = implemented.ToString();
                if (!implements.Add(path))
                {
                    Error(DuplicateShaderMemberCode, $"Duplicate implements entry '{path}' in shader '{shader.Name}'.");
                }
            }

            ValidateFields(shader.MaterialFields, "material", shader.Name, genericTypeNames);
            ValidateMethods(shader.Methods, shader.StageMethods, shader.Name, genericTypeNames);
        }

        private void ValidateMethods(
            IReadOnlyList<SdslvFunctionDecl> methods,
            IReadOnlyList<SdslvFunctionDecl> stageMethods,
            string shaderName,
            IReadOnlySet<string> genericTypeNames)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods.Concat(stageMethods))
            {
                if (!names.Add(method.Name))
                {
                    Error(DuplicateShaderMemberCode, $"Duplicate shader method '{method.Name}' in shader '{shaderName}'.");
                }

                ValidateFunction(method, genericTypeNames);
            }
        }

        private void ValidateInterface(SdslvInterfaceDecl interfaceDecl)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in interfaceDecl.Methods)
            {
                if (!names.Add(method.Name))
                {
                    Error(DuplicateShaderMemberCode, $"Duplicate interface method '{method.Name}' in interface '{interfaceDecl.Name}'.");
                }

                ValidateFunction(method, EmptyTypeNames);
            }
        }

        private void ValidateFlow(SdslvFlowDecl flow)
        {
            ValidateParameters(flow.Parameters, EmptyTypeNames);
            ValidateTypeRef(flow.ReturnType, EmptyTypeNames);
            if (flow.Board is not null)
            {
                ValidateFlowBoard(flow.Board, flow.Name);
            }
        }

        private void ValidateFlowBoard(SdslvFlowBoard board, string flowName)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in board.Fields)
            {
                if (!names.Add(field.Name))
                {
                    Error(DuplicateFieldCode, $"Duplicate board field '{field.Name}' in flow '{flowName}'.", field.Span);
                }

                ValidateTypeRef(field.TypeName, EmptyTypeNames);
            }
        }

        private void ValidateFunction(SdslvFunctionDecl function, IReadOnlySet<string> genericTypeNames)
        {
            ValidateParameters(function.Parameters, genericTypeNames);
            ValidateTypeRef(function.ReturnType, genericTypeNames);
            if (function.ErrorType is not null)
            {
                ValidateTypeRef(function.ErrorType, genericTypeNames);
            }

            if (function.Body is not null)
            {
                ValidateStatementBlock(function.Body.Statements, genericTypeNames);
            }
        }

        private void ValidateParameters(IReadOnlyList<SdslvFunctionParameter> parameters, IReadOnlySet<string> genericTypeNames)
        {
            foreach (var parameter in parameters)
            {
                ValidateTypeRef(parameter.TypeName, genericTypeNames);
            }
        }

        private void ValidateStatementBlock(IReadOnlyList<SdslvStatement> statements, IReadOnlySet<string> genericTypeNames)
        {
            var locals = new HashSet<string>(StringComparer.Ordinal);
            foreach (var statement in statements)
            {
                ValidateStatement(statement, genericTypeNames, locals);
            }
        }

        private void ValidateStatement(SdslvStatement statement, IReadOnlySet<string> genericTypeNames, HashSet<string> locals)
        {
            switch (statement)
            {
                case SdslvLetStatement let:
                    if (!locals.Add(let.Name))
                    {
                        Error(DuplicateLocalCode, $"Duplicate local '{let.Name}' in the same function scope.");
                    }

                    ValidateTypeRef(let.TypeName, genericTypeNames);
                    break;
                case SdslvIfStatement ifStatement:
                    ValidateStatementBlock(ifStatement.ThenBody, genericTypeNames);
                    if (ifStatement.ElseBody is not null)
                    {
                        ValidateStatementBlock(ifStatement.ElseBody, genericTypeNames);
                    }
                    break;
                case SdslvForStatement forStatement:
                    if (string.IsNullOrWhiteSpace(forStatement.Iterator) || forStatement.Iterator == "<error>")
                    {
                        Error(UnsupportedValidationFeatureCode, "For loop iterator name must not be empty.", forStatement.Span);
                    }

                    ValidateStatementBlock(forStatement.Body, genericTypeNames);
                    break;
            }
        }

        private void ValidateTypeRefs(IEnumerable<SdslvTypeRef> typeRefs, IReadOnlySet<string> genericTypeNames)
        {
            foreach (var typeRef in typeRefs)
            {
                ValidateTypeRef(typeRef, genericTypeNames);
            }
        }

        private void ValidateTypeRef(SdslvTypeRef typeRef, IReadOnlySet<string> genericTypeNames)
        {
            switch (typeRef)
            {
                case SdslvNamedTypeRef named:
                    ValidateNamedType(named, genericTypeNames);
                    break;
                case SdslvArrayTypeRef array:
                    if (array.Length <= 0)
                    {
                        Error(InvalidArrayLengthCode, $"Array length must be positive; found {array.Length}.", array.Span);
                    }

                    ValidateTypeRef(array.Element, genericTypeNames);
                    break;
                default:
                    Warning(UnsupportedValidationFeatureCode, $"Validation M0 does not recognize type reference shape '{typeRef.GetType().Name}'.");
                    break;
            }
        }

        private void ValidateNamedType(SdslvNamedTypeRef named, IReadOnlySet<string> genericTypeNames)
        {
            var name = named.Path.ToString();
            var lastSegment = named.Path.Segments.Count == 0 ? name : named.Path.Segments[^1];
            if (_typeNames.Contains(name) || _typeNames.Contains(lastSegment) || genericTypeNames.Contains(name) || genericTypeNames.Contains(lastSegment))
            {
                return;
            }

            Error(UnknownTypeCode, $"Unknown type reference '{name}'.");
        }

        private static string? GetTopLevelName(SdslvDecl declaration) => declaration switch
        {
            SdslvTypeAliasDecl alias => alias.Name,
            SdslvRecordDecl record => record.Name,
            SdslvStreamDecl stream => stream.Name,
            SdslvEnumDecl enumDecl => enumDecl.Name,
            SdslvShaderDecl shader => shader.Name,
            SdslvInterfaceDecl interfaceDecl => interfaceDecl.Name,
            SdslvFlowDecl flow => flow.Name,
            SdslvCompileDecl compile => compile.Alias,
            _ => null,
        };

        private void Error(string code, string message, SdslvSpan span = default) => _diagnostics.Add(new SdslvDiagnostic(
            code,
            SdslvDiagnosticSeverity.Error,
            SdslvDiagnosticPhase.Validation,
            message,
            NormalizeSpan(span)));

        private void Warning(string code, string message, SdslvSpan span = default) => _diagnostics.Add(new SdslvDiagnostic(
            code,
            SdslvDiagnosticSeverity.Warning,
            SdslvDiagnosticPhase.Validation,
            message,
            NormalizeSpan(span)));

        private static SdslvSpan NormalizeSpan(SdslvSpan span) => span == default ? SdslvSpan.Unknown : span;
    }
}
