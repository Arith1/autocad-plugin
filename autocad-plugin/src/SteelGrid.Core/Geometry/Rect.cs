using System;

namespace SteelGrid.Core.Geometry
{
    public struct Rect
    {
        public Rect(double x0, double y0, double x1, double y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }

        public double W => X1 - X0;

        public double H => Y1 - Y0;

        public Rect Clipped(Rect bounds)
        {
            return new Rect(
                Math.Max(X0, bounds.X0),
                Math.Max(Y0, bounds.Y0),
                Math.Min(X1, bounds.X1),
                Math.Min(Y1, bounds.Y1));
        }

        public bool Intersects(Rect other)
        {
            const double eps = 1e-9;
            return X0 < other.X1 - eps
                   && other.X0 < X1 - eps
                   && Y0 < other.Y1 - eps
                   && other.Y0 < Y1 - eps;
        }
    }
}
