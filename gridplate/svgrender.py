# -*- coding: utf-8 -*-
"""SVG 排条图渲染。SVG 中 1 单位 = 1 mm。"""

from __future__ import annotations

from pathlib import Path
from typing import Iterable, List, Optional, Tuple
from xml.sax.saxutils import escape

from .layout import LayoutResult
from .report import ReportItem, fmt, frame_table, hole_annotations, report_hole, report_table


FONT = "Microsoft YaHei, SimHei, sans-serif"
GREEN = "#a8d5aa"
GREEN_D = "#2e9e46"
PINK = "#f6b8d0"
RED = "#c0392b"
WARNING = "#e67e22"
WARNING_FILL = "#fdebd0"
MARGIN_L = 90.0
MARGIN_T = 140.0
LEGEND_W = 820.0
TABLE_HEADERS = ["序号", "规格", "尺寸", "方向", "数量", "首孔距", "尾孔距", "孔数"]
TABLE_WIDTHS = [60.0, 120.0, 110.0, 80.0, 80.0, 120.0, 120.0, 80.0]


def _fmt_points(points: Iterable[Tuple[float, float]]) -> str:
    return " ".join(f"{fmt(x)},{fmt(y)}" for x, y in points)


def _short_indices(indices: str, limit: int = 10) -> str:
    items = indices.split(",")
    if len(items) <= limit:
        return indices
    return ",".join(items[:limit]) + f" 等{len(items)}条"


def _dimension_line(
    svg: List[str],
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    label: str,
    text_x: Optional[float] = None,
    text_y: Optional[float] = None,
    rotate: Optional[Tuple[float, float]] = None,
    anchor: str = "middle",
    font_size: float = 20,
    halo: bool = False,
) -> None:
    svg.append(
        f'<line x1="{fmt(x1)}" y1="{fmt(y1)}" x2="{fmt(x2)}" y2="{fmt(y2)}" '
        f'stroke="#555" stroke-width="1"/>'
    )
    if abs(x2 - x1) < 1e-9:
        svg.append(f'<line x1="{fmt(x1 - 8)}" y1="{fmt(y1)}" x2="{fmt(x1 + 8)}" y2="{fmt(y1)}" stroke="#555"/>')
        svg.append(f'<line x1="{fmt(x2 - 8)}" y1="{fmt(y2)}" x2="{fmt(x2 + 8)}" y2="{fmt(y2)}" stroke="#555"/>')
    else:
        svg.append(f'<line x1="{fmt(x1)}" y1="{fmt(y1 - 8)}" x2="{fmt(x1)}" y2="{fmt(y1 + 8)}" stroke="#555"/>')
        svg.append(f'<line x1="{fmt(x2)}" y1="{fmt(y2 - 8)}" x2="{fmt(x2)}" y2="{fmt(y2 + 8)}" stroke="#555"/>')

    tx = text_x if text_x is not None else (x1 + x2) / 2
    ty = text_y if text_y is not None else (y1 + y2) / 2 - 12
    transform = f' transform="rotate({rotate[0]} {fmt(rotate[1])} {fmt(rotate[2])})"' if rotate else ""
    halo_style = (
        ' stroke="#ffffff" stroke-width="6" paint-order="stroke"' if halo else ""
    )
    svg.append(
        f'<text x="{fmt(tx)}" y="{fmt(ty)}" text-anchor="{anchor}" font-family="{FONT}" '
        f'font-size="{fmt(font_size)}" fill="#333"{halo_style}{transform}>{escape(label)}</text>'
    )


