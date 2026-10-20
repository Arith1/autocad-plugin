using System;
using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    /// <summary>一根直边框料的两端关系及沿外框边的实际下料区间。</summary>
    public sealed class FrameEdgePlan
    {
        internal FrameEdgePlan(int edgeIndex, Point2D a, Point2D b, Point2D innerA, bool startWrap, bool endWrap,
            double start, double end, double thickness, bool horizontal)
        {
            EdgeIndex = edgeIndex;
            OuterStart = a;
            OuterEnd = b;
            WrapAtStart = startWrap;
            WrapAtEnd = endWrap;
            Start = start;
            End = end;
            var length = FrameWrapPlanner.Length(FrameWrapPlanner.Sub(b, a));
            var ex = (b.X - a.X) / length;
            var ey = (b.Y - a.Y) / length;
            // 两端被包的料沿内框线定位，包边及混合端部沿外框线定位。
            var covered = !startWrap && !endWrap;
            var origin = covered ? innerA : a;
            var originU = (origin.X - a.X) * ex + (origin.Y - a.Y) * ey;
            var mid = (start + end) / 2 - originU;
            var normalOffset = (covered ? -1 : 1) * thickness / 2;
            var center = new Point2D(origin.X + ex * mid - ey * normalOffset,
                origin.Y + ey * mid + ex * normalOffset);
            var angle = Math.Atan2(ey, ex) * 180 / Math.PI;
            while (angle <= -90) angle += 180;
            while (angle > 90) angle -= 180;
            Piece = new RotatedPiece(edgeIndex, horizontal ? "横向" : "纵向", center,
                end - start, thickness, angle, new Point2D(ey, -ex));
        }

        public int EdgeIndex { get; }
        public Point2D OuterStart { get; }
        public Point2D OuterEnd { get; }
        public bool WrapAtStart { get; }
        public bool WrapAtEnd { get; }
        public double Start { get; }
        public double End { get; }
        public RotatedPiece Piece { get; }
    }

    /// <summary>
    /// 仅处理直线边框，不读取或修改横纵条。世界方向用于选边，输出保持输入局部坐标。
    /// 两端向中间推进，45°～135°的连接角强制一包一被包；其余角允许双被包。
    /// </summary>
    public static class FrameWrapPlanner
    {
        private const double Tol = 1e-6;
        private const double AngleTol = 1e-7;

        private sealed class Edge
        {
            public int Index;
            public Point2D A, B, InnerA, InnerB, E;
            public double Length, Low, High, Across, CrossSpan, PrimaryStart;
            public double LongStart, ShortStart, LongEnd, ShortEnd, Start, End;
            public double InnerStart, InnerEnd;
            public bool Horizontal;
        }

        public static List<FrameEdgePlan> Plan(Polygon plate, Polygon net, double thickness,
            LoadDirection direction, Func<Point2D, Point2D> toWorld = null)
        {
            if (plate == null || net == null || plate.IsEmpty || net.IsEmpty || !Finite(thickness) || thickness <= 0)
                throw new ArgumentException("直边框需要有效的内外框和正边框厚度");
            if (plate.Count != net.Count)
                throw new ArgumentException("内外框边数不同，不能逐角计算边框料");
            foreach (var polygon in new[] { plate, net })
                foreach (var point in polygon.Vertices)
                    if (!Finite(point.X) || !Finite(point.Y))
                        throw new ArgumentException("边框包含无效坐标");
            EnsureSimple(plate);
            EnsureSimple(net);
            if (net.Area >= plate.Area)
                throw new ArgumentException("内框未位于外框内部，不能计算边框料");

            var edges = PrepareEdges(plate, net, thickness, direction, toWorld);
            var count = edges.Count;
            var owners = new int[count];
            for (var i = 0; i < count; i++) owners[i] = -1; // -1 允许相遇处双被包。
            var low = new List<int>();
            var high = new List<int>();
            for (var i = 0; i < count; i++) { low.Add(i); high.Add(i); }
            low.Sort((a, b) => Compare(edges[a], edges[b], false));
            high.Sort((a, b) => Compare(edges[a], edges[b], true));
            var blocked = new bool[count];
            var tipCorners = new bool[count];
            var minimum = edges[low[0]].Low;
            var maximum = edges[high[0]].High;
            for (var corner = 0; corner < count; corner++)
            {
                var previous = edges[(corner + count - 1) % count];
                var current = edges[corner];
                tipCorners[corner] =
                    (current.PrimaryStart == minimum && previous.High > minimum && current.High > minimum) ||
                    (current.PrimaryStart == maximum && previous.Low < maximum && current.Low < maximum);
            }

            // 只有真实的端部平边才先包两端。极端位置为尖点时，其接头留到夹角判断，
            // 倒三角因此先由顶边包住两根斜料的上端，不会让一根斜料抢占整个底边身份。
            var lowerEdge = edges[low[0]];
            var upperEdge = edges[high[0]];
            if (lowerEdge.Low == lowerEdge.High) Claim(low[0], owners, blocked, tipCorners);
            if (upperEdge.Low == upperEdge.High && high[0] != low[0])
                Claim(high[0], owners, blocked, tipCorners);
            var lower = 0;
            var upper = 0;
            while (true)
            {
                while (lower < count && blocked[low[lower]]) lower++;
                while (upper < count && blocked[high[upper]]) upper++;
                if (lower == count && upper == count) break;
                if (lower < count) Claim(low[lower++], owners, blocked, tipCorners);
                while (upper < count && blocked[high[upper]]) upper++;
                if (upper < count) Claim(high[upper++], owners, blocked, tipCorners);
            }

            // 对所有直线轮廓逐角应用，与三角形、六边形或其他边数无关。
            // 已分配的角天然一包一被；未分配的角再按45°～135°约束补齐。
            for (var corner = 0; corner < count; corner++)
            {
                if (owners[corner] >= 0) continue;
                var previous = (corner + count - 1) % count;
                var cosine = -Dot(edges[previous].E, edges[corner].E);
                var angle = Math.Acos(Math.Max(-1, Math.Min(1, cosine))) * 180 / Math.PI;
                if (RequiresWrapping(angle))
                    owners[corner] = Compare(edges[previous], edges[corner], false) <= 0
                        ? previous : corner;
            }

            for (var i = 0; i < count; i++)
            {
                var edge = edges[i];
                var wrapsAtStart = owners[i] == i;
                var wrapsAtEnd = owners[(i + 1) % count] == i;
                if (wrapsAtStart == wrapsAtEnd)
                {
                    // 保留既有四点取料规则：只比较跨两端的 AB、ab、Ab、aB。
                    // 两端包取最长，两端被包取最短；禁止再按框带覆盖将短料延长。
                    var starts = new[] { 0.0, edge.InnerStart, 0.0, edge.InnerStart };
                    var ends = new[] { edge.Length, edge.InnerEnd, edge.InnerEnd, edge.Length };
                    var best = 0;
                    for (var candidate = 1; candidate < starts.Length; candidate++)
                    {
                        var length = Math.Abs(ends[candidate] - starts[candidate]);
                        var selected = Math.Abs(ends[best] - starts[best]);
                        if (wrapsAtStart ? length > selected : length < selected) best = candidate;
                    }
                    edge.Start = Math.Min(starts[best], ends[best]);
                    edge.End = Math.Max(starts[best], ends[best]);
                }
                else
                {
                    // 混合端部保持各自关系：包边端取向外投影，被包端取让位投影。
                    edge.Start = wrapsAtStart ? edge.LongStart : edge.ShortStart;
                    edge.End = wrapsAtEnd ? edge.LongEnd : edge.ShortEnd;
                }
                if (edge.End - edge.Start <= Tol)
                    throw new ArgumentException("边 " + (edge.Index + 1) + " 的可用边框料长不足");
            }

            var result = new List<FrameEdgePlan>();
            for (var i = 0; i < count; i++)
            {
                var e = edges[i];
                result.Add(new FrameEdgePlan(e.Index, e.A, e.B, e.InnerA, owners[i] == i,
                    owners[(i + 1) % count] == i, e.Start, e.End, thickness, e.Horizontal));
            }
            return result;
        }

        public static bool RequiresWrapping(double angleDegrees)
        {
            return angleDegrees >= 45 - AngleTol && angleDegrees <= 135 + AngleTol;
        }

        private static void Claim(int i, int[] owners, bool[] blocked, bool[] tipCorners)
        {
            var next = (i + 1) % owners.Length;
            if (owners[i] < 0 && !tipCorners[i]) owners[i] = i;
            if (owners[next] < 0 && !tipCorners[next]) owners[next] = i;
            blocked[i] = blocked[(i + owners.Length - 1) % owners.Length] = blocked[next] = true;
        }

        private static List<Edge> PrepareEdges(Polygon plate, Polygon net, double thickness,
            LoadDirection direction, Func<Point2D, Point2D> toWorld)
        {
            var indices = new List<int>();
            for (var i = 0; i < plate.Count; i++) indices.Add(i);
            // 连续共线点不是新材料；内外框同时共线才能合并，保持原边号供标注定位。
            var changed = true;
            while (changed && indices.Count > 3)
            {
                changed = false;
                for (var i = 0; i < indices.Count; i++)
                {
                    var a = indices[(i + indices.Count - 1) % indices.Count];
                    var b = indices[i];
                    var c = indices[(i + 1) % indices.Count];
                    if (Collinear(plate.Vertices[a], plate.Vertices[b], plate.Vertices[c]) &&
                        Collinear(net.Vertices[a], net.Vertices[b], net.Vertices[c]))
                    { indices.RemoveAt(i); changed = true; break; }
                }
            }

            var result = new List<Edge>();
            double basePrimary = double.MaxValue, baseAcross = double.MaxValue;
            for (var i = 0; i < indices.Count; i++)
            {
                var a = indices[i];
                var b = indices[(i + 1) % indices.Count];
                var edge = new Edge { Index = a, A = plate.Vertices[a], B = plate.Vertices[b],
                    InnerA = net.Vertices[a], InnerB = net.Vertices[b] };
                var delta = Sub(edge.B, edge.A);
                edge.Length = Length(delta);
                if (edge.Length <= Tol) throw new ArgumentException("边框存在零长度边");
                edge.E = new Point2D(delta.X / edge.Length, delta.Y / edge.Length);
                var innerDelta = Sub(edge.InnerB, edge.InnerA);
                var start = Dot(Sub(edge.InnerA, edge.A), edge.E);
                var end = Dot(Sub(edge.InnerB, edge.A), edge.E);
                var spacingA = Cross(edge.E, Sub(edge.InnerA, edge.A));
                var spacingB = Cross(edge.E, Sub(edge.InnerB, edge.A));
                var spacingTolerance = Math.Max(Tol, thickness * 1e-6);
                if (end - start <= Tol || Length(innerDelta) <= Tol ||
                    Math.Abs(spacingA - thickness) > spacingTolerance ||
                    Math.Abs(spacingB - thickness) > spacingTolerance)
                    throw new ArgumentException("边 " + (a + 1) + " 的内外框偏移对应失效，可能存在消失的短边");
                edge.LongStart = Math.Min(0, start);
                edge.InnerStart = start;
                edge.InnerEnd = end;
                edge.ShortStart = Math.Max(0, start);
                edge.LongEnd = Math.Max(edge.Length, end);
                edge.ShortEnd = Math.Min(edge.Length, end);
                var wa = toWorld == null ? edge.A : toWorld(edge.A);
                var wb = toWorld == null ? edge.B : toWorld(edge.B);
                var pa = direction == LoadDirection.Vertical ? wa.Y : wa.X;
                var pb = direction == LoadDirection.Vertical ? wb.Y : wb.X;
                var qa = direction == LoadDirection.Vertical ? wa.X : wa.Y;
                var qb = direction == LoadDirection.Vertical ? wb.X : wb.Y;
                edge.Low = Math.Min(pa, pb);
                edge.PrimaryStart = pa;
                edge.High = Math.Max(pa, pb);
                edge.Across = (qa + qb) / 2;
                edge.CrossSpan = Math.Abs(qa - qb);
                edge.Horizontal = Math.Abs(wb.X - wa.X) >= Math.Abs(wb.Y - wa.Y);
                basePrimary = Math.Min(basePrimary, edge.Low);
                baseAcross = Math.Min(baseAcross, Math.Min(qa, qb));
                result.Add(edge);
            }
            // 先以统一容差量化再排序，避免 epsilon 比较器不满足传递性。
            foreach (var e in result)
            {
                e.Low = Math.Round((e.Low - basePrimary) / Tol);
                e.High = Math.Round((e.High - basePrimary) / Tol);
                e.PrimaryStart = Math.Round((e.PrimaryStart - basePrimary) / Tol);
                e.Across = Math.Round((e.Across - baseAcross) / Tol);
                e.CrossSpan = Math.Round(e.CrossSpan / Tol);
            }
            return result;
        }

        private static int Compare(Edge a, Edge b, bool high)
        {
            var c = high ? b.High.CompareTo(a.High) : a.Low.CompareTo(b.Low);
            if (c != 0) return c;
            c = high ? b.Low.CompareTo(a.Low) : a.High.CompareTo(b.High);
            if (c != 0) return c;
            c = b.CrossSpan.CompareTo(a.CrossSpan);
            if (c != 0) return c;
            c = a.Across.CompareTo(b.Across);
            if (c != 0) return c;
            // 相同的投影界限、跨度和中点仍可能对应交叉朝向的不同边。
            var ax = Math.Min(a.A.X, a.B.X); var bx = Math.Min(b.A.X, b.B.X);
            c = ax.CompareTo(bx);
            if (c != 0) return c;
            c = Math.Min(a.A.Y, a.B.Y).CompareTo(Math.Min(b.A.Y, b.B.Y));
            if (c != 0) return c;
            return Math.Max(a.A.X, a.B.X).CompareTo(Math.Max(b.A.X, b.B.X));
        }

        private static bool Collinear(Point2D a, Point2D b, Point2D c)
        {
            var ab = Sub(b, a); var bc = Sub(c, b);
            return Dot(ab, bc) > 0 && Math.Abs(Cross(ab, bc)) <= Tol * Math.Max(Length(ab), Length(bc));
        }

        private static void EnsureSimple(Polygon polygon)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon.Vertices[i]; var b = polygon.Vertices[(i + 1) % polygon.Count];
                for (var j = i + 2; j < polygon.Count; j++)
                {
                    if (i == 0 && j == polygon.Count - 1) continue;
                    var c = polygon.Vertices[j]; var d = polygon.Vertices[(j + 1) % polygon.Count];
                    if (SegmentsIntersect(a, b, c, d))
                        throw new ArgumentException("边框轮廓自交或接触，不能可靠计算包边料");
                }
            }
        }

        private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d)
        {
            if (Math.Max(a.X, b.X) + Tol < Math.Min(c.X, d.X) ||
                Math.Max(c.X, d.X) + Tol < Math.Min(a.X, b.X) ||
                Math.Max(a.Y, b.Y) + Tol < Math.Min(c.Y, d.Y) ||
                Math.Max(c.Y, d.Y) + Tol < Math.Min(a.Y, b.Y)) return false;
            var ab = Sub(b, a); var cd = Sub(d, c);
            var t = Tol * Math.Max(1, Math.Max(Length(ab), Length(cd)));
            var s1 = Cross(ab, Sub(c, a)); var s2 = Cross(ab, Sub(d, a));
            var s3 = Cross(cd, Sub(a, c)); var s4 = Cross(cd, Sub(b, c));
            return !((s1 > t && s2 > t) || (s1 < -t && s2 < -t) ||
                (s3 > t && s4 > t) || (s3 < -t && s4 < -t));
        }

        internal static Point2D Sub(Point2D a, Point2D b) { return new Point2D(a.X - b.X, a.Y - b.Y); }
        internal static double Length(Point2D p) { return Math.Sqrt(Dot(p, p)); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Dot(Point2D a, Point2D b) { return a.X * b.X + a.Y * b.Y; }
        private static double Cross(Point2D a, Point2D b) { return a.X * b.Y - a.Y * b.X; }
    }
}
