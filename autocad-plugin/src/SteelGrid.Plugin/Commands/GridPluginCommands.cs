using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const double BorderTextOffset = 15.0;

        [CommandMethod("GPGRIDINFO")]
        public static void ShowInfo()
        {
            var editor = GetEditor();
            editor.WriteMessage("\n钢格板自动排条插件 正式版 2.0：矩形（凹口/缺角/凸出）走原逻辑；梯形/平行四边形按多边形净空排条，斜边处按短边切平下料；弧形图形提示跳过。");
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
            var selectedIds = SelectClosedPolylines(editor);
            if (selectedIds.Length == 0)
            {
                return;
            }

            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var pending = new List<Tuple<ObjectId, Polyline>>();
                foreach (var id in selectedIds)
                {
                    var outline = transaction.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (outline == null || !outline.Closed || !outline.Bounds.HasValue)
                    {
                        continue;
                    }

                    pending.Add(Tuple.Create(id, outline));
                }

                // 按输出选项中的生成顺序对源图形排序。
                var topDownFirst = _savedSettings.GenerationOrder != GenerationOrder.LeftRightFirst;
                pending.Sort((a, b) =>
                {
                    var boundsA = a.Item2.Bounds.Value;
                    var boundsB = b.Item2.Bounds.Value;
                    var centerAX = (boundsA.MinPoint.X + boundsA.MaxPoint.X) / 2.0;
                    var centerBX = (boundsB.MinPoint.X + boundsB.MaxPoint.X) / 2.0;
                    var centerAY = (boundsA.MinPoint.Y + boundsA.MaxPoint.Y) / 2.0;
                    var centerBY = (boundsB.MinPoint.Y + boundsB.MaxPoint.Y) / 2.0;
                    if (topDownFirst)
                    {
                        var byY = centerBY.CompareTo(centerAY);
                        if (byY != 0)
                        {
                            return byY;
                        }

                        return centerAX.CompareTo(centerBX);
                    }

                    var byX = centerAX.CompareTo(centerBX);
                    if (byX != 0)
                    {
                        return byX;
                    }

                    return centerBY.CompareTo(centerAY);
                });

                var results = new List<LayoutItem>();
                var skipped = 0;
                foreach (var item in pending)
                {
                    var id = item.Item1;
                    var outline = item.Item2;
                    try
                    {
                        if (OutlineReader.HasArc(outline))
                        {
                            skipped++;
                            editor.WriteMessage("\n图形 {0} 跳过：弧形暂不支持，不参与排条", id.Handle);
                            continue;
                        }

                        var parsed = OutlineReader.ReadShape(outline);
                        if (parsed.Supported
                            && parsed.Shape.Kind != OutlineKind.Rectangle
                            && !IsAxisAlignedOutline(outline))
                        {
                            var outlineSpec = BuildSpec(outline);
                            editor.WriteMessage(
                                "\n识别：{0}轮廓，局部 {1:0.##} x {2:0.##}，旋转 {3:0.##}°，受力 {4}",
                                parsed.Shape.Kind == OutlineKind.Trapezoid ? "梯形" : "平行四边形",
                                parsed.Shape.Bounds.W,
                                parsed.Shape.Bounds.H,
                                parsed.Shape.RotationDegrees,
                                outlineSpec.LoadDirection == LoadDirection.Vertical ? "垂直" : "水平");
                            results.Add(new LayoutItem(OutlineLayoutEngine.Layout(parsed.Shape, outlineSpec)));
                            continue;
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

                        results.Add(new LayoutItem(LayoutEngine.Layout(spec)));
                    }
                    catch (System.Exception ex)
                    {
                        editor.WriteMessage("\n图形 {0} 识别或排条失败：{1}", id.Handle, ex.Message);
                    }
                }

                if (results.Count == 0)
                {
                    editor.WriteMessage(skipped > 0
                        ? $"\n选区内没有可用的闭合多段线（跳过 {skipped} 个：弧形暂不支持）。"
                        : "\n选区内没有可用的闭合多段线。");
                    return;
                }

                var placement = editor.GetPoint(new PromptPointOptions("\n指定第一个排条图左上角位置"));
                if (placement.Status != PromptStatus.OK)
                {
                    return;
                }

                try
                {
                    const double horizontalGap = 250.0;
                    const double verticalGap = 250.0;
                    var baseX = placement.Value.X;
                    var baseY = placement.Value.Y;
                    var totalFrames = 0;
                    var totalSegments = 0;

                    if (_savedSettings.OutputFlow == OutputFlow.Vertical)
                    {
                        // 纵向输出：10 个一列，先自上至下排满一列，再新开一列。
                        var rowsPerColumn = Math.Max(1, _savedSettings.PerColumnRows);
                        var columnX = baseX;
                        var cursorY = baseY;
                        var rowInColumn = 0;
                        var columnMaxWidth = 0.0;
                        foreach (var result in results)
                        {
                            var insertion = new Point3d(columnX, cursorY, 0.0);
                            var extents = DrawItem(document.Database, transaction, result, insertion);
                            totalFrames += extents.FrameCount;
                            totalSegments += result.SegmentCount;
                            columnMaxWidth = Math.Max(columnMaxWidth, extents.Width);

                            rowInColumn++;
                            if (rowInColumn >= rowsPerColumn)
                            {
                                columnX += columnMaxWidth + horizontalGap;
                                cursorY = baseY;
                                rowInColumn = 0;
                                columnMaxWidth = 0.0;
                            }
                            else
                            {
                                cursorY -= extents.Height + verticalGap;
                            }
                        }
                    }
                    else
                    {
                        // 横向输出：10 个一行，先自左至右排满一行，再换行。
                        var columnsPerRow = Math.Max(1, _savedSettings.PerRowColumns);
                        var cursorX = baseX;
                        var rowY = baseY;
                        var column = 0;
                        var rowMaxHeight = 0.0;
                        foreach (var result in results)
                        {
                            var insertion = new Point3d(cursorX, rowY, 0.0);
                            var extents = DrawItem(document.Database, transaction, result, insertion);
                            totalFrames += extents.FrameCount;
                            totalSegments += result.SegmentCount;
                            rowMaxHeight = Math.Max(rowMaxHeight, extents.Height);

                            column++;
                            if (column >= columnsPerRow)
                            {
                                rowY -= rowMaxHeight + verticalGap;
                                cursorX = baseX;
                                column = 0;
                                rowMaxHeight = 0.0;
                            }
                            else
                            {
                                cursorX += extents.Width + horizontalGap;
                            }
                        }
                    }

                    transaction.Commit();
                    editor.WriteMessage(skipped > 0
                        ? $"\n排条完成：共 {results.Count} 个图形，边框料 {totalFrames} 个矩形，段数 {totalSegments}；跳过 {skipped} 个（弧形暂不支持）"
                        : $"\n排条完成：共 {results.Count} 个图形，边框料 {totalFrames} 个矩形，段数 {totalSegments}");
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

        private static ObjectId[] SelectClosedPolylines(Editor editor)
        {
            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\n选择要排条的闭合多段线（可用窗口框选多个）：",
                AllowDuplicates = false
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
            });
            var result = editor.GetSelection(options, filter);
            if (result.Status != PromptStatus.OK)
            {
                return new ObjectId[0];
            }

            return result.Value.GetObjectIds();
        }

        /// <summary>
        /// 轮廓是否全是水平/竖直边（矩形 + 缺口/缺角都属于这一类）。
        /// 这类图形交给原有矩形路径，避免凸包拆分在缺角处"造"出一条斜边。
        /// </summary>
        private static bool IsAxisAlignedOutline(Polyline outline)
        {
            if (outline == null || outline.NumberOfVertices < 3)
            {
                return false;
            }

            for (var i = 0; i < outline.NumberOfVertices; i++)
            {
                var a = outline.GetPoint2dAt(i);
                var b = outline.GetPoint2dAt((i + 1) % outline.NumberOfVertices);
                var dx = Math.Abs(b.X - a.X);
                var dy = Math.Abs(b.Y - a.Y);
                if (dx > 1e-6 && dy > 1e-6)
                {
                    return false;
                }
            }

            return true;
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

        private static GroupExtents DrawResult(
            Database database,
            Transaction transaction,
            LayoutResult result,
            Point3d insertionPoint)
        {
            EnsureLayers(database, transaction);

            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            var plateHeight = result.Geometry.Plate.H;

            // 边框按受力方向拆成独立边框料，能看出哪条边包住哪条边。
            var framePieces = FrameSplitter.GetPieces(result.Geometry);
            var frameCount = DrawFramePieces(
                modelSpace,
                transaction,
                framePieces,
                insertionPoint,
                plateHeight);

            var plateOutline = CreateOutlinePolyline(
                GeometryOutlines.PlateOutline(result.Geometry),
                insertionPoint,
                plateHeight,
                "外轮廓");
            modelSpace.AppendEntity(plateOutline);
            transaction.AddNewlyCreatedDBObject(plateOutline, true);

            var netOutline = CreateOutlinePolyline(
                GeometryOutlines.NetOutline(result.Geometry),
                insertionPoint,
                plateHeight,
                "内边框");
            modelSpace.AppendEntity(netOutline);
            transaction.AddNewlyCreatedDBObject(netOutline, true);

            // 边框标注改为按每块边框料矩形标注下料长度，序号与右侧边框下料表一致。
            var frameTable = ReportTables.FrameTable(result, framePieces);
            var frameRowIndexes = new Dictionary<string, int>();
            for (var i = 0; i < frameTable.Count; i++)
            {
                frameRowIndexes[frameTable[i].Direction + "|" + ReportTables.Format(frameTable[i].Length)] = i + 1;
            }

            DrawBorderDimensions(
                modelSpace,
                transaction,
                result.Geometry,
                framePieces,
                frameRowIndexes,
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
                    var layer = "横条首尾孔距标注";
                    var pitch = result.Spec.Horizontal.Pitch;
                    var lineLayoutY = annotation.BarCenter + pitch / 2.0;
                    var dimensionY = ToCadY(insertionPoint.Y, plateHeight, lineLayoutY);
                    var textY = ToCadY(insertionPoint.Y, plateHeight, lineLayoutY + pitch / 4.0);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.SegmentA,
                        dimensionY,
                        true,
                        layer);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.FirstPosition,
                        dimensionY,
                        true,
                        layer);
                    AddLine(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.SegmentA,
                        dimensionY,
                        insertionPoint.X + annotation.FirstPosition,
                        dimensionY,
                        layer);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.LastPosition,
                        dimensionY,
                        true,
                        layer);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.SegmentB,
                        dimensionY,
                        true,
                        layer);
                    AddLine(
                        modelSpace,
                        transaction,
                        insertionPoint.X + annotation.LastPosition,
                        dimensionY,
                        insertionPoint.X + annotation.SegmentB,
                        dimensionY,
                        layer);
                    AddText(
                        modelSpace,
                        transaction,
                        insertionPoint.X + (annotation.SegmentA + annotation.FirstPosition) / 2.0,
                        textY,
                        "首" + ReportTables.Format(annotation.FirstHole),
                        13.0,
                        layer,
                        true);
                    AddText(
                        modelSpace,
                        transaction,
                        insertionPoint.X + (annotation.LastPosition + annotation.SegmentB) / 2.0,
                        textY,
                        "尾" + ReportTables.Format(annotation.LastHole),
                        13.0,
                        layer,
                        true);
                }
                else
                {
                    var layer = "纵条首尾孔距标注";
                    var pitch = result.Spec.Vertical.Pitch;
                    var lineX = insertionPoint.X + annotation.BarCenter + pitch / 2.0;
                    var textX = lineX + pitch / 4.0;
                    var firstMidY = ToCadY(
                        insertionPoint.Y,
                        plateHeight,
                        (annotation.SegmentA + annotation.FirstPosition) / 2.0);
                    var lastMidY = ToCadY(
                        insertionPoint.Y,
                        plateHeight,
                        (annotation.LastPosition + annotation.SegmentB) / 2.0);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.SegmentA),
                        false,
                        layer);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.FirstPosition),
                        false,
                        layer);
                    AddLine(
                        modelSpace,
                        transaction,
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.SegmentA),
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.FirstPosition),
                        layer);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.LastPosition),
                        false,
                        layer);
                    AddEndTicks(
                        modelSpace,
                        transaction,
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.SegmentB),
                        false,
                        layer);
                    AddLine(
                        modelSpace,
                        transaction,
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.LastPosition),
                        lineX,
                        ToCadY(insertionPoint.Y, plateHeight, annotation.SegmentB),
                        layer);
                    AddText(
                        modelSpace,
                        transaction,
                        textX,
                        firstMidY,
                        "首" + ReportTables.Format(annotation.FirstHole),
                        13.0,
                        layer,
                        true,
                        Math.PI / 2.0);
                    AddText(
                        modelSpace,
                        transaction,
                        textX,
                        lastMidY,
                        "尾" + ReportTables.Format(annotation.LastHole),
                        13.0,
                        layer,
                        true,
                        Math.PI / 2.0);
                }
            }

            var plate = result.Geometry.Plate;
            var tableLeft = insertionPoint.X + plate.W + 260.0;
            var frameLayout = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                insertionPoint.Y,
                frameTable,
                "边框下料（共 " + frameTable.Sum(item => item.Count) + " 根）");
            var verticalRows = ReportTables.ReportTable(result, "纵向");
            var verticalLayout = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                frameLayout.Bottom - 80.0,
                verticalRows,
                "纵向" + result.Spec.Vertical.TypeName + "下料（共 "
                + verticalRows.Sum(item => item.Count) + " 根）");
            var horizontalRows = PluginTableRows.GetHorizontalRows(result);
            var horizontalLayout = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                verticalLayout.Bottom - 80.0,
                horizontalRows,
                "横向" + result.Spec.Horizontal.TypeName + "下料（共 "
                + horizontalRows.Sum(item => item.Count) + " 根）");
            var tableBottom = horizontalLayout.Bottom;
            var tableWidth = Math.Max(
                frameLayout.Width,
                Math.Max(verticalLayout.Width, horizontalLayout.Width));

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
            return new GroupExtents(
                groupRight - groupLeft,
                groupTop - groupBottom,
                frameCount);
        }

        private sealed class TableLayout
        {
            public double Bottom { get; set; }

            public double Width { get; set; }
        }

        private struct GroupExtents
        {
            public GroupExtents(double width, double height, int frameCount)
            {
                Width = width;
                Height = height;
                FrameCount = frameCount;
            }

            public double Width { get; }

            public double Height { get; }

            public int FrameCount { get; }
        }

        private static int DrawFramePieces(
            BlockTableRecord modelSpace,
            Transaction transaction,
            List<FramePiece> framePieces,
            Point3d insertionPoint,
            double plateHeight)
        {
            var drawn = 0;
            foreach (var piece in framePieces)
            {
                var rect = piece.Rect;
                if (rect.W <= 0.0 || rect.H <= 0.0)
                {
                    continue;
                }

                drawn++;
                var entity = CreateRectangle(
                    insertionPoint.X + rect.X0,
                    ToCadY(insertionPoint.Y, plateHeight, rect.Y1),
                    rect.W,
                    rect.H,
                    "外轮廓");
                modelSpace.AppendEntity(entity);
                transaction.AddNewlyCreatedDBObject(entity, true);
            }

            return drawn;
        }

        private static void DrawBorderDimensions(
            BlockTableRecord modelSpace,
            Transaction transaction,
            PlateGeometry geo,
            List<FramePiece> framePieces,
            IReadOnlyDictionary<string, int> frameRowIndexes,
            Point3d insertionPoint,
            double plateHeight)
        {
            var w = geo.Plate.W;
            var h = geo.Plate.H;

            foreach (var piece in framePieces)
            {
                var rect = piece.Rect;
                if (rect.W <= 0.0 || rect.H <= 0.0)
                {
                    continue;
                }

                var length = Math.Max(rect.W, rect.H);
                if (length <= 1e-6)
                {
                    continue;
                }

                var key = piece.Direction + "|" + ReportTables.Format(length);
                int rowIndex;
                frameRowIndexes.TryGetValue(key, out rowIndex);
                var text = "边框" + rowIndex + "  " + ReportTables.Format(length);

                var horizontal = Math.Abs(rect.H - geo.Spec.FrameT) <= 1e-6;
                if (horizontal)
                {
                    var textY = BorderLabelY(geo, rect, h);
                    var x0 = insertionPoint.X + rect.X0;
                    var x1 = insertionPoint.X + rect.X1;
                    AddText(
                        modelSpace,
                        transaction,
                        (x0 + x1) / 2.0,
                        ToCadY(insertionPoint.Y, plateHeight, textY),
                        text,
                        15.0,
                        "边框标注",
                        true);
                }
                else
                {
                    var textX = BorderLabelX(geo, rect, w);
                    var y0 = ToCadY(insertionPoint.Y, plateHeight, rect.Y0);
                    var y1 = ToCadY(insertionPoint.Y, plateHeight, rect.Y1);
                    AddText(
                        modelSpace,
                        transaction,
                        insertionPoint.X + textX,
                        (y0 + y1) / 2.0,
                        text,
                        15.0,
                        "边框标注",
                        true,
                        Math.PI / 2.0);
                }
            }
        }

        private static double BorderLabelY(PlateGeometry geo, Rect rect, double h)
        {
            const double eps = 1e-6;
            foreach (var notch in geo.Notches)
            {
                var c = notch.Clear;
                var overlapsX = c.X0 < rect.X1 - eps && rect.X0 < c.X1 - eps;
                if (Math.Abs(c.Y1 - rect.Y0) <= eps && overlapsX)
                {
                    return rect.Y0 - BorderTextOffset;
                }

                if (Math.Abs(c.Y0 - rect.Y1) <= eps && overlapsX)
                {
                    return rect.Y1 + BorderTextOffset;
                }
            }

            if (rect.Y0 <= eps)
            {
                return -BorderTextOffset;
            }

            if (rect.Y1 >= h - eps)
            {
                return h + BorderTextOffset;
            }

            return (rect.Y0 + rect.Y1) / 2.0 <= h / 2.0
                ? rect.Y0 - BorderTextOffset
                : rect.Y1 + BorderTextOffset;
        }

        private static double BorderLabelX(PlateGeometry geo, Rect rect, double w)
        {
            const double eps = 1e-6;
            foreach (var notch in geo.Notches)
            {
                var c = notch.Clear;
                var overlapsY = c.Y0 < rect.Y1 - eps && rect.Y0 < c.Y1 - eps;
                if (Math.Abs(c.X1 - rect.X0) <= eps && overlapsY)
                {
                    return rect.X0 - BorderTextOffset;
                }

                if (Math.Abs(c.X0 - rect.X1) <= eps && overlapsY)
                {
                    return rect.X1 + BorderTextOffset;
                }
            }

            if (rect.X0 <= eps)
            {
                return -BorderTextOffset;
            }

            if (rect.X1 >= w - eps)
            {
                return w + BorderTextOffset;
            }

            return (rect.X0 + rect.X1) / 2.0 <= w / 2.0
                ? rect.X0 - BorderTextOffset
                : rect.X1 + BorderTextOffset;
        }

        private static TableLayout DrawTable(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double left,
            double top,
            List<ReportItem> rows,
            string title)
        {
            var widths = ComputeTableWidths(rows);
            var width = widths.Sum();
            var compact = rows.Count > 12;
            var headerHeight = compact ? 36.0 : 42.0;
            var rowHeight = compact ? (rows.Count > 24 ? 26.0 : 30.0) : 34.0;
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

            return new TableLayout { Bottom = bottom, Width = width };
        }

        private static double[] ComputeTableWidths(List<ReportItem> rows)
        {
            var headers = new[] { "序号", "规格", "尺寸", "方向", "数量", "首孔距", "尾孔距", "孔数" };
            var minimums = new[] { 60.0, 110.0, 100.0, 80.0, 80.0, 110.0, 110.0, 70.0 };
            var widths = new double[8];
            for (var c = 0; c < 8; c++)
            {
                var needed = EstimateTextWidth(headers[c], 18.0) + 20.0;
                if (c == 0)
                {
                    needed = Math.Max(
                        needed,
                        EstimateTextWidth(rows.Count.ToString(CultureInfo.InvariantCulture), 18.0) + 20.0);
                }

                foreach (var row in rows)
                {
                    needed = Math.Max(needed, EstimateTextWidth(ColumnText(row, c), 18.0) + 20.0);
                }

                widths[c] = Math.Max(minimums[c], needed);
            }

            return widths;
        }

        private static string ColumnText(ReportItem row, int column)
        {
            switch (column)
            {
                case 1:
                    return RowSpec(row);
                case 2:
                    return ReportTables.Format(row.Length);
                case 3:
                    return row.Direction;
                case 4:
                    return row.Count.ToString(CultureInfo.InvariantCulture);
                case 5:
                    return row.FirstHole;
                case 6:
                    return row.LastHole;
                case 7:
                    return row.Holes;
                default:
                    return "";
            }
        }

        private static double EstimateTextWidth(string text, double height)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0.0;
            }

            var units = 0.0;
            foreach (var ch in text)
            {
                units += ch > 127 ? 1.0 : 0.58;
            }

            return units * height;
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

        private static void AddEndTicks(
            BlockTableRecord modelSpace,
            Transaction transaction,
            double x,
            double y,
            bool horizontalDimension,
            string layer)
        {
            const double tick = 4.0;
            if (horizontalDimension)
            {
                AddLine(modelSpace, transaction, x, y - tick, x, y + tick, layer);
            }
            else
            {
                AddLine(modelSpace, transaction, x - tick, y, x + tick, y, layer);
            }
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

        private sealed class LayoutItem
        {
            public LayoutItem(LayoutResult rectangle)
            {
                Rectangle = rectangle;
            }

            public LayoutItem(OutlineLayoutResult outline)
            {
                Outline = outline;
            }

            public LayoutResult Rectangle { get; }

            public OutlineLayoutResult Outline { get; }

            public int SegmentCount => Outline != null ? Outline.SegmentCount : Rectangle.SegmentCount;
        }

        private static GroupExtents DrawItem(
            Database database,
            Transaction transaction,
            LayoutItem item,
            Point3d insertionPoint)
        {
            return item.Outline != null
                ? DrawOutlineResult(database, transaction, item.Outline, insertionPoint)
                : DrawResult(database, transaction, item.Rectangle, insertionPoint);
        }

        private static void EnsureLayers(Database database, Transaction transaction)
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
                { "横条首尾孔距标注", 6 },
                { "纵条首尾孔距标注", 3 },
                { "表格", 7 },
                { "分组边框", 8 }
            };

            foreach (var item in layerColors)
            {
                if (layerTable.Has(item.Key))
                {
                    continue;
                }

                var layer = new LayerTableRecord { Name = item.Key };
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, item.Value);
                layerTable.Add(layer);
                transaction.AddNewlyCreatedDBObject(layer, true);
            }
        }

        /// <summary>
        /// 多边形轮廓（梯形/平行四边形）的排条图：轮廓、净空、边框料、条、标注、下料表和分组框。
        /// 局部坐标经过轮廓旋转，绘制时按轮廓的局部坐标轴映射回 AutoCAD 坐标。
        /// </summary>
        private static GroupExtents DrawOutlineResult(
            Database database,
            Transaction transaction,
            OutlineLayoutResult layout,
            Point3d insertionPoint)
        {
            var shape = layout.Shape;
            var spec = layout.Spec;
            EnsureLayers(database, transaction);

            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // 局部包围盒经过旋转后不一定从插入点向右下角展开，这里把图形左上角对齐到插入点。
            var origin = AlignOutlineInsertion(layout, insertionPoint);

            // 边框料：每条边一根，斜边料是旋转矩形。
            var framePieces = PolygonFrameSplitter.GetPieces(layout.Plate, layout.Net, spec.FrameT, spec.LoadDirection);
            foreach (var piece in framePieces)
            {
                AppendMappedPolyline(modelSpace, transaction, piece.Corners(), shape, origin, "外轮廓");
            }

            AppendMappedPolyline(modelSpace, transaction, layout.Plate.Vertices, shape, origin, "外轮廓");
            AppendMappedPolyline(modelSpace, transaction, layout.Net.Vertices, shape, origin, "内边框");

            // 边框标注：文字放在边框料外侧，序号与边框下料表一致。
            var frameTable = ReportTables.FrameTable(framePieces);
            var frameRowIndexes = new Dictionary<string, int>();
            for (var i = 0; i < frameTable.Count; i++)
            {
                frameRowIndexes[frameTable[i].Direction + "|" + ReportTables.Format(frameTable[i].Length)] = i + 1;
            }

            foreach (var piece in framePieces)
            {
                var radians = piece.AngleDeg * Math.PI / 180.0;
                var offset = piece.Thickness / 2.0 + BorderTextOffset;
                var textPoint = MapPoint(
                    shape,
                    origin,
                    piece.Center.X + piece.Outward.X * offset,
                    piece.Center.Y + piece.Outward.Y * offset);
                var index = frameRowIndexes[piece.Direction + "|" + ReportTables.Format(piece.Length)];
                AddText(
                    modelSpace,
                    transaction,
                    textPoint.X,
                    textPoint.Y,
                    "边框" + index + "  " + ReportTables.Format(piece.Length),
                    15.0,
                    "边框标注",
                    true,
                    TextRotation(shape, radians));
            }

            // 条：斜边截断已按短边切平，每段本身就是下料矩形。
            foreach (var bar in layout.VerticalBars)
            {
                for (var s = 0; s < bar.Segments.Count; s++)
                {
                    var segment = bar.Segments[s];
                    var half = bar.Thickness / 2.0;
                    var corners = new List<Point2D>
                    {
                        new Point2D(bar.Center - half, segment.A),
                        new Point2D(bar.Center + half, segment.A),
                        new Point2D(bar.Center + half, segment.B),
                        new Point2D(bar.Center - half, segment.B)
                    };
                    AppendMappedPolyline(modelSpace, transaction, corners, shape, origin, "纵条");

                    var labelY = bar.Full ? -BarLabelOffset : (segment.A + segment.B) / 2.0;
                    var labelPoint = MapPoint(shape, origin, bar.Center, labelY);
                    AddText(
                        modelSpace,
                        transaction,
                        labelPoint.X,
                        labelPoint.Y,
                        ReportTables.Format(segment.Length),
                        18.0,
                        "纵条标注",
                        true,
                        TextRotation(shape, Math.PI / 2.0));
                }
            }

            foreach (var bar in layout.HorizontalBars)
            {
                for (var s = 0; s < bar.Segments.Count; s++)
                {
                    var segment = bar.Segments[s];
                    var half = bar.Thickness / 2.0;
                    var corners = new List<Point2D>
                    {
                        new Point2D(segment.A, bar.Center - half),
                        new Point2D(segment.B, bar.Center - half),
                        new Point2D(segment.B, bar.Center + half),
                        new Point2D(segment.A, bar.Center + half)
                    };
                    AppendMappedPolyline(modelSpace, transaction, corners, shape, origin, "横条");

                    var labelX = bar.Full ? layout.Plate.Bounds.W + BarLabelOffset : (segment.A + segment.B) / 2.0;
                    var labelPoint = MapPoint(shape, origin, labelX, bar.Center);
                    AddText(
                        modelSpace,
                        transaction,
                        labelPoint.X,
                        labelPoint.Y,
                        ReportTables.Format(segment.Length),
                        18.0,
                        "横条标注",
                        true,
                        TextRotation(shape, 0.0));
                }
            }

            DrawOutlineHoleAnnotations(modelSpace, transaction, layout, shape, origin);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var vertex in layout.Plate.Vertices)
            {
                var point = MapPoint(shape, origin, vertex.X, vertex.Y);
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            var tableLeft = maxX + 260.0;
            var frameLayout = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                maxY,
                frameTable,
                "边框下料（共 " + frameTable.Sum(item => item.Count) + " 根）");
            var verticalRows = ReportTables.ReportTable(layout.Bars, "纵向");
            var verticalLayout = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                frameLayout.Bottom - 80.0,
                verticalRows,
                "纵向" + spec.Vertical.TypeName + "下料（共 " + verticalRows.Sum(item => item.Count) + " 根）");
            var horizontalRows = PluginTableRows.GetHorizontalRows(layout.Bars);
            var horizontalLayout = DrawTable(
                modelSpace,
                transaction,
                tableLeft,
                verticalLayout.Bottom - 80.0,
                horizontalRows,
                "横向" + spec.Horizontal.TypeName + "下料（共 " + horizontalRows.Sum(item => item.Count) + " 根）");
            var tableBottom = horizontalLayout.Bottom;
            var tableWidth = Math.Max(frameLayout.Width, Math.Max(verticalLayout.Width, horizontalLayout.Width));

            var groupLeft = minX - 260.0;
            var groupRight = tableLeft + tableWidth + 60.0;
            var groupTop = maxY + 220.0;
            var groupBottom = Math.Min(minY - 220.0, tableBottom - 60.0);
            var border = CreateRectangle(
                groupLeft,
                groupBottom,
                groupRight - groupLeft,
                groupTop - groupBottom,
                "分组边框");
            modelSpace.AppendEntity(border);
            transaction.AddNewlyCreatedDBObject(border, true);
            return new GroupExtents(groupRight - groupLeft, groupTop - groupBottom, framePieces.Count);
        }

        private static void DrawOutlineHoleAnnotations(
            BlockTableRecord modelSpace,
            Transaction transaction,
            OutlineLayoutResult layout,
            OutlineShape shape,
            Point3d origin)
        {
            var spec = layout.Spec;
            foreach (var annotation in ReportTables.HoleAnnotations(layout.Bars))
            {
                if (annotation.Direction == "横向" && spec.Horizontal.Type == BarType.TwistedSquare)
                {
                    continue;
                }

                if (annotation.Direction == "纵向" && spec.Vertical.Type == BarType.TwistedSquare)
                {
                    continue;
                }

                if (annotation.Direction == "横向")
                {
                    var layer = "横条首尾孔距标注";
                    var pitch = spec.Horizontal.Pitch;
                    var lineY = annotation.BarCenter + pitch / 2.0;
                    var textY = lineY + pitch / 4.0;
                    AddMappedLine(modelSpace, transaction, shape, origin, annotation.SegmentA, lineY, annotation.FirstPosition, lineY, layer);
                    AddMappedLine(modelSpace, transaction, shape, origin, annotation.LastPosition, lineY, annotation.SegmentB, lineY, layer);
                    AddMappedTick(modelSpace, transaction, shape, origin, annotation.FirstPosition, lineY, true, layer);
                    AddMappedTick(modelSpace, transaction, shape, origin, annotation.LastPosition, lineY, true, layer);
                    AddMappedTick(modelSpace, transaction, shape, origin, annotation.SegmentB, lineY, true, layer);
                    // 局部坐标 Y 向上：靠上的一端是 B 端，按矩形路径语义"首"在靠上一侧
                    var firstPoint = MapPoint(shape, origin, (annotation.LastPosition + annotation.SegmentB) / 2.0, textY);
                    AddText(
                        modelSpace, transaction, firstPoint.X, firstPoint.Y,
                        "首" + ReportTables.Format(annotation.LastHole), 13.0, layer, true, TextRotation(shape, 0.0));
                    var lastPoint = MapPoint(shape, origin, (annotation.SegmentA + annotation.FirstPosition) / 2.0, textY);
                    AddText(
                        modelSpace, transaction, lastPoint.X, lastPoint.Y,
                        "尾" + ReportTables.Format(annotation.FirstHole), 13.0, layer, true, TextRotation(shape, 0.0));
                }
                else
                {
                    var layer = "纵条首尾孔距标注";
                    var pitch = spec.Vertical.Pitch;
                    var lineX = annotation.BarCenter + pitch / 2.0;
                    var textX = lineX + pitch / 4.0;
                    AddMappedLine(modelSpace, transaction, shape, origin, lineX, annotation.SegmentA, lineX, annotation.FirstPosition, layer);
                    AddMappedLine(modelSpace, transaction, shape, origin, lineX, annotation.LastPosition, lineX, annotation.SegmentB, layer);
                    AddMappedTick(modelSpace, transaction, shape, origin, lineX, annotation.FirstPosition, false, layer);
                    AddMappedTick(modelSpace, transaction, shape, origin, lineX, annotation.LastPosition, false, layer);
                    AddMappedTick(modelSpace, transaction, shape, origin, lineX, annotation.SegmentB, false, layer);
                    var firstPoint = MapPoint(shape, origin, textX, (annotation.LastPosition + annotation.SegmentB) / 2.0);
                    AddText(
                        modelSpace, transaction, firstPoint.X, firstPoint.Y,
                        "首" + ReportTables.Format(annotation.LastHole), 13.0, layer, true, TextRotation(shape, Math.PI / 2.0));
                    var lastPoint = MapPoint(shape, origin, textX, (annotation.SegmentA + annotation.FirstPosition) / 2.0);
                    AddText(
                        modelSpace, transaction, lastPoint.X, lastPoint.Y,
                        "尾" + ReportTables.Format(annotation.FirstHole), 13.0, layer, true, TextRotation(shape, Math.PI / 2.0));
                }
            }
        }

        private static Point3d AlignOutlineInsertion(OutlineLayoutResult layout, Point3d insertionPoint)
        {
            var bounds = layout.Plate.Bounds;
            var shape = layout.Shape;
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (var x in new[] { bounds.X0, bounds.X1 })
            {
                foreach (var y in new[] { bounds.Y0, bounds.Y1 })
                {
                    minX = Math.Min(minX, x * shape.AxisX.X + y * shape.AxisY.X);
                    minY = Math.Min(minY, x * shape.AxisX.Y + y * shape.AxisY.Y);
                }
            }

            return new Point3d(insertionPoint.X - minX, insertionPoint.Y - minY, 0.0);
        }

        private static Point3d MapPoint(OutlineShape shape, Point3d origin, double x, double y)
        {
            return new Point3d(
                origin.X + x * shape.AxisX.X + y * shape.AxisY.X,
                origin.Y + x * shape.AxisX.Y + y * shape.AxisY.Y,
                0.0);
        }

        private static void AppendMappedPolyline(
            BlockTableRecord modelSpace,
            Transaction transaction,
            IReadOnlyList<Point2D> points,
            OutlineShape shape,
            Point3d origin,
            string layer)
        {
            var polyline = new Polyline();
            for (var i = 0; i < points.Count; i++)
            {
                var point = MapPoint(shape, origin, points[i].X, points[i].Y);
                polyline.AddVertexAt(i, new Point2d(point.X, point.Y), 0.0, 0.0, 0.0);
            }

            polyline.Closed = true;
            polyline.Layer = layer;
            modelSpace.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);
        }

        private static void AddMappedLine(
            BlockTableRecord modelSpace,
            Transaction transaction,
            OutlineShape shape,
            Point3d origin,
            double x1,
            double y1,
            double x2,
            double y2,
            string layer)
        {
            var a = MapPoint(shape, origin, x1, y1);
            var b = MapPoint(shape, origin, x2, y2);
            AddLine(modelSpace, transaction, a.X, a.Y, b.X, b.Y, layer);
        }

        private static void AddMappedTick(
            BlockTableRecord modelSpace,
            Transaction transaction,
            OutlineShape shape,
            Point3d origin,
            double x,
            double y,
            bool horizontalBar,
            string layer)
        {
            const double tick = 4.0;
            var a = horizontalBar
                ? MapPoint(shape, origin, x, y - tick)
                : MapPoint(shape, origin, x - tick, y);
            var b = horizontalBar
                ? MapPoint(shape, origin, x, y + tick)
                : MapPoint(shape, origin, x + tick, y);
            AddLine(modelSpace, transaction, a.X, a.Y, b.X, b.Y, layer);
        }

        /// <summary>文字角度：局部角度叠加轮廓旋转角，并保证文字不倒置。</summary>
        private static double TextRotation(OutlineShape shape, double localRadians)
        {
            var angle = Math.Atan2(shape.AxisX.Y, shape.AxisX.X) + localRadians;
            while (angle > Math.PI)
            {
                angle -= 2.0 * Math.PI;
            }

            while (angle <= -Math.PI)
            {
                angle += 2.0 * Math.PI;
            }

            if (angle > Math.PI / 2.0 + 1e-9)
            {
                angle -= Math.PI;
            }
            else if (angle <= -Math.PI / 2.0 - 1e-9)
            {
                angle += Math.PI;
            }

            return angle;
        }
    }
}

