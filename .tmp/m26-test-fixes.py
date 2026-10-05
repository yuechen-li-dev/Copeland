from pathlib import Path
p=Path('src/Copeland/Copeland.TS/Semantics/Binder.cs');s=p.read_text().replace('init = type == PrimitiveTypeSymbol.Error && !inferInitializer','init = type == PrimitiveTypeSymbol.Error && !inferInitializer && v.Type is not null');s=s.replace('BoundExpression left = BindExpression(syntax.Left);\n            if (left.Type is not OptionTypeSymbol option)','BoundExpression left = BindExpression(syntax.Left);\n            if (left.Type == PrimitiveTypeSymbol.Error) return new BoundErrorExpression();\n            if (left.Type is not OptionTypeSymbol option)',1);p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.Tests/OptionEffectsM0Tests.cs');s=p.read_text().replace('return undefined; }", "COPE-BIND-0001"','return undefined; }", "COPE-PROFILE-0011"');p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.Tests/TestData/Corpus/m0-mir-invalid/null_literal.diagnostics.txt');s=p.read_text().replace('Use fallible functions or an explicit option type when available.','Use Option<T> with Some(value) or None.');p.write_text(s)
p=Path('src/Copeland/Copeland.TS.MSBuild/CopelandCompile.cs');s=p.read_text();start=s.index('    private static void AppendCompilerPayloadFingerprint(');end=s.index('\n    private static bool IsCurrent',start);s=s[:start]+'''    private static void AppendCompilerPayloadFingerprint(IncrementalHash hash, System.Reflection.Assembly assembly)
    {
        AppendCompilerPayloadIdentityAndBytes(hash, assembly.GetName(), assembly.Location);
    }

    private static void AppendCompilerPayloadIdentityAndBytes(IncrementalHash hash, System.Reflection.AssemblyName identity, string assemblyPath)
    {
        Append(hash, identity.Name ?? "unknown");
        Append(hash, identity.Version?.ToString() ?? "unknown");
        if (!File.Exists(assemblyPath))
        {
            return;
        }
        hash.AppendData(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
    }
'''+s[end:];p.write_text(s)
p=Path('tests/Copeland/Copeland.TS.MSBuild.Tests/CompilerPayloadFingerprintTests.cs');s=p.read_text();start=s.index('            var firstContext');end=s.index('\n        }\n        finally',start);s=s[:start]+'''            Assert.Equal(AssemblyName.GetAssemblyName(first).FullName, AssemblyName.GetAssemblyName(second).FullName);
            Assert.NotEqual(Fingerprint(first), Fingerprint(second));'''+s[end:];s=s.replace('using System.Runtime.Loader;\n','').replace('private static string Fingerprint(Assembly assembly)','private static string Fingerprint(string path)').replace('GetMethod("AppendCompilerPayloadFingerprint"','GetMethod("AppendCompilerPayloadIdentityAndBytes"').replace('method.Invoke(null, [hash, assembly]);','method.Invoke(null, [hash, AssemblyName.GetAssemblyName(path), path]);');p.write_text(s)
