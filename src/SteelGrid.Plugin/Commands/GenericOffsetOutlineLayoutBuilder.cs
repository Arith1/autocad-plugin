using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;

namespace SteelGrid.Plugin.Commands
{
    /// <summary>
    /// 对无法走既有形状分类的闭合多段线，直接按缩尺和边框厚度生成两级内偏移。
    /// CAD 原生偏移负责保持直线/圆弧，Core 负责条带裁切、边框拆料和报表。
    /// </summary>
    internal static class GenericOffsetOutlineLayoutBuilder
    {
        private const double ChordTolerance = 0.05;
        private const double Tol = 1e-5;

        private sealed class PairedContours
        {
            public List<Point2D> PlatePoints;
            public List<Point2D> NetPoints;
            public List<bool> CurvedEdges;
            public List<Tuple<CircularArc, CircularArc, CircularArc>> Arcs;
            public double Winding;
        }

        public static OutlineLayoutResult Build(Polyline source, Spec spec)
        {
            if (source == null || !source.Closed)
            {
                throw new ArgumentException("需要一条闭合多段线作为外轮廓");
            }

            if (source.NumberOfVertices < 2)
            {
                throw new ArgumentException("轮廓顶点不足，无法生成边框");
            }

            var sourceSamples = Sample(source);
            var sourceRegion = new Polygon(sourceSamples);
            if (sourceRegion.IsEmpty || sourceRegion.Area <= Tol)
            {
                throw new ArgumentException("原始轮廓面积为零，无法生成边框");
            }

            EnsureSimple(sourceSamples, "原始轮廓");

            Polyline plateCurve = null;
            Polyline netCurve = null;
            try
            {
                plateCurve = OffsetInward(source, sourceRegion, spec.Shrink, "缩尺");
                var plateSamples = Sample(plateCurve);
                EnsureSimple(plateSamples, "缩尺外框");
                var plateRegion = new Polygon(plateSamples);
                netCurve = OffsetInward(plateCurve, plateRegion, spec.FrameT, "边框厚度");
                EnsureSimple(Sample(netCurve), "边框内框");

                var paired = SamplePaired(source, plateCurve, netCurve);
                var barSpec = CopyBarSpec(spec);
                var direct = DirectOutlineLayoutEngine.Layout(
                    paired.NetPoints,
                    StraightBulges(paired.NetPoints.Count),
                    true,
                    barSpec);

                var shape = direct.Shape;
                var plateLocalRaw = ToLocal(paired.PlatePoints, shape);
                var netLocalRaw = ToLocal(paired.NetPoints, shape);
                var plate = new Polygon(plateLocalRaw);
                var net = direct.Plate;
                if (plate.Count != net.Count)
                {
                    throw new ArgumentException("缩尺外框与排条内框的离散点数不一致，不能可靠对应边框料");
                }

                var plateMask = ReorderMask(plateLocalRaw, paired.CurvedEdges, plate.Vertices);
                var netMask = ReorderMask(netLocalRaw, paired.CurvedEdges, net.Vertices);
                for (var i = 0; i < plateMask.Count; i++)
                {
                    if (plateMask[i] != netMask[i])
                    {
                        throw new ArgumentException("偏移前后圆弧与直边的顺序发生变化，无法拆分边框料");
                    }
                }

                var curvedPieces = BuildCurvedPieces(paired, shape);
                return OutlineLayoutEngine.LayoutFromOffsetContours(
                    shape,
                    spec,
                    plate,
                    net,
                    plateMask,
                    curvedPieces);
            }
            finally
            {
                if (netCurve != null) netCurve.Dispose();
                if (plateCurve != null) plateCurve.Dispose();
            }
        }

        private static Spec CopyBarSpec(Spec source)
        {
            return new Spec
            {
                OpeningW = source.OpeningW,
                OpeningH = source.OpeningH,
                Shrink = 0.0,
                FrameT = 0.0,
                LoadDirection = source.LoadDirection,
                Vertical = source.Vertical,
                Horizontal = source.Horizontal,
                Notches = new List<Notch>()
            };
        }

