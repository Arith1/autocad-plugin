using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Model;

namespace GridTest
{
    internal static class FrameSamples
    {
        internal static Dictionary<string, List<Point2D>> All()
        {
            return new Dictionary<string, List<Point2D>>
            {
                { "矩形", Points(0,0, 1000,0, 1000,600, 0,600) },
                { "三角形", Points(0,0, 100,0, 50,80) },
                { "倒三角", Points(0,800, 500,0, 1000,800) },
                { "尖三角小于45度", Points(0,0, 200,0, 100,1000) },
                { "倒三角小于45度", Points(0,1000, 100,0, 200,1000) },
                { "左尖三角", Points(0,500, 1000,0, 1000,1000) },
                { "梯形", Points(0,0, 1000,0, 800,700, 100,700) },
                { "五边形", Points(0,0, 1000,0, 1000,800, 500,1200, 0,800) },
                { "六边形双被包", Points(0,0, 100,0, 120,50, 100,100, 0,100, -20,50) },
                { "六边形强制包边", Points(0,0, 100,0, 140,50, 100,100, 0,100, -40,50) },
                { "顶部缺口", Points(0,0, 1000,0, 1000,1000, 700,1000, 700,600, 300,600, 300,1000, 0,1000) },
                { "多底边", Points(0,0, 300,0, 300,200, 700,200, 700,0, 1000,0, 1000,800, 0,800) },
                { "侧向台阶", Points(0,0, 1000,0, 1000,500, 600,500, 600,800, 0,800) },
                { "斜壁缺口", Points(0,0, 1000,0, 1000,800, 650,800, 600,500, 400,500, 350,800, 0,800) },
                { "八边形135度", Points(100,0, 900,0, 1000,100, 1000,700, 900,800, 100,800, 0,700, 0,100) }
            };
        }

        internal static List<Point2D> Points(params double[] coordinates)
        {
            var points = new List<Point2D>();
            for (var i = 0; i < coordinates.Length; i += 2)
                points.Add(new Point2D(coordinates[i], coordinates[i + 1]));
            return points;
        }
    }

    internal static class FrameWrapTest
    {
        private static int checks;

