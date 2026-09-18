using System.Collections.Generic;
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

        public bool Full { get; }

        public List<List<double>> HoleGroups { get; }

        public List<string> Warnings { get; }
    }
}
