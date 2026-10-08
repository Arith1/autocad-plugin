using System;
using System.Collections.Generic;
using System.Globalization;
using SteelGrid.Core.Geometry;

namespace GridTest
{
    /// <summary>
    /// 多边形净空与条带裁剪的离线自检，不依赖 AutoCAD。
    /// 覆盖方案文档《矩形与梯形排条方案》6.1 的斜边截断规则。
    /// </summary>
    internal static class PolygonSelfTest
    {
        private const double Tol = 1e-6;

        public static bool Run()
        {
            var ok = true;
            Console.WriteLine("=== Polygon 自检（不依赖 AutoCAD） ===");

            var trapezoid = new Polygon(new[]
            {
                new Point2D(0, 0),
                new Point2D(1000, 0),
                new Point2D(1300, 1000),
                new Point2D(0, 1000)
            });

            // 异形图.dxf 图形 2 归一化：上 1300 / 下 1000 / 高 1000，右斜边。
            ok &= Check("梯形面积 1150000", Near(trapezoid.Area, 1150000.0, 1e-6), F(trapezoid.Area));
            ok &= Check("凸多边形判定", trapezoid.IsConvex, trapezoid.IsConvex.ToString());
            ok &= Check("内部点 (500,500)", trapezoid.Contains(500, 500), string.Empty);
            ok &= Check("外部点 (1200,500)", !trapezoid.Contains(1200, 500), string.Empty);
            ok &= Check("边界点 (0,0) 算在内", trapezoid.Contains(0, 0), string.Empty);
            ok &= Check("斜边上的点 (1150,500) 算在内", trapezoid.Contains(1150, 500), string.Empty);

            Point2D clippedStart;
            Point2D clippedEnd;
            var clipped = trapezoid.ClipSegment(
                new Point2D(-100, 500),
                new Point2D(2000, 500),
                out clippedStart,
                out clippedEnd);
            ok &= Check(
                "线段裁剪 y=500 -> x 0..1150",
                clipped && Near(clippedStart.X, 0.0, 1e-6) && Near(clippedEnd.X, 1150.0, 1e-6),
                clipped ? F(clippedStart.X) + ".." + F(clippedEnd.X) : "无交点");

            ok &= Check(
                "完全在外的线段被拒绝",
                !trapezoid.ClipSegment(new Point2D(1400, 0), new Point2D(1400, 1000), out clippedStart, out clippedEnd),
                string.Empty);

            var inset = trapezoid.Inset(5.0);
            Console.WriteLine("  内缩 5mm 顶点: " + Describe(inset));
            ok &= Check("内缩顶点数 4", inset.Count == 4, inset.Count.ToString(CultureInfo.InvariantCulture));
            ok &= Check("内缩含 (5,5)", HasPoint(inset, 5.0, 5.0), string.Empty);
            ok &= Check("内缩含 (996.27985,5)", HasPoint(inset, 996.27984674554, 5.0), string.Empty);
            ok &= Check("内缩含 (1293.27985,995)", HasPoint(inset, 1293.27984674554, 995.0), string.Empty);
            ok &= Check("内缩含 (5,995)", HasPoint(inset, 5.0, 995.0), string.Empty);

            Console.WriteLine("  纵条（条宽 5）:");
            ok &= CheckCut(
                "  x=505 满高",
                trapezoid.ClipBand(505, 2.5, BandAxis.Vertical),
                0.0, 1000.0, 0.0, 1000.0);
            ok &= CheckCut(
                "  x=1002.5 斜边切平",
                trapezoid.ClipBand(1002.5, 2.5, BandAxis.Vertical),
                16.6666666667, 1000.0, 0.0, 1000.0);
            ok &= CheckCut(
                "  x=1100 斜边切平",
                trapezoid.ClipBand(1100, 2.5, BandAxis.Vertical),
                341.6666666667, 1000.0, 325.0, 1000.0);
            ok &= CheckCut(
                "  x=1210 斜边切平",
                trapezoid.ClipBand(1210, 2.5, BandAxis.Vertical),
                708.3333333333, 1000.0, 691.6666666667, 1000.0);
            ok &= CheckCut(
                "  x=1290 斜边切平",
                trapezoid.ClipBand(1290, 2.5, BandAxis.Vertical),
                975.0, 1000.0, 958.3333333333, 1000.0);
            ok &= CheckEmpty("  x=1310 斜边外", trapezoid.ClipBand(1310, 2.5, BandAxis.Vertical));

            Console.WriteLine("  横条（条宽 5）:");
            ok &= CheckCut(
                "  y=500 斜边切平",
                trapezoid.ClipBand(500, 2.5, BandAxis.Horizontal),
                0.0, 1149.25, 0.0, 1150.75);
            ok &= CheckCut(
                "  y=100 斜边切平",
                trapezoid.ClipBand(100, 2.5, BandAxis.Horizontal),
                0.0, 1029.25, 0.0, 1030.75);

            var rectangle = new Polygon(new[]
            {
                new Point2D(0, 0),
                new Point2D(1000, 0),
                new Point2D(1000, 600),
                new Point2D(0, 600)
            });
            Console.WriteLine("  矩形回归（条宽 5）:");
            ok &= CheckCut(
                "  纵条 x=500 满高",
                rectangle.ClipBand(500, 2.5, BandAxis.Vertical),
                0.0, 600.0, 0.0, 600.0);
            ok &= CheckCut(
                "  横条 y=300 整长",
                rectangle.ClipBand(300, 2.5, BandAxis.Horizontal),
                0.0, 1000.0, 0.0, 1000.0);
            ok &= CheckEmpty("  纵条 x=1005 在外", rectangle.ClipBand(1005, 2.5, BandAxis.Vertical));

            Console.WriteLine("=== 轮廓解析自检 ===");
            ok &= RunOutline();

            Console.WriteLine(ok ? "=== 自检全部通过 ===" : "=== 自检有失败项 ===");
            return ok;
        }

