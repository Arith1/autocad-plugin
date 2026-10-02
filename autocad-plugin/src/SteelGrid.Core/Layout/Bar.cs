using System.Collections.Generic;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Layout
{
    public sealed class Bar
    {
        public Bar(
            string orientation,
            int index,
            double center,
            double thickness,
            List<Segment> segments,
            bool full)
        {
            Orientation = orientation;
            Index = index;
            Center = center;
            Thickness = thickness;
            Segments = segments;
            Full = full;
            HoleGroups = new List<List<double>>();
            Warnings = new List<string>();
        }

        public string Orientation { get; }

        public int Index { get; }

        public double Center { get; }

        public double Thickness { get; }

        public List<Segment> Segments { get; }

        public bool Full { get; set; }

        public List<List<double>> HoleGroups { get; }

        /// <summary>
        /// 多边形路径下每段的裁剪信息（含斜边处理论两端），矩形路径为空。
        /// 斜边截断已按短边切平，段本身就是下料矩形。
        /// </summary>
        public List<BandCut> BandCuts { get; } = new List<BandCut>();

        public List<string> Warnings { get; }
    }
}
