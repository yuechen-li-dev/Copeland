using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Runtime.Input;

namespace Machina.Testing;

public enum HitPointKind { Center, LeftCenter, RightCenter }

public sealed record DocumentGeometryResult(UiLoweringResult Lowering, LayoutDocument Document, ResolvedLayoutDocument Resolved, UiHitTestIndex HitTest)
{
    public bool HasRow(string id) => Resolved.Nodes.ContainsKey(new NodeId(id));
    public Rect RectOf(string id) => Resolved.Nodes.TryGetValue(new NodeId(id), out var n) ? n.Rect : throw new InvalidOperationException($"Missing row '{id}'.");
    public UiStyle StyleOf(string id) => Lowering.Styles[new NodeId(id)];
    public TextStyle TextStyleOf(string id) => Lowering.TextStyles[new NodeId(id)];
    public UiSemantics SemanticsOf(string id) => Lowering.Semantics[new NodeId(id)];
    public UiAction? ActionOf(string id) => Lowering.Actions.TryGetValue(new NodeId(id), out var a) ? a : null;
    public void AssertContainsRows(params string[] ids) { foreach (var id in ids) Ensure(HasRow(id), $"Expected row '{id}'."); }
    public void AssertRect(string id, double x, double y, double w, double h) => EnsureEqual(new Rect(x, y, w, h), RectOf(id), $"Rect mismatch for {id}");
    public void AssertHitActionInside(string id, string expectedActionName, HitPointKind pointKind = HitPointKind.Center)
    {
        var r = RectOf(id);
        var p = pointKind == HitPointKind.LeftCenter ? new PointerPoint((float)(r.X + 1), (float)(r.Y + r.Height / 2))
            : pointKind == HitPointKind.RightCenter ? new PointerPoint((float)(r.X + r.Width - 1), (float)(r.Y + r.Height / 2))
            : new PointerPoint((float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2));
        var hit = HitTest.HitTest(p);
        Ensure(hit is not null, $"Expected hit inside {id}");
        EnsureEqual(expectedActionName, hit!.Action.Name, $"Unexpected action for {id}");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void EnsureEqual<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(message); }
}

public static class GeometryHarness
{
    public static DocumentGeometryResult ResolveComponent(UiNode component, double width = 300, double height = 120, string hostId = "host")
        => ResolveDocument(UiDocument.Create([Row.Root("root"), Row.Anchor(hostId, "root", left: 0, top: 0, width: width, height: height, component: component)]), width, height);

    public static DocumentGeometryResult ResolveDocument(UiDocument document, double width, double height)
    {
        var lowering = UiDocumentLowerer.Lower(document);
        var doc = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(doc, new Rect(0, 0, width, height));
        return new DocumentGeometryResult(lowering, doc, resolved, UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics));
    }

    public static void AssertSameRowIds(DocumentGeometryResult a, DocumentGeometryResult b)
        => EnsureEqual(string.Join("|", a.Lowering.Rows.Select(x => x.Id.Value).OrderBy(x => x)), string.Join("|", b.Lowering.Rows.Select(x => x.Id.Value).OrderBy(x => x)), "Row ids differ");
    public static void AssertSameRectBetween(DocumentGeometryResult a, DocumentGeometryResult b, string id) => EnsureEqual(a.RectOf(id), b.RectOf(id), $"Rect mismatch for {id}");
    public static void AssertOnlyXDiffers(DocumentGeometryResult a, DocumentGeometryResult b, string id)
    {
        var ra = a.RectOf(id); var rb = b.RectOf(id);
        Ensure(ra.X != rb.X, $"Expected X difference for {id}");
        EnsureEqual(ra.Y, rb.Y, $"Y mismatch for {id}");
        EnsureEqual(ra.Width, rb.Width, $"Width mismatch for {id}");
        EnsureEqual(ra.Height, rb.Height, $"Height mismatch for {id}");
    }
    private static void Ensure(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void EnsureEqual<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(message); }
}
