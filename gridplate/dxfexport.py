# -*- coding: utf-8 -*-
"""把排条布局导出为 DXF。坐标单位为 mm，图层按用途分开。"""

from __future__ import annotations

from pathlib import Path
from typing import Iterable, List, Sequence, Tuple

from .geometry import net_outline, plate_outline
from .layout import LayoutResult
from .report import ReportItem, fmt, frame_table, hole_annotations, report_hole, report_table


DX = 100.0
DY = 160.0
FONT_H = 20.0
_CAD_HEIGHT = 0.0
_FLIP_Y = True
_OFFSET_X = 0.0
_OFFSET_Y = 0.0

TABLE_HEADERS = ["序号", "规格", "尺寸", "方向", "数量", "首孔距", "尾孔距", "孔数"]
TABLE_WIDTHS = [60.0, 130.0, 120.0, 90.0, 90.0, 140.0, 140.0, 90.0]

LAYER_COLORS = {
    "外轮廓": 1,
    "净空": 7,
    "缺口边框": 1,
    "纵条": 3,
    "横条": 6,
    "临界提示": 30,
    "标注": 5,
    "尺寸": 2,
    "首尾孔距": 4,
    "表格": 7,
    "分组边框": 8,
}


def _to_x(x: float) -> float:
    return x + DX + _OFFSET_X


def _to_y(y: float) -> float:
    if _FLIP_Y:
        return _CAD_HEIGHT - y + DY + _OFFSET_Y
    return y + DY + _OFFSET_Y


def _pairs(values: Iterable[Tuple[str, str]]) -> str:
    return "\n".join(f"{code}\n{value}" for code, value in values)


def _add_lwpolyline(entities: List[str], layer: str, points: Sequence[Tuple[float, float]], closed: bool) -> None:
    entities.append(
        _pairs(
            [
                ("0", "POLYLINE"),
                ("8", layer),
                ("66", "1"),
                ("70", "1" if closed else "0"),
            ]
        )
    )
    for x, y in points:
        entities.append(
            _pairs(
                [
                    ("0", "VERTEX"),
                    ("8", layer),
                    ("10", fmt(_to_x(x))),
                    ("20", fmt(_to_y(y))),
                ]
            )
        )
    entities.append(
        _pairs(
            [
                ("0", "SEQEND"),
                ("8", layer),
            ]
        )
    )


def _add_line(
    entities: List[str],
    layer: str,
    x1: float,
    y1: float,
    x2: float,
    y2: float,
) -> None:
    entities.append(
        _pairs(
            [
                ("0", "LINE"),
                ("8", layer),
                ("10", fmt(_to_x(x1))),
                ("20", fmt(_to_y(y1))),
                ("11", fmt(_to_x(x2))),
                ("21", fmt(_to_y(y2))),
            ]
        )
    )


def _add_rect(
    entities: List[str],
    layer: str,
    x: float,
    y: float,
    w: float,
    h: float,
) -> None:
    _add_lwpolyline(
        entities,
        layer,
        [(x, y), (x + w, y), (x + w, y + h), (x, y + h)],
        True,
    )


def _add_text(
    entities: List[str],
    layer: str,
    x: float,
    y: float,
    text: str,
    height: float = FONT_H,
    angle: float = 0.0,
    center: bool = True,
    width_factor: float = 1.0,
) -> None:
    values: List[Tuple[str, str]] = [
        ("0", "TEXT"),
        ("8", layer),
        ("10", fmt(_to_x(x))),
        ("20", fmt(_to_y(y))),
        ("30", "0"),
        ("40", fmt(height)),
        ("41", fmt(width_factor)),
        ("1", text),
        ("50", fmt(angle)),
    ]
    if center:
        values.extend(
            [
                ("72", "1"),
                ("11", fmt(_to_x(x))),
                ("21", fmt(_to_y(y))),
            ]
        )
    entities.append(_pairs(values))


