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
            LoadDirection loadDirection)
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
                var wrapped = loadDirection == LoadDirection.Vertical
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
                    if (wrapped ? spans[x] < spans[best] : spans[x] > spans[best])
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
                var origin = wrapped ? innerA : outerA;
                var midStart = ((startPoint.X - origin.X) * ex + (startPoint.Y - origin.Y) * ey
                                + (endPoint.X - origin.X) * ex + (endPoint.Y - origin.Y) * ey) / 2.0;
                var mid = new Point2D(origin.X + ex * midStart, origin.Y + ey * midStart);
                var center = new Point2D(
                    mid.X + (wrapped ? -1.0 : 1.0) * nx * frameThickness / 2.0,
                    mid.Y + (wrapped ? -1.0 : 1.0) * ny * frameThickness / 2.0);

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
                    i,
                    horizontalFlags[i] ? "横向" : "纵向",
                    center,
                    length,
                    frameThickness,
                    angle,
                    new Point2D(-nx, -ny)));
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
    }
}
