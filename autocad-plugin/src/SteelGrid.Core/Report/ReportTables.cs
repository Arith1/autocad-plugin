using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Report
{
    public sealed class ReportItem
    {
        public string Spec { get; set; }

        public double Length { get; set; }

        public string Direction { get; set; }

        public int Count { get; set; }

        public string FirstHole { get; set; } = "";

        public string LastHole { get; set; } = "";

        public string Holes { get; set; } = "0";
    }

    public sealed class HoleAnnotation
    {
        public HoleAnnotation(
            string direction,
            double length,
            double firstHole,
            double lastHole,
            double barCenter,
            double barThickness,
            double segmentA,
            double segmentB,
            double firstPosition,
            double lastPosition)
        {
            Direction = direction;
            Length = length;
            FirstHole = firstHole;
            LastHole = lastHole;
            BarCenter = barCenter;
            BarThickness = barThickness;
            SegmentA = segmentA;
            SegmentB = segmentB;
            FirstPosition = firstPosition;
            LastPosition = lastPosition;
        }

        public string Direction { get; }

        public double Length { get; }

        public double FirstHole { get; }

        public double LastHole { get; }

        public double BarCenter { get; }

        public double BarThickness { get; }

        public double SegmentA { get; }

        public double SegmentB { get; }

        public double FirstPosition { get; }

        public double LastPosition { get; }
    }

    public static class ReportTables
    {
        private const double Eps = 1e-6;

        private sealed class Variant
        {
            public Variant(double? first, double? last, int holes)
            {
                First = first;
                Last = last;
                Holes = holes;
            }

            public double? First { get; }

            public double? Last { get; }

            public int Holes { get; }
        }

        private sealed class GroupState
        {
            public GroupState(string direction, string cutType, double length)
            {
                Direction = direction;
                CutType = cutType;
                Length = length;
                Indices = new List<string>();
                Variants = new List<Variant>();
            }

            public string Direction { get; }

            public string CutType { get; }

            public double Length { get; }

            public List<string> Indices { get; }

            public List<Variant> Variants { get; }
        }

        public static string Format(double value)
        {
            var text = value.ToString("0.00", CultureInfo.InvariantCulture)
                .TrimEnd('0')
                .TrimEnd('.');
            return string.IsNullOrEmpty(text) ? "0" : text;
        }

        public static List<ReportItem> FrameTable(LayoutResult result)
        {
            return FrameTable(result, FrameSplitter.GetPieces(result.Geometry));
        }

        public static List<ReportItem> FrameTable(LayoutResult result, List<FramePiece> framePieces)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<ReportItem>();
            foreach (var piece in framePieces)
            {
                AddFramePiece(counts, order, piece.Direction, piece.Rect.W > piece.Rect.H ? piece.Rect.W : piece.Rect.H);
            }

            var items = new List<ReportItem>();
            foreach (var item in order)
            {
                items.Add(new ReportItem
                {
                    Spec = "边框",
                    Length = item.Length,
                    Direction = item.Direction,
                    Count = counts[item.Direction + "|" + Format(item.Length)]
                });
            }

            return items;
        }

        public static List<ReportItem> ReportTable(LayoutResult result, string directionFilter)
        {
            var geo = result.Geometry;
            var states = new Dictionary<string, GroupState>();
            var order = new List<GroupState>();
            AddBars(result.VerticalBars, "纵向", geo.Net.H, directionFilter, states, order);
            AddBars(result.HorizontalBars, "横向", geo.Net.W, directionFilter, states, order);

            var items = new List<ReportItem>();
            foreach (var state in order)
            {
                var first = JoinNumbers(state.Variants, true);
                var last = JoinNumbers(state.Variants, false);
                var holes = JoinHoles(state.Variants);
                items.Add(new ReportItem
                {
                    Spec = state.CutType,
                    Length = state.Length,
                    Direction = state.Direction,
                    Count = state.Indices.Count,
                    FirstHole = first,
                    LastHole = last,
                    Holes = holes
                });
            }

            return items;
        }

        public static List<HoleAnnotation> HoleAnnotations(LayoutResult result)
        {
            var annotations = new List<HoleAnnotation>();
            AddHoleAnnotations(result, result.VerticalBars, "纵向", annotations);
            AddHoleAnnotations(result, result.HorizontalBars, "横向", annotations);
            return annotations;
        }

        private static void AddHoleAnnotations(
            LayoutResult result,
            List<Bar> bars,
            string direction,
            List<HoleAnnotation> annotations)
        {
            foreach (var row in ReportTable(result, direction))
            {
                if (string.IsNullOrEmpty(row.FirstHole) && string.IsNullOrEmpty(row.LastHole))
                {
                    continue;
                }

                var candidates = new List<Tuple<Bar, Segment, List<double>>>();
                foreach (var bar in bars)
                {
                    for (var i = 0; i < bar.Segments.Count; i++)
                    {
                        var segment = bar.Segments[i];
                        var holes = bar.HoleGroups[i];
                        if (holes.Count > 0 && Math.Abs(segment.Length - row.Length) <= 1e-6)
                        {
                            candidates.Add(Tuple.Create(bar, segment, holes));
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                // 和 Python 逻辑一致：同一规格有多种首尾孔距时，取数量最多的一组。
                var counts = new Dictionary<string, int>();
                var order = new List<Tuple<double, double>>();
                foreach (var candidate in candidates)
                {
                    var first = Math.Round(candidate.Item3[0] - candidate.Item2.A, 6);
                    var last = Math.Round(candidate.Item2.B - candidate.Item3[candidate.Item3.Count - 1], 6);
                    var key = first.ToString("R", CultureInfo.InvariantCulture) + "|" + last.ToString("R", CultureInfo.InvariantCulture);
                    if (!counts.ContainsKey(key))
                    {
                        counts[key] = 0;
                        order.Add(Tuple.Create(first, last));
                    }

                    counts[key]++;
                }

                var selected = order[0];
                var selectedCount = counts[selected.Item1.ToString("R", CultureInfo.InvariantCulture) + "|" + selected.Item2.ToString("R", CultureInfo.InvariantCulture)];
                foreach (var item in order)
                {
                    var count = counts[item.Item1.ToString("R", CultureInfo.InvariantCulture) + "|" + item.Item2.ToString("R", CultureInfo.InvariantCulture)];
                    if (count > selectedCount)
                    {
                        selected = item;
                        selectedCount = count;
                    }
                }

                foreach (var candidate in candidates)
                {
                    var first = Math.Round(candidate.Item3[0] - candidate.Item2.A, 6);
                    var last = Math.Round(candidate.Item2.B - candidate.Item3[candidate.Item3.Count - 1], 6);
                    if (first == selected.Item1 && last == selected.Item2)
                    {
                        annotations.Add(new HoleAnnotation(
                            direction,
                            row.Length,
                            first,
                            last,
                            candidate.Item1.Center,
                            candidate.Item1.Thickness,
                            candidate.Item2.A,
                            candidate.Item2.B,
                            candidate.Item3[0],
                            candidate.Item3[candidate.Item3.Count - 1]));
                        break;
                    }
                }
            }
        }

        private static void AddBars(
            List<Bar> bars,
            string direction,
            double netSpan,
            string directionFilter,
            Dictionary<string, GroupState> states,
            List<GroupState> order)
        {
            if (directionFilter != null && direction != directionFilter)
            {
                return;
            }

            foreach (var bar in bars)
            {
                var cutType = GetCutType(bar, netSpan);
                for (var i = 0; i < bar.Segments.Count; i++)
                {
                    var segment = bar.Segments[i];
                    var holes = bar.HoleGroups[i];
                    double? first = holes.Count > 0 ? holes[0] - segment.A : (double?)null;
                    double? last = holes.Count > 0 ? segment.B - holes[holes.Count - 1] : (double?)null;
                    var key = direction + "|" + cutType + "|" + Math.Round(segment.Length, 6).ToString("R", CultureInfo.InvariantCulture);
                    GroupState state;
                    if (!states.TryGetValue(key, out state))
                    {
                        state = new GroupState(direction, cutType, segment.Length);
                        states[key] = state;
                        order.Add(state);
                    }

                    state.Indices.Add(bar.Index.ToString(CultureInfo.InvariantCulture));
                    state.Variants.Add(new Variant(first, last, holes.Count));
                }
            }
        }

        private static string GetCutType(Bar bar, double netSpan)
        {
            if (bar.Segments.Count == 1 && Math.Abs(bar.Segments[0].Length - netSpan) <= Eps)
            {
                return bar.Orientation == "vertical" ? "满长" : "整长";
            }

            if (bar.Orientation == "vertical" && bar.Segments.Count == 1)
            {
                return "短条";
            }

            return "分段";
        }

        private static void AddFramePiece(
            Dictionary<string, int> counts,
            List<ReportItem> order,
            string direction,
            double length)
        {
            if (length <= 1e-9)
            {
                return;
            }

            var rounded = Math.Round(length, 6);
            var key = direction + "|" + Format(rounded);
            if (!counts.ContainsKey(key))
            {
                counts[key] = 0;
                order.Add(new ReportItem
                {
                    Spec = "边框",
                    Length = rounded,
                    Direction = direction
                });
            }

            counts[key]++;
        }

        private static string JoinNumbers(List<Variant> variants, bool first)
        {
            var texts = new List<string>();
            foreach (var variant in variants)
            {
                var value = first ? variant.First : variant.Last;
                if (value.HasValue)
                {
                    AddUnique(texts, Format(value.Value));
                }
            }

            return string.Join("/", texts);
        }

        private static string JoinHoles(List<Variant> variants)
        {
            var texts = new List<string>();
            foreach (var variant in variants)
            {
                AddUnique(texts, variant.Holes.ToString(CultureInfo.InvariantCulture));
            }

            return texts.Count == 0 ? "0" : string.Join("/", texts);
        }

        private static void AddUnique(List<string> texts, string text)
        {
            if (!string.IsNullOrEmpty(text) && !texts.Contains(text))
            {
                texts.Add(text);
            }
        }
    }
}