        private static Polyline OffsetInward(Polyline source, Polygon sourceRegion, double distance, string stage)
        {
            if (distance <= Tol)
            {
                var clone = source.Clone() as Polyline;
                if (clone == null)
                {
                    throw new ArgumentException(stage + "失败：无法复制轮廓");
                }

                return clone;
            }

            var candidates = new List<Polyline>();
            foreach (var signedDistance in new[] { distance, -distance })
            {
                DBObjectCollection offsetCurves;
                try
                {
                    offsetCurves = source.GetOffsetCurves(signedDistance);
                }
                catch
                {
                    continue;
                }

                foreach (DBObject item in offsetCurves)
                {
                    var candidate = item as Polyline;
                    if (candidate == null)
                    {
                        item.Dispose();
                        continue;
                    }

                    if (!candidate.Closed || candidate.NumberOfVertices < 2)
                    {
                        candidate.Dispose();
                        continue;
                    }

                    var sample = Sample(candidate);
                    var region = new Polygon(sample);
                    if (region.IsEmpty || region.Area >= sourceRegion.Area - Tol)
                    {
                        candidate.Dispose();
                        continue;
                    }

                    var contained = true;
                    foreach (var point in sample)
                    {
                        if (!sourceRegion.Contains(point))
                        {
                            contained = false;
                            break;
                        }
                    }

                    if (!contained)
                    {
                        candidate.Dispose();
                        continue;
                    }

                    var aligned = AlignCandidateToSource(source, candidate);
                    if (!ReferenceEquals(aligned, candidate)) candidate.Dispose();
                    candidates.Add(aligned);
                }
            }

            if (candidates.Count != 1)
            {
                foreach (var candidate in candidates) candidate.Dispose();
                throw new ArgumentException(candidates.Count == 0
                    ? stage + "失败：没有找到位于原轮廓内部的有效偏移线"
                    : stage + "失败：偏移后轮廓分裂，存在多个内部轮廓，当前无法唯一确定排条区域");
            }

            return candidates[0];
        }

        private static Polyline AlignCandidateToSource(Polyline source, Polyline candidate)
        {
            var count = source.NumberOfVertices;
            if (count != candidate.NumberOfVertices)
            {
                candidate.Dispose();
                throw new ArgumentException("偏移改变了轮廓边数，无法可靠配对直料和弧料");
            }

            var sourceWinding = Math.Sign(SignedArea(Sample(source)));
            var candidateWinding = Math.Sign(SignedArea(Sample(candidate)));
            var direction = sourceWinding == candidateWinding ? 1 : -1;
            var sourceStart = GetPoint(source, 0);
            var sourceStartIsArc = Math.Abs(source.GetBulgeAt(0)) > 1e-9;
            var start = -1;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var edge = direction > 0 ? i : (i + count - 1) % count;
                var candidateIsArc = Math.Abs(candidate.GetBulgeAt(edge)) > 1e-9;
                if (candidateIsArc != sourceStartIsArc) continue;

                var point = GetPoint(candidate, i);
                var dx = point.X - sourceStart.X;
                var dy = point.Y - sourceStart.Y;
                var distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    start = i;
                }
            }

            if (start < 0)
            {
                candidate.Dispose();
                throw new ArgumentException("偏移后的起始边无法与原轮廓对应");
            }

            var aligned = new Polyline();
            for (var i = 0; i < count; i++)
            {
                var index = (start + direction * i + count * 2) % count;
                var point = candidate.GetPoint2dAt(index);
                var bulge = direction > 0
                    ? candidate.GetBulgeAt(index)
                    : -candidate.GetBulgeAt((index + count - 1) % count);
                aligned.AddVertexAt(i, point, bulge, 0.0, 0.0);
            }

