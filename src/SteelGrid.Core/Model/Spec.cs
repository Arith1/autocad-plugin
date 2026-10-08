using System.Collections.Generic;

namespace SteelGrid.Core.Model
{
    public sealed class Spec
    {
        public double OpeningW { get; set; }

        public double OpeningH { get; set; }

        public double Shrink { get; set; } = 5.0;

        public double FrameT { get; set; } = 5.0;

        public LoadDirection LoadDirection { get; set; } = LoadDirection.Vertical;

        public BarSpec Vertical { get; set; } = new BarSpec(BarType.Flat, 5.0, 36.85);

        public BarSpec Horizontal { get; set; } = new BarSpec(BarType.Flat, 5.0, 36.85);

        public List<Notch> Notches { get; set; } = new List<Notch>();

        public double PlateW => OpeningW - 2.0 * Shrink;

        public double PlateH => OpeningH - 2.0 * Shrink;
    }
}
