using System.Collections.Generic;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Geometry
{
    public sealed class PlateGeometry
    {
        public PlateGeometry(Spec spec, Rect plate, Rect net, List<NotchGeo> notches)
        {
            Spec = spec;
            Plate = plate;
            Net = net;
            Notches = notches;
        }

        public Spec Spec { get; }

        public Rect Plate { get; }

        public Rect Net { get; }

        public List<NotchGeo> Notches { get; }
    }
}