        internal static int Run(string outputDirectory)
        {
            checks = 0;
            Directory.CreateDirectory(outputDirectory);
            var index = new StringBuilder("<!doctype html><meta charset='utf-8'><title>边框包边预览</title>" +
                "<style>body{font:16px sans-serif;background:#eee}object{width:1100px;height:850px;background:white;margin:12px}</style>" +
                "<h1>直线异形边框预览</h1><p>蓝：至少一端包边；橙：两端被包。红：外框；灰：内框。允许连接处搭接。</p>");
            foreach (var sample in FrameSamples.All())
            {
                foreach (var direction in new[] { LoadDirection.Vertical, LoadDirection.Horizontal })
                {
                    var plate = new Polygon(sample.Value);
                    var net = plate.Offset(5);
                    var plans = FrameWrapPlanner.Plan(plate, net, 5, direction);
                    CheckRules(plans);
                    CheckLengths(plate, net, plans);
                    CheckStable(plate, net, plans, direction);
                    var name = sample.Key + "-" + direction + ".svg";
                    WriteSvg(Path.Combine(outputDirectory, name), sample.Key + " / " + direction, plate, net, plans);
                    index.Append("<object type='image/svg+xml' data='").Append(name).Append("'></object>");
                    Console.WriteLine("PASS " + sample.Key + " " + direction + " / " + plans.Count + " pieces");
                }
            }
            var rectangle = new Polygon(FrameSamples.All()["矩形"]);
            var rectanglePlans = FrameWrapPlanner.Plan(rectangle, rectangle.Offset(5), 5, LoadDirection.Vertical);
            Assert(Near(rectanglePlans[0].Piece.Length, 1000) && Near(rectanglePlans[1].Piece.Length, 590)
                && Near(rectanglePlans[2].Piece.Length, 1000) && Near(rectanglePlans[3].Piece.Length, 590), "矩形旧料长保持");
            var hex = new Polygon(FrameSamples.All()["六边形双被包"]);
            var hexPlans = FrameWrapPlanner.Plan(hex, hex.Offset(5), 5, LoadDirection.Vertical);
            Assert(hexPlans[0].WrapAtStart && hexPlans[0].WrapAtEnd &&
                hexPlans[3].WrapAtStart && hexPlans[3].WrapAtEnd, "六边形顶底包边");
            foreach (var i in new[] { 1, 2, 4, 5 })
                Assert(!hexPlans[i].WrapAtStart && !hexPlans[i].WrapAtEnd, "六边形中间四料允许双被包");
            var triangle = new Polygon(FrameSamples.All()["三角形"]);
            var trianglePlans = FrameWrapPlanner.Plan(triangle, triangle.Offset(5), 5, LoadDirection.Vertical);
            Assert(Near(trianglePlans[1].Piece.Length, 77.31857311303065), "三角形被包料保持最短77.318573，不得延长为82.9");
            Assert(Near(trianglePlans[2].Piece.Length, 85.31857311303065), "混合端部保持原投影长度85.318573");
            var upsideDown = new Polygon(FrameSamples.All()["倒三角"]);
            var upsideDownPlans = FrameWrapPlanner.Plan(upsideDown, upsideDown.Offset(5), 5, LoadDirection.Vertical);
            Assert(upsideDownPlans[2].WrapAtStart && upsideDownPlans[2].WrapAtEnd, "倒三角顶边包两端");
            Assert(!upsideDownPlans[0].WrapAtStart && !upsideDownPlans[1].WrapAtEnd, "倒三角两斜料上端均被顶边包住");
            Assert(upsideDownPlans[0].WrapAtEnd != upsideDownPlans[1].WrapAtStart, "倒三角下尖角在区间内必须一包一被");
            var narrowDown = new Polygon(FrameSamples.All()["倒三角小于45度"]);
            var narrowDownPlans = FrameWrapPlanner.Plan(narrowDown, narrowDown.Offset(5), 5, LoadDirection.Vertical);
            Assert(narrowDownPlans[2].WrapAtStart && narrowDownPlans[2].WrapAtEnd, "窄倒三角顶边仍包两端");
            Assert(!narrowDownPlans[0].WrapAtStart && !narrowDownPlans[0].WrapAtEnd &&
                !narrowDownPlans[1].WrapAtStart && !narrowDownPlans[1].WrapAtEnd, "小于45度的下尖角允许双被包");
            var leftTip = new Polygon(FrameSamples.All()["左尖三角"]);
            var leftTipPlans = FrameWrapPlanner.Plan(leftTip, leftTip.Offset(5), 5, LoadDirection.Horizontal);
            Assert(leftTipPlans[1].WrapAtStart && leftTipPlans[1].WrapAtEnd, "水平受力遇左尖点时先由右边包两端");
            var legacyRaw = new Polygon(FrameSamples.Points(0,0, 1000,0, 1300,1000, 0,1000));
            var legacyPlate = legacyRaw.Offset(5);
            var legacyPlans = FrameWrapPlanner.Plan(legacyPlate, legacyPlate.Offset(5), 5, LoadDirection.Vertical);
            var expectedLegacy = new[] { 991.279846745545, 1023.15003778123, 1288.27984674555, 980.0 };
            for (var i = 0; i < 4; i++)
                Assert(Math.Abs(legacyPlans[i].Piece.Length - expectedLegacy[i]) < .02, "交接文档梯形既有料长 " + i);
            Assert(FrameWrapPlanner.RequiresWrapping(45) && FrameWrapPlanner.RequiresWrapping(135), "角度区间包含边界");
            Assert(!FrameWrapPlanner.RequiresWrapping(44.999) && !FrameWrapPlanner.RequiresWrapping(135.001), "角度区间外允许双被包");
            var split = new Polygon(FrameSamples.Points(0,0, 500,0, 1000,0, 1000,600, 0,600));
            Assert(FrameWrapPlanner.Plan(split, split.Offset(5), 5, LoadDirection.Vertical).Count == 4, "连续共线点合并为一根料");
            var chamfer = new Polygon(FrameSamples.Points(0,0, 100,0, 100,98, 98,100, 0,100));
            Reject(() => FrameWrapPlanner.Plan(chamfer, chamfer.Offset(5), 5, LoadDirection.Vertical), "消失短边拒绝");
            Reject(() => FrameWrapPlanner.Plan(rectangle, rectangle.Offset(3), 5, LoadDirection.Vertical), "错误偏移间距拒绝");
            Reject(() => FrameWrapPlanner.Plan(rectangle, rectangle.Offset(5), double.NaN, LoadDirection.Vertical), "无效厚度拒绝");
            var touching = new Polygon(FrameSamples.Points(0,0, 100,0, 100,100, 50,0, 0,100));
            Reject(() => FrameWrapPlanner.Plan(touching, touching.Offset(5), 5, LoadDirection.Vertical), "自接触轮廓拒绝");
            File.WriteAllText(Path.Combine(outputDirectory, "index.html"), index.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Frame checks passed: " + checks + "; preview: " + Path.GetFullPath(outputDirectory));
            return 0;
        }

        private static void CheckRules(List<FrameEdgePlan> plans)
        {
            for (var i = 0; i < plans.Count; i++)
            {
                var current = plans[i]; var previous = plans[(i + plans.Count - 1) % plans.Count];
                var a = Vector(current.OuterStart, previous.OuterStart);
                var b = Vector(current.OuterStart, current.OuterEnd);
                var cosine = (a.X * b.X + a.Y * b.Y) / Math.Sqrt((a.X*a.X+a.Y*a.Y)*(b.X*b.X+b.Y*b.Y));
                var angle = Math.Acos(Math.Max(-1, Math.Min(1, cosine))) * 180 / Math.PI;
                var wrapping = (current.WrapAtStart ? 1 : 0) + (previous.WrapAtEnd ? 1 : 0);
                Assert(wrapping <= 1, "同一个角不能双方都包");
                if (angle >= 45 - 1e-7 && angle <= 135 + 1e-7) Assert(wrapping == 1, "强制区间必须有包边方");
                Assert(current.Piece.Length > 0, "每根料有正长度");
            }
        }

        internal static void CheckLengths(Polygon plate, Polygon net, List<FrameEdgePlan> plans,
            List<RotatedPiece> actualPieces = null)
        {
            foreach (var plan in plans)
            {
                var a = FindVertex(plate, plan.OuterStart);
                var b = FindVertex(plate, plan.OuterEnd);
                var dx = plan.OuterEnd.X - plan.OuterStart.X;
                var dy = plan.OuterEnd.Y - plan.OuterStart.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                var ex = dx / length; var ey = dy / length;
                var ua = (net.Vertices[a].X - plan.OuterStart.X) * ex + (net.Vertices[a].Y - plan.OuterStart.Y) * ey;
                var ub = (net.Vertices[b].X - plan.OuterStart.X) * ex + (net.Vertices[b].Y - plan.OuterStart.Y) * ey;
                var spans = new[] { length, Math.Abs(ub - ua), Math.Abs(ub), Math.Abs(length - ua) };
                double expected;
                if (plan.WrapAtStart && plan.WrapAtEnd) expected = Math.Max(Math.Max(spans[0],spans[1]),Math.Max(spans[2],spans[3]));
                else if (!plan.WrapAtStart && !plan.WrapAtEnd) expected = Math.Min(Math.Min(spans[0],spans[1]),Math.Min(spans[2],spans[3]));
                else expected = (plan.WrapAtEnd ? Math.Max(length,ub) : Math.Min(length,ub)) -
                    (plan.WrapAtStart ? Math.Min(0,ua) : Math.Max(0,ua));
                var piece = plan.Piece;
                if (actualPieces != null)
                {
                    piece = actualPieces.Find(item => item.EdgeIndex == plan.EdgeIndex);
                    Assert(piece != null, "实际输出含该根边框料");
                }
                Assert(Near(piece.Length, expected), "四点取料长度 E" + (plan.EdgeIndex + 1));
                var normalFromInner = -ey * (piece.Center.X - net.Vertices[a].X) + ex * (piece.Center.Y - net.Vertices[a].Y);
                Assert(Near(normalFromInner, -piece.Thickness / 2), "以内框法向位置定位，不偏移一个厚度");
                var middle = (piece.Center.X - plan.OuterStart.X) * ex + (piece.Center.Y - plan.OuterStart.Y) * ey;
                Assert(Near(middle, (plan.Start + plan.End) / 2), "沿选中端点的中点定位");
            }
        }

        private static int FindVertex(Polygon plate, Point2D point)
        {
            for (var i = 0; i < plate.Count; i++) if (Same(plate.Vertices[i], point)) return i;
            throw new Exception("找不到料件对应的原始框线顶点");
        }

        private static void CheckStable(Polygon plate, Polygon net, List<FrameEdgePlan> expected, LoadDirection direction)
        {
            for (var offset = 0; offset < plate.Count; offset++)
                foreach (var reverse in new[] { false, true })
                {
                    var outer = new List<Point2D>(); var inner = new List<Point2D>();
                    for (var i = 0; i < plate.Count; i++)
                    {
                        var j = (offset + (reverse ? plate.Count - i : i)) % plate.Count;
                        outer.Add(plate.Vertices[j]); inner.Add(net.Vertices[j]);
                    }
                    CompareGeometry(expected, FrameWrapPlanner.Plan(new Polygon(outer), new Polygon(inner), 5, direction), p => p);
                }
            var localPlate = new List<Point2D>(); var localNet = new List<Point2D>();
            foreach (var p in plate.Vertices) localPlate.Add(Rotate(p, -0.63));
            foreach (var p in net.Vertices) localNet.Add(Rotate(p, -0.63));
            CompareGeometry(expected, FrameWrapPlanner.Plan(new Polygon(localPlate), new Polygon(localNet), 5,
                direction, p => Rotate(p, 0.63)), p => Rotate(p, 0.63));
            localPlate.Clear(); localNet.Clear();
            foreach (var p in plate.Vertices) localPlate.Add(new Point2D(p.X + 1234, p.Y - 5678));
            foreach (var p in net.Vertices) localNet.Add(new Point2D(p.X + 1234, p.Y - 5678));
            CompareGeometry(expected, FrameWrapPlanner.Plan(new Polygon(localPlate), new Polygon(localNet), 5, direction),
                p => new Point2D(p.X - 1234, p.Y + 5678));
        }

        private static void CompareGeometry(List<FrameEdgePlan> expected, List<FrameEdgePlan> actual, Func<Point2D, Point2D> map)
        {
            Assert(expected.Count == actual.Count, "不因起点、绕向或局部坐标改变料件数");
            foreach (var e in expected)
            {
                FrameEdgePlan match = null;
                foreach (var a in actual)
                    if (Same(e.OuterStart, map(a.OuterStart)) && Same(e.OuterEnd, map(a.OuterEnd))) { match = a; break; }
                Assert(match != null, "几何边对应");
                Assert(e.WrapAtStart == match.WrapAtStart && e.WrapAtEnd == match.WrapAtEnd, "包边归属稳定");
                Assert(Near(e.Piece.Length, match.Piece.Length) && Same(e.Piece.Center, map(match.Piece.Center)), "料件长度和位置稳定");
            }
        }

        private static void Reject(Action action, string name)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new Exception(name + " 未拒绝");
        }

