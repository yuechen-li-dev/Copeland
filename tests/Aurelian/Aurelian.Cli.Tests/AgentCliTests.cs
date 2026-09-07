using System.Text.Json;
using Aurelian.Cli;
using Xunit;

namespace Aurelian.Cli.Tests;

public sealed class AgentCliTests
{
    [Fact]
    public async Task SpawnTickQueryDescribeAndReplayUseDeterministicHeadlessPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string session = Path.Combine(directory, "session.json");
        string trace = Path.Combine(directory, "trace.json");
        try
        {
            Assert.Equal(0, (await Run("session", "start", "--session-file", session, "--id", "test", "--json-compact")).ExitCode);
            CliResult act = await Run(
                "session", "act",
                "--session-file", session,
                "--request", "{\"kind\":\"SpawnUnitRequest\",\"unit\":{\"id\":2,\"kind\":\"sprite\",\"name\":\"Scout\",\"x\":12,\"y\":34,\"mesh\":\"quad\",\"material\":\"blue\"}}",
                "--json-compact");
            CliResult tick = await Run(
                "session", "tick",
                "--session-file", session,
                "--frames", "60",
                "--trace-out", trace,
                "--json-compact");
            CliResult query = await Run(
                "session", "query",
                "--session-file", session,
                "--entity", "2",
                "--json-compact");
            CliResult replay = await Run("replay", trace, "--assert-identical", "--json-compact");

            Assert.Equal(0, act.ExitCode);
            Assert.Equal(0, tick.ExitCode);
            Assert.Equal(0, query.ExitCode);
            Assert.Equal(0, replay.ExitCode);
            using JsonDocument tickJson = JsonDocument.Parse(tick.Output);
            Assert.Equal(60UL, tickJson.RootElement.GetProperty("tickIndex").GetUInt64());
            Assert.Equal(1, tickJson.RootElement.GetProperty("draw").GetProperty("drawCount").GetInt32());
            JsonElement draw = tickJson.RootElement.GetProperty("draw").GetProperty("trace").GetProperty("passes")[0].GetProperty("draws")[0];
            Assert.Equal("2", draw.GetProperty("id").GetString());
            Assert.Equal(12, draw.GetProperty("x").GetDouble());
            using JsonDocument replayJson = JsonDocument.Parse(replay.Output);
            Assert.True(replayJson.RootElement.GetProperty("identical").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaNamesDominatusAndInvalidActsReturnStructuredFailure()
    {
        CliResult schema = await Run("schema", "--json-compact");
        CliResult missing = await Run("session", "query", "--session-file", "missing.json", "--entity", "2", "--json-compact");

        Assert.Equal(0, schema.ExitCode);
        Assert.Contains("Dominatus", schema.Output, StringComparison.Ordinal);
        Assert.Equal(AurelianCliExitCode.SessionUnavailable, missing.ExitCode);
        Assert.Contains("AURELIAN-CLI-SESSION-NOT-FOUND", missing.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", missing.Output, StringComparison.Ordinal);
    }

    private static async Task<CliResult> Run(params string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await AurelianCli.RunAsync(args, output, error);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
}
