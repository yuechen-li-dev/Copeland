using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Core.Nodes;

namespace Machina.Core.Flat;

public static class Row
{
    public static UiRow Root(NodeId id, UiView? view = null, UiNode? component = null, int order = 0)
    {
        return new UiRow(id, Parent: null, Frame: new RootFrame(), Arrange: null, Order: order, View: view, Component: component);
    }

    public static UiRow Absolute(
        NodeId id,
        NodeId parent,
        double x,
        double y,
        double width,
        double height,
        UiView? view = null,
        ArrangeSpec? arrange = null,
        int order = 0)
    {
        return new UiRow(id, parent, new AbsoluteFrame(x, y, width, height), arrange, order, view);
    }

    public static UiRow Anchor(
        NodeId id,
        NodeId parent,
        UiLength? left = null,
        UiLength? right = null,
        UiLength? top = null,
        UiLength? bottom = null,
        UiLength? width = null,
        UiLength? height = null,
        UiView? view = null,
        UiNode? component = null,
        ArrangeSpec? arrange = null,
        int order = 0)
    {
        return new UiRow(id, parent, new AnchorFrame(left, right, top, bottom, width, height), arrange, order, view, component);
    }

    public static UiRow Fixed(
        NodeId id,
        NodeId parent,
        double? width = null,
        double? height = null,
        UiView? view = null,
        int order = 0)
    {
        return new UiRow(id, parent, new FixedFrame(width ?? 0, height ?? 0), Arrange: null, Order: order, View: view);
    }

    public static UiRow Fill(
        NodeId id,
        NodeId parent,
        double weight = 1,
        double? cross = null,
        UiView? view = null,
        int order = 0)
    {
        return new UiRow(id, parent, new FillFrame(weight, cross), Arrange: null, Order: order, View: view);
    }
}
