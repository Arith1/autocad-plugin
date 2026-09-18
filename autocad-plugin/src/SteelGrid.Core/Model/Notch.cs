namespace SteelGrid.Core.Model
{
    /// <summary>外轮廓边缘上的矩形缺口。</summary>
    public sealed class Notch
    {
        public Notch()
        {
        }

        public Notch(string edge, double start, double width, double depth)
        {
            Edge = edge;
            Start = start;
            Width = width;
            Depth = depth;
        }

        public string Edge { get; set; } = "top";

        public double Start { get; set; }

        public double Width { get; set; }

        public double Depth { get; set; }
    }
}
