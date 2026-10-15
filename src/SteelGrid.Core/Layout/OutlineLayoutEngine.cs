using System;
using System.Collections.Generic;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Model;

namespace SteelGrid.Core.Layout
{
    /// <summary>
    /// 多边形净空排条：条带与净空多边形裁剪（斜边按短边切平，见方案 6.1），
    /// 孔位与满长判定和矩形路径保持一致。
    /// </summary>
    public static class OutlineLayoutEngine
    {
        private const double Eps = 1e-6;

        public static OutlineLayoutResult Layout(OutlineShape shape, Spec spec)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            Validation.ValidateForOutline(spec);

            // 按外框线 → 内框线的顺序对整个闭合轮廓偏移；边框带就是两条完整轮廓之间的区域。
            var rawOutline = new Polygon(shape.LocalOutline);
            var plate = rawOutline.Offset(spec.Shrink);
            var net = plate.Offset(spec.FrameT);
            if (net.IsEmpty)
            {
                throw new ArgumentException("缩尺或边框厚度过大，净空不存在");
            }

            var curvedFrameEdges = shape.GetCurvedNotchEdgeMask(rawOutline.Vertices);
            if (curvedFrameEdges != null
                && (plate.Vertices.Count != curvedFrameEdges.Count || net.Vertices.Count != curvedFrameEdges.Count))
            {
                throw new ArgumentException("圆弧偏移后边数改变，无法正确拆分边框料");
            }

            var curvedFramePieces = new List<CurvedFramePiece>();
            for (var arcIndex = 0; arcIndex < shape.CurvedBoundaryCount; arcIndex++)
            {
                var sign = shape.GetCurvedNotchOutwardRadiusSign(arcIndex);
                CircularArc plateArc;
                CircularArc netArc;
                try
                {
                    plateArc = shape.OffsetCurvedNotchArc(arcIndex, spec.Shrink);
                    netArc = shape.OffsetCurvedNotchArc(arcIndex, spec.Shrink + spec.FrameT);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException("缩尺或边框厚度过大，曲线缺口偏移失败：" + ex.Message);
                }

                var bandPlateArc = plateArc;
                var bandNetArc = netArc;
                if (shape.CurvedBoundaryCount == 2)
                {
                    // Sector arcs cover the two radial straight bars. Give each
                    // curved band a shared angle while leaving the real net arc
                    // untouched for the straight-bar layout and clipping.
                    bandNetArc = new CircularArc(
                        netArc.Center, netArc.Radius, plateArc.StartAngle, plateArc.SweepAngle);
                }
                else if (shape.CurvedBoundaryCount == 1 && shape.IsCurvedBoundaryCorner(arcIndex))
                {
                    // A clipped rectangle corner is not a semicircle notch. Each
                    // arc end follows its adjacent straight frame's wrap rule:
                    // the covered edge uses the plate-side line; the wrapping
                    // edge uses the net-side line. This lets the two arc radii
                    // share the correct straight-bar end faces.
                    bandPlateArc = AlignCornerArcToStraightFrameEnds(
                        plateArc,
                        plateArc,
                        netArc,
                        shape.CurvedArcs[arcIndex],
                        rawOutline,
                        plate,
                        net,
                        curvedFrameEdges,
                        spec.LoadDirection);
                    bandNetArc = AlignCornerArcToStraightFrameEnds(
                        netArc,
                        plateArc,
                        netArc,
                        shape.CurvedArcs[arcIndex],
                        rawOutline,
                        plate,
                        net,
                        curvedFrameEdges,
                        spec.LoadDirection);
                }
                else if (shape.CurvedBoundaryCount == 1 && spec.LoadDirection == LoadDirection.Vertical)
                {
                    var plateIsShorter = plateArc.Radius < netArc.Radius;
                    var shorterArc = plateIsShorter ? plateArc : netArc;
                    var longerArc = plateIsShorter ? netArc : plateArc;
                    var longerOutline = plateIsShorter ? net : plate;

                    // Keep the longer-radius offset arc and align the shorter arc
                    // to the longer side's adjacent straight border lines. This
                    // follows the semicircle reference geometry, not the sector
                    // common-angle rule.
                    var alignedShorterArc = AlignShorterArcToLongerBorderLines(
                        shorterArc, longerArc, longerOutline, curvedFrameEdges);
                    if (plateIsShorter)
                    {
                        bandPlateArc = alignedShorterArc;
                    }
                    else
                    {
                        bandNetArc = alignedShorterArc;
                    }
                }

                curvedFramePieces.Add(new CurvedFramePiece(
                    plateArc,
                    netArc,
                    sign,
                    bandPlateArc,
                    bandNetArc));
            }