        private static void Assert(bool success, string name)
        {
            checks++;
            if (!success) throw new Exception(name);
        }
        private static bool Near(double a, double b) { return Math.Abs(a - b) <= 1e-5; }
        private static bool Same(Point2D a, Point2D b) { return Near(a.X,b.X) && Near(a.Y,b.Y); }
        private static Point2D Vector(Point2D a, Point2D b) { return new Point2D(b.X-a.X,b.Y-a.Y); }
        private static Point2D Rotate(Point2D p, double a) { return new Point2D(p.X*Math.Cos(a)-p.Y*Math.Sin(a),p.X*Math.Sin(a)+p.Y*Math.Cos(a)); }
        private static string F(double n) { return n.ToString("0.###", CultureInfo.InvariantCulture); }

        private static void WriteSvg(string path, string title, Polygon plate, Polygon net, List<FrameEdgePlan> plans)
        {
            var text = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='1100' height='850' viewBox='0 0 1100 850'><rect width='1100' height='850' fill='white'/>");
            text.Append("<g font-family='sans-serif'><text x='30' y='32' font-size='21'>").Append(title).Append("</text>");
            var scale = Math.Min(640 / plate.Bounds.W, 490 / plate.Bounds.H);
            Func<Point2D, Point2D> plot = p => new Point2D(50 + (p.X-plate.Bounds.X0)*scale, 570-(p.Y-plate.Bounds.Y0)*scale);
            Draw(text, plate, net, plans, plot, 1.1, true);
            for (var i = 0; i < plans.Count; i++)
            {
                var p = plans[i];
                text.Append("<text x='").Append(35 + i%3*355).Append("' y='").Append(625 + i/3*24).Append("' font-size='15'>E")
                    .Append(p.EdgeIndex+1).Append("  ").Append(p.WrapAtStart ? "包" : "被").Append(" / ")
                    .Append(p.WrapAtEnd ? "包" : "被").Append("  L=").Append(p.Piece.Length.ToString("0.#",CultureInfo.InvariantCulture)).Append("</text>");
            }
            var zoomCorners = new List<int>();
            for (var i = 0; i < plate.Count; i++)
            {
                var a = Vector(plate.Vertices[(i + plate.Count - 1) % plate.Count], plate.Vertices[i]);
                var b = Vector(plate.Vertices[i], plate.Vertices[(i + 1) % plate.Count]);
                if (a.X * b.Y - a.Y * b.X < -1e-6 || (Math.Abs(a.Y) > 1e-6 && Math.Abs(b.Y) > 1e-6))
                    zoomCorners.Add(i);
            }
            for (var i = 0; zoomCorners.Count < 2; i++) if (!zoomCorners.Contains(i)) zoomCorners.Add(i);
            for (var i = 0; i < 2; i++)
            {
                var corner = plate.Vertices[zoomCorners[i]];
                var top = 85+i*245;
                text.Append("<defs><clipPath id='zoom").Append(i).Append("'><rect x='780' y='").Append(top).Append("' width='270' height='210'/></clipPath></defs>");
                text.Append("<rect x='780' y='").Append(top).Append("' width='270' height='210' fill='#fafafa' stroke='#bbb'/><g clip-path='url(#zoom").Append(i).Append(")'>");
                Func<Point2D,Point2D> zoom = p => new Point2D(915+(p.X-corner.X)*8,top+105-(p.Y-corner.Y)*8);
                Draw(text, plate, net, plans, zoom, 1.2, false);
                text.Append("</g><text x='780' y='").Append(top-10).Append("' font-size='14'>顶点 ").Append(zoomCorners[i]+1).Append(" 放大</text>");
            }
            text.Append("<text x='35' y='815' font-size='15'>蓝：含包边端；橙：两端被包；红：外框；灰：内框。局部搭接允许。</text></g></svg>");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static void Draw(StringBuilder text, Polygon plate, Polygon net, List<FrameEdgePlan> plans,
            Func<Point2D,Point2D> map, double width, bool labels)
        {
            foreach (var p in plans)
            {
                var color = p.WrapAtStart || p.WrapAtEnd ? "#1976d2" : "#ef6c00";
                Poly(text,p.Piece.Corners(),map,color,color,width);
                if (labels)
                {
                    var center=map(p.Piece.Center);
                    text.Append("<text x='").Append(F(center.X)).Append("' y='").Append(F(center.Y-7))
                        .Append("' font-size='14' text-anchor='middle'>E").Append(p.EdgeIndex+1).Append("</text>");
                }
            }
            Poly(text,plate.Vertices,map,"none","#c62828",width);
            Poly(text,net.Vertices,map,"none","#777",width);
        }
        private static void Poly(StringBuilder s,IReadOnlyList<Point2D> points,Func<Point2D,Point2D> map,string fill,string stroke,double width)
        {
            s.Append("<polygon points='");
            foreach(var p in points){var q=map(p);s.Append(F(q.X)).Append(',').Append(F(q.Y)).Append(' ');}
            s.Append("' fill='").Append(fill).Append("' fill-opacity='.25' stroke='").Append(stroke).Append("' stroke-width='").Append(F(width)).Append("'/>");
        }
    }
}
