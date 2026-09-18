# -*- coding: utf-8 -*-
"""合并排条清单输出。"""

from __future__ import annotations

import csv
import zipfile
from xml.sax.saxutils import escape
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union

from .layout import Bar, LayoutResult, Segment


HEADER = ["序号", "规格", "尺寸", "方向", "数量", "首孔距", "尾孔距", "孔数"]


def fmt(value: float) -> str:
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return text if text else "0"


def report_hole(value: Optional[Union[float, str]]) -> str:
    if value is None:
        return ""
    if isinstance(value, str):
        return value
    if isinstance(value, int) and not isinstance(value, bool):
        return str(value) if value > 0 else ""
    return fmt(value)


def _join_report_values(values: List[Optional[Union[float, str]]]) -> Optional[Union[float, str]]:
    texts: List[str] = []
    for value in values:
        text = report_hole(value)
        if text and text not in texts:
            texts.append(text)
    if not texts:
        return None
    return texts[0] if len(texts) == 1 else "/".join(texts)


@dataclass(frozen=True)
class CutItem:
    direction: str
    type: str
    length: float
    count: int
    indices: str


@dataclass(frozen=True)
class HoleItem:
    direction: str
    length: float
    first_hole: float
    last_hole: float
    holes: int
    count: int
    indices: str


@dataclass(frozen=True)
class ReportItem:
    spec: str
    length: float
    direction: str
    count: int
    first_hole: Optional[Union[float, str]]
    last_hole: Optional[Union[float, str]]
    holes: Union[int, str]
    indices: str


@dataclass(frozen=True)
class HoleAnnotation:
    """按规格去重后的首尾孔标注，坐标为板件内部坐标。"""

    direction: str
    length: float
    first_hole: float
    last_hole: float
    bar_center: float
    bar_thickness: float
    seg_a: float
    seg_b: float
    first_pos: float
    last_pos: float


def hole_annotations(result: LayoutResult) -> List[HoleAnnotation]:
    """每个规格行只取第一根代表扁钢的一段，供图上标注首尾孔一次。"""

    annotations: List[HoleAnnotation] = []
    plans = (
        ("纵向", result.vertical_bars),
        ("横向", result.horizontal_bars),
    )
    for direction, bars in plans:
        for row in report_table(result, direction):
            if row.first_hole is None and row.last_hole is None:
                continue
            candidates = []
            for bar in bars:
                for segment, holes in zip(bar.segments, bar.hole_groups):
                    if holes and abs(segment.length - row.length) <= 1e-6:
                        candidates.append((bar, segment, holes))
            if not candidates:
                continue
            # 同一规格可能出现端部孔距变体；图上代表段取数量最多的一组。
            variant_counts = Counter(
                (round(holes[0] - segment.a, 6), round(segment.b - holes[-1], 6))
                for _, segment, holes in candidates
            )
            variant = variant_counts.most_common(1)[0][0]
            found = next(
                item
                for item in candidates
                if (
                    round(item[2][0] - item[1].a, 6),
                    round(item[1].b - item[2][-1], 6),
                )
                == variant
            )
            bar, segment, holes = found
            if found is None:
                continue
            annotations.append(
                HoleAnnotation(
                    direction,
                    row.length,
                    holes[0] - segment.a,
                    segment.b - holes[-1],
                    bar.center,
                    bar.thickness,
                    segment.a,
                    segment.b,
                    holes[0],
                    holes[-1],
                )
            )
    return annotations


def _same_length(a: Segment, b: float, eps: float = 1e-6) -> bool:
    return abs(a.length - b) <= eps


def _cut_type(orientation: str, bar: Bar, net_span: float) -> str:
    if len(bar.segments) == 1 and _same_length(bar.segments[0], net_span):
        return "满长" if orientation == "vertical" else "整长"
    if orientation == "vertical" and len(bar.segments) == 1:
        return "短条"
    return "分段"


def cut_list(result: LayoutResult) -> List[CutItem]:
    geo = result.geometry
    groups: Dict[Tuple[str, str, float], List[str]] = {}
    order: List[Tuple[str, str, float]] = []

    plans = (
        ("纵向", result.vertical_bars, geo.net.h),
        ("横向", result.horizontal_bars, geo.net.w),
    )
    for direction, bars, net_span in plans:
        for bar in bars:
            cut_type = _cut_type(bar.orientation, bar, net_span)
            for segment in bar.segments:
                key = (direction, cut_type, round(segment.length, 6))
                if key not in groups:
                    groups[key] = []
                    order.append(key)
                groups[key].append(str(bar.index))

    return [
        CutItem(direction, cut_type, length, len(groups[key]), ",".join(groups[key]))
        for key in order
        for direction, cut_type, length in [key]
    ]


