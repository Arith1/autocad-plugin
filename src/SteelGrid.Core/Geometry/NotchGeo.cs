using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    public sealed class NotchGeo
    {
        public NotchGeo(Notch source, Rect clear, Rect frame, List<string> touches)
        {
            Source = source;
            Clear = clear;
            Frame = frame;
            Touches = touches;
        }

        public Notch Source { get; }

        public Rect Clear { get; }

        public Rect Frame { get; }

        /// <summary>空洞贴到的板边（top/bottom/left/right）。</summary>
        public List<string> Touches { get; }
    }
}
