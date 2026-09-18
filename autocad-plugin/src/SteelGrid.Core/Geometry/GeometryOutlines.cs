using System;
using System.Collections.Generic;
using SteelGrid.Core.Layout;

namespace SteelGrid.Core.Geometry
{
    public struct OutlinePoint
    {
        public OutlinePoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }

    /// <summary>
    /// 生成“外接矩形 − 矩形空洞”的板件外轮廓和净空内轮廓。
    /// 把边界拆成带方向的轴对齐线段后按端点拼接，凹口和缺角都能正确处理。
    /// </summary>
    public static class GeometryOutlines
    {
        private const double Eps = 1e-9;
        private const double Round = 1e-6;

        private struct DirSeg
        {
            public DirSeg(OutlinePoint a, OutlinePoint b)
            {
                A = a;
                B = b;
            }

            public OutlinePoint A { get; }

            public OutlinePoint B { get; }
        }

        public static List<OutlinePoint> PlateOutline(PlateGeometry geo)
        {
            return WalkEdges(geo, geo.Plate, hole => hole.Clear);
        }

        public static List<OutlinePoint> NetOutline(PlateGeometry geo)
        {
            var net = geo.Net;
            return WalkEdges(geo, net, hole => hole.Frame.Clipped(net));
        }

        private static List<OutlinePoint> WalkEdges(
            PlateGeometry geo,
            Rect bounds,
            Func<NotchGeo, Rect> rectOf)
        {
            var segments = new List<DirSeg>();
            AddOuterEdge(segments, geo, bounds, rectOf, "top");
            AddOuterEdge(segments, geo, bounds, rectOf, "right");
            AddOuterEdge(segments, geo, bounds, rectOf, "bottom");
            AddOuterEdge(segments, geo, bounds, rectOf, "left");

            foreach (var hole in geo.Notches)
            {
                AddHoleWalls(segments, bounds, rectOf(hole).Clipped(bounds));
            }

            var next = new Dictionary<OutlinePoint, OutlinePoint>(new PointComparer());
            foreach (var segment in segments)
            {
                var key = Rounded(segment.A);
                if (!next.ContainsKey(key))
                {
                    next[key] = segment.B;
                }
            }

            var result = new List<OutlinePoint>();
            if (segments.Count == 0)
            {
                return result;
            }

            var start = Rounded(segments[0].A);
            var cursor = start;
            var guard = 0;
            do
            {
                result.Add(cursor);
                if (!next.TryGetValue(cursor, out var to))
                {
                    break;
                }

                cursor = Rounded(to);
                guard++;
                if (guard > segments.Count * 4 + 64)
                {
                    break;
                }
            }
            while (Distance(cursor, start) > Round * 2.0);

            return Dedupe(result);
        }

        private static void AddOuterEdge(
            List<DirSeg> segments,
            PlateGeometry geo,
            Rect bounds,
            Func<NotchGeo, Rect> rectOf,
            string edge)
        {
            var spans = OuterSpans(geo, bounds, rectOf, edge);
            if (edge == "top")
            {
                foreach (var span in spans)
                {
                    segments.Add(new DirSeg(
                        new OutlinePoint(span.A, bounds.Y0),
                        new OutlinePoint(span.B, bounds.Y0)));
                }
            }
            else if (edge == "right")
            {
                foreach (var span in spans)
                {
                    segments.Add(new DirSeg(
                        new OutlinePoint(bounds.X1, span.A),
                        new OutlinePoint(bounds.X1, span.B)));
                }
            }
            else if (edge == "bottom")
            {
                foreach (var span in spans)
                {
                    segments.Add(new DirSeg(
                        new OutlinePoint(span.B, bounds.Y1),
                        new OutlinePoint(span.A, bounds.Y1)));
                }
            }
            else
            {
                foreach (var span in spans)
                {
                    segments.Add(new DirSeg(
                        new OutlinePoint(bounds.X0, span.B),
                        new OutlinePoint(bounds.X0, span.A)));
                }
            }
        }

