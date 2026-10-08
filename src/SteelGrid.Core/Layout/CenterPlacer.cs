using System;
using System.Collections.Generic;

namespace SteelGrid.Core.Layout
{
    /// <summary>
    /// 按中心距在给定跨度里均分布置条中心；矩形路径与多边形路径共用同一份实现。
    /// </summary>
    public static class CenterPlacer
    {
        private const double Eps = 1e-9;

        public static List<double> Place(double lo, double hi, double thickness, double pitch)
        {
            var span = hi - lo;
            if (span <= Eps || thickness > span + Eps)
            {
                return new List<double>();
            }

            var count = Math.Max(1, (int)Math.Floor(span / pitch + Eps));
            var margin = (span - (count - 1) * pitch - thickness) / 2.0;
            if (margin < -Eps)
            {
                count = Math.Max(1, count - 1);
                margin = (span - (count - 1) * pitch - thickness) / 2.0;
            }

            var first = lo + margin + thickness / 2.0;
            var result = new List<double>();
            for (var i = 0; i < count; i++)
            {
                result.Add(first + i * pitch);
            }

            return result;
        }
    }
}
