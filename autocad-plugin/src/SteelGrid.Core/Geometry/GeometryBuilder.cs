using System;
using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    public static class GeometryBuilder
    {
        private const double Eps = 1e-9;

        public static PlateGeometry Build(Spec spec)
        {
            Validation.ValidateBasic(spec);

            var w = spec.PlateW;
            var h = spec.PlateH;
            var plate = new Rect(0.0, 0.0, w, h);
            var net = new Rect(
                spec.FrameT,
                spec.FrameT,
                w - spec.FrameT,
                h - spec.FrameT);

            if (net.W <= Eps || net.H <= Eps)
            {
                throw new ArgumentException("边框厚度过大，净空不存在");
            }

            var geos = new List<NotchGeo>();
            foreach (var notch in spec.Notches)
            {
                var edge = (notch.Edge ?? string.Empty).ToLowerInvariant();
                if (edge == "top" || edge == "bottom")
                {
                    var x0 = notch.Start - 2.0 * spec.Shrink;
                    var x1 = notch.Start + notch.Width;
                    var y0 = edge == "top" ? 0.0 : h - notch.Depth;
                    var y1 = edge == "top" ? notch.Depth : h;

                    var clear = AsPlateRect(new Rect(x0, y0, x1, y1), w, h);
                    var frame = AsPlateRect(
                        new Rect(
                            x0 - spec.FrameT,
                            y0 - spec.FrameT,
                            x1 + spec.FrameT,
                            y1 + spec.FrameT),
                        w,
                        h);
                    geos.Add(new NotchGeo(notch, clear, frame, EdgeTouches(clear, plate)));
                }
                else if (edge == "left" || edge == "right")
                {
                    var y0 = notch.Start - 2.0 * spec.Shrink;
                    var y1 = notch.Start + notch.Width;
                    var x0 = edge == "left" ? 0.0 : w - notch.Depth;
                    var x1 = edge == "left" ? notch.Depth : w;

                    var clear = AsPlateRect(new Rect(x0, y0, x1, y1), w, h);
                    var frame = AsPlateRect(
                        new Rect(
                            x0 - spec.FrameT,
                            y0 - spec.FrameT,
                            x1 + spec.FrameT,
                            y1 + spec.FrameT),
                        w,
                        h);
                    geos.Add(new NotchGeo(notch, clear, frame, EdgeTouches(clear, plate)));
                }
                else
                {
                    throw new ArgumentException("缺口 edge 必须是 top/bottom/left/right");
                }
            }

            ValidateGeometry(plate, net, geos);
            return new PlateGeometry(spec, plate, net, geos);
        }

        private static Rect AsPlateRect(Rect rect, double w, double h)
        {
            var clipped = rect.Clipped(new Rect(0.0, 0.0, w, h));
            if (clipped.W <= Eps || clipped.H <= Eps)
            {
                throw new ArgumentException("缺口不在板件范围内");
            }

            return clipped;
        }

        private static List<string> EdgeTouches(Rect rect, Rect plate)
        {
            var touches = new List<string>();
            if (Math.Abs(rect.X0 - plate.X0) <= Eps)
            {
                touches.Add("left");
            }

            if (Math.Abs(rect.X1 - plate.X1) <= Eps)
            {
                touches.Add("right");
            }

            if (Math.Abs(rect.Y0 - plate.Y0) <= Eps)
            {
                touches.Add("top");
            }

            if (Math.Abs(rect.Y1 - plate.Y1) <= Eps)
            {
                touches.Add("bottom");
            }

            return touches;
        }

        private static void ValidateGeometry(Rect plate, Rect net, List<NotchGeo> notches)
        {
            for (var i = 0; i < notches.Count; i++)
            {
                var item = notches[i];
                var touches = EdgeTouches(item.Frame, plate);
                if (touches.Count > 2)
                {
                    throw new ArgumentException($"第 {i + 1} 个空洞贯穿板件，当前版本不支持");
                }

                if (!net.Intersects(item.Frame) && !item.Frame.Intersects(net))
                {
                    throw new ArgumentException($"第 {i + 1} 个缺口没有进入净空区域");
                }
            }

            for (var i = 0; i < notches.Count; i++)
            {
                for (var j = i + 1; j < notches.Count; j++)
                {
                    if (notches[i].Frame.Intersects(notches[j].Frame))
                    {
                        throw new ArgumentException($"第 {i + 1} 和第 {j + 1} 个缺口的边框区域重叠");
                    }
                }
            }
        }
    }
}
