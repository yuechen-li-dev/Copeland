using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oblivion.App;

namespace Oblivion.Cli;

public static class OblivionCliExitCode
{
    public const int Success = 0;
    public const int ProductFailure = 1;
    public const int UsageError = 2;
    public const int WorkspaceUnavailable = 3;
    public const int InternalFailure = 4;
}

public sealed class OblivionCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly OblivionWorkspaceControl _control;
    private readonly OblivionConfigStore _configStore;
    private readonly Option<string?> _workspaceOption;
    private readonly Option<bool> _jsonOption;
    private readonly Option<bool> _jsonCompactOption;
    private readonly Option<string?> _sessionFileOption;
    private bool _writeIndented = true;
    private string? _discoveredWorkspace;

    public OblivionCli(
        TextWriter output,
        TextWriter error,
        OblivionWorkspaceControl? control = null,
        OblivionConfigStore? configStore = null)
    {
        _output = output;
        _error = error;
        _configStore = configStore ?? new OblivionConfigStore();
        _control = control ?? new OblivionWorkspaceControl(
            new OblivionApplication(configStore: _configStore));
        _workspaceOption = new Option<string?>("--workspace")
        {
            Description = "Explicit structured Oblivion vault root.",
            Recursive = true,
        };
        _workspaceOption.Aliases.Add("-w");
        _jsonOption = new Option<bool>("--json")
        {
            Description = "Write one deterministic JSON result to stdout.",
            Recursive = true,
        };
        _jsonCompactOption = new Option<bool>("--json-compact")
        {
            Description = "Write one deterministic compact JSON result to stdout.",
            Recursive = true,
        };
        _sessionFileOption = new Option<string?>("--session-file")
        {
            Description = "Read and atomically update a deterministic session document.",
            Recursive = true,
        };
    }

    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        OblivionCli cli = new(output, error);
        return cli.InvokeAsync(args, cancellationToken);
    }

    public RootCommand CreateRootCommand()
    {
        RootCommand root = new("Semantic shell access to Oblivion structured workspaces.");
        root.Options.Add(_workspaceOption);
        root.Options.Add(_jsonOption);
        root.Options.Add(_jsonCompactOption);
        root.Options.Add(_sessionFileOption);

        Command workspace = new("workspace", "Inspect, validate, or transactionally reload a workspace.");
        workspace.Subcommands.Add(CreateWorkspaceShowCommand());
        workspace.Subcommands.Add(CreateWorkspaceValidateCommand());
        workspace.Subcommands.Add(CreateWorkspaceReloadCommand());

        Command page = new("page", "Inspect semantic workspace pages.");
        page.Subcommands.Add(CreatePageListCommand());

        Command card = new("card", "Inspect semantic workspace cards.");
        card.Subcommands.Add(CreateCardListCommand());
        card.Subcommands.Add(CreateCardQueryCommand());
        card.Subcommands.Add(CreateCardShowCommand());
        card.Subcommands.Add(CreateCardContentCommand());
        card.Subcommands.Add(CreateCardReadCommand());
        card.Subcommands.Add(CreateCardRenderCommand());
        card.Subcommands.Add(CreateCardLayoutCommand());
        card.Subcommands.Add(CreateCardPeekCommand());
        card.Subcommands.Add(CreateCardPushCommand());
        card.Subcommands.Add(CreateCardPopCommand());

        Command config = new("config", "Inspect or change persistent Oblivion application configuration.");
        config.Subcommands.Add(CreateConfigShowCommand());
        config.Subcommands.Add(CreateConfigGetCommand());
        config.Subcommands.Add(CreateConfigSetCommand());

        Command command = new("command", "Discover or execute process-local application commands.");
        command.Subcommands.Add(CreateCommandListCommand());
        command.Subcommands.Add(CreateCommandRunCommand());

        Command function = new("function", "Inspect or run xUnit-backed Function Cards.");
        function.Subcommands.Add(CreateFunctionRunCommand());

        Command schema = new("schema", "Describe the agent-operable command surface as data.");
        schema.SetAction(parseResult => WriteSchema(Json(parseResult)));

        Command context = new("context", "Maintain an explicit fidelity and token-budget context set.");
        context.Subcommands.Add(CreateContextAddCommand());
        context.Subcommands.Add(CreateContextRemoveCommand());
        context.Subcommands.Add(CreateContextShowCommand());
        context.Subcommands.Add(CreateContextBudgetCommand());

        root.Subcommands.Add(workspace);
        root.Subcommands.Add(page);
        root.Subcommands.Add(card);
        root.Subcommands.Add(config);
        root.Subcommands.Add(command);
        root.Subcommands.Add(function);
        root.Subcommands.Add(schema);
        root.Subcommands.Add(context);
        return root;
    }

    public async Task<int> InvokeAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        RootCommand root = CreateRootCommand();
        ParseResult parseResult = root.Parse(args);
        bool json = parseResult.GetValue(_jsonOption);
        bool compact = parseResult.GetValue(_jsonCompactOption);
        json |= compact;
        _writeIndented = !compact;
        if (parseResult.Errors.Count > 0)
        {
            if (json)
            {
                WriteJson(new
                {
                    succeeded = false,
                    diagnostics = parseResult.Errors.Select(error => new
                    {
                        code = "OBLIVION-CLI-USAGE",
                        severity = "error",
                        message = error.Message,
                    }).ToArray(),
                });
                return OblivionCliExitCode.UsageError;
            }

            foreach (System.CommandLine.Parsing.ParseError parseError in parseResult.Errors)
            {
                _error.WriteLine($"error:OBLIVION-CLI-USAGE:{parseError.Message}");
            }

            return OblivionCliExitCode.UsageError;
        }

        if (RequiresWorkspace(args) && string.IsNullOrWhiteSpace(parseResult.GetValue(_workspaceOption)))
        {
            _discoveredWorkspace = DiscoverWorkspace(Environment.CurrentDirectory);
        }

        if (RequiresWorkspace(args) &&
            string.IsNullOrWhiteSpace(parseResult.GetValue(_workspaceOption)) &&
            _discoveredWorkspace is null)
        {
            const string message = "No workspace.json was found in the current directory or its parents; pass '--workspace'.";
            if (json)
            {
                WriteJson(new
                {
                    succeeded = false,
                    diagnostics = new[]
                    {
                        new
                        {
                            code = "OBLIVION-CLI-USAGE",
                            severity = "error",
                            message,
                        },
                    },
                });
            }
            else
            {
                _error.WriteLine($"error:OBLIVION-CLI-USAGE:{message}");
            }

            return OblivionCliExitCode.UsageError;
        }

        try
        {
            InvocationConfiguration configuration = new()
            {
                Output = _output,
                Error = _error,
                EnableDefaultExceptionHandler = false,
            };
            return await parseResult.InvokeAsync(configuration, cancellationToken);
        }
        catch (Exception exception)
        {
            if (json)
            {
                WriteJson(new
                {
                    succeeded = false,
                    diagnostics = new[]
                    {
                        new
                        {
                            code = "OBLIVION-CLI-INTERNAL",
                            severity = "error",
                            message = exception.Message,
                        },
                    },
                });
            }
            else
            {
                _error.WriteLine($"error:OBLIVION-CLI-INTERNAL:{exception.Message}");
            }

            return OblivionCliExitCode.InternalFailure;
        }
    }

    private Command CreateWorkspaceShowCommand()
    {
        Command command = new("show", "Show stable semantic workspace facts.");
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionWorkspaceInfo> result = _control.Show(Workspace(parseResult));
            return WriteResult(result, Json(parseResult), WriteWorkspaceText);
        });
        return command;
    }

    private Command CreateWorkspaceValidateCommand()
    {
        Command command = new("validate", "Validate the structured vault through the product persistence path.");
        command.SetAction(parseResult =>
        {
            OblivionWorkspaceValidation result = _control.Validate(Workspace(parseResult));
            if (Json(parseResult))
            {
                WriteJson(result);
            }
            else
            {
                if (result.Valid)
                {
                    _output.WriteLine("Workspace valid.");
                    _output.WriteLine($"{result.PageCount} page(s), {result.CardCount} card(s).");
                    _output.WriteLine($"{result.ErrorCount} errors, {result.WarningCount} warnings.");
                }
                else
                {
                    WriteDiagnostics(result.Diagnostics);
                }
            }

            return result.Valid
                ? OblivionCliExitCode.Success
                : FailureCode(result.Diagnostics);
        });
        return command;
    }

    private Command CreateWorkspaceReloadCommand()
    {
        Command command = new("reload", "Qualify an App-owned process-local transactional workspace reload.");
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionWorkspaceReload> result = _control.Reload(Workspace(parseResult));
            return WriteResult(result, Json(parseResult), reload =>
            {
                _output.WriteLine($"Workspace reloaded: {reload.Workspace.WorkspaceId}");
                _output.WriteLine($"Active Page: {reload.Session.ActivePageId}");
                _output.WriteLine($"Selected Card: {reload.Session.SelectedCardId ?? "<none>"}");
            });
        });
        return command;
    }

    private Command CreatePageListCommand()
    {
        Command command = new("list", "List pages in declared semantic order.");
        command.SetAction(parseResult =>
        {
            OblivionControlResult<IReadOnlyList<OblivionPageInfo>> result =
                _control.ListPages(Workspace(parseResult));
            return WriteResult(result, Json(parseResult), pages =>
            {
                _output.WriteLine("ID\tTitle\tCards");
                foreach (OblivionPageInfo page in pages)
                {
                    _output.WriteLine($"{page.Id}\t{page.Title}\t{page.CardCount}");
                }
            });
        });
        return command;
    }

    private Command CreateCardListCommand()
    {
        Option<string?> pageOption = new("--page")
        {
            Description = "Limit results to one exact page id.",
        };
        Command command = new("list", "List cards in declared semantic order.");
        command.Options.Add(pageOption);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<IReadOnlyList<OblivionCardInfo>> result = _control.ListCards(
                Workspace(parseResult),
                parseResult.GetValue(pageOption));
            return WriteResult(result, Json(parseResult), cards =>
            {
                _output.WriteLine("ID\tPage\tKind\tStatus\tTitle\tContent");
                foreach (OblivionCardInfo card in cards)
                {
                    _output.WriteLine(
                        $"{card.Id}\t{card.PageId}\t{card.Kind}\t{card.Status}\t{card.Title}\t{card.ContentSummary}");
                }
            });
        });
        return command;
    }

    private Command CreateCardQueryCommand()
    {
        Option<string?> pageOption = new("--page");
        Option<string?> kindOption = new("--kind");
        Option<string?> statusOption = new("--status");
        Option<string?> tagOption = new("--tag");
        Option<string?> containsOption = new("--contains");
        Option<string?> fieldsOption = new("--fields")
        {
            Description = "Comma-separated fields to project without full payloads.",
        };
        Command command = new("query", "Filter Cards and return only requested semantic fields.");
        command.Options.Add(pageOption);
        command.Options.Add(kindOption);
        command.Options.Add(statusOption);
        command.Options.Add(tagOption);
        command.Options.Add(containsOption);
        command.Options.Add(fieldsOption);
        command.SetAction(parseResult =>
        {
            string[] fields = (parseResult.GetValue(fieldsOption) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return WriteResult(
                _control.QueryCards(
                    Workspace(parseResult),
                    parseResult.GetValue(pageOption),
                    parseResult.GetValue(kindOption),
                    parseResult.GetValue(statusOption),
                    parseResult.GetValue(tagOption),
                    parseResult.GetValue(containsOption),
                    fields),
                Json(parseResult),
                rows =>
                {
                    foreach (IReadOnlyDictionary<string, object?> row in rows)
                    {
                        _output.WriteLine(string.Join("\t", row.Select(pair => $"{pair.Key}={pair.Value}")));
                    }
                });
        });
        return command;
    }

    private Command CreateCardShowCommand()
    {
        Argument<string> cardIdArgument = new("card-id")
        {
            Description = "Exact semantic card id.",
        };
        Command command = new("show", "Show bounded semantic card detail.");
        command.Arguments.Add(cardIdArgument);
        command.SetAction(parseResult =>
        {
            string cardId = parseResult.GetValue(cardIdArgument)!;
            OblivionControlResult<OblivionCardDetail> result = _control.ShowCard(
                Workspace(parseResult),
                cardId);
            return WriteResult(result, Json(parseResult), WriteCardText);
        });
        return command;
    }

    private Command CreateCardPeekCommand()
    {
        Option<string?> pageOption = CreatePageOption();
        Command command = new(
            "peek",
            "Inspect the top (last) Card on a Page stack without changing the vault.");
        command.Options.Add(pageOption);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCardStackInfo> result = _control.PeekCard(
                Workspace(parseResult),
                parseResult.GetValue(pageOption));
            return WriteResult(result, Json(parseResult), value =>
            {
                _output.WriteLine($"Top Card: {value.CardId}");
                _output.WriteLine($"Title: {value.Title}");
                _output.WriteLine("Kind: Markdown");
                _output.WriteLine($"Source: {value.Source}");
            });
        });
        return command;
    }

    private Command CreateCardContentCommand()
    {
        Argument<string> cardIdArgument = new("card-id")
        {
            Description = "Exact semantic card id.",
        };
        Option<string?> pageOption = CreatePageOption();
        Command command = new("content", "Write the complete Markdown source for one Card.");
        command.Arguments.Add(cardIdArgument);
        command.Options.Add(pageOption);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCardContentResult> result = _control.GetCardContent(
                Workspace(parseResult),
                parseResult.GetValue(cardIdArgument)!,
                parseResult.GetValue(pageOption));
            return WriteResult(result, Json(parseResult), value => _output.Write(value.Content));
        });
        return command;
    }

    private Command CreateCardReadCommand()
    {
        Argument<string> cardIdArgument = new("card-id")
        {
            Description = "Exact semantic Card id.",
        };
        Option<int> offsetOption = new("--offset")
        {
            Description = "Zero-based row or edge offset.",
            DefaultValueFactory = _ => 0,
        };
        Option<int> limitOption = new("--limit")
        {
            Description = "Maximum rows or edges to return (1-500).",
            DefaultValueFactory = _ => 100,
        };
        Option<string> fidelityOption = new("--fidelity")
        {
            Description = "Projection fidelity: summary, schema, or full.",
            DefaultValueFactory = _ => "full",
        };
        Option<string?> emphasizeOption = new("--emphasize")
        {
            Description = "Comma-separated diagram node ids to mark as emphasized.",
        };
        Command command = new("read", "Read semantic Card content with bounded fidelity and paging.");
        command.Arguments.Add(cardIdArgument);
        command.Options.Add(offsetOption);
        command.Options.Add(limitOption);
        command.Options.Add(fidelityOption);
        command.Options.Add(emphasizeOption);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCardReadResult> result = _control.ReadCard(
                Workspace(parseResult),
                parseResult.GetValue(cardIdArgument)!,
                parseResult.GetValue(fidelityOption)!,
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(limitOption),
                (parseResult.GetValue(emphasizeOption) ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.Ordinal));
            return WriteResult(result, Json(parseResult), value =>
            {
                if (value.Text is not null)
                {
                    _output.Write(value.Text);
                }
                else
                {
                    _output.WriteLine(value.Summary);
                }
            });
        });
        return command;
    }

    private Command CreateCardRenderCommand()
    {
        Argument<string> cardIdArgument = new("card-id");
        Option<string?> outputOption = new("--out") { Description = "PNG output path." };
        Option<int> widthOption = new("--width") { DefaultValueFactory = _ => 1200 };
        Option<int> heightOption = new("--height") { DefaultValueFactory = _ => 700 };
        Option<int> offsetOption = new("--offset") { DefaultValueFactory = _ => 0 };
        Option<int> limitOption = new("--limit") { DefaultValueFactory = _ => 100 };
        Command command = new("render", "Render one Card headlessly to deterministic PNG.");
        command.Arguments.Add(cardIdArgument);
        command.Options.Add(outputOption);
        command.Options.Add(widthOption);
        command.Options.Add(heightOption);
        command.Options.Add(offsetOption);
        command.Options.Add(limitOption);
        command.SetAction(parseResult =>
        {
            string? outputPath = parseResult.GetValue(outputOption);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                const string message = "Option '--out' is required.";
                if (Json(parseResult))
                {
                    WriteJson(new
                    {
                        succeeded = false,
                        diagnostics = new[] { new { code = "OBLIVION-CLI-USAGE", severity = "error", message } },
                    });
                }
                else
                {
                    _error.WriteLine("error:OBLIVION-CLI-USAGE:" + message);
                }
                return OblivionCliExitCode.UsageError;
            }

            return WriteResult(
                _control.RenderCard(
                    Workspace(parseResult),
                    parseResult.GetValue(cardIdArgument)!,
                    Path.GetFullPath(outputPath),
                    parseResult.GetValue(widthOption),
                    parseResult.GetValue(heightOption),
                    parseResult.GetValue(offsetOption),
                    parseResult.GetValue(limitOption)),
                Json(parseResult),
                value => _output.WriteLine(value.OutputPath));
        });
        return command;
    }

    private Command CreateCardLayoutCommand()
    {
        Argument<string> cardIdArgument = new("card-id");
        Option<int> widthOption = new("--width") { DefaultValueFactory = _ => 1200 };
        Option<int> heightOption = new("--height") { DefaultValueFactory = _ => 700 };
        Command command = new("layout", "Inspect deterministic native Diagram layout and viewport fit.");
        command.Arguments.Add(cardIdArgument);
        command.Options.Add(widthOption);
        command.Options.Add(heightOption);
        command.SetAction(parseResult => WriteResult(
            _control.InspectDiagramLayout(
                Workspace(parseResult),
                parseResult.GetValue(cardIdArgument)!,
                parseResult.GetValue(widthOption),
                parseResult.GetValue(heightOption)),
            Json(parseResult),
            value => _output.WriteLine($"{value.PolicyIdentity}: {value.Nodes.Count} nodes, {value.Edges.Count} edges, fit {value.FitScale:F3}")));
        return command;
    }

    private Command CreateCardPushCommand()
    {
        Argument<string> markdownFileArgument = new("markdown-file")
        {
            Description = "External Markdown file to import into vault-owned content.",
        };
        Option<string?> pageOption = CreatePageOption();
        Option<string?> idOption = new("--id")
        {
            Description = "Explicit lowercase Card id; otherwise derive it from the filename.",
        };
        Option<string?> titleOption = new("--title")
        {
            Description = "Card title; otherwise use the first '# ' heading, then the filename.",
        };
        Option<string?> subtitleOption = new("--subtitle")
        {
            Description = "Optional Card subtitle.",
        };
        Command command = new(
            "push",
            "Import a Markdown file as a new Card and push it onto a Page stack.");
        command.Arguments.Add(markdownFileArgument);
        command.Options.Add(pageOption);
        command.Options.Add(idOption);
        command.Options.Add(titleOption);
        command.Options.Add(subtitleOption);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCardStackInfo> result = _control.PushMarkdownCard(
                Workspace(parseResult),
                Path.GetFullPath(parseResult.GetValue(markdownFileArgument)!),
                parseResult.GetValue(pageOption),
                parseResult.GetValue(idOption),
                parseResult.GetValue(titleOption),
                parseResult.GetValue(subtitleOption));
            return WriteResult(result, Json(parseResult), value =>
            {
                _output.WriteLine($"Pushed {value.CardId} onto {value.PageId}.");
                _output.WriteLine($"Stack size: {value.OldCount} → {value.NewCount}.");
                _output.WriteLine($"Metadata: {value.MetadataPath}");
                _output.WriteLine($"Content: {value.ContentPath}");
            });
        });
        return command;
    }

    private Command CreateCardPopCommand()
    {
        Option<string?> pageOption = CreatePageOption();
        Command command = new(
            "pop",
            "Remove the top (last) Card from a Page stack and safely delete owned files.");
        command.Options.Add(pageOption);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCardStackInfo> result = _control.PopCard(
                Workspace(parseResult),
                parseResult.GetValue(pageOption));
            return WriteResult(result, Json(parseResult), value =>
            {
                _output.WriteLine($"Popped {value.CardId} from {value.PageId}.");
                _output.WriteLine($"Stack size: {value.OldCount} → {value.NewCount}.");
                _output.WriteLine($"Removed metadata: {value.MetadataPath}");
                if (value.ContentDeleted == true)
                {
                    _output.WriteLine($"Removed content: {value.ContentPath}");
                }
                else
                {
                    _output.WriteLine("Content retained: referenced elsewhere.");
                }
            });
        });
        return command;
    }

    private static Option<string?> CreatePageOption()
    {
        return new Option<string?>("--page")
        {
            Description = "Exact Page id; otherwise use the workspace default Page.",
        };
    }

    private Command CreateConfigShowCommand()
    {
        Command command = new("show", "Show the complete typed application configuration.");
        command.SetAction(parseResult => WriteConfigShow(_configStore.Show(), Json(parseResult)));
        return command;
    }

    private Command CreateConfigGetCommand()
    {
        Argument<string> keyArgument = new("key")
        {
            Description = "Config key: appearance, newline, or style.",
        };
        Command command = new("get", "Get one typed application configuration value.");
        command.Arguments.Add(keyArgument);
        command.SetAction(parseResult => WriteConfigValue(
            _configStore.Get(parseResult.GetValue(keyArgument)!),
            Json(parseResult),
            includeSuccess: false));
        return command;
    }

    private Command CreateConfigSetCommand()
    {
        Argument<string> keyArgument = new("key")
        {
            Description = "Config key: appearance, newline, or style.",
        };
        Argument<string> valueArgument = new("value")
        {
            Description = "Typed value accepted by the selected config key.",
        };
        Command command = new("set", "Validate and atomically persist one config value.");
        command.Arguments.Add(keyArgument);
        command.Arguments.Add(valueArgument);
        command.SetAction(parseResult => WriteConfigValue(
            _configStore.Set(
                parseResult.GetValue(keyArgument)!,
                parseResult.GetValue(valueArgument)!),
            Json(parseResult),
            includeSuccess: true));
        return command;
    }

    private Command CreateCommandListCommand()
    {
        Command command = new("list", "List stable application command descriptors.");
        command.SetAction(parseResult =>
        {
            IReadOnlyList<OblivionCommandInfo> commands = _control.ListCommands();
            if (Json(parseResult))
            {
                WriteJson(commands);
            }
            else
            {
                foreach (OblivionCommandInfo descriptor in commands)
                {
                    _output.WriteLine($"{descriptor.Id,-24} {descriptor.Title}");
                }
            }

            return OblivionCliExitCode.Success;
        });
        return command;
    }

    private Command CreateCommandRunCommand()
    {
        Argument<string> commandIdArgument = new("command-id")
        {
            Description = "Exact stable application command id.",
        };
        Command command = new("run", "Run one command against an optional durable session document.");
        command.Arguments.Add(commandIdArgument);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCommandRunInfo> result = _control.RunCommand(
                Workspace(parseResult),
                parseResult.GetValue(commandIdArgument)!,
                SessionFile(parseResult));
            return WriteResult(result, Json(parseResult), value =>
            {
                _output.WriteLine($"Executed {value.Id}: {value.Title}");
                _output.WriteLine($"Scope: {value.Scope}");
                _output.WriteLine($"Affected Cards: {value.AffectedCards}");
            });
        });
        return command;
    }

    private Command CreateFunctionRunCommand()
    {
        Argument<string> cardIdArgument = new("card-id")
        {
            Description = "Exact Function Card id.",
        };
        Command command = new("run", "Run the exact xUnit test selected by one Function Card.");
        command.Arguments.Add(cardIdArgument);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionFunctionRunInfo> result = _control.RunFunction(
                Workspace(parseResult),
                parseResult.GetValue(cardIdArgument)!);
            if (result.Value is null)
            {
                return WriteResult(result, Json(parseResult), _ => { });
            }

            OblivionFunctionRunInfo value = result.Value;
            if (Json(parseResult))
            {
                WriteJson(value);
            }
            else
            {
                _output.WriteLine($"Test: {value.TestIdentity}");
                _output.WriteLine($"Outcome: {value.Outcome}");
                _output.WriteLine($"Setup: {value.Realization}");
                _output.WriteLine($"Duration: {value.DurationMilliseconds?.ToString("0.###") ?? "<unavailable>"} ms");
                if (value.CaseCount > 1)
                {
                    _output.WriteLine(
                        $"Cases: {value.PassedCases} passed, {value.FailedCases} failed, {value.SkippedCases} skipped");
                }
                if (!string.IsNullOrWhiteSpace(value.FailureMessage))
                {
                    _output.WriteLine($"Failure: {value.FailureMessage}");
                    _output.WriteLine($"Source: {value.FailureSource ?? value.Source}:{value.FailureLine?.ToString() ?? "?"}");
                }
                WriteDiagnostics(value.Diagnostics);
            }

            return value.Outcome is "Passed" or "Skipped"
                ? OblivionCliExitCode.Success
                : OblivionCliExitCode.ProductFailure;
        });
        return command;
    }

    private Command CreateContextAddCommand()
    {
        Argument<string> cardId = new("card-id");
        Option<string> fidelity = new("--fidelity")
        {
            DefaultValueFactory = _ => "summary",
            Description = "Projection fidelity: summary, schema, or full.",
        };
        Command command = new("add", "Add or replace one Card fidelity selection.");
        command.Arguments.Add(cardId);
        command.Options.Add(fidelity);
        command.SetAction(parseResult => WithRequiredSessionFile(parseResult, sessionFile => WriteResult(
            _control.AddContextCard(
                Workspace(parseResult),
                sessionFile,
                parseResult.GetValue(cardId)!,
                parseResult.GetValue(fidelity)!),
            Json(parseResult),
            WriteContextText)));
        return command;
    }

    private Command CreateContextRemoveCommand()
    {
        Argument<string> cardId = new("card-id");
        Command command = new("remove", "Remove one Card from the context set.");
        command.Arguments.Add(cardId);
        command.SetAction(parseResult => WithRequiredSessionFile(parseResult, sessionFile => WriteResult(
            _control.RemoveContextCard(
                Workspace(parseResult),
                sessionFile,
                parseResult.GetValue(cardId)!),
            Json(parseResult),
            WriteContextText)));
        return command;
    }

    private Command CreateContextShowCommand()
    {
        Command command = new("show", "Show selected fidelities and estimated budget use.");
        command.SetAction(parseResult => WithRequiredSessionFile(parseResult, sessionFile => WriteResult(
            _control.ShowContext(Workspace(parseResult), sessionFile),
            Json(parseResult),
            WriteContextText)));
        return command;
    }

    private Command CreateContextBudgetCommand()
    {
        Argument<int> tokens = new("estimated-tokens");
        Command command = new("budget", "Set the optional estimated-token ceiling.");
        command.Arguments.Add(tokens);
        command.SetAction(parseResult => WithRequiredSessionFile(parseResult, sessionFile => WriteResult(
            _control.SetContextBudget(
                Workspace(parseResult),
                sessionFile,
                parseResult.GetValue(tokens)),
            Json(parseResult),
            WriteContextText)));
        return command;
    }

    private void WriteContextText(OblivionContextSetInfo context)
    {
        foreach (OblivionContextItem item in context.Items)
        {
            _output.WriteLine($"{item.CardId}\t{item.Fidelity}\t~{item.Cost.ApproximateTokens} tokens");
        }
        _output.WriteLine($"Total estimated tokens: {context.TotalEstimatedTokens}");
        _output.WriteLine($"Budget: {context.TokenBudget?.ToString() ?? "<none>"}");
        _output.WriteLine($"Over budget: {context.OverBudget}");
    }

    private int WithRequiredSessionFile(ParseResult parseResult, Func<string, int> action)
    {
        string? sessionFile = SessionFile(parseResult);
        if (sessionFile is not null)
        {
            return action(sessionFile);
        }

        const string message = "Option '--session-file' is required for context commands.";
        if (Json(parseResult))
        {
            WriteJson(new
            {
                succeeded = false,
                diagnostics = new[] { new { code = "OBLIVION-CLI-USAGE", severity = "error", message } },
            });
        }
        else
        {
            _error.WriteLine("error:OBLIVION-CLI-USAGE:" + message);
        }
        return OblivionCliExitCode.UsageError;
    }

    private int WriteConfigShow(OblivionConfigResult result, bool json)
    {
        if (result.Config is null)
        {
            return WriteConfigFailure(result, json);
        }

        if (json)
        {
            WriteJson(new
            {
                appearance = OblivionConfigStore.FormatValue(result.Config, OblivionConfigKey.Appearance),
                newline = OblivionConfigStore.FormatValue(result.Config, OblivionConfigKey.Newline),
                style = OblivionConfigStore.FormatValue(result.Config, OblivionConfigKey.Style),
            });
        }
        else
        {
            _output.WriteLine($"Appearance: {OblivionConfigStore.FormatValue(result.Config, OblivionConfigKey.Appearance)}");
            _output.WriteLine($"Newline: {OblivionConfigStore.FormatValue(result.Config, OblivionConfigKey.Newline)}");
            _output.WriteLine($"Style: {OblivionConfigStore.FormatValue(result.Config, OblivionConfigKey.Style)}");
        }

        return OblivionCliExitCode.Success;
    }

    private int WriteConfigValue(OblivionConfigResult result, bool json, bool includeSuccess)
    {
        if (!result.Succeeded || result.Key is null || result.Value is null)
        {
            return WriteConfigFailure(result, json);
        }

        string key = OblivionConfigStore.FormatKey(result.Key.Value);
        if (json)
        {
            WriteJson(new
            {
                key,
                value = result.Value,
                succeeded = includeSuccess ? true : (bool?)null,
            });
        }
        else
        {
            _output.WriteLine(includeSuccess ? $"{key} = {result.Value}" : result.Value);
        }

        return OblivionCliExitCode.Success;
    }

    private int WriteConfigFailure(OblivionConfigResult result, bool json)
    {
        if (json)
        {
            WriteJson(new { succeeded = false, diagnostics = result.Diagnostics });
        }
        else
        {
            foreach (OblivionConfigDiagnostic diagnostic in result.Diagnostics)
            {
                _error.WriteLine($"{diagnostic.Severity}:{diagnostic.Code}:source={diagnostic.Path}:{diagnostic.Message}");
            }
        }

        return OblivionCliExitCode.ProductFailure;
    }

    private int WriteResult<T>(
        OblivionControlResult<T> result,
        bool json,
        Action<T> writeText)
    {
        if (json)
        {
            object payload = result.Value is null
                ? new
                {
                    succeeded = false,
                    diagnostics = result.Diagnostics,
                }
                : result.Value;
            WriteJson(payload);
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

        return result.Succeeded
            ? OblivionCliExitCode.Success
            : FailureCode(result.Diagnostics);
    }

    private void WriteWorkspaceText(OblivionWorkspaceInfo workspace)
    {
        _output.WriteLine($"Workspace: {workspace.WorkspaceId}");
        _output.WriteLine($"Title: {workspace.Title}");
        _output.WriteLine($"Format: {workspace.FormatVersion}");
        _output.WriteLine($"Default Page: {workspace.DefaultPageId ?? "<none>"}");
        _output.WriteLine($"Pages: {workspace.PageCount}");
        _output.WriteLine($"Cards: {workspace.CardCount}");
        _output.WriteLine($"Vault: {workspace.WorkspaceRoot}");
    }

    private void WriteCardText(OblivionCardDetail card)
    {
        _output.WriteLine($"ID: {card.Id}");
        _output.WriteLine($"Page: {card.PageId}");
        _output.WriteLine($"Title: {card.Title}");
        _output.WriteLine($"Kind: {card.Kind}");
        _output.WriteLine($"Status: {card.Status}");
        _output.WriteLine($"Tags: {(card.Tags.Count == 0 ? "<none>" : string.Join(", ", card.Tags))}");
        _output.WriteLine($"Markdown: {card.MarkdownSource ?? "<inline>"}");
        _output.WriteLine($"Provenance: {card.ProvenanceKind} {card.ProvenanceSource ?? "<none>"}");
        if (card.DiagramSourceKind is not null)
        {
            _output.WriteLine($"Diagram source: {card.DiagramSourceKind} {card.DiagramSourceReference}");
            _output.WriteLine($"Diagram symbol: {card.DiagramSymbol}");
            _output.WriteLine($"Diagram projection: {card.DiagramProjection}");
            _output.WriteLine($"Diagram fingerprint: {card.DiagramSemanticFingerprint ?? "<unavailable>"}");
            _output.WriteLine($"Diagram artifact: {card.DiagramDerivedArtifactStatus}");
            _output.WriteLine(
                $"Diagram cached appearances: " +
                $"{(card.DiagramCachedAppearances is null || card.DiagramCachedAppearances.Count == 0 ? "<none>" : string.Join(", ", card.DiagramCachedAppearances))}");
            _output.WriteLine($"Diagram requested appearance: {card.DiagramRequestedAppearance ?? "<not-applicable>"}");
            _output.WriteLine($"Diagram resolved appearance: {card.DiagramResolvedAppearance ?? "<requires-platform-resolution>"}");
            _output.WriteLine($"Diagram renderer: {card.DiagramRenderer}");
            _output.WriteLine($"Diagram preferred backend: {card.DiagramPreferredBackend ?? "<none>"}");
            _output.WriteLine(
                $"Diagram available cached backends: " +
                $"{(card.DiagramAvailableCachedBackends is null || card.DiagramAvailableCachedBackends.Count == 0 ? "<none>" : string.Join(", ", card.DiagramAvailableCachedBackends))}");
            _output.WriteLine($"Diagram active artifact backend: {card.DiagramActiveArtifactBackend ?? "<none>"}");
            _output.WriteLine($"Diagram layout policy: {card.DiagramLayoutPolicyIdentity ?? "<none>"}");
            _output.WriteLine($"Diagram renderer provenance: {card.DiagramRendererProvenance ?? "<none>"}");
        }
        if (card.TableSourceKind is not null)
        {
            _output.WriteLine($"Table source: {card.TableSourceKind} {card.TableSourceReference}");
            _output.WriteLine($"Table profile: {card.TableProfile ?? "<unavailable>"}");
            _output.WriteLine($"Table identity: {card.TableIdentity ?? "<unavailable>"}");
            _output.WriteLine($"Table schema identity: {card.TableSchemaIdentity ?? "<unavailable>"}");
            _output.WriteLine($"Table rows: {card.TableRowCount?.ToString() ?? "<unavailable>"}");
            _output.WriteLine($"Table columns: {card.TableColumnCount?.ToString() ?? "<unavailable>"}");
            _output.WriteLine($"Table column names: {FormatList(card.TableColumnNames)}");
            _output.WriteLine($"Table column types: {FormatList(card.TableColumnTypes)}");
            _output.WriteLine($"Table column identities: {FormatList(card.TableColumnIdentities)}");
            _output.WriteLine($"Table source hash: {card.TableSourceHash ?? "<unavailable>"}");
            _output.WriteLine($"Table load ms: {card.TableLoadMilliseconds?.ToString() ?? "<unavailable>"}");
        }
        if (card.FunctionSourceKind is not null)
        {
            _output.WriteLine($"Function source: {card.FunctionSourceKind} {card.FunctionSourceReference}");
            _output.WriteLine($"Function test: {card.FunctionTest}");
            _output.WriteLine($"Function discovered: {card.FunctionDiscovered}");
            _output.WriteLine($"Function identity: {card.FunctionTestIdentity ?? "<unavailable>"}");
            _output.WriteLine($"Function kind: {card.FunctionTestKind ?? "<unavailable>"}");
            _output.WriteLine($"Function cases: {card.FunctionCaseCount?.ToString() ?? "<unavailable>"}");
            _output.WriteLine($"Function source hash: {card.FunctionSourceHash ?? "<unavailable>"}");
            _output.WriteLine($"Function runner: {card.FunctionRunner ?? "<unavailable>"}");
        }
        _output.WriteLine($"Actions: {(card.Actions.Count == 0 ? "<none>" : string.Join(", ", card.Actions))}");
        _output.WriteLine("Preview:");
        _output.WriteLine(card.ContentPreview);
    }

    private static string FormatList(IReadOnlyList<string>? values)
    {
        return values is null || values.Count == 0 ? "<none>" : string.Join(", ", values);
    }

    private void WriteDiagnostics(IReadOnlyList<OblivionControlDiagnostic> diagnostics)
    {
        foreach (OblivionControlDiagnostic diagnostic in diagnostics)
        {
            _error.WriteLine(
                $"{diagnostic.Severity}:{diagnostic.Code}:source={diagnostic.Source ?? "<none>"}:{diagnostic.Message}");
        }
    }

    private void WriteJson<T>(T value)
    {
        JsonSerializerOptions options = new(JsonOptions) { WriteIndented = _writeIndented };
        _output.WriteLine(JsonSerializer.Serialize(value, options));
    }

    private string Workspace(ParseResult parseResult)
    {
        string? explicitWorkspace = parseResult.GetValue(_workspaceOption);
        return Path.GetFullPath(explicitWorkspace ?? _discoveredWorkspace!);
    }

    private string? SessionFile(ParseResult parseResult)
    {
        string? path = parseResult.GetValue(_sessionFileOption);
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
    }

    private static string? DiscoverWorkspace(string startDirectory)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "workspace.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private int WriteSchema(bool json)
    {
        var manifest = new
        {
            schema = "oblivion.cli.schema.v1",
            commands = new object[]
            {
                new { id = "workspace.show", access = "read", workspace = "required-or-discovered" },
                new { id = "workspace.validate", access = "read", workspace = "required-or-discovered" },
                new { id = "page.list", access = "read", workspace = "required-or-discovered" },
                new { id = "card.list", access = "read", arguments = new[] { "--page" } },
                new { id = "card.query", access = "read", arguments = new[] { "--page", "--kind", "--status", "--tag", "--contains", "--fields" } },
                new { id = "card.show", access = "read", arguments = new[] { "card-id" } },
                new { id = "card.content", access = "read", arguments = new[] { "card-id", "--page" } },
                new { id = "card.read", access = "read", arguments = new[] { "card-id", "--fidelity", "--offset", "--limit", "--emphasize" } },
                new { id = "card.render", access = "write-artifact", arguments = new[] { "card-id", "--out", "--width", "--height", "--offset", "--limit" } },
                new { id = "card.layout", access = "read", arguments = new[] { "card-id", "--width", "--height" } },
                new { id = "card.push", access = "write", arguments = new[] { "markdown-file", "--page", "--id", "--title", "--subtitle" } },
                new { id = "card.pop", access = "write", arguments = new[] { "--page" } },
                new { id = "command.run", access = "session-write", arguments = new[] { "command-id", "--session-file" } },
                new { id = "function.run", access = "execute", arguments = new[] { "card-id" } },
                new { id = "context.add", access = "session-write", arguments = new[] { "card-id", "--fidelity", "--session-file" } },
                new { id = "context.remove", access = "session-write", arguments = new[] { "card-id", "--session-file" } },
                new { id = "context.show", access = "read", arguments = new[] { "--session-file" } },
                new { id = "context.budget", access = "session-write", arguments = new[] { "estimated-tokens", "--session-file" } },
                new { id = "schema", access = "read", arguments = Array.Empty<string>() },
            },
            globalOptions = new[] { "--workspace", "--session-file", "--json", "--json-compact" },
            exitCodes = new { success = 0, productFailure = 1, usage = 2, workspaceUnavailable = 3, internalFailure = 4 },
            sessionSchema = OblivionSessionDocument.CurrentSchema,
            diagnostics = new[]
            {
                "OBLIVION-CLI-USAGE",
                "OBLIVION-CLI-INTERNAL",
                "OBLIVION-COMMAND-UNKNOWN",
                "OBLIVION-SESSION-SCHEMA-INVALID",
                "OBLIVION-SESSION-WORKSPACE-MISMATCH",
                "OBLIVION-SESSION-READ-FAILED",
                "OBLIVION-SESSION-WRITE-FAILED",
            },
        };
        if (json)
        {
            WriteJson(manifest);
        }
        else
        {
            _output.WriteLine("oblivion.cli.schema.v1");
            _output.WriteLine("Use --json or --json-compact for the machine-readable manifest.");
        }

        return OblivionCliExitCode.Success;
    }

    private static bool RequiresWorkspace(string[] args)
    {
        if (args.Length == 0 || args.Any(argument => argument is "--help" or "-h"))
        {
            return false;
        }

        int configIndex = Array.IndexOf(args, "config");
        if (configIndex >= 0)
        {
            return false;
        }

        if (Array.IndexOf(args, "schema") >= 0)
        {
            return false;
        }

        int commandIndex = Array.IndexOf(args, "command");
        if (commandIndex >= 0 && commandIndex + 1 < args.Length && args[commandIndex + 1] == "list")
        {
            return false;
        }

        return true;
    }

    private bool Json(ParseResult parseResult)
    {
        return parseResult.GetValue(_jsonOption) || parseResult.GetValue(_jsonCompactOption);
    }

    private static int FailureCode(IReadOnlyList<OblivionControlDiagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic => diagnostic.Code is
            "OBLIVION-MISSING-WORKSPACE-MANIFEST" or
            "OBLIVION-WORKSPACE-UNREADABLE")
                ? OblivionCliExitCode.WorkspaceUnavailable
                : OblivionCliExitCode.ProductFailure;
    }
}
