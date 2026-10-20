using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;

namespace GridTest
{
    internal static class ArcFrameEndTest
    {
        internal static int CaptureLayouts(string path, string output, bool includeStock = false)
        {
            var result = new StringBuilder();
            var index = 0;
            var successfulLayouts = 0;
            foreach (var outline in DxfReader.Read(path))
            {
                if (!outline.Closed) continue;
                var hasArc = false;
                foreach (var bulge in outline.Bulges) if (Math.Abs(bulge) > 1e-9) hasArc = true;
                if (!hasArc) continue;
                var parsed = OutlineParser.Parse(outline.Points, outline.Bulges, outline.Closed);
                if (!parsed.Supported)
                {
                    result.AppendLine("PARSE REJECT: " + parsed.Message);
                    continue;
                }
                foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                {
                    result.AppendLine("CASE " + index + " " + direction);
                    try
                    {
                        var layout = OutlineLayoutEngine.Layout(parsed.Shape, new Spec { LoadDirection = direction });
                        successfulLayouts++;
                        result.Append(BarRegressionSnapshot.Capture(layout.Bars));
                        if (includeStock)
                        {
                            foreach (var frame in layout.FramePieces)
                            {
                                result.Append("STRAIGHT ").Append(frame.EdgeIndex).Append(' ').Append(frame.Length.ToString("R",CultureInfo.InvariantCulture));
                                foreach (var p in frame.Corners()) result.Append(' ').Append(p.X.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(p.Y.ToString("R",CultureInfo.InvariantCulture));
                                result.AppendLine();
                            }
                            foreach (var frame in layout.CurvedFramePieces)
                                foreach (var arc in new[] { frame.PlateArc,frame.NetArc,frame.OutlinePlateArc,frame.OutlineNetArc })
                                    result.AppendLine("ARC " + arc.Center.X.ToString("R",CultureInfo.InvariantCulture) + " " +
                                        arc.Center.Y.ToString("R",CultureInfo.InvariantCulture) + " " + arc.Radius.ToString("R",CultureInfo.InvariantCulture) + " " +
                                        arc.StartAngle.ToString("R",CultureInfo.InvariantCulture) + " " + arc.SweepAngle.ToString("R",CultureInfo.InvariantCulture));
                        }
                    }
                    catch (ArgumentException e) { result.AppendLine("ROUTE TO GENERIC: " + e.Message); }
                }
                index++;
            }
            File.WriteAllText(output,result.ToString(),new UTF8Encoding(false));
            Console.WriteLine("Actual DXF legacy-layout snapshot: " + output + "; successful layouts=" + successfulLayouts);
            return 0;
        }

        internal static int Run(string path, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var tested = 0;
            var targets = 0;
            foreach (var outline in DxfReader.Read(path))
            {
                var arcIndex = -1; var arcCount = 0;
                for (var i = 0; i < outline.Bulges.Count; i++)
                    if (Math.Abs(outline.Bulges[i]) > 1e-9) { arcCount++; arcIndex = i; }
                if (arcCount != 1) continue;
                var parsed = OutlineParser.Parse(outline.Points, outline.Bulges, outline.Closed);
                if (!parsed.Supported) throw new Exception("实际单弧样例解析失败：" + parsed.Message);
                var shape = parsed.Shape;
                var source = shape.CurvedArcs[0];
                var plateArc = shape.OffsetCurvedNotchArc(5);
                var netArc = shape.OffsetCurvedNotchArc(10);
                var previous = shape.ToLocal(outline.Points[(arcIndex + outline.Points.Count - 1) % outline.Points.Count]);
                var next = shape.ToLocal(outline.Points[(arcIndex + 2) % outline.Points.Count]);
                var isTarget = Math.Abs(outline.Bulges[arcIndex] - 0.3647142888081311) < 1e-12;
                var sign = Math.Sign(plateArc.Radius - source.Radius);
                foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                {
                    var before = new CurvedFramePiece(plateArc, netArc, sign);
                    var after = ArcFrameEndAligner.AlignConvexTangentEnds(before, source, previous, next, direction);
                    Assert(ReferenceEquals(after.OutlinePlateArc, before.OutlinePlateArc) &&
                        ReferenceEquals(after.OutlineNetArc, before.OutlineNetArc), "真实内外弧必须保持原对象");
                    Assert(Near(after.Length, Math.Max(after.PlateArc.Length, after.NetArc.Length)), "弧料仍按较长弧边计料");
                    Assert(after.PlateArc.SweepAngle * source.SweepAngle > 0 &&
                        after.NetArc.SweepAngle * source.SweepAngle > 0, "弧带扫角方向不能翻转");
                    if (sign > 0) Assert(ReferenceEquals(before, after), "已有凹弧端部规则不改变");

                    var points = new List<Point2D>();
                    foreach (var p in outline.Points) points.Add(shape.ToLocal(p));
                    var straightPlate = new List<Point2D>(new Polygon(points).Offset(5).Vertices);
                    var straightNet = new List<Point2D>(new Polygon(points).Offset(10).Vertices);
                    straightPlate[arcIndex] = plateArc.Start;
                    straightPlate[(arcIndex + 1) % points.Count] = plateArc.End;
                    straightNet[arcIndex] = netArc.Start;
                    straightNet[(arcIndex + 1) % points.Count] = netArc.End;
                    var pSamples = new List<Point2D>(); var nSamples = new List<Point2D>(); var mask = new List<bool>();
                    var subdivisions = Math.Max(plateArc.Tessellate(.05).Count, netArc.Tessellate(.05).Count) - 1;
                    for (var edge = 0; edge < points.Count; edge++)
                    {
                        if (edge != arcIndex) { pSamples.Add(straightPlate[edge]); nSamples.Add(straightNet[edge]); mask.Add(false); }
                        else for (var j = 0; j < subdivisions; j++)
                        {
                            pSamples.Add(plateArc.PointAt((double)j / subdivisions));
                            nSamples.Add(netArc.PointAt((double)j / subdivisions)); mask.Add(true);
                        }
                    }
                    var plate = new Polygon(pSamples); var net = new Polygon(nSamples);
                    var spec = new Spec { Shrink = 5, FrameT = 5, LoadDirection = direction };
                    var oldLayout = OutlineLayoutEngine.LayoutFromOffsetContours(shape, spec, plate, net, mask, new[] { before });
                    var newLayout = OutlineLayoutEngine.LayoutFromOffsetContours(shape, spec, plate, net, mask, new[] { after });
                    Assert(BarRegressionSnapshot.Capture(oldLayout.Bars) == BarRegressionSnapshot.Capture(newLayout.Bars),
                        "实际弧形图的横纵条及孔位不能随弧料端头变化");
                    Assert(oldLayout.FramePieces.Count == newLayout.FramePieces.Count, "直边框数不变");
                    for (var i = 0; i < oldLayout.FramePieces.Count; i++)
                    {
                        var a = oldLayout.FramePieces[i]; var b = newLayout.FramePieces[i];
                        Assert(Near(a.Length,b.Length) && Near(a.Center.X,b.Center.X) && Near(a.Center.Y,b.Center.Y), "直边框料长与位置不变");
                    }
                    if (isTarget)
                    {
                        targets++;
                        Assert(Math.Abs(before.PlateArc.End.Y - before.NetArc.End.Y) > 1, "重现43188原斜端头");
                        Assert(Near(after.PlateArc.Start.X, after.NetArc.Start.X), "43188上端应为竖直平口");
                        Assert(Near(after.PlateArc.End.Y, after.NetArc.End.Y), "43188下端应为水平平口");
                        Assert(Distance(after.PlateArc.Start,after.NetArc.Start) > 4.99 &&
                            Distance(after.PlateArc.Start,after.NetArc.Start) < 5.01, "43188近切线处端头保持5mm宽");
                        if (direction == LoadDirection.Horizontal)
                        {
                            var legacy = OutlineLayoutEngine.Layout(shape,spec).CurvedFramePieces[0];
                            Assert(Near(legacy.PlateArc.Start.X,legacy.NetArc.Start.X) &&
                                Near(legacy.PlateArc.End.Y,legacy.NetArc.End.Y), "43188常规水平受力路径也须切平");
                        }
                        WriteSvg(Path.Combine(outputDirectory, "43188-" + direction + ".svg"), direction,
                            plate, net, oldLayout.FramePieces, before, after);
                        Console.WriteLine("43188 " + direction + " before end delta=" + F(before.PlateArc.End.X-before.NetArc.End.X) + "," +
                            F(before.PlateArc.End.Y-before.NetArc.End.Y) + "; after=" + F(after.PlateArc.End.X-after.NetArc.End.X) + "," +
                            F(after.PlateArc.End.Y-after.NetArc.End.Y));
                    }
                    tested++;
                }
            }
            Assert(targets == 2, "必须实际检查到43188的两种受力方向");
            Console.WriteLine("Arc end checks passed: " + tested + " cases; preview: " + Path.GetFullPath(outputDirectory));
            return 0;
        }

        private static bool Near(double a,double b) { return Math.Abs(a-b) < 1e-6; }
        private static double Distance(Point2D a,Point2D b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
        private static void Assert(bool success,string message) { if(!success)throw new Exception(message); }
        private static string F(double x) { return x.ToString("0.######",CultureInfo.InvariantCulture); }

        private static void WriteSvg(string path,LoadDirection direction,Polygon plate,Polygon net,List<RotatedPiece> straights,
            CurvedFramePiece before,CurvedFramePiece after)
        {
            var s = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='900' viewBox='0 0 1200 900'><rect width='1200' height='900' fill='white'/><g font-family='sans-serif'>");
            s.Append("<text x='25' y='30' font-size='22'>43188 弧料端头修正 / ").Append(direction).Append("</text>");
            var scale = Math.Min(480 / plate.Bounds.W, 400 / plate.Bounds.H);
            for(var panel=0;panel<2;panel++)
            {
                var piece=panel==0?before:after;
                var origin=panel*600;
                s.Append("<text x='").Append(origin+30).Append("' y='64' font-size='19'>").Append(panel==0?"修改前：原偏移端点直连":"修改后：独立切平弧料端头").Append("</text>");
                Func<Point2D,Point2D> plot=p=>new Point2D(origin+50+(p.X-plate.Bounds.X0)*scale,510-(p.Y-plate.Bounds.Y0)*scale);
                Draw(s,plate,net,straights,piece,plot);
                s.Append("<text x='").Append(origin+35).Append("' y='550' font-size='15'>弧料较长边：").Append(piece.Length.ToString("0.#",CultureInfo.InvariantCulture)).Append("</text>");
                for(var end=0;end<2;end++)
                {
                    var focus=end==0?after.NetArc.Start:after.NetArc.End;
                    var left=origin+25+end*280;
                    var id="c"+panel+end;
                    s.Append("<defs><clipPath id='").Append(id).Append("'><rect x='").Append(left).Append("' y='605' width='260' height='235'/></clipPath></defs>");
                    s.Append("<rect x='").Append(left).Append("' y='605' width='260' height='235' fill='#fafafa' stroke='#bbb'/><g clip-path='url(#").Append(id).Append(")'>");
                    Func<Point2D,Point2D> zoom=p=>new Point2D(left+130+(p.X-focus.X)*10,720-(p.Y-focus.Y)*10);
                    Draw(s,plate,net,straights,piece,zoom);
                    s.Append("</g><text x='").Append(left).Append("' y='590' font-size='15'>").Append(end==0?"上端放大":"下端放大").Append("</text>");
                }
            }
            s.Append("<text x='25' y='880' font-size='15'>蓝：弧形料；灰：直边料；红：真实外框；深灰：真实内框。排条区域及直边料保持原样。</text></g></svg>");
            File.WriteAllText(path,s.ToString(),new UTF8Encoding(false));
        }

        private static void Draw(StringBuilder s,Polygon plate,Polygon net,List<RotatedPiece> straights,CurvedFramePiece band,Func<Point2D,Point2D> map)
        {
            foreach(var p in straights)Poly(s,p.Corners(),map,"#999","#777");
            Poly(s,band.BandOutline(),map,"#1976d2","#1976d2");
            Poly(s,plate.Vertices,map,"none","#c62828");
            Poly(s,net.Vertices,map,"none","#444");
        }
        private static void Poly(StringBuilder s,IReadOnlyList<Point2D> points,Func<Point2D,Point2D> map,string fill,string stroke)
        {
            s.Append("<polygon points='");
            foreach(var p in points){var q=map(p);s.Append(F(q.X)).Append(',').Append(F(q.Y)).Append(' ');}
            s.Append("' fill='").Append(fill).Append("' fill-opacity='.3' stroke='").Append(stroke).Append("' stroke-width='1.2'/>");
        }
    }
}
