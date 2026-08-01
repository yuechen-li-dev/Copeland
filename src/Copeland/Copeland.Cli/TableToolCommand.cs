using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

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
                ? new { kind = "computed", sourceTable = derived.SourceTable.Name, inputs = projection.SourceColumns, authoredPosition = projection.ExpressionPosition }
                : new { kind = "copied", sourceTable = derived.SourceTable.Name, inputs = new[] { projection.CopiedSourceColumn }, authoredPosition = projection.ExpressionPosition },
        }).ToArray();
        if (json)
        {
            WriteJson(new { schemaVersion = SchemaVersion, success = true, command = "table.schema", table = table.Name, kind = "derived", readOnly = true, source = derived.SourceTable.Name, rowCount = derived.RowCount, columns });
            return;
        }
        Console.Out.WriteLine($"{table.Name} ({derived.RowCount} rows) derived read-only");
        Console.Out.WriteLine($"source: {derived.SourceTable.Name}");
        foreach (BoundDerivedTableColumnDefinition projection in derived.Projections)
        {
            string provenance = projection.CopiedSourceColumn is null
                ? $"computed from {derived.SourceTable.Name}"
                : $"from {derived.SourceTable.Name}.{projection.CopiedSourceColumn}";
            Console.Out.WriteLine($"{projection.Column.Name}: {projection.Column.Type.Name} {provenance}");
        }
    }

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

            bool flag = argument is "--dry-run" or "--replace";
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
