using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Geometry
{
    /// <summary>局部坐标中的精确圆弧，正 sweep 为逆时针。</summary>
    public sealed class CircularArc
    {
        public CircularArc(Point2D center, double radius, double startAngle, double sweepAngle)
        {
            if (radius <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            SweepAngle = sweepAngle;
        }

        public Point2D Center { get; }

        public double Radius { get; }

        public double StartAngle { get; }

        public double SweepAngle { get; }

        public Point2D Start => PointAt(0.0);

        public Point2D End => PointAt(1.0);

        public double Length => Radius * Math.Abs(SweepAngle);

        public Point2D PointAt(double fraction)
        {
            var angle = StartAngle + SweepAngle * fraction;
            return new Point2D(
                Center.X + Radius * Math.Cos(angle),
                Center.Y + Radius * Math.Sin(angle));
        }

        /// <summary>按最大弦高误差离散，仅供现有直线多边形算法裁剪和检查。</summary>
        public List<Point2D> Tessellate(double maxSagitta)
        {
            var points = new List<Point2D>();
            var tolerance = Math.Max(1e-4, maxSagitta);
            var maxAngle = Radius <= tolerance
                ? Math.PI / 18.0
                : 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - tolerance / Radius)));
            maxAngle = Math.Min(maxAngle, Math.PI / 18.0);
            if (maxAngle <= 1e-6)
            {
                maxAngle = Math.PI / 180.0;
            }

            var count = Math.Max(1, (int)Math.Ceiling(Math.Abs(SweepAngle) / maxAngle));
            for (var i = 0; i <= count; i++)
            {
                points.Add(PointAt((double)i / count));
            }

            return points;
        }

        /// <summary>由 AutoCAD bulge（tan(包含角/4)）恢复圆心、半径和有向扫角。</summary>
        public static CircularArc FromBulge(Point2D start, Point2D end, double bulge)
        {
            if (Math.Abs(bulge) <= 1e-12)
            {
                throw new ArgumentOutOfRangeException(nameof(bulge));
            }

            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord <= 1e-9)
            {
                throw new ArgumentException("圆弧端点重合");
            }

            var midpointX = (start.X + end.X) / 2.0;
            var midpointY = (start.Y + end.Y) / 2.0;
            var centerOffset = chord * (1.0 - bulge * bulge) / (4.0 * bulge);
            var center = new Point2D(
                midpointX - dy / chord * centerOffset,
                midpointY + dx / chord * centerOffset);
            var radius = Math.Sqrt(
                (start.X - center.X) * (start.X - center.X)
                + (start.Y - center.Y) * (start.Y - center.Y));
            var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            return new CircularArc(center, radius, startAngle, 4.0 * Math.Atan(bulge));
        }
    }
}