def _table_svg(
    x: float,
    y: float,
    rows: List[ReportItem],
    title: str,
) -> Tuple[List[str], float]:
    svg: List[str] = []
    left = x + 24.0
    top = y + 42.0
    width = sum(TABLE_WIDTHS)
    header_h = 42.0
    row_h = 34.0
    height = header_h + row_h * max(1, len(rows))

    svg.append(
        f'<rect x="{fmt(left)}" y="{fmt(top)}" width="{fmt(width)}" height="{fmt(height)}" '
        f'fill="#ffffff" stroke="#bbb" stroke-width="1"/>'
    )

    svg.append(
        f'<text x="{fmt(left)}" y="{fmt(y + 26)}" font-family="{FONT}" '
        f'font-size="22" font-weight="bold" fill="#111">{escape(title)}</text>'
    )

    bounds = [left]
    for col_width in TABLE_WIDTHS:
        bounds.append(bounds[-1] + col_width)
    for boundary in bounds:
        svg.append(
            f'<line x1="{fmt(boundary)}" y1="{fmt(top)}" x2="{fmt(boundary)}" '
            f'y2="{fmt(top + height)}" stroke="#ddd" stroke-width="0.7"/>'
        )

    svg.append(
        f'<line x1="{fmt(left)}" y1="{fmt(top + header_h)}" x2="{fmt(left + width)}" '
        f'y2="{fmt(top + header_h)}" stroke="#999" stroke-width="1"/>'
    )
    for i, text in enumerate(TABLE_HEADERS):
        svg.append(
            f'<text x="{fmt(bounds[i] + 10)}" y="{fmt(top + 27)}" font-family="{FONT}" '
            f'font-size="20" font-weight="bold" fill="#222">{escape(text)}</text>'
        )

    for row_no, row in enumerate(rows, 1):
        top_y = top + header_h + (row_no - 1) * row_h
        if row_no > 1:
            svg.append(
                f'<line x1="{fmt(left)}" y1="{fmt(top_y)}" x2="{fmt(left + width)}" '
                f'y2="{fmt(top_y)}" stroke="#eee" stroke-width="0.8"/>'
            )
        first = report_hole(row.first_hole)
        last = report_hole(row.last_hole)
        holes = report_hole(row.holes)
        values = [
            str(row_no),
            row.spec,
            fmt(row.length),
            row.direction,
            str(row.count),
            first,
            last,
            holes,
        ]
        for col_no, (value, boundary) in enumerate(zip(values, bounds[:-1])):
            color = "#222"
            if col_no == 3:
                color = GREEN_D if row.direction == "纵向" else "#ad1457"
            svg.append(
                f'<text x="{fmt(boundary + 10)}" y="{fmt(top_y + 24)}" font-family="{FONT}" '
                f'font-size="19" fill="{color}">{escape(value)}</text>'
            )

    return svg, top + height


def _param_lines(result: LayoutResult) -> List[Tuple[str, Optional[str], bool]]:
    spec = result.spec
    warning_count = sum(bool(bar.warnings) for bar in result.vertical_bars + result.horizontal_bars)
    lines: List[Tuple[str, Optional[str], bool]] = [
        ("排条参数", None, True),
        (f"缩尺 {fmt(spec.shrink)}｜边框厚 {fmt(spec.frame_t)}", None, False),
        (
            f"纵条厚 {fmt(spec.vertical.thickness)}｜中心距 {fmt(spec.vertical.pitch)}",
            GREEN,
            False,
        ),
        (
            f"横条厚 {fmt(spec.horizontal.thickness)}｜中心距 {fmt(spec.horizontal.pitch)}",
            PINK,
            False,
        ),
    ]
    if warning_count:
        lines.append(
            (
                f"橙色提示：{warning_count} 根距缺口边框不足 {fmt(spec.frame_t)}mm，已截断/分段",
                WARNING_FILL,
                False,
            )
        )
    return lines