def _add_dimension(
    entities: List[str],
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    label: str,
    text_x: float | None = None,
    text_y: float | None = None,
    angle: float = 0.0,
    layer: str = "尺寸",
    height: float = FONT_H,
) -> None:
    _add_line(entities, layer, x1, y1, x2, y2)
    if abs(x2 - x1) < 1e-9:
        _add_line(entities, layer, x1 - 8, y1, x1 + 8, y1)
        _add_line(entities, layer, x2 - 8, y2, x2 + 8, y2)
    else:
        _add_line(entities, layer, x1, y1 - 8, x1, y1 + 8)
        _add_line(entities, layer, x2, y2 - 8, x2, y2 + 8)
    tx = text_x if text_x is not None else (x1 + x2) / 2
    ty = text_y if text_y is not None else (y1 + y2) / 2 - 12
    _add_text(entities, layer, tx, ty, label, height=height, angle=angle)


def _add_table(
    entities: List[str],
    x: float,
    top: float,
    rows: List[ReportItem],
    title: str,
) -> float:
    global _FLIP_Y
    old_flip = _FLIP_Y
    left = x + 24.0
    width = sum(TABLE_WIDTHS)
    header_h = 42.0
    row_h = 34.0
    height = header_h + row_h * max(1, len(rows))
    bottom = top - height
    _FLIP_Y = False

    _add_rect(entities, "表格", left, bottom, width, height)
    _add_text(entities, "表格", left + width / 2, top + 26, title, height=22, width_factor=0.85)

    bounds = [left]
    for col_width in TABLE_WIDTHS:
        bounds.append(bounds[-1] + col_width)
    for boundary in bounds:
        _add_line(entities, "表格", boundary, bottom, boundary, top)
    _add_line(entities, "表格", left, top - header_h, left + width, top - header_h)

    for i, text in enumerate(TABLE_HEADERS):
        _add_text(entities, "表格", bounds[i] + 10, top - 27, text, center=False, width_factor=0.9)

    for row_no, row in enumerate(rows, 1):
        top_y = top - header_h - (row_no - 1) * row_h
        if row_no > 1:
            _add_line(entities, "表格", left, top_y, left + width, top_y)
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
        for value, boundary in zip(values, bounds[:-1]):
            _add_text(
                entities,
                "表格",
                boundary + 10,
                top_y - 24,
                value,
                height=18,
                center=False,
                width_factor=0.9,
            )
    _FLIP_Y = old_flip
    return bottom


