using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Geometry
{
    /// <summary>二维点，单位 mm。</summary>
    public struct Point2D
    {
        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public override string ToString()
        {
            return X.ToString("0.###") + "," + Y.ToString("0.###");
        }
    }

    /// <summary>条带方向：纵条沿 Y 轴，横条沿 X 轴。</summary>
    public enum BandAxis
    {
        Vertical,
        Horizontal
    }

    /// <summary>
    /// 条带与净空多边形求交得到的一段料。
    /// 斜边截断已按短边切平，<see cref="Rect"/> 就是实际下料矩形。
    /// </summary>
    public struct BandCut
    {
        public BandCut(double a, double b, Rect rect, double rawStart, double rawEnd)
        {
            A = a;
            B = b;
            Rect = rect;
            RawStart = rawStart;
            RawEnd = rawEnd;
        }

        /// <summary>沿条方向的起点（已切平）。</summary>
        public double A { get; }

        /// <summary>沿条方向的终点（已切平）。</summary>
        public double B { get; }

        /// <summary>实际下料矩形，斜边侧切口已切平。</summary>
        public Rect Rect { get; }

        /// <summary>斜边处理论起点（取到较长的一侧）；非斜边时等于 A。</summary>
        public double RawStart { get; }

        /// <summary>斜边处理论终点（取到较长的一侧）；非斜边时等于 B。</summary>
        public double RawEnd { get; }

        public double Length => B - A;

        /// <summary>起点因斜边切平而缩短。</summary>
        public bool StartSlanted => A - RawStart > Tolerance;

        /// <summary>终点因斜边切平而缩短。</summary>
        public bool EndSlanted => RawEnd - B > Tolerance;

        internal const double Tolerance = 1e-6;
    }

    /// <summary>
    /// 直线边多边形，用于矩形/梯形等凸轮廓的净空裁剪。
    /// 顶点按逆时针保存；凸多边形可保证条带裁剪结果唯一。
    /// </summary>
    public sealed class Polygon
    {
        private const double Tol = 1e-6;

        private readonly List<Point2D> _vertices;

        public Polygon(IEnumerable<Point2D> vertices)
        {
            _vertices = new List<Point2D>();
            foreach (var point in vertices)
            {
                if (_vertices.Count > 0 && Same(_vertices[_vertices.Count - 1], point))
                {
                    continue;
                }

                _vertices.Add(point);
            }

            while (_vertices.Count > 1 && Same(_vertices[0], _vertices[_vertices.Count - 1]))
            {
                _vertices.RemoveAt(_vertices.Count - 1);
            }

            if (SignedArea(_vertices) < 0)
            {
                _vertices.Reverse();
            }

            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            if (_vertices.Count > 0)
            {
                minX = maxX = _vertices[0].X;
                minY = maxY = _vertices[0].Y;
                foreach (var point in _vertices)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }
            }

            Bounds = new Rect(minX, minY, maxX, maxY);
            IsConvex = TestConvex(_vertices);
        }

        public IReadOnlyList<Point2D> Vertices => _vertices;

        public Rect Bounds { get; }

        public int Count => _vertices.Count;

        /// <summary>顶点少于 3 个（退化）时为 true。</summary>
        public bool IsEmpty => _vertices.Count < 3;

        public double Area => Math.Abs(SignedArea(_vertices));

        /// <summary>凸多边形判定；凹轮廓本阶段不支持。</summary>
        public bool IsConvex { get; }

        /// <summary>点是否在多边形内（含边界）。</summary>
        public bool Contains(Point2D point)
        {
            return Contains(point.X, point.Y);
        }

        /// <summary>点是否在多边形内（含边界）。</summary>
        public bool Contains(double x, double y)
        {
            if (_vertices.Count < 3)
            {
                return false;
            }

            var inside = false;
            for (var i = 0; i < _vertices.Count; i++)
            {
                var a = _vertices[i];
                var b = _vertices[(i + 1) % _vertices.Count];
                if (OnSegment(a, b, x, y))
                {
                    return true;
                }

                if ((a.Y > y) != (b.Y > y))
                {
                    var t = (y - a.Y) / (b.Y - a.Y);
                    if (a.X + t * (b.X - a.X) > x)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        /// <summary>直线段被多边形裁剪；返回 false 表示完全在多边形外。</summary>
        public bool ClipSegment(Point2D from, Point2D to, out Point2D start, out Point2D end)
        {
            start = from;
            end = to;
            if (_vertices.Count < 3)
            {
                return false;
            }

            var tLo = 0.0;
            var tHi = 1.0;
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;

            for (var i = 0; i < _vertices.Count; i++)
            {
                var v1 = _vertices[i];
                var v2 = _vertices[(i + 1) % _vertices.Count];
                var ex = v2.X - v1.X;
                var ey = v2.Y - v1.Y;
                var tol = Tol * Math.Sqrt(ex * ex + ey * ey);
                var c0 = Cross(ex, ey, from.X - v1.X, from.Y - v1.Y);
                var c1 = Cross(ex, ey, to.X - v1.X, to.Y - v1.Y);

                if (c0 >= -tol && c1 >= -tol)
                {
                    continue;
                }

                if (c0 < -tol && c1 < -tol)
                {
                    return false;
                }

                var t = c0 / (c0 - c1);
                if (c0 < 0)
                {
                    tLo = Math.Max(tLo, t);
                }
                else
                {
                    tHi = Math.Min(tHi, t);
                }
            }

            if (tLo > tHi + Tol)
            {
                return false;
            }

            start = new Point2D(from.X + dx * tLo, from.Y + dy * tLo);
            end = new Point2D(from.X + dx * tHi, from.Y + dy * tHi);
            return true;
        }

        /// <summary>
        /// 条带与多边形求交。条带宽 <paramref name="halfWidth"/> * 2，中心在
        /// <paramref name="center"/>；被斜边斜着截断的一端按较短长边切平（见方案文档 6.1）。
        /// 返回 0 或 1 段：凸多边形与条带的交集是唯一一段。
        /// </summary>
        public List<BandCut> ClipBand(double center, double halfWidth, BandAxis axis)
        {
            var result = new List<BandCut>();
            if (_vertices.Count < 3 || halfWidth <= 0)
            {
                return result;
            }

            var lo = center - halfWidth;
            var hi = center + halfWidth;

            double loA, loB, hiA, hiB;
            if (!TryIntervalAt(lo, axis, out loA, out loB) || !TryIntervalAt(hi, axis, out hiA, out hiB))
            {
                return result;
            }

            var a = Math.Max(loA, hiA);
            var b = Math.Min(loB, hiB);
            if (b - a <= Tol)
            {
                return result;
            }

            var rect = axis == BandAxis.Vertical
                ? new Rect(lo, a, hi, b)
                : new Rect(a, lo, b, hi);

            result.Add(new BandCut(a, b, rect, Math.Min(loA, hiA), Math.Max(loB, hiB)));
            return result;
        }

        /// <summary>
        /// 条带与多边形的**并集区间**：只要条带宽度方向和多边形有任何重叠，
        /// 就返回重叠的沿向范围（用于从排条区里挖掉缺口；整宽判定会漏掉只压一半的条带）。
        /// </summary>
        public bool TryClipBandUnion(
            double center,
            double halfWidth,
            BandAxis axis,
            out double low,
            out double high)
        {
            low = double.MaxValue;
            high = double.MinValue;
            if (_vertices.Count < 3 || halfWidth <= 0)
            {
                return false;
            }

            var found = false;
            foreach (var perp in new[] { center - halfWidth, center + halfWidth })
            {
                double a, b;
                if (!TryIntervalAt(perp, axis, out a, out b))
                {
                    continue;
                }

                low = Math.Min(low, a);
                high = Math.Max(high, b);
                found = true;
            }

            return found && high - low > Tol;
        }

        /// <summary>凸多边形沿内法线外扩（缺口外扩一个边框厚用）。</summary>
        public Polygon Outset(double distance)
        {
            if (_vertices.Count < 3 || distance <= 0)
            {
                return new Polygon(_vertices);
            }

            var current = new List<Point2D>(_vertices);
            for (var i = 0; i < _vertices.Count && current.Count >= 3; i++)
            {
                var v1 = _vertices[i];
                var v2 = _vertices[(i + 1) % _vertices.Count];
                var ex = v2.X - v1.X;
                var ey = v2.Y - v1.Y;
                var length = Math.Sqrt(ex * ex + ey * ey);
                if (length <= Tol)
                {
                    continue;
                }

                current = ClipHalfPlane(current, v1.X, v1.Y, ex, ey, -distance * length);
            }

            return new Polygon(current);
        }

        /// <summary>
        /// 直接偏移：每条边沿内法线平移 distance，相邻两条偏移线求交得到新顶点。
        /// 不依赖凸包/缺口分解，凹轮廓（缺角、凹口）也能直接算。
        /// </summary>
        public Polygon Offset(double distance)
        {
            if (_vertices.Count < 3 || distance <= 0.0)
            {
                return new Polygon(_vertices);
            }

            var count = _vertices.Count;
            var result = new List<Point2D>();
            for (var i = 0; i < count; i++)
            {
                var previous = (i + count - 1) % count;
                var p1 = _vertices[previous];
                var p2 = _vertices[i];
                var q1 = _vertices[i];
                var q2 = _vertices[(i + 1) % count];

                var e1x = p2.X - p1.X;
                var e1y = p2.Y - p1.Y;
                var e2x = q2.X - q1.X;
                var e2y = q2.Y - q1.Y;
                var len1 = Math.Sqrt(e1x * e1x + e1y * e1y);
                var len2 = Math.Sqrt(e2x * e2x + e2y * e2y);
                if (len1 <= Tol || len2 <= Tol)
                {
                    result.Add(p2);
                    continue;
                }

                // 逆时针多边形：左侧为内侧
                var a1x = p1.X - e1y / len1 * distance;
                var a1y = p1.Y + e1x / len1 * distance;
                var a2x = q1.X - e2y / len2 * distance;
                var a2y = q1.Y + e2x / len2 * distance;

                var denominator = e1x * e2y - e1y * e2x;
                if (Math.Abs(denominator) <= Tol)
                {
                    // 两条边平行：退化处取偏移后的端点
                    result.Add(new Point2D(a2x, a2y));
                    continue;
                }

                var t = ((a2x - a1x) * e2y - (a2y - a1y) * e2x) / denominator;
                result.Add(new Point2D(a1x + e1x * t, a1y + e1y * t));
            }

            return new Polygon(result);
        }

        /// <summary>点集凸包（Andrew 单调链），返回逆时针顶点。</summary>
        public static Polygon ConvexHull(IList<Point2D> points)
        {
            if (points == null || points.Count < 3)
            {
                return new Polygon(new List<Point2D>());
            }

            var sorted = new List<Point2D>(points);
            sorted.Sort((a, b) =>
            {
                var byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });

            var hull = new List<Point2D>();

            // 下凸包
            for (var i = 0; i < sorted.Count; i++)
            {
                while (hull.Count >= 2 && Turn(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0.0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(sorted[i]);
            }

            // 上凸包
            var lower = hull.Count + 1;
            for (var i = sorted.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && Turn(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0.0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(sorted[i]);
            }

            hull.RemoveAt(hull.Count - 1);
            return new Polygon(hull);
        }

        /// <summary>三点转向：正为左转（逆时针）。</summary>
        private static double Turn(Point2D a, Point2D b, Point2D c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        /// <summary>凸多边形沿内法线内缩，得到净空多边形。</summary>
        public Polygon Inset(double distance)
        {
            if (_vertices.Count < 3 || distance <= 0)
            {
                return new Polygon(_vertices);
            }

            var current = new List<Point2D>(_vertices);
            for (var i = 0; i < _vertices.Count && current.Count >= 3; i++)
            {
                var v1 = _vertices[i];
                var v2 = _vertices[(i + 1) % _vertices.Count];
                var ex = v2.X - v1.X;
                var ey = v2.Y - v1.Y;
                var length = Math.Sqrt(ex * ex + ey * ey);
                if (length <= Tol)
                {
                    continue;
                }

                current = ClipHalfPlane(current, v1.X, v1.Y, ex, ey, distance * length);
            }

            return new Polygon(current);
        }

        private static List<Point2D> ClipHalfPlane(
            List<Point2D> polygon,
            double vx,
            double vy,
            double ex,
            double ey,
            double need)
        {
            var result = new List<Point2D>();
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                var fp = Cross(ex, ey, p.X - vx, p.Y - vy) - need;
                var fq = Cross(ex, ey, q.X - vx, q.Y - vy) - need;

                if (fp >= -Tol)
                {
                    result.Add(p);
                }

                if ((fp < -Tol && fq > Tol) || (fp > Tol && fq < -Tol))
                {
                    var t = fp / (fp - fq);
                    result.Add(new Point2D(p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y)));
                }
            }

            return result;
        }

        /// <summary>求多边形与直线（垂直于条带方向的坐标 = perp）相交得到的区间。</summary>
        private bool TryIntervalAt(double perp, BandAxis axis, out double lo, out double hi)
        {
            lo = double.MaxValue;
            hi = double.MinValue;
            var found = false;

            for (var i = 0; i < _vertices.Count; i++)
            {
                var p = _vertices[i];
                var q = _vertices[(i + 1) % _vertices.Count];
                var pp = Perp(p, axis) - perp;
                var qp = Perp(q, axis) - perp;
                var up = Along(p, axis);
                var uq = Along(q, axis);

                if (Math.Abs(pp) <= Tol)
                {
                    lo = Math.Min(lo, up);
                    hi = Math.Max(hi, up);
                    found = true;
                }

                if (Math.Abs(qp) <= Tol)
                {
                    lo = Math.Min(lo, uq);
                    hi = Math.Max(hi, uq);
                    found = true;
                }

                if ((pp < -Tol && qp > Tol) || (pp > Tol && qp < -Tol))
                {
                    var t = pp / (pp - qp);
                    var u = up + t * (uq - up);
                    lo = Math.Min(lo, u);
                    hi = Math.Max(hi, u);
                    found = true;
                }
            }

            return found && hi - lo > Tol;
        }

        private static double Perp(Point2D point, BandAxis axis)
        {
            return axis == BandAxis.Vertical ? point.X : point.Y;
        }

        private static double Along(Point2D point, BandAxis axis)
        {
            return axis == BandAxis.Vertical ? point.Y : point.X;
        }

        private static double Cross(double ax, double ay, double bx, double by)
        {
            return ax * by - ay * bx;
        }

        private static bool Same(Point2D a, Point2D b)
        {
            return Math.Abs(a.X - b.X) <= Tol && Math.Abs(a.Y - b.Y) <= Tol;
        }

        private static bool OnSegment(Point2D a, Point2D b, double x, double y)
        {
            var ex = b.X - a.X;
            var ey = b.Y - a.Y;
            var length = Math.Sqrt(ex * ex + ey * ey);
            if (length <= Tol)
            {
                return Math.Abs(a.X - x) <= Tol && Math.Abs(a.Y - y) <= Tol;
            }

            if (Math.Abs(Cross(ex, ey, x - a.X, y - a.Y)) > Tol * length)
            {
                return false;
            }

            var dot = (x - a.X) * ex + (y - a.Y) * ey;
            return dot >= -Tol && dot <= length * length + Tol;
        }

        private static double SignedArea(List<Point2D> points)
        {
            if (points.Count < 3)
            {
                return 0.0;
            }

            var sum = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2.0;
        }

        private static bool TestConvex(List<Point2D> points)
        {
            if (points.Count < 4)
            {
                return true;
            }

            var positive = false;
            var negative = false;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var c = points[(i + 2) % points.Count];
                var cross = Cross(b.X - a.X, b.Y - a.Y, c.X - b.X, c.Y - b.Y);
                if (cross > Tol)
                {
                    positive = true;
                }
                else if (cross < -Tol)
                {
                    negative = true;
                }

                if (positive && negative)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
