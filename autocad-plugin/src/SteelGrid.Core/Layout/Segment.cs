namespace SteelGrid.Core.Layout
{
    public struct Segment
    {
        public Segment(double a, double b)
        {
            A = a;
            B = b;
        }

        public double A { get; }

        public double B { get; }

        public double Length => B - A;

        public bool Contains(double value)
        {
            const double eps = 1e-9;
            return A <= value + eps && value <= B + eps;
        }
    }
}
