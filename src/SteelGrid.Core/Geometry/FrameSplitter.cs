using System;
using System.Collections.Generic;
using System.Linq;
using SteelGrid.Core.Layout;

namespace SteelGrid.Core.Geometry
{
    public sealed class FramePiece
    {
        public FramePiece(Rect rect, string direction)
        {
            Rect = rect;
            Direction = direction;
        }

        public Rect Rect { get; }

        public string Direction { get; }
    }

    /// <summary>
    /// 把“外接矩形 − 矩形空洞”的板件边框拆成一根根下料矩形，
    /// 并体现受力方向的包边顺序：垂直受力时水平边包垂直边，水平受力时垂直边包水平边。
    /// </summary>
    public static class FrameSplitter
    {
        private const double Eps = 1e-9;

        public static List<FramePiece> GetPieces(PlateGeometry geo)
        {
            var pieces = new List<FramePiece>();
            var main = MainBands(geo);
            foreach (var piece in main)
            {
                pieces.Add(piece);
            }

            foreach (var hole in geo.Notches)
            {
                foreach (var candidate in HoleCandidates(geo, hole))
                {
                    var remaining = SubtractRects(candidate, main.Select(item => item.Rect).ToList());
                    foreach (var rect in remaining)
                    {
                        var direction = Math.Abs(rect.H - geo.Spec.FrameT) <= Eps
                            ? "横向"
                            : "纵向";
                        pieces.Add(new FramePiece(rect, direction));
                    }
                }
            }

            return pieces;
        }

        private static List<FramePiece> MainBands(PlateGeometry geo)
        {
            var spec = geo.Spec;
            var t = spec.FrameT;
            var plate = geo.Plate;
            var w = plate.W;
            var h = plate.H;
            var verticalForce = spec.LoadDirection == Model.LoadDirection.Vertical;
            var pieces = new List<FramePiece>();

            if (verticalForce)
            {
                foreach (var span in SpansBetween(0.0, w, Cuts(geo, "top", false)))
                {
                    pieces.Add(new FramePiece(new Rect(span.A, 0.0, span.B, t), "横向"));
                }

                foreach (var span in SpansBetween(0.0, w, Cuts(geo, "bottom", false)))
                {
                    pieces.Add(new FramePiece(new Rect(span.A, h - t, span.B, h), "横向"));
                }

                foreach (var span in SpansBetween(t, h - t, Cuts(geo, "left", true)))
                {
                    pieces.Add(new FramePiece(new Rect(0.0, span.A, t, span.B), "纵向"));
                }

                foreach (var span in SpansBetween(t, h - t, Cuts(geo, "right", true)))
                {
                    pieces.Add(new FramePiece(new Rect(w - t, span.A, w, span.B), "纵向"));
                }
            }
            else
            {
                foreach (var span in SpansBetween(0.0, h, Cuts(geo, "left", false)))
                {
                    pieces.Add(new FramePiece(new Rect(0.0, span.A, t, span.B), "纵向"));
                }

                foreach (var span in SpansBetween(0.0, h, Cuts(geo, "right", false)))
                {
                    pieces.Add(new FramePiece(new Rect(w - t, span.A, w, span.B), "纵向"));
                }

                foreach (var span in SpansBetween(t, w - t, Cuts(geo, "top", true)))
                {
                    pieces.Add(new FramePiece(new Rect(span.A, 0.0, span.B, t), "横向"));
                }

                foreach (var span in SpansBetween(t, w - t, Cuts(geo, "bottom", true)))
                {
                    pieces.Add(new FramePiece(new Rect(span.A, h - t, span.B, h), "横向"));
                }
            }

            return pieces;
        }

        private static List<Segment> Cuts(PlateGeometry geo, string edge, bool frame)
        {
            var cuts = new List<Segment>();
            foreach (var hole in geo.Notches)
            {
                if (!Touches(hole, edge))
                {
                    continue;
                }

                if (edge == "top" || edge == "bottom")
                {
                    var x0 = frame ? hole.Clear.X0 - geo.Spec.FrameT : hole.Clear.X0;
                    var x1 = frame ? hole.Clear.X1 + geo.Spec.FrameT : hole.Clear.X1;
                    cuts.Add(new Segment(
                        Math.Max(0.0, x0),
                        Math.Min(geo.Plate.W, x1)));
                }
                else
                {
                    var y0 = frame ? hole.Clear.Y0 - geo.Spec.FrameT : hole.Clear.Y0;
                    var y1 = frame ? hole.Clear.Y1 + geo.Spec.FrameT : hole.Clear.Y1;
                    cuts.Add(new Segment(
                        Math.Max(0.0, y0),
                        Math.Min(geo.Plate.H, y1)));
                }
            }

            return cuts;
        }

