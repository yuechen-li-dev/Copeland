from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:70]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
c='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs'
j='src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs'
edit(c,'    private static readonly AsyncLocal<string?> CurrentModuleClassName = new();','    private static readonly AsyncLocal<string?> CurrentModuleClassName = new();\n    private static readonly AsyncLocal<bool> UsesNativeStrings = new();')
edit(c,'        CurrentModuleClassName.Value = moduleClassName;','        CurrentModuleClassName.Value = moduleClassName;\n        bool previousNativeStrings = UsesNativeStrings.Value;\n        UsesNativeStrings.Value = false;')
edit(c,'        foreach (var function in program.Functions) EmitFunction(writer, function, enumNames, recordsById, diagnostics);','        foreach (var function in program.Functions) EmitFunction(writer, function, enumNames, recordsById, diagnostics);\n        if (UsesNativeStrings.Value) NativeStringRuntime.Emit(writer);')
edit(c,'        CurrentModuleClassName.Value = previousModuleClassName;','        CurrentModuleClassName.Value = previousModuleClassName;\n        UsesNativeStrings.Value = previousNativeStrings;')
edit(c,'        string functionName = CSharpNameMangler.Mangle(call.FunctionName);','        string functionName = NativeCallName(call) ?? CSharpNameMangler.Mangle(call.FunctionName);')
edit(c,'MirCallExpression call => $"{CSharpNameMangler.Mangle(call.FunctionName)}','MirCallExpression call => $"{NativeCallName(call) ?? CSharpNameMangler.Mangle(call.FunctionName)}')
insert='''    private static string? NativeCallName(MirCallExpression call)
    {
        if (call.NativeOperation is null) return null;
        UsesNativeStrings.Value = true;
        return "__cope_native_" + call.NativeOperation.Value;
    }

'''
edit(c,'    private static string EmitCall(',insert+'    private static string EmitCall(')
edit(j,'    private static readonly AsyncLocal<bool> ModuleFactoryEmission = new();','    private static readonly AsyncLocal<bool> ModuleFactoryEmission = new();\n    private static readonly AsyncLocal<bool> UsesNativeStrings = new();')
edit(j,'        bool previousModuleFactoryEmission = ModuleFactoryEmission.Value;','        bool previousNativeStrings = UsesNativeStrings.Value;\n        UsesNativeStrings.Value = false;\n        bool previousModuleFactoryEmission = ModuleFactoryEmission.Value;')
edit(j,'        string sourceText = writer.ToString();','        if (UsesNativeStrings.Value) NativeStringRuntime.Emit(writer);\n        string sourceText = writer.ToString();')
edit(j,'            ModuleFactoryEmission.Value = previousModuleFactoryEmission;','            ModuleFactoryEmission.Value = previousModuleFactoryEmission;\n            UsesNativeStrings.Value = previousNativeStrings;')
edit(j,'values => $"{JavaScriptIdentifierEncoder.Encode(call.FunctionName)}','values => $"{NativeCallName(call) ?? JavaScriptIdentifierEncoder.Encode(call.FunctionName)}')
edit(j,'MirCallExpression call => $"{JavaScriptIdentifierEncoder.Encode(call.FunctionName)}','MirCallExpression call => $"{NativeCallName(call) ?? JavaScriptIdentifierEncoder.Encode(call.FunctionName)}')
edit(j,'    private static EmittedExpression EmitCall(',insert+'    private static EmittedExpression EmitCall(')
