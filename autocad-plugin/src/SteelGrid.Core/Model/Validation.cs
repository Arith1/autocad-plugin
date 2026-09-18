using System;

namespace SteelGrid.Core.Model
{
    public static class Validation
    {
        private const double Eps = 1e-9;

        public static void ValidateBasic(Spec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if (spec.OpeningW <= 0.0 || spec.OpeningH <= 0.0)
            {
                throw new ArgumentException("洞口尺寸必须大于 0");
            }

            if (spec.Shrink < 0.0)
            {
                throw new ArgumentException("缩尺不能为负数；不需要缩尺时请设为 0");
            }

            if (spec.PlateW <= 0.0 || spec.PlateH <= 0.0)
            {
                throw new ArgumentException("缩尺后的板件尺寸必须大于 0");
            }

            if (spec.FrameT <= 0.0)
            {
                throw new ArgumentException("边框厚度必须大于 0");
            }

            ValidateBar("纵向", spec.Vertical);
            ValidateBar("横向", spec.Horizontal);

            for (var i = 0; i < spec.Notches.Count; i++)
            {
                var notch = spec.Notches[i];
                var edge = (notch.Edge ?? string.Empty).ToLowerInvariant();
                if (edge != "top" && edge != "bottom" && edge != "left" && edge != "right")
                {
                    throw new ArgumentException($"第 {i + 1} 个缺口的 edge 无效：{notch.Edge}");
                }

                if (notch.Width <= 0.0 || notch.Depth <= 0.0)
                {
                    throw new ArgumentException($"第 {i + 1} 个缺口的宽度和深度必须大于 0");
                }
            }
        }

        private static void ValidateBar(string name, BarSpec bars)
        {
            if (bars == null)
            {
                throw new ArgumentException($"{name}参数不能为空");
            }

            if (bars.Thickness <= 0.0 || bars.Pitch <= 0.0)
            {
                throw new ArgumentException($"{name}条材厚度和中心距必须大于 0");
            }

            if (bars.Thickness > bars.Pitch + Eps)
            {
                throw new ArgumentException($"{name}条材厚度不能大于中心距");
            }
        }
    }
}
