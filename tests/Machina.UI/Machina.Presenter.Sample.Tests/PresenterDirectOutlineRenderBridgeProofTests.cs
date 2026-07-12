using System.Diagnostics;
using System.Text.Json;
using Machina.Pipeline;
using Machina.Presenter.Sample;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

public sealed class PresenterDirectOutlineRenderBridgeProofTests
{
    [Fact]
    public void Presenter_DefaultBehavior_DoesNotIncludeDirectOutlineProof()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse([]);
        var frame = Render(new PresenterProofOptions());
        var ids = frame.Resolved.Nodes.Keys.Select(key => key.Value).ToHashSet(StringComparer.Ordinal);

        Assert.False(options.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
        Assert.DoesNotContain($"{PresenterDirectOutlineRenderBridgeProofLayout.SectionId}/{PresenterDirectOutlineRenderBridgeProofLayout.ProofImageSlotLeafId}", ids);
    }

    [Fact]
    public void Presenter_DirectOutlineProof_IsOptIn()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse(["--include-direct-outline-render-bridge-proof"]);
        var frame = Render(options.ProofOptions);
        var ids = frame.Resolved.Nodes.Keys.Select(key => key.Value).ToHashSet(StringComparer.Ordinal);

        Assert.True(options.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
        Assert.Contains($"{PresenterDirectOutlineRenderBridgeProofLayout.SectionId}/{PresenterDirectOutlineRenderBridgeProofLayout.ProofImageSlotLeafId}", ids);
        Assert.Contains($"{PresenterDirectOutlineRenderBridgeProofLayout.SectionId}/{PresenterDirectOutlineRenderBridgeProofLayout.AlignmentGridImageSlotLeafId}", ids);
    }

    [Fact]
    public async Task Presenter_DirectOutlineProof_UsesRenderBridge()
    {
        PresenterDirectOutlineRenderBridgeProofRenderResult result =
            await PresenterDirectOutlineRenderBridgeProofRenderer.RenderStandaloneAsync(440, 384, 440, 152);

        Assert.NotEmpty(result.ProofCases);
        Assert.NotEmpty(result.AlignmentCases);
        Assert.All(result.ProofCases, item => Assert.Equal(item.CaseId, item.RenderResult.Request.DebugLabel));
        Assert.All(result.AlignmentCases, item => Assert.Equal(item.CaseId, item.RenderResult.Request.DebugLabel));
    }

    [Fact]
    public async Task Presenter_DirectOutlineProof_ContainsHeaderButtonCardClippingCases()
    {
        PresenterDirectOutlineRenderBridgeProofRenderResult result =
            await PresenterDirectOutlineRenderBridgeProofRenderer.RenderStandaloneAsync(440, 384, 440, 152);

        string[] proofTexts = result.ProofCases.Select(item => item.RenderResult.Request.Text).ToArray();
        string[] alignmentCaseIds = result.AlignmentCases.Select(item => item.CaseId).ToArray();

        Assert.Contains("Machina Presenter", proofTexts);
        Assert.Contains("DirectOutlineStatic", proofTexts);
        Assert.Contains("Render bridge proof", proofTexts);
        Assert.Contains("Static/reference backend", proofTexts);
        Assert.Contains("MSDF experimental remains opt-in", proofTexts);
        Assert.Contains("Email updates", proofTexts);
        Assert.Contains("Save changes", proofTexts);
        Assert.Contains("Status card", proofTexts);
        Assert.Contains(proofTexts, text => text.Contains("clip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(proofTexts, text => text.Contains("\n", StringComparison.Ordinal));
        Assert.Equal(["alignment-left", "alignment-center", "alignment-right", "alignment-caption-left", "alignment-caption-center", "alignment-caption-right"], alignmentCaseIds);
    }

    [Fact]
    public void ExportPresenter_WithDirectOutlineRenderBridgeProof_WritesArtifact()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-export-tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "presenter-direct-outline-render-bridge-proof.png");

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                outputPath,
                new PresenterProofOptions(IncludeDirectOutlineRenderBridgeProof: true),
                new PresenterNavigationExportOptions(
                    true,
                    SelectedSectionId: "text",
                    SelectedTabId: "direct-outline"),
                StandardTheme.Default);

