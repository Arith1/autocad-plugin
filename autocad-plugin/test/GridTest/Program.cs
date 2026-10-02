using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;
using SteelGrid.Core.Report;

namespace GridTest
{
    internal static class Program
    {
        private const double Eps = 1e-6;

        private static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--geom")
                {
                    Environment.ExitCode = PolygonSelfTest.Run() ? 0 : 1;
                    return;
                }

                if (args.Length > 0 && args[0] == "--outline")
                {
                    var outlinePath = args.Length > 1
                        ? args[1]
                        : @"C:\Users\Miyna\Desktop\待排条\异形图.dxf";
                    Environment.ExitCode = OutlineTest.Run(outlinePath);
                    return;
                }

                var path = args.Length > 0 ? args[0] : @"C:\Users\Miyna\Desktop\待排条\三个图形.dxf";
                var outlines = DxfReader.Read(path);
                Console.WriteLine("outlines: " + outlines.Count);

            for (var i = 0; i < outlines.Count; i++)
            {
                var points = ToPoints(outlines[i].Points);
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var point in points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }

                var w = maxX - minX;
                var h = maxY - minY;
                var normalized = new List<Pt>();
                foreach (var point in points)
                {
                    normalized.Add(new Pt(point.X - minX, maxY - point.Y));
                }

                var notches = DetectNotches(normalized, w, h);
                Console.WriteLine("--- " + i + ": W=" + F(w) + " H=" + F(h));
                foreach (var notch in notches)
                {
                    Console.WriteLine("    hole " + notch.Edge + " start=" + F(notch.Start)
                        + " width=" + F(notch.Width) + " depth=" + F(notch.Depth)
                        + " touches=" + string.Join(",", notch.Touches));
                }

                var spec = new Spec
                {
                    OpeningW = w,
                    OpeningH = h,
                    Shrink = 5.0,
                    FrameT = 5.0,
                    Vertical = new BarSpec(BarType.Flat, 5.0, 36.85),
                    Horizontal = new BarSpec(BarType.Flat, 5.0, 36.85),
                    Notches = notches
                };

