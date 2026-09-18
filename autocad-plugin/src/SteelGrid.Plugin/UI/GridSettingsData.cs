using SteelGrid.Core.Model;

namespace SteelGrid.Plugin.UI
{
    /// <summary>已保存的排条参数。</summary>
    public sealed class GridSettingsData
    {
        public double Shrink { get; set; } = 5.0;

        public double FrameT { get; set; } = 5.0;

        public LoadDirection LoadDirection { get; set; } = LoadDirection.Vertical;

        public BarType HorizontalType { get; set; } = BarType.Flat;

        public double HorizontalThickness { get; set; } = 5.0;

        public double HorizontalPitch { get; set; } = 36.85;

        public BarType VerticalType { get; set; } = BarType.Flat;

        public double VerticalThickness { get; set; } = 5.0;

        public double VerticalPitch { get; set; } = 36.85;

        public Spec ToSpec(double openingW, double openingH, Notch[] notches)
        {
            return new Spec
            {
                OpeningW = openingW,
                OpeningH = openingH,
                Shrink = Shrink,
                FrameT = FrameT,
                LoadDirection = LoadDirection,
                Horizontal = new BarSpec(
                    HorizontalType,
                    HorizontalThickness,
                    HorizontalPitch),
                Vertical = new BarSpec(
                    VerticalType,
                    VerticalThickness,
                    VerticalPitch),
                Notches = new System.Collections.Generic.List<Notch>(notches)
            };
        }
    }
}
