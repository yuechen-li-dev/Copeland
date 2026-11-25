using System.Text.Json;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed partial class CliIntegrationTests
{
    [Fact]
    public async Task Published_Node_and_CLR_round_trip_generated_nested_nullable_Newtonsoft_DTOs()
    {
        using var project = new TempDir();
        string sample = Path.Combine(GetRepoRoot(), "samples", "copeland-ts", "nuget-roundtrip-m5");
        Directory.CreateDirectory(Path.Combine(project.Path, "src"));
        foreach (string file in new[] { "App.csproj", "manifest.tsx", "tsconfig.tsx", "nuget-worker.json", "src/Main.ts" })
        {
            File.Copy(Path.Combine(sample, file), Path.Combine(project.Path, file));
        }

        await BuildNugetSample(project, "node");
        CliResult generation = await RunCliAsync(project.Path, "nuget", "generate", ".");
        Assert.True(generation.ExitCode == 0, generation.StdErr);
        string generated = await File.ReadAllTextAsync(Path.Combine(project.Path, "src", "generated", "Nuget.ts"));
        Assert.Contains("backup: Option<Address>;", generated);
        Assert.Contains("tags: Option<string>[];", generated);
        Assert.Contains("export async function DeserializeShipment(arg0: string): Option<Shipment> ! NugetError", generated);
        CliResult check = await RunCliAsync(project.Path, "check", "--standalone", ".");
        Assert.True(check.ExitCode == 0, check.StdErr + check.StdOut);
        await BuildNugetSample(project, "clr");

        using var deployed = new TempDir();
        string nodePath = RelocateNugetPublish(project, deployed, "node");
        string clrPath = RelocateNugetPublish(project, deployed, "clr");
        Assert.True(File.Exists(Path.Combine(deployed.Path, "node", "copeland-nuget", "worker", "Newtonsoft.Json.dll")));
        Assert.True(File.Exists(Path.Combine(deployed.Path, "clr", "Newtonsoft.Json.dll")));
        Assert.False(Directory.Exists(Path.Combine(deployed.Path, "clr", "copeland-nuget")));

        using var outside = new TempDir();
        CliResult node = await RunExecutableAsync("node", outside.Path, nodePath);
        CliResult clr = await RunExecutableAsync("dotnet", outside.Path, clrPath);
        Assert.True(node.ExitCode == 0, node.StdErr);
        Assert.True(clr.ExitCode == 0, clr.StdErr);
        Assert.Equal(node.StdOut, clr.StdOut);

        using JsonDocument data = JsonDocument.Parse(node.StdOut);
        JsonElement shipment = data.RootElement;
        Assert.Equal("Ship \"the docs\" 🌍", shipment.GetProperty("name").GetString());
        JsonElement address = shipment.GetProperty("address");
        Assert.Equal("Montréal", address.GetProperty("city").GetString());
        Assert.Equal(JsonValueKind.Null, address.GetProperty("unit").ValueKind);
        Assert.Equal(0.0, address.GetProperty("floor").GetDouble());
        Assert.Equal("4B", shipment.GetProperty("backup").GetProperty("unit").GetString());
        Assert.False(shipment.GetProperty("active").GetBoolean());
        Assert.Equal(2, shipment.GetProperty("stops").GetArrayLength());
        JsonElement tags = shipment.GetProperty("tags");
        Assert.Equal(JsonValueKind.Null, tags[0].ValueKind);
        Assert.Equal(string.Empty, tags[1].GetString());
    }

    private static string RelocateNugetPublish(TempDir project, TempDir deployed, string target)
    {
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(project.Path, target + ".json")));
        string destination = Path.Combine(deployed.Path, target);
        foreach (JsonElement output in receipt.RootElement.GetProperty("outputs").EnumerateArray())
        {
            string path = output.GetProperty("path").GetString()!;
            string file = Path.Combine(destination, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(Path.Combine(project.Path, "dist", target, path), file);
        }
        string entry = receipt.RootElement.GetProperty("entryOutputPath").GetString()!;
        return Path.Combine(destination, entry);
    }
}
