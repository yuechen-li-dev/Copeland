using System.Text.Json;
using Copeland.Profile;
using Copeland.TS.Profiles;

string root = Directory.GetCurrentDirectory();
string artifactDirectory = Path.Combine(root, "artifacts", "copeland-profile-selectors-repeat-m6");
Directory.CreateDirectory(artifactDirectory);

ProfileCompilationResult badge = Compile(BadgeSource(), "TabbedBadgeM6.profile.tsx");
ProfileCompilationResult curve = Compile(CurveSource(), "CurvedScallops.profile.tsx");
ProfileCompilationResult shifted = Compile(TopologyShiftSource(), "StableTopology.profile.tsx");
File.WriteAllText(Path.Combine(artifactDirectory, "tabbed-badge-repeat-linear.svg"), badge.Svg!);
File.WriteAllText(Path.Combine(artifactDirectory, "curved-repeat-along-path.svg"), curve.Svg!);
File.WriteAllText(Path.Combine(artifactDirectory, "stable-topology-shift.svg"), shifted.Svg!);

string pelicanPath = Path.Combine(root, "samples", "copeland-ts", "profile-pelican-bicycle", "pelican-bicycle.profile.tsx");
ProfileCompositionCompilationResult pelican = ProfileTsxCompiler.CompileComposition(File.ReadAllText(pelicanPath), pelicanPath);
if (!pelican.Success)
{
    throw new InvalidOperationException(Diagnostics(pelican.Diagnostics));
}

ProfileLoweredReplacementSummary[] linearLowering = badge.States[^1].LoweredReplacements.ToArray();
ProfileLoweredReplacementSummary[] pathLowering = curve.States[^1].LoweredReplacements.ToArray();
var proof = new
{
    stableTopology = new
    {
        targetFeature = "RightTarget",
        namedSpan = "RightEdge",
        beforeRawSegmentIndex = 1,
        afterRawSegmentIndex = shifted.States[2].Segments
            .Select((segment, index) => (segment, index))
            .Single(item => item.segment.SemanticTags.Contains("name:RightEdge", StringComparer.Ordinal))
            .index,
        loweredReplacementIndexAfterArcRefinement = shifted.States[^1].LoweredReplacements.Single().TargetSegmentIndex,
        resolution = "feature and name semantic tags are resolved on each current SSA state",
        shifted.CanonicalContourHash,
    },
    repeatLinear = new
    {
        count = linearLowering.Length,
        spacing = 14.0,
        footprint = 6.0,
        lowered = linearLowering,
    },
    repeatAlongPath = new
    {
        count = pathLowering.Length,
        spacing = 3.4,
        footprint = 1.4,
        parameterization = "deterministic-arc-length",
        orientation = "local-target-tangent-and-canonical-outward-normal",
        lowered = pathLowering,
    },
    geometryHashIgnoresSelectorResolutionState = badge.States[0].ContourHash == badge.States[1].ContourHash,
    pelican = new
    {
        changed = false,
        reason = "No forced repetition: the existing semantic illustration remains the stronger benchmark.",
        pelican.CompositionHash,
        pelican.CanonicalGeometryHash,
    },
};
File.WriteAllText(
    Path.Combine(artifactDirectory, "proof.json"),
    JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));

var manifest = new
{
    milestone = "COPELAND-PROFILE-SELECTORS-REPEAT-M6",
    kind = "stable-semantic-profile-selectors-linear-path-repeat",
    semanticSelectorsAdded = true,
    selectorIsOwnerIndependent = true,
    resolvedSpanIsOwnerBound = true,
    rawIndexSelectorStillEscapeHatch = true,
    repeatLinearQualified = true,
    repeatAlongPathQualified = true,
    arcLengthPlacementUsed = true,
    sequentialSsaUsed = true,
    stableFeatureIdentityUsed = true,
    runtimeQuerySystemAdded = false,
    cadConstraintSolverAdded = false,
    rendererChanged = false,
    svgProfileArcParkedAfterMilestone = true,
    badge.ProfileIrHash,
    badge.CanonicalContourHash,
    curvedProfileIrHash = curve.ProfileIrHash,
    curvedContourHash = curve.CanonicalContourHash,
    pelicanCompositionHash = pelican.CompositionHash,
};
string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(artifactDirectory, "manifest.json"), manifestJson);
Console.WriteLine(manifestJson);

