using System.Collections.Generic;

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

    /// <summary>移植自 Python gridplate.geometry 的轮廓生成。</summary>
    public static class GeometryOutlines
    {
        private const double Eps = 1e-9;

        public static List<OutlinePoint> PlateOutline(PlateGeometry geo)
        {
            var p = geo.Plate;
            var points = new List<OutlinePoint> { new OutlinePoint(p.X0, p.Y0) };

            foreach (var notch in SortedByStart(geo.Notches, "top"))
            {
                points.Add(new OutlinePoint(notch.Clear.X0, p.Y0));
                points.Add(new OutlinePoint(notch.Clear.X0, notch.Clear.Y1));
                points.Add(new OutlinePoint(notch.Clear.X1, notch.Clear.Y1));
                points.Add(new OutlinePoint(notch.Clear.X1, p.Y0));
            }

            points.Add(new OutlinePoint(p.X1, p.Y0));

            foreach (var notch in SortedByStart(geo.Notches, "right"))
            {
                points.Add(new OutlinePoint(p.X1, notch.Clear.Y0));
                points.Add(new OutlinePoint(notch.Clear.X0, notch.Clear.Y0));
                points.Add(new OutlinePoint(notch.Clear.X0, notch.Clear.Y1));
                points.Add(new OutlinePoint(p.X1, notch.Clear.Y1));
            }

            points.Add(new OutlinePoint(p.X1, p.Y1));

            foreach (var notch in SortedByStart(geo.Notches, "bottom", true))
            {
                points.Add(new OutlinePoint(notch.Clear.X1, p.Y1));
                points.Add(new OutlinePoint(notch.Clear.X1, notch.Clear.Y0));
                points.Add(new OutlinePoint(notch.Clear.X0, notch.Clear.Y0));
                points.Add(new OutlinePoint(notch.Clear.X0, p.Y1));
            }

            points.Add(new OutlinePoint(p.X0, p.Y1));

            foreach (var notch in SortedByStart(geo.Notches, "left", true))
            {
                points.Add(new OutlinePoint(p.X0, notch.Clear.Y1));
                points.Add(new OutlinePoint(notch.Clear.X1, notch.Clear.Y1));
                points.Add(new OutlinePoint(notch.Clear.X1, notch.Clear.Y0));
                points.Add(new OutlinePoint(p.X0, notch.Clear.Y0));
            }

            return Dedupe(points);
        }

        public static List<OutlinePoint> NetOutline(PlateGeometry geo)
        {
            var n = geo.Net;
            var points = new List<OutlinePoint> { new OutlinePoint(n.X0, n.Y0) };

            foreach (var notch in SortedByStart(geo.Notches, "top"))
            {
                points.Add(new OutlinePoint(notch.Frame.X0, n.Y0));
                points.Add(new OutlinePoint(notch.Frame.X0, notch.Frame.Y1));
                points.Add(new OutlinePoint(notch.Frame.X1, notch.Frame.Y1));
                points.Add(new OutlinePoint(notch.Frame.X1, n.Y0));
            }

            points.Add(new OutlinePoint(n.X1, n.Y0));

            foreach (var notch in SortedByStart(geo.Notches, "right"))
            {
                points.Add(new OutlinePoint(n.X1, notch.Frame.Y0));
                points.Add(new OutlinePoint(notch.Frame.X0, notch.Frame.Y0));
                points.Add(new OutlinePoint(notch.Frame.X0, notch.Frame.Y1));
                points.Add(new OutlinePoint(n.X1, notch.Frame.Y1));
            }

            points.Add(new OutlinePoint(n.X1, n.Y1));

            foreach (var notch in SortedByStart(geo.Notches, "bottom", true))
            {
                points.Add(new OutlinePoint(notch.Frame.X1, n.Y1));
                points.Add(new OutlinePoint(notch.Frame.X1, notch.Frame.Y0));
                points.Add(new OutlinePoint(notch.Frame.X0, notch.Frame.Y0));
                points.Add(new OutlinePoint(notch.Frame.X0, n.Y1));
            }

            points.Add(new OutlinePoint(n.X0, n.Y1));

            foreach (var notch in SortedByStart(geo.Notches, "left", true))
            {
                points.Add(new OutlinePoint(n.X0, notch.Frame.Y1));
                points.Add(new OutlinePoint(notch.Frame.X1, notch.Frame.Y1));
                points.Add(new OutlinePoint(notch.Frame.X1, notch.Frame.Y0));
                points.Add(new OutlinePoint(n.X0, notch.Frame.Y0));
            }

            return Dedupe(points);
        }

        private static List<NotchGeo> SortedByStart(List<NotchGeo> notches, string edge, bool reverse = false)
        {
            var items = new List<NotchGeo>();
            foreach (var notch in notches)
            {
                if (notch.Source.Edge == edge)
                {
                    items.Add(notch);
                }
            }

            if (edge == "top" || edge == "bottom")
            {
                items.Sort((a, b) =>
                {
                    var result = a.Clear.X0.CompareTo(b.Clear.X0);
                    return reverse ? -result : result;
                });
            }
            else
            {
                items.Sort((a, b) =>
                {
                    var result = a.Clear.Y0.CompareTo(b.Clear.Y0);
                    return reverse ? -result : result;
                });
            }

            return items;
        }

        private static List<OutlinePoint> Dedupe(List<OutlinePoint> points)
        {
            var result = new List<OutlinePoint>();
            foreach (var point in points)
            {
                if (result.Count == 0
                    || System.Math.Abs(point.X - result[result.Count - 1].X) > Eps
                    || System.Math.Abs(point.Y - result[result.Count - 1].Y) > Eps)
                {
                    result.Add(point);
                }
            }

            if (result.Count > 1
                && System.Math.Abs(result[0].X - result[result.Count - 1].X) <= Eps
                && System.Math.Abs(result[0].Y - result[result.Count - 1].Y) <= Eps)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }
    }
}
