from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:60]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
c='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs'
edit(c,'    private static readonly AsyncLocal<string?> CurrentModuleClassName = new();','    private static readonly AsyncLocal<CSharpEmissionOptions?> CurrentOptions = new();')
edit(c,'=> EmitCore(program, null, DefaultModuleClassName);','=> Emit(program, new CSharpEmissionOptions());')
# Retain the existing string overload, with options validation in one place.
p=Path(c);s=p.read_text();start=s.index('        if (!IsValidModuleClassName(moduleClassName))');end=s.index('    private static bool IsValidModuleClassName',start)
s=s[:start]+'''        return Emit(program, new CSharpEmissionOptions { ModuleClassName = moduleClassName });
    }

    public static CSharpCompilation Emit(MirProgram program, CSharpEmissionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsValidModuleClassName(options.ModuleClassName)) throw new ArgumentException("Invalid generated module class name.", nameof(options));
        if (string.IsNullOrEmpty(options.Namespace) || !options.Namespace.Split('.').All(IsValidModuleClassName)) throw new ArgumentException("Invalid generated namespace.", nameof(options));
        if (options.RecordCarrierScope is not null && !IsValidModuleClassName(options.RecordCarrierScope)) throw new ArgumentException("Invalid record carrier scope.", nameof(options));
        return EmitCore(program, null, options);
    }

'''+s[end:]
s=s.replace('        return true;\n    }\n\n    private static string ModuleClassName => CurrentModuleClassName.Value ?? DefaultModuleClassName;','''        return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;
    }

    private static CSharpEmissionOptions EmissionOptions => CurrentOptions.Value ?? new CSharpEmissionOptions();
    private static string ModuleClassName => EmissionOptions.ModuleClassName;
    private static string FunctionAccessibility(MirFunction function)
        => EmissionOptions.PublicFunctionNames is null || EmissionOptions.PublicFunctionNames.Contains(function.Name) ? "public" : "internal";''')
s=s.replace('contract : null, DefaultModuleClassName)','contract : null, new CSharpEmissionOptions())').replace('CSharpSidecarContract? sidecarContract, string moduleClassName)','CSharpSidecarContract? sidecarContract, CSharpEmissionOptions options)')
s=s.replace('string? previousModuleClassName = CurrentModuleClassName.Value;\n        CurrentModuleClassName.Value = moduleClassName;','CSharpEmissionOptions? previousOptions = CurrentOptions.Value;\n        CurrentOptions.Value = options;')
s=s.replace('writer.WriteLine("namespace Copeland.Generated;")','writer.WriteLine($"namespace {EmissionOptions.Namespace};")')
s=s.replace('CurrentModuleClassName.Value = previousModuleClassName;','CurrentOptions.Value = previousOptions;')
s=s.replace('$"public static {returnType} {CSharpNameMangler.Mangle(function.Name)}', '$"{FunctionAccessibility(function)} static {returnType} {CSharpNameMangler.Mangle(function.Name)}')
s=s.replace('$"public static {returnType} {publicName}', '$"{FunctionAccessibility(function)} static {returnType} {publicName}')
s=s.replace('$"public static CopeAsync<{resultType}> {CSharpNameMangler.Mangle(function.Name)}', '$"{FunctionAccessibility(function)} static CopeAsync<{resultType}> {CSharpNameMangler.Mangle(function.Name)}')
# Exact record carrier owner comes from the host options, never post-emission regex.
s=s.replace('=> "__CopeRecord_" + EncodeStableIdentity(id.Value);','=> "__CopeRecord_" + (EmissionOptions.RecordCarrierScope is null ? string.Empty : EmissionOptions.RecordCarrierScope + "_") + EncodeStableIdentity(id.Value);')
p.write_text(s,encoding='utf-8',newline='\n')
m='src/Copeland/Copeland.TS.MSBuild/CopelandCompile.cs';p=Path(m);s=p.read_text();s=s.replace('using System.Text.RegularExpressions;\n','')
s=s.replace('CSharpBackend.Emit(project.Compilation!.MirCompilation!.Program!, publicModuleName)','''CSharpBackend.Emit(project.Compilation!.MirCompilation!.Program!, new CSharpEmissionOptions
            {
                ModuleClassName = publicModuleName,
                Namespace = NormalizeNamespace(rootNamespace) + ".Copeland",
                RecordCarrierScope = graphArtifactName,
                PublicFunctionNames = project.MirProjectGraph!.Modules.SelectMany(module => module.Exports.Select(export => export.Name)).ToHashSet(StringComparer.Ordinal),
            })''')