static ProfileCompilationResult Compile(string source, string path)
{
    ProfileCompilationResult first = ProfileTsxCompiler.Compile(source, path);
    ProfileCompilationResult second = ProfileTsxCompiler.Compile(source, path);
    if (!first.Success)
    {
        throw new InvalidOperationException(Diagnostics(first.Diagnostics));
    }
    if (first.ProfileIrHash != second.ProfileIrHash
        || first.CanonicalContourHash != second.CanonicalContourHash
        || first.Svg != second.Svg)
    {
        throw new InvalidOperationException($"{path} was not deterministic.");
    }
    return first;
}

static string Diagnostics(IReadOnlyList<ProfileDiagnostic> diagnostics)
    => string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Id}: {item.Message}"));

static string BadgeSource() => """
    const NotchPattern: ProfileSpanPattern = SpanPattern(SpanOf([
        LineSegment(Point(0.0, 0.0), Point(0.5, -2.0)),
        LineSegment(Point(0.5, -2.0), Point(1.0, 0.0))
    ]));
    export default (
        <Profile name="TabbedBadgeM6" base={RoundedRectangle({ width: 100.0, height: 56.0, radius: 8.0 })}>
            {NameSpan({ id: "TopTarget", as: "Named", name: "TopEdge", target: SpanOf([SelectSegment("Base", 0)]) })}
            {RepeatLinear({ id: "TopNotches", as: "Repeated", target: AlongSpan(NamedSpan("TopEdge"), 0.1, 0.9), pattern: NotchPattern, count: 4, spacing: 14.0, footprint: 6.0, offset: 1.0 })}
            {Yield(Repeated)}
        </Profile>
    );
    """;

static string CurveSource() => """
    const Scallop: ProfileSpanPattern = SpanPattern(SpanOf([
        LineSegment(Point(0.0, 0.0), Point(0.5, -0.5)),
        LineSegment(Point(0.5, -0.5), Point(1.0, 0.0))
    ]));
    const Guide: ConceptPath = CurvedPath(
        Point(30.0, 20.0), Point(40.0, 10.0),
        Spline({ control1: Point(35.52284749830794, 20.0), control2: Point(40.0, 15.522847498307936) })
    );
    export default (
        <Profile name="CurvedScallops" base={RoundedRectangle({ width: 80.0, height: 40.0, radius: 10.0 })}>
            {NameSpan({ id: "CurveTarget", as: "NamedCurve", name: "TopRightCurve", target: SpanOf([SelectSegment("Base", 1)]) })}
            {RepeatAlongPath({ id: "Scallops", as: "Repeated", target: FeatureSpan("CurveTarget"), path: Guide, pattern: Scallop, count: 4, spacing: 3.4, footprint: 1.4, offset: 0.4 })}
            {Yield(Repeated)}
        </Profile>
    );
    """;

static string TopologyShiftSource() => """
    const TabPattern: ProfileSpanPattern = SpanPattern(SpanOf([
        LineSegment(Point(0.0, 0.0), Point(0.3, 3.0)),
        LineSegment(Point(0.3, 3.0), Point(0.7, 3.0)),
        LineSegment(Point(0.7, 3.0), Point(1.0, 0.0))
    ]));
    const NotchPattern: ProfileSpanPattern = SpanPattern(SpanOf([
        LineSegment(Point(0.0, 0.0), Point(0.5, -2.0)),
        LineSegment(Point(0.5, -2.0), Point(1.0, 0.0))
    ]));
    export default (
        <Profile name="StableTopology" base={Rectangle({ width: 80.0, height: 40.0 })}>
            {NameSpan({ id: "RightTarget", as: "Named", name: "RightEdge", target: SpanOf([SelectSegment("Base", 1)]) })}
            {ReplaceSpanWithPattern({ id: "EarlierTab", as: "Shifted", target: SpanOf([SelectSegment("Named", 0)]), pattern: TabPattern })}
            {RepeatLinear({ id: "RightNotch", as: "Done", target: NamedSpan("RightEdge"), pattern: NotchPattern, count: 1, spacing: 4.0, footprint: 4.0, offset: 4.0 })}
            {Yield(Done)}
        </Profile>
    );
    """;