def hole_table(result: LayoutResult) -> List[HoleItem]:
    groups: Dict[Tuple[str, float, float, float, int], List[str]] = {}
    order: List[Tuple[str, float, float, float, int]] = []

    plans = (
        ("纵向", result.vertical_bars),
        ("横向", result.horizontal_bars),
    )
    for direction, bars in plans:
        for bar in bars:
            for segment, holes in zip(bar.segments, bar.hole_groups):
                if not holes:
                    continue
                first = holes[0] - segment.a
                last = segment.b - holes[-1]
                key = (direction, round(segment.length, 6), round(first, 6), round(last, 6), len(holes))
                if key not in groups:
                    groups[key] = []
                    order.append(key)
                groups[key].append(str(bar.index))

    return [
        HoleItem(direction, length, first, last, holes, len(groups[key]), ",".join(groups[key]))
        for key in order
        for direction, length, first, last, holes in [key]
    ]


def report_table(result: LayoutResult, direction_filter: str | None = None) -> List[ReportItem]:
    """合并下料清单和首尾孔距，得到右侧表格使用的一行一条记录。"""

    geo = result.geometry
    groups: Dict[Tuple[str, str, float], Dict[str, Any]] = {}
    order: List[Tuple[str, str, float]] = []

    plans = (
        ("纵向", result.vertical_bars, geo.net.h),
        ("横向", result.horizontal_bars, geo.net.w),
    )
    for direction, bars, net_span in plans:
        if direction_filter is not None and direction != direction_filter:
            continue
        for bar in bars:
            cut_type = _cut_type(bar.orientation, bar, net_span)
            for segment, holes in zip(bar.segments, bar.hole_groups):
                first = holes[0] - segment.a if holes else None
                last = segment.b - holes[-1] if holes else None
                key = (
                    direction,
                    cut_type,
                    round(segment.length, 6),
                )
                if key not in groups:
                    groups[key] = {"indices": [], "variants": []}
                    order.append(key)
                groups[key]["indices"].append(str(bar.index))
                groups[key]["variants"].append((first, last, len(holes)))

    return [
        ReportItem(
            cut_type,
            length,
            direction,
            len(groups[key]["indices"]),
            _join_report_values([item[0] for item in groups[key]["variants"]]),
            _join_report_values([item[1] for item in groups[key]["variants"]]),
            _join_report_values([item[2] for item in groups[key]["variants"]]) or 0,
            ",".join(groups[key]["indices"]),
        )
        for key in order
        for direction, cut_type, length in [key]
    ]


def _subtract_spans(start: float, end: float, cuts: List[Segment]) -> List[Segment]:
    spans = [Segment(start, end)]
    for cut in cuts:
        next_spans: List[Segment] = []
        for span in spans:
            if span.b <= cut.a or span.a >= cut.b:
                next_spans.append(span)
                continue
            if span.a < cut.a:
                next_spans.append(Segment(span.a, cut.a))
            if cut.b < span.b:
                next_spans.append(Segment(cut.b, span.b))
        spans = next_spans
    return spans


def _add_frame_piece(
    groups: Dict[Tuple[str, float], int],
    order: List[Tuple[str, float]],
    direction: str,
    length: float,
) -> None:
    if length <= 1e-9:
        return
    key = (direction, round(length, 6))
    groups[key] = groups.get(key, 0) + 1
    if key not in order:
        order.append(key)