                foreach (var vertical in new[] { true, false })
                {
                    spec.LoadDirection = vertical ? LoadDirection.Vertical : LoadDirection.Horizontal;
                    try
                    {
                        var result = LayoutEngine.Layout(spec);
                        var pieces = FrameSplitter.GetPieces(result.Geometry);
                        var outline = GeometryOutlines.PlateOutline(result.Geometry);
                        Console.WriteLine("    outline points: " + outline.Count);
                        Console.WriteLine("      " + string.Join(" ", outline.ConvertAll(p => F(p.X) + "," + F(p.Y))));
                        Console.WriteLine("    frame " + (vertical ? "垂直" : "水平") + ": " + pieces.Count);
                        foreach (var piece in pieces)
                        {
                            var r = piece.Rect;
                            Console.WriteLine("      " + piece.Direction + " " + F(r.W) + "x" + F(r.H)
                                + " @(" + F(r.X0) + "," + F(r.Y0) + ")");
                        }

                        var frameRows = ReportTables.FrameTable(result, pieces);
                        Console.WriteLine("    frame table rows: " + frameRows.Count);
                        for (var r = 0; r < frameRows.Count; r++)
                        {
                            Console.WriteLine("      #" + (r + 1) + " " + frameRows[r].Direction + " "
                                + F(frameRows[r].Length) + " x" + frameRows[r].Count);
                        }

                        foreach (var direction in new[] { "纵向", "横向" })
                        {
                            var rows = ReportTables.ReportTable(result, direction);
                            Console.WriteLine("    " + direction + " table rows: " + rows.Count);
                            foreach (var row in rows)
                            {
                                Console.WriteLine("      " + row.Spec + " len=" + F(row.Length) + " x" + row.Count
                                    + " first=" + row.FirstHole + " last=" + row.LastHole + " holes=" + row.Holes);
                            }
                        }

                        var annotations = ReportTables.HoleAnnotations(result);
                        Console.WriteLine("    hole annotations: " + annotations.Count);
                        foreach (var annotation in annotations)
                        {
                            Console.WriteLine("      " + annotation.Direction + " len=" + F(annotation.Length)
                                + " first=" + F(annotation.FirstHole) + " last=" + F(annotation.LastHole));
                        }

                        foreach (var direction in new[] { "纵向", "横向" })
                        {
                            var bars = direction == "纵向" ? result.VerticalBars : result.HorizontalBars;
                            var rows = ReportTables.ReportTable(result, direction);
                            foreach (var row in rows)
                            {
                                var exact = 0;
                                foreach (var bar in bars)
                                {
                                    for (var s = 0; s < bar.Segments.Count; s++)
                                    {
                                        var segment = bar.Segments[s];
                                        var holes = bar.HoleGroups[s];
                                        if (holes.Count == 0 || Math.Abs(segment.Length - row.Length) > 1e-6)
                                        {
                                            continue;
                                        }

                                        var first = Math.Round(holes[0] - segment.A, 6);
                                        var last = Math.Round(segment.B - holes[holes.Count - 1], 6);
                                        if (ReportTables.Format(first) == row.FirstHole
                                            && ReportTables.Format(last) == row.LastHole)
                                        {
                                            exact++;
                                        }
                                    }
                                }

                                Console.WriteLine("    exact-match " + direction + " len=" + F(row.Length)
                                    + " first=" + row.FirstHole + " last=" + row.LastHole + " -> " + exact);

                                if (exact == 0)
                                {
                                    var combos = new Dictionary<string, int>();
                                    foreach (var bar in bars)
                                    {
                                        for (var s = 0; s < bar.Segments.Count; s++)
                                        {
                                            var segment = bar.Segments[s];
                                            var holes = bar.HoleGroups[s];
                                            if (holes.Count == 0 || Math.Abs(segment.Length - row.Length) > 1e-6)
                                            {
                                                continue;
                                            }

                                            var first = Math.Round(holes[0] - segment.A, 6);
                                            var last = Math.Round(segment.B - holes[holes.Count - 1], 6);
                                            var combo = ReportTables.Format(first) + "/" + ReportTables.Format(last);
                                            if (!combos.ContainsKey(combo))
                                            {
                                                combos[combo] = 0;
                                            }

                                            combos[combo]++;
                                        }
                                    }

                                    foreach (var combo in combos)
                                    {
                                        Console.WriteLine("      raw combos " + combo.Key + " x" + combo.Value);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("    error: " + ex.Message);
                    }
                }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("fatal: " + ex.GetType().Name + " " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Environment.ExitCode = 1;
            }
        }

        private static string F(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private sealed class Pt
        {
            public Pt(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }

            public double Y { get; }
        }

        private static List<Pt> ToPoints(List<Point2D> points)
        {
            var result = new List<Pt>();
            foreach (var point in points)
            {
                result.Add(new Pt(point.X, point.Y));
            }

            return result;
        }

        private sealed class Run
        {
            public Run(string kind, Pt start, Pt end)
            {
                Kind = kind;
                Start = start;
                End = end;
            }

            public string Kind { get; }

            public Pt Start { get; }

            public Pt End { get; }

            public double LoX => Math.Min(Start.X, End.X);

            public double HiX => Math.Max(Start.X, End.X);

            public double LoY => Math.Min(Start.Y, End.Y);

            public double HiY => Math.Max(Start.Y, End.Y);
        }

        private static List<Notch> DetectNotches(List<Pt> points, double width, double height)
        {
            var runs = ClassifyRuns(BuildRuns(points), width, height);
            var notches = new List<Notch>();
            ReadHorizontalEdge(runs, "top", width, height, notches);
            ReadHorizontalEdge(runs, "bottom", width, height, notches);
            ReadVerticalEdge(runs, "left", width, height, notches);
            ReadVerticalEdge(runs, "right", width, height, notches);

            var merged = new List<Notch>();
            foreach (var notch in notches)
            {
                var key = RectKey(notch, width, height);
                var match = merged.Find(item => RectKey(item, width, height) == key);
                if (match == null)
                {
                    merged.Add(notch);
                }
                else
                {
                    foreach (var touch in notch.Touches)
                    {
                        if (!match.Touches.Contains(touch))
                        {
                            match.Touches.Add(touch);
                        }
                    }
                }
            }

            return merged;
        }

        private static string RectKey(Notch notch, double width, double height)
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
                    y0 = height - notch.Depth;
                    y1 = height;
                    break;
                case "left":
                    x0 = 0.0;
                    x1 = notch.Depth;
                    y0 = notch.Start;
                    y1 = notch.Start + notch.Width;
                    break;
                default:
                    x0 = width - notch.Depth;
                    x1 = width;
                    y0 = notch.Start;
                    y1 = notch.Start + notch.Width;
                    break;
            }

            return Math.Round(x0, 3).ToString("R") + "|"
                   + Math.Round(y0, 3).ToString("R") + "|"
                   + Math.Round(x1, 3).ToString("R") + "|"
                   + Math.Round(y1, 3).ToString("R");
        }

        private static List<Run> BuildRuns(List<Pt> points)
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

            if (start < width - Eps)
            {
                spans.Add(Tuple.Create(start, width));
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

            if (start < height - Eps)
            {
                spans.Add(Tuple.Create(start, height));
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

            found = null;
            return false;
        }
    }
}
