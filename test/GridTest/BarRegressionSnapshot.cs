using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;
using SteelGrid.Core.Report;

namespace GridTest
{
    /// <summary>同一个探针分别加载基线及当前 Core，逐值比较排条和孔位；不使用新包边类型。</summary>
    internal static class BarRegressionSnapshot
    {
        internal static string Capture(LayoutResult result)
        {
            var text = new StringBuilder();
            Append(text, "bars", result);
            return text.ToString();
        }

        internal static int Run(string path)
        {
            var text = new StringBuilder();
            foreach (var sample in FrameSamples.All())
            {
                foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                {
                    var points = sample.Value;
                    var polygon = new Polygon(points);
                    var shape = IdentityShape(points);
                    var spec = new Spec { OpeningW = polygon.Bounds.W, OpeningH = polygon.Bounds.H,
                        Shrink = 0, FrameT = 5, LoadDirection = direction };
                    Append(text, sample.Key + "/outline/" + direction, OutlineLayoutEngine.Layout(shape, spec).Bars);
                    Append(text, sample.Key + "/offset/" + direction,
                        OutlineLayoutEngine.LayoutFromOffsetContours(shape, spec, polygon, polygon.Offset(5),
                            new bool[polygon.Count], new CurvedFramePiece[0]).Bars);
                    Append(text, sample.Key + "/noborder/" + direction,
                        DirectOutlineLayoutEngine.Layout(points, new double[points.Count], true, spec).Bars);
                }
            }
            // 真正旋转的旧分类路径，检查传递世界坐标没有影响排条坐标系。
            var trapezoid = FrameSamples.All()["梯形"];
            var rotated = new List<Point2D>();
            foreach (var p in trapezoid)
                rotated.Add(new Point2D(p.X * Math.Cos(.42) - p.Y * Math.Sin(.42) + 300,
                    p.X * Math.Sin(.42) + p.Y * Math.Cos(.42) - 900));
            var parsed = OutlineParser.Parse(rotated, null, true);
            if (!parsed.Supported) throw new Exception("旋转梯形探针无法解析：" + parsed.Message);
            foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                Append(text, "rotated/" + direction, OutlineLayoutEngine.Layout(parsed.Shape,
                    new Spec { LoadDirection = direction }).Bars);

            foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
            {
                var result = LayoutEngine.Layout(new Spec { OpeningW = 1000, OpeningH = 800,
                    LoadDirection = direction });
                Append(text, "rectangle/" + direction, result);
                foreach (var frame in FrameSplitter.GetPieces(result.Geometry))
                    text.AppendLine("old-frame " + frame.Direction + " " + F(frame.Rect.X0) + " " +
                        F(frame.Rect.Y0) + " " + F(frame.Rect.X1) + " " + F(frame.Rect.Y1));
            }

            var arcSamples = new[]
            {
                FrameSamples.Points(0,0, 1000,0, 1000,800, 700,800, 300,800, 0,800),
                FrameSamples.Points(0,0, 1000,0, 1000,600, 800,800, 0,800),
                FrameSamples.Points(1000,0, 0,1000, 0,800, 800,0)
            };
            var quarterBulge = Math.Tan(Math.PI / 8);
            var arcBulges = new[]
            {
                new[] { 0.0, 0, 0, -1, 0, 0 },
                new[] { 0.0, 0, -quarterBulge, 0, 0 },
                new[] { quarterBulge, 0, -quarterBulge, 0 }
            };
            for (var i = 0; i < arcSamples.Length; i++)
            {
                var arcShape = OutlineParser.Parse(arcSamples[i], arcBulges[i], true);
                if (!arcShape.Supported) throw new Exception("弧形探针无法解析：" + arcShape.Message);
                foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                {
                    var arcLayout = OutlineLayoutEngine.Layout(arcShape.Shape, new Spec { LoadDirection = direction });
                    Append(text, "arc/" + i + "/" + direction, arcLayout.Bars);
                    foreach (var piece in arcLayout.FramePieces)
                    {
                        text.Append("arc-straight ").Append(piece.EdgeIndex);
                        foreach (var corner in piece.Corners()) text.Append(' ').Append(F(corner.X)).Append(',').Append(F(corner.Y));
                        text.AppendLine();
                    }
                    foreach (var piece in arcLayout.CurvedFramePieces)
                        foreach (var arc in new[] { piece.PlateArc, piece.NetArc, piece.OutlinePlateArc, piece.OutlineNetArc })
                            text.AppendLine("arc-curve " + F(arc.Center.X) + " " + F(arc.Center.Y) + " " + F(arc.Radius) + " " + F(arc.StartAngle) + " " + F(arc.SweepAngle));
                }
            }

            // 也检查带弧标记的直料分支，不把离散圆弧送入新的纯直线规划器。
            var arcPlate = new Polygon(FrameSamples.Points(0,0, 1000,0, 900,700, 100,700));
            var arcMask = new[] { false, false, true, false };
            foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                foreach (var cover in new[] { false, true })
                    foreach (var piece in PolygonFrameSplitter.GetPieces(arcPlate, arcPlate.Offset(5), 5, direction, arcMask, cover))
                    {
                        text.Append("arc-branch ").Append(direction).Append(' ').Append(cover).Append(' ').Append(piece.EdgeIndex);
                        foreach (var corner in piece.Corners()) text.Append(' ').Append(F(corner.X)).Append(',').Append(F(corner.Y));
                        text.AppendLine();
                    }

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Bar snapshot: " + Path.GetFullPath(path));
            return 0;
        }

        private static OutlineShape IdentityShape(List<Point2D> points)
        {
            return new OutlineShape(OutlineKind.Trapezoid, new Polygon(points), points,
                new List<OutlineEdge>(), new List<Polygon>(), new Point2D(0,0), new Point2D(1,0), new Point2D(0,1));
        }

        private static void Append(StringBuilder text, string name, LayoutResult layout)
        {
            text.AppendLine("CASE " + name);
            foreach (var bars in new[] { layout.VerticalBars, layout.HorizontalBars })
                foreach (var bar in bars)
                {
                    text.AppendLine("BAR " + bar.Orientation + " " + bar.Index + " " + F(bar.Center) + " " + F(bar.Thickness) + " " + bar.Full);
                    foreach (var segment in bar.Segments) text.AppendLine("SEG " + F(segment.A) + " " + F(segment.B));
                    foreach (var holes in bar.HoleGroups)
                    {
                        text.Append("HOLES");
                        foreach (var hole in holes) text.Append(' ').Append(F(hole));
                        text.AppendLine();
                    }
                    foreach (var cut in bar.BandCuts)
                        text.AppendLine("CUT " + F(cut.A) + " " + F(cut.B) + " " + F(cut.RawStart) + " " + F(cut.RawEnd) +
                            " " + F(cut.Rect.X0) + " " + F(cut.Rect.Y0) + " " + F(cut.Rect.X1) + " " + F(cut.Rect.Y1));
                    foreach (var warning in bar.Warnings) text.AppendLine("WARN " + warning);
                }
            foreach (var direction in new[] { "纵向", "横向" })
                foreach (var row in ReportTables.ReportTable(layout, direction))
                    text.AppendLine("ROW " + direction + " " + row.Spec + " " + F(row.Length) + " " + row.Count +
                        " " + row.FirstHole + " " + row.LastHole + " " + row.Holes);
        }

        private static string F(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    }
}
