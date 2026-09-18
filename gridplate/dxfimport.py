# -*- coding: utf-8 -*-
"""从 DXF 的闭合外轮廓提取洞口、缩尺、边框和排条参数。"""

from __future__ import annotations

import math
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Tuple

from .model import BarSpec, Notch, Spec


EPS = 1e-6
Point = Tuple[float, float]


def _read_groups(path: Path) -> List[Tuple[int, str]]:
    text: str | None = None
    for encoding in ("utf-8-sig", "gb18030", "cp936"):
        try:
            text = path.read_text(encoding=encoding)
            break
        except UnicodeDecodeError:
            continue
    if text is None:
        raise ValueError("DXF 编码无法识别")
    lines = text.splitlines()
    if len(lines) % 2 != 0:
        raise ValueError("DXF 文件格式不完整")
    return [(int(lines[i].strip()), lines[i + 1]) for i in range(0, len(lines) - 1, 2)]


def _read_entities(groups: List[Tuple[int, str]]) -> List[Dict[str, object]]:
    entities: List[Dict[str, object]] = []
    current: Dict[str, object] | None = None
    in_entities = False
    section_name: str | None = None

    for code, value in groups:
        text = value.strip()
        if code == 0 and text == "SECTION":
            section_name = None
            continue
        if section_name is None and code == 2:
            section_name = text
            in_entities = section_name == "ENTITIES"
            continue
        if code == 0 and text == "ENDSEC":
            if current is not None:
                entities.append(current)
                current = None
            in_entities = False
            section_name = None
            continue
        if not in_entities:
            continue
        if code == 0:
            if current is not None:
                entities.append(current)
            current = {"type": text, "values": {}}
            continue
        if current is not None:
            values = current["values"]
            assert isinstance(values, dict)
            values.setdefault(code, []).append(value)

    if current is not None:
        entities.append(current)
    return entities


def _points(entity: Dict[str, object]) -> List[Point]:
    values = entity["values"]
    assert isinstance(values, dict)
    xs = [float(v) for v in values.get(10, [])]
    ys = [float(v) for v in values.get(20, [])]
    if len(xs) != len(ys) or len(xs) < 3:
        return []
    points = list(zip(xs, ys))
    closed = bool(int(values.get(70, ["0"])[0]) & 1)
    if closed and len(points) > 1:
        first, last = points[0], points[-1]
        if abs(first[0] - last[0]) <= EPS and abs(first[1] - last[1]) <= EPS:
            points = points[:-1]
    return points


def _single_point(entity: Dict[str, object]) -> Point | None:
    values = entity["values"]
    assert isinstance(values, dict)
    xs = values.get(10, [])
    ys = values.get(20, [])
    if not xs or not ys:
        return None
    return (float(xs[0]), float(ys[0]))


def _polyline_area(points: List[Point]) -> float:
    return abs(
        sum(
            points[i][0] * points[(i + 1) % len(points)][1]
            - points[(i + 1) % len(points)][0] * points[i][1]
            for i in range(len(points))
        )
        / 2.0
    )


def _extract_outlines(entities: List[Dict[str, object]]) -> List[List[Point]]:
    candidates: List[List[Point]] = []
    outline_layers = {"外轮廓", "0"}
    preferred: List[List[Point]] = []
    pending_polyline: bool = False
    pending_points: List[Point] = []
    pending_layer = ""
    for entity in entities:
        if entity["type"] == "LWPOLYLINE":
            points = _points(entity)
            if len(points) >= 3 and _polyline_area(points) > EPS:
                candidates.append(points)
                values = entity["values"]
                assert isinstance(values, dict)
                layer = values.get(8, ["0"])[0]
                if layer in outline_layers:
                    preferred.append(points)
        elif entity["type"] == "POLYLINE":
            pending_polyline = True
            pending_points = []
            values = entity["values"]
            assert isinstance(values, dict)
            pending_layer = values.get(8, ["0"])[0]
        elif entity["type"] == "VERTEX" and pending_polyline:
            point = _single_point(entity)
            if point is not None:
                pending_points.append(point)
        elif entity["type"] == "SEQEND" and pending_polyline:
            if len(pending_points) >= 3 and _polyline_area(pending_points) > EPS:
                candidates.append(pending_points)
                if pending_layer in outline_layers:
                    preferred.append(pending_points)
            pending_polyline = False
            pending_points = []
    if not candidates:
        raise ValueError("DXF 中没有找到闭合外轮廓（LWPOLYLINE/POLYLINE）")
    # 多图形必须保持源 DXF 中的实体顺序，避免合并输出和清单 sheet 顺序错乱。
    return preferred if preferred else candidates


