using Copeland.Profile;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;

namespace Copeland.TS.Profiles;

public static partial class ProfileTsxCompiler
{
    public static ProfileCompositionCompilationResult CompileComposition(
        string source,
        string sourcePath = "Asset.profile.tsx")
    {
        ArgumentNullException.ThrowIfNull(source);
        SyntaxTree tree = SyntaxTree.Parse(source, sourcePath);
        List<ProfileDiagnostic> diagnostics = tree.Diagnostics
            .Select(item => new ProfileDiagnostic(
                item.Id,
                item.Message,
                new ProfileSourceSpan(sourcePath, item.Position, item.Length)))
            .ToList();
        if (diagnostics.Count > 0)
        {
            return FailedComposition(diagnostics);
        }

        ExportDefaultDeclarationSyntax[] exports = tree.Root.Members
            .OfType<ExportDefaultDeclarationSyntax>()
            .ToArray();
        if (exports.Length != 1)
        {
            diagnostics.Add(Diagnostic(
                "COPE-PROFILE-COMPOSE-0001",
                "Profile composition requires exactly one export default Layers(...) value.",
                tree.Root,
                sourcePath));
            return FailedComposition(diagnostics);
        }

        ExpressionSyntax expression = Unwrap(exports[0].Expression);
        const string probeName = "__cope_profile_composition";
        ProfileExpressionEvaluation evaluation = ProfileExpressionEvaluator.Evaluate(
            source,
            sourcePath,
            exports[0],
            [(probeName, expression)],
            null,
            null);
        diagnostics.AddRange(evaluation.Diagnostics);
        if (!evaluation.Values.TryGetValue(probeName, out StaticValue? value))
        {
            return FailedComposition(diagnostics);
        }
        if (value is not StaticRecordValue compositionValue
            || compositionValue.Type.Name != "ProfileComposition")
        {
            diagnostics.Add(Diagnostic(
                "COPE-PROFILE-COMPOSE-0002",
                $"Default export has type '{value.Type.Name}'; expected ProfileComposition from Layers(...).",
                expression,
                sourcePath));
            return FailedComposition(diagnostics);
        }

        ProfileComposition? composition = DecodeComposition(
            compositionValue,
            Span(expression, sourcePath),
            diagnostics);
        if (composition is null || diagnostics.Count > 0)
        {
            return FailedComposition(diagnostics);
        }

        string svg = ProfileSvgExporter.ExportComposition(composition);
        return new ProfileCompositionCompilationResult(
            composition,
            diagnostics,
            composition.SemanticHash,
            composition.CanonicalGeometryHash,
            svg);
    }

    private static ProfileComposition? DecodeComposition(
        StaticRecordValue compositionValue,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        if (Field(compositionValue, "layers") is not StaticArrayValue layerValues)
        {
            diagnostics.Add(new ProfileDiagnostic(
                "COPE-PROFILE-COMPOSE-0003",
                "ProfileComposition.layers must be a ProfileLayer array.",
                span));
            return null;
        }

        var layers = new List<ProfileLayer>();
        var layerNames = new HashSet<string>(StringComparer.Ordinal);
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (StaticValue layerValue in layerValues.Elements)
        {
            if (layerValue is not StaticRecordValue layerRecord
                || layerRecord.Type.Name != "ProfileLayer")
            {
                diagnostics.Add(new ProfileDiagnostic(
                    "COPE-PROFILE-COMPOSE-0003",
                    "Composition values must be typed ProfileLayer values.",
                    span));
                continue;
            }

            StaticRecordValue idRecord = (StaticRecordValue)Field(layerRecord, "id");
            string layerName = Text(idRecord, "name");
            if (string.IsNullOrWhiteSpace(layerName))
            {
                diagnostics.Add(new ProfileDiagnostic(
                    "COPE-PROFILE-COMPOSE-0004",
                    "Layer identity must be a nonempty static name.",
                    span));
                continue;
            }
            if (!layerNames.Add(layerName))
            {
                diagnostics.Add(new ProfileDiagnostic(
                    "COPE-PROFILE-COMPOSE-0005",
                    $"Duplicate Profile layer identity '{layerName}'.",
                    span));
                continue;
            }

            StaticArrayValue profiles = (StaticArrayValue)Field(layerRecord, "profiles");
            var items = new List<ResolvedProfilePaintItem>();
            foreach (StaticValue profileValue in profiles.Elements)
            {
                if (profileValue is not StaticRecordValue profileRecord
                    || profileRecord.Type.Name != "ProfileSource")
                {
                    diagnostics.Add(new ProfileDiagnostic(
                        "COPE-PROFILE-COMPOSE-0006",
                        "Layer content must be typed Profile values.",
                        span));
                    continue;
                }

                ResolvedProfilePaintItem? item = DecodeComposedProfile(profileRecord, span, diagnostics);
                if (item is null)
                {
                    continue;
                }
                if (!profileIds.Add(item.Id))
                {
                    diagnostics.Add(new ProfileDiagnostic(
                        "COPE-PROFILE-COMPOSE-0007",
                        $"Duplicate composed Profile identity '{item.Id}'.",
                        span));
                    continue;
                }
                items.Add(item);
            }

            // Empty layers are legal authoring conveniences and erase here.
            if (items.Count > 0)
            {
                layers.Add(new ProfileLayer(new ProfileLayerId(layerName), items));
            }
        }

        if (layers.Count == 0)
        {
            diagnostics.Add(new ProfileDiagnostic(
                "COPE-PROFILE-COMPOSE-0008",
                "Profile composition did not resolve any Profile content.",
                span));
            return null;
        }
        return diagnostics.Count == 0 ? new ProfileComposition(layers) : null;
    }

