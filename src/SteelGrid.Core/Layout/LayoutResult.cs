using System.Collections.Generic;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Layout
{
    public sealed class LayoutResult
    {
        public LayoutResult(PlateGeometry geometry, List<Bar> verticalBars, List<Bar> horizontalBars)
        {
            Geometry = geometry;
            VerticalBars = verticalBars;
            HorizontalBars = horizontalBars;
        }

        public PlateGeometry Geometry { get; }

        public List<Bar> VerticalBars { get; }

        public List<Bar> HorizontalBars { get; }

        public Spec Spec => Geometry.Spec;

        public int SegmentCount
        {
            get
            {
                var count = 0;
                foreach (var bar in VerticalBars)
                {
                    count += bar.Segments.Count;
                }

                foreach (var bar in HorizontalBars)
                {
                    count += bar.Segments.Count;
                }

                return count;
            }
        }
    }
}
