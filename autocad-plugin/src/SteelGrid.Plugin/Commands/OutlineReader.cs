using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelGrid.Core.Model;

namespace SteelGrid.Plugin.Commands
{
    /// <summary>从闭合多段线识别板件尺寸和边缘矩形缺口。</summary>
    public static class OutlineReader
    {
        private const double Eps = 1e-6;

        private struct Run
        {
            public Run(string kind, Point2d start, Point2d end)
            {
                Kind = kind;
                Start = start;
                End = end;
            }

            public string Kind { get; }

            public Point2d Start { get; }

            public Point2d End { get; }

            public double LoX => Math.Min(Start.X, End.X);

            public double HiX => Math.Max(Start.X, End.X);

            public double LoY => Math.Min(Start.Y, End.Y);

            public double HiY => Math.Max(Start.Y, End.Y);
        }

        public static List<Notch> ReadNotches(Polyline outline)
        {
            var points = new List<Point2d>();
            for (var i = 0; i < outline.NumberOfVertices; i++)
            {
                var point = outline.GetPoint2dAt(i);
                if (points.Count == 0 || Distance(point, points[points.Count - 1]) > Eps)
                {
                    points.Add(point);
                }
            }

            if (points.Count > 1 && Distance(points[0], points[points.Count - 1]) <= Eps)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 3)
            {
                return new List<Notch>();
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var point in points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var normalized = new List<Point2d>();
            foreach (var point in points)
            {
                // AutoCAD 的 Y 轴向上；排条算法的“top”在局部 Y=0，因此这里做 Y 轴翻转。
                normalized.Add(new Point2d(point.X - minX, maxY - point.Y));
            }

            var runs = ClassifyRuns(BuildRuns(normalized), width, height);
            var notches = new List<Notch>();
            ReadHorizontalEdge(runs, "top", width, height, notches);
            ReadHorizontalEdge(runs, "bottom", width, height, notches);
            ReadVerticalEdge(runs, "left", width, height, notches);
            ReadVerticalEdge(runs, "right", width, height, notches);
            return MergeNotches(notches, width, height);
        }

        private static List<Notch> MergeNotches(List<Notch> notches, double plateW, double plateH)
        {
            var merged = new List<Notch>();
            foreach (var notch in notches)
            {
                var key = RectKey(notch, plateW, plateH);
                var match = merged.Find(item => RectKey(item, plateW, plateH) == key);
                if (match == null)
                {
                    merged.Add(notch);
                }
                else
                {
                    foreach (var touch in notch.Touches)
                    {
                        AddTouch(match, touch);
                    }
                }
            }

            merged.Sort((a, b) =>
            {
                var result = string.Compare(a.Edge, b.Edge, StringComparison.Ordinal);
                return result != 0 ? result : a.Start.CompareTo(b.Start);
            });
            return merged;
        }

        private static string RectKey(Notch notch, double plateW, double plateH)
        {
            double x0;
            double y0;
            double x1;
            double y1;
            switch (notch.Edge)
            {
                case "top":
                    x0 = notch.Start;
                    x1 = notch.Start + notch.Width;
                    y0 = 0.0;
                    y1 = notch.Depth;
                    break;
                case "bottom":
                    x0 = notch.Start;
                    x1 = notch.Start + notch.Width;
                    y0 = plateH - notch.Depth;
                    y1 = plateH;
                    break;
                case "left":
                    x0 = 0.0;
                    x1 = notch.Depth;
                    y0 = notch.Start;
                    y1 = notch.Start + notch.Width;
                    break;
                default:
                    x0 = plateW - notch.Depth;
                    x1 = plateW;
                    y0 = notch.Start;
                    y1 = notch.Start + notch.Width;
                    break;
            }

            return Math.Round(x0, 3).ToString("R") + "|"
                   + Math.Round(y0, 3).ToString("R") + "|"
                   + Math.Round(x1, 3).ToString("R") + "|"
                   + Math.Round(y1, 3).ToString("R");
        }

        private static void AddTouch(Notch notch, string edge)
        {
            if (edge != null && !notch.Touches.Contains(edge))
            {
                notch.Touches.Add(edge);
            }
        }

