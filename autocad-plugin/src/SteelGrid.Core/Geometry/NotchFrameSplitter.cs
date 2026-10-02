using System;
using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    /// <summary>
    /// 缺口四周的边框料，规则照搬矩形路径：
    ///
    /// - 缺口开口所在的边不出料（开口由基准边料让开、封口在内壁）；
    /// - 其余每条壁出一根料，贴在缺口壁的**材料侧**（往缺口外退半个边框厚）；
    /// - 包边方向的壁（垂直受力的横壁）两端各外扩一个边框厚；
    /// - 贴着开口的那一端让一个边框厚（等价于矩形路径里减去主边框带）。
    /// </summary>
    public static class NotchFrameSplitter
    {
        private const double Tol = 1e-6;

        public static List<RotatedPiece> GetPieces(
            OutlineShape shape,
            Polygon plate,
            IReadOnlyList<Polygon> notches,
            double frameThickness,
            LoadDirection loadDirection)
        {
            var pieces = new List<RotatedPiece>();
            if (shape == null || shape.Polygon == null || frameThickness <= 0.0)
            {
                return pieces;
            }

            var rings = notches != null && notches.Count > 0 ? notches : shape.NotchPolygons;
            var reference = plate;
            if (reference == null)
            {
                reference = shape.Polygon;
            }

            foreach (var notch in rings)
            {
                var vertices = notch.Vertices;
                if (vertices.Count < 3)
                {
                    continue;
                }

                for (var e = 0; e < vertices.Count; e++)
                {
                    var a = vertices[e];
                    var b = vertices[(e + 1) % vertices.Count];
                    if (IsMouthEdge(reference, a, b))
                    {
                        continue;
                    }

                    var previous = (e + vertices.Count - 1) % vertices.Count;
                    var next = (e + 2) % vertices.Count;
                    var direction = DirectionOf(a, b);
                    var wrapExtend = IsWrapDirection(direction, loadDirection) ? frameThickness : 0.0;

                    var startExtra = IsMouthEdge(reference, vertices[previous], a) ? -frameThickness : wrapExtend;
                    var endExtra = IsMouthEdge(reference, b, vertices[next]) ? -frameThickness : wrapExtend;

                    AddPiece(pieces, a, b, direction, frameThickness, startExtra, endExtra);
                }
            }

            return pieces;
        }

        private static void AddPiece(
            List<RotatedPiece> pieces,
            Point2D from,
            Point2D to,
            string direction,
            double thickness,
            double startExtra,
            double endExtra)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= Tol)
            {
                return;
            }

            var ex = dx / length;
            var ey = dy / length;

            // 缺口多边形逆时针：右法线指向缺口外侧（材料侧）。
            var nx = ey;
            var ny = -ex;

            var start = new Point2D(from.X - ex * startExtra, from.Y - ey * startExtra);
            var end = new Point2D(to.X + ex * endExtra, to.Y + ey * endExtra);
            var pieceLength = length + startExtra + endExtra;
            if (pieceLength <= Tol)
            {
                return;
            }

            var mid = new Point2D((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
            var center = new Point2D(
                mid.X + nx * thickness / 2.0,
                mid.Y + ny * thickness / 2.0);

            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            pieces.Add(new RotatedPiece(
                -1,
                direction,
                center,
                pieceLength,
                thickness,
                angle,
                new Point2D(-nx, -ny)));
        }

        private static bool IsWrapDirection(string direction, LoadDirection loadDirection)
        {
            return loadDirection == LoadDirection.Vertical
                ? direction == "横向"
                : direction == "纵向";
        }

        /// <summary>这条边是否是缺口开口（两端都落在基准轮廓上）。</summary>
        private static bool IsMouthEdge(Polygon reference, Point2D from, Point2D to)
        {
            return reference != null
                   && DistanceToBoundary(reference, from) <= 0.01
                   && DistanceToBoundary(reference, to) <= 0.01;
        }

        private static string DirectionOf(Point2D from, Point2D to)
        {
            var angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            return Math.Abs(angle) <= 45.0 ? "横向" : "纵向";
        }

        private static double DistanceToBoundary(Polygon polygon, Point2D point)
        {
            var best = double.MaxValue;
            var vertices = polygon.Vertices;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var lengthSquared = dx * dx + dy * dy;
                var t = lengthSquared <= 1e-12
                    ? 0.0
                    : ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
                t = Math.Max(0.0, Math.Min(1.0, t));
                var px = a.X + t * dx;
                var py = a.Y + t * dy;
                best = Math.Min(best, Math.Sqrt((point.X - px) * (point.X - px) + (point.Y - py) * (point.Y - py)));
            }

            return best;
        }
    }
}
