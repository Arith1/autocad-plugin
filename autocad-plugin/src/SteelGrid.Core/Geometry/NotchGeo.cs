using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    public sealed class NotchGeo
    {
        public NotchGeo(Notch source, Rect clear, Rect frame)
        {
            Source = source;
            Clear = clear;
            Frame = frame;
        }

        public Notch Source { get; }

        public Rect Clear { get; }

        public Rect Frame { get; }
    }
}