def _extract_outline(entities: List[Dict[str, object]]) -> List[Point]:
    return _extract_outlines(entities)[0]


def _axis_vectors(points: List[Point]) -> Tuple[Point, Point]:
    edges: List[Tuple[float, float, float]] = []
    for i in range(len(points)):
        x1, y1 = points[i]
        x2, y2 = points[(i + 1) % len(points)]
        dx, dy = x2 - x1, y2 - y1
        length = math.hypot(dx, dy)
        if length > EPS:
            edges.append((dx / length, dy / length, length))
    if not edges:
        raise ValueError("DXF 外轮廓没有可用的边")
    ux, uy, _ = max(edges, key=lambda item: (abs(item[0]), item[2]))
    if ux < 0:
        ux, uy = -ux, -uy
    vx, vy = -uy, ux
    return (ux, uy), (vx, vy)


def _normalize(points: List[Point]) -> List[Point]:
    (ux, uy), (vx, vy) = _axis_vectors(points)

    origin = points[0]
    local: List[Point] = []
    for x, y in points:
        local.append(((x - origin[0]) * ux + (y - origin[1]) * uy,
                      (x - origin[0]) * vx + (y - origin[1]) * vy))
    min_x = min(p[0] for p in local)
    min_y = min(p[1] for p in local)
    return [(round(p[0] - min_x, 6), round(p[1] - min_y, 6)) for p in local]


@dataclass(frozen=True)
class _Run:
    kind: str
    start: Point
    end: Point

    @property
    def x_span(self) -> Tuple[float, float]:
        return (min(self.start[0], self.end[0]), max(self.start[0], self.end[0]))

    @property
    def y_span(self) -> Tuple[float, float]:
        return (min(self.start[1], self.end[1]), max(self.start[1], self.end[1]))


def _build_runs(points: List[Point]) -> List[_Run]:
    runs: List[_Run] = []
    start = points[0]
    current = points[0]

    for i in range(1, len(points) + 1):
        nxt = points[i % len(points)]
        dx, dy = nxt[0] - current[0], nxt[1] - current[1]
        if abs(dx) <= EPS and abs(dy) <= EPS:
            continue
        horizontal = abs(dx) >= abs(dy)
        sign = 1.0 if (dx if horizontal else dy) >= 0 else -1.0
        if len(runs) == 0:
            runs.append(_Run("h+" if horizontal else "v+", start, nxt))
        else:
            prev = runs[-1]
            prev_horizontal = prev.kind.startswith("h")
            prev_sign = 1.0 if prev.kind.endswith("+") else -1.0
            if prev_horizontal == horizontal and prev_sign == sign:
                runs[-1] = _Run(prev.kind, prev.start, nxt)
            else:
                runs.append(_Run(("h+" if horizontal else "v+") if sign >= 0 else ("h-" if horizontal else "v-"), current, nxt))
        current = nxt
    return runs


def _classify_runs(runs: List[_Run], width: float, height: float) -> List[_Run]:
    result: List[_Run] = []
    tol = max(EPS, min(width, height) * 1e-6)
    for run in runs:
        if run.kind.startswith("h"):
            y = (run.start[1] + run.end[1]) / 2.0
            if abs(y - height) <= tol:
                kind = "top"
            elif abs(y) <= tol:
                kind = "bottom"
            else:
                kind = "inner_h"
        else:
            x = (run.start[0] + run.end[0]) / 2.0
            if abs(x - width) <= tol:
                kind = "right"
            elif abs(x) <= tol:
                kind = "left"
            else:
                kind = "inner_v"
        result.append(_Run(kind, run.start, run.end))
    return result