s=s.replace('CSharpBackend.Emit(compilation.MirCompilation!.Program!, moduleName)','''CSharpBackend.Emit(compilation.MirCompilation!.Program!, new CSharpEmissionOptions
        {
            ModuleClassName = moduleName,
            Namespace = NormalizeNamespace(rootNamespace) + ".Copeland",
            RecordCarrierScope = moduleName,
        })''')
start=s.index('            string generatedNamespace = NormalizeNamespace(rootNamespace)');end=s.index('            WriteIfChanged(outputPath, generatedSource);',start)
s=s[:start]+'            string generatedSource = emitted.SourceText;\n'+s[end:]
start=s.index('        string generatedNamespace = NormalizeNamespace(rootNamespace)');end=s.index('        WriteIfChanged(outputPath, generatedSource);',start)
s=s[:start]+'        string generatedSource = emitted.SourceText;\n\n'+s[end:]
start=s.index('    private static string ScopeRecordCarrierNames(');end=s.index('    private static IReadOnlyDictionary<string, string> CreateModuleNames(',start)
s=s[:start]+s[end:]
# Payload bytes are authoritative even when a repack preserves length/time/version.
start=s.index('        var assemblyFile = new FileInfo(assemblyPath);');end=s.index('    }',start)
s=s[:start]+'        hash.AppendData(SHA256.HashData(File.ReadAllBytes(assemblyPath)));\n'+s[end:]
p.write_text(s,encoding='utf-8',newline='\n')
t='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/TableQuerySourceGeneration.cs';p=Path(t);s=p.read_text();s=s.replace('public static CSharpQuerySourceArtifact CreateArtifact(MirTableQueryArtifact query)\n        => new(query.StableId, "CopelandQuery_" + query.StableId + ".g.cs", WriteSource(query));','''public static CSharpQuerySourceArtifact CreateArtifact(MirTableQueryArtifact query, CSharpEmissionOptions? options = null)
        => new(query.StableId, "CopelandQuery_" + query.StableId + ".g.cs", WriteSource(query, options ?? new CSharpEmissionOptions()));''')
s=s.replace('Execute(MirProgram program, MirTableQueryArtifact query)','Execute(MirProgram program, MirTableQueryArtifact query, CSharpEmissionOptions? options = null)')
s=s.replace('        CSharpCompilation emitted = CSharpBackend.Emit(program);','        CSharpEmissionOptions identity = options ?? new CSharpEmissionOptions();\n        CSharpCompilation emitted = CSharpBackend.Emit(program, identity);').replace('CreateArtifact(query);','CreateArtifact(query, identity);')
s=s.replace('string typeName = "Copeland.Generated.__CopelandQuery_" + query.StableId;','string typeName = identity.Namespace + ".__CopelandQuery_" + query.StableId;')
s=s.replace('WriteSource(MirTableQueryArtifact query)','WriteSource(MirTableQueryArtifact query, CSharpEmissionOptions identity)').replace('writer.Line("namespace Copeland.Generated;");','writer.Line("namespace " + identity.Namespace + ";");').replace('writer.Line("var table = CopelandModule.__CopelandQueryTable_"','writer.Line("var table = " + identity.ModuleClassName + ".__CopelandQueryTable_"')
p.write_text(s,encoding='utf-8',newline='\n')