def _render_dxf_entities(result: LayoutResult, origin_x: float = 0.0, origin_y: float = 0.0) -> List[str]:
    spec = result.spec
    geo = result.geometry
    w, h = spec.plate_w, spec.plate_h
    global _CAD_HEIGHT, _FLIP_Y, _OFFSET_X, _OFFSET_Y
    _CAD_HEIGHT = h
    _FLIP_Y = True
    _OFFSET_X = origin_x
    _OFFSET_Y = origin_y
    entities: List[str] = []

    _add_lwpolyline(entities, "外轮廓", plate_outline(geo), True)
    _add_lwpolyline(entities, "净空", net_outline(geo), True)
    for notch in geo.notches:
        f = notch.frame
        _add_rect(entities, "缺口边框", f.x0, f.y0, f.w, f.h)

    for bar in result.vertical_bars:
        for seg in bar.segments:
            _add_rect(entities, "纵条", bar.center - bar.thickness / 2, seg.a, bar.thickness, seg.length)
    for bar in result.horizontal_bars:
        for seg in bar.segments:
            _add_rect(entities, "横条", seg.a, bar.center - bar.thickness / 2, seg.length, bar.thickness)

    for bar in result.vertical_bars + result.horizontal_bars:
        if not bar.warnings:
            continue
        for seg in bar.segments:
            if bar.orientation == "vertical":
                _add_rect(
                    entities,
                    "临界提示",
                    bar.center - bar.thickness / 2,
                    seg.a,
                    bar.thickness,
                    seg.length,
                )
            else:
                _add_rect(
                    entities,
                    "临界提示",
                    seg.a,
                    bar.center - bar.thickness / 2,
                    seg.length,
                    bar.thickness,
                )

    # 每根内部扁钢都标长度：满长标在边框外侧，分段标在该段中心。
    for bar in result.horizontal_bars:
        for seg in bar.segments:
            cx = w + 30 if bar.full else (seg.a + seg.b) / 2
            _add_text(entities, "标注", cx, bar.center + 5, fmt(seg.length), height=18)

    for bar in result.vertical_bars:
        for seg in bar.segments:
            cy = h + 25 if bar.full else (seg.a + seg.b) / 2
            _add_text(entities, "标注", bar.center, cy, fmt(seg.length), height=18, angle=270)

    bounds = [0.0]
    top_notches = sorted((n for n in geo.notches if n.source.edge == "top"), key=lambda n: n.clear.x0)
    for notch in top_notches:
        bounds.extend([notch.clear.x0, notch.clear.x1])
    bounds.append(w)
    for a, b in zip(bounds[:-1], bounds[1:]):
        if b - a > 1e-6:
            _add_dimension(entities, a, -22, b, -22, fmt(b - a))

    for notch in geo.notches:
        x = (notch.clear.x0 + notch.clear.x1) / 2
        _add_dimension(
            entities,
            x,
            notch.clear.y0,
            x,
            notch.clear.y1,
            fmt(notch.source.depth),
            text_x=x + 50.0,
            text_y=(notch.clear.y0 + notch.clear.y1) / 2.0,
            layer="尺寸",
        )

    _add_dimension(entities, 0, h + 145, w, h + 145, fmt(w), text_y=h + 172)
    _add_dimension(entities, -45, 0, -45, h, fmt(h), text_x=-65, text_y=h / 2, angle=270)

    # 首尾孔按规格只标注一次：横向标在段两端上方，纵向标在段两端左侧。
    for annot in hole_annotations(result):
        first = fmt(annot.first_hole)
        last = fmt(annot.last_hole)
        if annot.direction == "横向":
            dim_y = annot.bar_center - annot.bar_thickness / 2.0 - 10.0
            _add_dimension(
                entities,
                annot.seg_a,
                dim_y,
                annot.first_pos,
                dim_y,
                f"首{first}",
                text_x=annot.seg_a - 50.0,
                text_y=dim_y + 6.0,
                height=14,
                layer="首尾孔距",
            )
            _add_dimension(
                entities,
                annot.last_pos,
                dim_y,
                annot.seg_b,
                dim_y,
                f"尾{last}",
                text_x=annot.seg_b + 50.0,
                text_y=dim_y + 6.0,
                height=14,
                layer="首尾孔距",
            )
        else:
            dim_x = annot.bar_center - annot.bar_thickness / 2.0 - 8.0
            _add_dimension(
                entities,
                dim_x,
                annot.seg_a,
                dim_x,
                annot.first_pos,
                f"首{first}",
                text_x=dim_x - 50.0,
                text_y=annot.seg_a - 8.0,
                angle=270,
                height=14,
                layer="首尾孔距",
            )
            _add_dimension(
                entities,
                dim_x,
                annot.last_pos,
                dim_x,
                annot.seg_b,
                f"尾{last}",
                text_x=dim_x - 50.0,
                text_y=annot.seg_b + 24.0,
                angle=270,
                height=14,
                layer="首尾孔距",
            )

    step = 5 if len(result.vertical_bars) > 20 else max(1, len(result.vertical_bars) // 10)
    shown = list(range(1, len(result.vertical_bars) + 1, step))
    if result.vertical_bars and shown[-1] != result.vertical_bars[-1].index:
        shown.append(result.vertical_bars[-1].index)
    for idx in shown:
        center = result.vertical_bars[idx - 1].center
        _add_text(entities, "标注", center, h + 100, str(idx), height=17)

    legend_x = DX + w + 170.0
    frame_rows = frame_table(result)
    vertical_rows = report_table(result, "纵向")
    horizontal_rows = report_table(result, "横向")
    table_top = h
    frame_bottom = _add_table(
        entities,
        legend_x,
        table_top,
        frame_rows,
        f"边框下料（共 {sum(row.count for row in frame_rows)} 段，不冲孔）",
    )
    vertical_bottom = _add_table(
        entities,
        legend_x,
        frame_bottom - 80.0,
        vertical_rows,
        f"纵向扁钢下料 / 首尾孔距（共 {sum(row.count for row in vertical_rows)} 段）",
    )
    horizontal_bottom = _add_table(
        entities,
        legend_x,
        vertical_bottom - 80.0,
        horizontal_rows,
        f"横向扁钢下料 / 首尾孔距（共 {sum(row.count for row in horizontal_rows)} 段）",
    )

    # 合并 DXF 时用分组边框把每块板件和它的表格圈起来，便于区分相邻图形。
    table_bottom = min(frame_bottom, vertical_bottom, horizontal_bottom)
    block_min_x = -90.0
    block_min_y = min(-100.0, table_bottom - 50.0)
    block_max_x = legend_x + sum(TABLE_WIDTHS) + 50.0
    block_max_y = h + 200.0
    _add_rect(
        entities,
        "分组边框",
        block_min_x,
        block_min_y,
        block_max_x - block_min_x,
        block_max_y - block_min_y,
    )

    return entities


def _dxf_header_and_tables() -> str:
    layer_table = "\n".join(
        _pairs(
            [
                ("0", "LAYER"),
                ("2", name),
                ("70", "0"),
                ("62", str(color)),
                ("6", "CONTINUOUS"),
            ]
        )
        for name, color in LAYER_COLORS.items()
    )

    header = _pairs(
        [
            ("0", "SECTION"),
            ("2", "HEADER"),
            ("9", "$ACADVER"),
            ("1", "AC1009"),
            ("9", "$DWGCODEPAGE"),
            ("3", "ANSI_936"),
            ("0", "ENDSEC"),
        ]
    )
    ltype_table = _pairs([("0", "TABLE"), ("2", "LTYPE"), ("70", "3")])
    for name, description in (
        ("BYBLOCK", "Solid line"),
        ("BYLAYER", "Solid line"),
        ("CONTINUOUS", "Solid line"),
    ):
        ltype_table += "\n" + _pairs(
            [
                ("0", "LTYPE"),
                ("2", name),
                ("70", "0"),
                ("3", description),
                ("72", "65"),
                ("73", "0"),
                ("40", "0"),
            ]
        )
    ltype_table += "\n" + _pairs([("0", "ENDTAB")])

    style_table = _pairs([("0", "TABLE"), ("2", "STYLE"), ("70", "1")])
    style_table += "\n" + _pairs(
        [
            ("0", "STYLE"),
            ("2", "STANDARD"),
            ("70", "0"),
            ("40", "0"),
            ("41", "1"),
            ("50", "0"),
            ("71", "0"),
            ("42", "0.2"),
            ("3", "simfang.ttf"),
            ("4", ""),
        ]
    )
    style_table += "\n" + _pairs([("0", "ENDTAB")])

    tables = _pairs([("0", "SECTION"), ("2", "TABLES")])
    tables += "\n" + ltype_table + "\n" + style_table
    tables += "\n" + _pairs([("0", "TABLE"), ("2", "LAYER"), ("70", str(len(LAYER_COLORS)))])
    tables += "\n" + layer_table + "\n" + _pairs([("0", "ENDTAB"), ("0", "ENDSEC")])
    return "\n".join([header, tables])


def render_dxf(result: LayoutResult) -> str:
    entities = _render_dxf_entities(result)
    return "\n".join(
        [
            _dxf_header_and_tables(),
            _pairs([("0", "SECTION"), ("2", "ENTITIES")]),
            "\n".join(entities),
            _pairs([("0", "ENDSEC"), ("0", "EOF")]),
        ]
    )


def render_dxf_many(results: List[LayoutResult], gap: float = 120.0) -> str:
    entity_bodies: List[str] = []
    offset_x = 0.0
    for result in results:
        entity_bodies.append("\n".join(_render_dxf_entities(result, origin_x=offset_x)))
        # 分组边框会向左/右各多占一点，偏移量需要额外加上这部分宽度。
        offset_x += DX + result.spec.plate_w + 170.0 + sum(TABLE_WIDTHS) + 140.0 + gap
    return "\n".join(
        [
            _dxf_header_and_tables(),
            _pairs([("0", "SECTION"), ("2", "ENTITIES")]),
            "\n".join(entity_bodies),
            _pairs([("0", "ENDSEC"), ("0", "EOF")]),
        ]
    )


def save_dxf(result: LayoutResult, path: str | Path) -> Path:
    out = Path(path)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render_dxf(result), encoding="gb18030")
    return out


def save_dxf_many(results: List[LayoutResult], path: str | Path) -> Path:
    out = Path(path)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render_dxf_many(results), encoding="gb18030")
    return out