def _find_run(runs: List[_Run], kind: str, lo: float, hi: float) -> _Run | None:
    for run in runs:
        if run.kind != kind:
            continue
        if kind in ("top", "bottom", "inner_h"):
            a, b = run.x_span
        else:
            a, b = run.y_span
        if abs(a - lo) <= 1e-4 and abs(b - hi) <= 1e-4:
            return run
    return None


def _detect_notches(points: List[Point]) -> List[Notch]:
    width = max(p[0] for p in points) - min(p[0] for p in points)
    height = max(p[1] for p in points) - min(p[1] for p in points)
    runs = _classify_runs(_build_runs(points), width, height)
    notches: List[Notch] = []

    for edge, outer_kind, inner_kind, depth_axis in (
        ("top", "top", "inner_h", "y"),
        ("bottom", "bottom", "inner_h", "y"),
        ("left", "left", "inner_v", "x"),
        ("right", "right", "inner_v", "x"),
    ):
        outer = [run for run in runs if run.kind == outer_kind]
        if depth_axis == "y":
            outer.sort(key=lambda run: run.x_span[0])
        else:
            outer.sort(key=lambda run: run.y_span[0])

        for prev, nxt in zip(outer[:-1], outer[1:]):
            if depth_axis == "y":
                lo, hi = prev.x_span[1], nxt.x_span[0]
                inner = _find_run(runs, inner_kind, lo, hi)
                if inner is None or hi <= lo:
                    continue
                depth_y = (inner.start[1] + inner.end[1]) / 2.0
                depth = (height - depth_y) if edge == "top" else depth_y
                start, notch_w = lo, hi - lo
            else:
                lo, hi = prev.y_span[1], nxt.y_span[0]
                inner = _find_run(runs, inner_kind, lo, hi)
                if inner is None or hi <= lo:
                    continue
                depth_x = (inner.start[0] + inner.end[0]) / 2.0
                depth = depth_x if edge == "left" else width - depth_x
                start, notch_w = lo, hi - lo
            notches.append(
                Notch(
                    edge=edge,
                    start=round(start, 3),
                    width=round(notch_w, 3),
                    depth=round(depth, 3),
                )
            )
    return notches


def spec_from_dxf(
    path: str | Path,
    shrink: float = 5.0,
    frame_t: float = 5.0,
    vertical: BarSpec | None = None,
    horizontal: BarSpec | None = None,
) -> Spec:
    """从 DXF 外轮廓生成排条参数；缩尺和扁钢参数由调用方给定。"""

    specs = specs_from_dxf(path, shrink=shrink, frame_t=frame_t, vertical=vertical, horizontal=horizontal)
    if not specs:
        raise ValueError("DXF 中没有找到闭合外轮廓")
    return specs[0]


def specs_from_dxf(
    path: str | Path,
    shrink: float = 5.0,
    frame_t: float = 5.0,
    vertical: BarSpec | None = None,
    horizontal: BarSpec | None = None,
) -> List[Spec]:
    """从 DXF 中识别全部闭合外轮廓，每个轮廓生成一份排条参数。"""

    path = Path(path)
    entities = _read_entities(_read_groups(path))
    specs: List[Spec] = []
    for points in _extract_outlines(entities):
        points = _normalize(points)
        notches = _detect_notches(points)
        width = max(p[0] for p in points) - min(p[0] for p in points)
        height = max(p[1] for p in points) - min(p[1] for p in points)
        if notches:
            notches.sort(key=lambda n: (n.edge, n.start))
        specs.append(
            Spec(
                opening_w=round(width, 3),
                opening_h=round(height, 3),
                shrink=shrink,
                frame_t=frame_t,
                vertical=vertical or BarSpec(5.0, 36.85),
                horizontal=horizontal or BarSpec(5.0, 36.85),
                notches=notches,
            )
        )
    return specs
