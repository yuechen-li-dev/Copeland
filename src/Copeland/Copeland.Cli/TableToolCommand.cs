using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using System.Reflection;

namespace Copeland.Cli;

/// <summary>
/// Compiler-owned inspection and localized source transformations for authored record tables.
/// Rows are projected from bound columns; they are never persisted as a second representation.
/// </summary>
internal static class TableToolCommand
{
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const int UsageExitCode = 2;
    private const int FileIoExitCode = 3;
    private const int SchemaVersion = 1;

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("COPE-TABLE-TOOL-0001", "Missing table subcommand.");
        }

        try
        {
            if (args.Contains("--source", StringComparer.Ordinal) || args.Contains("--project", StringComparer.Ordinal))
            {
                return RunProjectedLayoutTable(args);
            }

            return args[1] switch
            {
                "list" => RunList(args),
                "schema" => RunSchema(args),
                "rows" => RunRows(args),
                "query" => RunQuery(args),
                "set" => RunSet(args),
                "add-row" => RunAddRow(args),
                "delete-row" => RunDeleteRow(args),
                "validate" => RunValidate(args),
                "export" => RunExport(args),
                "import" => RunImport(args),
                _ => Usage("COPE-TABLE-TOOL-0001", $"Unknown table subcommand '{args[1]}'."),
            };
        }
        catch (TableToolException exception)
        {
            WriteFailure("table." + args[1], exception.Diagnostic, HasJsonFormat(args));
            return FailureExitCode;
        }
        catch (CopelandProjectContextException exception)
        {
            WriteFailure(
                "table." + args[1],
                new ToolDiagnostic(exception.Code, exception.Message, null, null, null),
                HasJsonFormat(args));
            return FailureExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteFailure("table." + args[1], new ToolDiagnostic("COPE-TABLE-TOOL-0018", exception.Message, null, null, null), HasJsonFormat(args));
            return FileIoExitCode;
        }
    }

    private static int RunProjectedLayoutTable(string[] args)
    {
        string? sourcePath = OptionValue(args, "--source");
        string? projectPath = OptionValue(args, "--project");
        if (sourcePath is null && projectPath is null)
        {
            throw Error(
                "COPE-TABLE-TOOL-0004",
                "Projected table inspection requires '--project <manifest.tsx>' or '--source <entry.ts>'.",
                HasJsonFormat(args));
        }

        bool json = OptionValue(args, "--format") == "json" || OptionValue(args, "--result-format") == "json";
        CopelandProjectCompilation compilation = LayoutInspectionCommand.CompileProject(
            sourcePath,
            projectPath,
            out string projectRoot,
            out string? graphFingerprint);
        if (!compilation.Success)
        {
            return WriteProjectedCompilationFailure(compilation.Diagnostics, json);
        }

        ProjectedTableSet tables = LayoutProjectedTableProvider.Create(compilation, projectRoot);
        string command = args[1];
        string[] positional = PositionalArguments(args, 2);
        if (command is "set" or "add-row" or "delete-row" or "import")
        {
            throw Error("COPE-TABLE-PROJECTED-0001", "This table is compiler-projected and read-only. Edit the originating layout/stream source instead.", json);
        }

        if (command == "list")
        {
            if (json)
            {
                WriteJson(new
                {
                    schemaVersion = SchemaVersion,
                    success = true,
                    command = "table.list",
                    graphFingerprint,
                    tables = tables.Tables.Select(projected => new { name = projected.Name, sourceKind = "projected", readOnly = true, rowCount = projected.Rows.Count }),
                });
            }
            else
            {
                foreach (ProjectedTable projected in tables.Tables)
                {
                    Console.Out.WriteLine($"{projected.Name}\t{projected.Rows.Count} rows\tprojected read-only");
                }
            }
            return SuccessExitCode;
        }

        if (positional.Length != 1)
        {
            throw Error("COPE-TABLE-TOOL-0001", $"Projected table command '{command}' requires one table identity.", json);
        }

        ProjectedTable table;
        try { table = tables.Require(positional[0]); }
        catch (InvalidOperationException exception) { throw Error("COPE-TABLE-TOOL-0005", exception.Message, json); }
        if (command == "schema")
        {
            if (json)
            {
                WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.schema", table = table.Name, sourceKind = "projected", readOnly = true, graphFingerprint, rowCount = table.Rows.Count, columns = table.Columns.Select(column => new { name = column.Name, type = column.Type }) });
            }
            else
            {
                Console.Out.WriteLine($"{table.Name} ({table.Rows.Count} rows) projected read-only");
                foreach (ProjectedColumn column in table.Columns) Console.Out.WriteLine($"{column.Name}: {column.Type}");
            }
            return SuccessExitCode;
        }

        if (command == "rows")
        {
            int offset = ParseNonNegative(OptionValue(args, "--offset") ?? "0", "--offset", json);
            int limit = ParseNonNegative(OptionValue(args, "--limit") ?? table.Rows.Count.ToString(CultureInfo.InvariantCulture), "--limit", json);
            IReadOnlyList<ProjectedColumn> columns = SelectProjectedColumns(table, OptionValue(args, "--columns"), json);
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = table.Rows.Skip(offset).Take(limit).Select(row => FilterProjectedRow(row, columns)).ToArray();
            if (json)
            {
                WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.rows", table = table.Name, sourceKind = "projected", readOnly = true, graphFingerprint, offset, rows = rows.Select((values, index) => new { row = offset + index, values }) });
            }
            else
            {
                WriteProjectedTextRows(columns, rows);
            }
            return SuccessExitCode;
        }

        if (command == "export")
        {
            if (OptionValue(args, "--format") != "csv") throw Error("COPE-TABLE-TOOL-0015", "Table export currently supports '--format csv'.", json);
            string csv = Csv.Write(table.Columns.Select(column => column.Name).ToArray(), table.Rows.Select(row => table.Columns.Select(column => ProjectedCsvValue(row[column.Name])).ToArray()).ToArray());
            string? output = OptionValue(args, "--output");
            if (output is null) Console.Out.Write(csv); else AtomicWrite(Path.GetFullPath(output), csv, expectedHash: null);
            if (json) WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.export", table = table.Name, sourceKind = "projected", readOnly = true, graphFingerprint, format = "csv", output, rowCount = table.Rows.Count });
            return SuccessExitCode;
        }

        throw Error("COPE-TABLE-TOOL-0001", $"Unknown table subcommand '{command}'.", json);
    }

    private static string RequiredOption(string[] args, string name)
        => OptionValue(args, name) ?? throw Error("COPE-TABLE-TOOL-0004", $"Option '{name}' requires a value.", HasJsonFormat(args));

    private static string? OptionValue(string[] args, string name)
    {
        for (int index = 0; index + 1 < args.Length; index += 1)
        {
            if (args[index] == name) return args[index + 1];
        }
        return null;
    }

    private static string[] PositionalArguments(string[] args, int start)
    {
        var values = new List<string>();
        for (int index = start; index < args.Length; index += 1)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal)) { index += 1; continue; }
            values.Add(args[index]);
        }
        return values.ToArray();
    }

    private static IReadOnlyList<ProjectedColumn> SelectProjectedColumns(ProjectedTable table, string? selected, bool json)
    {
        if (string.IsNullOrWhiteSpace(selected)) return table.Columns;
        var result = new List<ProjectedColumn>();
        foreach (string name in selected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ProjectedColumn? column = table.Columns.SingleOrDefault(column => column.Name == name);
            if (column is null || !result.Contains(column)) result.Add(column ?? throw Error("COPE-TABLE-TOOL-0008", $"Column '{name}' is not a column of table '{table.Name}'.", json));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, object?> FilterProjectedRow(IReadOnlyDictionary<string, object?> row, IReadOnlyList<ProjectedColumn> columns)
        => columns.ToDictionary(column => column.Name, column => row[column.Name], StringComparer.Ordinal);

    private static void WriteProjectedTextRows(IReadOnlyList<ProjectedColumn> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        string[] headers = columns.Select(column => column.Name).ToArray();
        string[][] values = rows.Select(row => columns.Select(column => ProjectedCsvValue(row[column.Name])).ToArray()).ToArray();
        int[] widths = headers.Select((header, index) => Math.Max(header.Length, values.Length == 0 ? 0 : values.Max(row => row[index].Length))).ToArray();
        Console.Out.WriteLine(string.Join("  ", headers.Select((header, index) => header.PadRight(widths[index]))));
        foreach (string[] row in values) Console.Out.WriteLine(string.Join("  ", row.Select((value, index) => value.PadRight(widths[index]))));
    }

    private static string ProjectedCsvValue(object? value)
        => value switch
        {
            null => string.Empty,
            string text => text,
            int number => number.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        };

    private static int WriteProjectedCompilationFailure(IReadOnlyList<Diagnostic> diagnostics, bool json)
    {
        if (json) WriteJson(new { schemaVersion = SchemaVersion, success = false, diagnostics = diagnostics.Select(diagnostic => new { code = diagnostic.Id, severity = "error", message = diagnostic.Message }) });
        else foreach (Diagnostic diagnostic in diagnostics) Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
        return FailureExitCode;
    }

    private static int RunList(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 1, allowed: ["--format"]);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        if (parsed.Format == "json")
        {
            WriteJson(new
            {
                schemaVersion = SchemaVersion,
                success = true,
                command = "table.list",
                tables = document.Tables.Select(table => new
                {
                    name = table.Name,
                    exported = table.Bound.IsExported,
                    rowCount = table.Bound.RowCount,
                    kind = table.Bound.Kind == BoundTableDefinitionKind.Derived ? "derived" : "authored",
                    key = table.Bound.TableType.KeyColumn?.Name,
                    source = Location(document.SourcePath, document.SourceText, table.Syntax.Identifier.Position),
                }),
            });
            return SuccessExitCode;
        }

        foreach (TableModel table in document.Tables)
        {
            string key = table.Bound.TableType.KeyColumn is null ? string.Empty : $"\tkey {table.Bound.TableType.KeyColumn.Name}";
            string kind = table.Bound.Kind == BoundTableDefinitionKind.Derived ? "derived" : "authored";
            Console.Out.WriteLine($"{table.Name}\t{table.Bound.RowCount} rows\t{kind}{key}");
        }

        return SuccessExitCode;
    }

    private static int RunSchema(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--format"]);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], parsed.Format == "json");
        if (table.Bound is BoundDerivedTableDefinition derived)
        {
            WriteDerivedSchema(document, table, derived, parsed.Format == "json");
            return SuccessExitCode;
        }
        if (parsed.Format == "json")
        {
            WriteJson(new
            {
                schemaVersion = SchemaVersion,
                success = true,
                command = "table.schema",
                table = table.Name,
                nominalIdentity = table.Bound.TableType.StableIdentity,
                exported = table.Bound.IsExported,
                rowCount = table.Bound.RowCount,
                key = table.Bound.TableType.KeyColumn?.Name,
                columns = table.Columns.Select(column => new
                {
                    name = column.Name,
                    type = column.Bound.Column.Type.Name,
                    reference = column.Bound.Column.Reference is null
                        ? null
                        : new
                        {
                            table = column.Bound.Column.Reference.TargetTable.Name,
                            key = column.Bound.Column.Reference.TargetKey.Name,
                        },
                    enumCases = EnumCases(column.Bound.Column.Type),
                    source = Location(document.SourcePath, document.SourceText, column.Syntax.Identifier.Position),
                }),
            });
            return SuccessExitCode;
        }

        Console.Out.WriteLine($"{table.Name} ({table.Bound.RowCount} rows){(table.Bound.IsExported ? " exported" : string.Empty)}");
        if (table.Bound.TableType.KeyColumn is not null)
        {
            Console.Out.WriteLine($"key: {table.Bound.TableType.KeyColumn.Name}");
        }
        foreach (TableColumnModel column in table.Columns)
        {
            string reference = column.Bound.Column.Reference is null
                ? string.Empty
                : $" -> {column.Bound.Column.Reference.TargetTable.Name}.{column.Bound.Column.Reference.TargetKey.Name}";
            Console.Out.WriteLine($"{column.Name}: {column.Bound.Column.Type.Name}{reference}");
        }

        return SuccessExitCode;
    }

    private static int RunRows(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--format", "--offset", "--limit", "--columns"]);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], parsed.Format == "json");
        EnsureAuthored(table, "table.rows", parsed.Format == "json");
        int offset = ParseNonNegative(parsed.Value("--offset") ?? "0", "--offset", parsed.Format == "json");
        int limit = ParseNonNegative(parsed.Value("--limit") ?? table.Bound.RowCount.ToString(CultureInfo.InvariantCulture), "--limit", parsed.Format == "json");
        IReadOnlyList<TableColumnModel> columns = SelectColumns(table, parsed.Value("--columns"), parsed.Format == "json");
        IReadOnlyList<RowValue> rows = ProjectRows(table, columns, offset, limit);

        if (parsed.Format == "json")
        {
            WriteJson(new
            {
                schemaVersion = SchemaVersion,
                success = true,
                command = "table.rows",
                table = table.Name,
                offset,
                rows = rows.Select(row => new { row = row.Index, values = row.Values }),
            });
            return SuccessExitCode;
        }

        WriteTextRows(columns, rows);
        return SuccessExitCode;
    }

    private static int RunSet(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--row", "--column", "--value", "--dry-run", "--format"]);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], parsed.Format == "json");
        EnsureAuthored(table, "table.set", parsed.Format == "json");
        int row = ParseRequiredNonNegative(parsed, "--row");
        string columnName = parsed.Required("--column");
        TableColumnModel column = table.RequireColumn(columnName, parsed.Format == "json");
        EnsureRow(table, row, parsed.Format == "json");
        string replacement = ParseCommandValue(parsed.Required("--value"), column.Bound.Column.Type, parsed.Format == "json");
        string oldSource = Slice(document.SourceText, SpanOf(column.Syntax.Cells.Elements[row]));
        TextEdit edit = new(SpanOf(column.Syntax.Cells.Elements[row]), replacement);
        string candidate = ApplyEdits(document.SourceText, [edit]);
        ValidateCandidate(document.SourcePath, candidate, parsed.Format == "json");
        object? oldValue = SerializeConstant(column.Bound.Cells[row]);
        object? newValue = SerializeConstant(LoadDocument(document.SourcePath, candidate).RequireTable(table.Name, parsed.Format == "json").RequireColumn(column.Name, parsed.Format == "json").Bound.Cells[row]);
        var result = new
        {
            schemaVersion = SchemaVersion,
            success = true,
            command = "table.set",
            changed = new { table = table.Name, row, column = column.Name, oldValue, newValue, source = Location(document.SourcePath, document.SourceText, column.Syntax.Identifier.Position) },
            dryRun = parsed.Has("--dry-run"),
        };
        PublishMutation(document, candidate, parsed, "Changed:\n" + table.Name + "[" + row + "]." + column.Name + "\n" + oldSource + " → " + replacement, result);
        return SuccessExitCode;
    }

    private static int RunAddRow(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--json", "--dry-run", "--format"]);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], parsed.Format == "json");
        EnsureAuthored(table, "table.add-row", parsed.Format == "json");
        Dictionary<string, string> values = ParseRowJson(parsed.Required("--json"), table, parsed.Format == "json");
        var edits = new List<TextEdit>();
        foreach (TableColumnModel column in table.Columns)
        {
            edits.Add(AppendEdit(document.SourceText, column.Syntax.Cells, values[column.Name]));
        }

        string candidate = ApplyEdits(document.SourceText, edits);
        ValidateCandidate(document.SourcePath, candidate, parsed.Format == "json");
        int row = table.Bound.RowCount;
        TableModel updatedTable = LoadDocument(document.SourcePath, candidate).RequireTable(table.Name, parsed.Format == "json");
        Dictionary<string, object?> addedValues = updatedTable.Columns.ToDictionary(
            column => column.Name,
            column => SerializeConstant(column.Bound.Cells[row]),
            StringComparer.Ordinal);
        var result = new
        {
            schemaVersion = SchemaVersion,
            success = true,
            command = "table.add-row",
            added = new { table = table.Name, row, values = addedValues },
            dryRun = parsed.Has("--dry-run"),
        };
        PublishMutation(
            document,
            candidate,
            parsed,
            "Added:\n" + table.Name + " row " + row + "\n" + FormatRow(addedValues),
            result);
        return SuccessExitCode;
    }

    private static int RunDeleteRow(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--row", "--dry-run", "--format"]);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], parsed.Format == "json");
        EnsureAuthored(table, "table.delete-row", parsed.Format == "json");
        int row = ParseRequiredNonNegative(parsed, "--row");
        EnsureRow(table, row, parsed.Format == "json");
        Dictionary<string, object?> deleted = table.Columns.ToDictionary(column => column.Name, column => SerializeConstant(column.Bound.Cells[row]), StringComparer.Ordinal);
        string candidate = ApplyEdits(document.SourceText, table.Columns.Select(column => DeleteEdit(column.Syntax.Cells, row)));
        ValidateCandidate(document.SourcePath, candidate, parsed.Format == "json");
        var result = new
        {
            schemaVersion = SchemaVersion,
            success = true,
            command = "table.delete-row",
            deleted = new { table = table.Name, row, values = deleted },
            dryRun = parsed.Has("--dry-run"),
        };
        PublishMutation(document, candidate, parsed, "Deleted:\n" + table.Name + " row " + row + "\n" + FormatRow(deleted), result);
        return SuccessExitCode;
    }

    private static int RunValidate(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 1, allowed: ["--format"]);
        string sourcePath = Path.GetFullPath(parsed.Positionals[0]);
        string sourceText = File.ReadAllText(sourcePath);
        CopelandCompilation compilation = Compile(sourcePath, sourceText);
        if (!compilation.Success)
        {
            WriteCompilationFailure("table.validate", compilation.Diagnostics, sourcePath, sourceText, parsed.Format == "json");
            return FailureExitCode;
        }

        IReadOnlyList<BoundTableDefinition> tables = compilation.BoundCompilation!.Program.Tables;
        if (parsed.Format == "json")
        {
            WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.validate", tableCount = tables.Count, totalRows = tables.Sum(table => table.RowCount), diagnostics = Array.Empty<object>() });
        }
        else
        {
            Console.Out.WriteLine($"{tables.Count} tables valid");
            Console.Out.WriteLine($"{tables.Sum(table => table.RowCount)} total rows");
            Console.Out.WriteLine("0 errors");
        }

        return SuccessExitCode;
    }

    private static int RunExport(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--format", "--output", "--result-format"]);
        if (parsed.Value("--format") is not "csv")
        {
            throw Error("COPE-TABLE-TOOL-0015", "Table export currently supports '--format csv'.", ResultIsJson(parsed));
        }

        bool resultJson = ResultIsJson(parsed);
        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], resultJson);
        EnsureAuthored(table, "table.export", resultJson);
        string csv = Csv.Write(table.Columns.Select(column => column.Name).ToArray(), ProjectRows(table, table.Columns, 0, table.Bound.RowCount)
            .Select(row => table.Columns.Select(column => CsvValue(column.Bound.Cells[row.Index])).ToArray()).ToArray());
        string? outputPath = parsed.Value("--output");
        if (resultJson && outputPath is null)
        {
            throw Error("COPE-TABLE-TOOL-0017", "CSV export with '--result-format json' requires '--output' so CSV and JSON do not share stdout.", json: true);
        }

        if (outputPath is null)
        {
            Console.Out.Write(csv);
        }
        else
        {
            AtomicWrite(Path.GetFullPath(outputPath), csv, expectedHash: null);
        }

        if (resultJson)
        {
            WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.export", table = table.Name, format = "csv", output = outputPath, rowCount = table.Bound.RowCount });
        }

        return SuccessExitCode;
    }

    private static int RunImport(string[] args)
    {
        ParsedArguments parsed = ParseOptions(args, 2, positionalCount: 2, allowed: ["--format", "--input", "--replace", "--dry-run", "--result-format"]);
        bool resultJson = ResultIsJson(parsed);
        if (parsed.Value("--format") is not "csv")
        {
            throw Error("COPE-TABLE-TOOL-0015", "Table import currently supports '--format csv'.", resultJson);
        }

        if (!parsed.Has("--replace"))
        {
            throw Error("COPE-TABLE-TOOL-0016", "CSV import requires explicit '--replace' because it replaces every authored row.", resultJson);
        }

        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], resultJson);
        EnsureAuthored(table, "table.import", resultJson);
        CsvDocument csv = Csv.Read(File.ReadAllText(Path.GetFullPath(parsed.Required("--input"))));
        ValidateHeaders(csv.Headers, table, resultJson);
        var cells = table.Columns.ToDictionary(column => column.Name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (CsvRecord record in csv.Rows)
        {
            for (int index = 0; index < table.Columns.Count; index += 1)
            {
                TableColumnModel column = table.Columns[index];
                cells[column.Name].Add(ParseCsvValue(record.Cells[index], column, document.SourceText, record.Line, index + 1, resultJson));
            }
        }

        string candidate = ApplyEdits(document.SourceText, table.Columns.Select(column => ReplaceArrayEdit(document.SourceText, column.Syntax.Cells, cells[column.Name])));
        ValidateCandidate(document.SourcePath, candidate, resultJson);
        var result = new { schemaVersion = SchemaVersion, success = true, command = "table.import", imported = new { table = table.Name, format = "csv", rowCount = csv.Rows.Count, replace = true }, dryRun = parsed.Has("--dry-run") };
        PublishMutation(document, candidate, parsed with { Format = resultJson ? "json" : "text" }, "Imported:\n" + table.Name + "\n" + csv.Rows.Count + " rows replaced", result);
        return SuccessExitCode;
    }

    private static int RunQuery(string[] args)
    {
        ParsedArguments parsed = ParseOptions(
            args,
            2,
            positionalCount: 2,
            allowed: ["--where", "--select", "--group-by", "--aggregate", "--order-by", "--skip", "--take", "--query-json", "--explain", "--dry-run", "--format", "--executor"]);
        bool json = parsed.Format == "json";
        string executor = parsed.Value("--executor") ?? "sourcegen";
        if (executor is not "sourcegen" and not "legacy" and not "compare")
        {
            throw Error("COPE-TABLE-QUERY-0028", "'--executor' must be 'sourcegen', 'legacy', or 'compare'.", json);
        }
        bool explain = parsed.Has("--explain") || parsed.Has("--dry-run");
        if (explain && parsed.Format == "csv")
        {
            throw Error("COPE-TABLE-QUERY-0015", "'--explain' supports text or JSON output, not CSV.", json);
        }

        TableDocument document = LoadDocument(parsed.Positionals[0]);
        TableModel table = document.RequireTable(parsed.Positionals[1], json);
        TableQueryRequest request = ParseQueryRequest(parsed, table, json);
        CopelandCompilation compilerCompilation = Compile(document.SourcePath, document.SourceText);
        BoundTableQueryPlan compilerPlan;
        MirTableQueryArtifact queryArtifact;
        try
        {
            compilerPlan = TableQueryBinder.Bind(
                compilerCompilation.BoundCompilation!,
                compilerCompilation.MirCompilation!.Program!,
                new Copeland.TS.Compiler.TableQueryRequest(
                    table.Name,
                    request.Where,
                    request.Select.Select(item => new TableQueryProjectionRequest(item.Column, item.Alias)).ToArray(),
                    request.GroupBy,
                    request.Aggregates.Select(item => new TableQueryAggregateRequest(item.Function, item.Input, item.Alias)).ToArray(),
                    request.OrderBy.Select(item => new TableQueryOrderingRequest(item.Column, item.Direction)).ToArray(),
                    request.Skip,
                    request.Take,
                    document.SourcePath + "#table-query"));
            queryArtifact = TableQueryBinder.Lower(compilerPlan);
        }
        catch (TableQueryBindingException exception)
        {
            throw Error(exception.Code, exception.Message, json, document.SourcePath, document.SourceText, exception.Position);
        }

        TableQueryPlan plan = CreateRenderingPlan(table, compilerPlan);

        if (explain)
        {
            WriteQueryExplain(document, plan, parsed.Format, queryArtifact);
            return SuccessExitCode;
        }

        IReadOnlyList<QueryMaterializedRow> sourceGeneratedRows = ExecuteSourceGeneratedQuery(compilerCompilation.MirCompilation!.Program!, queryArtifact, plan, json);
        IReadOnlyList<QueryMaterializedRow> rows = sourceGeneratedRows;
        if (executor is "legacy" or "compare")
        {
            TableQueryPlan legacyPlan = BindQueryPlan(table, request, json);
            IReadOnlyList<QueryMaterializedRow> legacyRows = ExecuteQuery(document, legacyPlan, json);
            if (executor == "compare")
            {
                AssertQueryParity(plan, sourceGeneratedRows, legacyPlan, legacyRows, json);
            }
            else
            {
                rows = legacyRows;
            }
        }
        WriteQueryResult(document, plan, rows, parsed.Format, queryArtifact);
        return SuccessExitCode;
    }

    private static void AssertQueryParity(
        TableQueryPlan sourceGeneratedPlan,
        IReadOnlyList<QueryMaterializedRow> sourceGeneratedRows,
        TableQueryPlan legacyPlan,
        IReadOnlyList<QueryMaterializedRow> legacyRows,
        bool json)
    {
        bool sameSchema = sourceGeneratedPlan.ResultColumns.Select(column => (column.Name, column.Type.Name))
            .SequenceEqual(legacyPlan.ResultColumns.Select(column => (column.Name, column.Type.Name)));
        bool sameRows = sourceGeneratedRows.Count == legacyRows.Count
            && sourceGeneratedRows.Zip(legacyRows).All(pair => sourceGeneratedPlan.ResultColumns.All(column =>
            {
                QueryResultColumn legacyColumn = legacyPlan.ResultColumns.Single(candidate => candidate.Name == column.Name);
                return QueryValuesEquivalent(pair.First.Values[column.ValueIndex], pair.Second.Values[legacyColumn.ValueIndex]);
            }));
        if (!sameSchema || !sameRows)
        {
            throw Error("COPE-TABLE-QUERY-0029", "Legacy and source-generated query execution produced different results.", json);
        }
    }

    private static bool QueryValuesEquivalent(object? left, object? right)
    {
        if (Equals(left, right)) return true;
        if (left is IConvertible leftNumber && right is IConvertible rightNumber
            && left is not string && right is not string && left is not bool && right is not bool)
        {
            return leftNumber.ToDouble(CultureInfo.InvariantCulture) == rightNumber.ToDouble(CultureInfo.InvariantCulture);
        }
        return false;
    }

    private static TableQueryPlan CreateRenderingPlan(TableModel table, BoundTableQueryPlan compilerPlan)
    {
        IReadOnlyList<TableColumnSymbol> columns = compilerPlan.SourceColumns.Select(column => column.Symbol).ToArray();
        QueryColumnProvenance Convert(TableQueryColumnProvenance provenance)
            => new(provenance.Kind, provenance.SourceTable, provenance.Inputs, provenance.Relationships, provenance.Aggregate, provenance.Filter);
        return new TableQueryPlan(
            table,
            columns,
            compilerPlan.PredicateCSharp,
            compilerPlan.Projection.Select(item => new QueryProjection(item.Name, columns[item.SourceIndex], item.SourceIndex, Convert(item.Provenance))).ToArray(),
            compilerPlan.GroupKeys.Select(item => new QueryGroupKey(columns[item.SourceIndex], item.SourceIndex, Convert(item.Provenance))).ToArray(),
            compilerPlan.Aggregates.Select(item => new QueryAggregate(item.Name, (QueryAggregateKind)item.Kind, item.InputIndex < 0 ? null : columns[item.InputIndex], item.InputIndex, item.Type, Convert(item.Provenance))).ToArray(),
            compilerPlan.ResultColumns.Select(item => new QueryResultColumn(item.Name, item.Type, item.ValueIndex, Convert(item.Provenance))).ToArray(),
            compilerPlan.Ordering.Select(item => new QueryOrderTerm(item.Name, item.Type, item.ValueIndex, item.Descending)).ToArray(),
            compilerPlan.Skip,
            compilerPlan.Take);
    }

    private static IReadOnlyList<QueryMaterializedRow> ExecuteSourceGeneratedQuery(MirProgram program, MirTableQueryArtifact artifact, TableQueryPlan plan, bool json)
    {
        try
        {
            Copeland.TS.Backend.CSharp.ITypedQueryResult result = CSharpTableQueryMaterializer.Execute(program.WithExecutableArtifact(artifact), artifact);
            var rows = new List<QueryMaterializedRow>(result.RowCount);
            for (int rowIndex = 0; rowIndex < result.RowCount; rowIndex += 1)
            {
                object?[] values = Enumerable.Range(0, plan.ResultColumns.Count)
                    .Select(columnIndex => NormalizeRuntimeValue(result.GetValue(rowIndex, columnIndex)))
                    .ToArray();
                rows.Add(new QueryMaterializedRow(rowIndex, values));
            }
            return rows;
        }
        catch (CSharpQueryMaterializationException exception)
        {
            string code = exception.Message.Contains("__q", StringComparison.Ordinal)
                ? "COPE-TABLE-QUERY-0003"
                : "COPE-TABLE-QUERY-0016";
            throw Error(code, code == "COPE-TABLE-QUERY-0003" ? "Invalid '--where' expression: " + exception.Message : "The C# source generator query executor failed: " + exception.Message, json);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException inner)
        {
            throw Error("COPE-TABLE-QUERY-0027", inner.Message, json);
        }
    }

    private static TableQueryRequest ParseQueryRequest(ParsedArguments parsed, TableModel table, bool json)
    {
        if (parsed.Value("--query-json") is string queryJsonPath)
        {
            if (parsed.Has("--where") || parsed.Has("--select") || parsed.Has("--group-by") || parsed.Has("--aggregate") || parsed.Has("--order-by") || parsed.Has("--skip") || parsed.Has("--take"))
            {
                throw Error("COPE-TABLE-QUERY-0001", "'--query-json' cannot be combined with textual query options.", json);
            }

            try
            {
                return ParseQueryJson(File.ReadAllText(Path.GetFullPath(queryJsonPath)), table, json);
            }
            catch (JsonException exception)
            {
                throw Error("COPE-TABLE-QUERY-0011", "Malformed query JSON: " + exception.Message, json);
            }
        }

        return new TableQueryRequest(
            parsed.Value("--where"),
            ParseSelectItems(parsed.Value("--select"), json),
            ParseGroupByItems(parsed.Value("--group-by"), json),
            ParseAggregateItems(parsed.Value("--aggregate"), json),
            ParseOrderTerms(parsed.Value("--order-by"), json),
            ParseNonNegative(parsed.Value("--skip") ?? "0", "--skip", json),
            ParseNonNegative(parsed.Value("--take") ?? table.Bound.RowCount.ToString(CultureInfo.InvariantCulture), "--take", json));
    }

    private static TableQueryRequest ParseQueryJson(string text, TableModel table, bool json)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw Error("COPE-TABLE-QUERY-0011", "Query JSON must contain one object.", json);
        }

        JsonElement root = document.RootElement;
        string? where = root.TryGetProperty("where", out JsonElement whereElement)
            ? QueryJsonExpression(whereElement, table, json)
            : null;
        IReadOnlyList<QuerySelectRequest> select = root.TryGetProperty("select", out JsonElement selectElement)
            ? ParseQueryJsonSelect(selectElement, json)
            : [];
        IReadOnlyList<string> groupBy = root.TryGetProperty("groupBy", out JsonElement groupByElement)
            ? ParseQueryJsonGroupBy(groupByElement, json)
            : [];
        IReadOnlyList<QueryAggregateRequest> aggregates = root.TryGetProperty("aggregates", out JsonElement aggregateElement)
            ? ParseQueryJsonAggregates(aggregateElement, table, json)
            : [];
        IReadOnlyList<QueryOrderRequest> orderBy = root.TryGetProperty("orderBy", out JsonElement orderElement)
            ? ParseQueryJsonOrder(orderElement, json)
            : [];
        int skip = root.TryGetProperty("skip", out JsonElement skipElement)
            ? QueryJsonNonNegative(skipElement, "skip", json)
            : 0;
        int take = root.TryGetProperty("take", out JsonElement takeElement)
            ? QueryJsonNonNegative(takeElement, "take", json)
            : table.Bound.RowCount;
        return new TableQueryRequest(where, select, groupBy, aggregates, orderBy, skip, take);
    }

    private static string QueryJsonExpression(JsonElement element, TableModel table, bool json)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Error("COPE-TABLE-QUERY-0012", "Query JSON expression nodes must be objects.", json);
        }

        if (element.TryGetProperty("column", out JsonElement column))
        {
            if (column.ValueKind != JsonValueKind.String)
            {
                throw Error("COPE-TABLE-QUERY-0012", "Query JSON 'column' must be a string.", json);
            }

            return column.GetString()!;
        }

        if (element.TryGetProperty("number", out JsonElement number) && number.ValueKind == JsonValueKind.Number)
        {
            return number.GetRawText();
        }

        if (element.TryGetProperty("string", out JsonElement text) && text.ValueKind == JsonValueKind.String)
        {
            return JsonSerializer.Serialize(text.GetString());
        }

        if (element.TryGetProperty("boolean", out JsonElement boolean) && boolean.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return boolean.GetBoolean() ? "true" : "false";
        }

        if (element.TryGetProperty("enum", out JsonElement @enum) && @enum.ValueKind == JsonValueKind.String)
        {
            return @enum.GetString()!;
        }

        if (!element.TryGetProperty("operator", out JsonElement operatorElement)
            || operatorElement.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("left", out JsonElement left)
            || !element.TryGetProperty("right", out JsonElement right))
        {
            throw Error("COPE-TABLE-QUERY-0012", "Query JSON expression must be a column, literal, or binary operator node.", json);
        }

        string operation = operatorElement.GetString()!;
        string token = operation switch
        {
            "equal" => "==",
            "notEqual" => "!=",
            "greaterThan" => ">",
            "greaterThanOrEqual" => ">=",
            "lessThan" => "<",
            "lessThanOrEqual" => "<=",
            "and" => "&&",
            "or" => "||",
            "add" => "+",
            "subtract" => "-",
            "multiply" => "*",
            "divide" => "/",
            _ => throw Error("COPE-TABLE-QUERY-0013", $"Query JSON operator '{operation}' is not supported by M2A.", json),
        };
        return "(" + QueryJsonExpression(left, table, json) + " " + token + " " + QueryJsonExpression(right, table, json) + ")";
    }

    private static IReadOnlyList<QuerySelectRequest> ParseQueryJsonSelect(JsonElement element, bool json)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Error("COPE-TABLE-QUERY-0012", "Query JSON 'select' must be an array.", json);
        }

        var select = new List<QuerySelectRequest>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("column", out JsonElement column) || column.ValueKind != JsonValueKind.String)
            {
                throw Error("COPE-TABLE-QUERY-0012", "Each query JSON select item requires a string 'column'.", json);
            }

            string? alias = item.TryGetProperty("as", out JsonElement aliasElement) && aliasElement.ValueKind == JsonValueKind.String
                ? aliasElement.GetString()
                : null;
            select.Add(new QuerySelectRequest(column.GetString()!, alias));
        }

        return select;
    }

    private static IReadOnlyList<QueryOrderRequest> ParseQueryJsonOrder(JsonElement element, bool json)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Error("COPE-TABLE-QUERY-0012", "Query JSON 'orderBy' must be an array.", json);
        }

        var order = new List<QueryOrderRequest>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("column", out JsonElement column) || column.ValueKind != JsonValueKind.String)
            {
                throw Error("COPE-TABLE-QUERY-0012", "Each query JSON ordering item requires a string 'column'.", json);
            }

            string direction = item.TryGetProperty("direction", out JsonElement directionElement) && directionElement.ValueKind == JsonValueKind.String
                ? directionElement.GetString()!
                : "ascending";
            order.Add(new QueryOrderRequest(column.GetString()!, direction));
        }

        return order;
    }

    private static IReadOnlyList<string> ParseQueryJsonGroupBy(JsonElement element, bool json)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Error("COPE-TABLE-QUERY-0012", "Query JSON 'groupBy' must be an array.", json);
        }

        var groupBy = new List<string>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("column", out JsonElement column) || column.ValueKind != JsonValueKind.String)
            {
                throw Error("COPE-TABLE-QUERY-0012", "Each query JSON group key requires a string 'column'.", json);
            }

            groupBy.Add(column.GetString()!);
        }

        return groupBy;
    }

    private static IReadOnlyList<QueryAggregateRequest> ParseQueryJsonAggregates(JsonElement element, TableModel table, bool json)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Error("COPE-TABLE-QUERY-0012", "Query JSON 'aggregates' must be an array.", json);
        }

        var aggregates = new List<QueryAggregateRequest>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("function", out JsonElement function)
                || function.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("as", out JsonElement alias)
                || alias.ValueKind != JsonValueKind.String)
            {
                throw Error("COPE-TABLE-QUERY-0012", "Each query JSON aggregate requires string 'function' and 'as' properties.", json);
            }

            string? input = null;
            if (item.TryGetProperty("input", out JsonElement inputElement))
            {
                if (inputElement.ValueKind != JsonValueKind.Object
                    || !inputElement.TryGetProperty("column", out JsonElement column)
                    || column.ValueKind != JsonValueKind.String)
                {
                    throw Error("COPE-TABLE-QUERY-0012", "Query JSON aggregate 'input' must be a column object.", json);
                }

                input = column.GetString();
            }

            aggregates.Add(new QueryAggregateRequest(function.GetString()!, input, alias.GetString()!));
        }

        return aggregates;
    }

    private static int QueryJsonNonNegative(JsonElement element, string name, bool json)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int value) || value < 0)
        {
            throw Error("COPE-TABLE-QUERY-0014", $"Query JSON '{name}' must be a non-negative integer.", json);
        }

        return value;
    }

    private static IReadOnlyList<QuerySelectRequest> ParseSelectItems(string? text, bool json)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<QuerySelectRequest>();
        foreach (string item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = item.Split(" as ", StringSplitOptions.TrimEntries);
            if (parts.Length is < 1 or > 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                throw Error("COPE-TABLE-QUERY-0006", $"Invalid '--select' item '{item}'. Use 'column' or 'column as name'.", json);
            }

            result.Add(new QuerySelectRequest(parts[0], parts.Length == 2 ? parts[1] : null));
        }

        return result;
    }

    private static IReadOnlyList<string> ParseGroupByItems(string? text, bool json)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        string[] columns = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length == 0)
        {
            throw Error("COPE-TABLE-QUERY-0017", "'--group-by' requires one or more direct column names.", json);
        }

        return columns;
    }

    private static IReadOnlyList<QueryAggregateRequest> ParseAggregateItems(string? text, bool json)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var aggregates = new List<QueryAggregateRequest>();
        foreach (string item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int aliasIndex = item.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (aliasIndex <= 0 || aliasIndex + 4 >= item.Length)
            {
                throw Error("COPE-TABLE-QUERY-0018", $"Invalid '--aggregate' item '{item}'. Use 'function(column) as name' or 'count() as name'.", json);
            }

            string call = item[..aliasIndex].Trim();
            string alias = item[(aliasIndex + 4)..].Trim();
            int open = call.IndexOf('(');
            if (open <= 0 || !call.EndsWith(')') || call.IndexOf('(', open + 1) >= 0)
            {
                throw Error("COPE-TABLE-QUERY-0018", $"Invalid aggregate call '{call}'.", json);
            }

            string function = call[..open].Trim();
            string input = call[(open + 1)..^1].Trim();
            if (string.IsNullOrWhiteSpace(alias) || !IsIdentifierStart(alias[0]) || alias.Any(character => !IsIdentifierPart(character)))
            {
                throw Error("COPE-TABLE-QUERY-0018", $"Aggregate alias '{alias}' must be an identifier.", json);
            }

            aggregates.Add(new QueryAggregateRequest(function, string.IsNullOrWhiteSpace(input) ? null : input, alias));
        }

        return aggregates;
    }

    private static IReadOnlyList<QueryOrderRequest> ParseOrderTerms(string? text, bool json)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<QueryOrderRequest>();
        foreach (string item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = item.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2)
            {
                throw Error("COPE-TABLE-QUERY-0008", $"Invalid '--order-by' term '{item}'.", json);
            }

            result.Add(new QueryOrderRequest(parts[0], parts.Length == 2 ? parts[1] : "asc"));
        }

        return result;
    }

    private static TableQueryPlan BindQueryPlan(TableModel table, TableQueryRequest request, bool json)
    {
        IReadOnlyList<TableColumnSymbol> sourceColumns = QueryColumns(table.Bound);
        var columnsByName = sourceColumns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        string? predicate = request.Where is null ? null : NormalizeQueryExpression(request.Where, sourceColumns);
        if (request.GroupBy.Count > 0 && request.Aggregates.Count == 0)
        {
            throw Error("COPE-TABLE-QUERY-0019", "'--group-by' requires at least one '--aggregate' declaration.", json);
        }
        if (request.Aggregates.Count > 0 && request.Select.Count > 0)
        {
            throw Error("COPE-TABLE-QUERY-0020", "'--select' cannot be combined with '--aggregate'; aggregates define the result schema.", json);
        }

        var selected = new List<QueryProjection>();
        IEnumerable<QuerySelectRequest> requestedSelect = request.Select.Count == 0
            ? sourceColumns.Select(column => new QuerySelectRequest(column.Name, null))
            : request.Select;
        foreach (QuerySelectRequest selection in requestedSelect)
        {
            if (!columnsByName.TryGetValue(selection.Column, out TableColumnSymbol? column))
            {
                throw Error("COPE-TABLE-QUERY-0005", UnknownColumnMessage(selection.Column, sourceColumns, table.Name), json);
            }

            string outputName = selection.Alias ?? column.Name;
            if (selected.Any(projection => projection.Name == outputName))
            {
                throw Error("COPE-TABLE-QUERY-0007", $"Query selection produces duplicate column '{outputName}'.", json);
            }

            selected.Add(new QueryProjection(outputName, column, Array.IndexOf(sourceColumns.ToArray(), column), QueryProvenance(table.Bound, column)));
        }

        var groupKeys = new List<QueryGroupKey>();
        foreach (string groupColumn in request.GroupBy)
        {
            if (!columnsByName.TryGetValue(groupColumn, out TableColumnSymbol? column))
            {
                throw Error("COPE-TABLE-QUERY-0005", UnknownColumnMessage(groupColumn, sourceColumns, table.Name), json);
            }
            if (!IsGroupable(column.Type))
            {
                throw Error("COPE-TABLE-QUERY-0021", $"Column '{column.Name}' of type '{column.Type.Name}' cannot be used as a group key.", json);
            }
            if (groupKeys.Any(key => key.Column == column))
            {
                throw Error("COPE-TABLE-QUERY-0022", $"Query grouping contains duplicate column '{column.Name}'.", json);
            }

            groupKeys.Add(new QueryGroupKey(column, Array.IndexOf(sourceColumns.ToArray(), column), QueryProvenance(table.Bound, column)));
        }

        var aggregates = new List<QueryAggregate>();
        foreach (QueryAggregateRequest aggregate in request.Aggregates)
        {
            QueryAggregateKind kind = aggregate.Function.ToLowerInvariant() switch
            {
                "count" => QueryAggregateKind.Count,
                "sum" => QueryAggregateKind.Sum,
                "average" => QueryAggregateKind.Average,
                "min" => QueryAggregateKind.Min,
                "max" => QueryAggregateKind.Max,
                _ => throw Error("COPE-TABLE-QUERY-0023", $"Aggregate '{aggregate.Function}' is not supported. Use count, sum, average, min, or max.", json),
            };
            TableColumnSymbol? input = null;
            int inputIndex = -1;
            if (aggregate.Input is not null)
            {
                if (!columnsByName.TryGetValue(aggregate.Input, out input))
                {
                    throw Error("COPE-TABLE-QUERY-0005", UnknownColumnMessage(aggregate.Input, sourceColumns, table.Name), json);
                }
                inputIndex = Array.IndexOf(sourceColumns.ToArray(), input);
            }
            if (kind == QueryAggregateKind.Count)
            {
                // count() and count(column) are both meaningful for the non-nullable table columns in M2B.
            }
            else if (input is null)
            {
                throw Error("COPE-TABLE-QUERY-0024", $"Aggregate '{aggregate.Function}' requires a direct column input.", json);
            }
            else if ((kind is QueryAggregateKind.Sum or QueryAggregateKind.Average) && !TypeFacts.IsNumeric(input.Type))
            {
                throw Error("COPE-TABLE-QUERY-0025", $"Aggregate '{aggregate.Function}' requires a numeric column, got '{input.Type.Name}'.", json);
            }
            else if (kind == QueryAggregateKind.Average && input.Type == PrimitiveTypeSymbol.Int)
            {
                throw Error("COPE-TABLE-QUERY-0025", "Aggregate 'average' is supported only for number columns; convert int values before aggregation.", json);
            }
            else if ((kind is QueryAggregateKind.Min or QueryAggregateKind.Max) && !IsOrderable(input.Type))
            {
                throw Error("COPE-TABLE-QUERY-0025", $"Aggregate '{aggregate.Function}' requires an orderable column, got '{input.Type.Name}'.", json);
            }
            if (aggregates.Any(item => item.Name == aggregate.Alias) || groupKeys.Any(key => key.Column.Name == aggregate.Alias))
            {
                throw Error("COPE-TABLE-QUERY-0026", $"Aggregate result name '{aggregate.Alias}' is duplicated.", json);
            }

            TypeSymbol resultType = kind == QueryAggregateKind.Count ? PrimitiveTypeSymbol.Int : input!.Type;
            QueryColumnProvenance provenance = kind == QueryAggregateKind.Count
                ? new QueryColumnProvenance("aggregate", table.Name, [], [], aggregate.Function, predicate)
                : new QueryColumnProvenance("aggregate", table.Name, [input!.Name], QueryProvenance(table.Bound, input).Relationships, aggregate.Function, predicate);
            aggregates.Add(new QueryAggregate(aggregate.Alias, kind, input, inputIndex, resultType, provenance));
        }

        IReadOnlyList<QueryResultColumn> resultColumns = aggregates.Count == 0
            ? selected.Select(projection => new QueryResultColumn(projection.Name, projection.Column.Type, projection.SourceIndex, projection.Provenance)).ToArray()
            : groupKeys.Select((key, index) => new QueryResultColumn(key.Column.Name, key.Column.Type, index, key.Provenance))
                .Concat(aggregates.Select((aggregate, index) => new QueryResultColumn(aggregate.Name, aggregate.Type, groupKeys.Count + index, aggregate.Provenance)))
                .ToArray();

        var order = new List<QueryOrderTerm>();
        foreach (QueryOrderRequest requestTerm in request.OrderBy)
        {
            QueryResultColumn? resultColumn = aggregates.Count == 0
                ? columnsByName.TryGetValue(requestTerm.Column, out TableColumnSymbol? sourceColumn)
                    ? new QueryResultColumn(sourceColumn.Name, sourceColumn.Type, Array.IndexOf(sourceColumns.ToArray(), sourceColumn), QueryProvenance(table.Bound, sourceColumn))
                    : null
                : resultColumns.SingleOrDefault(column => column.Name == requestTerm.Column);
            if (resultColumn is null)
            {
                IReadOnlyList<string> names = resultColumns.Select(column => column.Name).ToArray();
                string message = aggregates.Count > 0
                    ? $"Column '{requestTerm.Column}' is not present in the aggregate result."
                    : UnknownColumnMessage(requestTerm.Column, sourceColumns, table.Name);
                throw Error("COPE-TABLE-QUERY-0005", message, json);
            }

            bool descending = requestTerm.Direction switch
            {
                "asc" or "ascending" => false,
                "desc" or "descending" => true,
                _ => throw Error("COPE-TABLE-QUERY-0009", $"Order direction '{requestTerm.Direction}' must be 'asc' or 'desc'.", json),
            };
            if (!IsOrderable(resultColumn.Type))
            {
                throw Error("COPE-TABLE-QUERY-0010", $"Column '{resultColumn.Name}' of type '{resultColumn.Type.Name}' is not orderable.", json);
            }

            order.Add(new QueryOrderTerm(resultColumn.Name, resultColumn.Type, resultColumn.ValueIndex, descending));
        }

        return new TableQueryPlan(table, sourceColumns, predicate, selected, groupKeys, aggregates, resultColumns, order, request.Skip, request.Take);
    }

    private static IReadOnlyList<TableColumnSymbol> QueryColumns(BoundTableDefinition table)
        => table is BoundDerivedTableDefinition derived
            ? derived.Projections.Select(projection => projection.Column).ToArray()
            : table.Columns.Select(column => column.Column).ToArray();

    private static string UnknownColumnMessage(string name, IReadOnlyList<TableColumnSymbol> columns, string table)
    {
        string? suggestion = columns
            .OrderBy(column => LevenshteinDistance(name, column.Name))
            .FirstOrDefault(column => LevenshteinDistance(name, column.Name) <= 3)
            ?.Name;
        return suggestion is null
            ? $"Column '{name}' was not found in table '{table}'."
            : $"Column '{name}' was not found in table '{table}'. Did you mean '{suggestion}'?";
    }

    private static int LevenshteinDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex += 1)
        {
            int[] current = new int[right.Length + 1];
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex += 1)
            {
                current[rightIndex] = Math.Min(Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1), previous[rightIndex - 1] + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[right.Length];
    }

    private static bool IsOrderable(TypeSymbol type)
        => type == PrimitiveTypeSymbol.Int
            || type == PrimitiveTypeSymbol.Float
            || type == PrimitiveTypeSymbol.Number
            || type == PrimitiveTypeSymbol.String;

    private static bool IsGroupable(TypeSymbol type)
        => IsOrderable(type) || type is EnumTypeSymbol || type == PrimitiveTypeSymbol.Boolean;

    private static QueryColumnProvenance QueryProvenance(BoundTableDefinition table, TableColumnSymbol column)
    {
        if (table is not BoundDerivedTableDefinition derived)
        {
            return new QueryColumnProvenance("authored", table.TableType.Name, [column.Name], []);
        }

        BoundDerivedTableColumnDefinition projection = derived.Projections.Single(item => item.Column == column);
        return new QueryColumnProvenance(
            projection.CopiedSourceColumn is null ? "computed" : "copied",
            derived.SourceTable.Name,
            projection.SourceColumns,
            projection.Relationships.Select(join => RelationshipText(join.Relationship)).ToArray());
    }

    private static string NormalizeQueryExpression(string expression, IReadOnlyList<TableColumnSymbol> columns)
    {
        var output = new StringBuilder(expression.Length + 16);
        var columnNames = columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        var enumCases = columns
            .Where(column => column.Type is EnumTypeSymbol)
            .SelectMany(column => ((EnumTypeSymbol)column.Type).Cases
                .Where(@case => !@case.HasPayload)
                .Select(@case => new { @case.Name, EnumName = @case.EnumType.Name }))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.EnumName).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        bool inString = false;
        bool escaped = false;
        for (int index = 0; index < expression.Length;)
        {
            char current = expression[index];
            if (inString)
            {
                output.Append(current);
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                index += 1;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                output.Append(current);
                index += 1;
                continue;
            }

            if (!IsIdentifierStart(current))
            {
                output.Append(current);
                index += 1;
                continue;
            }

            int start = index;
            index += 1;
            while (index < expression.Length && IsIdentifierPart(expression[index])) index += 1;
            string identifier = expression[start..index];
            bool memberName = PreviousNonWhitespace(expression, start - 1) == '.';
            if (!memberName && columnNames.Contains(identifier))
            {
                output.Append("row.").Append(identifier);
            }
            else if (!memberName && enumCases.TryGetValue(identifier, out string[]? enumNames) && enumNames.Length == 1)
            {
                output.Append(enumNames[0]).Append('.').Append(identifier);
            }
            else
            {
                output.Append(identifier);
            }
        }

        return output.ToString();
    }

    private static bool IsIdentifierStart(char value) => char.IsAsciiLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';

    private static char PreviousNonWhitespace(string text, int index)
    {
        while (index >= 0 && char.IsWhiteSpace(text[index])) index -= 1;
        return index < 0 ? '\0' : text[index];
    }

    private static IReadOnlyList<QueryMaterializedRow> ExecuteQuery(TableDocument document, TableQueryPlan plan, bool json)
    {
        string functionName = QueryFunctionName(document);
        string querySource = BuildQuerySource(document.SourceText, plan.Table.Name, plan.Predicate, functionName);
        CopelandCompilation compilation = Compile(document.SourcePath, querySource);
        if (!compilation.Success)
        {
            Diagnostic diagnostic = compilation.Diagnostics.First();
            throw Error("COPE-TABLE-QUERY-0003", "Invalid '--where' expression: " + diagnostic.Message, json, document.SourcePath, querySource, diagnostic.Position);
        }

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        if (emitted.Diagnostics.Count > 0)
        {
            throw Error("COPE-TABLE-QUERY-0016", "The C# query executor is unavailable: " + emitted.Diagnostics[0].Message, json);
        }

        Array rows = InvokeQueryAssembly(emitted.SourceText, functionName, json);
        var materialized = new List<QueryMaterializedRow>(rows.Length);
        for (int index = 0; index < rows.Length; index += 1)
        {
            object row = rows.GetValue(index)!;
            var values = new List<object?>(plan.SourceColumns.Count);
            foreach (TableColumnSymbol column in plan.SourceColumns)
            {
                System.Reflection.PropertyInfo? property = row.GetType().GetProperty(column.Name);
                if (property is null)
                {
                    throw Error("COPE-TABLE-QUERY-0016", $"The C# query executor did not expose column '{column.Name}'.", json);
                }

                values.Add(NormalizeRuntimeValue(property.GetValue(row)));
            }

            materialized.Add(new QueryMaterializedRow(index, values));
        }

        IReadOnlyList<QueryMaterializedRow> resultRows = plan.Aggregates.Count == 0
            ? materialized
            : ExecuteAggregates(plan, materialized, json);
        IEnumerable<QueryMaterializedRow> ordered = plan.OrderBy.Count == 0
            ? resultRows
            : resultRows.OrderBy(row => row, new QueryRowComparer(plan.OrderBy));
        return ordered.Skip(plan.Skip).Take(plan.Take).ToArray();
    }

    private static IReadOnlyList<QueryMaterializedRow> ExecuteAggregates(TableQueryPlan plan, IReadOnlyList<QueryMaterializedRow> sourceRows, bool json)
    {
        var groups = new List<QueryAggregateGroup>();
        if (plan.GroupKeys.Count == 0)
        {
            groups.Add(new QueryAggregateGroup(0, []));
        }

        foreach (QueryMaterializedRow sourceRow in sourceRows)
        {
            object?[] keyValues = plan.GroupKeys.Select(key => sourceRow.Values[key.SourceIndex]).ToArray();
            QueryAggregateGroup? group = groups.FirstOrDefault(candidate => GroupKeysEqual(candidate.KeyValues, keyValues, plan.GroupKeys));
            if (group is null)
            {
                group = new QueryAggregateGroup(sourceRow.SourceIndex, keyValues);
                groups.Add(group);
            }

            group.Add(sourceRow, plan.Aggregates);
        }

        var result = new List<QueryMaterializedRow>(groups.Count);
        foreach (QueryAggregateGroup group in groups)
        {
            var values = new List<object?>(plan.ResultColumns.Count);
            values.AddRange(group.KeyValues);
            foreach (QueryAggregate aggregate in plan.Aggregates)
            {
                values.Add(group.Finalize(aggregate, json));
            }

            result.Add(new QueryMaterializedRow(group.FirstSourceIndex, values));
        }

        return result;
    }

    private static bool GroupKeysEqual(IReadOnlyList<object?> left, IReadOnlyList<object?> right, IReadOnlyList<QueryGroupKey> keys)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index += 1)
        {
            if (CompareQueryValues(left[index], right[index], keys[index].Column.Type) != 0) return false;
        }

        return true;
    }

    private static string QueryFunctionName(TableDocument document)
    {
        var names = document.Tables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        string candidate = "__tscl_table_query";
        while (names.Contains(candidate)
            || document.SourceText.Contains("function " + candidate, StringComparison.Ordinal))
        {
            candidate += "_";
        }

        return candidate;
    }

    private static string BuildQuerySource(string source, string table, string? predicate, string functionName)
    {
        string predicateName = functionName + "_predicate";
        string predicateFunction = predicate is null
            ? string.Empty
            : "function " + predicateName + "(row: " + table + ".Row): boolean { return " + predicate + "; }" + Environment.NewLine;
        string rows = predicate is null
            ? table + ".rows().select(row => row)"
            : table + ".rows().where(" + predicateName + ").select(row => row)";
        return source + Environment.NewLine + predicateFunction + "function " + functionName + "(): " + table + ".Row[] { return " + rows + "; }" + Environment.NewLine;
    }

    private static Array InvokeQueryAssembly(string source, string functionName, bool json)
    {
        try
        {
            return CSharpLegacyQueryExecutor.ExecuteArray(source, functionName);
        }
        catch (Exception exception) when (exception is CSharpQueryMaterializationException or System.Reflection.TargetInvocationException)
        {
            throw Error("COPE-TABLE-QUERY-0016", "The C# query executor failed: " + (exception.InnerException?.Message ?? exception.Message), json);
        }
    }

    private static object? NormalizeRuntimeValue(object? value)
        => value is Enum @enum ? @enum.ToString() : value;

    private static void WriteQueryExplain(TableDocument document, TableQueryPlan plan, string format, MirTableQueryArtifact? artifact = null)
    {
        object result = new
        {
            schemaVersion = SchemaVersion,
            success = true,
            command = "table.query",
            source = document.SourcePath,
            table = plan.Table.Name,
            executor = "csharp-relation-plan",
            backend = artifact is null ? "legacy" : "roslyn-incremental-generator",
            queryArtifactId = artifact?.StableId,
            query = QuerySummary(plan),
            schema = new { columns = plan.ResultColumns.Select(QuerySchema).ToArray() },
            diagnostics = Array.Empty<object>(),
        };
        if (format == "json")
        {
            WriteJson(result);
            return;
        }

        Console.Out.WriteLine($"table: {plan.Table.Name}");
        Console.Out.WriteLine("executor: csharp-relation-plan");
        Console.Out.WriteLine("backend: " + (artifact is null ? "legacy" : "roslyn-incremental-generator"));
        if (artifact is not null) Console.Out.WriteLine("query-artifact: " + artifact.StableId);
        Console.Out.WriteLine("where: " + (plan.Predicate ?? "<none>"));
        Console.Out.WriteLine("group-by: " + (plan.GroupKeys.Count == 0 ? "<none>" : string.Join(", ", plan.GroupKeys.Select(key => key.Column.Name))));
        Console.Out.WriteLine("aggregates: " + (plan.Aggregates.Count == 0 ? "<none>" : string.Join(", ", plan.Aggregates.Select(aggregate => aggregate.Kind.ToString().ToLowerInvariant() + "(" + (aggregate.Input?.Name ?? string.Empty) + ") as " + aggregate.Name))));
        Console.Out.WriteLine("empty-input: " + (plan.Aggregates.Count == 0 ? "not applicable" : "count=0; sum=typed zero; average/min/max=diagnostic"));
        Console.Out.WriteLine("order-by: " + (plan.OrderBy.Count == 0 ? "first occurrence/source order" : string.Join(", ", plan.OrderBy.Select(term => term.Name + (term.Descending ? " desc" : " asc")))));
        Console.Out.WriteLine($"skip: {plan.Skip}; take: {plan.Take}");
        Console.Out.WriteLine("columns: " + string.Join(", ", plan.ResultColumns.Select(column => column.Name + ": " + column.Type.Name)));
    }

    private static void WriteQueryResult(TableDocument document, TableQueryPlan plan, IReadOnlyList<QueryMaterializedRow> rows, string format, MirTableQueryArtifact? artifact = null)
    {
        if (format == "csv")
        {
            Console.Out.Write(Csv.Write(
                plan.ResultColumns.Select(column => column.Name).ToArray(),
                rows.Select(row => plan.ResultColumns.Select(column => QueryDisplayValue(row.Values[column.ValueIndex])).ToArray()).ToArray()));
            return;
        }

        if (format == "json")
        {
            WriteJson(new
            {
                schemaVersion = SchemaVersion,
                success = true,
                command = "table.query",
                source = document.SourcePath,
                table = plan.Table.Name,
                executor = "csharp-relation-plan",
                backend = artifact is null ? "legacy" : "roslyn-incremental-generator",
                queryArtifactId = artifact?.StableId,
                schema = new { columns = plan.ResultColumns.Select(QuerySchema).ToArray() },
                query = QuerySummary(plan),
                rows = rows.Select(row => plan.ResultColumns.ToDictionary(column => column.Name, column => row.Values[column.ValueIndex], StringComparer.Ordinal)),
                rowCount = rows.Count,
                diagnostics = Array.Empty<object>(),
            });
            return;
        }

        string[] headers = plan.ResultColumns.Select(column => column.Name).ToArray();
        string[][] values = rows.Select(row => plan.ResultColumns.Select(column => QueryDisplayValue(row.Values[column.ValueIndex])).ToArray()).ToArray();
        int[] widths = headers.Select((header, index) => Math.Min(48, Math.Max(header.Length, values.Length == 0 ? 0 : values.Max(row => Math.Min(48, row[index].Length))))).ToArray();
        Console.Out.WriteLine(string.Join("  ", headers.Select((header, index) => header.Length <= widths[index] ? header.PadRight(widths[index]) : header[..Math.Max(1, widths[index] - 1)] + "…")));
        foreach (string[] row in values)
        {
            Console.Out.WriteLine(string.Join("  ", row.Select((value, index) => Truncate(value, widths[index]).PadRight(widths[index]))));
        }
    }

    private static object QuerySchema(QueryResultColumn column)
        => new { name = column.Name, type = column.Type.Name, provenance = column.Provenance };

    private static object QuerySummary(TableQueryPlan plan)
        => new
        {
            where = plan.Predicate,
            select = plan.Aggregates.Count == 0 ? plan.ResultColumns.Select(column => column.Name).ToArray() : Array.Empty<string>(),
            groupBy = plan.GroupKeys.Select(key => key.Column.Name).ToArray(),
            aggregates = plan.Aggregates.Select(aggregate => new { function = aggregate.Kind.ToString().ToLowerInvariant(), input = aggregate.Input?.Name, @as = aggregate.Name }).ToArray(),
            orderBy = plan.OrderBy.Select(term => new { column = term.Name, direction = term.Descending ? "descending" : "ascending" }).ToArray(),
            skip = plan.Skip,
            take = plan.Take,
        };

    private static string QueryDisplayValue(object? value)
        => value switch
        {
            null => string.Empty,
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

    private static string Truncate(string value, int width)
        => value.Length <= width ? value : value[..Math.Max(1, width - 1)] + "…";

    private static TableDocument LoadDocument(string sourcePath)
    {
        string fullPath = Path.GetFullPath(sourcePath);
        return LoadDocument(fullPath, File.ReadAllText(fullPath));
    }

    private static TableDocument LoadDocument(string sourcePath, string sourceText)
    {
        CopelandCompilation compilation = Compile(sourcePath, sourceText);
        if (!compilation.Success)
        {
            throw new CompilationException(compilation.Diagnostics, sourcePath, sourceText);
        }

        IReadOnlyList<TableDeclarationSyntax> declarations = Descendants(compilation.SyntaxTree!.Root)
            .OfType<TableDeclarationSyntax>()
            .ToArray();
        var tables = new List<TableModel>();
        foreach (TableDeclarationSyntax declaration in declarations)
        {
            BoundTableDefinition? bound = compilation.BoundCompilation!.Program.Tables.SingleOrDefault(table => table.TableType.Name == declaration.Identifier.Text);
            if (bound is null)
            {
                continue;
            }

            var columns = new List<TableColumnModel>();
            for (int index = 0; index < declaration.Columns.Count; index += 1)
            {
                columns.Add(new TableColumnModel(declaration.Columns[index], bound.Columns[index]));
            }

            tables.Add(new TableModel(declaration, bound, columns));
        }

        return new TableDocument(sourcePath, sourceText, tables);
    }

    private static void EnsureAuthored(TableModel table, string command, bool json)
    {
        if (table.Bound.Kind == BoundTableDefinitionKind.Derived)
        {
            throw Error("COPE-TABLE-TOOL-0020", $"{command} is unavailable for derived table '{table.Name}'; derived tables are read-only.", json);
        }
    }

    private static void WriteDerivedSchema(TableDocument document, TableModel table, BoundDerivedTableDefinition derived, bool json)
    {
        var columns = derived.Projections.Select(projection => new
        {
            name = projection.Column.Name,
            type = projection.Column.Type.Name,
            provenance = projection.CopiedSourceColumn is null
                ? new { kind = "computed", sourceTable = derived.SourceTable.Name, inputs = projection.SourceColumns.ToArray(), relationships = projection.Relationships.Select(join => RelationshipText(join.Relationship)).ToArray(), authoredPosition = projection.ExpressionPosition }
                : new { kind = "copied", sourceTable = derived.SourceTable.Name, inputs = new[] { projection.CopiedSourceColumn }, relationships = projection.Relationships.Select(join => RelationshipText(join.Relationship)).ToArray(), authoredPosition = projection.ExpressionPosition },
        }).ToArray();
        if (json)
        {
            WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.schema", table = table.Name, kind = "derived", readOnly = true, source = derived.SourceTable.Name, joins = derived.Joins.Select(join => new { relation = join.JoinedTable.Name, alias = join.Alias, through = RelationshipText(join.Relationship), cardinality = join.IsOneToOne ? "one-to-one" : "many-to-one" }), rowCount = derived.RowCount, columns });
            return;
        }
        Console.Out.WriteLine($"{table.Name} ({derived.RowCount} rows) derived read-only");
        Console.Out.WriteLine($"source: {derived.SourceTable.Name}");
        if (derived.Joins.Count > 0)
        {
            Console.Out.WriteLine("joins:");
            foreach (BoundDerivedTableJoin join in derived.Joins)
            {
                Console.Out.WriteLine($"  {join.JoinedTable.Name} as {join.Alias} through {RelationshipText(join.Relationship)} ({(join.IsOneToOne ? "one-to-one" : "many-to-one")})");
            }
        }
        foreach (BoundDerivedTableColumnDefinition projection in derived.Projections)
        {
            string provenance = projection.CopiedSourceColumn is null
                ? $"computed from {string.Join(", ", projection.SourceColumns)}"
                : $"from {projection.CopiedSourceColumn}";
            if (projection.Relationships.Count > 0)
            {
                provenance += $" through {string.Join(" and ", projection.Relationships.Select(join => RelationshipText(join.Relationship)))}";
            }
            Console.Out.WriteLine($"{projection.Column.Name}: {projection.Column.Type.Name} {provenance}");
        }
    }

    private static string RelationshipText(TableReferenceSymbol relationship)
        => $"{relationship.SourceTable.Name}.{relationship.SourceColumn.Name} -> {relationship.TargetTable.Name}.{relationship.TargetKey.Name}";

    private static CopelandCompilation Compile(string sourcePath, string sourceText)
        => CopelandCompiler.CompileToMir(sourceText, new CopelandCompilationOptions
        {
            SourcePath = sourcePath,
            ProjectRoot = Path.GetDirectoryName(sourcePath),
            AssetSource = ToolAssetSource.Instance,
        });

    private static void ValidateCandidate(string sourcePath, string candidate, bool json)
    {
        CopelandCompilation compilation = Compile(sourcePath, candidate);
        if (!compilation.Success)
        {
            Diagnostic diagnostic = compilation.Diagnostics.First();
            throw Error(diagnostic.Id, diagnostic.Message, json, sourcePath, candidate, diagnostic.Position);
        }
    }

    private static void PublishMutation(TableDocument original, string candidate, ParsedArguments parsed, string summary, object result)
    {
        if (!parsed.Has("--dry-run"))
        {
            AtomicWrite(original.SourcePath, candidate, Hash(original.SourceText));
        }

        if (parsed.Format == "json")
        {
            WriteJson(result);
            return;
        }

        Console.Out.WriteLine(summary);
        if (parsed.Has("--dry-run"))
        {
            Console.Out.WriteLine("Dry run: source was not written.");
        }
    }

    private static void AtomicWrite(string path, string content, string? expectedHash)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (expectedHash is not null && (!File.Exists(path) || !string.Equals(Hash(File.ReadAllText(path)), expectedHash, StringComparison.Ordinal)))
        {
            throw Error("COPE-TABLE-TOOL-0014", "Source changed while the table edit was being planned; no write was performed.", json: false, path);
        }

        string temporaryPath = path + ".table-tool-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Move(temporaryPath, path, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ParseCommandValue(string rawValue, TypeSymbol type, bool json)
    {
        if (type == PrimitiveTypeSymbol.String)
        {
            return JsonSerializer.Serialize(rawValue);
        }

        if (type == PrimitiveTypeSymbol.Boolean)
        {
            if (rawValue is "true" or "false") return rawValue;
            throw Error("COPE-TABLE-TOOL-0010", $"'{rawValue}' is not a boolean literal.", json);
        }

        if (type == PrimitiveTypeSymbol.Int)
        {
            if (int.TryParse(rawValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)) return number.ToString(CultureInfo.InvariantCulture);
            throw Error("COPE-TABLE-TOOL-0010", $"'{rawValue}' is not an int literal.", json);
        }

        if (type == PrimitiveTypeSymbol.Float || type == PrimitiveTypeSymbol.Number)
        {
            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) return rawValue;
            throw Error("COPE-TABLE-TOOL-0010", $"'{rawValue}' is not a finite numeric literal.", json);
        }

        if (type is EnumTypeSymbol enumType)
        {
            string caseName = rawValue.StartsWith(enumType.Name + ".", StringComparison.Ordinal) ? rawValue[(enumType.Name.Length + 1)..] : rawValue;
            EnumCaseSymbol? @case = enumType.Cases.SingleOrDefault(candidate => candidate.Name == caseName);
            if (@case is null || @case.HasPayload)
            {
                throw Error("COPE-TABLE-TOOL-0011", $"'{rawValue}' is not a zero-payload case of enum '{enumType.Name}'.", json);
            }

            return enumType.Name + "." + @case.Name;
        }

        throw Error("COPE-TABLE-TOOL-0010", $"Column type '{type.Name}' is not supported by M0 command literals. Use primitive values or zero-payload enum cases.", json);
    }

    private static string ParseCsvValue(string value, TableColumnModel column, string source, int line, int csvColumn, bool json)
    {
        try
        {
            string parsed = column.Bound.Column.Type == PrimitiveTypeSymbol.String
                ? JsonSerializer.Serialize(value)
                : ParseCommandValue(value, column.Bound.Column.Type, json);
            return PreserveNumericColumnFormatting(value, parsed, column, source);
        }
        catch (TableToolException exception)
        {
            throw Error(exception.Diagnostic.Code, exception.Diagnostic.Message + $" (CSV line {line}, column {csvColumn}).", json);
        }
    }

    private static string PreserveNumericColumnFormatting(string csvValue, string parsedValue, TableColumnModel column, string source)
    {
        TypeSymbol type = column.Bound.Column.Type;
        if (type != PrimitiveTypeSymbol.Float && type != PrimitiveTypeSymbol.Number)
        {
            return parsedValue;
        }

        int? precision = ExistingDecimalPrecision(source, column.Syntax.Cells);
        int? incomingPrecision = DecimalPrecision(csvValue);
        if (precision is null || incomingPrecision is null || incomingPrecision > precision)
        {
            return parsedValue;
        }

        if (!double.TryParse(csvValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            return parsedValue;
        }

        return value.ToString("F" + precision.Value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private static int? ExistingDecimalPrecision(string source, ArrayLiteralExpressionSyntax cells)
    {
        int precision = 0;
        foreach (ExpressionSyntax cell in cells.Elements)
        {
            int? cellPrecision = DecimalPrecision(Slice(source, SpanOf(cell)));
            if (cellPrecision is null)
            {
                return null;
            }

            precision = Math.Max(precision, cellPrecision.Value);
        }

        return precision;
    }

    private static int? DecimalPrecision(string value)
    {
        int decimalPoint = value.IndexOf(".", StringComparison.Ordinal);
        int start = value.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
        if (start >= value.Length)
        {
            return null;
        }

        for (int index = start; index < value.Length; index += 1)
        {
            if (index == decimalPoint)
            {
                continue;
            }

            if (!char.IsAsciiDigit(value[index]))
            {
                return null;
            }
        }

        if (decimalPoint < 0)
        {
            return 0;
        }

        return decimalPoint == value.Length - 1 ? null : value.Length - decimalPoint - 1;
    }

    private static Dictionary<string, string> ParseRowJson(string text, TableModel table, bool json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Error("COPE-TABLE-TOOL-0007", "--json must contain one JSON object.", json);
            }

            Dictionary<string, JsonElement> supplied = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            foreach (string name in supplied.Keys)
            {
                if (!table.Columns.Any(column => column.Name == name))
                {
                    throw Error("COPE-TABLE-TOOL-0008", $"Input field '{name}' is not a column of table '{table.Name}'.", json);
                }
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TableColumnModel column in table.Columns)
            {
                if (!supplied.TryGetValue(column.Name, out JsonElement value))
                {
                    throw Error("COPE-TABLE-TOOL-0009", $"Input is missing required column '{column.Name}'.", json);
                }

                result.Add(column.Name, ParseJsonValue(value, column.Bound.Column.Type, json));
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw Error("COPE-TABLE-TOOL-0007", "Invalid JSON row: " + exception.Message, json);
        }
    }

    private static string ParseJsonValue(JsonElement value, TypeSymbol type, bool json)
    {
        if (type == PrimitiveTypeSymbol.String && value.ValueKind == JsonValueKind.String) return JsonSerializer.Serialize(value.GetString());
        if (type == PrimitiveTypeSymbol.Boolean && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)) return value.GetBoolean() ? "true" : "false";
        if (type == PrimitiveTypeSymbol.Int && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int integer)) return integer.ToString(CultureInfo.InvariantCulture);
        if ((type == PrimitiveTypeSymbol.Float || type == PrimitiveTypeSymbol.Number) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number)) return number.ToString("R", CultureInfo.InvariantCulture);
        if (type is EnumTypeSymbol && value.ValueKind == JsonValueKind.String) return ParseCommandValue(value.GetString()!, type, json);
        throw Error("COPE-TABLE-TOOL-0010", $"JSON value does not match declared column type '{type.Name}'.", json);
    }

    private static void ValidateHeaders(IReadOnlyList<string> headers, TableModel table, bool json)
    {
        if (headers.Count != headers.Distinct(StringComparer.Ordinal).Count())
        {
            throw Error("COPE-TABLE-TOOL-0012", "CSV contains duplicate headers.", json);
        }

        IReadOnlyList<string> expected = table.Columns.Select(column => column.Name).ToArray();
        if (!headers.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw Error("COPE-TABLE-TOOL-0012", "CSV headers must exactly match declared table columns in declaration order.", json);
        }
    }

    private static IReadOnlyList<TableColumnModel> SelectColumns(TableModel table, string? selected, bool json)
    {
        if (string.IsNullOrWhiteSpace(selected)) return table.Columns;
        var columns = new List<TableColumnModel>();
        foreach (string name in selected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (columns.Any(column => column.Name == name))
            {
                throw Error("COPE-TABLE-TOOL-0008", $"Column '{name}' was selected more than once.", json);
            }

            columns.Add(table.RequireColumn(name, json));
        }

        return columns;
    }

    private static IReadOnlyList<RowValue> ProjectRows(TableModel table, IReadOnlyList<TableColumnModel> columns, int offset, int limit)
    {
        if (offset > table.Bound.RowCount) return [];
        int count = Math.Min(limit, table.Bound.RowCount - offset);
        var rows = new List<RowValue>(count);
        for (int row = offset; row < offset + count; row += 1)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (TableColumnModel column in columns)
            {
                values.Add(column.Name, SerializeConstant(column.Bound.Cells[row]));
            }

            rows.Add(new RowValue(row, values));
        }

        return rows;
    }

    private static object? SerializeConstant(BoundTableConstant constant)
        => constant switch
        {
            BoundTableLiteralConstant literal => literal.Value,
            BoundTableArrayConstant array => array.Elements.Select(SerializeConstant).ToArray(),
            BoundTableRecordConstant record => record.Fields.ToDictionary(field => field.Field.Name, field => SerializeConstant(field.Value), StringComparer.Ordinal),
            BoundTableEnumConstant @enum when !@enum.Case.HasPayload => @enum.Case.Name,
            BoundTableEnumConstant @enum => new Dictionary<string, object?> { ["case"] = @enum.Case.Name, ["values"] = @enum.Payloads.Select(SerializeConstant).ToArray() },
            BoundTableResultConstant result => new Dictionary<string, object?> { [result.IsOk ? "ok" : "err"] = SerializeConstant(result.Payload) },
            _ => constant.ToString(),
        };

    private static string CsvValue(BoundTableConstant constant)
        => constant switch
        {
            BoundTableLiteralConstant literal => Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            BoundTableEnumConstant @enum when !@enum.Case.HasPayload => @enum.Case.Name,
            _ => JsonSerializer.Serialize(SerializeConstant(constant)),
        };

    private static IReadOnlyList<string>? EnumCases(TypeSymbol type)
        => type is EnumTypeSymbol @enum ? @enum.Cases.Where(@case => !@case.HasPayload).Select(@case => @case.Name).ToArray() : null;

    private static TextEdit AppendEdit(string source, ArrayLiteralExpressionSyntax array, string value)
    {
        if (array.Elements.Count == 0)
        {
            return new TextEdit(new TextSpan(array.OpenBracketToken.Position + 1, 0), value);
        }

        string inner = Slice(source, new TextSpan(array.OpenBracketToken.Position + 1, array.CloseBracketToken.Position - array.OpenBracketToken.Position - 1));
        string separator = inner.Contains('\n') || inner.Contains('\r')
            ? "," + DetectNewline(source) + ElementIndent(source, array) + value
            : ", " + value;
        return new TextEdit(new TextSpan(array.CloseBracketToken.Position, 0), separator);
    }

    private static TextEdit DeleteEdit(ArrayLiteralExpressionSyntax array, int row)
    {
        int count = array.Elements.Count;
        if (count == 1)
        {
            TextSpan span = SpanOf(array.Elements[0]);
            return new TextEdit(span, string.Empty);
        }

        if (row < count - 1)
        {
            int start = SpanOf(array.Elements[row]).Start;
            int end = SpanOf(array.Elements[row + 1]).Start;
            return new TextEdit(new TextSpan(start, end - start), string.Empty);
        }

        int comma = array.CommaTokens[row - 1].Position;
        int endLast = SpanOf(array.Elements[row]).End;
        return new TextEdit(new TextSpan(comma, endLast - comma), string.Empty);
    }

    private static TextEdit ReplaceArrayEdit(string source, ArrayLiteralExpressionSyntax array, IReadOnlyList<string> values)
    {
        string replacement;
        string original = Slice(source, SpanOf(array));
        if (values.Count == 0)
        {
            replacement = "[]";
        }
        else if (!original.Contains('\n') && !original.Contains('\r'))
        {
            replacement = "[" + string.Join(", ", values) + "]";
        }
        else
        {
            string newline = DetectNewline(source);
            string indent = ElementIndent(source, array);
            replacement = "[" + newline + indent + string.Join("," + newline + indent, values) + newline + IndentAt(source, array.CloseBracketToken.Position) + "]";
        }

        return new TextEdit(SpanOf(array), replacement);
    }

    private static string ApplyEdits(string source, IEnumerable<TextEdit> edits)
    {
        StringBuilder builder = new(source);
        foreach (TextEdit edit in edits.OrderByDescending(edit => edit.Span.Start))
        {
            builder.Remove(edit.Span.Start, edit.Span.Length);
            builder.Insert(edit.Span.Start, edit.Replacement);
        }

        return builder.ToString();
    }

    private static TextSpan SpanOf(SyntaxNode node)
    {
        SyntaxToken[] tokens = Tokens(node).OrderBy(token => token.Position).ToArray();
        if (tokens.Length == 0) return new TextSpan(0, 0);
        int start = tokens[0].Position;
        int end = tokens.Max(token => token.Position + token.Text.Length);
        return new TextSpan(start, end - start);
    }

    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode node)
    {
        foreach (object child in node.GetChildren())
        {
            if (child is SyntaxToken token) yield return token;
            if (child is SyntaxNode childNode)
            {
                foreach (SyntaxToken descendant in Tokens(childNode)) yield return descendant;
            }
        }
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
    {
        yield return node;
        foreach (object child in node.GetChildren())
        {
            if (child is not SyntaxNode childNode)
            {
                continue;
            }

            foreach (SyntaxNode descendant in Descendants(childNode))
            {
                yield return descendant;
            }
        }
    }

    private static string Slice(string text, TextSpan span) => text.Substring(span.Start, span.Length);

    private static string ElementIndent(string source, ArrayLiteralExpressionSyntax array)
        => array.Elements.Count > 0 ? IndentAt(source, SpanOf(array.Elements[0]).Start) : IndentAt(source, array.OpenBracketToken.Position) + "    ";

    private static string IndentAt(string source, int position)
    {
        int lineStart = source.LastIndexOfAny(['\r', '\n'], Math.Max(0, position - 1)) + 1;
        int end = lineStart;
        while (end < source.Length && (source[end] == ' ' || source[end] == '\t')) end += 1;
        return source.Substring(lineStart, end - lineStart);
    }

    private static string DetectNewline(string source) => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void EnsureRow(TableModel table, int row, bool json)
    {
        if (row >= table.Bound.RowCount)
        {
            throw Error("COPE-TABLE-TOOL-0006", $"Row {row} is outside table '{table.Name}' with {table.Bound.RowCount} rows.", json);
        }
    }

    private static int ParseRequiredNonNegative(ParsedArguments parsed, string option)
        => ParseNonNegative(parsed.Required(option), option, parsed.Format == "json");

    private static int ParseNonNegative(string value, string option, bool json)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result))
        {
            throw Error("COPE-TABLE-TOOL-0005", $"Option '{option}' requires a non-negative integer.", json);
        }

        return result;
    }

    private static ParsedArguments ParseOptions(string[] args, int start, int positionalCount, IReadOnlyList<string> allowed)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = start; index < args.Length; index += 1)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(argument);
                continue;
            }

            if (!allowed.Contains(argument, StringComparer.Ordinal))
            {
                throw Error("COPE-TABLE-TOOL-0002", $"Unknown option '{argument}'.", HasJsonFormat(args));
            }

            bool flag = argument is "--dry-run" or "--replace" or "--explain";
            if (options.ContainsKey(argument))
            {
                throw Error("COPE-TABLE-TOOL-0003", $"Option '{argument}' was supplied more than once.", HasJsonFormat(args));
            }

            if (flag)
            {
                options.Add(argument, null);
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw Error("COPE-TABLE-TOOL-0004", $"Option '{argument}' requires a value.", HasJsonFormat(args));
            }

            options.Add(argument, args[index + 1]);
            index += 1;
        }

        if (positional.Count != positionalCount)
        {
            throw Error("COPE-TABLE-TOOL-0001", "Unexpected table command arguments.", HasJsonFormat(args));
        }

        string format = options.GetValueOrDefault("--format") ?? "text";
        if (format is not "text" and not "json" and not "csv")
        {
            throw Error("COPE-TABLE-TOOL-0002", "Option '--format' must be 'text', 'json', or 'csv' where supported.", HasJsonFormat(args));
        }

        return new ParsedArguments(positional, options, format);
    }

    private static bool HasJsonFormat(IEnumerable<string> args)
    {
        string[] values = args.ToArray();
        for (int index = 0; index + 1 < values.Length; index += 1)
        {
            if ((values[index] == "--format" || values[index] == "--result-format") && values[index + 1] == "json") return true;
        }

        return false;
    }

    private static SourceLocation Location(string path, string source, int position)
    {
        int line = 1;
        int column = 1;
        for (int index = 0; index < position; index += 1)
        {
            if (source[index] == '\n')
            {
                line += 1;
                column = 1;
            }
            else
            {
                column += 1;
            }
        }

        return new SourceLocation(path, line, column);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string FormatRow(IEnumerable<KeyValuePair<string, object?>> values)
        => "{" + string.Join(", ", values.Select(pair => pair.Key + ": " + JsonSerializer.Serialize(pair.Value))) + "}";

    private static void WriteTextRows(IReadOnlyList<TableColumnModel> columns, IReadOnlyList<RowValue> rows)
    {
        string[] headers = ["row", .. columns.Select(column => column.Name)];
        var data = rows.Select(row => new[] { row.Index.ToString(CultureInfo.InvariantCulture) }
            .Concat(columns.Select(column => CsvValue(column.Bound.Cells[row.Index]))).ToArray()).ToArray();
        int[] widths = headers.Select((header, index) => Math.Max(header.Length, data.Length == 0 ? 0 : data.Max(row => row[index].Length))).ToArray();
        Console.Out.WriteLine(string.Join("  ", headers.Select((header, index) => header.PadRight(widths[index]))));
        foreach (string[] row in data)
        {
            Console.Out.WriteLine(string.Join("  ", row.Select((value, index) => value.PadRight(widths[index]))));
        }
    }

    private static void WriteCompilationFailure(string command, IReadOnlyList<Diagnostic> diagnostics, string path, string source, bool json)
    {
        if (json)
        {
            WriteJson(new { schemaVersion = SchemaVersion, success = false, command, diagnostics = diagnostics.Select(diagnostic => ToJsonDiagnostic(diagnostic, path, source)) });
        }
        else
        {
            foreach (Diagnostic diagnostic in diagnostics) Console.Error.WriteLine(diagnostic.Id + " error: " + diagnostic.Message);
        }
    }

    private static object ToJsonDiagnostic(Diagnostic diagnostic, string defaultPath, string source)
    {
        SourceLocation location = Location(diagnostic.SourcePath ?? defaultPath, source, diagnostic.Position);
        return new { code = diagnostic.Id, severity = "error", message = diagnostic.Message, file = location.File, line = location.Line, column = location.Column };
    }

    private static void WriteFailure(string command, ToolDiagnostic diagnostic, bool json)
    {
        if (json)
        {
            WriteJson(new { schemaVersion = SchemaVersion, success = false, command, diagnostics = new[] { new { code = diagnostic.Code, severity = "error", message = diagnostic.Message, file = diagnostic.File, line = diagnostic.Line, column = diagnostic.Column } } });
        }
        else
        {
            Console.Error.WriteLine(diagnostic.Code + " error: " + diagnostic.Message);
        }
    }

    private static void WriteJson(object value) => Console.Out.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    }));

    private static TableToolException Error(string code, string message, bool json, string? path = null, string? source = null, int? position = null)
    {
        int? line = null;
        int? column = null;
        if (source is not null && position is not null)
        {
            SourceLocation location = Location(path ?? string.Empty, source, position.Value);
            line = location.Line;
            column = location.Column;
        }

        return new TableToolException(new ToolDiagnostic(code, message, path, line, column), json);
    }

    private static bool ResultIsJson(ParsedArguments parsed)
    {
        string resultFormat = parsed.Value("--result-format") ?? "text";
        if (resultFormat is not "text" and not "json")
        {
            throw Error("COPE-TABLE-TOOL-0002", "Option '--result-format' must be 'text' or 'json'.", HasJsonFormat(parsed.Positionals));
        }

        return resultFormat == "json";
    }

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine("Usage: tscl table list <source> [--format text|json]");
        Console.Error.WriteLine("       tscl table schema|rows <source> <table> [options]");
        Console.Error.WriteLine("       tscl table query <source> <table> [--where <expression>] [--select <columns>] [--group-by <columns>] [--aggregate <calls>] [--order-by <terms>] [--skip n] [--take n] [--format text|json|csv]");
        Console.Error.WriteLine("       tscl table set|add-row|delete-row <source> <table> [options]");
        Console.Error.WriteLine("       tscl table validate <source> [--format text|json]");
        Console.Error.WriteLine("       tscl table export|import <source> <table> --format csv [options]");
        Console.Error.WriteLine(code + " error: " + message);
        return UsageExitCode;
    }

    private sealed record TableDocument(string SourcePath, string SourceText, IReadOnlyList<TableModel> Tables)
    {
        public TableModel RequireTable(string name, bool json)
            => Tables.SingleOrDefault(table => table.Name == name)
                ?? throw Error("COPE-TABLE-TOOL-0005", $"Record table '{name}' was not found.", json, SourcePath);
    }

    private sealed record TableModel(TableDeclarationSyntax Syntax, BoundTableDefinition Bound, IReadOnlyList<TableColumnModel> Columns)
    {
        public string Name => Syntax.Identifier.Text;
        public TableColumnModel RequireColumn(string name, bool json)
            => Columns.SingleOrDefault(column => column.Name == name)
                ?? throw Error("COPE-TABLE-TOOL-0005", $"Column '{name}' was not found in table '{Name}'.", json);
    }

    private sealed record TableColumnModel(TableColumnSyntax Syntax, BoundTableColumnDefinition Bound)
    {
        public string Name => Syntax.Identifier.Text;
    }

    private sealed record RowValue(int Index, IReadOnlyDictionary<string, object?> Values);
    private sealed record TableQueryRequest(
        string? Where,
        IReadOnlyList<QuerySelectRequest> Select,
        IReadOnlyList<string> GroupBy,
        IReadOnlyList<QueryAggregateRequest> Aggregates,
        IReadOnlyList<QueryOrderRequest> OrderBy,
        int Skip,
        int Take);
    private sealed record QuerySelectRequest(string Column, string? Alias);
    private sealed record QueryAggregateRequest(string Function, string? Input, string Alias);
    private sealed record QueryOrderRequest(string Column, string Direction);
    private sealed record TableQueryPlan(
        TableModel Table,
        IReadOnlyList<TableColumnSymbol> SourceColumns,
        string? Predicate,
        IReadOnlyList<QueryProjection> Projection,
        IReadOnlyList<QueryGroupKey> GroupKeys,
        IReadOnlyList<QueryAggregate> Aggregates,
        IReadOnlyList<QueryResultColumn> ResultColumns,
        IReadOnlyList<QueryOrderTerm> OrderBy,
        int Skip,
        int Take);
    private sealed record QueryProjection(string Name, TableColumnSymbol Column, int SourceIndex, QueryColumnProvenance Provenance);
    private sealed record QueryGroupKey(TableColumnSymbol Column, int SourceIndex, QueryColumnProvenance Provenance);
    private sealed record QueryAggregate(string Name, QueryAggregateKind Kind, TableColumnSymbol? Input, int InputIndex, TypeSymbol Type, QueryColumnProvenance Provenance);
    private sealed record QueryResultColumn(string Name, TypeSymbol Type, int ValueIndex, QueryColumnProvenance Provenance);
    private sealed record QueryOrderTerm(string Name, TypeSymbol Type, int ValueIndex, bool Descending);
    private sealed record QueryColumnProvenance(string Kind, string SourceTable, IReadOnlyList<string> Inputs, IReadOnlyList<string> Relationships, string? Aggregate = null, string? Filter = null);
    private sealed record QueryMaterializedRow(int SourceIndex, IReadOnlyList<object?> Values);

    private enum QueryAggregateKind { Count, Sum, Average, Min, Max }

    private sealed class QueryAggregateGroup(int firstSourceIndex, IReadOnlyList<object?> keyValues)
    {
        private readonly Dictionary<QueryAggregate, QueryAggregateAccumulator> _accumulators = [];

        public int FirstSourceIndex { get; } = firstSourceIndex;
        public IReadOnlyList<object?> KeyValues { get; } = keyValues;

        public void Add(QueryMaterializedRow row, IReadOnlyList<QueryAggregate> aggregates)
        {
            foreach (QueryAggregate aggregate in aggregates)
            {
                if (!_accumulators.TryGetValue(aggregate, out QueryAggregateAccumulator? accumulator))
                {
                    accumulator = new QueryAggregateAccumulator(aggregate);
                    _accumulators.Add(aggregate, accumulator);
                }

                accumulator.Add(aggregate.InputIndex < 0 ? null : row.Values[aggregate.InputIndex]);
            }
        }

        public object? Finalize(QueryAggregate aggregate, bool json)
        {
            if (!_accumulators.TryGetValue(aggregate, out QueryAggregateAccumulator? accumulator))
            {
                accumulator = new QueryAggregateAccumulator(aggregate);
            }

            return accumulator.Finalize(json);
        }
    }

    private sealed class QueryAggregateAccumulator(QueryAggregate aggregate)
    {
        private int _count;
        private int _intSum;
        private double _numberSum;
        private object? _minimum;
        private object? _maximum;

        public void Add(object? value)
        {
            _count += 1;
            switch (aggregate.Kind)
            {
                case QueryAggregateKind.Count:
                    return;
                case QueryAggregateKind.Sum:
                case QueryAggregateKind.Average:
                    if (aggregate.Type == PrimitiveTypeSymbol.Int)
                    {
                        _intSum = checked(_intSum + (int)value!);
                    }
                    else
                    {
                        _numberSum += (double)value!;
                    }
                    return;
                case QueryAggregateKind.Min:
                    if (_minimum is null || CompareQueryValues(value, _minimum, aggregate.Type) < 0)
                    {
                        _minimum = value;
                    }
                    return;
                case QueryAggregateKind.Max:
                    if (_maximum is null || CompareQueryValues(value, _maximum, aggregate.Type) > 0)
                    {
                        _maximum = value;
                    }
                    return;
            }
        }

        public object? Finalize(bool json)
        {
            return aggregate.Kind switch
            {
                QueryAggregateKind.Count => _count,
                QueryAggregateKind.Sum when aggregate.Type == PrimitiveTypeSymbol.Int => _intSum,
                QueryAggregateKind.Sum => _numberSum,
                QueryAggregateKind.Average when _count > 0 => _numberSum / _count,
                QueryAggregateKind.Min when _count > 0 => _minimum,
                QueryAggregateKind.Max when _count > 0 => _maximum,
                _ => throw Error("COPE-TABLE-QUERY-0027", $"Aggregate '{aggregate.Kind.ToString().ToLowerInvariant()}' is not defined for empty input. Use count() or sum(), or filter only when a value exists.", json),
            };
        }
    }

    private sealed class QueryRowComparer(IReadOnlyList<QueryOrderTerm> terms) : IComparer<QueryMaterializedRow>
    {
        public int Compare(QueryMaterializedRow? left, QueryMaterializedRow? right)
        {
            if (left is null || right is null) return ReferenceEquals(left, right) ? 0 : left is null ? -1 : 1;
            foreach (QueryOrderTerm term in terms)
            {
                int comparison = CompareQueryValues(left.Values[term.ValueIndex], right.Values[term.ValueIndex], term.Type);
                if (comparison != 0) return term.Descending ? -comparison : comparison;
            }

            return left.SourceIndex.CompareTo(right.SourceIndex);
        }

    }

    private static int CompareQueryValues(object? left, object? right, TypeSymbol type)
    {
        if (type == PrimitiveTypeSymbol.String || type is EnumTypeSymbol)
        {
            return StringComparer.Ordinal.Compare((string?)left, (string?)right);
        }
        if (type == PrimitiveTypeSymbol.Boolean)
        {
            return ((bool)left!).CompareTo((bool)right!);
        }
        if (type == PrimitiveTypeSymbol.Int)
        {
            return ((int)left!).CompareTo((int)right!);
        }
        if (type == PrimitiveTypeSymbol.Float || type == PrimitiveTypeSymbol.Number)
        {
            return ((double)left!).CompareTo((double)right!);
        }

        throw new InvalidOperationException($"Unexpected query comparison type '{type.Name}'.");
    }
    private sealed record TextSpan(int Start, int Length) { public int End => Start + Length; }
    private sealed record TextEdit(TextSpan Span, string Replacement);
    private sealed record ToolDiagnostic(string Code, string Message, string? File, int? Line, int? Column);
    private sealed record SourceLocation(string File, int Line, int Column);

    private class TableToolException(ToolDiagnostic diagnostic, bool json) : Exception(diagnostic.Message)
    {
        public ToolDiagnostic Diagnostic { get; } = diagnostic;
        public bool Json { get; } = json;
    }

    private sealed class CompilationException : TableToolException
    {
        public CompilationException(IReadOnlyList<Diagnostic> diagnostics, string path, string source)
            : base(CreateDiagnostic(diagnostics, path, source), json: false)
        {
        }

        private static ToolDiagnostic CreateDiagnostic(IReadOnlyList<Diagnostic> diagnostics, string path, string source)
        {
            Diagnostic diagnostic = diagnostics.First();
            SourceLocation location = Location(path, source, diagnostic.Position);
            return new ToolDiagnostic(diagnostic.Id, diagnostic.Message, path, location.Line, location.Column);
        }
    }

    private sealed record ParsedArguments(IReadOnlyList<string> Positionals, IReadOnlyDictionary<string, string?> Options, string Format)
    {
        public bool Has(string name) => Options.ContainsKey(name);
        public string? Value(string name) => Options.GetValueOrDefault(name);
        public string Required(string name) => Value(name) ?? throw Error("COPE-TABLE-TOOL-0004", $"Missing required option '{name}'.", Format == "json");
    }

    private sealed class ToolAssetSource : ICopelandAssetSource
    {
        public static ToolAssetSource Instance { get; } = new();
        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            try
            {
                sourceText = File.ReadAllText(normalizedPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sourceText = null;
                return false;
            }
        }
    }

    private sealed record CsvRecord(IReadOnlyList<string> Cells, int Line);
    private sealed record CsvDocument(IReadOnlyList<string> Headers, IReadOnlyList<CsvRecord> Rows);

    private static class Csv
    {
        public static string Write(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
        {
            StringBuilder builder = new();
            WriteRow(builder, headers);
            foreach (string[] row in rows) WriteRow(builder, row);
            return builder.ToString();
        }

        public static CsvDocument Read(string text)
        {
            var rows = new List<CsvRecord>();
            var cells = new List<string>();
            StringBuilder cell = new();
            bool quoted = false;
            int line = 1;
            int recordLine = 1;
            for (int index = 0; index < text.Length; index += 1)
            {
                char current = text[index];
                if (quoted)
                {
                    if (current == '"' && index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index += 1; continue; }
                    if (current == '"') { quoted = false; continue; }
                    cell.Append(current);
                    if (current == '\n') line += 1;
                    continue;
                }

                if (current == '"' && cell.Length == 0) { quoted = true; continue; }
                if (current == ',') { cells.Add(cell.ToString()); cell.Clear(); continue; }
                if (current == '\r' || current == '\n')
                {
                    if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index += 1;
                    cells.Add(cell.ToString()); cell.Clear();
                    rows.Add(new CsvRecord(cells.ToArray(), recordLine)); cells.Clear();
                    line += 1; recordLine = line;
                    continue;
                }

                cell.Append(current);
            }

            if (quoted) throw Error("COPE-TABLE-TOOL-0013", "CSV contains an unterminated quoted cell.", json: false);
            if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString()); rows.Add(new CsvRecord(cells.ToArray(), recordLine)); }
            if (rows.Count == 0) throw Error("COPE-TABLE-TOOL-0013", "CSV must contain a header row.", json: false);
            int width = rows[0].Cells.Count;
            if (rows.Any(row => row.Cells.Count != width)) throw Error("COPE-TABLE-TOOL-0013", "CSV rows have inconsistent cell counts.", json: false);
            return new CsvDocument(rows[0].Cells, rows.Skip(1).ToArray());
        }

        private static void WriteRow(StringBuilder builder, IEnumerable<string> row)
        {
            builder.Append(string.Join(',', row.Select(Escape))).Append('\n');
        }

        private static string Escape(string value)
            => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