def frame_table(result: LayoutResult) -> List[ReportItem]:
    """生成边框下料清单；边框只下料，不参与冲孔。"""

    geo = result.geometry
    spec = result.spec
    t = spec.frame_t
    w, h = geo.plate.w, geo.plate.h
    groups: Dict[Tuple[str, float], int] = {}
    order: List[Tuple[str, float]] = []

    top_notches = [n for n in geo.notches if n.source.edge == "top"]
    bottom_notches = [n for n in geo.notches if n.source.edge == "bottom"]
    left_notches = [n for n in geo.notches if n.source.edge == "left"]
    right_notches = [n for n in geo.notches if n.source.edge == "right"]

    horizontal_bands = (
        ("top", Segment(0.0, t), top_notches),
        ("bottom", Segment(h - t, h), bottom_notches),
    )
    vertical_bands = (
        ("left", Segment(0.0, t), left_notches),
        ("right", Segment(w - t, w), right_notches),
    )

    # 长边包住短边：宽不小于高时横向边框是主框，先列长边下料。
    primary_is_horizontal = w >= h
    primary_bands = horizontal_bands if primary_is_horizontal else vertical_bands
    secondary_bands = vertical_bands if primary_is_horizontal else horizontal_bands

    for _, _, notches in primary_bands:
        cuts = [Segment(n.clear.x0, n.clear.x1) for n in notches]
        if primary_is_horizontal:
            spans = _subtract_spans(0.0, w, cuts)
            direction = "横向"
        else:
            spans = _subtract_spans(t, h - t, cuts)
            direction = "纵向"
        for span in spans:
            _add_frame_piece(groups, order, direction, span.length)

    for _, _, notches in secondary_bands:
        cuts = [Segment(n.clear.y0, n.clear.y1) for n in notches]
        if primary_is_horizontal:
            spans = _subtract_spans(t, h - t, cuts)
            direction = "纵向"
        else:
            spans = _subtract_spans(0.0, w, cuts)
            direction = "横向"
        for span in spans:
            _add_frame_piece(groups, order, direction, span.length)

    # 缺口的三面包边：两条侧边和一块封头。
    for notch in top_notches:
        _add_frame_piece(groups, order, "纵向", notch.source.depth)
        _add_frame_piece(groups, order, "纵向", notch.source.depth)
        _add_frame_piece(groups, order, "横向", notch.clear.w)
    for notch in bottom_notches:
        _add_frame_piece(groups, order, "纵向", notch.source.depth)
        _add_frame_piece(groups, order, "纵向", notch.source.depth)
        _add_frame_piece(groups, order, "横向", notch.clear.w)
    for notch in left_notches:
        _add_frame_piece(groups, order, "横向", notch.source.depth)
        _add_frame_piece(groups, order, "横向", notch.source.depth)
        _add_frame_piece(groups, order, "纵向", notch.clear.h)
    for notch in right_notches:
        _add_frame_piece(groups, order, "横向", notch.source.depth)
        _add_frame_piece(groups, order, "横向", notch.source.depth)
        _add_frame_piece(groups, order, "纵向", notch.clear.h)

    return [
        ReportItem("边框", length, direction, count, None, None, 0, "")
        for direction, length in order
        for count in [groups[(direction, length)]]
    ]


def print_report(result: LayoutResult) -> str:
    spec = result.spec
    frame_items = frame_table(result)
    vertical_items = report_table(result, "纵向")
    horizontal_items = report_table(result, "横向")
    items = vertical_items + horizontal_items
    frame_total = sum(item.count for item in frame_items)
    total = sum(item.count for item in items)
    lines: List[str] = []
    lines.append(f"洞口：{fmt(spec.opening_w)} x {fmt(spec.opening_h)}，缩尺 {fmt(spec.shrink)}")
    lines.append(f"板件：{fmt(spec.plate_w)} x {fmt(spec.plate_h)}，边框 {fmt(spec.frame_t)}")
    lines.append(
        f"净空：{fmt(result.geometry.net.w)} x {fmt(result.geometry.net.h)}"
        f"，纵向 {fmt(spec.vertical.pitch)} / 横向 {fmt(spec.horizontal.pitch)}"
    )
    lines.append(f"边框下料：共 {frame_total} 段；内部扁钢：共 {total} 段")
    lines.append("边框下料（不冲孔）")
    lines.append("|序号|规格|尺寸|方向|数量|首孔距|尾孔距|孔数|")
    for no, item in enumerate(frame_items, 1):
        lines.append(
            f"  |{no}|{item.spec}|{fmt(item.length)}|{item.direction}|"
            f"{item.count}|||"
        )

    for title, section_items in (
        ("纵向扁钢下料 / 首尾孔距", vertical_items),
        ("横向扁钢下料 / 首尾孔距", horizontal_items),
    ):
        lines.append(title)
        lines.append("|序号|规格|尺寸|方向|数量|首孔距|尾孔距|孔数|")
        for no, item in enumerate(section_items, 1):
            first = report_hole(item.first_hole)
            last = report_hole(item.last_hole)
            lines.append(
                f"  |{no}|{item.spec}|{fmt(item.length)}|{item.direction}|"
                f"{item.count}|{first}|{last}|{item.holes}|"
            )
    return "\n".join(lines)


