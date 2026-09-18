using System;
using System.Collections.Generic;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Layout
{
    public static class LayoutEngine
    {
        private const double Eps = 1e-9;

        public static LayoutResult Layout(Spec spec)
        {
            var geo = GeometryBuilder.Build(spec);
            var net = geo.Net;
            var vCenters = PlaceCenters(
                net.X0,
                net.X1,
                spec.Vertical.Thickness,
                spec.Vertical.Pitch);
            var hCenters = PlaceCenters(
                net.Y0,
                net.Y1,
                spec.Horizontal.Thickness,
                spec.Horizontal.Pitch);

            var verticalBars = new List<Bar>();
            for (var i = 0; i < vCenters.Count; i++)
            {
                var center = vCenters[i];
                var segments = VerticalSegments(geo, center, spec.Vertical.Thickness);
                var isFull = segments.Count == 1
                             && Math.Abs(segments[0].Length - net.H) <= Eps;
                var bar = new Bar("vertical", i + 1, center, spec.Vertical.Thickness, segments, isFull);
                SetWarnings(bar, geo);
                verticalBars.Add(bar);
            }

            var horizontalBars = new List<Bar>();
            for (var i = 0; i < hCenters.Count; i++)
            {
                var center = hCenters[i];
                var segments = HorizontalSegments(geo, center, spec.Horizontal.Thickness);
                var isFull = segments.Count == 1
                             && Math.Abs(segments[0].Length - net.W) <= Eps;
                var bar = new Bar("horizontal", i + 1, center, spec.Horizontal.Thickness, segments, isFull);
                SetWarnings(bar, geo);
                horizontalBars.Add(bar);
            }

            foreach (var bar in verticalBars)
            {
                SetHoleGroups(bar, horizontalBars);
            }

            foreach (var bar in horizontalBars)
            {
                SetHoleGroups(bar, verticalBars);
            }

            return new LayoutResult(geo, verticalBars, horizontalBars);
        }

        private static List<double> PlaceCenters(double lo, double hi, double thickness, double pitch)
        {
            var span = hi - lo;
            if (span <= Eps || thickness > span + Eps)
            {
                return new List<double>();
            }

            var count = Math.Max(1, (int)Math.Floor(span / pitch + Eps));
            var margin = (span - (count - 1) * pitch - thickness) / 2.0;
            if (margin < -Eps)
            {
                count = Math.Max(1, count - 1);
                margin = (span - (count - 1) * pitch - thickness) / 2.0;
            }

            var first = lo + margin + thickness / 2.0;
            var result = new List<double>();
            for (var i = 0; i < count; i++)
            {
                result.Add(first + i * pitch);
            }

            return result;
        }

        private static Tuple<double, double> Band(double center, double thickness)
        {
            return Tuple.Create(center - thickness / 2.0, center + thickness / 2.0);
        }

        private static bool NearInterval(Tuple<double, double> band, double lo, double hi, double clearance)
        {
            if (band.Item1 < hi - Eps && lo < band.Item2 - Eps)
            {
                return true;
            }

            if (band.Item2 < lo)
            {
                return lo - band.Item2 < clearance - Eps;
            }

            return band.Item1 - hi < clearance - Eps;
        }

        private static List<Segment> TruncateStart(List<Segment> segments, double limit)
        {
            var result = new List<Segment>();
            foreach (var segment in segments)
            {
                if (segment.B > limit + Eps)
                {
                    result.Add(new Segment(Math.Max(segment.A, limit), segment.B));
                }
            }

            return result;
        }

        private static List<Segment> TruncateEnd(List<Segment> segments, double limit)
        {
            var result = new List<Segment>();
            foreach (var segment in segments)
            {
                if (segment.A < limit - Eps)
                {
                    result.Add(new Segment(segment.A, Math.Min(segment.B, limit)));
                }
            }

            return result;
        }

        private static List<Segment> SubtractInterval(List<Segment> segments, double lo, double hi)
        {
            var result = new List<Segment>();
            foreach (var segment in segments)
            {
                if (segment.B <= lo + Eps || segment.A >= hi - Eps)
                {
                    result.Add(segment);
                    continue;
                }

                if (segment.A < lo - Eps)
                {
                    result.Add(new Segment(segment.A, lo));
                }

                if (hi < segment.B - Eps)
                {
                    result.Add(new Segment(hi, segment.B));
                }
            }

            return result;
        }

        private static List<Segment> VerticalSegments(PlateGeometry geo, double center, double thickness)
        {
            var net = geo.Net;
            var segments = new List<Segment> { new Segment(net.Y0, net.Y1) };
            var band = Band(center, thickness);

            foreach (var notch in geo.Notches)
            {
                if (notch.Source.Edge == "top"
                    && NearInterval(band, notch.Frame.X0, notch.Frame.X1, geo.Spec.FrameT))
                {
                    segments = TruncateStart(segments, notch.Frame.Y1);
                }
                else if (notch.Source.Edge == "bottom"
                         && NearInterval(band, notch.Frame.X0, notch.Frame.X1, geo.Spec.FrameT))
                {
                    segments = TruncateEnd(segments, notch.Frame.Y0);
                }
            }

            foreach (var notch in geo.Notches)
            {
                if (notch.Source.Edge == "left"
                    && NearInterval(band, notch.Frame.X0, notch.Frame.X1, geo.Spec.FrameT))
                {
                    segments = SubtractInterval(segments, notch.Frame.Y0, notch.Frame.Y1);
                }
                else if (notch.Source.Edge == "right"
                         && NearInterval(band, notch.Frame.X0, notch.Frame.X1, geo.Spec.FrameT))
                {
                    segments = SubtractInterval(segments, notch.Frame.Y0, notch.Frame.Y1);
                }
            }

            return segments;
        }

        private static List<Segment> HorizontalSegments(PlateGeometry geo, double center, double thickness)
        {
            var net = geo.Net;
            var segments = new List<Segment> { new Segment(net.X0, net.X1) };
            var band = Band(center, thickness);

            foreach (var notch in geo.Notches)
            {
                if (notch.Source.Edge == "left"
                    && NearInterval(band, notch.Frame.Y0, notch.Frame.Y1, geo.Spec.FrameT))
                {
                    segments = TruncateStart(segments, notch.Frame.X1);
                }
                else if (notch.Source.Edge == "right"
                         && NearInterval(band, notch.Frame.Y0, notch.Frame.Y1, geo.Spec.FrameT))
                {
                    segments = TruncateEnd(segments, notch.Frame.X0);
                }
            }

            foreach (var notch in geo.Notches)
            {
                if (notch.Source.Edge == "top"
                    && NearInterval(band, notch.Frame.Y0, notch.Frame.Y1, geo.Spec.FrameT))
                {
                    segments = SubtractInterval(segments, notch.Frame.X0, notch.Frame.X1);
                }
                else if (notch.Source.Edge == "bottom"
                         && NearInterval(band, notch.Frame.Y0, notch.Frame.Y1, geo.Spec.FrameT))
                {
                    segments = SubtractInterval(segments, notch.Frame.X0, notch.Frame.X1);
                }
            }

            return segments;
        }

        private static bool Covers(List<Segment> segments, double value)
        {
            foreach (var segment in segments)
            {
                if (segment.Contains(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetWarnings(Bar bar, PlateGeometry geo)
        {
            var edgeNames = new Dictionary<string, string>
            {
                { "top", "上" },
                { "bottom", "下" },
                { "left", "左" },
                { "right", "右" }
            };
            var clearance = geo.Spec.FrameT;
            bar.Warnings.Clear();

            foreach (var notch in geo.Notches)
            {
                bool nearEdge;
                bool crosses;
                string action;
                if (bar.Orientation == "vertical")
                {
                    nearEdge = (notch.Source.Edge == "top" || notch.Source.Edge == "bottom")
                               && NearInterval(
                                   Band(bar.Center, bar.Thickness),
                                   notch.Frame.X0,
                                   notch.Frame.X1,
                                   clearance);
                    crosses = (notch.Source.Edge == "left" || notch.Source.Edge == "right")
                              && NearInterval(
                                  Band(bar.Center, bar.Thickness),
                                  notch.Frame.X0,
                                  notch.Frame.X1,
                                  clearance);
                    action = notch.Source.Edge == "top" || notch.Source.Edge == "bottom"
                        ? "截短"
                        : "分段";
                }
                else
                {
                    nearEdge = (notch.Source.Edge == "left" || notch.Source.Edge == "right")
                               && NearInterval(
                                   Band(bar.Center, bar.Thickness),
                                   notch.Frame.Y0,
                                   notch.Frame.Y1,
                                   clearance);
                    crosses = (notch.Source.Edge == "top" || notch.Source.Edge == "bottom")
                              && NearInterval(
                                  Band(bar.Center, bar.Thickness),
                                  notch.Frame.Y0,
                                  notch.Frame.Y1,
                                  clearance);
                    action = notch.Source.Edge == "top" || notch.Source.Edge == "bottom"
                        ? "分段"
                        : "截短";
                }

                if (nearEdge || crosses)
                {
                    var text = $"距{edgeNames[notch.Source.Edge]}缺口边框不足{clearance:0.##}mm，已{action}";
                    if (!bar.Warnings.Contains(text))
                    {
                        bar.Warnings.Add(text);
                    }
                }
            }
        }

        private static void SetHoleGroups(Bar bar, List<Bar> crossBars)
        {
            bar.HoleGroups.Clear();
            foreach (var segment in bar.Segments)
            {
                var holes = new List<double>();
                foreach (var cross in crossBars)
                {
                    if (segment.Contains(cross.Center) && Covers(cross.Segments, bar.Center))
                    {
                        holes.Add(cross.Center);
                    }
                }

                bar.HoleGroups.Add(holes);
            }
        }
    }
}
