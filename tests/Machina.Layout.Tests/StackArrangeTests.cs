using Machina.Layout.Compilation;
using Machina.Layout.Diagnostics;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Xunit;

namespace Machina.Layout.Tests;

public sealed class StackArrangeTests
{
    [Fact]
    public void HorizontalFixedStart()
    {
        var r = Resolve(new[] { Row("root", new RootFrame(), arrange: new StackArrange(StackAxis.Horizontal, 10)), Row("a", new FixedFrame(100, 20), "root"), Row("b", new FixedFrame(50, 30), "root") });
        AssertRect(r.Nodes["a"].Rect, 0, 0, 100, 20);
        AssertRect(r.Nodes["b"].Rect, 110, 0, 50, 30);
    }

    [Fact] public void HorizontalFill(){ var r=Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Horizontal,10)), Row("a",new FixedFrame(100,20),"root"), Row("fill",new FillFrame(1),"root"), Row("b",new FixedFrame(50,20),"root")}); AssertRect(r.Nodes["fill"].Rect,110,0,130,100);}    
    [Fact] public void VerticalFixed(){ var r=Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Vertical,10)), Row("a",new FixedFrame(50,100),"root"), Row("b",new FixedFrame(30,50),"root")}, new Rect(0,0,200,300)); AssertRect(r.Nodes["b"].Rect,0,110,30,50);}    
    [Fact] public void DirectFixedRejected(){ AssertLayoutError("FixedFrameWithoutArranger",()=>FrameResolver.ResolveFrame(new Rect(0,0,10,10),new FixedFrame(1,1))); }
    [Fact] public void DirectFillRejected(){ AssertLayoutError("FillFrameWithoutArranger",()=>FrameResolver.ResolveFrame(new Rect(0,0,10,10),new FillFrame())); }
    [Fact] public void InvalidStackChild(){ AssertLayoutError("InvalidStackChildFrame",()=>Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Horizontal)), Row("a",new AbsoluteFrame(0,0,10,10),"root") })); }
    [Fact] public void InvalidWeight(){ AssertLayoutError("InvalidFillWeight",()=>Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Horizontal)), Row("a",new FillFrame(0),"root") })); }
    [Fact] public void InvalidFixedSize(){ AssertLayoutError("InvalidFixedFrameSize",()=>Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Horizontal)), Row("a",new FixedFrame(-1,10),"root") })); }
    [Fact] public void NegativeContent(){ AssertLayoutError("NegativeStackContentSize",()=>Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Horizontal,Padding:new EdgeInsets(0,200,0,200))), Row("a",new FixedFrame(1,1),"root") }, new Rect(0,0,300,100))); }
    [Fact] public void NegativeRemaining(){ AssertLayoutError("NegativeStackRemainingSpace",()=>Resolve(new[] { Row("root",new RootFrame(),arrange:new StackArrange(StackAxis.Horizontal,10)), Row("a",new FixedFrame(200,10),"root"), Row("b",new FixedFrame(200,10),"root") }, new Rect(0,0,300,100))); }

    private static Documents.ResolvedLayoutDocument Resolve(LayoutRow[] rows, Rect? root = null) => LayoutDocumentResolver.ResolveLayoutDocument(LayoutCompiler.CompileLayoutRows(rows), root ?? new Rect(0, 0, 300, 100));
    private static LayoutRow Row(string id, FrameSpec frame, string? parent = null, int order = 0, ArrangeSpec? arrange = null) => new(id, frame, parent is null ? (NodeId?)null : new NodeId(parent), order, 0, null, null, null, null, arrange);
    private static void AssertRect(Rect actual, double x, double y, double w, double h){ Assert.Equal(x, actual.X); Assert.Equal(y, actual.Y); Assert.Equal(w, actual.Width); Assert.Equal(h, actual.Height);}    
    private static LayoutError AssertLayoutError(string expectedCode, Action action){ var e = Assert.Throws<LayoutError>(action); Assert.Equal(expectedCode, e.Code); return e; }
}
