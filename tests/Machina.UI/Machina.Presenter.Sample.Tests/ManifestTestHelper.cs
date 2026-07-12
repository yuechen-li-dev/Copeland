using System.Text.Json;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

internal static class ManifestTestHelper
{
    public static string RepoRoot => PlaybackTestEnvironment.GetRepoRoot();

    public static JsonDocument LoadArtifactManifest(string milestone, string fileName)
    {
        return JsonDocument.Parse(File.ReadAllText(GetArtifactPath(milestone, fileName)));
    }

    public static string GetArtifactPath(string milestone, string fileName)
    {
        return Path.Combine(RepoRoot, "artifacts", milestone, fileName);
    }

    public static void AssertMilestoneAndKind(JsonElement root, string milestone, string kind)
    {
        Assert.Equal(milestone, root.GetProperty("milestone").GetString());
        Assert.Equal(kind, root.GetProperty("kind").GetString());
    }

    public static void AssertBoolean(JsonElement root, string propertyName, bool expected)
    {
        Assert.Equal(expected, root.GetProperty(propertyName).GetBoolean());
    }

    public static void AssertForbiddenFlagsFalse(JsonElement root, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            Assert.False(root.GetProperty(propertyName).GetBoolean());
        }
    }
}
