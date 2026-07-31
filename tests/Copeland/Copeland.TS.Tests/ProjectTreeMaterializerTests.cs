using Copeland.TS.Templates;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ProjectTreeMaterializerTests
{
    [Fact]
    public void Writes_A_Deterministic_New_Project_And_Refuses_Existing_Output()
    {
        Assert.True(ProjectTree.TryCreate([new TextFileArtifact("src/a.txt", ProjectTree.EncodeText("a\r\n"), "test")], out ProjectTree? tree, out var diagnostics));
        Assert.Empty(diagnostics);

        string root = Path.Combine(Path.GetTempPath(), "copeland-template-tests", Guid.NewGuid().ToString("N"));
        try
        {
            ProjectTreeMaterializationResult first = ProjectTreeMaterializer.Materialize(tree!, root);
            Assert.True(first.Succeeded, first.Message);
            Assert.Equal("a\n", File.ReadAllText(Path.Combine(root, "src", "a.txt")));

            ProjectTreeMaterializationResult second = ProjectTreeMaterializer.Materialize(tree!, root);
            Assert.False(second.Succeeded);
            Assert.Equal("COPE-TEMPLATE-CLI-0009", second.DiagnosticId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
