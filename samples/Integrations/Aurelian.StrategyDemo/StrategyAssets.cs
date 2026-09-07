using Copeland.Profile;
using Copeland.TS.Profiles;
using SkiaSharp;

namespace Aurelian.StrategyDemo;

/// <summary>Leaf realization of canonical Profile contours. Source authoring and SVG export stay in Copeland.</summary>
public sealed class StrategyAssets : IDisposable
{
    private readonly Dictionary<string, (SKPath Path, SKPaint Paint)[]> assets = [];
    public Dictionary<string, string> Hashes { get; } = [];

    public StrategyAssets(string directory)
    {
        string toolkit = File.ReadAllText(Path.Combine(directory, "StrategyArt.ts"));
        foreach (string file in Directory.GetFiles(directory, "*.profile.tsx").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file).Replace(".profile.tsx", "", StringComparison.Ordinal);
            ProfileCompositionCompilationResult result = ProfileTsxCompiler.CompileComposition(toolkit + "\n" + File.ReadAllText(file), file);
            if (!result.Success)
            {
                throw new InvalidDataException(name + ": " + string.Join("; ", result.Diagnostics.Select(d => d.Id + " " + d.Message)));
            }
            Hashes.Add(name, result.CompositionHash!);
            assets.Add(name, result.Composition!.Layers.SelectMany(layer => layer.Items).Select(Compile).ToArray());
        }
    }

    public void Draw(SKCanvas canvas, string asset, float x, float y, float scale = 1)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale, -scale);
        foreach ((SKPath path, SKPaint paint) in assets[asset])
        {
            canvas.DrawPath(path, paint);
        }
        canvas.Restore();
    }

    public void Dispose()
    {
        foreach ((SKPath path, SKPaint paint) in assets.Values.SelectMany(items => items))
        {
            path.Dispose();
            paint.Dispose();
        }
    }

    private static (SKPath, SKPaint) Compile(ResolvedProfilePaintItem item)
    {
        var path = new SKPath { FillType = SKPathFillType.Winding };
        foreach (VectorContour contour in item.Shape.Contours)
        {
            bool first = true;
            foreach (VectorSegment segment in contour.Segments)
            {
                VectorPoint start = segment switch
                {
                    VectorLine line => line.P0,
                    VectorQuadratic quadratic => quadratic.P0,
                    VectorCubic cubic => cubic.P0,
                    _ => throw new InvalidDataException("Unknown Profile segment."),
                };
                if (first)
                {
                    path.MoveTo((float)start.X, (float)start.Y);
                    first = false;
                }
                switch (segment)
                {
                    case VectorLine line:
                        path.LineTo((float)line.P1.X, (float)line.P1.Y);
                        break;
                    case VectorQuadratic curve:
                        path.QuadTo((float)curve.P1.X, (float)curve.P1.Y, (float)curve.P2.X, (float)curve.P2.Y);
                        break;
                    case VectorCubic curve:
                        path.CubicTo((float)curve.P1.X, (float)curve.P1.Y, (float)curve.P2.X, (float)curve.P2.Y, (float)curve.P3.X, (float)curve.P3.Y);
                        break;
                }
            }
            path.Close();
        }
        return (path, new SKPaint { Color = SKColor.Parse(item.Style.Fill), IsAntialias = true });
    }
}
