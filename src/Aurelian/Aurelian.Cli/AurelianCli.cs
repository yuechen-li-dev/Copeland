using System.CommandLine;
using System.Text.Json;

namespace Aurelian.Cli;

public static class AurelianCliExitCode
{
    public const int Success = 0;
    public const int ProductFailure = 1;
    public const int UsageError = 2;
    public const int SessionUnavailable = 3;
    public const int InternalFailure = 4;
}

public sealed class AurelianCli
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly AurelianAgentSessionService _sessions = new();
    private readonly Option<string?> _sessionFile = new("--session-file")
    {
        Description = "Deterministic Aurelian agent-session document.",
        Recursive = true,
    };
    private readonly Option<bool> _json = new("--json")
    {
        Description = "Write deterministic JSON.",
        Recursive = true,
    };
    private readonly Option<bool> _compact = new("--json-compact")
    {
        Description = "Write deterministic compact JSON.",
        Recursive = true,
    };
    private bool _writeIndented = true;

    public AurelianCli(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        return new AurelianCli(output, error).InvokeAsync(args, cancellationToken);
    }

    public RootCommand CreateRootCommand()
    {
        RootCommand root = new("Deterministic headless Aurelian runtime control for agents.");
        root.Options.Add(_sessionFile);
        root.Options.Add(_json);
        root.Options.Add(_compact);

        Command session = new("session", "Start, actuate, tick, query, and describe a headless session.");
        session.Subcommands.Add(StartCommand());
        session.Subcommands.Add(ActCommand());
        session.Subcommands.Add(TickCommand());
        session.Subcommands.Add(QueryCommand());
        session.Subcommands.Add(DescribeCommand());

        Argument<string> replayPath = new("trace");
        Option<bool> assertIdentical = new("--assert-identical");
        Command replay = new("replay", "Recompute deterministic state and draw identities.");
        replay.Arguments.Add(replayPath);
        replay.Options.Add(assertIdentical);
        replay.SetAction(parseResult => WriteResult(
            _sessions.Replay(
                Path.GetFullPath(parseResult.GetValue(replayPath)!),
                parseResult.GetValue(assertIdentical)),
            Json(parseResult)));

        Command schema = new("schema", "Describe this CLI as machine-readable data.");
        schema.SetAction(parseResult =>
        {
            WriteJson(new
            {
                schema = "aurelian.cli.schema.v1",
                commands = new[]
                {
                    new { id = "session.start", arguments = new[] { "--session-file", "--id" } },
                    new { id = "session.act", arguments = new[] { "--session-file", "--request" } },
                    new { id = "session.tick", arguments = new[] { "--session-file", "--frames", "--trace-out" } },
                    new { id = "session.query", arguments = new[] { "--session-file", "--entity" } },
                    new { id = "session.describe-frame", arguments = new[] { "--session-file" } },
                    new { id = "replay", arguments = new[] { "trace", "--assert-identical" } },
                },
                execution = "AurelianRuntimeSession over Dominatus; World actuation records; Rendering.Null",
                exitCodes = new { success = 0, productFailure = 1, usage = 2, sessionUnavailable = 3, internalFailure = 4 },
            });
            return AurelianCliExitCode.Success;
        });

        root.Subcommands.Add(session);
        root.Subcommands.Add(replay);
        root.Subcommands.Add(schema);
        return root;
    }

    public async Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default)
    {
        RootCommand root = CreateRootCommand();
        ParseResult parse = root.Parse(args);
        _writeIndented = !parse.GetValue(_compact);
        if (parse.Errors.Count > 0)
        {
            return Usage(parse.Errors.Select(error => error.Message));
        }

        try
        {
            return await parse.InvokeAsync(new InvocationConfiguration
            {
                Output = _output,
                Error = _error,
                EnableDefaultExceptionHandler = false,
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            WriteJson(new
            {
                succeeded = false,
                diagnostics = new[] { new { code = "AURELIAN-CLI-INTERNAL", severity = "error", message = exception.Message } },
            });
            return AurelianCliExitCode.InternalFailure;
        }
    }

    private Command StartCommand()
    {
        Option<string?> id = new("--id") { Description = "Optional stable session id." };
        Command command = new("start", "Create a fresh headless session document.");
        command.Options.Add(id);
        command.SetAction(parseResult => WithSessionFile(parseResult, path => WriteResult(
            _sessions.Start(path, parseResult.GetValue(id)),
            Json(parseResult))));
        return command;
    }

    private Command ActCommand()
    {
        Option<string?> request = new("--request")
        {
            Description = "JSON actuation request; currently SpawnUnitRequest.",
        };
        Command command = new("act", "Apply one typed Aurelian world actuation transactionally.");
        command.Options.Add(request);
        command.SetAction(parseResult => WithSessionFile(parseResult, path =>
        {
            string? json = parseResult.GetValue(request);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Usage(["Option '--request' is required."]);
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                return WriteResult(_sessions.Act(path, document.RootElement), Json(parseResult));
            }
            catch (JsonException exception)
            {
                return WriteResult(
                    AurelianAgentSessionStore.Failure<object>("AURELIAN-CLI-ACT-JSON", exception.Message),
                    Json(parseResult));
            }
        }));
        return command;
    }

    private Command TickCommand()
    {
        Option<int> frames = new("--frames")
        {
            Description = "Number of deterministic 60 Hz ticks.",
            DefaultValueFactory = _ => 1,
        };
        Option<string?> traceOut = new("--trace-out")
        {
            Description = "Optional deterministic replay envelope path.",
        };
        Command command = new("tick", "Tick through the Dominatus-backed runtime session.");
        command.Options.Add(frames);
        command.Options.Add(traceOut);
        command.SetAction(async (parseResult, cancellationToken) => await WithSessionFileAsync(
            parseResult,
            async path => WriteResult(
                await _sessions.TickAsync(
                    path,
                    parseResult.GetValue(frames),
                    parseResult.GetValue(traceOut),
                    cancellationToken),
                Json(parseResult))));
        return command;
    }

    private Command QueryCommand()
    {
        Option<ulong> entity = new("--entity") { Description = "Stable entity id." };
        Command command = new("query", "Query one bounded semantic entity snapshot.");
        command.Options.Add(entity);
        command.SetAction(parseResult => WithSessionFile(parseResult, path => WriteResult(
            _sessions.Query(path, parseResult.GetValue(entity)),
            Json(parseResult))));
        return command;
    }

    private Command DescribeCommand()
    {
        Command command = new("describe-frame", "Describe exact Null-render passes and draws.");
        command.SetAction(parseResult => WithSessionFile(parseResult, path => WriteResult(
            _sessions.Describe(path),
            Json(parseResult))));
        return command;
    }

    private int WithSessionFile(ParseResult parseResult, Func<string, int> action)
    {
        string? path = parseResult.GetValue(_sessionFile);
        return string.IsNullOrWhiteSpace(path)
            ? Usage(["Option '--session-file' is required."])
            : action(Path.GetFullPath(path));
    }

    private async Task<int> WithSessionFileAsync(ParseResult parseResult, Func<string, Task<int>> action)
    {
        string? path = parseResult.GetValue(_sessionFile);
        return string.IsNullOrWhiteSpace(path)
            ? Usage(["Option '--session-file' is required."])
            : await action(Path.GetFullPath(path));
    }

    private int WriteResult<T>(AurelianAgentResult<T> result, bool json)
    {
        if (json || result.Value is not null)
        {
            object payload = result.Value is null
                ? new { succeeded = false, diagnostics = result.Diagnostics }
                : result.Value;
            WriteJson(payload);
        }
        else
        {
            foreach (AurelianAgentDiagnostic diagnostic in result.Diagnostics)
            {
                _error.WriteLine($"{diagnostic.Severity}:{diagnostic.Code}:{diagnostic.Message}");
            }
        }

        if (result.Succeeded)
        {
            return AurelianCliExitCode.Success;
        }
        return result.Diagnostics.Any(diagnostic => diagnostic.Code == "AURELIAN-CLI-SESSION-NOT-FOUND")
            ? AurelianCliExitCode.SessionUnavailable
            : AurelianCliExitCode.ProductFailure;
    }

    private int Usage(IEnumerable<string> messages)
    {
        AurelianAgentDiagnostic[] diagnostics = messages.Select(message =>
            new AurelianAgentDiagnostic("AURELIAN-CLI-USAGE", "error", message)).ToArray();
        WriteJson(new { succeeded = false, diagnostics });
        return AurelianCliExitCode.UsageError;
    }

    private bool Json(ParseResult parseResult)
    {
        return parseResult.GetValue(_json) || parseResult.GetValue(_compact);
    }

    private void WriteJson<T>(T value)
    {
        JsonSerializerOptions options = new(AurelianAgentSessionStore.JsonOptions)
        {
            WriteIndented = _writeIndented,
        };
        _output.WriteLine(JsonSerializer.Serialize(value, options));
    }
}
