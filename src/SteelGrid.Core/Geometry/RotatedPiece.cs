using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Geometry
{
    /// <summary>
    /// 一条可以斜放的边框料：中心、下料长度、厚度、与局部 X 轴的夹角。
    /// 梯形斜边料就是这种旋转矩形。
    /// </summary>
    public sealed class RotatedPiece
    {
        public RotatedPiece(
            int edgeIndex,
            string direction,
            Point2D center,
            double length,
            double thickness,
            double angleDeg,
            Point2D outward)
        {
            EdgeIndex = edgeIndex;
            Direction = direction;
            Center = center;
            Length = length;
            Thickness = thickness;
            AngleDeg = angleDeg;
            Outward = outward;
        }

        /// <summary>对应的板边序号（局部顶点下标）。</summary>
        public int EdgeIndex { get; }

        /// <summary>横向 / 纵向，按边与局部 X 轴的夹角归类。</summary>
        public string Direction { get; }

        public Point2D Center { get; }

        /// <summary>下料长度（含两端搭接）。</summary>
        public double Length { get; }

        public double Thickness { get; }

        /// <summary>与局部 X 轴的夹角（度），范围 (-90, 90]。</summary>
        public double AngleDeg { get; }

        /// <summary>板外方向的单位向量（放边框标注用）。</summary>
        public Point2D Outward { get; }

        /// <summary>四个角点（局部坐标，逆时针）。</summary>
        public List<Point2D> Corners()
        {
            var radians = AngleDeg * Math.PI / 180.0;
            var ex = Math.Cos(radians);
            var ey = Math.Sin(radians);
            var nx = -ey;
            var ny = ex;
            var halfLength = Length / 2.0;
            var halfThickness = Thickness / 2.0;

            var corners = new List<Point2D>
            {
                new Point2D(
                    Center.X - ex * halfLength - nx * halfThickness,
                    Center.Y - ey * halfLength - ny * halfThickness),
                new Point2D(
                    Center.X + ex * halfLength - nx * halfThickness,
                    Center.Y + ey * halfLength - ny * halfThickness),
                new Point2D(
                    Center.X + ex * halfLength + nx * halfThickness,
                    Center.Y + ey * halfLength + ny * halfThickness),
                new Point2D(
                    Center.X - ex * halfLength + nx * halfThickness,
                    Center.Y - ey * halfLength + ny * halfThickness)
            };

            return corners;
        }
    }
}
