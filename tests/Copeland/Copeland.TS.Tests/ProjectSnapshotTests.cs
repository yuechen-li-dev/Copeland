using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProjectSnapshotTests
{
    [Fact]
    public void Snapshot_reuses_project_module_binding_and_accepts_an_unsaved_overlay()
    {
        CopelandProjectSnapshot snapshot = CopelandProjectCompiler.CreateSnapshot(
        [
            new CopelandProjectSource("Library.ts", "C:/workspace/Library.ts", "export function Score(value: number): number { return value; }"),
            new CopelandProjectSource("App.ts", "C:/workspace/App.ts", "import { Score } from \"./Library\"; function Main(): number { return Score(1); }"),
        ]);

        CopelandProjectCompilation baseline = snapshot.CompileToMir();
        Assert.True(baseline.Success);
        CopelandProjectModuleCompilation app = Assert.Single(baseline.Modules, module => module.LogicalPath == "App.ts");
        CopelandProjectImport import = Assert.Single(app.Imports);
        Assert.Equal("Library.ts", import.TargetLogicalPath);
        Assert.Equal("Score", import.ExportedName);

        CopelandProjectCompilation unsaved = snapshot
            .WithSourceText("C:/workspace/App.ts", "import { Score } from \"./Library\"; function Main(): number { return Score(); }")
            .CompileToMir();

        Assert.Contains(unsaved.Diagnostics, diagnostic => diagnostic.SourcePath == "C:/workspace/App.ts");
    }
}