        private static bool CheckCut(
            string name,
            List<BandCut> cuts,
            double expectA,
            double expectB,
            double expectRawA,
            double expectRawB)
        {
            if (cuts.Count != 1)
            {
                return Check(name, false, "段数 " + cuts.Count.ToString(CultureInfo.InvariantCulture));
            }

            var cut = cuts[0];
            var good = Near(cut.A, expectA, 1e-6) && Near(cut.B, expectB, 1e-6)
                       && Near(cut.RawStart, expectRawA, 1e-6) && Near(cut.RawEnd, expectRawB, 1e-6);
            var detail = "A=" + F(cut.A) + " B=" + F(cut.B) + " 长=" + F(cut.Length)
                         + " 理论=" + F(cut.RawStart) + ".." + F(cut.RawEnd)
                         + (cut.StartSlanted ? " [起点切平]" : string.Empty)
                         + (cut.EndSlanted ? " [终点切平]" : string.Empty);
            return Check(name, good, detail);
        }

        private static bool CheckEmpty(string name, List<BandCut> cuts)
        {
            return Check(name, cuts.Count == 0, "段数 " + cuts.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static bool Check(string name, bool good, string detail)
        {
            Console.WriteLine((good ? "  [ok] " : "  [FAIL] ") + name
                              + (string.IsNullOrEmpty(detail) ? string.Empty : "  (" + detail + ")"));
            return good;
        }

        /// <summary>轮廓解析自检：梯形识别、矩形回归、旋转归一化、拒绝分支。</summary>
        private static bool RunOutline()
        {
            var ok = true;

            // 异形图.dxf 图形 2：上 1300 / 下 1000 / 高 1000，右斜边
            var trapezoid = OutlineParser.Parse(
                new[]
                {
                    new Point2D(627021.93, 209548.84),
                    new Point2D(625721.93, 209548.84),
                    new Point2D(625721.93, 208548.84),
                    new Point2D(626721.93, 208548.84)
                },
                null,
                true);

            ok &= Check("梯形解析通过", trapezoid.Supported, trapezoid.Message);
            if (!trapezoid.Supported)
            {
                return ok;
            }

            var shape = trapezoid.Shape;
            ok &= Check("梯形类型", shape.Kind == OutlineKind.Trapezoid, shape.Kind.ToString());
            ok &= Check(
                "梯形包围盒 1300 x 1000",
                Near(shape.Bounds.W, 1300.0, 1e-6) && Near(shape.Bounds.H, 1000.0, 1e-6),
                F(shape.Bounds.W) + " x " + F(shape.Bounds.H));
            ok &= Check(
                "梯形局部顶点跟图纸一致 (0,0)(1000,0)(1300,1000)(0,1000)",
                HasPoint(shape.Polygon, 0, 0)
                && HasPoint(shape.Polygon, 1000, 0)
                && HasPoint(shape.Polygon, 1300, 1000)
                && HasPoint(shape.Polygon, 0, 1000),
                Describe(shape.Polygon));
            ok &= Check("最长边是局部 X 轴", shape.Edges[0].IsLongest && Near(shape.Edges[0].Length, 1300, 1e-6),
                "len=" + F(shape.Edges[0].Length));
            ok &= Check("斜边归为竖向边", shape.Edges[3].NearVertical, "角度 " + F(shape.Edges[3].AngleDeg));
            ok &= Check("局部/世界坐标互转", Near(shape.ToLocal(shape.ToWorld(new Point2D(650, 500))).X, 650, 1e-6)
                && Near(shape.ToLocal(shape.ToWorld(new Point2D(650, 500))).Y, 500, 1e-6), string.Empty);
            ok &= Check("局部原点回到世界坐标", Near(shape.ToWorld(new Point2D(0, 0)).X, 625721.93, 1e-6)
                && Near(shape.ToWorld(new Point2D(0, 0)).Y, 208548.84, 1e-6),
                F(shape.ToWorld(new Point2D(0, 0)).X) + "," + F(shape.ToWorld(new Point2D(0, 0)).Y));

            // 局部坐标下再走一遍斜边截断：x=200 处理论到 675，按短边 658.33 下料
            ok &= CheckCut(
                "  梯形净空 x=1100 斜边切平",
                shape.Polygon.ClipBand(1100, 2.5, BandAxis.Vertical),
                341.6666666667, 1000.0, 325.0, 1000.0);

            // 横平竖直矩形走原路径
            var rectangle = OutlineParser.Parse(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(1000, 0),
                    new Point2D(1000, 1200),
                    new Point2D(0, 1200)
                },
                null,
                true);
            ok &= Check("矩形解析通过", rectangle.Supported, rectangle.Message);
            if (rectangle.Supported)
            {
                ok &= Check("矩形类型", rectangle.Shape.Kind == OutlineKind.Rectangle,
                    rectangle.Shape.Kind.ToString());
                ok &= Check(
                    "矩形包围盒 1000 x 1200",
                    Near(rectangle.Shape.Bounds.W, 1000, 1e-6) && Near(rectangle.Shape.Bounds.H, 1200, 1e-6),
                    F(rectangle.Shape.Bounds.W) + " x " + F(rectangle.Shape.Bounds.H));
            }

            // 整体旋转 30° 的矩形：应归一化成 1000 x 400 的平行四边形
            var sin = Math.Sin(30.0 * Math.PI / 180.0);
            var cos = Math.Cos(30.0 * Math.PI / 180.0);
            var rotated = new List<Point2D>();
            foreach (var corner in new[]
            {
                new Point2D(0, 0),
                new Point2D(1000, 0),
                new Point2D(1000, 400),
                new Point2D(0, 400)
            })
            {
                rotated.Add(new Point2D(corner.X * cos - corner.Y * sin, corner.X * sin + corner.Y * cos));
            }

            var tilted = OutlineParser.Parse(rotated, null, true);
            ok &= Check("旋转矩形解析通过", tilted.Supported, tilted.Message);
            if (tilted.Supported)
            {
                ok &= Check("旋转矩形判为平行四边形", tilted.Shape.Kind == OutlineKind.Parallelogram,
                    tilted.Shape.Kind.ToString());
                ok &= Check(
                    "旋转矩形跟随图纸（不转动坐标轴）",
                    Near(tilted.Shape.Bounds.W, 1066.03, 0.02) && Near(tilted.Shape.Bounds.H, 846.41, 0.02)
                    && Near(tilted.Shape.RotationDegrees, 0.0, 1e-9),
                    F(tilted.Shape.Bounds.W) + " x " + F(tilted.Shape.Bounds.H)
                    + " 旋转 " + F(tilted.Shape.RotationDegrees) + "°");
            }

            // 拒绝分支
            var arc = OutlineParser.Parse(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(1000, 0),
                    new Point2D(1000, 1000),
                    new Point2D(0, 1000)
                },
                new[] { 0.0, 0.0, -1.0, 0.0 },
                true);
            ok &= Check("弧形轮廓被拒绝", !arc.Supported && arc.Message.Contains("弧形"), arc.Message);

            var pentagon = OutlineParser.Parse(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(1000, 0),
                    new Point2D(1200, 500),
                    new Point2D(600, 900),
                    new Point2D(0, 500)
                },
                null,
                true);
            ok &= Check(
                "凸五边形被接受（凸包本身就是基准轮廓）",
                pentagon.Supported && pentagon.Shape.Polygon.Count == 5,
                pentagon.Supported ? pentagon.Shape.Polygon.Count + " 条边" : pentagon.Message);

            var open = OutlineParser.Parse(
                new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(1000, 1000), new Point2D(0, 1000) },
                null,
                false);
            ok &= Check("未闭合轮廓被拒绝", !open.Supported, open.Message);

            return ok;
        }

        private static bool HasPoint(Polygon polygon, double x, double y)
        {
            foreach (var point in polygon.Vertices)
            {
                if (Near(point.X, x, 0.01) && Near(point.Y, y, 0.01))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(Polygon polygon)
        {
            var parts = new List<string>();
            foreach (var point in polygon.Vertices)
            {
                parts.Add(F(point.X) + "," + F(point.Y));
            }

            return string.Join(" ", parts);
        }

        private static bool Near(double value, double expect, double tol)
        {
            return Math.Abs(value - expect) <= tol;
        }

        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