def export_csv(result: LayoutResult, out_dir: str | Path, filename: str | None = None) -> Path:
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    if filename is None:
        dimension = f"{fmt(result.spec.plate_w)}x{fmt(result.spec.plate_h)}"
        filename = f"排条清单_{dimension}.csv"
    csv_path = out / filename

    with open(csv_path, "w", encoding="utf-8-sig", newline="") as fp:
        writer = csv.writer(fp)
        writer.writerow(["边框下料（不冲孔）"])
        writer.writerow(HEADER)
        for no, item in enumerate(frame_table(result), 1):
            writer.writerow([no, item.spec, fmt(item.length), item.direction, item.count, "", "", ""])

        writer.writerow([])
        for title, section_items in (
            ("纵向扁钢下料 / 首尾孔距", report_table(result, "纵向")),
            ("横向扁钢下料 / 首尾孔距", report_table(result, "横向")),
        ):
            writer.writerow([])
            writer.writerow([title])
            writer.writerow(HEADER)
            for no, item in enumerate(section_items, 1):
                writer.writerow(
                    [
                        no,
                        item.spec,
                        fmt(item.length),
                        item.direction,
                        item.count,
                        report_hole(item.first_hole),
                        report_hole(item.last_hole),
                        item.holes,
                    ]
                )
    return csv_path


def _xlsx_sheet_title(no: int) -> str:
    return f"图形{no}"


def _xlsx_cell(ref: str, value: object) -> str:
    if value is None:
        return f'<c r="{ref}"/>'
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return f'<c r="{ref}"><v>{value}</v></c>'
    text = escape(str(value))
    return f'<c r="{ref}" t="inlineStr"><is><t xml:space="preserve">{text}</t></is></c>'


def _xlsx_sheet_xml(rows: List[List[object]]) -> str:
    row_xml: List[str] = []
    for row_no, row in enumerate(rows, 1):
        cells = []
        for col_no, value in enumerate(row, 1):
            ref = f"{chr(64 + col_no)}{row_no}"
            cells.append(_xlsx_cell(ref, value))
        row_xml.append(f'<row r="{row_no}">{"".join(cells)}</row>')
    return (
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        f'<sheetData>{"".join(row_xml)}</sheetData></worksheet>'
    )


def export_workbook(results: List[LayoutResult], out_dir: str | Path, filename: str | None = None) -> Path:
    """把多个排条清单合并成一个 xlsx 工作簿，每个板件一个 sheet。"""

    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    if filename is None:
        filename = "排条清单_合并.xlsx"
    if not filename.lower().endswith(".xlsx"):
        filename += ".xlsx"
    workbook_path = out / filename

    sheet_names = [_xlsx_sheet_title(no) for no in range(1, len(results) + 1)]
    sheet_xmls = []
    for result in results:
        rows: List[List[object]] = [["边框下料（不冲孔）"], HEADER]
        rows.extend(
            [no, item.spec, fmt(item.length), item.direction, item.count, "", "", ""]
            for no, item in enumerate(frame_table(result), 1)
        )
        rows.append([])
        for title, section_items in (
            ("纵向扁钢下料 / 首尾孔距", report_table(result, "纵向")),
            ("横向扁钢下料 / 首尾孔距", report_table(result, "横向")),
        ):
            rows.append([])
            rows.append([title])
            rows.append(HEADER)
            rows.extend(
                [
                    no,
                    item.spec,
                    fmt(item.length),
                    item.direction,
                    item.count,
                    report_hole(item.first_hole),
                    report_hole(item.last_hole),
                    item.holes,
                ]
                for no, item in enumerate(section_items, 1)
            )
        sheet_xmls.append(_xlsx_sheet_xml(rows))

    workbook_sheets = "".join(
        f'<sheet name="{escape(name)}" sheetId="{no}" r:id="rId{no}"/>'
        for no, name in enumerate(sheet_names, 1)
    )
    workbook_rels = "".join(
        f'<Relationship Id="rId{no}" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
        f'Target="worksheets/sheet{no}.xml"/>'
        for no in range(1, len(results) + 1)
    )
    workbook_rels += (
        '<Relationship Id="rIdStyles" '
        'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" '
        'Target="styles.xml"/>'
    )
    content_types = (
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
        + "".join(
            f'<Override PartName="/xl/worksheets/sheet{no}.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
            for no in range(1, len(results) + 1)
        )
        + "</Types>"
    )
    styles = (
        '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
        '<fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>'
        '<fills count="1"><fill><patternFill patternType="none"/></fill></fills>'
        '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
        '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
        '<cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>'
        "</styleSheet>"
    )

    with zipfile.ZipFile(workbook_path, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("[Content_Types].xml", content_types)
        zf.writestr(
            "_rels/.rels",
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
            'Target="xl/workbook.xml"/></Relationships>',
        )
        zf.writestr(
            "xl/workbook.xml",
            '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
            f"<sheets>{workbook_sheets}</sheets></workbook>",
        )
        zf.writestr(
            "xl/_rels/workbook.xml.rels",
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            f"{workbook_rels}</Relationships>",
        )
        zf.writestr("xl/styles.xml", styles)
        for no, sheet_xml in enumerate(sheet_xmls, 1):
            zf.writestr(f"xl/worksheets/sheet{no}.xml", sheet_xml)

    return workbook_path