            if (curvedFramePieces.Count > 0)
            {
                // Keep exact curved endpoints and the corrected sampled arc chain
                // on the same contour used for clipping, drawing, and frame splitting.
                plate = WithExactArcEnds(plate, curvedFramePieces, curvedFrameEdges, true);
                net = WithExactArcEnds(net, curvedFramePieces, curvedFrameEdges, false);
            }

            // 缺口区域按边框厚外扩，从净空里挖掉（缺口四周的边框料占位）。
            // 板件缺口：开口方向两端各加一个缩尺量（开口宽 + 2×缩尺），深度保持原始深度。
            var plateNotches = new List<Polygon>();
            var netHoles = new List<Polygon>();
            foreach (var notch in shape.NotchPolygons)
            {
                // 缺口按"每个壁各自沿法线向外偏移"扩展（斜口缺口也适用）：
                // 板件缺口偏移 缩尺；排条区缺口偏移 缩尺 + 边框厚。
                plateNotches.Add(notch.OffsetOutward(spec.Shrink));
                netHoles.Add(notch.OffsetOutward(spec.Shrink + spec.FrameT));
            }

            // 中心距布置范围按"原始轮廓包围盒 缩尺 + 边框厚"（与矩形路径一致），
            // 缺口不影响布置起点，否则缺口那一侧的条会整体位移。
            var rawBounds = new Polygon(shape.LocalOutline).Bounds;
            var bounds = new Rect(
                rawBounds.X0 + spec.Shrink + spec.FrameT,
                rawBounds.Y0 + spec.Shrink + spec.FrameT,
                rawBounds.X1 - spec.Shrink - spec.FrameT,
                rawBounds.Y1 - spec.Shrink - spec.FrameT);
            var verticalBars = BuildBars(
                net,
                netHoles,
                CenterPlacer.Place(bounds.X0, bounds.X1, spec.Vertical.Thickness, spec.Vertical.Pitch),
                spec.Vertical,
                BandAxis.Vertical,
                bounds.H,
                spec.FrameT,
                shape.CurvedBoundaryCount > 0);
            var horizontalBars = BuildBars(
                net,
                netHoles,
                CenterPlacer.Place(bounds.Y0, bounds.Y1, spec.Horizontal.Thickness, spec.Horizontal.Pitch),
                spec.Horizontal,
                BandAxis.Horizontal,
                bounds.W,
                spec.FrameT,
                shape.CurvedBoundaryCount > 0);

            foreach (var bar in verticalBars)
            {
                BarHoles.Assign(bar, horizontalBars);
            }

            foreach (var bar in horizontalBars)
            {
                BarHoles.Assign(bar, verticalBars);
            }

            // 斜边处孔距不足：删掉该孔，并把另一方向的条在斜边侧断开（断口落到被截断条的内侧边）。
            // 竖向受力：纵条不截断（要一直顶到边框），只动横条。
            var cutOrientation = spec.LoadDirection == LoadDirection.Vertical ? "horizontal" : "vertical";
            ApplySlantHoleRule(verticalBars, horizontalBars, spec, false, cutOrientation);
            ApplySlantHoleRule(horizontalBars, verticalBars, spec, true, cutOrientation);

            foreach (var bar in verticalBars)
            {
                BarHoles.Assign(bar, horizontalBars);
            }

            foreach (var bar in horizontalBars)
            {
                BarHoles.Assign(bar, verticalBars);
            }

            // 收尾：重新分配孔位后，任何端距不足最小孔距的孔都去掉（条本身的位置已在上一步处理）。
            foreach (var bar in verticalBars)
            {
                RemoveTooCloseHoles(bar, spec);
            }

