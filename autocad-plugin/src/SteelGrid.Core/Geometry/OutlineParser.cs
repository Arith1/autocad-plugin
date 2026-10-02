using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Geometry
{
    /// <summary>轮廓解析结果。</summary>
    public sealed class OutlineParseResult
    {
        private OutlineParseResult(bool supported, string message, OutlineShape shape)
        {
            Supported = supported;
            Message = message;
            Shape = shape;
        }

        public bool Supported { get; }

        /// <summary>失败原因，直接用于提示用户。</summary>
        public string Message { get; }

        public OutlineShape Shape { get; }

        public static OutlineParseResult Ok(OutlineShape shape)
        {
            return new OutlineParseResult(true, string.Empty, shape);
        }

        public static OutlineParseResult Reject(string message)
        {
            return new OutlineParseResult(false, message, null);
        }
    }

    /// <summary>
    /// 直线轮廓解析：顶点清洗 → 弧段检查 → 四边形判定 → 局部坐标归一化。
    /// 纯算法，不依赖 AutoCAD，插件与离线测试共用同一份实现。
    /// </summary>
    public static class OutlineParser
    {
        private const double Tol = 1e-6;

        /// <summary>判断边是否横平竖直、对边是否平行的角度容差（度）。</summary>
        private const double AngleTol = 0.05;

        /// <summary>
        /// 解析一条闭合轮廓。
        /// <paramref name="vertices"/> 为世界坐标顶点，<paramref name="bulges"/> 为每条边的凸度。
        /// </summary>
        public static OutlineParseResult Parse(IList<Point2D> vertices, IList<double> bulges, bool closed)
        {
            if (vertices == null || vertices.Count < 3)
            {
                return OutlineParseResult.Reject("轮廓顶点不足 3 个");
            }

            if (!closed)
            {
                return OutlineParseResult.Reject("轮廓未闭合");
            }

            if (bulges != null)
            {
                foreach (var bulge in bulges)
                {
                    if (Math.Abs(bulge) > 1e-9)
                    {
                        return OutlineParseResult.Reject("弧形暂不支持（检测到弧段）");
                    }
                }
            }

            var points = Clean(vertices);
            if (points.Count < 4)
            {
                return OutlineParseResult.Reject("暂只支持矩形/梯形（可带矩形缺口），当前 " + points.Count + " 条边");
            }

            // 保留清洗后的原始轮廓（含缺角/缺口），直接偏移用它
            var originalPoints = new List<Point2D>(points);

            // 顶点多于 4 个：凸包就是基准轮廓，包内的顶点链就是缺口。
            var notchRings = new List<List<Point2D>>();
            if (points.Count > 4)
            {
                var hull = Polygon.ConvexHull(points);
                if (hull.Vertices.Count < 4)
                {
                    return OutlineParseResult.Reject("凸包退化成一条线，无法排条");
                }

                var basePoints = new List<Point2D>(hull.Vertices);
                if (!TrySplitNotches(points, basePoints, notchRings))
                {
                    return OutlineParseResult.Reject("缺口形状暂不支持（缺口需为凸多边形且贴在基准轮廓边上）");
                }

                points = basePoints;
            }

            var area = SignedArea(points);
            if (Math.Abs(area) <= Tol)
            {
                return OutlineParseResult.Reject("轮廓面积为零");
            }

            if (area < 0)
            {
                points.Reverse();
            }

            if (SelfIntersects(points))
            {
                return OutlineParseResult.Reject("自相交轮廓暂不支持");
            }

            if (!IsConvex(points))
            {
                return OutlineParseResult.Reject("凹轮廓暂不支持");
            }

            return OutlineParseResult.Ok(Build(points, originalPoints, notchRings));
        }

        /// <summary>
        /// 把轮廓拆成"基准凸多边形 + 缺口多边形"。
        ///
        /// 凸包相邻两个顶点之间的那串包内顶点就是缺口；但如果这条凸包边不是真实轮廓的边
        /// （缺角被凸包用一条"桥"跨过去了），就用相邻两条边的交点补回真实角点，
        /// 保证基准轮廓就是真实外轮廓。
        /// </summary>
        private static bool TrySplitNotches(
            List<Point2D> points,
            List<Point2D> basePoints,
            List<List<Point2D>> notchRings)
        {
            var baseIndexes = new List<int>();
            for (var i = 0; i < points.Count; i++)
            {
                if (FindPoint(basePoints, points[i]) >= 0)
                {
                    baseIndexes.Add(i);
                }
            }

            if (baseIndexes.Count != basePoints.Count)
            {
                return false;
            }

            for (var b = 0; b < baseIndexes.Count; b++)
            {
                var from = baseIndexes[b];
                var to = baseIndexes[(b + 1) % baseIndexes.Count];
                var count = (to - from + points.Count) % points.Count;
                if (count <= 1)
                {
                    continue;
                }

                var chain = new List<Point2D>();
                for (var k = 1; k < count; k++)
                {
                    chain.Add(points[(from + k) % points.Count]);
                }

                var first = points[from];
                var last = points[to];
                var ring = new List<Point2D>();

                if (OnSegment(first, last, chain[0]) && OnSegment(first, last, chain[chain.Count - 1]))
                {
                    // 普通缺口：开口落在基准边这条边上
                    ring.Add(first);
                    ring.AddRange(chain);
                    ring.Add(last);
                }
                else
                {
                    // 缺角：凸包边是"桥"，补回真实角点（相邻两条边的交点）
                    var firstIndex = FindPoint(basePoints, first);
                    var lastIndex = FindPoint(basePoints, last);
                    if (firstIndex < 0 || lastIndex < 0)
                    {
                        return false;
                    }

                    var count0 = basePoints.Count;
                    var prev = points[(from + points.Count - 1) % points.Count];
                    var next = points[(to + 1) % points.Count];
                    Point2D corner;
                    if (!TryIntersectLines(prev, first, last, next, out corner))
                    {
                        return false;
                    }

                    var adjacent = (firstIndex + 1) % count0 == lastIndex
                                   || (lastIndex + 1) % count0 == firstIndex;
                    if (!adjacent)
                    {
                        return false;
                    }

                    var low = Math.Min(firstIndex, lastIndex);
                    var high = Math.Max(firstIndex, lastIndex);
                    basePoints.RemoveAt(high);
                    basePoints.RemoveAt(low);
                    basePoints.Insert(low, corner);

                    ring.Add(corner);
                    ring.Add(first);
                    ring.AddRange(chain);
                    ring.Add(last);
                }

                ring = CleanRing(ring);
                var notch = new Polygon(ring);
                if (notch.IsEmpty || !notch.IsConvex || notch.Area <= 1e-6)
                {
                    return false;
                }

                foreach (var vertex in notch.Vertices)
                {
                    if (!ContainsOrOnBoundary(basePoints, vertex))
                    {
                        return false;
                    }
                }

                notchRings.Add(ring);
            }

            return true;
        }

        /// <summary>两条直线求交（用点+方向表示）。</summary>
        private static bool TryIntersectLines(
            Point2D a1,
            Point2D a2,
            Point2D b1,
            Point2D b2,
            out Point2D result)
        {
            result = a1;
            var ax = a2.X - a1.X;
            var ay = a2.Y - a1.Y;
            var bx = b2.X - b1.X;
            var by = b2.Y - b1.Y;
            var denominator = ax * by - ay * bx;
            if (Math.Abs(denominator) <= Tol)
            {
                return false;
            }

            var t = ((b1.X - a1.X) * by - (b1.Y - a1.Y) * bx) / denominator;
            result = new Point2D(a1.X + ax * t, a1.Y + ay * t);
            return true;
        }

        private static bool OnSegment(Point2D a, Point2D b, Point2D point)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= Tol)
            {
                return false;
            }

            if (Math.Abs(dx * (point.Y - a.Y) - dy * (point.X - a.X)) / length > 0.01)
            {
                return false;
            }

            var dot = (point.X - a.X) * dx + (point.Y - a.Y) * dy;
            return dot >= -0.01 && dot <= length * length + 0.01;
        }

        /// <summary>去掉缺口环上共线的点（含回折造成的"尖刺"）。</summary>
        private static List<Point2D> CleanRing(List<Point2D> ring)
        {
            var result = new List<Point2D>(ring);
            var changed = true;
            while (changed && result.Count > 3)
            {
                changed = false;
                for (var i = 0; i < result.Count; i++)
                {
                    var previous = result[(i + result.Count - 1) % result.Count];
                    var current = result[i];
                    var next = result[(i + 1) % result.Count];
                    var ax = current.X - previous.X;
                    var ay = current.Y - previous.Y;
                    var bx = next.X - current.X;
                    var by = next.Y - current.Y;
                    var cross = ax * by - ay * bx;
                    var scale = Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
                    if (scale <= Tol || Math.Abs(cross) / scale <= Tol)
                    {
                        result.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            return result;
        }

        private static int FindPoint(List<Point2D> points, Point2D target)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (Same(points[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ContainsOrOnBoundary(List<Point2D> basePoints, Point2D point)
        {
            var polygon = new Polygon(basePoints);
            return polygon.Contains(point);
        }

        private static OutlineShape Build(
            List<Point2D> points,
            List<Point2D> originalPoints,
            List<List<Point2D>> notchRings)
        {
            var axisAligned = true;
            for (var i = 0; i < points.Count; i++)
            {
                var angle = EdgeAngle(points[i], points[(i + 1) % points.Count]);
                var horizontal = Math.Abs(angle) <= AngleTol;
                var vertical = Math.Abs(Math.Abs(angle) - 90.0) <= AngleTol;
                if (!horizontal && !vertical)
                {
                    axisAligned = false;
                    break;
                }
            }

            Point2D axisX;
            Point2D axisY;
            Point2D origin0;
            var longest = LongestEdgeIndex(points);

            // 局部坐标系与图纸方向一致（只平移不旋转）：条的方向、边框的横/竖归类
            // 都以图纸方向为准，图形整体旋转不影响它们。
            axisX = new Point2D(1.0, 0.0);
            axisY = new Point2D(0.0, 1.0);
            origin0 = new Point2D(0.0, 0.0);

            var local = new List<Point2D>();
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (var point in points)
            {
                var dx = point.X - origin0.X;
                var dy = point.Y - origin0.Y;
                var lx = dx * axisX.X + dy * axisX.Y;
                var ly = dx * axisY.X + dy * axisY.Y;
                local.Add(new Point2D(lx, ly));
                minX = Math.Min(minX, lx);
                minY = Math.Min(minY, ly);
            }

            // 平移到包围盒最小值，同时把原点换算回世界坐标。
            var shifted = new List<Point2D>();
            foreach (var point in local)
            {
                shifted.Add(new Point2D(point.X - minX, point.Y - minY));
            }

            var origin = new Point2D(
                origin0.X + minX * axisX.X + minY * axisY.X,
                origin0.Y + minX * axisX.Y + minY * axisY.Y);

            var edges = new List<OutlineEdge>();
            for (var i = 0; i < shifted.Count; i++)
            {
                edges.Add(new OutlineEdge(
                    i,
                    shifted[i],
                    shifted[(i + 1) % shifted.Count],
                    i == longest));
            }

            var polygon = new Polygon(shifted);
            var kind = axisAligned
                ? OutlineKind.Rectangle
                : IsParallelogram(shifted)
                    ? OutlineKind.Parallelogram
                    : OutlineKind.Trapezoid;

            var notches = new List<Polygon>();
            foreach (var ring in notchRings)
            {
                var localRing = new List<Point2D>();
                foreach (var point in ring)
                {
                    var dx = point.X - origin0.X;
                    var dy = point.Y - origin0.Y;
                    localRing.Add(new Point2D(
                        dx * axisX.X + dy * axisX.Y - minX,
                        dx * axisY.X + dy * axisY.Y - minY));
                }

                notches.Add(new Polygon(localRing));
            }

            return new OutlineShape(
                kind,
                polygon,
                originalPoints.AsReadOnly(),
                edges,
                notches,
                origin,
                axisX,
                axisY);
        }

        /// <summary>去掉重复点与共线点。</summary>
        private static List<Point2D> Clean(IList<Point2D> vertices)
        {
            var result = new List<Point2D>();
            foreach (var point in vertices)
            {
                if (result.Count > 0 && Same(result[result.Count - 1], point))
                {
                    continue;
                }

                result.Add(point);
            }

            while (result.Count > 1 && Same(result[0], result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            // 共线点（含 180° 回折的重复方向点）合并
            var changed = true;
            while (changed && result.Count > 2)
            {
                changed = false;
                for (var i = 0; i < result.Count; i++)
                {
                    var previous = result[(i + result.Count - 1) % result.Count];
                    var current = result[i];
                    var next = result[(i + 1) % result.Count];
                    var ax = current.X - previous.X;
                    var ay = current.Y - previous.Y;
                    var bx = next.X - current.X;
                    var by = next.Y - current.Y;
                    var cross = ax * by - ay * bx;
                    var scale = Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
                    if (scale <= Tol)
                    {
                        result.RemoveAt(i);
                        changed = true;
                        break;
                    }

                    if (Math.Abs(cross) / scale <= Tol && ax * bx + ay * by > 0)
                    {
                        result.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            return result;
        }

        private static int LongestEdgeIndex(List<Point2D> points)
        {
            var index = 0;
            var longest = -1.0;
            for (var i = 0; i < points.Count; i++)
            {
                var length = Distance(points[i], points[(i + 1) % points.Count]);
                if (length > longest + Tol)
                {
                    longest = length;
                    index = i;
                }
            }

            return index;
        }

        private static bool IsParallelogram(List<Point2D> points)
        {
            var a = Sub(points[1], points[0]);
            var b = Sub(points[2], points[1]);
            var c = Sub(points[3], points[2]);
            var d = Sub(points[0], points[3]);
            return Parallel(a, c) && Parallel(b, d);
        }

        private static bool Parallel(Point2D a, Point2D b)
        {
            var cross = a.X * b.Y - a.Y * b.X;
            var scale = Math.Sqrt((a.X * a.X + a.Y * a.Y) * (b.X * b.X + b.Y * b.Y));
            if (scale <= Tol)
            {
                return true;
            }

            // 反向平行（180°）也算平行
            return Math.Abs(cross) / scale <= Tol;
        }

        private static bool IsConvex(List<Point2D> points)
        {
            var positive = false;
            var negative = false;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var c = points[(i + 2) % points.Count];
                var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
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

        private static bool SelfIntersects(List<Point2D> points)
        {
            // 四边形只需检查两对不相邻的边
            return SegmentsIntersect(points[0], points[1], points[2], points[3])
                   || SegmentsIntersect(points[1], points[2], points[3], points[0]);
        }

        private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d)
        {
            var d1 = Cross(b, c, a);
            var d2 = Cross(b, d, a);
            var d3 = Cross(d, a, c);
            var d4 = Cross(d, b, c);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }

        private static double Cross(Point2D origin, Point2D p, Point2D q)
        {
            return (p.X - origin.X) * (q.Y - origin.Y) - (p.Y - origin.Y) * (q.X - origin.X);
        }

        private static Point2D Sub(Point2D a, Point2D b)
        {
            return new Point2D(a.X - b.X, a.Y - b.Y);
        }

        private static double Distance(Point2D a, Point2D b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double EdgeAngle(Point2D a, Point2D b)
        {
            var angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            return angle;
        }

        private static bool Same(Point2D a, Point2D b)
        {
            return Math.Abs(a.X - b.X) <= Tol && Math.Abs(a.Y - b.Y) <= Tol;
        }

        private static double SignedArea(List<Point2D> points)
        {
            var sum = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2.0;
        }
    }
}
