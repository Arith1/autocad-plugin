using System.Collections.Generic;

namespace SteelGrid.Core.Model
{
    /// <summary>外接矩形内部的一个矩形空洞（缺口/缺角）。</summary>
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

        /// <summary>
        /// 空洞贴到的板边，可为多个（角部空洞如 "top,left"）。
        /// 识别器按贴边情况填写，几何层按板件坐标重新核对。
        /// </summary>
        public List<string> Touches { get; set; } = new List<string>();
    }
}
