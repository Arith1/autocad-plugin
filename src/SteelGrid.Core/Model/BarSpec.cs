namespace SteelGrid.Core.Model
{
    /// <summary>纵横条布置参数。坐标和尺寸单位均为 mm。</summary>
    public sealed class BarSpec
    {
        public BarSpec()
        {
        }

        public BarSpec(BarType type, double thickness, double pitch)
        {
            Type = type;
            Thickness = thickness;
            Pitch = pitch;
        }

        /// <summary>扁钢=宽度；扭绞方钢=边长。</summary>
        public BarType Type { get; set; } = BarType.Flat;

        public double Thickness { get; set; } = 5.0;

        public double Pitch { get; set; } = 36.85;

        /// <summary>报告显示名。</summary>
        public string TypeName => Type == BarType.TwistedSquare ? "扭绞方钢" : "扁钢";

        /// <summary>规格显示名。</summary>
        public string SpecName =>
            Type == BarType.TwistedSquare
                ? $"{TypeName} {Format(Thickness)}x{Format(Thickness)}"
                : TypeName;

        private static string Format(double value)
        {
            return value.ToString("0.##");
        }
    }
}