def render_svg(result: LayoutResult) -> str:
    spec = result.spec
    geo = result.geometry
    w, h = spec.plate_w, spec.plate_h
    svg: List[str] = []
    legend_x = MARGIN_L + w + 170.0
    legend_y = MARGIN_T
    frame_rows = frame_table(result)
    vertical_rows = report_table(result, "纵向")
    horizontal_rows = report_table(result, "横向")
    frame_svg, frame_bottom = _table_svg(
        legend_x,
        legend_y,
        frame_rows,
        f"边框下料（共 {sum(row.count for row in frame_rows)} 段，不冲孔）",
    )
    vertical_y = frame_bottom + 48.0
    vertical_svg, vertical_bottom = _table_svg(
        legend_x,
        vertical_y,
        vertical_rows,
        f"纵向扁钢下料 / 首尾孔距（共 {sum(row.count for row in vertical_rows)} 段）",
    )
    horizontal_y = vertical_bottom + 48.0
    horizontal_svg, table_bottom = _table_svg(
        legend_x,
        horizontal_y,
        horizontal_rows,
        f"横向扁钢下料 / 首尾孔距（共 {sum(row.count for row in horizontal_rows)} 段）",
    )
    legend_lines = _param_lines(result)
    params_h = sum(44.0 if bold else 36.0 for _, _, bold in legend_lines)
    legend_h = max(720.0, table_bottom - legend_y + 40.0 + params_h + 40.0)
    total_w = legend_x + LEGEND_W + 80.0
    total_h = max(MARGIN_T + h + 230.0, legend_y + legend_h + 80.0)

    svg.append(
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{fmt(total_w)}" '
        f'height="{fmt(total_h)}" viewBox="0 0 {fmt(total_w)} {fmt(total_h)}">'
    )
    svg.append(f'<rect width="{fmt(total_w)}" height="{fmt(total_h)}" fill="#ffffff"/>')
    title = (
        f"钢格板排条图 {fmt(w)} × {fmt(h)}（原洞口 {fmt(spec.opening_w)} × {fmt(spec.opening_h)}，"
        f"缩尺 {fmt(spec.shrink)}）"
    )
    svg.append(
        f'<text x="{fmt(MARGIN_L)}" y="55" font-family="{FONT}" font-size="30" '
        f'font-weight="bold" fill="#111">{escape(title)}</text>'
    )
    svg.append(f'<g transform="translate({fmt(MARGIN_L)},{fmt(MARGIN_T)})">')

    svg.append(
        f'<polygon points="{_fmt_points(_plate_points(result))}" fill="#f3dcdc" stroke="{RED}" stroke-width="3"/>'
    )
    svg.append(
        f'<polygon points="{_fmt_points(_net_points(result))}" fill="#ffffff" stroke="{RED}" stroke-width="1.5"/>'
    )

    for notch in geo.notches:
        f = notch.frame
        svg.append(
            f'<rect x="{fmt(f.x0)}" y="{fmt(f.y0)}" width="{fmt(f.w)}" height="{fmt(f.h)}" '
            f'fill="none" stroke="{RED}" stroke-width="1.2" stroke-dasharray="8,5"/>'
        )

    for bar in result.vertical_bars:
        for seg in bar.segments:
            short = not (len(bar.segments) == 1 and abs(seg.length - geo.net.h) <= 1e-6)
            fill = GREEN_D if short else GREEN
            stroke = "#1b5e20" if short else "#2e7d32"
            svg.append(
                f'<rect x="{fmt(bar.center - bar.thickness / 2)}" y="{fmt(seg.a)}" '
                f'width="{fmt(bar.thickness)}" height="{fmt(seg.length)}" fill="{fill}" '
                f'stroke="{stroke}" stroke-width="0.4"/>'
            )

    for bar in result.horizontal_bars:
        for seg in bar.segments:
            fill = PINK
            stroke = "#ad1457"
            svg.append(
                f'<rect x="{fmt(seg.a)}" y="{fmt(bar.center - bar.thickness / 2)}" '
                f'width="{fmt(seg.length)}" height="{fmt(bar.thickness)}" fill="{fill}" '
                f'stroke="{stroke}" stroke-width="0.4"/>'
            )

    for bar in result.vertical_bars + result.horizontal_bars:
        if not bar.warnings:
            continue
        for seg in bar.segments:
            if bar.orientation == "vertical":
                x, y, rect_w, rect_h = bar.center - bar.thickness / 2, seg.a, bar.thickness, seg.length
            else:
                x, y, rect_w, rect_h = seg.a, bar.center - bar.thickness / 2, seg.length, bar.thickness
            svg.append(
                f'<rect x="{fmt(x)}" y="{fmt(y)}" width="{fmt(rect_w)}" height="{fmt(rect_h)}" '
                f'fill="{WARNING_FILL}" stroke="{WARNING}" stroke-width="1.4"/>'
            )

    # 每根内部扁钢都标长度：满长标在边框外侧，分段标在该段中心。
    for bar in result.horizontal_bars:
        for seg in bar.segments:
            cx = w + 30 if bar.full else (seg.a + seg.b) / 2
            ty = bar.center + 5
            svg.append(
                f'<text x="{fmt(cx)}" y="{fmt(ty)}" text-anchor="middle" font-family="{FONT}" '
                f'font-size="18" font-weight="bold" fill="#1a1a1a">{escape(fmt(seg.length))}</text>'
            )

    for bar in result.vertical_bars:
        for seg in bar.segments:
            tx = bar.center
            cy = h + 25 if bar.full else (seg.a + seg.b) / 2
            transform = f' transform="rotate(-90 {fmt(tx)} {fmt(cy)})"'
            svg.append(
                f'<text x="{fmt(tx)}" y="{fmt(cy)}" text-anchor="middle" font-family="{FONT}" '
                f'font-size="18" font-weight="bold" fill="#1a1a1a"{transform}>{escape(fmt(seg.length))}</text>'
            )

    # 顶部分段按缩尺后的板件坐标标注；缺口位置取边框外沿。
    bounds = [0.0]
    for notch in sorted((n for n in geo.notches if n.source.edge == "top"), key=lambda n: n.clear.x0):
        bounds.extend([notch.clear.x0, notch.clear.x1])
    bounds.append(w)
    for a, b in zip(bounds[:-1], bounds[1:]):
        if b - a > 1e-6:
            _dimension_line(svg, a, -22, b, -22, fmt(b - a))

    for notch in geo.notches:
        if notch.source.edge == "top":
            x = (notch.clear.x0 + notch.clear.x1) / 2
            _dimension_line(
                svg,
                x,
                notch.clear.y0,
                x,
                notch.clear.y1,
                fmt(notch.source.depth),
                text_x=x + 50.0,
                text_y=(notch.clear.y0 + notch.clear.y1) / 2.0,
                anchor="middle",
                font_size=18,
            )
        elif notch.source.edge == "bottom":
            x = (notch.clear.x0 + notch.clear.x1) / 2
            _dimension_line(
                svg,
                x,
                notch.clear.y0,
                x,
                notch.clear.y1,
                fmt(notch.source.depth),
                text_x=x + 50.0,
                text_y=(notch.clear.y0 + notch.clear.y1) / 2.0,
                anchor="middle",
                font_size=18,
            )

    # 首尾孔按规格只标注一次：横向标在段两端上方，纵向标在段两端左侧。
    for annot in hole_annotations(result):
        first = fmt(annot.first_hole)
        last = fmt(annot.last_hole)
        if annot.direction == "横向":
            dim_y = annot.bar_center - annot.bar_thickness / 2.0 - 10.0
            _dimension_line(
                svg,
                annot.seg_a,
                dim_y,
                annot.first_pos,
                dim_y,
                f"首孔{first}",
                text_x=annot.seg_a - 6.0,
                text_y=dim_y + 6.0,
                anchor="end",
                font_size=17,
                halo=True,
            )
            _dimension_line(
                svg,
                annot.last_pos,
                dim_y,
                annot.seg_b,
                dim_y,
                f"尾孔{last}",
                text_x=annot.seg_b + 6.0,
                text_y=dim_y + 6.0,
                anchor="start",
                font_size=17,
                halo=True,
            )
        else:
            dim_x = annot.bar_center - annot.bar_thickness / 2.0 - 8.0
            _dimension_line(
                svg,
                dim_x,
                annot.seg_a,
                dim_x,
                annot.first_pos,
                f"首孔{first}",
                text_x=dim_x - 6.0,
                text_y=annot.seg_a - 8.0,
                anchor="end",
                font_size=17,
                halo=True,
            )
            _dimension_line(
                svg,
                dim_x,
                annot.last_pos,
                dim_x,
                annot.seg_b,
                f"尾孔{last}",
                text_x=dim_x - 6.0,
                text_y=annot.seg_b + 24.0,
                anchor="end",
                font_size=17,
                halo=True,
            )

    step = 5 if len(result.vertical_bars) > 20 else max(1, len(result.vertical_bars) // 10)
    shown = list(range(1, len(result.vertical_bars) + 1, step))
    if result.vertical_bars and shown[-1] != result.vertical_bars[-1].index:
        shown.append(result.vertical_bars[-1].index)
    for idx in shown:
        center = result.vertical_bars[idx - 1].center
        svg.append(
            f'<text x="{fmt(center)}" y="{fmt(h + 100)}" text-anchor="middle" font-family="{FONT}" '
            f'font-size="17" fill="#333">{idx}</text>'
        )

    _dimension_line(svg, 0, h + 145, w, h + 145, fmt(w), text_y=h + 172)
    _dimension_line(svg, -45, 0, -45, h, fmt(h), text_x=-65, text_y=(h / 2), rotate=(-90, -65, h / 2))
    svg.append("</g>")

    svg.append(
        f'<rect x="{fmt(legend_x)}" y="{fmt(legend_y)}" width="{fmt(LEGEND_W)}" height="{fmt(legend_h)}" '
        f'fill="#fbf9f4" stroke="#bbb"/>'
    )
    svg.extend(frame_svg)
    svg.extend(vertical_svg)
    svg.extend(horizontal_svg)
    y = table_bottom + 45
    for text, swatch, bold in legend_lines:
        weight = ' font-weight="bold"' if bold else ""
        svg.append(
            f'<text x="{fmt(legend_x + 24)}" y="{fmt(y)}" font-family="{FONT}" font-size="20"{weight} '
            f'fill="#222">{escape(text)}</text>'
        )
        if swatch:
            svg.append(
                f'<rect x="{fmt(legend_x + LEGEND_W - 62)}" y="{fmt(y - 15)}" width="26" height="18" '
                f'fill="{swatch}" stroke="#666" stroke-width="0.6"/>'
            )
        y += 44 if bold else 36

    svg.append("</svg>")
    return "\n".join(svg)


def _plate_points(result: LayoutResult) -> List[Tuple[float, float]]:
    from .geometry import plate_outline

    return plate_outline(result.geometry)


def _net_points(result: LayoutResult) -> List[Tuple[float, float]]:
    from .geometry import net_outline

    return net_outline(result.geometry)


def save_svg(result: LayoutResult, path: str | Path) -> Path:
    out = Path(path)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render_svg(result), encoding="utf-8")
    return out