        private static List<Segment> OuterSpans(
            PlateGeometry geo,
            Rect bounds,
            Func<NotchGeo, Rect> rectOf,
            string edge)
        {
            var horizontal = edge == "top" || edge == "bottom";
            var full = horizontal
                ? new Segment(bounds.X0, bounds.X1)
                : new Segment(bounds.Y0, bounds.Y1);
            var cuts = new List<Segment>();

            foreach (var hole in geo.Notches)
            {
                var rect = rectOf(hole).Clipped(bounds);
                var touches = horizontal
                    ? (edge == "top"
                        ? Math.Abs(rect.Y0 - bounds.Y0) <= Round
                        : Math.Abs(rect.Y1 - bounds.Y1) <= Round)
                    : (edge == "left"
                        ? Math.Abs(rect.X0 - bounds.X0) <= Round
                        : Math.Abs(rect.X1 - bounds.X1) <= Round);
                if (!touches)
                {
                    continue;
                }

                cuts.Add(horizontal
                    ? new Segment(rect.X0, rect.X1)
                    : new Segment(rect.Y0, rect.Y1));
            }

            return SubtractSpans(full, cuts);
        }

        private static List<Segment> SubtractSpans(Segment full, List<Segment> cuts)
        {
            var spans = new List<Segment> { full };
            foreach (var cut in cuts)
            {
                var next = new List<Segment>();
                foreach (var span in spans)
                {
                    if (span.B <= cut.A + Round || span.A >= cut.B - Round)
                    {
                        next.Add(span);
                        continue;
                    }

                    if (span.A < cut.A - Round)
                    {
                        next.Add(new Segment(span.A, Math.Min(span.B, cut.A)));
                    }

                    if (cut.B < span.B - Round)
                    {
                        next.Add(new Segment(Math.Max(span.A, cut.B), span.B));
                    }
                }

                spans = next;
            }

            return spans;
        }

        private static void AddHoleWalls(List<DirSeg> segments, Rect bounds, Rect rect)
        {
            if (rect.W <= Round || rect.H <= Round)
            {
                return;
            }

            // 空洞贴住外边的那一侧是开口，不生成边界段；其余内壁按逆时针方向生成。
            if (Math.Abs(rect.Y0 - bounds.Y0) > Round)
            {
                segments.Add(new DirSeg(
                    new OutlinePoint(rect.X1, rect.Y0),
                    new OutlinePoint(rect.X0, rect.Y0)));
            }

            if (Math.Abs(rect.X0 - bounds.X0) > Round)
            {
                segments.Add(new DirSeg(
                    new OutlinePoint(rect.X0, rect.Y0),
                    new OutlinePoint(rect.X0, rect.Y1)));
            }

            if (Math.Abs(rect.Y1 - bounds.Y1) > Round)
            {
                segments.Add(new DirSeg(
                    new OutlinePoint(rect.X0, rect.Y1),
                    new OutlinePoint(rect.X1, rect.Y1)));
            }

            if (Math.Abs(rect.X1 - bounds.X1) > Round)
            {
                segments.Add(new DirSeg(
                    new OutlinePoint(rect.X1, rect.Y1),
                    new OutlinePoint(rect.X1, rect.Y0)));
            }
        }

        private static OutlinePoint Rounded(OutlinePoint point)
        {
            return new OutlinePoint(Math.Round(point.X, 6), Math.Round(point.Y, 6));
        }

        private static double Distance(OutlinePoint left, OutlinePoint right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class PointComparer : IEqualityComparer<OutlinePoint>
        {
            public bool Equals(OutlinePoint left, OutlinePoint right)
            {
                return Math.Abs(left.X - right.X) <= Round && Math.Abs(left.Y - right.Y) <= Round;
            }

            public int GetHashCode(OutlinePoint point)
            {
                unchecked
                {
                    var x = (long)Math.Round(point.X / Round);
                    var y = (long)Math.Round(point.Y / Round);
                    return (int)((x * 397) ^ y);
                }
            }
        }

        private static List<OutlinePoint> Dedupe(List<OutlinePoint> points)
        {
            var result = new List<OutlinePoint>();
            foreach (var point in points)
            {
                if (result.Count == 0
                    || Math.Abs(point.X - result[result.Count - 1].X) > Eps
                    || Math.Abs(point.Y - result[result.Count - 1].Y) > Eps)
                {
                    result.Add(point);
                }
            }

            if (result.Count > 1
                && Math.Abs(result[0].X - result[result.Count - 1].X) <= Eps
                && Math.Abs(result[0].Y - result[result.Count - 1].Y) <= Eps)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }
    }
}