    private static ResolvedProfilePaintItem? DecodeComposedProfile(
        StaticRecordValue profile,
        ProfileSourceSpan span,
        List<ProfileDiagnostic> diagnostics)
    {
        string name = Text(profile, "name");
        string baseState = OptionalText(profile, "baseState") ?? "Base";
        string yieldState = Text(profile, "yieldState");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(yieldState))
        {
            diagnostics.Add(new ProfileDiagnostic(
                "COPE-PROFILE-COMPOSE-0009",
                "Composed Profile name and yieldState must be nonempty static text.",
                span));
            return null;
        }

        ProfileShapeSpec? baseShape = DecodeShape(Field(profile, "shape"), span, diagnostics);
        StaticValue operationsValue = Field(profile, "operations");
        IReadOnlyList<ProfileOperation> operations = DecodeProfileBodyValue(
            operationsValue,
            baseState,
            span,
            diagnostics);
        ProfileStyle style = ProfileStyle.Default;
        StaticValue? styleValue = UnwrapOptional(profile, "style");
        if (styleValue is StaticRecordValue styleRecord && styleRecord.Type.Name == "ProfileStyle")
        {
            style = new ProfileStyle(Text(styleRecord, "fill"));
            if (!style.IsValid)
            {
                diagnostics.Add(new ProfileDiagnostic(
                    "COPE-PROFILE-TSX-0051",
                    "ProfileStyle.fill requires black, white, or #RRGGBB.",
                    span));
            }
        }

        if (baseShape is null || diagnostics.Count > 0)
        {
            return null;
        }
        var definition = new ProfileDefinition(
            name,
            baseState,
            baseShape,
            operations,
            yieldState,
            span);
        ProfileCompilationResult result = ProfileCompiler.Compile(definition);
        diagnostics.AddRange(result.Diagnostics);
        if (!result.Success)
        {
            return null;
        }
        return new ResolvedProfilePaintItem(
            name,
            result.Shape!,
            style,
            result.ProfileIrHash!,
            result.CanonicalContourHash!);
    }

    private static StaticValue? UnwrapOptional(StaticRecordValue record, string name)
    {
        StaticValue? value = OptionalField(record, name);
        return value switch
        {
            StaticEnumValue { Type: OptionTypeSymbol, Case.Name: "None" } => null,
            StaticEnumValue { Type: OptionTypeSymbol, Case.Name: "Some", Payloads.Count: 1 } some => some.Payloads[0],
            _ => value,
        };
    }

    private static string? OptionalText(StaticRecordValue record, string name)
    {
        StaticValue? value = UnwrapOptional(record, name);
        return value is StaticPrimitiveValue primitive
            ? Convert.ToString(primitive.Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static ProfileCompositionCompilationResult FailedComposition(
        IReadOnlyList<ProfileDiagnostic> diagnostics)
        => new(null, diagnostics, null, null, null);
}
