using SteelGrid.Core.Model;

namespace SteelGrid.Plugin.UI
{
    /// <summary>批量输出时排条图的排版方向。</summary>
    public enum OutputFlow
    {
        /// <summary>横向输出：排条图先从左到右排，再换行。</summary>
        Horizontal,

        /// <summary>纵向输出：排条图先从上到下排，再换列。</summary>
        Vertical
    }

    /// <summary>批量生成时源图形的处理顺序。</summary>
    public enum GenerationOrder
    {
        /// <summary>先自上至下，后自左至右。</summary>
        TopDownFirst,

        /// <summary>先自左至右，后自上至下。</summary>
        LeftRightFirst
    }

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

        public OutputFlow OutputFlow { get; set; } = OutputFlow.Horizontal;

        public GenerationOrder GenerationOrder { get; set; } = GenerationOrder.TopDownFirst;

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
