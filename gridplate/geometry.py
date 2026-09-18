# -*- coding: utf-8 -*-
"""缩尺、边框和缺口几何。EPS 用于吸收浮点误差。"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Sequence, Tuple

from .model import Notch, Spec, validate_basic


EPS = 1e-9


@dataclass(frozen=True)
class Rect:
    x0: float
    y0: float
    x1: float
    y1: float

    @property
    def w(self) -> float:
        return self.x1 - self.x0

    @property
    def h(self) -> float:
        return self.y1 - self.y0

    def clipped(self, bounds: Rect) -> "Rect":
        return Rect(
            max(self.x0, bounds.x0),
            max(self.y0, bounds.y0),
            min(self.x1, bounds.x1),
            min(self.y1, bounds.y1),
        )

    def intersects(self, other: "Rect") -> bool:
        return (
            self.x0 < other.x1 - EPS
            and other.x0 < self.x1 - EPS
            and self.y0 < other.y1 - EPS
            and other.y0 < self.y1 - EPS
        )


@dataclass(frozen=True)
class NotchGeo:
    source: Notch
    clear: Rect
    frame: Rect


@dataclass(frozen=True)
class PlateGeometry:
    spec: Spec
    plate: Rect
    net: Rect
    notches: List[NotchGeo]


def _as_plate_rect(rect: Rect, w: float, h: float) -> Rect:
    clipped = rect.clipped(Rect(0.0, 0.0, w, h))
    if clipped.w <= EPS or clipped.h <= EPS:
        raise ValueError("缺口不在板件范围内")
    return clipped


def build_geometry(spec: Spec) -> PlateGeometry:
    validate_basic(spec)
    w, h = spec.plate_w, spec.plate_h
    plate = Rect(0.0, 0.0, w, h)
    net = Rect(spec.frame_t, spec.frame_t, w - spec.frame_t, h - spec.frame_t)
    if net.w <= EPS or net.h <= EPS:
        raise ValueError("边框厚度过大，净空不存在")

    s = spec.shrink
    geos: List[NotchGeo] = []
    for notch in spec.notches:
        if notch.edge in ("top", "bottom"):
            # 缩尺按外轮廓整体内偏；凹口两侧也向材料内缩。
            x0 = notch.start - 2 * s
            x1 = notch.start + notch.width
            y0 = 0.0 if notch.edge == "top" else h - notch.depth
            y1 = notch.depth if notch.edge == "top" else h
            y1_frame = notch.depth + spec.frame_t if notch.edge == "top" else h
            y0_frame = 0.0 if notch.edge == "top" else h - notch.depth - spec.frame_t
            clear = _as_plate_rect(Rect(x0, y0, x1, y1), w, h)
            frame = _as_plate_rect(Rect(x0 - spec.frame_t, y0_frame, x1 + spec.frame_t, y1_frame), w, h)
        else:
            y0 = notch.start - 2 * s
            y1 = notch.start + notch.width
            x0 = 0.0 if notch.edge == "left" else w - notch.depth
            x1 = notch.depth if notch.edge == "left" else w
            x1_frame = notch.depth + spec.frame_t if notch.edge == "left" else w
            x0_frame = 0.0 if notch.edge == "left" else w - notch.depth - spec.frame_t
            clear = _as_plate_rect(Rect(x0, y0, x1, y1), w, h)
            frame = _as_plate_rect(Rect(x0_frame, y0 - spec.frame_t, x1_frame, y1 + spec.frame_t), w, h)
        geos.append(NotchGeo(notch, clear, frame))

    _validate_geometry(plate, net, geos)
    return PlateGeometry(spec, plate, net, geos)


def _edge_touch_count(rect: Rect, plate: Rect) -> int:
    return sum(
        (
            abs(rect.x0 - plate.x0) <= EPS,
            abs(rect.x1 - plate.x1) <= EPS,
            abs(rect.y0 - plate.y0) <= EPS,
            abs(rect.y1 - plate.y1) <= EPS,
        )
    )


def _validate_geometry(plate: Rect, net: Rect, notches: Sequence[NotchGeo]) -> None:
    for i, item in enumerate(notches, 1):
        touches = _edge_touch_count(item.frame, plate)
        if touches > 1:
            raise ValueError(f"第 {i} 个缺口跨过板角或贯穿板件，当前版本不支持")
        if not net.intersects(item.frame) and not item.frame.intersects(net):
            raise ValueError(f"第 {i} 个缺口没有进入净空区域")

    for i in range(len(notches)):
        for j in range(i + 1, len(notches)):
            if notches[i].frame.intersects(notches[j].frame):
                raise ValueError(f"第 {i + 1} 和第 {j + 1} 个缺口的边框区域重叠")


def _dedupe(points: List[Tuple[float, float]]) -> List[Tuple[float, float]]:
    result: List[Tuple[float, float]] = []
    for point in points:
        if not result or abs(point[0] - result[-1][0]) > EPS or abs(point[1] - result[-1][1]) > EPS:
            result.append(point)
    if len(result) > 1 and abs(result[0][0] - result[-1][0]) <= EPS and abs(result[0][1] - result[-1][1]) <= EPS:
        result.pop()
    return result


def _sorted_by_start(notches: Sequence[NotchGeo], edge: str, reverse: bool = False) -> List[NotchGeo]:
    items = [n for n in notches if n.source.edge == edge]
    if edge in ("top", "bottom"):
        return sorted(items, key=lambda n: n.clear.x0, reverse=reverse)
    return sorted(items, key=lambda n: n.clear.y0, reverse=reverse)


def plate_outline(geo: PlateGeometry) -> List[Tuple[float, float]]:
    """按顺时针方向返回板件外轮廓。"""

    p = geo.plate
    pts: List[Tuple[float, float]] = [(p.x0, p.y0)]
    for n in _sorted_by_start(geo.notches, "top"):
        pts.extend(
            [
                (n.clear.x0, p.y0),
                (n.clear.x0, n.clear.y1),
                (n.clear.x1, n.clear.y1),
                (n.clear.x1, p.y0),
            ]
        )
    pts.append((p.x1, p.y0))

    for n in _sorted_by_start(geo.notches, "right"):
        pts.extend(
            [
                (p.x1, n.clear.y0),
                (n.clear.x0, n.clear.y0),
                (n.clear.x0, n.clear.y1),
                (p.x1, n.clear.y1),
            ]
        )
    pts.append((p.x1, p.y1))

    for n in _sorted_by_start(geo.notches, "bottom", reverse=True):
        pts.extend(
            [
                (n.clear.x1, p.y1),
                (n.clear.x1, n.clear.y0),
                (n.clear.x0, n.clear.y0),
                (n.clear.x0, p.y1),
            ]
        )
    pts.append((p.x0, p.y1))

    for n in _sorted_by_start(geo.notches, "left", reverse=True):
        pts.extend(
            [
                (p.x0, n.clear.y1),
                (n.clear.x1, n.clear.y1),
                (n.clear.x1, n.clear.y0),
                (p.x0, n.clear.y0),
            ]
        )
    return _dedupe(pts)


def net_outline(geo: PlateGeometry) -> List[Tuple[float, float]]:
    """返回真正钢格板净空的内轮廓，缺口处已扣除边框。"""

    n = geo.net
    pts: List[Tuple[float, float]] = [(n.x0, n.y0)]
    for item in _sorted_by_start(geo.notches, "top"):
        pts.extend(
            [
                (item.frame.x0, n.y0),
                (item.frame.x0, item.frame.y1),
                (item.frame.x1, item.frame.y1),
                (item.frame.x1, n.y0),
            ]
        )
    pts.append((n.x1, n.y0))

    for item in _sorted_by_start(geo.notches, "right"):
        pts.extend(
            [
                (n.x1, item.frame.y0),
                (item.frame.x0, item.frame.y0),
                (item.frame.x0, item.frame.y1),
                (n.x1, item.frame.y1),
            ]
        )
    pts.append((n.x1, n.y1))

    for item in _sorted_by_start(geo.notches, "bottom", reverse=True):
        pts.extend(
            [
                (item.frame.x1, n.y1),
                (item.frame.x1, item.frame.y0),
                (item.frame.x0, item.frame.y0),
                (item.frame.x0, n.y1),
            ]
        )
    pts.append((n.x0, n.y1))

    for item in _sorted_by_start(geo.notches, "left", reverse=True):
        pts.extend(
            [
                (n.x0, item.frame.y1),
                (item.frame.x1, item.frame.y1),
                (item.frame.x1, item.frame.y0),
                (n.x0, item.frame.y0),
            ]
        )
    return _dedupe(pts)
