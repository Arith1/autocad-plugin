using System;
using System.Collections.Generic;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    /// <summary>
    /// 不依赖矩形、梯形或缺口分类，直接把闭合轮廓当作排条区域。
    /// 圆弧按弦高容差离散后，纵横条带沿图纸全局竖直/水平方向与凹多边形求交，可生成多段下料。
    /// </summary>
    public static class DirectOutlineLayoutEngine
    {
        private const double ChordTolerance = 0.05;
        private const double Eps = 1e-7;

        public static OutlineLayoutResult Layout(
            IList<Point2D> vertices,
            IList<double> bulges,
            bool closed,
            Spec spec)
        {
            if (!closed)
            {
                throw new ArgumentException("所选轮廓未闭合");
            }

            if (vertices == null || vertices.Count < 2)
            {
                throw new ArgumentException("轮廓顶点不足，无法排条");
            }

            if (bulges == null || bulges.Count != vertices.Count)
            {
                throw new ArgumentException("圆弧凸度数量与顶点数不一致");
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            Validation.ValidateForOutline(spec);

            // GPGRIDNOBORDER 的条向由图纸坐标系决定，不随三角形或其他轮廓边旋转。
            var axisX = new Point2D(1.0, 0.0);
            var axisY = new Point2D(0.0, 1.0);
            var sampledWorld = Tessellate(vertices, bulges);
            if (sampledWorld.Count < 3)
            {
                throw new ArgumentException("轮廓有效边不足，无法排条");
            }

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            foreach (var point in sampledWorld)
            {
                var x = Dot(point, axisX);
                var y = Dot(point, axisY);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            var origin = new Point2D(
                axisX.X * minX + axisY.X * minY,
                axisX.Y * minX + axisY.Y * minY);
            var localPoints = new List<Point2D>();
            foreach (var point in sampledWorld)
            {
                localPoints.Add(new Point2D(
                    Dot(point, axisX) - minX,
                    Dot(point, axisY) - minY));
            }

            var region = new Polygon(localPoints);
            if (region.IsEmpty || region.Area <= Eps)
            {
                throw new ArgumentException("轮廓面积为零，无法排条");
            }

            EnsureSimple(region.Vertices);

            var width = region.Bounds.W;
            var height = region.Bounds.H;
            var directSpec = CopyDirectSpec(spec, width, height);
            var verticalBars = BuildBars(region, directSpec.Vertical, BandAxis.Vertical, width, height);
            var horizontalBars = BuildBars(region, directSpec.Horizontal, BandAxis.Horizontal, width, height);
            if (SegmentCount(verticalBars, horizontalBars) == 0)
            {
                throw new ArgumentException("轮廓内没有足够空间放置横条或纵条");
            }

            foreach (var bar in verticalBars)
            {
                BarHoles.Assign(bar, horizontalBars);
            }

            foreach (var bar in horizontalBars)
            {
                BarHoles.Assign(bar, verticalBars);
            }

            var shapeVertices = new List<Point2D>();
            foreach (var point in region.Vertices)
            {
                shapeVertices.Add(new Point2D(
                    origin.X + point.X * axisX.X + point.Y * axisY.X,
                    origin.Y + point.X * axisX.Y + point.Y * axisY.Y));
            }

            var edges = new List<OutlineEdge>();
            var longest = -1.0;
            var longestIndex = 0;
            for (var i = 0; i < region.Count; i++)
            {
                var a = region.Vertices[i];
                var b = region.Vertices[(i + 1) % region.Count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length > longest)
                {
                    longest = length;
                    longestIndex = i;
                }
            }

            for (var i = 0; i < region.Count; i++)
            {
                edges.Add(new OutlineEdge(
                    i,
                    region.Vertices[i],
                    region.Vertices[(i + 1) % region.Count],
                    i == longestIndex));
            }

            var shape = new OutlineShape(
                OutlineKind.Trapezoid,
                region,
                shapeVertices,
                edges,
                new List<Polygon>(),
                origin,
                axisX,
                axisY);
            var bounds = new Rect(0.0, 0.0, width, height);
            var geometry = new PlateGeometry(directSpec, bounds, bounds, new List<NotchGeo>());
            var bars = new LayoutResult(geometry, verticalBars, horizontalBars);

            return new OutlineLayoutResult(
                shape,
                directSpec,
                region,
                region,
                new List<Polygon>(),
                new List<Polygon>(),
                new List<CurvedFramePiece>(),
                new List<bool>(),
                new List<RotatedPiece>(),
                bars);
        }

        private static Spec CopyDirectSpec(Spec source, double width, double height)
        {
            return new Spec
            {
                OpeningW = width,
                OpeningH = height,
                Shrink = 0.0,
                FrameT = 0.0,
                LoadDirection = source.LoadDirection,
                Vertical = source.Vertical,
                Horizontal = source.Horizontal,
                Notches = new List<Notch>()
            };
        }

        private static List<Point2D> Tessellate(IList<Point2D> vertices, IList<double> bulges)
        {
            var result = new List<Point2D>();
            for (var i = 0; i < vertices.Count; i++)
            {
                if (Math.Abs(bulges[i]) <= 1e-9)
                {
                    AddDistinct(result, vertices[i]);
                    continue;
                }

                CircularArc arc;
                try
                {
                    arc = CircularArc.FromBulge(vertices[i], vertices[(i + 1) % vertices.Count], bulges[i]);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException("圆弧参数无效：" + ex.Message);
                }

                var points = arc.Tessellate(ChordTolerance);
                for (var p = 0; p < points.Count - 1; p++)
                {
                    AddDistinct(result, points[p]);
                }
            }

            if (result.Count > 1 && Distance(result[0], result[result.Count - 1]) <= Eps)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static List<Bar> BuildBars(
            Polygon region,
            BarSpec barSpec,
            BandAxis axis,
            double width,
            double height)
        {
            var span = axis == BandAxis.Vertical ? height : width;
            var lo = axis == BandAxis.Vertical ? region.Bounds.X0 : region.Bounds.Y0;
            var hi = axis == BandAxis.Vertical ? region.Bounds.X1 : region.Bounds.Y1;
            var centers = CenterPlacer.Place(lo, hi, barSpec.Thickness, barSpec.Pitch);
            var bars = new List<Bar>();
            var orientation = axis == BandAxis.Vertical ? "vertical" : "horizontal";
            for (var i = 0; i < centers.Count; i++)
            {
                var cuts = region.ClipBandIntervals(centers[i], barSpec.Thickness / 2.0, axis);
                var segments = new List<Segment>();
                foreach (var cut in cuts)
                {
                    segments.Add(new Segment(cut.A, cut.B));
                }

                var full = cuts.Count == 1 && Math.Abs(cuts[0].Length - span) <= 1e-6;
                var bar = new Bar(orientation, i + 1, centers[i], barSpec.Thickness, segments, full);
                bar.BandCuts.AddRange(cuts);
                bars.Add(bar);
            }

            return bars;
        }

        private static int SegmentCount(List<Bar> verticalBars, List<Bar> horizontalBars)
        {
            var count = 0;
            foreach (var bar in verticalBars) count += bar.Segments.Count;
            foreach (var bar in horizontalBars) count += bar.Segments.Count;
            return count;
        }

        private static void EnsureSimple(IReadOnlyList<Point2D> points)
        {
            var count = points.Count;
            for (var i = 0; i < count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % count];
                for (var j = i + 1; j < count; j++)
                {
                    if (j == i || j == (i + 1) % count || (j + 1) % count == i)
                    {
                        continue;
                    }

                    var c = points[j];
                    var d = points[(j + 1) % count];
                    if (SegmentsIntersect(a, b, c, d))
                    {
                        throw new ArgumentException("轮廓存在自相交，无法确定排条区域");
                    }
                }
            }
        }

        private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d)
        {
            var abC = Cross(a, b, c);
            var abD = Cross(a, b, d);
            var cdA = Cross(c, d, a);
            var cdB = Cross(c, d, b);
            return abC * abD < -Eps && cdA * cdB < -Eps;
        }

        private static double Cross(Point2D a, Point2D b, Point2D c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static double Dot(Point2D point, Point2D axis)
        {
            return point.X * axis.X + point.Y * axis.Y;
        }

        private static void AddDistinct(List<Point2D> points, Point2D point)
        {
            if (points.Count == 0 || Distance(points[points.Count - 1], point) > Eps)
            {
                points.Add(point);
            }
        }

        private static double Distance(Point2D a, Point2D b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
