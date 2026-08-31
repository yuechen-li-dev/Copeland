using Xunit;

namespace Oblivion.App.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Missing_file_uses_typed_defaults_without_creating_a_file()
    {
        using TemporaryConfig config = new();
        OblivionConfigStore store = new(config.Path);

        OblivionConfigResult result = store.Show();

        Assert.True(result.Succeeded);
        Assert.Equal(OblivionConfig.Default, result.Config);
        Assert.False(result.Persisted);
        Assert.False(File.Exists(config.Path));
    }

    [Fact]
    public void Set_validates_typed_values_and_writes_a_complete_atomic_toml_file()
    {
        using TemporaryConfig config = new();
        OblivionConfigStore store = new(config.Path);

        Assert.True(store.Set("appearance", "dark").Succeeded);
        Assert.True(store.Set("newline", "lf").Succeeded);
        Assert.True(store.Set("style", "default").Succeeded);
        OblivionConfigResult loaded = store.Load();

        Assert.Equal(OblivionAppearance.Dark, loaded.Config!.Appearance);
        Assert.Equal(OblivionNewlinePolicy.Lf, loaded.Config.NewlinePolicy);
        Assert.Equal(OblivionStyleProfile.Default, loaded.Config.Style);
        Assert.Equal("dark", store.Get("appearance").Value);
        Assert.Empty(Directory.GetFiles(config.Directory, "*.tmp"));
        Assert.Equal(
            "appearance = \"dark\"\nnewline = \"lf\"\nstyle = \"default\"\n",
            File.ReadAllText(config.Path));
    }

    [Theory]
    [InlineData("missing", "value", "OBLIVION-CONFIG-KEY-UNKNOWN")]
    [InlineData("appearance", "blue", "OBLIVION-CONFIG-VALUE-INVALID")]
    [InlineData("newline", "native", "OBLIVION-CONFIG-VALUE-INVALID")]
    [InlineData("style", "compact", "OBLIVION-CONFIG-VALUE-INVALID")]
    public void Invalid_key_or_value_fails_without_mutating_config(
        string key,
        string value,
        string expectedCode)
    {
        using TemporaryConfig config = new();
        OblivionConfigStore store = new(config.Path);
        Assert.True(store.Set("appearance", "light").Succeeded);
        byte[] before = File.ReadAllBytes(config.Path);

        OblivionConfigResult result = store.Set(key, value);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, Assert.Single(result.Diagnostics).Code);
        Assert.Equal(before, File.ReadAllBytes(config.Path));
    }

    [Fact]
    public void Malformed_persisted_config_fails_explicitly()
    {
        using TemporaryConfig config = new();
        Directory.CreateDirectory(config.Directory);
        File.WriteAllText(config.Path, "appearance = dark\n");

        OblivionConfigResult result = new OblivionConfigStore(config.Path).Load();

        Assert.False(result.Succeeded);
        Assert.Equal("OBLIVION-CONFIG-TOML-INVALID", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("lf", "\n")]
    [InlineData("crlf", "\r\n")]
    public void Application_consumes_configured_newline_policy_during_push(
        string configuredValue,
        string expectedNewline)
    {
        using TemporaryConfig config = new();
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        OblivionConfigStore store = new(config.Path);
        Assert.True(store.Set("newline", configuredValue).Succeeded);
        OblivionApplication application = new(configStore: store);
        OblivionWorkspaceSession session = application.OpenWorkspace(vault.Root).Session!;
        string source = System.IO.Path.Combine(config.Directory, "new-card.md");
        File.WriteAllText(source, "# New card\r\n");

        OblivionStackOperationResult result = application.PushMarkdownCard(
            session,
            new OblivionPushMarkdownCardRequest(source, CardId: "new-card"));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        string cardToml = File.ReadAllText(System.IO.Path.Combine(vault.Root, "cards", "new-card.toml"));
        if (expectedNewline == "\r\n")
        {
            Assert.Contains("\r\n", cardToml, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", cardToml.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("\n", cardToml, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", cardToml, StringComparison.Ordinal);
        }
    }

    private sealed class TemporaryConfig : IDisposable
    {
        public TemporaryConfig()
        {
            Directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "oblivion-m19m-config-tests",
                Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Directory, "config.toml");
        }

        public string Directory { get; }
        public string Path { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private sealed class TemporaryVault : IDisposable
    {
        private TemporaryVault(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryVault CopyFixture()
        {
            string fixture = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "M19iNotebook.oblivion");
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "oblivion-m19m-config-vault-tests",
                Guid.NewGuid().ToString("N"));
            foreach (string sourcePath in System.IO.Directory.GetFiles(fixture, "*", SearchOption.AllDirectories))
            {
                string relativePath = System.IO.Path.GetRelativePath(fixture, sourcePath);
                string destinationPath = System.IO.Path.Combine(root, relativePath);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            return new TemporaryVault(root);
        }

        public void Dispose()
        {
            System.IO.Directory.Delete(Root, recursive: true);
        }
    }
}
