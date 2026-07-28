using Copeland.TS.Syntax;
using Copeland.TS.Templates;
using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TemplateStaticTests
{
    [Fact]
    public void Parses_Template_And_Explicit_Static_Statements()
    {
        const string source = """
template Demo(): ProjectTree {
    static if (true) { emit(textFile("a.txt", "a")); }
    static for (const item of ["b"]) { emit(textFile(item + ".txt", item)); }
    static match "Console" { Console => { } }
}
""";
        SyntaxTree tree = SyntaxTree.Parse(source);

        Assert.DoesNotContain(tree.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        string dump = SyntaxTreeDumper.Dump(tree.Root);
        Assert.Contains("TemplateDeclaration", dump, StringComparison.Ordinal);
        Assert.Contains("StaticIfStatement", dump, StringComparison.Ordinal);
        Assert.Contains("StaticForStatement", dump, StringComparison.Ordinal);
        Assert.Contains("StaticMatchStatement", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluates_Static_Selection_And_Traversal_Deterministically()
    {
        const string source = """
template Demo(): ProjectTree {
    static if (false) { emit(textFile("inactive.txt", "no")); }
    static for (const item of ["a", "b"]) { emit(textFile(item + ".txt", item)); }
}
""";
        TemplateEvaluationResult first = TemplateCompiler.Evaluate(source, "Demo");
        TemplateEvaluationResult second = TemplateCompiler.Evaluate(source, "Demo");

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(first.Project!.ToPreviewJson("Demo"), second.Project!.ToPreviewJson("Demo"));
        Assert.Equal(["a.txt", "b.txt"], first.Project.Files.Select(file => file.Path));
    }

    [Fact]
    public void Rejects_Recursive_Template_Expansion_And_Invalid_Artifact_Paths()
    {
        const string recursive = """
template Loop(): ProjectTree { emit(Loop()); }
""";
        TemplateEvaluationResult recursiveResult = TemplateCompiler.Evaluate(recursive, "Loop");
        Assert.Contains(recursiveResult.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0004");

        const string invalidPath = """
template Invalid(): ProjectTree { emit(textFile("../outside.txt", "no")); }
""";
        TemplateEvaluationResult invalidPathResult = TemplateCompiler.Evaluate(invalidPath, "Invalid");
        Assert.Contains(invalidPathResult.Diagnostics, diagnostic => diagnostic.Id == "COPE-ARTIFACT-0001");
    }

    [Fact]
    public void Selects_An_Exhaustive_Static_Boolean_Match()
    {
        const string source = """
template Demo(): ProjectTree {
    static match true {
        true => { emit(textFile("selected.txt", "yes")); }
        false => { emit(textFile("not-selected.txt", "no")); }
    }
}
""";
        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Demo");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["selected.txt"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Rejects_Runtime_Calls_In_Template_Evaluation()
    {
        const string source = """
template Invalid(): ProjectTree { emit(readEnvironment("PATH")); }
""";
        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Invalid");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0005");
    }

    [Fact]
    public void Does_Not_Silently_Lower_Template_Source_As_Runtime_Code()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("template Demo(): ProjectTree { emit(textFile(\"a.txt\", \"a\")); }");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0006");
    }

    [Fact]
    public void Declares_Templates_In_The_Ordinary_Bound_Symbol_Scope()
    {
        const string source = """
record ConsoleConfig { name: string; includeTests: boolean; }
template ConsoleApp<TConfig extends ConsoleConfig>(): ProjectTree { emit(textFile("a.txt", "a")); }
""";
        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        Assert.NotNull(compilation.BoundCompilation);
        Assert.Single(compilation.BoundCompilation!.Program.Templates);
        Assert.True(compilation.BoundCompilation.ModuleScope!.Declarations.TryGetValue("ConsoleApp", out var symbol));
        Assert.IsType<Copeland.TS.Semantics.TemplateSymbol>(symbol);
        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void Compiler_Binds_A_Syntax_Free_Template_Plan()
    {
        const string source = """
template Demo(): ProjectTree {
    const files = [textFile("a.txt", "a")];
    static if (true) { emit(project(files)); }
}
""";
        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        BoundTemplateDeclaration declaration = Assert.Single(compilation.BoundCompilation!.Program.Templates);
        BoundTemplateBlock plan = Assert.IsType<BoundTemplateBlock>(declaration.Plan);
        BoundTemplateLocal local = Assert.IsType<BoundTemplateLocal>(plan.Statements[0]);
        Assert.IsType<BoundTemplateArray>(local.Initializer);
        BoundStaticIf conditional = Assert.IsType<BoundStaticIf>(plan.Statements[1]);
        BoundTemplateBlock selected = Assert.IsType<BoundTemplateBlock>(conditional.ThenStatement);
        BoundTemplateEmit emit = Assert.IsType<BoundTemplateEmit>(Assert.Single(selected.Statements));
        Assert.IsType<BoundArtifactConstructor>(emit.Value);

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(compilation.BoundCompilation, "Demo");
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["a.txt"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Evaluates_Imported_Template_By_Resolved_Symbol_Identity()
    {
        CopelandProjectSource[] sources =
        [
            new CopelandProjectSource(
                "templates/base.ts",
                "templates/base.ts",
                """
export template BaseProject(): ProjectTree {
    emit(textFile("base.txt", "base"));
}
"""),
            new CopelandProjectSource(
                "templates/app.ts",
                "templates/app.ts",
                """
import { BaseProject as Base } from "./base";
export template App(): ProjectTree {
    emit(Base());
    emit(sourceFile("Program.cs", "Console.WriteLine(\"Hello from Copeland template\");\n"));
}
"""),
        ];

        TemplateEvaluationResult result = CopelandProjectCompiler.CompileTemplates(sources, "App");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Program.cs", "base.txt"], result.Project!.Files.Select(file => file.Path));
        Assert.Contains(result.InstantiationChain, chain => chain.Contains("BaseProject", StringComparison.Ordinal));
    }
}