            foreach (var bar in horizontalBars)
            {
                RemoveTooCloseHoles(bar, spec);
            }

            // Keep straight stock on the actual offset outlines. Rebuilding the
            // entire sampled contour from extended arc endpoints skews the two
            // neighboring straight edges and can make the semicircle fail layout.
            var framePieces = PolygonFrameSplitter.GetPieces(
                plate,
                net,
                spec.FrameT,
                spec.LoadDirection,
                curvedFrameEdges,
                shape.CurvedBoundaryCount == 2);

            var geometry = new PlateGeometry(spec, plate.Bounds, net.Bounds, new List<NotchGeo>());
            return new OutlineLayoutResult(
                shape,
                spec,
                plate,
                net,
                plateNotches,
                netHoles,
                curvedFramePieces,
                curvedFrameEdges,
                framePieces,
                new LayoutResult(geometry, verticalBars, horizontalBars));
        }

        /// <summary>半圆垂直受力：用较大半径侧的直边确定较小半径弧的端点。</summary>
        private static CircularArc AlignShorterArcToLongerBorderLines(
            CircularArc shorterArc,
            CircularArc longerArc,
            Polygon longerOutline,
            IReadOnlyList<bool> curvedEdges)
        {
            if (shorterArc == null || longerArc == null || longerOutline == null
                || curvedEdges == null || curvedEdges.Count != longerOutline.Vertices.Count)
            {
                throw new ArgumentException("无法确定半圆弧料端点对应的内框直线");
            }

            var count = curvedEdges.Count;
            var runStart = -1;
            for (var i = 0; i < count; i++)
            {
                if (curvedEdges[i] && !curvedEdges[(i + count - 1) % count])
                {
                    runStart = i;
                    break;
                }
            }

            if (runStart < 0)
            {
                throw new ArgumentException("未找到半圆弧对应的内框直线");
            }

            var runEnd = runStart;
            do
            {
                runEnd = (runEnd + 1) % count;
            }
            while (curvedEdges[runEnd] && runEnd != runStart);

            var before = (runStart + count - 1) % count;
            var after = (runEnd + 1) % count;
            var sampledStart = longerOutline.Vertices[runStart];
            var startsAtArcStart = DistanceSquared(sampledStart, longerArc.Start)
                                   <= DistanceSquared(sampledStart, longerArc.End);
            var startLineNeighbor = longerOutline.Vertices[startsAtArcStart ? before : after];
            var endLineNeighbor = longerOutline.Vertices[startsAtArcStart ? after : before];

            var shorterStart = IntersectLineWithCircle(
                longerArc.Start, startLineNeighbor, shorterArc.Center, shorterArc.Radius, shorterArc.Start);
            var shorterEnd = IntersectLineWithCircle(
                longerArc.End, endLineNeighbor, shorterArc.Center, shorterArc.Radius, shorterArc.End);
            var startAngle = Math.Atan2(shorterStart.Y - shorterArc.Center.Y, shorterStart.X - shorterArc.Center.X);
            var endAngle = Math.Atan2(shorterEnd.Y - shorterArc.Center.Y, shorterEnd.X - shorterArc.Center.X);
            var sweep = endAngle - startAngle;
            if (shorterArc.SweepAngle > 0.0)
            {
                while (sweep <= 0.0) sweep += 2.0 * Math.PI;
            }
            else
            {
                while (sweep >= 0.0) sweep -= 2.0 * Math.PI;
            }

            return new CircularArc(shorterArc.Center, shorterArc.Radius, startAngle, sweep);
        }