            aligned.Closed = true;
            return aligned;
        }

        private static PairedContours SamplePaired(Polyline source, Polyline plate, Polyline net)
        {
            if (source.NumberOfVertices != plate.NumberOfVertices
                || plate.NumberOfVertices != net.NumberOfVertices)
            {
                throw new ArgumentException("缩尺或边框厚度偏移改变了轮廓边数，无法逐边拆分下料");
            }

            var result = new PairedContours
            {
                PlatePoints = new List<Point2D>(),
                NetPoints = new List<Point2D>(),
                CurvedEdges = new List<bool>(),
                Arcs = new List<Tuple<CircularArc, CircularArc, CircularArc>>(),
                Winding = Math.Sign(SignedArea(Sample(source)))
            };

            var edgeCount = source.NumberOfVertices;
            for (var i = 0; i < edgeCount; i++)
            {
                var sourceA = GetPoint(source, i);
                var sourceB = GetPoint(source, (i + 1) % edgeCount);
                var plateA = GetPoint(plate, i);
                var plateB = GetPoint(plate, (i + 1) % edgeCount);
                var netA = GetPoint(net, i);
                var netB = GetPoint(net, (i + 1) % edgeCount);
                var sourceBulge = source.GetBulgeAt(i);
                var plateBulge = plate.GetBulgeAt(i);
                var netBulge = net.GetBulgeAt(i);
                var isArc = Math.Abs(sourceBulge) > 1e-9;
                if (isArc != (Math.Abs(plateBulge) > 1e-9)
                    || isArc != (Math.Abs(netBulge) > 1e-9))
                {
                    throw new ArgumentException("偏移改变了直边/圆弧类型，无法对应边框材料");
                }

                if (!isArc)
                {
                    ValidateParallel(sourceA, sourceB, plateA, plateB, "缩尺");
                    ValidateParallel(plateA, plateB, netA, netB, "边框厚度");
                    result.PlatePoints.Add(plateA);
                    result.NetPoints.Add(netA);
                    result.CurvedEdges.Add(false);
                    continue;
                }

                var sourceArc = CircularArc.FromBulge(sourceA, sourceB, sourceBulge);
                var plateArc = CircularArc.FromBulge(plateA, plateB, plateBulge);
                var netArc = CircularArc.FromBulge(netA, netB, netBulge);
                if (Distance(sourceArc.Center, plateArc.Center) > 0.05
                    || Distance(plateArc.Center, netArc.Center) > 0.05
                    || Math.Sign(sourceArc.SweepAngle) != Math.Sign(plateArc.SweepAngle)
                    || Math.Sign(sourceArc.SweepAngle) != Math.Sign(netArc.SweepAngle))
                {
                    throw new ArgumentException("圆弧偏移后圆心或方向改变，无法唯一对应弧形边框");
                }

                var subdivisions = Math.Max(
                    plateArc.Tessellate(ChordTolerance).Count - 1,
                    netArc.Tessellate(ChordTolerance).Count - 1);
                subdivisions = Math.Max(1, subdivisions);
                for (var p = 0; p < subdivisions; p++)
                {
                    var fraction = (double)p / subdivisions;
                    result.PlatePoints.Add(plateArc.PointAt(fraction));
                    result.NetPoints.Add(netArc.PointAt(fraction));
                    result.CurvedEdges.Add(true);
                }

                result.Arcs.Add(Tuple.Create(sourceArc, plateArc, netArc));
            }

            if (result.PlatePoints.Count < 3 || result.PlatePoints.Count != result.NetPoints.Count)
            {
                throw new ArgumentException("偏移后有效边不足，无法形成边框带");
            }

            return result;
        }

        private static void ValidateParallel(Point2D sourceA, Point2D sourceB, Point2D offsetA, Point2D offsetB, string stage)
        {
            var sx = sourceB.X - sourceA.X;
            var sy = sourceB.Y - sourceA.Y;
            var ox = offsetB.X - offsetA.X;
            var oy = offsetB.Y - offsetA.Y;
            var sl = Math.Sqrt(sx * sx + sy * sy);
            var ol = Math.Sqrt(ox * ox + oy * oy);
            if (sl <= Tol || ol <= Tol || (sx * ox + sy * oy) <= 0.0
                || Math.Abs(sx * oy - sy * ox) / (sl * ol) > 1e-5)
            {
                throw new ArgumentException(stage + "偏移改变了直边方向或长度退化");
            }
        }

        private static List<CurvedFramePiece> BuildCurvedPieces(PairedContours contours, OutlineShape shape)
        {
            var result = new List<CurvedFramePiece>();
            var sourceArcs = contours.Arcs;
            if (sourceArcs.Count == 0)
            {
                return result;
            }

            if (IsFullCircularContour(contours))
            {
                var first = sourceArcs[0];
                var sweep = 0.0;
                foreach (var arc in sourceArcs) sweep += arc.Item1.SweepAngle;
                var plate = new CircularArc(first.Item2.Center, first.Item2.Radius,
                    first.Item2.StartAngle, sweep);
                var net = new CircularArc(first.Item3.Center, first.Item3.Radius,
                    first.Item3.StartAngle, sweep);
                var sign = Math.Sign(plate.Radius - first.Item1.Radius);
                result.Add(new CurvedFramePiece(ToLocal(plate, shape), ToLocal(net, shape), sign));
                return result;
            }

            foreach (var item in sourceArcs)
            {
                var sign = Math.Sign(item.Item2.Radius - item.Item1.Radius);
                if (sign == 0)
                {
                    sign = (int)(-Math.Sign(item.Item1.SweepAngle) * contours.Winding);
                }

                result.Add(new CurvedFramePiece(
                    ToLocal(item.Item2, shape),
                    ToLocal(item.Item3, shape),
                    sign));
            }

            return result;
        }

        private static bool IsFullCircularContour(PairedContours contours)
        {
            if (contours.CurvedEdges.Count == 0 || contours.Arcs.Count < 2)
            {
                return false;
            }

            foreach (var curved in contours.CurvedEdges)
            {
                if (!curved) return false;
            }

            var first = contours.Arcs[0];
            var sweep = 0.0;
            foreach (var arc in contours.Arcs)
            {
                if (Distance(first.Item1.Center, arc.Item1.Center) > 0.05
                    || Distance(first.Item2.Center, arc.Item2.Center) > 0.05
                    || Distance(first.Item3.Center, arc.Item3.Center) > 0.05
                    || Math.Abs(first.Item1.Radius - arc.Item1.Radius) > 0.05
                    || Math.Sign(first.Item1.SweepAngle) != Math.Sign(arc.Item1.SweepAngle))
                {
                    return false;
                }

                sweep += arc.Item1.SweepAngle;
            }

            return Math.Abs(Math.Abs(sweep) - 2.0 * Math.PI) <= 1e-4;
        }

        private static CircularArc ToLocal(CircularArc arc, OutlineShape shape)
        {
            return new CircularArc(shape.ToLocal(arc.Center), arc.Radius, arc.StartAngle, arc.SweepAngle);
        }

        private static List<Point2D> ToLocal(IReadOnlyList<Point2D> points, OutlineShape shape)
        {
            var result = new List<Point2D>();
            foreach (var point in points) result.Add(shape.ToLocal(point));
            return result;
        }

        private static List<bool> ReorderMask(
            IReadOnlyList<Point2D> rawPoints,
            IReadOnlyList<bool> rawMask,
            IReadOnlyList<Point2D> polygonPoints)
        {
            if (rawPoints.Count != rawMask.Count || rawPoints.Count != polygonPoints.Count)
            {
                throw new ArgumentException("离散弧段边数与偏移轮廓不一致");
            }

            var result = new List<bool>();
            for (var i = 0; i < polygonPoints.Count; i++)
            {
                var a = polygonPoints[i];
                var b = polygonPoints[(i + 1) % polygonPoints.Count];
                var found = false;
                for (var j = 0; j < rawPoints.Count; j++)
                {
                    var next = (j + 1) % rawPoints.Count;
                    if (Same(a, rawPoints[j]) && Same(b, rawPoints[next]))
                    {
                        result.Add(rawMask[j]);
                        found = true;
                        break;
                    }

                    if (Same(a, rawPoints[next]) && Same(b, rawPoints[j]))
                    {
                        result.Add(rawMask[j]);
                        found = true;
                        break;
                    }
                }

                if (!found) throw new ArgumentException("离散轮廓顺序变化，无法映射圆弧边");
            }

            return result;
        }

        private static List<Point2D> Sample(Polyline polyline)
        {
            var result = new List<Point2D>();
            for (var i = 0; i < polyline.NumberOfVertices; i++)
            {
                var start = GetPoint(polyline, i);
                var end = GetPoint(polyline, (i + 1) % polyline.NumberOfVertices);
                var bulge = polyline.GetBulgeAt(i);
                if (Math.Abs(bulge) <= 1e-9)
                {
                    AddDistinct(result, start);
                    continue;
                }

                var arc = CircularArc.FromBulge(start, end, bulge);
                var points = arc.Tessellate(ChordTolerance);
                for (var p = 0; p < points.Count - 1; p++) AddDistinct(result, points[p]);
            }

            if (result.Count > 1 && Same(result[0], result[result.Count - 1])) result.RemoveAt(result.Count - 1);
            return result;
        }

        private static List<double> StraightBulges(int count)
        {
            var result = new List<double>();
            for (var i = 0; i < count; i++) result.Add(0.0);
            return result;
        }

        private static Point2D GetPoint(Polyline polyline, int index)
        {
            var point = polyline.GetPoint2dAt(index);
            return new Point2D(point.X, point.Y);
        }

        private static double SignedArea(IReadOnlyList<Point2D> points)
        {
            var sum = 0.0;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2.0;
        }

        private static void EnsureSimple(IReadOnlyList<Point2D> points, string stage)
        {
            if (points == null || points.Count < 3)
            {
                throw new ArgumentException(stage + "有效边不足");
            }

            for (var i = 0; i < points.Count; i++)
            {
                var iNext = (i + 1) % points.Count;
                for (var j = i + 1; j < points.Count; j++)
                {
                    var jNext = (j + 1) % points.Count;
                    if (i == j || iNext == j || jNext == i) continue;
                    if (SegmentsIntersect(points[i], points[iNext], points[j], points[jNext]))
                    {
                        throw new ArgumentException(stage + "发生自交或边界接触，不能安全生成边框");
                    }
                }
            }
        }

        private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d)
        {
            const double epsilon = 1e-8;
            var abC = Cross(a, b, c);
            var abD = Cross(a, b, d);
            var cdA = Cross(c, d, a);
            var cdB = Cross(c, d, b);
            if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
                && ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon)))
            {
                return true;
            }

            return (Math.Abs(abC) <= epsilon && OnSegment(a, b, c, epsilon))
                || (Math.Abs(abD) <= epsilon && OnSegment(a, b, d, epsilon))
                || (Math.Abs(cdA) <= epsilon && OnSegment(c, d, a, epsilon))
                || (Math.Abs(cdB) <= epsilon && OnSegment(c, d, b, epsilon));
        }

        private static double Cross(Point2D a, Point2D b, Point2D c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static bool OnSegment(Point2D a, Point2D b, Point2D p, double epsilon)
        {
            return p.X >= Math.Min(a.X, b.X) - epsilon
                && p.X <= Math.Max(a.X, b.X) + epsilon
                && p.Y >= Math.Min(a.Y, b.Y) - epsilon
                && p.Y <= Math.Max(a.Y, b.Y) + epsilon;
        }

        private static void AddDistinct(List<Point2D> points, Point2D point)
        {
            if (points.Count == 0 || !Same(points[points.Count - 1], point)) points.Add(point);
        }

        private static bool Same(Point2D a, Point2D b)
        {
            return Distance(a, b) <= Tol;
        }

        private static double Distance(Point2D a, Point2D b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
