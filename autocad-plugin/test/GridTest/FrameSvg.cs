using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;

namespace GridTest
{
    /// <summary>把板件轮廓和边框矩形画成 SVG，方便直接看位置对不对。</summary>
    internal static class FrameSvg
    {
        private const double Size = 1200.0;
        private const double Margin = 60.0;

        public static void Write(
            string path,
            OutlineShape shape,
            OutlineLayoutResult layout,
            List<RotatedPiece> pieces,
            int index)
        {
            var points = new List<Point2D>();
            AddRange(points, shape.Polygon.Vertices);
            foreach (var notch in layout.PlateNotches)
            {
                AddRange(points, notch.Vertices);
            }

            AddRange(points, layout.Plate.Vertices);
            AddRange(points, layout.Net.Vertices);
            foreach (var hole in layout.NetHoles)
            {
                AddRange(points, hole.Vertices);
            }

            foreach (var piece in pieces)
            {
                AddRange(points, piece.Corners());
            }

            if (points.Count == 0)
            {
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var point in points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            var scale = Math.Min((Size - 2 * Margin) / Math.Max(1e-6, maxX - minX),
                                 (Size - 2 * Margin) / Math.Max(1e-6, maxY - minY));

            var svg = new StringBuilder();
            svg.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + F(Size) + "\" height=\"" + F(Size)
                           + "\" viewBox=\"0 0 " + F(Size) + " " + F(Size) + "\">");
            svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");

            // 板件外轮廓（含缺口）与净空内边框
            // 原始轮廓（图纸上的图形，含缺角/缺口）
            svg.AppendLine(Polygon2(shape.LocalOutline, "none", "#2e7d32", 1.5, minX, maxY, scale));

            // 外框线 = 直接偏移的结果（本身已带缺角/缺口）
            svg.AppendLine(Polygon2(layout.Plate.Vertices, "none", "#d32f2f", 2.0, minX, maxY, scale));
            svg.AppendLine(Polygon2(layout.Net.Vertices, "none", "#9e9e9e", 1.5, minX, maxY, scale));

            // 边框矩形
            foreach (var piece in pieces)
            {
                var color = piece.EdgeIndex >= 0 ? "#1565c0" : "#ef6c00";
                svg.AppendLine(Polygon2(piece.Corners(), color, 1.5, minX, maxY, scale));
                var corners = piece.Corners();
                var cx = (corners[0].X + corners[2].X) / 2.0;
                var cy = (corners[0].Y + corners[2].Y) / 2.0;
                svg.AppendLine(Text(cx, cy, F(piece.Length), "#000000", 13.0, minX, maxY, scale));
            }

            svg.AppendLine("</svg>");
            File.WriteAllText(path, svg.ToString(), new UTF8Encoding(false));
            Console.WriteLine("  SVG: " + path + "（蓝=基准边料，橙=缺口料）");
        }

        private static void AddRange(List<Point2D> target, IEnumerable<Point2D> source)
        {
            foreach (var point in source)
            {
                target.Add(point);
            }
        }

        private static string Polygon(Polygon polygon, string fill, string stroke, double width, double minX, double maxY, double scale)
        {
            return Polygon2(polygon.Vertices, fill, stroke, width, minX, maxY, scale);
        }

        private static string Polygon2(IReadOnlyList<Point2D> points, string color, double width, double minX, double maxY, double scale)
        {
            return Polygon2(points, "none", color, width, minX, maxY, scale);
        }

        private static string Polygon2(
            IReadOnlyList<Point2D> points,
            string fill,
            string stroke,
            double width,
            double minX,
            double maxY,
            double scale)
        {
            var builder = new StringBuilder();
            builder.Append("<polygon points=\"");
            for (var i = 0; i < points.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(F(X(points[i].X, minX, scale)));
                builder.Append(',');
                builder.Append(F(Y(points[i].Y, maxY, scale)));
            }

            builder.Append("\" fill=\"" + fill + "\" stroke=\"" + stroke + "\" stroke-width=\"" + F(width) + "\"/>");
            return builder.ToString();
        }

        private static string Text(double x, double y, string text, string color, double size, double minX, double maxY, double scale)
        {
            return "<text x=\"" + F(X(x, minX, scale)) + "\" y=\"" + F(Y(y, maxY, scale))
                   + "\" font-size=\"" + F(size) + "\" fill=\"" + color
                   + "\" text-anchor=\"middle\">" + text + "</text>";
        }

        private static double X(double x, double minX, double scale)
        {
            return Margin + (x - minX) * scale;
        }

        private static double Y(double y, double maxY, double scale)
        {
            return Margin + (maxY - y) * scale;
        }

        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
