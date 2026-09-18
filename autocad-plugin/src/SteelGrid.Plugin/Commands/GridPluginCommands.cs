using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelGrid.Core.Geometry;
using SteelGrid.Core.Layout;
using SteelGrid.Core.Model;
using SteelGrid.Core.Report;
using SteelGrid.Plugin.UI;

namespace SteelGrid.Plugin.Commands
{
    /// <summary>AutoCAD 命令入口。</summary>
    public static class GridPluginCommands
    {
        private static GridSettingsData _savedSettings = GridSettingsStore.Load();
        private static UI.GridSettingsForm _settingsForm;
        private const double BarLabelOffset = 50.0;
        private const double BorderDimensionOffset = 110.0;
        private const double BorderTextExtraOffset = 40.0;

        [CommandMethod("GPGRIDINFO")]
        public static void ShowInfo()
        {
            var editor = GetEditor();
            editor.WriteMessage("\n钢格板自动排条插件 v0.16：支持扁钢 / 扭绞方钢。");
        }

        [CommandMethod("GPGRIDSET")]
        public static void OpenSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new UI.GridSettingsForm(
                    _savedSettings,
                    settings =>
                    {
                        _savedSettings = settings;
                        GridSettingsStore.Save(settings);
                    });
            }

            if (_settingsForm.Visible)
            {
                _settingsForm.Activate();
            }
            else
            {
                Application.ShowModalDialog(_settingsForm);
            }
        }

        [CommandMethod("GPGRID")]
        public static void RunGridLayout()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                return;
            }

            var editor = document.Editor;
            var selection = SelectClosedPolyline(editor);
            if (selection == null)
            {
                return;
            }

            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var outline = transaction.GetObject(selection.ObjectId, OpenMode.ForRead) as Polyline;
                if (outline == null)
                {
                    editor.WriteMessage("\n未选中闭合多段线。");
                    return;
                }

                var spec = BuildSpec(outline);
                editor.WriteMessage(
                    "\n识别：板件 {0:0.##} x {1:0.##}，缺口 {2} 个，受力 {3}",
                    spec.PlateW,
                    spec.PlateH,
                    spec.Notches.Count,
                    spec.LoadDirection == LoadDirection.Vertical ? "垂直" : "水平");
                for (var i = 0; i < spec.Notches.Count; i++)
                {
                    var notch = spec.Notches[i];
                    editor.WriteMessage(
                        "\n  缺口{0}：{1} 起 {2:0.##} 宽 {3:0.##} 深 {4:0.##}",
                        i + 1,
                        notch.Edge,
                        notch.Start,
                        notch.Width,
                        notch.Depth);
                }

                var result = LayoutEngine.Layout(spec);
                var placement = editor.GetPoint(new PromptPointOptions("\n指定排条图左上角位置"));
                if (placement.Status != PromptStatus.OK)
                {
                    return;
                }

                try
                {
                    var frameCount = DrawResult(
                        document.Database,
                        transaction,
                        result,
                        outline,
                        placement.Value);
                    transaction.Commit();
                    editor.WriteMessage(
                        $"\n排条完成：纵={spec.Vertical.TypeName}，横={spec.Horizontal.TypeName}；" +
                        $"边框料 {frameCount} 个矩形，段数 {result.SegmentCount}");
                }
                catch (System.Exception ex)
                {
                    transaction.Abort();
                    editor.WriteMessage("\n排条绘制失败：{0}", ex.Message);
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        editor.WriteMessage("\n{0}", ex.StackTrace);
                    }
                }
            }
        }

        private static PromptEntityResult SelectClosedPolyline(Editor editor)
        {
            var options = new PromptEntityOptions("\n选择闭合多段线")
            {
                AllowNone = false
            };
            options.SetRejectMessage("必须选择闭合 LWPOLYLINE");
            options.AddAllowedClass(typeof(Polyline), true);
            return editor.GetEntity(options);
        }

        private static Spec BuildSpec(Polyline outline)
        {
            var bounds = outline.Bounds.Value;
            var width = bounds.MaxPoint.X - bounds.MinPoint.X;
            var height = bounds.MaxPoint.Y - bounds.MinPoint.Y;
            var notches = OutlineReader.ReadNotches(outline);
            var spec = _savedSettings.ToSpec(width, height, notches.ToArray());

            return spec;
        }

        private static Point3d GetOrigin(Polyline outline)
        {
            var bounds = outline.Bounds.Value;
            return new Point3d(bounds.MinPoint.X, bounds.MinPoint.Y, 0.0);
        }

        private static int DrawResult(
            Database database,
            Transaction transaction,
            LayoutResult result,
            Polyline sourceOutline,
            Point3d insertionPoint)
        {
            var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
            var layerColors = new Dictionary<string, short>
            {
                { "外轮廓", 1 },
                { "内边框", 5 },
                { "纵条", 3 },
                { "横条", 6 },
                { "边框标注", 5 },
                { "纵条标注", 3 },
                { "横条标注", 6 },
                { "首尾孔距标注", 30 },
                { "表格", 7 },
                { "分组边框", 8 }
            };

            foreach (var item in layerColors)
            {
                if (!layerTable.Has(item.Key))
                {
                    var layer = new LayerTableRecord { Name = item.Key };
                    layer.Color = Color.FromColorIndex(ColorMethod.ByAci, item.Value);
                    layerTable.Add(layer);
                    transaction.AddNewlyCreatedDBObject(layer, true);
                }
            }

            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var plateHeight = result.Geometry.Plate.H;

            // 边框按受力方向拆成独立边框料，能看出哪条边包住哪条边。
            var frameCount = DrawFramePieces(modelSpace, transaction, result, insertionPoint, plateHeight);

            // 外边框的每一段都标注尺寸，含缺口侧边和封头边。
            DrawBorderDimensions(
                modelSpace,
                transaction,
                result.Geometry,
                GeometryOutlines.PlateOutline(result.Geometry),
                insertionPoint,
                plateHeight);

            var verticalLayer = "纵条";
            var horizontalLayer = "横条";
            foreach (var bar in result.VerticalBars)
            {
                foreach (var segment in bar.Segments)
                {
                    var entity = CreateRectangle(
                        insertionPoint.X + bar.Center - bar.Thickness / 2.0,
                        ToCadY(insertionPoint.Y, plateHeight, segment.B),
                        bar.Thickness,
                        segment.Length,
                        verticalLayer);
                    modelSpace.AppendEntity(entity);
                    transaction.AddNewlyCreatedDBObject(entity, true);

                    if (bar.Full)
                    {
                        AddLengthText(
                            modelSpace,
                            transaction,
                            insertionPoint.X + bar.Center,
                            ToCadY(insertionPoint.Y, plateHeight, result.Geometry.Plate.H + BarLabelOffset),
                            segment.Length,
                            true,
                            "纵条标注");
                    }
                    else
                    {
                        AddLengthText(
                            modelSpace,
                            transaction,
                            insertionPoint.X + bar.Center,
                            ToCadY(insertionPoint.Y, plateHeight, (segment.A + segment.B) / 2.0),
                            segment.Length,
                            true,
                            "纵条标注");
                    }
                }
            }

            foreach (var bar in result.HorizontalBars)
            {
                foreach (var segment in bar.Segments)
                {
                    var entity = CreateRectangle(
                        insertionPoint.X + segment.A,
                        ToCadY(insertionPoint.Y, plateHeight, bar.Center) - bar.Thickness / 2.0,
                        segment.Length,
                        bar.Thickness,
                        horizontalLayer);
                    modelSpace.AppendEntity(entity);
                    transaction.AddNewlyCreatedDBObject(entity, true);

                    if (bar.Full)
                    {
                        AddLengthText(
                            modelSpace,
                            transaction,
                            insertionPoint.X + result.Geometry.Plate.W + BarLabelOffset,
                            ToCadY(insertionPoint.Y, plateHeight, bar.Center),
                            segment.Length,
                            false,
                            "横条标注");
                    }
                    else
                    {
                        AddLengthText(
                            modelSpace,
                            transaction,
                            insertionPoint.X + (segment.A + segment.B) / 2.0,
                            ToCadY(insertionPoint.Y, plateHeight, bar.Center),
                            segment.Length,
                            false,
                            "横条标注");
                    }
                }
            }

            var holeAnnotations = ReportTables.HoleAnnotations(result);
            var horizontalTwisted = result.Spec.Horizontal.Type == BarType.TwistedSquare;
            var verticalTwisted = result.Spec.Vertical.Type == BarType.TwistedSquare;
            foreach (var annotation in holeAnnotations)
            {
                if (annotation.Direction == "横向" && horizontalTwisted)
                {
                    continue;
                }

                if (annotation.Direction == "纵向" && verticalTwisted)
                {
                    continue;
                }

                if (annotation.Direction == "横向")
                {
                    var dimensionY = ToCadY(insertionPoint.Y, plateHeight, annotation.BarCenter - annotation.BarThickness / 2.0 - 10.0);
                    AddLine(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.SegmentA,
                        dimensionY,
                        insertionPoint.X + annotation.FirstPosition,
                        dimensionY,
                        "首尾孔距标注");
                    AddLine(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.LastPosition,
                        dimensionY,
                        insertionPoint.X + annotation.SegmentB,
                        dimensionY,
                        "首尾孔距标注");
                    AddText(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.FirstPosition,
                        dimensionY + 8.0,
                        "首" + ReportTables.Format(annotation.FirstHole),
                        17.0,
                        "首尾孔距标注",
                        true);
                    AddText(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.LastPosition,
                        dimensionY + 8.0,
                        "尾" + ReportTables.Format(annotation.LastHole),
                        17.0,
                        "首尾孔距标注",
                        true);
                }
                else
                {
                    var dimensionX = insertionPoint.X + annotation.BarCenter - annotation.BarThickness / 2.0 - 8.0;
                    AddLine(
                        modelSpace,
                        transaction,
                        dimensionX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.SegmentA),
                        dimensionX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.FirstPosition),
                        "首尾孔距标注");
                    AddLine(
                        modelSpace,
                        transaction,
                        dimensionX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.LastPosition),
                        dimensionX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.SegmentB),
                        "首尾孔距标注");
                    AddText(
                        modelSpace,
                        transaction,
                        dimensionX - 6.0,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.FirstPosition),
                        "首" + ReportTables.Format(annotation.FirstHole),
                        17.0,
                        "首尾孔距标注",
                        true);
                    AddText(
                        modelSpace,
                        transaction,
                        dimensionX - 6.0,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.LastPosition),
                        "尾" + ReportTables.Format(annotation.LastHole),
                        17.0,
                        "首尾孔距标注",
                        true);
                }
            }

            var plate = result.Geometry.Plate;
            var tableLeft = insertionPoint.X + plate.W + 260.0;
            var tableWidth = 770.0;
            var tableBottom = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                insertionPoint.Y,
                ReportTables.FrameTable(result),
                "边框下料（共 " + ReportTables.FrameTable(result).Sum(item => item.Count) + " 根）");
            tableBottom = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                tableBottom - 80.0,
                ReportTables.ReportTable(result, "纵向"),
                "纵向扁钢下料（共 " + ReportTables.ReportTable(result, "纵向").Sum(item => item.Count) + " 根）");
            tableBottom = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                tableBottom - 80.0,
                PluginTableRows.GetHorizontalRows(result),
                "横向扁钢下料（共 " + PluginTableRows.GetHorizontalRows(result).Sum(item => item.Count) + " 根）");

            var groupLeft = insertionPoint.X - 260.0;
            var groupRight = tableLeft + tableWidth + 60.0;
            var groupTop = insertionPoint.Y + 220.0;
            var groupBottom = Math.Min(insertionPoint.Y - plate.H - 220.0, tableBottom - 60.0);
            var border = CreateRectangle(
                groupLeft,
                groupBottom,
                groupRight - groupLeft,
                groupTop - groupBottom,
                "分组边框");
            modelSpace.AppendEntity(border);
            transaction.AddNewlyCreatedDBObject(border, true);
            return frameCount;
        }

        private static int DrawFramePieces(
            BlockTableRecord modelSpace,
            Transaction transaction,
            LayoutResult result,
            Point3d insertionPoint,
            double plateHeight)
        {
            var t = result.Spec.FrameT;
            var plate = result.Geometry.Plate;
            var w = plate.W;
            var h = plate.H;
            var rects = new List<Rect>();

            var topNotches = new List<NotchGeo>();
            var bottomNotches = new List<NotchGeo>();
            var leftNotches = new List<NotchGeo>();
            var rightNotches = new List<NotchGeo>();
            foreach (var notch in result.Geometry.Notches)
            {
                if (notch.Source.Edge == "top")
                {
                    topNotches.Add(notch);
                }
                else if (notch.Source.Edge == "bottom")
                {
                    bottomNotches.Add(notch);
                }
                else if (notch.Source.Edge == "left")
                {
                    leftNotches.Add(notch);
                }
                else if (notch.Source.Edge == "right")
                {
                    rightNotches.Add(notch);
                }
            }

            var verticalForce = result.Spec.LoadDirection == LoadDirection.Vertical;
            if (verticalForce)
            {
                var topSpans = SubtractSpans(
                    0.0,
                    w,
                    topNotches.Select(item => new Segment(item.Clear.X0, item.Clear.X1)).ToList());
                foreach (var span in topSpans)
                {
                    rects.Add(new Rect(span.A, 0.0, span.B, t));
                }

                var bottomSpans = SubtractSpans(
                    0.0,
                    w,
                    bottomNotches.Select(item => new Segment(item.Clear.X0, item.Clear.X1)).ToList());
                foreach (var span in bottomSpans)
                {
                    rects.Add(new Rect(span.A, h - t, span.B, h));
                }

                var leftSpans = SubtractSpans(
                    t,
                    h - t,
                    leftNotches.Select(item => new Segment(item.Clear.Y0, item.Clear.Y1)).ToList());
                foreach (var span in leftSpans)
                {
                    rects.Add(new Rect(0.0, span.A, t, span.B));
                }

                var rightSpans = SubtractSpans(
                    t,
                    h - t,
                    rightNotches.Select(item => new Segment(item.Clear.Y0, item.Clear.Y1)).ToList());
                foreach (var span in rightSpans)
                {
                    rects.Add(new Rect(w - t, span.A, w, span.B));
                }
            }
            else
            {
                var leftSpans = SubtractSpans(
                    0.0,
                    h,
                    leftNotches.Select(item => new Segment(item.Clear.Y0, item.Clear.Y1)).ToList());
                foreach (var span in leftSpans)
                {
                    rects.Add(new Rect(0.0, span.A, t, span.B));
                }

                var rightSpans = SubtractSpans(
                    0.0,
                    h,
                    rightNotches.Select(item => new Segment(item.Clear.Y0, item.Clear.Y1)).ToList());
                foreach (var span in rightSpans)
                {
                    rects.Add(new Rect(w - t, span.A, w, span.B));
                }

                var topSpans = SubtractSpans(
                    t,
                    w - t,
                    topNotches.Select(item => new Segment(item.Clear.X0 - t, item.Clear.X1 + t)).ToList());
                foreach (var span in topSpans)
                {
                    rects.Add(new Rect(span.A, 0.0, span.B, t));
                }

                var bottomSpans = SubtractSpans(
                    t,
                    w - t,
                    bottomNotches.Select(item => new Segment(item.Clear.X0 - t, item.Clear.X1 + t)).ToList());
                foreach (var span in bottomSpans)
                {
                    rects.Add(new Rect(span.A, h - t, span.B, h));
                }
            }

            foreach (var notch in topNotches)
            {
                var x0 = notch.Clear.X0;
                var x1 = notch.Clear.X1;
                var depth = notch.Clear.H;
                if (verticalForce)
                {
                    rects.Add(new Rect(x0 - t, t, x0, depth));
                    rects.Add(new Rect(x1, t, x1 + t, depth));
                }
                else
                {
                    rects.Add(new Rect(x0 - t, 0.0, x0, depth + t));
                    rects.Add(new Rect(x1, 0.0, x1 + t, depth + t));
                }

                if (verticalForce)
                {
                    rects.Add(new Rect(x0 - t, depth, x1 + t, depth + t));
                }
                else
                {
                    rects.Add(new Rect(x0, depth, x1, depth + t));
                }
            }

            foreach (var notch in bottomNotches)
            {
                var x0 = notch.Clear.X0;
                var x1 = notch.Clear.X1;
                var depth = notch.Clear.H;
                if (verticalForce)
                {
                    rects.Add(new Rect(x0 - t, h - depth, x0, h - t));
                    rects.Add(new Rect(x1, h - depth, x1 + t, h - t));
                }
                else
                {
                    rects.Add(new Rect(x0 - t, h - depth - t, x0, h - depth));
                    rects.Add(new Rect(x1, h - depth - t, x1 + t, h - depth));
                }

                if (verticalForce)
                {
                    rects.Add(new Rect(x0 - t, h - depth - t, x1 + t, h - depth));
                }
                else
                {
                    rects.Add(new Rect(x0, h - depth - t, x1, h - depth));
                }
            }

            foreach (var notch in leftNotches)
            {
                var y0 = notch.Clear.Y0;
                var y1 = notch.Clear.Y1;
                var depth = notch.Clear.W;
                rects.Add(new Rect(depth, y0, depth + t, y1));
                rects.Add(new Rect(0.0, y0 - t, depth, y0));
                rects.Add(new Rect(0.0, y1, depth, y1 + t));
            }

            foreach (var notch in rightNotches)
            {
                var y0 = notch.Clear.Y0;
                var y1 = notch.Clear.Y1;
                var depth = notch.Clear.W;
                rects.Add(new Rect(w - depth - t, y0, w - depth, y1));
                rects.Add(new Rect(w - depth, y0 - t, w, y0));
                rects.Add(new Rect(w - depth, y1, w, y1 + t));
            }

            var drawn = 0;
            foreach (var rect in rects)
            {
                if (rect.W <= 0.0 || rect.H <= 0.0)
                {
                    continue;
                }

                drawn++;
                var piece = CreateRectangle(
                    insertionPoint.X + rect.X0,
                    ToCadY(insertionPoint.Y, plateHeight, rect.Y1),
                    rect.W,
                    rect.H,
                    "外轮廓");
                modelSpace.AppendEntity(piece);
                transaction.AddNewlyCreatedDBObject(piece, true);
            }

            return drawn;
        }

        private static List<Segment> SubtractSpans(double start, double end, List<Segment> cuts)
        {
            var spans = new List<Segment> { new Segment(start, end) };
            foreach (var cut in cuts)
            {
                var nextSpans = new List<Segment>();
                foreach (var span in spans)
                {
                    if (span.B <= cut.A || span.A >= cut.B)
                    {
                        nextSpans.Add(span);
                        continue;
                    }

                    if (span.A < cut.A)
                    {
                        nextSpans.Add(new Segment(span.A, cut.A));
                    }

                    if (cut.B < span.B)
                    {
                        nextSpans.Add(new Segment(cut.B, span.B));
                    }
                }

                spans = nextSpans;
            }

            return spans;
        }

        private struct WallSegment
        {
            public WallSegment(double x0, double y0, double x1, double y1)
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
        }

        private static List<WallSegment> BuildNotchWalls(PlateGeometry geo)
        {
            var walls = new List<WallSegment>();
            var w = geo.Plate.W;
            var h = geo.Plate.H;
            foreach (var notch in geo.Notches)
            {
                var c = notch.Clear;
                var edge = notch.Source.Edge;
                if (edge == "top")
                {
                    walls.Add(new WallSegment(c.X0, 0.0, c.X0, c.H));
                    walls.Add(new WallSegment(c.X1, c.H, c.X1, 0.0));
                }
                else if (edge == "bottom")
                {
                    walls.Add(new WallSegment(c.X0, h, c.X0, h - c.H));
                    walls.Add(new WallSegment(c.X1, h - c.H, c.X1, h));
                }
                else if (edge == "left")
                {
                    walls.Add(new WallSegment(0.0, c.Y0, c.W, c.Y0));
                    walls.Add(new WallSegment(0.0, c.Y1, c.W, c.Y1));
                }
                else if (edge == "right")
                {
                    walls.Add(new WallSegment(w - c.W, c.Y0, w, c.Y0));
                    walls.Add(new WallSegment(w - c.W, c.Y1, w, c.Y1));
                }
            }

            return walls;
        }

        private static bool IsNotchWall(List<WallSegment> walls, double x0, double y0, double x1, double y1)
        {
            const double eps = 1e-6;
            foreach (var wall in walls)
            {
                var forward =
                    Math.Abs(x0 - wall.X0) <= eps
                    && Math.Abs(y0 - wall.Y0) <= eps
                    && Math.Abs(x1 - wall.X1) <= eps
                    && Math.Abs(y1 - wall.Y1) <= eps;
                var backward =
                    Math.Abs(x0 - wall.X1) <= eps
                    && Math.Abs(y0 - wall.Y1) <= eps
                    && Math.Abs(x1 - wall.X0) <= eps
                    && Math.Abs(y1 - wall.Y0) <= eps;
                if (forward || backward)
                {
                    return true;
                }
            }

            return false;
        }

        private static void DrawBorderDimensions(
            BlockTableRecord modelSpace,
            Transaction transaction,
            PlateGeometry geo,
            List<OutlinePoint> points,
            Point3d insertionPoint,
            double plateHeight)
        {
            if (points.Count < 2)
            {
                return;
            }

            var w = geo.Plate.W;
            var h = geo.Plate.H;
            var walls = BuildNotchWalls(geo);

            var cadPoints = new List<Point3d>();
            foreach (var point in points)
            {
                cadPoints.Add(new Point3d(
                    insertionPoint.X + point.X,
                    ToCadY(insertionPoint.Y, plateHeight, point.Y),
                    0.0));
            }

            for (var i = 0; i < cadPoints.Count; i++)
            {
                var start = cadPoints[i];
                var end = cadPoints[(i + 1) % cadPoints.Count];
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 1e-6)
                {
                    continue;
                }

                var layoutX0 = start.X - insertionPoint.X;
                var layoutY0 = insertionPoint.Y - start.Y;
                var layoutX1 = end.X - insertionPoint.X;
                var layoutY1 = insertionPoint.Y - end.Y;

                // 缺口内壁（深度边）不单独标注，深度统一在缺口中心标一次。
                if (IsNotchWall(walls, layoutX0, layoutY0, layoutX1, layoutY1))
                {
                    continue;
                }

                var horizontal = Math.Abs(dy) <= 1e-6;
                if (horizontal)
                {
                    var above = (layoutY0 + layoutY1) / 2.0 <= h / 2.0;
                    var dimY = above
                        ? -BorderDimensionOffset
                        : h + BorderDimensionOffset;
                    var textY = above
                        ? -BorderDimensionOffset - BorderTextExtraOffset
                        : h + BorderDimensionOffset + BorderTextExtraOffset;
                    AddLine(
                        modelSpace,
                        transaction,
                        start.X,
                        ToCadY(insertionPoint.Y, plateHeight, dimY),
                        end.X,
                        ToCadY(insertionPoint.Y, plateHeight, dimY),
                        "边框标注");
                    AddLine(
                        modelSpace,
                        transaction,
                        start.X,
                        start.Y,
                        start.X,
                        ToCadY(insertionPoint.Y, plateHeight, dimY),
                        "边框标注");
                    AddLine(
                        modelSpace,
                        transaction,
                        end.X,
                        end.Y,
                        end.X,
                        ToCadY(insertionPoint.Y, plateHeight, dimY),
                        "边框标注");
                    AddText(
                        modelSpace,
                        transaction,
                        (start.X + end.X) / 2.0,
                        ToCadY(insertionPoint.Y, plateHeight, textY),
                        ReportTables.Format(length),
                        20.0,
                        "边框标注",
                        true);
                }
                else
                {
                    var left = (layoutX0 + layoutX1) / 2.0 <= w / 2.0;
                    var dimX = left
                        ? -BorderDimensionOffset
                        : w + BorderDimensionOffset;
                    var textX = left
                        ? -BorderDimensionOffset - BorderTextExtraOffset
                        : w + BorderDimensionOffset + BorderTextExtraOffset;
                    AddLine(
                        modelSpace,
                        transaction,
                        insertionPoint.X + dimX,
                        start.Y,
                        insertionPoint.X + dimX,
                        end.Y,
                        "边框标注");
                    AddLine(
                        modelSpace,
                        transaction,
                        start.X,
                        start.Y,
                        insertionPoint.X + dimX,
                        start.Y,
                        "边框标注");
                    AddLine(
                        modelSpace,
                        transaction,
                        end.X,
                        end.Y,
                        insertionPoint.X + dimX,
                        end.Y,
                        "边框标注");
                    AddText(
                        modelSpace,
                        transaction,
                        insertionPoint.X + textX,
                        (start.Y + end.Y) / 2.0,
                        ReportTables.Format(length),
                        20.0,
                        "边框标注",
                        true,
                        Math.PI / 2.0);
                }
            }

            foreach (var notch in geo.Notches)
            {
                DrawNotchDepthDimension(
                    modelSpace,
                    transaction,
                    geo,
                    notch,
                    insertionPoint,
                    plateHeight);
            }
        }

        private static void DrawNotchDepthDimension(
            BlockTableRecord modelSpace,
            Transaction transaction,
            PlateGeometry geo,
            NotchGeo notch,
            Point3d insertionPoint,
            double plateHeight)
        {
            var c = notch.Clear;
            var w = geo.Plate.W;
            var h = geo.Plate.H;
            var edge = notch.Source.Edge;

            double lineX1;
            double lineY1;
            double lineX2;
            double lineY2;
            double textX;
            double textY;
            double depth;
            var vertical = true;

            if (edge == "top")
            {
                var centerX = (c.X0 + c.X1) / 2.0;
                lineX1 = centerX;
                lineY1 = 0.0;
                lineX2 = centerX;
                lineY2 = c.H;
                textX = centerX + 25.0;
                textY = c.H / 2.0;
                depth = c.H;
            }
            else if (edge == "bottom")
            {
                var centerX = (c.X0 + c.X1) / 2.0;
                lineX1 = centerX;
                lineY1 = h - c.H;
                lineX2 = centerX;
                lineY2 = h;
                textX = centerX + 25.0;
                textY = h - c.H / 2.0;
                depth = c.H;
            }
            else if (edge == "left")
            {
                var centerY = (c.Y0 + c.Y1) / 2.0;
                lineX1 = 0.0;
                lineY1 = centerY;
                lineX2 = c.W;
                lineY2 = centerY;
                textX = c.W / 2.0;
                textY = centerY + 25.0;
                depth = c.W;
                vertical = false;
            }
            else
            {
                var centerY = (c.Y0 + c.Y1) / 2.0;
                lineX1 = w - c.W;
                lineY1 = centerY;
                lineX2 = w;
                lineY2 = centerY;
                textX = w - c.W / 2.0;
                textY = centerY + 25.0;
                depth = c.W;
                vertical = false;
            }

            if (depth <= 1e-9)
            {
                return;
            }

            AddLine(
                modelSpace,
                transaction,
                insertionPoint.X + lineX1,
                ToCadY(insertionPoint.Y, plateHeight, lineY1),
                insertionPoint.X + lineX2,
                ToCadY(insertionPoint.Y, plateHeight, lineY2),
                "边框标注");
            AddText(
                modelSpace,
                transaction,
                insertionPoint.X + textX,
                ToCadY(insertionPoint.Y, plateHeight, textY),
                ReportTables.Format(depth),
                20.0,
                "边框标注",
                true,
                vertical ? Math.PI / 2.0 : 0.0);
        }

        private static double DrawTable(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double left,
            double top,
            List<ReportItem> rows,
            string title)
        {
            var widths = new[] { 60.0, 110.0, 100.0, 80.0, 80.0, 110.0, 110.0, 70.0 };
            var width = widths.Sum();
            var headerHeight = 42.0;
            var rowHeight = 34.0;
            var height = headerHeight + rowHeight * Math.Max(rows.Count, 1);
            var bottom = top - height;

            var rect = CreateRectangle(left, bottom, width, height, "表格");
            modelSpace.AppendEntity(rect);
            transaction.AddNewlyCreatedDBObject(rect, true);

            AddText(modelSpace, transaction, left, top + 26.0, title, 22.0, "表格");

            var boundaries = new List<double> { left };
            foreach (var item in widths)
            {
                boundaries.Add(boundaries[boundaries.Count - 1] + item);
            }

            for (var i = 1; i < boundaries.Count - 1; i++)
            {
                AddLine(modelSpace, transaction, boundaries[i], bottom, boundaries[i], top, "表格");
            }

            AddLine(modelSpace, transaction, left, top - headerHeight, left + width, top - headerHeight, "表格");
            AddText(modelSpace, transaction, left + 10.0, top - 28.0, "序号", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[1] + 10.0, top - 28.0, "规格", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[2] + 10.0, top - 28.0, "尺寸", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[3] + 10.0, top - 28.0, "方向", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[4] + 10.0, top - 28.0, "数量", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[5] + 10.0, top - 28.0, "首孔距", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[6] + 10.0, top - 28.0, "尾孔距", 18.0, "表格");
            AddText(modelSpace, transaction, boundaries[7] + 10.0, top - 28.0, "孔数", 18.0, "表格");

            for (var i = 0; i < rows.Count; i++)
            {
                var rowTop = top - headerHeight - i * rowHeight;
                if (i > 0)
                {
                    AddLine(modelSpace, transaction, left, rowTop, left + width, rowTop, "表格");
                }

                var textY = rowTop - 24.0;
                AddText(modelSpace, transaction, left + 10.0, textY, (i + 1).ToString(), 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[1] + 10.0, textY, RowSpec(rows[i]), 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[2] + 10.0, textY, ReportTables.Format(rows[i].Length), 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[3] + 10.0, textY, rows[i].Direction, 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[4] + 10.0, textY, rows[i].Count.ToString(), 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[5] + 10.0, textY, rows[i].FirstHole, 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[6] + 10.0, textY, rows[i].LastHole, 18.0, "表格");
                AddText(modelSpace, transaction, boundaries[7] + 10.0, textY, rows[i].Holes, 18.0, "表格");
            }

            return bottom;
        }

        private static string RowSpec(ReportItem item)
        {
            return item.Spec;
        }

        private static void AddText(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double x,
            double y,
            string text,
            double height,
            string layer,
            bool centered = false,
            double rotation = 0.0)
        {
            var entity = new DBText
            {
                Position = new Point3d(x, y, 0.0),
                Height = height,
                TextString = text,
                Layer = layer,
                Rotation = rotation
            };

            if (centered)
            {
                entity.Justify = AttachmentPoint.MiddleCenter;
                entity.AlignmentPoint = entity.Position;
            }

            modelSpace.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
        }

        private static void AddAnchoredText(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double x,
            double y,
            string text,
            double height,
            string layer,
            AttachmentPoint attachmentPoint)
        {
            var entity = new DBText
            {
                Position = new Point3d(x, y, 0.0),
                Height = height,
                TextString = text,
                Layer = layer,
                Justify = attachmentPoint
            };
            entity.AlignmentPoint = entity.Position;

            modelSpace.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
        }

        private static void AddLine(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double x1,
            double y1,
            double x2,
            double y2,
            string layer)
        {
            var line = new Line(
                new Point3d(x1, y1, 0.0),
                new Point3d(x2, y2, 0.0));
            line.Layer = layer;
            modelSpace.AppendEntity(line);
            transaction.AddNewlyCreatedDBObject(line, true);
        }

        private static Polyline CreateOutlinePolyline(
            List<OutlinePoint> points,
            Point3d insertionPoint,
            double plateHeight,
            string layer)
        {
            var polyline = new Polyline();
            for (var i = 0; i < points.Count; i++)
            {
                polyline.AddVertexAt(
                    i,
                    new Point2d(
                        insertionPoint.X + points[i].X,
                        ToCadY(insertionPoint.Y, plateHeight, points[i].Y)),
                    0.0,
                    0.0,
                    0.0);
            }

            polyline.Closed = true;
            polyline.Layer = layer;
            return polyline;
        }

        private static bool TryOffsetInward(Polyline source, double distance, out Polyline result)
        {
            result = null;
            if (source == null || distance <= 0.0)
            {
                return false;
            }

            var originalArea = Math.Abs(source.Area);
            Polyline best = null;
            var bestArea = double.MaxValue;

            foreach (var offsetDistance in new[] { distance, -distance })
            {
                foreach (DBObject item in source.GetOffsetCurves(offsetDistance))
                {
                    var candidate = item as Polyline;
                    if (candidate == null)
                    {
                        continue;
                    }

                    var area = Math.Abs(candidate.Area);
                    if (area >= originalArea - 1e-6)
                    {
                        continue;
                    }

                    if (best == null || area < bestArea)
                    {
                        best = candidate;
                        bestArea = area;
                    }
                }
            }

            result = best;
            return best != null;
        }

        private static Polyline CreateRectangle(double x, double y, double width, double height, string layer)
        {
            var polyline = new Polyline();
            polyline.AddVertexAt(0, new Point2d(x, y), 0.0, 0.0, 0.0);
            polyline.AddVertexAt(1, new Point2d(x + width, y), 0.0, 0.0, 0.0);
            polyline.AddVertexAt(2, new Point2d(x + width, y + height), 0.0, 0.0, 0.0);
            polyline.AddVertexAt(3, new Point2d(x, y + height), 0.0, 0.0, 0.0);
            polyline.Closed = true;
            polyline.Layer = layer;
            return polyline;
        }

        private static double ToCadY(double insertionY, double plateHeight, double layoutY)
        {
            return insertionY - layoutY;
        }

        private static void AddLengthText(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double x,
            double y,
            double length,
            bool vertical,
            string layer)
        {
            var text = new DBText
            {
                Position = new Point3d(x, y, 0.0),
                Height = 18.0,
                TextString = length.ToString("0.##"),
                Layer = layer,
                Rotation = vertical ? Math.PI / 2.0 : 0.0
            };

            text.Justify = AttachmentPoint.MiddleCenter;
            text.AlignmentPoint = text.Position;
            modelSpace.AppendEntity(text);
            transaction.AddNewlyCreatedDBObject(text, true);
        }

        private static Editor GetEditor()
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                throw new InvalidOperationException("没有活动文档");
            }

            return document.Editor;
        }
    }
}

