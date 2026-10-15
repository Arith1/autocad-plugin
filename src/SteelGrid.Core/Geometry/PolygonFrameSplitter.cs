using System;
using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    /// <summary>
    /// 多边形轮廓的边框拆分：**内外框线之间的带状区域**，每条边一根料。
    ///
    /// - 外框线 = 原始轮廓偏移缩尺；内框线 = 再偏移一个边框厚；
    /// - 长度按内外边框线取：包边方向取外边框线长度，被包方向取内边框线长度（水平受力对调）；
    /// - 定位：以"取长度的那条线"为基准，料的两条长边分别贴住内外框线；
    /// - 归类按图纸方向；四条边同向时按偏离水平角度排序兜底。
    /// </summary>
    public static class PolygonFrameSplitter
    {
        private const double Tol = 1e-9;

        public static List<RotatedPiece> GetPieces(
            Polygon plate,
            Polygon net,
            double frameThickness,
            LoadDirection loadDirection,
            IReadOnlyList<bool> curvedEdges = null,
            bool curveCoversStraightBars = false)
        {
            var pieces = new List<RotatedPiece>();
            if (plate == null || net == null || plate.IsEmpty || net.IsEmpty || frameThickness <= 0.0)
            {
                return pieces;
            }

            var count = plate.Vertices.Count;
            if (net.Vertices.Count != count)
            {
                return pieces;
            }

            if (curvedEdges != null && curvedEdges.Count != count)
            {
                throw new ArgumentException("弧段边标记与轮廓边数不一致", nameof(curvedEdges));
            }

            var horizontalFlags = new bool[count];
            for (var i = 0; i < count; i++)
            {
                horizontalFlags[i] = IsHorizontal(plate.Vertices[i], plate.Vertices[(i + 1) % count]);
            }

            if (count == 4 && (All(horizontalFlags, true) || All(horizontalFlags, false)))
            {
                var order = new List<int> { 0, 1, 2, 3 };
                order.Sort((a, b) => Deviation(plate, a).CompareTo(Deviation(plate, b)));
                for (var i = 0; i < count; i++)
                {
                    horizontalFlags[i] = false;
                }

                horizontalFlags[order[0]] = true;
                horizontalFlags[order[1]] = true;
            }

            for (var i = 0; i < count; i++)
            {
                if (curvedEdges != null && curvedEdges[i])
                {
                    continue;
                }

                var outerA = plate.Vertices[i];
                var outerB = plate.Vertices[(i + 1) % count];
                var innerA = net.Vertices[i];
                var innerB = net.Vertices[(i + 1) % count];

                var dx = outerB.X - outerA.X;
                var dy = outerB.Y - outerA.Y;
                var outerLength = Math.Sqrt(dx * dx + dy * dy);
                var innerLength = EdgeLength(innerA, innerB);
                if (outerLength <= Tol || innerLength <= Tol)
                {
                    continue;
                }

                var ex = dx / outerLength;
                var ey = dy / outerLength;
                var nx = -ey;
                var ny = ex;
                // isCovered 表示本边由相邻边框料覆盖：竖向受力时竖边让位、横边包住竖边；
                // 水平受力时规则对调。
                var isCovered = loadDirection == LoadDirection.Vertical
                    ? !horizontalFlags[i]
                    : horizontalFlags[i];

                // 矩形路径的包边规则：
                // 包边（横边）要包住相邻的被包边 —— 取本条边两端「内框线交点 + 外框线交点」
                // 共 4 个点，沿边方向距离最长的那段就是它的长度；
                // 被包边（纵边）让位，取内框线长度（最短）。
                var ua = 0.0;
                var ub = outerLength;
                var uia = (innerA.X - outerA.X) * ex + (innerA.Y - outerA.Y) * ey;
                var uib = (innerB.X - outerA.X) * ex + (innerB.Y - outerA.Y) * ey;
                // 四个点两两沿边距离：包边取最大值（盖住相邻边），被包取最小值（让位）
                // 四个点 = 本条边两端：外框线交点 A/B、内框线交点 a/b
                var points = new[] { outerA, outerB, innerA, innerB };
                var spans = new[]
                {
                    Math.Abs(ua - ub),
                    Math.Abs(uia - uib),
                    Math.Abs(ua - uib),
                    Math.Abs(uia - ub)
                };
                var pairs = new[]
                {
                    new[] { 0, 1 },
                    new[] { 2, 3 },
                    new[] { 0, 3 },
                    new[] { 1, 2 }
                };

                // 包边取最长那一对、被包取最短那一对
                var best = 0;
                for (var x = 1; x < spans.Length; x++)
                {
                    if (isCovered ? spans[x] < spans[best] : spans[x] > spans[best])
                    {
                        best = x;
                    }
                }

                var startPoint = points[pairs[best][0]];
                var endPoint = points[pairs[best][1]];
                var length = spans[best];
                if (length <= 0.0)
                {
                    continue;
                }

                // 位置：包边按外框线坐标、被包按内框线坐标取中点（两条线的起点差一个边框厚）
                var origin = isCovered ? innerA : outerA;
                var midStart = ((startPoint.X - origin.X) * ex + (startPoint.Y - origin.Y) * ey
                                + (endPoint.X - origin.X) * ex + (endPoint.Y - origin.Y) * ey) / 2.0;
                var mid = new Point2D(origin.X + ex * midStart, origin.Y + ey * midStart);
                var center = new Point2D(
                    mid.X + (isCovered ? -1.0 : 1.0) * nx * frameThickness / 2.0,
                    mid.Y + (isCovered ? -1.0 : 1.0) * ny * frameThickness / 2.0);

                var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                while (angle <= -90.0)
                {
                    angle += 180.0;
                }

                while (angle > 90.0)
                {
                    angle -= 180.0;
                }

                var piece = new RotatedPiece(
                    i,
                    horizontalFlags[i] ? "横向" : "纵向",
                    center,
                    length,
                    frameThickness,
                    angle,
                    new Point2D(-nx, -ny));

                if (curveCoversStraightBars)
                {
                    // On a sector, arc bands cover the radial bars. Trim only
                    // the bar corners that cross the true outer outline at an
                    // arc joint; semicircle bars retain their covering ends.
                    var outline = KeepCornersInside(piece.Corners(), center, plate);
                    piece = new RotatedPiece(
                        i,
                        horizontalFlags[i] ? "横向" : "纵向",
                        center,
                        length,
                        frameThickness,
                        angle,
                        new Point2D(-nx, -ny),
                        outline);
                }

                pieces.Add(piece);
            }

            return pieces;
        }

        /// <summary>该顶点在逆时针轮廓里是不是凹角（缺口/缺角处）。</summary>
        private static bool IsConcave(Polygon polygon, int index)
        {
            var count = polygon.Vertices.Count;
            var previous = polygon.Vertices[(index + count - 1) % count];
            var current = polygon.Vertices[index];
            var next = polygon.Vertices[(index + 1) % count];
            var ax = current.X - previous.X;
            var ay = current.Y - previous.Y;
            var bx = next.X - current.X;
            var by = next.Y - current.Y;
            return ax * by - ay * bx < -1e-6;
        }

        private static bool IsHorizontal(Point2D a, Point2D b)
        {
            var angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            return Math.Abs(angle) <= 45.0;
        }

        private static double Deviation(Polygon polygon, int index)
        {
            var a = polygon.Vertices[index];
            var b = polygon.Vertices[(index + 1) % polygon.Vertices.Count];
            var angle = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            return Math.Abs(angle);
        }

        private static bool All(bool[] values, bool expected)
        {
            foreach (var value in values)
            {
                if (value != expected)
                {
                    return false;
                }
            }

            return true;
        }

        private static double EdgeLength(Point2D a, Point2D b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static List<Point2D> KeepCornersInside(
            IReadOnlyList<Point2D> rectangle,
            Point2D interiorPoint,
            Polygon boundary)
        {
            var clipped = new List<Point2D>(rectangle);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var outside = FindOutsidePoint(clipped, boundary);
                if (!outside.HasValue)
                {
                    return clipped;
                }

                var edgeIndex = NearestEdge(outside.Value, boundary);
                var edgeStart = boundary.Vertices[edgeIndex];
                var edgeEnd = boundary.Vertices[(edgeIndex + 1) % boundary.Vertices.Count];
                clipped = ClipToHalfPlane(clipped, edgeStart, edgeEnd, interiorPoint);
                if (clipped.Count < 3)
                {
                    throw new ArgumentException("扇形直料裁到外框后退化，无法形成边框料");
                }
            }

            if (FindOutsidePoint(clipped, boundary).HasValue)
            {
                throw new ArgumentException("扇形直料端角无法收进缩尺外框");
            }

            return clipped;
        }

        private static Point2D? FindOutsidePoint(IReadOnlyList<Point2D> points, Polygon boundary)
        {
            foreach (var point in points)
            {
                if (!boundary.Contains(point))
                {
                    return point;
                }
            }

            return null;
        }

        private static int NearestEdge(Point2D point, Polygon polygon)
        {
            var bestIndex = 0;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < polygon.Vertices.Count; i++)
            {
                var a = polygon.Vertices[i];
                var b = polygon.Vertices[(i + 1) % polygon.Vertices.Count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var lengthSquared = dx * dx + dy * dy;
                var t = lengthSquared <= Tol
                    ? 0.0
                    : ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
                t = Math.Max(0.0, Math.Min(1.0, t));
                var x = a.X + t * dx;
                var y = a.Y + t * dy;
                var distance = (point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static List<Point2D> ClipToHalfPlane(
            IReadOnlyList<Point2D> polygon,
            Point2D lineStart,
            Point2D lineEnd,
            Point2D keepPoint)
        {
            var result = new List<Point2D>();
            var keepSide = SignedSide(lineStart, lineEnd, keepPoint);
            if (Math.Abs(keepSide) <= Tol)
            {
                throw new ArgumentException("扇形直料定位点落在外框裁切线上");
            }

            var sign = keepSide > 0.0 ? 1.0 : -1.0;
            var previous = polygon[polygon.Count - 1];
            var previousSide = sign * SignedSide(lineStart, lineEnd, previous);
            var previousInside = previousSide >= -Tol;
            for (var i = 0; i < polygon.Count; i++)
            {
                var current = polygon[i];
                var currentSide = sign * SignedSide(lineStart, lineEnd, current);
                var currentInside = currentSide >= -Tol;
                if (currentInside != previousInside)
                {
                    var denominator = previousSide - currentSide;
                    if (Math.Abs(denominator) > Tol)
                    {
                        var t = previousSide / denominator;
                        result.Add(new Point2D(
                            previous.X + (current.X - previous.X) * t,
                            previous.Y + (current.Y - previous.Y) * t));
                    }
                }

                if (currentInside)
                {
                    result.Add(current);
                }

                previous = current;
                previousSide = currentSide;
                previousInside = currentInside;
            }

            return RemoveAdjacentDuplicates(result);
        }

        private static List<Point2D> RemoveAdjacentDuplicates(List<Point2D> points)
        {
            var result = new List<Point2D>();
            foreach (var point in points)
            {
                if (result.Count == 0 || DistanceSquared(result[result.Count - 1], point) > Tol * Tol)
                {
                    result.Add(point);
                }
            }

            if (result.Count > 1 && DistanceSquared(result[0], result[result.Count - 1]) <= Tol * Tol)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static double SignedSide(Point2D a, Point2D b, Point2D point)
        {
            return (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
        }

        private static double DistanceSquared(Point2D a, Point2D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

    }
}
