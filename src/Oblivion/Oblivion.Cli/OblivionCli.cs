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

        Command workspace = new("workspace", "Inspect, validate, or transactionally reload a workspace.");
        workspace.Subcommands.Add(CreateWorkspaceShowCommand());
        workspace.Subcommands.Add(CreateWorkspaceValidateCommand());
        workspace.Subcommands.Add(CreateWorkspaceReloadCommand());

        Command page = new("page", "Inspect semantic workspace pages.");
        page.Subcommands.Add(CreatePageListCommand());

        Command card = new("card", "Inspect semantic workspace cards.");
        card.Subcommands.Add(CreateCardListCommand());
        card.Subcommands.Add(CreateCardShowCommand());
        card.Subcommands.Add(CreateCardContentCommand());
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

        root.Subcommands.Add(workspace);
        root.Subcommands.Add(page);
        root.Subcommands.Add(card);
        root.Subcommands.Add(config);
        root.Subcommands.Add(command);
        return root;
    }

    public async Task<int> InvokeAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        RootCommand root = CreateRootCommand();
        ParseResult parseResult = root.Parse(args);
        bool json = parseResult.GetValue(_jsonOption);
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
            const string message = "Option '--workspace' is required for this command.";
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
        Command command = new("run", "Run one command against a process-local App session.");
        command.Arguments.Add(commandIdArgument);
        command.SetAction(parseResult =>
        {
            OblivionControlResult<OblivionCommandRunInfo> result = _control.RunCommand(
                Workspace(parseResult),
                parseResult.GetValue(commandIdArgument)!);
            return WriteResult(result, Json(parseResult), value =>
            {
                _output.WriteLine($"Executed {value.Id}: {value.Title}");
                _output.WriteLine($"Scope: {value.Scope}");
                _output.WriteLine($"Affected Cards: {value.AffectedCards}");
            });
        });
        return command;
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
        _output.WriteLine($"Actions: {(card.Actions.Count == 0 ? "<none>" : string.Join(", ", card.Actions))}");
        _output.WriteLine("Preview:");
        _output.WriteLine(card.ContentPreview);
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
        _output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private string Workspace(ParseResult parseResult)
    {
        return Path.GetFullPath(parseResult.GetValue(_workspaceOption)!);
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

        int commandIndex = Array.IndexOf(args, "command");
        if (commandIndex >= 0 && commandIndex + 1 < args.Length && args[commandIndex + 1] == "list")
        {
            return false;
        }

        return true;
    }

    private bool Json(ParseResult parseResult)
    {
        return parseResult.GetValue(_jsonOption);
    }

    private static int FailureCode(IReadOnlyList<OblivionControlDiagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic => diagnostic.Code is
            "missing-workspace-manifest" or
            "workspace-unreadable")
                ? OblivionCliExitCode.WorkspaceUnavailable
                : OblivionCliExitCode.ProductFailure;
    }
}
