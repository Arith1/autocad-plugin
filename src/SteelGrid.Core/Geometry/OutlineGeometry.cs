using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Geometry
{
    /// <summary>
    /// 把"凸基准轮廓 + 缺口"合成一条带缺口的**凹轮廓折线**：
    /// 沿基准多边形逆时针走，走到缺口开口时沿缺口绕进去、再从另一侧出来，继续沿基准边走。
    /// 外轮廓 = NotchedOutline(板件多边形, 板件缺口)；
    /// 内边框 = NotchedOutline(净空多边形, 净空缺口)。
    /// </summary>
    public static class OutlineGeometry
    {
        private const double Tol = 1e-6;

        public static List<Point2D> NotchedOutline(Polygon basePolygon, IReadOnlyList<Polygon> notches)
        {
            var result = new List<Point2D>();
            if (basePolygon == null || basePolygon.IsEmpty)
            {
                return result;
            }

            var vertices = basePolygon.Vertices;
            var count = vertices.Count;
            for (var i = 0; i < count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= Tol)
                {
                    continue;
                }

                var ex = dx / length;
                var ey = dy / length;
                result.Add(a);

                var onEdge = new List<Tuple<double, List<Point2D>>>();
                if (notches != null)
                {
                    foreach (var notch in notches)
                    {
                        var chain = ChainOnEdge(notch, a, ex, ey, length);
                        if (chain == null || chain.Count == 0)
                        {
                            continue;
                        }

                        var u = (chain[0].X - a.X) * ex + (chain[0].Y - a.Y) * ey;
                        onEdge.Add(Tuple.Create(u, chain));
                    }
                }

                onEdge.Sort((left, right) => left.Item1.CompareTo(right.Item1));
                foreach (var item in onEdge)
                {
                    result.AddRange(item.Item2);
                }
            }

            return result;
        }

        /// <summary>缺口在这条基准边上的绕行链：从沿边先遇到的开口顶点起，按缺口自身顺序绕到另一个开口顶点。</summary>
        private static List<Point2D> ChainOnEdge(Polygon notch, Point2D edgeStart, double ex, double ey, double edgeLength)
        {
            var vertices = notch.Vertices;
            var count = vertices.Count;
            if (count < 3)
            {
                return null;
            }

            for (var i = 0; i < count; i++)
            {
                var p = vertices[i];
                var q = vertices[(i + 1) % count];
                double up, vp, uq, vq;
                if (!OnLine(edgeStart, ex, ey, edgeLength, p, out up, out vp)
                    || !OnLine(edgeStart, ex, ey, edgeLength, q, out uq, out vq))
                {
                    continue;
                }

                // 开口边 = p→q；沿基准边先遇到的那端是"入口"
                var entryIndex = up <= uq ? i : (i + 1) % count;
                var exitIndex = up <= uq ? (i + 1) % count : i;

                // 从入口沿"不经过开口边"的方向绕到出口，这样才是绕进缺口里
                var step = entryIndex == i ? count - 1 : 1;
                var chain = new List<Point2D>();
                var index = entryIndex;
                while (true)
                {
                    chain.Add(vertices[index]);
                    if (index == exitIndex)
                    {
                        break;
                    }

                    index = (index + step) % count;
                    if (chain.Count > count)
                    {
                        break;
                    }
                }

                return chain;
            }

            return null;
        }

        private static bool OnLine(
            Point2D edgeStart,
            double ex,
            double ey,
            double edgeLength,
            Point2D point,
            out double u,
            out double v)
        {
            var dx = point.X - edgeStart.X;
            var dy = point.Y - edgeStart.Y;
            u = dx * ex + dy * ey;
            v = -dx * ey + dy * ex;
            return Math.Abs(v) <= 0.01 && u >= -0.01 && u <= edgeLength + 0.01;
        }
    }
}