        /// <summary>缺角弧带两端分别按相邻直边的包边关系投影。</summary>
        private static CircularArc AlignCornerArcToStraightFrameEnds(
            CircularArc targetArc,
            CircularArc plateArc,
            CircularArc netArc,
            CircularArc rawArc,
            Polygon rawOutline,
            Polygon plateOutline,
            Polygon netOutline,
            IReadOnlyList<bool> curvedEdges,
            LoadDirection loadDirection)
        {
            if (targetArc == null || plateArc == null || netArc == null || rawArc == null
                || rawOutline == null || plateOutline == null || netOutline == null
                || curvedEdges == null || curvedEdges.Count != rawOutline.Vertices.Count
                || plateOutline.Vertices.Count != curvedEdges.Count
                || netOutline.Vertices.Count != curvedEdges.Count)
            {
                throw new ArgumentException("无法确定圆弧缺角与直边框料的连接端点");
            }

            var count = curvedEdges.Count;
            var runStart = -1;
            for (var i = 0; i < count; i++)
            {
                if (curvedEdges[i] && !curvedEdges[(i + count - 1) % count])
                {
                    runStart = i;
                    break;
                }
            }

            if (runStart < 0)
            {
                throw new ArgumentException("未找到圆弧缺角的离散边界");
            }

            var runEnd = runStart;
            do
            {
                runEnd = (runEnd + 1) % count;
            }
            while (curvedEdges[runEnd] && runEnd != runStart);

            var before = (runStart + count - 1) % count;
            var after = (runEnd + 1) % count;
            var runStartPoint = rawOutline.Vertices[runStart];
            var startsAtRunStart = DistanceSquared(runStartPoint, rawArc.Start)
                                   <= DistanceSquared(runStartPoint, rawArc.End);

            var startVertex = startsAtRunStart ? runStart : runEnd;
            var startNeighbor = startsAtRunStart ? before : after;
            var endVertex = startsAtRunStart ? runEnd : runStart;
            var endNeighbor = startsAtRunStart ? after : before;

            var startFromPlate = UsePlateSideForCornerEndpoint(
                rawOutline.Vertices[startVertex],
                rawOutline.Vertices[startNeighbor],
                loadDirection);
            var endFromPlate = UsePlateSideForCornerEndpoint(
                rawOutline.Vertices[endVertex],
                rawOutline.Vertices[endNeighbor],
                loadDirection);

            var startSourceArc = startFromPlate ? plateArc : netArc;
            var startSourceOutline = startFromPlate ? plateOutline : netOutline;
            var endSourceArc = endFromPlate ? plateArc : netArc;
            var endSourceOutline = endFromPlate ? plateOutline : netOutline;

            var startPoint = IntersectLineWithCircle(
                startsAtRunStart ? startSourceArc.Start : startSourceArc.End,
                startSourceOutline.Vertices[startNeighbor],
                targetArc.Center,
                targetArc.Radius,
                targetArc.Start);
            var endPoint = IntersectLineWithCircle(
                startsAtRunStart ? endSourceArc.End : endSourceArc.Start,
                endSourceOutline.Vertices[endNeighbor],
                targetArc.Center,
                targetArc.Radius,
                targetArc.End);

            return ArcWithEndpoints(targetArc, startPoint, endPoint);
        }

        private static bool UsePlateSideForCornerEndpoint(
            Point2D edgeA,
            Point2D edgeB,
            LoadDirection loadDirection)
        {
            var dx = edgeB.X - edgeA.X;
            var dy = edgeB.Y - edgeA.Y;
            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            while (angle <= -90.0)
            {
                angle += 180.0;
            }

            while (angle > 90.0)
            {
                angle -= 180.0;
            }

            var horizontal = Math.Abs(angle) <= 45.0;
            var isCovered = loadDirection == LoadDirection.Vertical ? !horizontal : horizontal;
            return isCovered;
        }

        private static CircularArc ArcWithEndpoints(CircularArc arc, Point2D start, Point2D end)
        {
            var startAngle = Math.Atan2(start.Y - arc.Center.Y, start.X - arc.Center.X);
            var endAngle = Math.Atan2(end.Y - arc.Center.Y, end.X - arc.Center.X);
            var sweep = endAngle - startAngle;
            if (arc.SweepAngle > 0.0)
            {
                while (sweep <= 0.0) sweep += 2.0 * Math.PI;
            }
            else
            {
                while (sweep >= 0.0) sweep -= 2.0 * Math.PI;
            }

            return new CircularArc(arc.Center, arc.Radius, startAngle, sweep);
        }

        private static Point2D IntersectLineWithCircle(
            Point2D linePoint,
            Point2D lineNeighbor,
            Point2D center,
            double radius,
            Point2D preferred)
        {
            var dx = lineNeighbor.X - linePoint.X;
            var dy = lineNeighbor.Y - linePoint.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= Eps)
            {
                throw new ArgumentException("弧端相邻边框直线长度过小");
            }

