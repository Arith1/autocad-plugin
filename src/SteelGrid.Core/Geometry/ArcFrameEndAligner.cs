using System;
using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    /// <summary>
    /// 通用偏移中的凸弧接头：近切线处按直料的截面切平，其余端按直料包边面切平。
    /// 只调整弧形料带，不移动真实内外轮廓、排条边界或相邻直料。
    /// </summary>
    public static class ArcFrameEndAligner
    {
        private const double Tol = 1e-7;
        private static readonly double TangentTolerance = Math.Sin(Math.PI / 180.0);

        public static CurvedFramePiece AlignConvexTangentEnds(CurvedFramePiece piece,
            CircularArc sourceArc, Polygon sourceOutline, IReadOnlyList<bool> curvedEdges, LoadDirection direction)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (piece.OutwardRadiusSign >= 0) return piece;
            if (sourceOutline == null || curvedEdges == null || curvedEdges.Count != sourceOutline.Count)
                throw new ArgumentException("无法对应弧料端部与原始直边");
            var count = sourceOutline.Count;
            for (var i = 0; i < count; i++)
            {
                if (!curvedEdges[i] || curvedEdges[(i + count - 1) % count]) continue;
                var end = (i + 1) % count;
                while (curvedEdges[end] && end != i) end = (end + 1) % count;
                var previous = sourceOutline.Vertices[(i + count - 1) % count];
                var next = sourceOutline.Vertices[(end + 1) % count];
                var direct = DistanceSquared(sourceOutline.Vertices[i], sourceArc.Start) <=
                    DistanceSquared(sourceOutline.Vertices[i], sourceArc.End);
                return AlignConvexTangentEnds(piece, sourceArc, direct ? previous : next,
                    direct ? next : previous, direction);
            }
            return piece;
        }

        public static CurvedFramePiece AlignConvexTangentEnds(CurvedFramePiece piece,
            CircularArc sourceArc, Point2D previous, Point2D next, LoadDirection direction)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (sourceArc == null) throw new ArgumentNullException(nameof(sourceArc));
            if (piece.OutwardRadiusSign >= 0 ||
                (!NearTangent(sourceArc, true, previous) && !NearTangent(sourceArc, false, next)))
                return piece;

            var plate = piece.OutlinePlateArc;
            var net = piece.OutlineNetArc;
            Point2D plateStart, netStart, plateEnd, netEnd;
            AlignEnd(sourceArc, plate, net, true, previous, direction, out plateStart, out netStart);
            AlignEnd(sourceArc, plate, net, false, next, direction, out plateEnd, out netEnd);
            return new CurvedFramePiece(plate, net, piece.OutwardRadiusSign,
                WithEndpoints(plate, plateStart, plateEnd), WithEndpoints(net, netStart, netEnd));
        }

        private static void AlignEnd(CircularArc raw, CircularArc plate, CircularArc net,
            bool start, Point2D neighbor, LoadDirection direction, out Point2D platePoint, out Point2D netPoint)
        {
            var endpoint = start ? raw.Start : raw.End;
            var dx = neighbor.X - endpoint.X;
            var dy = neighbor.Y - endpoint.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= Tol) throw new ArgumentException("弧端相邻直边长度不足");
            dx /= length;
            dy /= length;
            var horizontal = Math.Abs(dx) >= Math.Abs(dy);
            var straightIsCovered = direction == LoadDirection.Vertical ? !horizontal : horizontal;
            var reference = straightIsCovered ? plate : net;
            var anchor = start ? reference.Start : reference.End;

            if (NearTangent(raw, start, neighbor))
            {
                // 切线延长线可能根本不穿过另一半径的圆。采用垂直于直料的截面，
                // 避免把切线交接错当成普通转角，产生巨大斜端头或无交点。
                var swap = dx;
                dx = -dy;
                dy = swap;
            }

            platePoint = Intersect(anchor, dx, dy, plate, start ? plate.Start : plate.End);
            netPoint = Intersect(anchor, dx, dy, net, start ? net.Start : net.End);
        }

        private static bool NearTangent(CircularArc arc, bool start, Point2D neighbor)
        {
            var point = start ? arc.Start : arc.End;
            var dx = neighbor.X - point.X;
            var dy = neighbor.Y - point.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= Tol) return false;
            // 切线与直边近似平行，等价于半径与直边近似垂直。原图允许1°以内的小偏差。
            return Math.Abs((point.X - arc.Center.X) * dx + (point.Y - arc.Center.Y) * dy)
                / (arc.Radius * length) <= TangentTolerance;
        }

        private static Point2D Intersect(Point2D anchor, double dx, double dy, CircularArc arc, Point2D preferred)
        {
            var x = anchor.X - arc.Center.X;
            var y = anchor.Y - arc.Center.Y;
            var t = -(x * dx + y * dy);
            var closest = new Point2D(anchor.X + t * dx, anchor.Y + t * dy);
            var ox = closest.X - arc.Center.X;
            var oy = closest.Y - arc.Center.Y;
            var square = arc.Radius * arc.Radius - ox * ox - oy * oy;
            if (square < -Tol) throw new ArgumentException("弧料端部切平线与另一侧弧没有交点");
            var distance = Math.Sqrt(Math.Max(0, square));
            var a = new Point2D(closest.X + dx * distance, closest.Y + dy * distance);
            var b = new Point2D(closest.X - dx * distance, closest.Y - dy * distance);
            return DistanceSquared(a, preferred) <= DistanceSquared(b, preferred) ? a : b;
        }

        private static CircularArc WithEndpoints(CircularArc arc, Point2D start, Point2D end)
        {
            var startAngle = Math.Atan2(start.Y - arc.Center.Y, start.X - arc.Center.X);
            var endAngle = Math.Atan2(end.Y - arc.Center.Y, end.X - arc.Center.X);
            var sweep = endAngle - startAngle;
            if (arc.SweepAngle > 0) { while (sweep <= 0) sweep += 2 * Math.PI; }
            else { while (sweep >= 0) sweep -= 2 * Math.PI; }
            if (Math.Abs(sweep - arc.SweepAngle) > Math.PI / 4)
                throw new ArgumentException("弧料端部切平改变扫角过大，需检查接头");
            return new CircularArc(arc.Center, arc.Radius, startAngle, sweep);
        }

        private static double DistanceSquared(Point2D a, Point2D b)
        {
            var dx = a.X - b.X; var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
