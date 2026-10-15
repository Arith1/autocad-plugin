using System.Collections.Generic;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Layout
{
    /// <summary>
    /// 多边形净空排条结果：板件/净空多边形 + 条。
    /// 条与孔位复用 <see cref="LayoutResult"/>，报表、标注、绘图逻辑不用改。
    /// </summary>
    public sealed class OutlineLayoutResult
    {
        public OutlineLayoutResult(
            OutlineShape shape,
            Spec spec,
            Polygon plate,
            Polygon net,
            IReadOnlyList<Polygon> plateNotches,
            IReadOnlyList<Polygon> netHoles,
            IReadOnlyList<CurvedFramePiece> curvedFramePieces,
            IReadOnlyList<bool> curvedFrameEdges,
            List<RotatedPiece> framePieces,
            LayoutResult bars)
        {
            NetHoles = netHoles;
            CurvedFramePieces = curvedFramePieces ?? new List<CurvedFramePiece>();
            CurvedFramePiece = CurvedFramePieces.Count > 0 ? CurvedFramePieces[0] : null;
            CurvedFrameEdges = curvedFrameEdges;
            FramePieces = framePieces;
            PlateNotches = plateNotches;
            Shape = shape;
            Spec = spec;
            Plate = plate;
            Net = net;
            Bars = bars;
        }

        public OutlineShape Shape { get; }

        public Spec Spec { get; }

        /// <summary>缩尺后的板件多边形（局部坐标）。</summary>
        public Polygon Plate { get; }

        /// <summary>净空多边形（局部坐标），条的裁剪都在它上面做。</summary>
        public Polygon Net { get; }

        /// <summary>板件级缺口（开口宽已加 2×缩尺，深度不变），没有缺口时为空。</summary>
        public IReadOnlyList<Polygon> PlateNotches { get; private set; }

        /// <summary>净空里要挖掉的缺口区域（已按边框厚外扩，局部坐标），没有缺口时为空。</summary>
        public IReadOnlyList<Polygon> NetHoles { get; }

        /// <summary>曲线缺口上的单根弧形边框料；无弧形缺口时为空。</summary>
        public CurvedFramePiece CurvedFramePiece { get; }

        public IReadOnlyList<CurvedFramePiece> CurvedFramePieces { get; }

        /// <summary>离散圆弧占用的边，仅供裁剪索引使用；这些边不生成直边框料。</summary>
        public IReadOnlyList<bool> CurvedFrameEdges { get; }

        /// <summary>实际直边框料，已排除曲线离散产生的弦边。</summary>
        public List<RotatedPiece> FramePieces { get; }

        /// <summary>条与孔位；内部 Geometry 用多边形包围盒占位，供现有报表/标注复用。</summary>
        public LayoutResult Bars { get; }

        public List<Bar> VerticalBars => Bars.VerticalBars;

        public List<Bar> HorizontalBars => Bars.HorizontalBars;

        public int SegmentCount => Bars.SegmentCount;
    }
}
