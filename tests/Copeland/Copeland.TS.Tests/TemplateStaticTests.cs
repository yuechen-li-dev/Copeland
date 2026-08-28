using Copeland.TS.Syntax;
using Copeland.TS.Templates;
using Copeland.TS.Compiler;
using System.Text;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TemplateStaticTests
{
    [Theory]
    [InlineData("CSharp", "src/App.ts", "COPE-ARTIFACT-0011")]
    [InlineData("UnknownLanguage", "src/App.ts", "COPE-ARTIFACT-0003")]
    public void Diagnoses_Invalid_Typed_Source_Language_Or_Extension(string language, string path, string diagnosticId)
    {
        string source = $$"""
template<> App: ProjectTree {
    emit(sourceFile<{{language}}>("{{path}}", code { export function app(): void { } }));
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "App");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Rejects_Imported_Identifier_Injection()
    {
        const string source = """
template<static projectName: string = "Safe; public class Injected { }"> App: ProjectTree {
    emit(sourceFile<CSharp>("Program.cs", { ProjectNamespace: projectName }, code {
        namespace ProjectNamespace;
    }));
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "App");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-ARTIFACT-0010");
    }

    [Fact]
    public void Rejects_Malformed_CSharp_Typed_Source_Before_Materialization()
    {
        const string source = """
template<> App: ProjectTree {
    emit(sourceFile<CSharp>("Program.cs", code {
        public class InvalidSyntax { public void M() => ; }
    }));
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "App");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-ARTIFACT-0012");
    }

    [Fact]
    public void Materializes_Typed_Source_Bodies_With_Hygienic_Identifier_Imports()
    {
        const string source = """
template<static projectName: string = "HelloCopeland"> App: ProjectTree {
    emit(sourceFile<CSharp>("Program.cs", { ProjectNamespace: projectName }, code {
        namespace ProjectNamespace;
        public static class Program { }
    }));
    emit(sourceFile<CopelandTS>("src/App.tsx", code {
        export function App(): Document {
            return <Document><Paragraph>Hello</Paragraph></Document>;
        }
    }));
    emit(testFile<CopelandTest>("tests/App.tsxtest", code {
        using Xunit;
        [Fact] export function works(): void { Assert.True(true); }
    }));
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "App");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("namespace HelloCopeland;", Encoding.UTF8.GetString(result.Project!.Files.Single(file => file.Path == "Program.cs").Bytes));
        Assert.Contains("export function App", Encoding.UTF8.GetString(result.Project.Files.Single(file => file.Path == "src/App.tsx").Bytes));
    }

    [Fact]
    public void Parses_Canonical_Typed_Template_Declaration_And_Instantiation()
    {
        const string source = """
type ProjectShape = { name: string; };
record StandardProject { name: string; }
template<type TProject extends ProjectShape = StandardProject, static name: string = "Demo"> ProjectTemplate: ProjectTree {
    emit(textFile(`${name}-${nameOf<TProject>()}.txt`, "ok"));
}
template<> Entry: ProjectTree {
    return instantiate ProjectTemplate<StandardProject, name: "Hello">;
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Entry");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Hello-StandardProject.txt"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Applies_Type_And_Static_Defaults_For_Cli_Facing_Template()
    {
        const string source = """
interface Named { name: string; }
record Standard { name: string; }
template<type T extends Named = Standard, static name: string = "Default"> App: ProjectTree {
    emit(textFile(`${name}-${nameOf<T>()}.txt`, "ok"));
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "App");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Default-Standard.txt"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Applies_Static_Defaults_Through_Forward_Instantiation()
    {
        const string source = """
template<> Entry: ProjectTree { return instantiate Later<>; }
template<static name: string = "Forward"> Later: ProjectTree { emit(textFile(name, name)); }
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Entry");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Forward"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Diagnoses_Unsatisfied_Template_Type_Constraint_Directly()
    {
        const string source = """
interface Named { name: string; }
record MissingName { value: string; }
template<type T extends Named> Inner: ProjectTree { emit(textFile("x", "x")); }
template<> Entry: ProjectTree { return instantiate Inner<MissingName>; }
""";

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REQUIREMENT-0006");
    }

    [Fact]
    public void Requires_Defaults_For_Cli_Facing_Type_Parameters()
    {
        const string source = """
interface Named { name: string; }
record Standard { name: string; }
template<type T extends Named> App: ProjectTree { emit(textFile("x", "x")); }
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "App");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0010");
    }

    [Fact]
    public void One_Template_Abstraction_Classifies_Program_Presentation_And_Project_Results()
    {
        const string source = """
type ProgramEntity = { name: string; };
type Presentation = { title: string; };
template<static name: string = "Program"> ProgramTemplate: ProgramEntity { return { name }; }
template<static title: string = "Component"> ComponentTemplate: Presentation { return { title }; }
template<> ProjectTemplate: DotNetSolution {
    const project: DotNetProject = dotNetProject("Demo", [textFile("a.txt", "a")]);
    return dotNetSolution("Demo", project, [slnxFile("Demo.slnx", "Demo/Demo.csproj")]);
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        Assert.Empty(compilation.Diagnostics);
        Assert.Equal(3, compilation.BoundCompilation!.Program.Templates.Count);
        Assert.Equal(
            ["ProgramEntity", "Presentation", "DotNetSolution"],
            compilation.BoundCompilation.Program.Templates.Select(template => template.Symbol.ResultTypeDisplayName));
    }

    [Fact]
    public void Binds_And_Materializes_Schema_Checked_CsProject_TsXml()
    {
        const string source = """
template<> App: DotNetSolution {
    const definition: ProjectFile = csProjectFile("Demo.csproj", <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>);
    const project: DotNetProject = dotNetProject("Demo", [definition]);
    return dotNetSolution("Demo", project, [slnxFile("Demo.slnx", "Demo/Demo.csproj")]);
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(
            source,
            new CopelandCompilationOptions { SourcePath = "App.tsx" });
        TemplateEvaluationResult result = TemplateCompiler.Evaluate(compilation.BoundCompilation!, "App");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        FileArtifact project = result.Project!.Files.Single(file => file.Path.EndsWith(".csproj", StringComparison.Ordinal));
        string text = System.Text.Encoding.UTF8.GetString(project.Bytes);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_Invalid_CsProject_TsXml_Before_Materialization()
    {
        const string source = """
template<> App: DotNetSolution {
    const definition: ProjectFile = csProjectFile("Demo.csproj", <Project Sdk="Microsoft.NET.Sdk"><Script /></Project>);
    const project: DotNetProject = dotNetProject("Demo", [definition]);
    return dotNetSolution("Demo", project, []);
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(
            source,
            new CopelandCompilationOptions { SourcePath = "App.tsx" });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-PROJECT-XML-0001");
    }

    [Fact]
    public void Parses_Template_And_Explicit_Static_Statements()
    {
        const string source = """
template<> Demo: ProjectTree {
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
    public void Function_Shaped_Prototype_Syntax_Has_A_Focused_Migration_Diagnostic()
    {
        SyntaxTree tree = SyntaxTree.Parse("template Old(static name: string): ProjectTree { emit(textFile(name, name)); }");

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0011");
        Assert.Single(tree.Root.Members.OfType<TemplateDeclarationSyntax>());
    }

    [Fact]
    public void Evaluates_Static_Selection_And_Traversal_Deterministically()
    {
        const string source = """
template<> Demo: ProjectTree {
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
template<> Loop: ProjectTree { emit(instantiate Loop<>); }
""";
        TemplateEvaluationResult recursiveResult = TemplateCompiler.Evaluate(recursive, "Loop");
        Assert.Contains(recursiveResult.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0004");

        const string invalidPath = """
template<> Invalid: ProjectTree { emit(textFile("../outside.txt", "no")); }
""";
        TemplateEvaluationResult invalidPathResult = TemplateCompiler.Evaluate(invalidPath, "Invalid");
        Assert.Contains(invalidPathResult.Diagnostics, diagnostic => diagnostic.Id == "COPE-ARTIFACT-0001");
    }

    [Fact]
    public void Selects_An_Exhaustive_Static_Boolean_Match()
    {
        const string source = """
template<> Demo: ProjectTree {
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
template<> Invalid: ProjectTree { emit(readEnvironment("PATH")); }
""";
        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Invalid");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0005");
    }

    [Fact]
    public void Does_Not_Silently_Lower_Template_Source_As_Runtime_Code()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("template<> Demo: ProjectTree { emit(textFile(\"a.txt\", \"a\")); }");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0006");
    }

    [Fact]
    public void Declares_Templates_In_The_Ordinary_Bound_Symbol_Scope()
    {
        const string source = """
record ConsoleConfig { name: string; includeTests: boolean; }
template<type TConfig extends ConsoleConfig = ConsoleConfig> ConsoleApp: ProjectTree { emit(textFile("a.txt", "a")); }
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
template<> Demo: ProjectTree {
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
export template<> BaseProject: ProjectTree {
    emit(textFile("base.txt", "base"));
}
"""),
            new CopelandProjectSource(
                "templates/app.ts",
                "templates/app.ts",
                """
import { BaseProject as Base } from "./base";
export template<> App: ProjectTree {
    emit(instantiate Base<>);
    emit(sourceFile("Program.cs", "Console.WriteLine(\"Hello from Copeland template\");\n"));
}
"""),
        ];

        TemplateEvaluationResult result = CopelandProjectCompiler.CompileTemplates(sources, "App");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Program.cs", "base.txt"], result.Project!.Files.Select(file => file.Path));
        Assert.Contains(result.InstantiationChain, chain => chain.Contains("BaseProject", StringComparison.Ordinal));
    }

    [Fact]
    public void Binds_Typed_Static_Object_Arguments_And_Field_Projection()
    {
        const string source = """
type ConsoleConfig = {
    name: string;
    includeTests: boolean;
};

template<static config: ConsoleConfig> ConsoleApp: ProjectTree {
    static if (config.includeTests) {
        emit(textFile("Tests.txt", `Tests for ${config.name}`));
    }
    emit(sourceFile("Program.ts", `console.log("Hello from ${config.name}");`));
}

template<> Entry: ProjectTree {
    emit(instantiate ConsoleApp<config: { name: "HelloCopeland", includeTests: true }>);
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Entry");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Program.ts", "Tests.txt"], result.Project!.Files.Select(file => file.Path).OrderBy(path => path));
    }

    [Fact]
    public void Diagnoses_Invalid_Typed_Static_Object_Arguments()
    {
        const string source = """
type Config = { port: number; };
template<static config: Config> Server: ProjectTree { emit(textFile("a.txt", "a")); }
template<> Entry: ProjectTree { emit(instantiate Server<config: { port: "wrong", extra: true }>); }
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Entry");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0007");
    }

    [Fact]
    public void Traverses_Finite_Type_Field_Metadata_Deterministically()
    {
        const string source = """
type AppSettings = {
    host: string;
    port: number;
    development?: boolean;
};
template<> SettingsDocument: ProjectTree {
    static for (const field of fieldsOf<AppSettings>()) {
        emit(textFile(`${field.name}.txt`, `${field.typeName}:${field.optional}`));
    }
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "SettingsDocument");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["development.txt", "host.txt", "port.txt"], result.Project!.Files.Select(file => file.Path).OrderBy(path => path));
    }

    [Fact]
    public void Applies_Bounded_Structural_Projections_In_Declaration_Order()
    {
        const string source = """
type Config = { name: string; internal: boolean; port?: number; };
type PublicConfig = Pick<Config, "name">;
type InternalConfig = Omit<Config, "name">;
type CompleteConfig = Readonly<Required<Partial<Config>>>;
template<> Document: ProjectTree {
    static for (const field of fieldsOf<InternalConfig>()) {
        emit(textFile(`${field.name}.txt`, `${field.optional}:${field.readonly}`));
    }
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Document");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["internal.txt", "port.txt"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Enum_metadata_preserves_declaration_order_and_payload_shape()
    {
        const string source = """
enum Color { Red, Rgb(red: int, green: int, blue: int), Blue, }
template<> EnumDocument: ProjectTree {
    static for (const item of enumCasesOf<Color>()) {
        emit(textFile(`${item.name}-${item.payloadCount}.txt`, item.name));
    }
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "EnumDocument");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["Blue-0.txt", "Red-0.txt", "Rgb-3.txt"], result.Project!.Files.Select(file => file.Path));

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);
        BoundTemplateDeclaration declaration = Assert.Single(compilation.BoundCompilation!.Program.Templates);
        BoundStaticFor loop = Assert.IsType<BoundStaticFor>(Assert.Single(declaration.Plan!.Statements));
        BoundTemplateArray metadata = Assert.IsType<BoundTemplateArray>(loop.Values);
        Assert.Equal(
            ["Red", "Rgb", "Blue"],
            metadata.Elements
                .Cast<BoundTemplateStructuralObject>()
                .Select(item => Assert.IsType<BoundTemplateLiteral>(item.Fields.Single(field => field.Name == "name").Value).Value));
    }

    [Fact]
    public void Record_metadata_exposes_optional_fields_as_Option_values()
    {
        const string source = """
record User { id: int; name: string; nickname?: string; }
template<> RecordDocument: ProjectTree {
    static for (const field of fieldsOf<User>()) {
        emit(textFile(`${field.name}.txt`, `${field.typeName}:${field.optional}`));
    }
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "RecordDocument");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        TextFileArtifact nickname = Assert.IsType<TextFileArtifact>(result.Project!.Files.Single(file => file.Path == "nickname.txt"));
        Assert.Equal("Option<string>:True", Encoding.UTF8.GetString(nickname.Bytes));
    }

    [Fact]
    public void User_defined_template_type_parameters_drive_typed_metadata()
    {
        const string source = """
record User { id: int; nickname?: string; }
template<type T = User> Metadata: ProjectTree {
    static for (const field of fieldsOf<T>()) {
        emit(textFile(`${nameOf<T>()}-${field.name}.txt`, field.typeName));
    }
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundTemplateDeclaration declaration = Assert.Single(compilation.BoundCompilation!.Program.Templates);
        BoundStaticFor loop = Assert.IsType<BoundStaticFor>(Assert.Single(declaration.Plan!.Statements));
        Assert.IsType<BoundTemplateTypeMetadataArray>(loop.Values);

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(compilation.BoundCompilation, "Metadata");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["User-id.txt", "User-nickname.txt"], result.Project!.Files.Select(file => file.Path));
    }

    [Fact]
    public void Repeated_identical_template_instantiations_are_memoized()
    {
        const string source = """
template<static value: string> Label: string { return value; }
template<> Entry: ProjectTree {
    emit(textFile("a.txt", instantiate Label<value: "same">));
    emit(textFile("b.txt", instantiate Label<value: "same">));
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Entry");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(1, result.InstantiationChain.Count(chain => chain.Contains("Label", StringComparison.Ordinal)));
    }
}
