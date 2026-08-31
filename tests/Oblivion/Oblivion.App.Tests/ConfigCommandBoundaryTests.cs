using Xunit;

namespace Oblivion.App.Tests;

public sealed class ConfigCommandBoundaryTests
{
    [Fact]
    public void Config_and_command_sources_have_no_UI_framework_or_generic_bus_dependency()
    {
        string root = FindRepositoryRoot();
        string application = Path.Combine(root, "src", "Oblivion", "Oblivion.App", "Application");
        string configSource = File.ReadAllText(Path.Combine(application, "OblivionConfiguration.cs"));
        string commandSource = File.ReadAllText(Path.Combine(application, "OblivionCommandRegistry.cs"));
        string combined = configSource + Environment.NewLine + commandSource;

        Assert.DoesNotContain("Avalonia", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Presenter", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandBus", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<string, string>", configSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteCommand(string", commandSource, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oblivion.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
