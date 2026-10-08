using System.Collections.Generic;

namespace SteelGrid.Core.Layout
{
    /// <summary>给条分配孔位：与另一方向条的交点，矩形路径与多边形路径共用。</summary>
    public static class BarHoles
    {
        public static void Assign(Bar bar, List<Bar> crossBars)
        {
            bar.HoleGroups.Clear();
            foreach (var segment in bar.Segments)
            {
                var holes = new List<double>();
                foreach (var cross in crossBars)
                {
                    if (segment.Contains(cross.Center) && Covers(cross.Segments, bar.Center))
                    {
                        holes.Add(cross.Center);
                    }
                }

                bar.HoleGroups.Add(holes);
            }
        }

        private static bool Covers(List<Segment> segments, double value)
        {
            foreach (var segment in segments)
            {
                if (segment.Contains(value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
