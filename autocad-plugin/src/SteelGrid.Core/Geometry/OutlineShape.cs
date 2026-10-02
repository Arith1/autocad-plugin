using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Geometry
{
    /// <summary>轮廓类型。</summary>
    public enum OutlineKind
    {
        /// <summary>横平竖直的矩形，走插件现有矩形路径。</summary>
        Rectangle,

        /// <summary>平行四边形（含整体旋转的矩形）。</summary>
        Parallelogram,

        /// <summary>梯形等其余凸四边形。</summary>
        Trapezoid
    }

    /// <summary>局部坐标下的一条直线边。</summary>
    public sealed class OutlineEdge
    {
        public OutlineEdge(int index, Point2D start, Point2D end, bool isLongest)
        {
            Index = index;
            Start = start;
            End = end;
            IsLongest = isLongest;

            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            Length = Math.Sqrt(dx * dx + dy * dy);

            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            AngleDeg = angle;
        }

        /// <summary>起点在局部顶点数组里的下标。</summary>
        public int Index { get; }

        public Point2D Start { get; }

        public Point2D End { get; }

        public double Length { get; }

        /// <summary>与局部 X 轴的夹角（度），范围 (-90, 90]。</summary>
        public double AngleDeg { get; }

        /// <summary>是否是最长边（局部 X 轴所在边）。</summary>
        public bool IsLongest { get; }

        /// <summary>接近水平（|角度| ≤ 45°），边框包边顺序按这个归类。</summary>
        public bool NearHorizontal => Math.Abs(AngleDeg) <= 45.0;

        public bool NearVertical => !NearHorizontal;
    }

    /// <summary>
    /// 轮廓解析结果：局部坐标（最长边为局部 X 轴、局部 Y 轴指向板内、逆时针）
    /// 以及局部坐标与世界坐标的互转。
    /// </summary>
    public sealed class OutlineShape
    {
        private readonly Point2D _axisX;
        private readonly Point2D _axisY;

        public OutlineShape(
            OutlineKind kind,
            Polygon polygon,
            IReadOnlyList<Point2D> worldVertices,
            IReadOnlyList<OutlineEdge> edges,
            IReadOnlyList<Polygon> notches,
            Point2D origin,
            Point2D axisX,
            Point2D axisY)
        {
            NotchPolygons = notches;
            Kind = kind;
            Polygon = polygon;
            WorldVertices = worldVertices;
            Edges = edges;
            Origin = origin;
            _axisX = axisX;
            _axisY = axisY;
            RotationDegrees = Math.Atan2(axisX.Y, axisX.X) * 180.0 / Math.PI;

            // 局部原始轮廓（含缺角/缺口，直接偏移用）
            var outline = new List<Point2D>();
            foreach (var vertex in WorldVertices)
            {
                var dx = vertex.X - Origin.X;
                var dy = vertex.Y - Origin.Y;
                outline.Add(new Point2D(
                    dx * axisX.X + dy * axisX.Y,
                    dx * axisY.X + dy * axisY.Y));
            }

            LocalOutline = outline;
        }

        public OutlineKind Kind { get; }

        /// <summary>局部坐标下的基准轮廓（凸包：矩形/梯形/平行四边形，逆时针）。</summary>
        public Polygon Polygon { get; }

        /// <summary>缺口多边形（局部坐标，凸、逆时针），没有缺口时为空。</summary>
        public IReadOnlyList<Polygon> NotchPolygons { get; }

        /// <summary>局部坐标下的**原始闭合轮廓**（含缺角/缺口，未拆解），直接偏移用它。</summary>
        public IReadOnlyList<Point2D> LocalOutline { get; private set; }

        /// <summary>清洗后的**原始**世界坐标顶点（含缺角/缺口），逆时针。</summary>
        public IReadOnlyList<Point2D> WorldVertices { get; }

        /// <summary>局部坐标下的边，顺序与顶点一致。</summary>
        public IReadOnlyList<OutlineEdge> Edges { get; }

        /// <summary>局部原点（包围盒最小值）对应的世界坐标点。</summary>
        public Point2D Origin { get; }

        /// <summary>局部 X 轴在世界坐标中的单位向量。</summary>
        public Point2D AxisX => _axisX;

        /// <summary>局部 Y 轴在世界坐标中的单位向量（指向板内）。</summary>
        public Point2D AxisY => _axisY;

        /// <summary>局部 X 轴相对世界 X 轴的夹角（度）。</summary>
        public double RotationDegrees { get; }

        public Rect Bounds => Polygon.Bounds;

        /// <summary>局部坐标 → 世界坐标。</summary>
        public Point2D ToWorld(Point2D local)
        {
            return new Point2D(
                Origin.X + local.X * _axisX.X + local.Y * _axisY.X,
                Origin.Y + local.X * _axisX.Y + local.Y * _axisY.Y);
        }

        /// <summary>世界坐标 → 局部坐标。</summary>
        public Point2D ToLocal(Point2D world)
        {
            var dx = world.X - Origin.X;
            var dy = world.Y - Origin.Y;
            return new Point2D(
                dx * _axisX.X + dy * _axisX.Y,
                dx * _axisY.X + dy * _axisY.Y);
        }
    }
}