            Assert.True(result.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("text.direct-outline", result.NavigationPageId);
            Assert.True(File.Exists(result.OutputPath));

            byte[] bytes = File.ReadAllBytes(result.OutputPath);
            Assert.True(bytes.Length > 8);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExportPresenter_DefaultBehavior_UsesNavigationShell()
    {
        PresenterProgramOptions options = PresenterProgramOptions.Parse(["--export-only"]);
        string outputDirectory = Path.Combine(Path.GetTempPath(), "machina-presenter-export-tests", Guid.NewGuid().ToString("N"));
        string outputPath = Path.Combine(outputDirectory, "presenter-default.png");

        try
        {
            PresenterExportResult result = PresenterExporter.Export(
                DemoState.Default,
                outputPath,
                new PresenterProofOptions());

            Assert.True(options.ExportOnly);
            Assert.False(options.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
            Assert.False(result.ProofOptions.IncludeDirectOutlineRenderBridgeProof);
            Assert.True(result.IncludesNavigationShell);
            Assert.Equal("overview.home", result.NavigationPageId);
            Assert.True(File.Exists(result.OutputPath));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void FontPhaseCloseoutManifest_WritesJson()
    {
        string outputDirectory = CreateManifestOutputDirectory();

        try
        {
            RunManifestScript(outputDirectory);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "font-phase-closeout-manifest.json")));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void FontPhaseCloseoutManifest_WritesText()
    {
        string outputDirectory = CreateManifestOutputDirectory();

        try
        {
            RunManifestScript(outputDirectory);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "font-phase-closeout-manifest.txt")));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void FontPhaseCloseoutManifest_RecordsDirectOutlineStatus()
    {
        string outputDirectory = CreateManifestOutputDirectory();

        try
        {
            RunManifestScript(outputDirectory);
            using JsonDocument document = LoadManifestJson(outputDirectory);

            Assert.Equal("static-reference-path", document.RootElement.GetProperty("directOutlineStatic").GetProperty("status").GetString());
            Assert.True(document.RootElement.GetProperty("directOutlineStatic").GetProperty("presenterProof").GetBoolean());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void FontPhaseCloseoutManifest_RecordsMsdfExperimentalStatus()
    {
        string outputDirectory = CreateManifestOutputDirectory();

        try
        {
            RunManifestScript(outputDirectory);
            using JsonDocument document = LoadManifestJson(outputDirectory);

            Assert.Equal("explicit-experimental-scalable", document.RootElement.GetProperty("msdf").GetProperty("status").GetString());
            Assert.Equal("M9f", document.RootElement.GetProperty("msdf").GetProperty("alignmentRepair").GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void FontPhaseCloseoutManifest_RecordsProductionDefaultUnchanged()
    {
        string outputDirectory = CreateManifestOutputDirectory();

        try
        {
            RunManifestScript(outputDirectory);
            using JsonDocument document = LoadManifestJson(outputDirectory);

            Assert.False(document.RootElement.GetProperty("productionUi").GetProperty("defaultRendererChanged").GetBoolean());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public void FontPhaseCloseoutManifest_UsesStableOrdering()
    {
        string firstOutputDirectory = CreateManifestOutputDirectory();
        string secondOutputDirectory = CreateManifestOutputDirectory();

        try
        {
            RunManifestScript(firstOutputDirectory);
            RunManifestScript(secondOutputDirectory);

            string firstJson = File.ReadAllText(Path.Combine(firstOutputDirectory, "font-phase-closeout-manifest.json"));
            string secondJson = File.ReadAllText(Path.Combine(secondOutputDirectory, "font-phase-closeout-manifest.json"));
            string firstText = File.ReadAllText(Path.Combine(firstOutputDirectory, "font-phase-closeout-manifest.txt"));
            string secondText = File.ReadAllText(Path.Combine(secondOutputDirectory, "font-phase-closeout-manifest.txt"));

            Assert.Equal(firstJson, secondJson);
            Assert.Equal(firstText, secondText);
        }
        finally
        {
            DeleteDirectory(firstOutputDirectory);
            DeleteDirectory(secondOutputDirectory);
        }
    }

    private static MachinaFrame Render(PresenterProofOptions proofOptions)
    {
        var document = SettingsScreen.Build(DemoState.Default, CreateTheme(), proofOptions);
        var frame = new MachinaRasterPipeline().Render(
            document,
            SettingsScreen.GetWidth(proofOptions),
            SettingsScreen.GetHeight(proofOptions));

        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            PresenterDirectOutlineRenderBridgeProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
        }

        return frame;
    }

    private static string CreateManifestOutputDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-font-phase-closeout-tests", Guid.NewGuid().ToString("N"));
    }

    private static void RunManifestScript(string outputDirectory)
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repoRoot, "tools", "Write-MachinaFontPhaseCloseoutManifest.ps1");

        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -OutputDir \"{outputDirectory}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot,
        };

        using Process process = Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Manifest script failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
    }

    private static JsonDocument LoadManifestJson(string outputDirectory)
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "font-phase-closeout-manifest.json")));
    }

    private static StandardTheme CreateTheme()
    {
        return StandardTheme.Default with
        {
            Button = StandardTheme.Default.Button with
            {
                Default = StandardTheme.Default.Button.Default with
                {
                    Background = Machina.Core.Styling.ColorToken.Hex(0x111827FF),
                    Foreground = Machina.Core.Styling.ColorToken.Hex(0xF9FAFBFF),
                },
            },
            Card = StandardTheme.Default.Card with
            {
                Default = StandardTheme.Default.Card.Default with
                {
                    ContentInset = 18,
                },
            },
        };
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
