using System.Text;
using System.Text.Json;
using Copeland.TS.Database;

namespace Copeland.Cli;

internal static class DatabaseCommand
{
    private const int SuccessExitCode = 0;
    private const int CompileFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;
    private const int FileIoErrorExitCode = 3;

    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[1] != "build")
        {
            return Usage("COPE-CLI-DATABASE-0001", "Usage: database build --schema <schema.ts> --definition <index.tsx> --input <rows.json> --output <directory> --generated-source <file>.");
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 2; index < args.Length; index += 2)
        {
            string option = args[index];
            if (!option.StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length)
            {
                return Usage("COPE-CLI-DATABASE-0002", $"Option '{option}' requires a value.");
            }

            if (option is not ("--schema" or "--definition" or "--input" or "--output" or "--generated-source"))
            {
                return Usage("COPE-CLI-DATABASE-0003", $"Unknown database build option '{option}'.");
            }

            if (!options.TryAdd(option, args[index + 1]))
            {
                return Usage("COPE-CLI-DATABASE-0004", $"Duplicate database build option '{option}'.");
            }
        }

        foreach (string required in new[] { "--schema", "--definition", "--input", "--output", "--generated-source" })
        {
            if (!options.ContainsKey(required))
            {
                return Usage("COPE-CLI-DATABASE-0005", $"Missing required database build option '{required}'.");
            }
        }

        try
        {
            string schemaPath = Path.GetFullPath(options["--schema"]);
            string definitionPath = Path.GetFullPath(options["--definition"]);
            DatabaseDefinitionResult binding = DatabaseDefinitionBinder.Bind(
                File.ReadAllText(schemaPath),
                File.ReadAllText(definitionPath),
                schemaPath,
                definitionPath);
            if (!binding.Success)
            {
                foreach (var diagnostic in binding.Diagnostics)
                {
                    Console.Error.WriteLine($"{diagnostic.Id} error: {diagnostic.Message}");
                }

                return CompileFailureExitCode;
            }

            DatabaseRow[] rows = ReadRows(options["--input"], binding.Schema!);
            DatabaseBuildResult build = DatabaseBuilder.Build(binding.Schema!, rows);
            build.WriteToDirectory(options["--output"]);

            string generatedPath = Path.GetFullPath(options["--generated-source"]);
            string? generatedDirectory = Path.GetDirectoryName(generatedPath);
            if (generatedDirectory is not null)
            {
                Directory.CreateDirectory(generatedDirectory);
            }

            File.WriteAllText(
                generatedPath,
                build.GeneratedSource,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.Out.WriteLine(
                $"Built {build.Metrics.RowCount} rows into {build.Metrics.LeafCount} leaves ({build.Metrics.BinaryBytes} bytes, {build.Metrics.BuildTime.TotalMilliseconds:F3} ms in-process).");
            Console.Out.WriteLine($"Schema {build.Schema.SchemaIdentity}");
            Console.Out.WriteLine($"Index {build.Schema.IndexIdentity}");
            return SuccessExitCode;
        }
        catch (DatabaseBuildException exception)
        {
            Console.Error.WriteLine($"COPE-CLI-DATABASE-0006 error: {exception.Message}");
            return CompileFailureExitCode;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"COPE-CLI-DATABASE-0007 error: Invalid JSON input: {exception.Message}");
            return CompileFailureExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"COPE-CLI-DATABASE-0008 error: {exception.Message}");
            return FileIoErrorExitCode;
        }
    }

    private static DatabaseRow[] ReadRows(string inputPath, DatabaseSchema schema)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(inputPath));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new DatabaseBuildException("Database input must be a JSON array of row objects.");
        }

        var rows = new List<DatabaseRow>();
        int rowIndex = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new DatabaseBuildException($"Row {rowIndex} is not an object.");
            }

            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (DatabaseField field in schema.Fields)
            {
                if (!element.TryGetProperty(field.Name, out JsonElement value))
                {
                    throw new DatabaseBuildException($"Row {rowIndex} is missing field '{field.Name}'.");
                }

                values.Add(field.Name, ReadValue(value, field, rowIndex));
            }

            string? extra = element.EnumerateObject()
                .Select(property => property.Name)
                .FirstOrDefault(name => !schema.Fields.Any(field => field.Name == name));
            if (extra is not null)
            {
                throw new DatabaseBuildException($"Row {rowIndex} has unknown field '{extra}'.");
            }

            rows.Add(new DatabaseRow(values));
            rowIndex++;
        }

        return rows.ToArray();
    }

    private static object ReadValue(JsonElement value, DatabaseField field, int rowIndex)
    {
        try
        {
            return field.Type switch
            {
                DatabaseScalarType.Boolean => value.GetBoolean(),
                DatabaseScalarType.Int32 => value.GetInt32(),
                DatabaseScalarType.Float64 => value.GetDouble(),
                DatabaseScalarType.String => value.GetString()
                    ?? throw new DatabaseBuildException($"Row {rowIndex} field '{field.Name}' cannot be null."),
                _ => throw new DatabaseBuildException($"Unsupported field type '{field.Type}'."),
            };
        }
        catch (InvalidOperationException)
        {
            throw new DatabaseBuildException(
                $"Row {rowIndex} field '{field.Name}' is not a valid {field.Type} JSON value.");
        }
    }

    private static int Usage(string id, string message)
    {
        Console.Error.WriteLine($"{id} error: {message}");
        return UsageErrorExitCode;
    }
}