        private static double Distance(Point2d left, Point2d right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static List<Run> BuildRuns(List<Point2d> points)
        {
            var runs = new List<Run>();
            var start = points[0];
            var current = points[0];

            for (var i = 1; i <= points.Count; i++)
            {
                var next = points[i % points.Count];
                var dx = next.X - current.X;
                var dy = next.Y - current.Y;
                if (Math.Abs(dx) <= Eps && Math.Abs(dy) <= Eps)
                {
                    continue;
                }

                var horizontal = Math.Abs(dx) >= Math.Abs(dy);
                var sign = horizontal ? (dx >= 0.0 ? "+" : "-") : (dy >= 0.0 ? "+" : "-");
                var kind = (horizontal ? "h" : "v") + sign;
                if (runs.Count == 0)
                {
                    runs.Add(new Run(kind, start, next));
                }
                else
                {
                    var previous = runs[runs.Count - 1];
                    var previousHorizontal = previous.Kind.StartsWith("h", StringComparison.Ordinal);
                    var previousSign = previous.Kind.EndsWith("+", StringComparison.Ordinal) ? "+" : "-";
                    if (previousHorizontal == horizontal && previousSign == sign)
                    {
                        runs[runs.Count - 1] = new Run(previous.Kind, previous.Start, next);
                    }
                    else
                    {
                        runs.Add(new Run(kind, current, next));
                    }
                }

                current = next;
            }

            return runs;
        }

        private static List<Run> ClassifyRuns(List<Run> runs, double width, double height)
        {
            var tolerance = Math.Max(Eps, Math.Min(width, height) * 1e-6);
            var result = new List<Run>();
            foreach (var run in runs)
            {
                if (run.Kind.StartsWith("h", StringComparison.Ordinal))
                {
                    var y = (run.Start.Y + run.End.Y) / 2.0;
                    // OutlineReader 已将 CAD 坐标转换为算法坐标：Y=0 是 top，Y=height 是 bottom。
                    var kind = Math.Abs(y) <= tolerance
                        ? "top"
                        : Math.Abs(y - height) <= tolerance ? "bottom" : "inner_h";
                    result.Add(new Run(kind, run.Start, run.End));
                }
                else
                {
                    var x = (run.Start.X + run.End.X) / 2.0;
                    var kind = Math.Abs(x - width) <= tolerance
                        ? "right"
                        : Math.Abs(x) <= tolerance ? "left" : "inner_v";
                    result.Add(new Run(kind, run.Start, run.End));
                }
            }

            return result;
        }

        private static void ReadHorizontalEdge(List<Run> runs, string edge, double width, double height, List<Notch> notches)
        {
            var outer = runs.FindAll(run => run.Kind == edge);
            outer.Sort((a, b) => a.LoX.CompareTo(b.LoX));

            var spans = new List<Tuple<double, double>>();
            var start = 0.0;
            for (var i = 0; i < outer.Count; i++)
            {
                var item = outer[i];
                if (item.LoX > start + Eps)
                {
                    spans.Add(Tuple.Create(start, item.LoX));
                }

                start = Math.Max(start, item.HiX);
            }

            var spanW = width;
            if (start < spanW - Eps)
            {
                spans.Add(Tuple.Create(start, spanW));
            }

            foreach (var item in spans)
            {
                var lo = item.Item1;
                var hi = item.Item2;
                Run inner;
                if (hi <= lo + Eps || !TryFindRun(runs, "inner_h", lo, hi, out inner))
                {
                    continue;
                }

                var middleY = (inner.Start.Y + inner.End.Y) / 2.0;
                var depth = edge == "top" ? middleY : height - middleY;
                var notch = new Notch(edge, lo, hi - lo, depth);
                notch.Touches.Add(edge);
                notches.Add(notch);
            }
        }

        private static void ReadVerticalEdge(List<Run> runs, string edge, double width, double height, List<Notch> notches)
        {
            var outer = runs.FindAll(run => run.Kind == edge);
            outer.Sort((a, b) => a.LoY.CompareTo(b.LoY));

            var spans = new List<Tuple<double, double>>();
            var start = 0.0;
            for (var i = 0; i < outer.Count; i++)
            {
                var item = outer[i];
                if (item.LoY > start + Eps)
                {
                    spans.Add(Tuple.Create(start, item.LoY));
                }

                start = Math.Max(start, item.HiY);
            }

            var spanH = height;
            if (start < spanH - Eps)
            {
                spans.Add(Tuple.Create(start, spanH));
            }

            foreach (var item in spans)
            {
                var lo = item.Item1;
                var hi = item.Item2;
                Run inner;
                if (hi <= lo + Eps || !TryFindRun(runs, "inner_v", lo, hi, out inner))
                {
                    continue;
                }

                var middleX = (inner.Start.X + inner.End.X) / 2.0;
                var depth = edge == "left" ? middleX : width - middleX;
                // 角部空洞已由水平边识别，垂直边检测只保留不贴顶/底边的左/右空洞。
                if (lo <= Eps || hi >= height - Eps)
                {
                    continue;
                }

                var notch = new Notch(edge, lo, hi - lo, depth);
                notch.Touches.Add(edge);
                notches.Add(notch);
            }
        }

        private static bool TryFindRun(List<Run> runs, string kind, double lo, double hi, out Run found)
        {
            foreach (var run in runs)
            {
                if (run.Kind != kind)
                {
                    continue;
                }

                var a = kind == "inner_h" ? run.LoX : run.LoY;
                var b = kind == "inner_h" ? run.HiX : run.HiY;
                if (Math.Abs(a - lo) <= 1e-4 && Math.Abs(b - hi) <= 1e-4)
                {
                    found = run;
                    return true;
                }
            }

            found = new Run();
            return false;
        }
    }
}