        private static List<Segment> SpansBetween(double start, double end, List<Segment> cuts)
        {
            var spans = new List<Segment> { new Segment(start, end) };
            foreach (var cut in cuts)
            {
                var next = new List<Segment>();
                foreach (var span in spans)
                {
                    if (span.B <= cut.A + Eps || span.A >= cut.B - Eps)
                    {
                        next.Add(span);
                        continue;
                    }

                    if (span.A < cut.A - Eps)
                    {
                        next.Add(new Segment(span.A, cut.A));
                    }

                    if (cut.B < span.B - Eps)
                    {
                        next.Add(new Segment(cut.B, span.B));
                    }
                }

                spans = next;
            }

            return spans;
        }

        private static List<Rect> HoleCandidates(PlateGeometry geo, NotchGeo hole)
        {
            var t = geo.Spec.FrameT;
            var c = hole.Clear;
            var plate = geo.Plate;
            var verticalForce = geo.Spec.LoadDirection == Model.LoadDirection.Vertical;
            var candidates = new List<Rect>();

            // 水平内壁：垂直受力时水平边包垂直边，条带外扩一个边框厚；
            // 水平受力时垂直边包水平边，条带取净宽。
            if (!Touches(hole, "top"))
            {
                var x0 = verticalForce ? c.X0 - t : c.X0;
                var x1 = verticalForce ? c.X1 + t : c.X1;
                candidates.Add(new Rect(x0, c.Y0 - t, x1, c.Y0));
            }

            if (!Touches(hole, "bottom"))
            {
                var x0 = verticalForce ? c.X0 - t : c.X0;
                var x1 = verticalForce ? c.X1 + t : c.X1;
                candidates.Add(new Rect(x0, c.Y1, x1, c.Y1 + t));
            }

            // 垂直内壁：包边规则与水平内壁相反。
            if (!Touches(hole, "left"))
            {
                var y0 = verticalForce ? c.Y0 : c.Y0 - t;
                var y1 = verticalForce ? c.Y1 : c.Y1 + t;
                candidates.Add(new Rect(c.X0 - t, y0, c.X0, y1));
            }

            if (!Touches(hole, "right"))
            {
                var y0 = verticalForce ? c.Y0 : c.Y0 - t;
                var y1 = verticalForce ? c.Y1 : c.Y1 + t;
                candidates.Add(new Rect(c.X1, y0, c.X1 + t, y1));
            }

            var clipped = new List<Rect>();
            foreach (var rect in candidates)
            {
                var item = rect.Clipped(plate);
                if (item.W > Eps && item.H > Eps)
                {
                    clipped.Add(item);
                }
            }

            return clipped;
        }

        private static List<Rect> SubtractRects(Rect source, List<Rect> cuts)
        {
            var result = new List<Rect> { source };
            foreach (var cut in cuts)
            {
                var next = new List<Rect>();
                foreach (var rect in result)
                {
                    if (!Overlaps(rect, cut))
                    {
                        next.Add(rect);
                        continue;
                    }

                    if (rect.Y0 < cut.Y0 - Eps)
                    {
                        next.Add(new Rect(rect.X0, rect.Y0, rect.X1, Math.Min(rect.Y1, cut.Y0)));
                    }

                    if (cut.Y1 < rect.Y1 - Eps)
                    {
                        next.Add(new Rect(rect.X0, Math.Max(rect.Y0, cut.Y1), rect.X1, rect.Y1));
                    }

                    var low = Math.Max(rect.Y0, cut.Y0);
                    var high = Math.Min(rect.Y1, cut.Y1);
                    if (high > low + Eps)
                    {
                        if (rect.X0 < cut.X0 - Eps)
                        {
                            next.Add(new Rect(rect.X0, low, Math.Min(rect.X1, cut.X0), high));
                        }

                        if (cut.X1 < rect.X1 - Eps)
                        {
                            next.Add(new Rect(Math.Max(rect.X0, cut.X1), low, rect.X1, high));
                        }
                    }
                }

                result = next;
                if (result.Count == 0)
                {
                    break;
                }
            }

            return result;
        }

        private static bool Overlaps(Rect left, Rect right)
        {
            return left.X0 < right.X1 - Eps
                   && right.X0 < left.X1 - Eps
                   && left.Y0 < right.Y1 - Eps
                   && right.Y0 < left.Y1 - Eps;
        }

        private static bool Touches(NotchGeo hole, string edge)
        {
            return hole.Touches.Contains(edge);
        }
    }
}