            var ux = dx / length;
            var uy = dy / length;
            var fromCenterX = linePoint.X - center.X;
            var fromCenterY = linePoint.Y - center.Y;
            var along = -(fromCenterX * ux + fromCenterY * uy);
            var closestX = linePoint.X + along * ux;
            var closestY = linePoint.Y + along * uy;
            var offsetX = closestX - center.X;
            var offsetY = closestY - center.Y;
            var remainder = radius * radius - offsetX * offsetX - offsetY * offsetY;
            if (remainder < -1e-7)
            {
                throw new ArgumentException("边框直线延长后与弧料圆没有交点");
            }

            var perpendicular = Math.Sqrt(Math.Max(0.0, remainder));
            // The intersections lie along the line direction from the closest
            // point. Using the normal collapses horizontal cut lines onto the
            // circle's vertical diameter and can degenerate the arc band.
            var px = ux * perpendicular;
            var py = uy * perpendicular;
            var first = new Point2D(closestX + px, closestY + py);
            var second = new Point2D(closestX - px, closestY - py);
            return DistanceSquared(first, preferred) <= DistanceSquared(second, preferred) ? first : second;
        }

        /// <summary>用精确偏移弧端替换离散轮廓中的采样弦端，保持各直边不漂移。</summary>
        private static Polygon WithExactArcEnds(
            Polygon sampledOutline,
            IReadOnlyList<CurvedFramePiece> curvedPieces,
            IReadOnlyList<bool> curvedEdges,
            bool plateBoundary)
        {
            var count = sampledOutline.Vertices.Count;
            var vertices = new List<Point2D>(sampledOutline.Vertices);
            for (var i = 0; i < count; i++)
            {
                if (curvedEdges[i] && !curvedEdges[(i + count - 1) % count])
                {
                    var end = i;
                    var arcEdgeCount = 0;
                    do
                    {
                        arcEdgeCount++;
                        end = (end + 1) % count;
                    }
                    while (curvedEdges[end] && end != i);

                    var sampledStart = vertices[i];
                    CurvedFramePiece matched = null;
                    var bestDistance = double.MaxValue;
                    CircularArc matchedArc = null;
                    foreach (var piece in curvedPieces)
                    {
                        var candidate = plateBoundary ? piece.OutlinePlateArc : piece.OutlineNetArc;
                        var dStart = DistanceSquared(sampledStart, candidate.Start);
                        var dEnd = DistanceSquared(sampledStart, candidate.End);
                        if (Math.Min(dStart, dEnd) < bestDistance)
                        {
                            bestDistance = Math.Min(dStart, dEnd);
                            matched = piece;
                            matchedArc = candidate;
                        }
                    }

                    if (matched == null)
                    {
                        throw new ArgumentException("无法将离散弧边对应到整根弧料");
                    }

                    var direct = DistanceSquared(sampledStart, matchedArc.Start) <= DistanceSquared(sampledStart, matchedArc.End);
                    for (var sample = 0; sample <= arcEdgeCount; sample++)
                    {
                        var vertexIndex = (i + sample) % count;
                        var fraction = (double)sample / arcEdgeCount;
                        vertices[vertexIndex] = matchedArc.PointAt(direct ? fraction : 1.0 - fraction);
                    }
                }
            }

            return new Polygon(vertices);
        }

        private static double DistanceSquared(Point2D a, Point2D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>板件缺口：沿开口方向两端各加一个缩尺量，深度方向不动。</summary>
        private static Polygon WidenNotch(Polygon notch, Polygon basePolygon, double shrink)
        {
            if (shrink <= 0.0 || notch.IsEmpty || basePolygon == null)
            {
                return notch;
            }

            // 找贴在基准轮廓上的那条边（开口）
            var vertices = notch.Vertices;
            var mouthFrom = vertices[0];
            var mouthTo = vertices[1 % vertices.Count];
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                if (AtBoundary(basePolygon, a) && AtBoundary(basePolygon, b))
                {
                    mouthFrom = a;
                    mouthTo = b;
                    break;
                }
            }

            var dx = mouthTo.X - mouthFrom.X;
            var dy = mouthTo.Y - mouthFrom.Y;
            var mouthWidth = Math.Sqrt(dx * dx + dy * dy);
            if (mouthWidth <= Eps)
            {
                return notch;
            }

            var ex = dx / mouthWidth;
            var ey = dy / mouthWidth;
            var scale = (mouthWidth + 2.0 * shrink) / mouthWidth;
            var centerX = (mouthFrom.X + mouthTo.X) / 2.0;
            var centerY = (mouthFrom.Y + mouthTo.Y) / 2.0;

            // 缺口开口朝板内的一侧：把缺口整体往板内推一个缩尺量，
            // 这样开口落在板件边上、深度从板件边量起仍等于原始深度。
            var inwardX = 0.0;
            var inwardY = 0.0;
            for (var i = 0; i < basePolygon.Vertices.Count; i++)
            {
                var a = basePolygon.Vertices[i];
                var b = basePolygon.Vertices[(i + 1) % basePolygon.Vertices.Count];
                var bx = b.X - a.X;
                var by = b.Y - a.Y;
                var len = Math.Sqrt(bx * bx + by * by);
                if (len <= Eps)
                {
                    continue;
                }

                var ux = -by / len;
                var uy = bx / len;
                var distance = Math.Abs((mouthFrom.X - a.X) * ux + (mouthFrom.Y - a.Y) * uy);
                if (distance <= 0.05)
                {
                    inwardX = ux;
                    inwardY = uy;
                    break;
                }
            }

            var widened = new List<Point2D>();
            foreach (var vertex in vertices)
            {
                var u = (vertex.X - centerX) * ex + (vertex.Y - centerY) * ey;
                var v = -(vertex.X - centerX) * ey + (vertex.Y - centerY) * ex;
                widened.Add(new Point2D(
                    centerX + ex * u * scale - ey * v + inwardX * shrink,
                    centerY + ey * u * scale + ex * v + inwardY * shrink));
            }

            return new Polygon(widened);
        }

        /// <summary>点是否落在多边形边界上（判定缺口开口用）。</summary>
        private static bool AtBoundary(Polygon polygon, Point2D point)
        {
            var vertices = polygon.Vertices;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var lengthSquared = dx * dx + dy * dy;
                var s = lengthSquared <= 1e-12
                    ? 0.0
                    : ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
                s = Math.Max(0.0, Math.Min(1.0, s));
                var px = a.X + s * dx;
                var py = a.Y + s * dy;
                if (Math.Sqrt((point.X - px) * (point.X - px) + (point.Y - py) * (point.Y - py)) <= 0.01)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把缺口区域占用的条带区间从段里减掉（缺口四周留边框料）。</summary>
        private static List<BandCut> SubtractHoles(
            List<BandCut> cuts,
            List<Polygon> netHoles,
            double center,
            double halfWidth,
            BandAxis axis,
            double clearance)
        {
            foreach (var hole in netHoles)
            {
                double low, high;
                // 纯偏移模型：缺口已经偏移两次（含边框厚），条带只按自身宽度判是否重叠。
                if (hole.TryClipBandUnion(center, halfWidth, axis, out low, out high))
                {
                    cuts = Subtract(cuts, low, high);
                }
            }

            return cuts;
        }

        private static List<BandCut> Subtract(List<BandCut> cuts, double lo, double hi)
        {
            var result = new List<BandCut>();
            foreach (var cut in cuts)
            {
                if (cut.B <= lo + Eps || cut.A >= hi - Eps)
                {
                    result.Add(cut);
                    continue;
                }

                if (cut.A < lo - Eps)
                {
                    result.Add(new BandCut(cut.A, lo, cut.Rect, cut.RawStart, cut.RawEnd));
                }

                if (cut.B > hi + Eps)
                {
                    result.Add(new BandCut(hi, cut.B, cut.Rect, cut.RawStart, cut.RawEnd));
                }
            }

            return result;
        }

        /// <summary>
        /// 斜边处被截断的条，若离端头最近的孔距小于「边框厚 + 该条厚/2」，这个孔打不了：
        /// 直接删掉该孔，并把另一方向的条在斜边侧断开——缩短量 = 它自己在这个交点的孔距 + 被截断条厚/2，
        /// 断口正好落在被截断条的内侧边上（见方案 6.2）。
        /// </summary>
        private static void ApplySlantHoleRule(
            List<Bar> bars,
            List<Bar> crossBars,
            Spec spec,
            bool allowCutOwnEnd,
            string cutOrientation)
        {
            foreach (var bar in bars)
            {
                var minHole = spec.FrameT + bar.Thickness / 2.0;
                for (var s = 0; s < bar.Segments.Count; s++)
                {
                    var segment = bar.Segments[s];
                    var holes = bar.HoleGroups[s];
                    if (holes.Count == 0 || s >= bar.BandCuts.Count)
                    {
                        continue;
                    }

                    var cut = bar.BandCuts[s];
                    var slantStart = cut.StartSlanted;
                    var slantEnd = cut.EndSlanted;
                    for (var i = holes.Count - 1; i >= 0; i--)
                    {
                        var hole = holes[i];
                        var tooCloseAtEnd = segment.B - hole < minHole - 1e-9;
                        var tooCloseAtStart = hole - segment.A < minHole - 1e-9;
                        if (!tooCloseAtEnd && !tooCloseAtStart)
                        {
                            continue;
                        }

                        holes.RemoveAt(i);
                        // 只有另一方向的条在这个交点上也快到头了（孔距同样不够），才把它在斜边侧断开：
                        // 那只是一点点修边；否则只能删掉这个孔，不动那根条。
                        var counterThickness = CounterThickness(crossBars, hole);
                        var counterNear = CounterEndDistance(crossBars, hole, bar)
                                          < spec.FrameT + counterThickness / 2.0 - 1e-9;
                        if (counterNear && !allowCutOwnEnd)
                        {
                            // 竖向受力时纵条不能截断：只把横条（另一方向）断开。
                            ShortenBarAt(crossBars, hole, bar.Center, bar.Thickness, cutOrientation);
                        }
                        else if (allowCutOwnEnd && (slantStart || slantEnd))
                        {
                            // 横条自己的孔太靠端头、而对面那条还长：退横条自己的端头，并去掉对面的孔。
                            ShortenBarAt(bars, bar.Center, hole, counterThickness, cutOrientation);
                            RemoveHole(crossBars, hole, bar.Center);
                        }
                        else
                        {
                            // 对面那条还长、又不能截断纵条：只删孔，不动条（条仍顶到边框）。
                            RemoveHole(crossBars, hole, bar.Center);
                        }
                    }
                }
            }
        }

        /// <summary>另一方向的条在这个交点处，其斜边侧端头到交点的距离（找不到时返回很大值）。</summary>
        private static double CounterEndDistance(List<Bar> crossBars, double holeCenter, Bar truncated)
        {
            var best = double.MaxValue;
            foreach (var cross in crossBars)
            {
                if (System.Math.Abs(cross.Center - holeCenter) > 1e-9)
                {
                    continue;
                }

                for (var s = 0; s < cross.Segments.Count && s < cross.BandCuts.Count; s++)
                {
                    var segment = cross.Segments[s];
                    if (truncated.Center < segment.A - 1e-9 || truncated.Center > segment.B + 1e-9)
                    {
                        continue;
                    }

                    var cut = cross.BandCuts[s];
                    if (cut.StartSlanted)
                    {
                        best = System.Math.Min(best, truncated.Center - segment.A);
                    }

                    if (cut.EndSlanted)
                    {
                        best = System.Math.Min(best, segment.B - truncated.Center);
                    }
                }
            }

            return best;
        }

        /// <summary>某一方向条厚（按交点找条）。</summary>
        private static double CounterThickness(List<Bar> crossBars, double holeCenter)
        {
            foreach (var cross in crossBars)
            {
                if (System.Math.Abs(cross.Center - holeCenter) <= 1e-9)
                {
                    return cross.Thickness;
                }
            }

            return 5.0;
        }

        /// <summary>去掉端距不足最小孔距的孔（打不了孔）。</summary>
        private static void RemoveTooCloseHoles(Bar bar, Spec spec)
        {
            var minHole = spec.FrameT + bar.Thickness / 2.0;
            for (var s = 0; s < bar.Segments.Count && s < bar.HoleGroups.Count; s++)
            {
                var segment = bar.Segments[s];
                var holes = bar.HoleGroups[s];
                holes.RemoveAll(hole => hole - segment.A < minHole - 1e-9 || segment.B - hole < minHole - 1e-9);
            }
        }

        /// <summary>去掉另一方向条在某个交点上的孔。</summary>
        private static void RemoveHole(List<Bar> crossBars, double centerToFind, double junction)
        {
            foreach (var cross in crossBars)
            {
                if (System.Math.Abs(cross.Center - centerToFind) > 1e-9)
                {
                    continue;
                }

                for (var s = 0; s < cross.Segments.Count && s < cross.HoleGroups.Count; s++)
                {
                    var segment = cross.Segments[s];
                    if (junction < segment.A - 1e-9 || junction > segment.B + 1e-9)
                    {
                        continue;
                    }

                    cross.HoleGroups[s].RemoveAll(value => System.Math.Abs(value - junction) <= 1e-9);
                    return;
                }
            }
        }

        /// <summary>把某根条在斜边侧的端头断开到交点另一侧（断口落在交点条的内侧边）。</summary>
        private static void ShortenBarAt(
            List<Bar> bars,
            double centerToFind,
            double junction,
            double junctionThickness,
            string allowedOrientation)
        {
            foreach (var cross in bars)
            {
                if (cross.Orientation != allowedOrientation)
                {
                    continue;
                }

                if (System.Math.Abs(cross.Center - centerToFind) > 1e-9)
                {
                    continue;
                }

                for (var s = 0; s < cross.Segments.Count; s++)
                {
                    var segment = cross.Segments[s];
                    if (junction < segment.A - 1e-9 || junction > segment.B + 1e-9)
                    {
                        continue;
                    }

                    var toStart = junction - segment.A;
                    var toEnd = segment.B - junction;
                    var updated = toStart <= toEnd
                        ? new Segment(junction + junctionThickness / 2.0, segment.B)
                        : new Segment(segment.A, junction - junctionThickness / 2.0);

                    if (updated.Length <= 1e-9)
                    {
                        cross.Segments.RemoveAt(s);
                        if (s < cross.BandCuts.Count)
                        {
                            cross.BandCuts.RemoveAt(s);
                        }
                    }
                    else
                    {
                        cross.Segments[s] = updated;
                        cross.Full = false;
                        if (s < cross.BandCuts.Count)
                        {
                            var cut = cross.BandCuts[s];
                            cross.BandCuts[s] = new BandCut(updated.A, updated.B, cut.Rect, cut.RawStart, cut.RawEnd);
                        }
                    }

                    return;
                }
            }
        }

        private static List<Bar> BuildBars(
            Polygon net,
            List<Polygon> netHoles,
            List<double> centers,
            BarSpec barSpec,
            BandAxis axis,
            double netSpan,
            double clearance,
            bool curvedOutline)
        {
            var orientation = axis == BandAxis.Vertical ? "vertical" : "horizontal";
            var bars = new List<Bar>();
            for (var i = 0; i < centers.Count; i++)
            {
                var center = centers[i];
                var cuts = curvedOutline
                    ? net.ClipBandIntervals(center, barSpec.Thickness / 2.0, axis)
                    : SubtractHoles(
                        net.ClipBand(center, barSpec.Thickness / 2.0, axis),
                        netHoles,
                        center,
                        barSpec.Thickness / 2.0,
                        axis,
                        clearance);
                var segments = new List<Segment>();
                foreach (var cut in cuts)
                {
                    segments.Add(new Segment(cut.A, cut.B));
                }

                var full = cuts.Count == 1 && Math.Abs(cuts[0].Length - netSpan) <= Eps;
                var bar = new Bar(orientation, i + 1, center, barSpec.Thickness, segments, full);
                foreach (var cut in cuts)
                {
                    bar.BandCuts.Add(cut);
                }

                bars.Add(bar);
            }

            return bars;
        }
    }
}
