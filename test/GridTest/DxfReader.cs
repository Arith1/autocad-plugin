using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SteelGrid.Core.Geometry;

namespace GridTest
{
    /// <summary>一条多段线：顶点、每条边的凸度、闭合标记。</summary>
    internal sealed class DxfOutline
    {
        public DxfOutline(List<Point2D> points, List<double> bulges, bool closed)
        {
            Points = points;
            Bulges = bulges;
            Closed = closed;
        }

        public List<Point2D> Points { get; }

        /// <summary>第 i 条边（顶点 i → 顶点 i+1）的凸度；DXF 里为 0 时会省略。</summary>
        public List<double> Bulges { get; }

        public bool Closed { get; }
    }

    /// <summary>
    /// 极简 DXF 读取：按顺序解析 ENTITIES 段里的多段线，取顶点（10/20）、
    /// 凸度（42，落到它所属的顶点上）和闭合标记（70 的第 1 位）。
    /// </summary>
    internal static class DxfReader
    {
        public static List<DxfOutline> Read(string path)
        {
            var result = new List<DxfOutline>();
            var lines = ReadLines(path);

            var inEntities = false;
            var expectSectionName = false;
            var collecting = false;
            var points = new List<Point2D>();
            var bulges = new List<double>();
            var closed = false;
            double x = 0.0;
            var hasX = false;

            for (var i = 0; i + 1 < lines.Count; i += 2)
            {
                int code;
                if (!int.TryParse(lines[i].Trim(), out code))
                {
                    continue;
                }

                var value = lines[i + 1].Trim();

                if (code == 0)
                {
                    if (collecting)
                    {
                        Flush(result, points, bulges, closed);
                        collecting = false;
                    }

                    points = new List<Point2D>();
                    bulges = new List<double>();
                    closed = false;
                    hasX = false;

                    if (value == "SECTION")
                    {
                        inEntities = false;
                        expectSectionName = true;
                    }
                    else if (value == "ENDSEC")
                    {
                        inEntities = false;
                        expectSectionName = false;
                    }
                    else if (inEntities && (value == "LWPOLYLINE" || value == "POLYLINE"))
                    {
                        collecting = true;
                    }

                    continue;
                }

                if (expectSectionName && code == 2)
                {
                    inEntities = value == "ENTITIES";
                    expectSectionName = false;
                    continue;
                }

                if (!collecting)
                {
                    continue;
                }

                switch (code)
                {
                    case 10:
                        hasX = TryParse(value, out x);
                        break;
                    case 20:
                        double y;
                        if (hasX && TryParse(value, out y))
                        {
                            points.Add(new Point2D(x, y));
                            bulges.Add(0.0);
                        }

                        hasX = false;
                        break;
                    case 42:
                        double bulge;
                        if (bulges.Count > 0 && TryParse(value, out bulge))
                        {
                            bulges[bulges.Count - 1] = bulge;
                        }

                        break;
                    case 70:
                        int flags;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
                        {
                            closed = (flags & 1) != 0;
                        }

                        break;
                }
            }

            if (collecting)
            {
                Flush(result, points, bulges, closed);
            }

            return result;
        }

        /// <summary>按共享读打开，AutoCAD 正打开着图纸时也能读。</summary>
        private static List<string> ReadLines(string path)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static void Flush(List<DxfOutline> result, List<Point2D> points, List<double> bulges, bool closed)
        {
            if (points.Count < 3 || Area(points) <= 1e-6)
            {
                return;
            }

            result.Add(new DxfOutline(new List<Point2D>(points), new List<double>(bulges), closed));
        }

        private static double Area(List<Point2D> points)
        {
            var sum = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }

            return Math.Abs(sum) / 2.0;
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
