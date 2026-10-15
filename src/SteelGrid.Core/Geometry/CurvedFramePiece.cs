using System;
using System.Collections.Generic;
namespace SteelGrid.Core.Geometry
{
    /// <summary>由折弯扁钢制作的单根圆弧边框料。</summary>
    public sealed class CurvedFramePiece
    {
        public CurvedFramePiece(
            CircularArc plateArc,
            CircularArc netArc,
            int outwardRadiusSign,
            CircularArc bandPlateArc = null,
            CircularArc bandNetArc = null)
        {
            OutwardRadiusSign = outwardRadiusSign >= 0 ? 1 : -1;

            OutlinePlateArc = plateArc;
            OutlineNetArc = netArc;

            // Keep the stock's overlap treatment separate from the exact offset
            // contours. The semicircle's straight bars cover the arc ends; on a
            // sector, the arc bands cover the straight bars. Neither joint rule
            // should move the true plate/net contours used for clipping or layout.
            PlateArc = bandPlateArc ?? plateArc;
            NetArc = bandNetArc ?? netArc;
            CenterlineRadius = (PlateArc.Radius + NetArc.Radius) / 2.0;

            // Curved flat-bar stock is measured along the longer (outside) edge.
            // Keep both radii for the drawn band, but report the larger arc length
            // for every curved frame, independent of load direction.
            Length = Math.Max(PlateArc.Length, NetArc.Length);

            var plateMid = PlateArc.PointAt(0.5);
            var netMid = NetArc.PointAt(0.5);
            Center = new Point2D(
                (plateMid.X + netMid.X) / 2.0,
                (plateMid.Y + netMid.Y) / 2.0);
            var radialX = Center.X - plateArc.Center.X;
            var radialY = Center.Y - plateArc.Center.Y;
            var radialLength = Math.Sqrt(radialX * radialX + radialY * radialY);
            Outward = new Point2D(
                -OutwardRadiusSign * radialX / radialLength,
                -OutwardRadiusSign * radialY / radialLength);
            var midAngle = PlateArc.StartAngle + PlateArc.SweepAngle / 2.0;
            TangentAngle = midAngle + (Math.Sign(PlateArc.SweepAngle) >= 0 ? Math.PI / 2.0 : -Math.PI / 2.0);
        }

        /// <summary>弧料带外侧边，垂直受力半圆可按内框直线延长到该弧确定端点。</summary>
        public CircularArc PlateArc { get; }

        /// <summary>弧料带贴合净空内框的内侧边。</summary>
        public CircularArc NetArc { get; }

        /// <summary>板件缩尺外框上的真实偏移弧，用于保持外轮廓不漂移。</summary>
        public CircularArc OutlinePlateArc { get; }

        /// <summary>净空内框上的真实偏移弧；扇形外弧会按共用端角调整。</summary>
        public CircularArc OutlineNetArc { get; }

        public int OutwardRadiusSign { get; }

        public double CenterlineRadius { get; }

        public double Length { get; }

        public Point2D Center { get; }

        public Point2D Outward { get; }

        public double TangentAngle { get; }

        /// <summary>弧形料带的折线预览边界；CAD 下料图使用原生 bulge 圆弧。</summary>
        public List<Point2D> BandOutline(double maxSagitta = 0.05)
        {
            var result = PlateArc.Tessellate(maxSagitta);
            var inner = NetArc.Tessellate(maxSagitta);
            for (var i = inner.Count - 1; i >= 0; i--)
            {
                result.Add(inner[i]);
            }

            return result;
        }

    }
}
