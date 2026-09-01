using System.Security.Cryptography;
using Copeland.TS.Tson;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionTableCardRealization(
    bool Succeeded,
    OblivionTableSource Source,
    string? Profile,
    TsonTable? Table,
    string? SourceHash,
    long LoadMilliseconds,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public sealed class OblivionTableCardRealizer
{
    public OblivionTableCardRealization Realize(OblivionCard card, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        OblivionTableSource source = card.Table ?? new OblivionTableSource(
            OblivionTableSourceKind.TsonTable,
            string.Empty);
        if (card.Kind != OblivionCardKind.Table || card.Table is null)
        {
            return Failure(source, "OBLIVION-TABLE-SOURCE-MISSING", "The Card has no TSON table source.");
        }

        if (!TryResolveProfile(source.Reference, out TsonDocumentProfile profile, out string? profileName))
        {
            return Failure(
                source,
                "OBLIVION-TABLE-SOURCE-EXTENSION-UNSUPPORTED",
                $"Table source '{source.Reference}' must end in '.obj.ts' or '.tson'.");
        }

        string root = Path.GetFullPath(workspaceRoot);
        string path = Path.GetFullPath(Path.Combine(root, source.Reference));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return Failure(
                source,
                "OBLIVION-TABLE-SOURCE-NOT-FOUND",
                $"TSON table source '{source.Reference}' was not found inside the workspace.",
                profileName);
        }

        string text = File.ReadAllText(path);
        long started = Environment.TickCount64;
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(text, profile);
        long elapsed = Environment.TickCount64 - started;
        List<OblivionCardDiagnostic> diagnostics = [];
        diagnostics.AddRange(read.SyntaxDiagnostics.Select(diagnostic => new OblivionCardDiagnostic(
            diagnostic.Id,
            OblivionDiagnosticSeverity.Error,
            diagnostic.Message,
            source.Reference)));
        diagnostics.AddRange(read.Diagnostics.Select(diagnostic => new OblivionCardDiagnostic(
            diagnostic.Code,
            OblivionDiagnosticSeverity.Error,
            diagnostic.Message,
            source.Reference)));
        if (!read.Success || read.Document is null)
        {
            return new OblivionTableCardRealization(
                false,
                source,
                profileName,
                null,
                ComputeHash(text),
                elapsed,
                diagnostics);
        }

        if (read.Document.Root is not TsonTable table)
        {
            diagnostics.Add(new OblivionCardDiagnostic(
                "OBLIVION-TABLE-ROOT-NOT-TABLE",
                OblivionDiagnosticSeverity.Error,
                $"TSON document root is '{read.Document.Root.GetType().Name}', not TsonTable.",
                source.Reference));
            return new OblivionTableCardRealization(
                false,
                source,
                profileName,
                null,
                ComputeHash(text),
                elapsed,
                diagnostics);
        }

        return new OblivionTableCardRealization(
            true,
            source,
            profileName,
            table,
            ComputeHash(text),
            elapsed,
            diagnostics);
    }

    private static bool TryResolveProfile(
        string reference,
        out TsonDocumentProfile profile,
        out string? profileName)
    {
        if (reference.EndsWith(".obj.ts", StringComparison.OrdinalIgnoreCase))
        {
            profile = TsonDocumentProfile.ObjectTypeScript;
            profileName = "obj.ts";
            return true;
        }

        if (reference.EndsWith(".tson", StringComparison.OrdinalIgnoreCase))
        {
            profile = TsonDocumentProfile.CanonicalTson;
            profileName = "tson";
            return true;
        }

        profile = default;
        profileName = null;
        return false;
    }

    private static OblivionTableCardRealization Failure(
        OblivionTableSource source,
        string code,
        string message,
        string? profile = null)
    {
        return new OblivionTableCardRealization(
            false,
            source,
            profile,
            null,
            null,
            0,
            [new OblivionCardDiagnostic(code, OblivionDiagnosticSeverity.Error, message, source.Reference)]);
    }

    private static string ComputeHash(string text)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }
}
