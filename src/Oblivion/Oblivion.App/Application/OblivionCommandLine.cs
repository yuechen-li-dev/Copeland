using Oblivion.Persistence;

namespace Oblivion.App;

public sealed class OblivionCommandLine
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly OblivionProductSurface _surface;

    public OblivionCommandLine(
        TextWriter output,
        TextWriter error,
        OblivionProductSurface? surface = null)
    {
        _output = output;
        _error = error;
        _surface = surface ?? new OblivionProductSurface();
    }

    public int Run(IReadOnlyList<string> args)
    {
        ParsedArguments parsed = ParsedArguments.Parse(args);
        if (parsed.Error is not null)
        {
            return WriteCommandError("OBLIVION-CLI-INVALID-ARGUMENTS", parsed.Error, parsed.Json);
        }

        if (parsed.Command is "help" or "--help" or "-h")
        {
            WriteHelp();
            return 0;
        }

        string manifestPath = parsed.WorkspacePath ?? OblivionWorkspacePaths.ResolveWorkspaceManifestPath();
        return parsed.Command switch
        {
            "inspect" => WriteResult(_surface.Inspect(manifestPath), parsed.Json, WriteInspectText),
            "pages" => WriteResult(_surface.ListPages(manifestPath), parsed.Json, WritePagesText),
            "cards" => WriteResult(
                _surface.ListCards(manifestPath, parsed.Positionals.FirstOrDefault()),
                parsed.Json,
                WriteCardsText),
            "show" => RequirePositionals(parsed, 1, values =>
                WriteResult(_surface.ShowCard(manifestPath, values[0]), parsed.Json, WriteCardText)),
            "actions" => RequirePositionals(parsed, 1, values =>
                WriteResult(_surface.ListActions(manifestPath, values[0]), parsed.Json, WriteActionsText)),
            "artifacts" => WriteResult(_surface.ListArtifacts(manifestPath), parsed.Json, WriteArtifactsText),
            "invoke" => RequirePositionals(parsed, 2, values =>
                WriteResult(
                    _surface.Invoke(manifestPath, values[0], values[1]),
                    parsed.Json,
                    WriteInvocationText)),
            "validate" => WriteResult(_surface.Validate(manifestPath), parsed.Json, WriteValidationText),
            _ => WriteCommandError(
                "OBLIVION-CLI-UNKNOWN-COMMAND",
                $"Unknown command '{parsed.Command}'. Use 'help' to list commands.",
                parsed.Json),
        };
    }

    private int RequirePositionals(
        ParsedArguments parsed,
        int required,
        Func<IReadOnlyList<string>, int> run)
    {
        if (parsed.Positionals.Count < required)
        {
            return WriteCommandError(
                "OBLIVION-CLI-MISSING-ARGUMENT",
                $"Command '{parsed.Command}' requires {required} argument(s).",
                parsed.Json);
        }

        return run(parsed.Positionals);
    }

    private int WriteResult<T>(
        OblivionProductSurfaceResult<T> result,
        bool json,
        Action<T> writeText)
    {
        if (json)
        {
            object value = result.Value is null
                ? new
                {
                    schemaVersion = OblivionProductSurface.SchemaVersion,
                    diagnostics = result.Diagnostics,
                }
                : result.Value;
            _output.WriteLine(OblivionProductJson.Serialize(value));
        }
        else if (result.Value is not null)
        {
            writeText(result.Value);
            WriteDiagnostics(result.Diagnostics);
        }
        else
        {
            WriteDiagnostics(result.Diagnostics);
        }

        return result.Succeeded ? 0 : 1;
    }

    private int WriteCommandError(string code, string message, bool json)
    {
        OblivionProductDiagnostic diagnostic = new(code, "error", message);
        if (json)
        {
            _output.WriteLine(OblivionProductJson.Serialize(new
            {
                schemaVersion = OblivionProductSurface.SchemaVersion,
                diagnostics = new[] { diagnostic },
            }));
        }
        else
        {
            _error.WriteLine($"error:{code}:{message}");
        }

        return 2;
    }

    private void WriteInspectText(OblivionProductWorkspaceSnapshot snapshot)
    {
        _output.WriteLine($"workspace={snapshot.Workspace.Id}");
        _output.WriteLine($"title={snapshot.Workspace.Title}");
        _output.WriteLine($"manifest={snapshot.Workspace.ManifestPath}");
        _output.WriteLine($"pages={snapshot.Workspace.Pages.Count}");
        _output.WriteLine($"cards={snapshot.Cards.Count}");
        _output.WriteLine($"selectedPage={snapshot.Session.SelectedPageId ?? "<none>"}");
        _output.WriteLine($"selectedCard={snapshot.Session.SelectedCardId ?? "<none>"}");
    }

    private void WritePagesText(IReadOnlyList<OblivionProductPageSummary> pages)
    {
        foreach (OblivionProductPageSummary page in pages)
        {
            _output.WriteLine($"{page.Id}\t{page.CardIds.Count}\t{page.Title}");
        }
    }

    private void WriteCardsText(IReadOnlyList<OblivionProductCardSummary> cards)
    {
        foreach (OblivionProductCardSummary card in cards)
        {
            _output.WriteLine($"{card.Id}\t{card.PageId}\t{card.Kind}\t{card.Status}\t{card.Title}");
        }
    }

    private void WriteCardText(OblivionProductCardSnapshot card)
    {
        _output.WriteLine($"id={card.Id}");
        _output.WriteLine($"page={card.PageId}");
        _output.WriteLine($"kind={card.Kind}");
        _output.WriteLine($"status={card.Status}");
        _output.WriteLine($"title={card.Title}");
        _output.WriteLine($"cardSource={card.Provenance.SourceReference ?? "<none>"}");
        _output.WriteLine($"contentSource={card.Body.SourceReference ?? "<inline>"}");
        _output.WriteLine("content:");
        _output.WriteLine(card.Body.Text);
    }

    private void WriteActionsText(IReadOnlyList<OblivionProductActionSnapshot> actions)
    {
        foreach (OblivionProductActionSnapshot action in actions)
        {
            _output.WriteLine(
                $"{action.Id}\t{action.Availability}\t{action.EffectKind}\t{action.HostCapabilityRequired ?? "none"}\t{action.Label}");
        }
    }

    private void WriteArtifactsText(IReadOnlyList<OblivionProductArtifactSnapshot> artifacts)
    {
        foreach (OblivionProductArtifactSnapshot artifact in artifacts)
        {
            _output.WriteLine(
                $"{artifact.Id}\t{artifact.CardId}\t{artifact.Kind}\t{artifact.Reference ?? "<none>"}\t{artifact.SourceReference ?? "<inline>"}");
        }
    }

    private void WriteInvocationText(OblivionProductInvocationSnapshot invocation)
    {
        _output.WriteLine($"card={invocation.CardId}");
        _output.WriteLine($"action={invocation.ActionId}");
        _output.WriteLine($"effect={invocation.EffectKind}");
        _output.WriteLine($"status={invocation.Status}");
        _output.WriteLine($"message={invocation.Message}");
    }

    private void WriteValidationText(OblivionProductValidationSnapshot validation)
    {
        _output.WriteLine($"workspace={validation.WorkspaceId}");
        _output.WriteLine($"valid={validation.Valid.ToString().ToLowerInvariant()}");
        _output.WriteLine($"pages={validation.PageCount}");
        _output.WriteLine($"cards={validation.CardCount}");
        _output.WriteLine($"errors={validation.ErrorCount}");
        _output.WriteLine($"warnings={validation.WarningCount}");
    }

    private void WriteDiagnostics(IReadOnlyList<OblivionProductDiagnostic> diagnostics)
    {
        foreach (OblivionProductDiagnostic diagnostic in diagnostics)
        {
            _error.WriteLine(
                $"{diagnostic.Severity}:{diagnostic.Code}:workspace={diagnostic.WorkspaceId ?? "<none>"}:page={diagnostic.PageId ?? "<none>"}:card={diagnostic.CardId ?? "<none>"}:source={diagnostic.SourceReference ?? "<none>"}:{diagnostic.Message}");
        }
    }

    private void WriteHelp()
    {
        _output.WriteLine("Oblivion semantic workspace CLI");
        _output.WriteLine("usage: oblivion <command> [arguments] [--workspace <manifest>] [--json]");
        _output.WriteLine("commands:");
        _output.WriteLine("  inspect                 inspect workspace and initial session state");
        _output.WriteLine("  pages                   list pages in stable workspace order");
        _output.WriteLine("  cards [page-id]         list cards, optionally filtered by page");
        _output.WriteLine("  show <card-id>          show content, provenance, actions, and artifacts");
        _output.WriteLine("  actions <card-id>       list semantic product actions");
        _output.WriteLine("  artifacts               list artifacts and owning cards");
        _output.WriteLine("  invoke <card> <action>  invoke through the typed product action path");
        _output.WriteLine("  validate                reload and validate durable workspace state");
    }

    private sealed record ParsedArguments(
        string Command,
        IReadOnlyList<string> Positionals,
        string? WorkspacePath,
        bool Json,
        string? Error)
    {
        public static ParsedArguments Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                return new("inspect", [], null, false, null);
            }

            string command = args[0];
            List<string> positionals = [];
            string? workspacePath = null;
            bool json = false;
            for (int index = 1; index < args.Count; index++)
            {
                string argument = args[index];
                if (argument == "--json")
                {
                    json = true;
                    continue;
                }

                if (argument == "--workspace")
                {
                    if (index + 1 >= args.Count)
                    {
                        return new(command, positionals, workspacePath, json, "--workspace requires a manifest path.");
                    }

                    workspacePath = args[++index];
                    continue;
                }

                if (argument.StartsWith("--", StringComparison.Ordinal))
                {
                    return new(command, positionals, workspacePath, json, $"Unknown option '{argument}'.");
                }

                positionals.Add(argument);
            }

            return new(command, positionals, workspacePath, json, null);
        }
    }
}
