from pathlib import Path
p=Path('src/Copeland/Copeland.TS.MSBuild/CopelandCompile.cs');s=p.read_text()
s=s.replace('authoredCSharpSources, RootNamespace, moduleName, projectTypes);','authoredCSharpSources, RootNamespace, moduleName, projectTypes, LangVersion, DefineConstants, Nullable);')
s=s.replace('rootNamespace, graphArtifactName, projectTypes);','rootNamespace, graphArtifactName, projectTypes, LangVersion, DefineConstants, Nullable);')
s=s.replace('CopelandProjectTypeSet projectTypes)\n    {\n        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);','CopelandProjectTypeSet projectTypes,\n        string langVersion,\n        string defineConstants,\n        string nullable)\n    {\n        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);\n        Append(hash, langVersion);\n        Append(hash, defineConstants);\n        Append(hash, nullable);')
s=s.replace('AppendCompilerPayloadFingerprint(hash, typeof(CSharpBackend).Assembly);','AppendCompilerPayloadFingerprint(hash, typeof(CSharpBackend).Assembly);\n        AppendCompilerPayloadFingerprint(hash, typeof(MirProgram).Assembly);')
p.write_text(s,newline='\n')
