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

        /// <summary>精确的曲线缺口弧；直线多边形轮廓时为空。</summary>
        public CircularArc CurvedNotchArc { get; private set; }

        /// <summary>缺口外扩时圆弧半径的变化方向（+1 增大，-1 减小）。</summary>
        public int CurvedNotchOutwardRadiusSign { get; private set; }

        private Point2D _curvedNotchPrevious;

        private Point2D _curvedNotchNext;

        private int _outlineWinding;

        private bool[] _curvedSampledEdges;
        private readonly List<CurvedBoundaryInfo> _curvedBoundaries = new List<CurvedBoundaryInfo>();

        private sealed class CurvedBoundaryInfo
        {
            public CircularArc Arc;
            public Point2D Previous;
            public Point2D Next;
            public int Winding;
            public int RadiusSign;
            public int Start;
            public int End;
        }

        public IReadOnlyList<CircularArc> CurvedArcs
        {
            get { return _curvedBoundaries.ConvertAll(item => item.Arc).AsReadOnly(); }
        }

        public int CurvedBoundaryCount => _curvedBoundaries.Count;

        /// <summary>
        /// 判断圆弧是否位于轮廓转角：相邻直边不共线时是缺角，
        /// 共线时是矩形边上的弧形缺口（例如半圆缺口）。
        /// </summary>
        public bool IsCurvedBoundaryCorner(int boundaryIndex)
        {
            if (boundaryIndex < 0 || boundaryIndex >= _curvedBoundaries.Count)
            {
                return false;
            }

            var boundary = _curvedBoundaries[boundaryIndex];
            var inX = boundary.Arc.Start.X - boundary.Previous.X;
            var inY = boundary.Arc.Start.Y - boundary.Previous.Y;
            var outX = boundary.Next.X - boundary.Arc.End.X;
            var outY = boundary.Next.Y - boundary.Arc.End.Y;
            var scale = Math.Sqrt((inX * inX + inY * inY) * (outX * outX + outY * outY));
            if (scale <= 1e-9)
            {
                return false;
            }

            var cross = inX * outY - inY * outX;
            return Math.Abs(cross) / scale > 1e-4;
        }

        public int GetCurvedNotchOutwardRadiusSign(int boundaryIndex)
        {
            return _curvedBoundaries[boundaryIndex].RadiusSign;
        }

        internal void SetCurvedNotchArc(
            CircularArc worldArc,
            Point2D previous,
            Point2D next,
            int outlineWinding,
            int outwardRadiusSign,
            int sampledStart,
            int sampledEnd)
        {
            var localArc = new CircularArc(ToLocal(worldArc.Center), worldArc.Radius, worldArc.StartAngle, worldArc.SweepAngle);
            _curvedBoundaries.Add(new CurvedBoundaryInfo
            {
                Arc = localArc,
                Previous = ToLocal(previous),
                Next = ToLocal(next),
                Winding = outlineWinding >= 0 ? 1 : -1,
                RadiusSign = outwardRadiusSign >= 0 ? 1 : -1,
                Start = sampledStart,
                End = sampledEnd
            });
            CurvedNotchArc = new CircularArc(
                localArc.Center, localArc.Radius, localArc.StartAngle, localArc.SweepAngle);
            CurvedNotchOutwardRadiusSign = outwardRadiusSign >= 0 ? 1 : -1;
            _curvedNotchPrevious = ToLocal(previous);
            _curvedNotchNext = ToLocal(next);
            _outlineWinding = outlineWinding >= 0 ? 1 : -1;

            // 记录原始 bulge 占用的整段边。离散点仅用于排条裁剪，不能拆成边框料。
            if (_curvedSampledEdges == null)
            {
                _curvedSampledEdges = new bool[LocalOutline.Count];
            }

            var index = sampledStart;
            while (index != sampledEnd)
            {
                _curvedSampledEdges[index] = true;
                index = (index + 1) % LocalOutline.Count;
            }
        }

        /// <summary>把输入弧段的边归属映射到多边形统一逆时针后的顶点顺序。</summary>
        public IReadOnlyList<bool> GetCurvedNotchEdgeMask(IReadOnlyList<Point2D> normalizedVertices)
        {
            if (_curvedSampledEdges == null)
            {
                return null;
            }

            if (normalizedVertices.Count != LocalOutline.Count)
            {
                throw new ArgumentException("圆弧离散点数与轮廓边数不一致");
            }

            var mask = new bool[normalizedVertices.Count];
            for (var i = 0; i < mask.Length; i++)
            {
                var from = FindLocalVertex(normalizedVertices[i]);
                var to = FindLocalVertex(normalizedVertices[(i + 1) % mask.Length]);
                if (from < 0 || to < 0)
                {
                    throw new ArgumentException("圆弧轮廓顶点无法对应到输入边");
                }

                if ((from + 1) % mask.Length == to)
                {
                    mask[i] = _curvedSampledEdges[from];
                }
                else if ((to + 1) % mask.Length == from)
                {
                    mask[i] = _curvedSampledEdges[to];
                }
                else
                {
                    throw new ArgumentException("圆弧轮廓边顺序已改变");
                }
            }

            return mask;
        }

        private int FindLocalVertex(Point2D point)
        {
            for (var i = 0; i < LocalOutline.Count; i++)
            {
                var vertex = LocalOutline[i];
                if (Math.Abs(vertex.X - point.X) <= 1e-6
                    && Math.Abs(vertex.Y - point.Y) <= 1e-6)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>按缺口侧壁向板材方向偏移弧及端点，求圆弧与偏移直线的交点。</summary>
        public CircularArc OffsetCurvedNotchArc(double distance)
        {
            return OffsetCurvedNotchArc(0, distance);
        }

        public CircularArc OffsetCurvedNotchArc(int boundaryIndex, double distance)
        {
            if (boundaryIndex < 0 || boundaryIndex >= _curvedBoundaries.Count)
            {
                return null;
            }

            var boundary = _curvedBoundaries[boundaryIndex];
            var arc = boundary.Arc;
            var radius = arc.Radius + boundary.RadiusSign * distance;
            if (radius <= 0.0)
            {
                throw new ArgumentException("曲线缺口偏移后圆弧半径无效");
            }

            var start = IntersectOffsetSideWithCircle(boundary.Previous, arc.Start, arc.Center, radius, distance, arc.Start, boundary.Winding);
            var end = IntersectOffsetSideWithCircle(arc.End, boundary.Next, arc.Center, radius, distance, arc.End, boundary.Winding);
            var startAngle = Math.Atan2(start.Y - arc.Center.Y, start.X - arc.Center.X);
            var endAngle = Math.Atan2(end.Y - arc.Center.Y, end.X - arc.Center.X);
            var sweep = endAngle - startAngle;
            if (arc.SweepAngle > 0.0)
            {
                while (sweep <= 0.0) sweep += 2.0 * Math.PI;
            }
            else
            {
                while (sweep >= 0.0) sweep -= 2.0 * Math.PI;
            }

            return new CircularArc(arc.Center, radius, startAngle, sweep);
        }

        private Point2D IntersectOffsetSideWithCircle(
            Point2D lineStart,
            Point2D lineEnd,
            Point2D center,
            double radius,
            double distance,
            Point2D preferred,
            int winding)
        {
            var dx = lineEnd.X - lineStart.X;
            var dy = lineEnd.Y - lineStart.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 1e-9)
            {
                throw new ArgumentException("曲线缺口侧壁长度过小");
            }

            var ux = dx / length;
            var uy = dy / length;
            var nx = -uy * winding;
            var ny = ux * winding;
            var qx = lineStart.X + nx * distance - center.X;
            var qy = lineStart.Y + ny * distance - center.Y;
            var normalDistance = qx * nx + qy * ny;
            var remainder = radius * radius - normalDistance * normalDistance;
            if (remainder < -1e-7)
            {
                throw new ArgumentException("曲线缺口偏移侧壁与圆弧没有交点");
            }

            var root = Math.Sqrt(Math.Max(0.0, remainder));
            var first = new Point2D(
                center.X + ux * root + nx * normalDistance,
                center.Y + uy * root + ny * normalDistance);
            var second = new Point2D(
                center.X - ux * root + nx * normalDistance,
                center.Y - uy * root + ny * normalDistance);
            var d1x = first.X - preferred.X;
            var d1y = first.Y - preferred.Y;
            var d2x = second.X - preferred.X;
            var d2y = second.Y - preferred.Y;
            return d1x * d1x + d1y * d1y <= d2x * d2x + d2y * d2y ? first : second;
        }

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
