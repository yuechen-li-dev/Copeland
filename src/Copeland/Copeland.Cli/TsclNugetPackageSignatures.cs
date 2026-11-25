using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Copeland.Cli;

internal static partial class TsclBuildContract
{
    private sealed record NugetWireSignature(string[] Parameters, string Result);

    private sealed class NugetPackageSignatures
    {
        private readonly CSharpCompilation compilation;
        private readonly Dictionary<string, List<MetadataReference>> packages = new(StringComparer.OrdinalIgnoreCase);

        public NugetPackageSignatures(JsonElement assets)
        {
            string[] roots = assets.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToArray();
            JsonElement libraries = assets.GetProperty("libraries");
            var references = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
            foreach (JsonProperty library in assets.GetProperty("targets").GetProperty("net10.0").EnumerateObject())
            {
                if (!library.Value.TryGetProperty("compile", out JsonElement compile)
                    || !libraries.GetProperty(library.Name).TryGetProperty("path", out JsonElement relativePath))
                {
                    continue;
                }
                string id = library.Name[..library.Name.IndexOf('/')];
                var packageReferences = new List<MetadataReference>();
                foreach (JsonProperty asset in compile.EnumerateObject())
                {
                    if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string path = roots.Select(root => Path.Combine(root, relativePath.GetString()!, asset.Name))
                        .FirstOrDefault(File.Exists)
                        ?? throw new InvalidDataException($"NuGet compile asset '{asset.Name}' for '{id}' is missing.");
                    var reference = MetadataReference.CreateFromFile(path);
                    references.Add(reference);
                    packageReferences.Add(reference);
                }
                packages.Add(id, packageReferences);
            }
            compilation = CSharpCompilation.Create("CopelandNugetSignatures", references: references);
        }

        public NugetWireSignature Resolve(NugetSignature signature)
        {
            if (!packages.TryGetValue(signature.Package, out List<MetadataReference>? references) || references.Count == 0)
            {
                throw new InvalidDataException($"NuGet package '{signature.Package}' has no .NET 10 compile assets.");
            }
            IReadOnlyList<string> typeArguments = signature.Binding.TypeArguments;
            var matches = new List<IMethodSymbol>();
            foreach (MetadataReference reference in references)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly
                    || assembly.GetTypeByMetadataName(signature.Binding.Type) is not { DeclaredAccessibility: Accessibility.Public, Arity: 0 } type)
                {
                    continue;
                }
                foreach (IMethodSymbol method in type.GetMembers(signature.Binding.Method).OfType<IMethodSymbol>())
                {
                    if (method is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, MethodKind: MethodKind.Ordinary, IsVararg: false }
                        && method.Arity == typeArguments.Count
                        && method.Parameters.All(parameter => parameter.RefKind == RefKind.None)
                        && method.Parameters.Select(parameter => ClrAlias(parameter.Type, typeArguments, nullableReferences: false))
                            .SequenceEqual(signature.Parameters))
                    {
                        matches.Add(method);
                    }
                }
            }
            if (matches.Count != 1)
            {
                throw new InvalidDataException($"Expected one public static overload for {signature.Binding.Type}.{signature.Binding.Method}({string.Join(", ", signature.Parameters)}) in {signature.Package}; found {matches.Count}.");
            }
            IMethodSymbol selected = matches[0];
            if (selected.TypeParameters.Any(parameter => parameter.HasValueTypeConstraint || parameter.ConstraintTypes.Length != 0))
            {
                throw new InvalidDataException($"NuGet operation '{signature.Binding.Operation}' cannot close generics with those CLR type constraints.");
            }
            bool projected = signature.Binding.ProjectDto;
            string? result = ClrAlias(selected.ReturnType, typeArguments, projected);
            if (selected.ReturnsByRef || selected.ReturnsByRefReadonly || result is null || result == "object")
            {
                throw new InvalidDataException($"NuGet operation '{signature.Binding.Operation}' returns an unsupported TSON type.");
            }

            var parameters = new string[selected.Parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                IParameterSymbol parameter = selected.Parameters[index];
                string? inferred = ClrAlias(parameter.Type, typeArguments, projected);
                string? specified = signature.WireParameters?[index];
                if (parameter.Type.SpecialType == SpecialType.System_Object)
                {
                    parameters[index] = specified ?? throw new InvalidDataException(
                        $"NuGet operation '{signature.Binding.Operation}' requires an explicit wireParameters entry for CLR object parameter {index + 1}.");
                }
                else
                {
                    if (inferred is null || (specified is not null && specified != inferred))
                    {
                        throw new InvalidDataException($"NuGet operation '{signature.Binding.Operation}' has incompatible wire type '{specified}' for parameter {index + 1}.");
                    }
                    parameters[index] = inferred;
                }
            }
            return new(parameters, result);
        }

        private static string? ClrAlias(ITypeSymbol type, IReadOnlyList<string> typeArguments, bool nullableReferences)
        {
            string? name;
            switch (type)
            {
                case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter:
                    name = parameter.Ordinal < typeArguments.Count ? typeArguments[parameter.Ordinal] : null;
                    break;
                case IArrayTypeSymbol { IsSZArray: true } array:
                    string? element = ClrAlias(array.ElementType, typeArguments, nullableReferences);
                    name = element is not null && element != "object" ? element + "[]" : null;
                    break;
                case INamedTypeSymbol nullable when nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
                    string? value = ClrAlias(nullable.TypeArguments[0], typeArguments, nullableReferences);
                    return value is null ? null : $"Option<{value}>";
                default:
                    name = type.SpecialType switch
                    {
                        SpecialType.System_String => "string",
                        SpecialType.System_Boolean => "boolean",
                        SpecialType.System_Double => "number",
                        SpecialType.System_Object => "object",
                        _ => null,
                    };
                    break;
            }
            if (nullableReferences && name is not null && name != "object" && type.NullableAnnotation == NullableAnnotation.Annotated)
            {
                return $"Option<{name}>";
            }
            return name;
        }
    }
}
