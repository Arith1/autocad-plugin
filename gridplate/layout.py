# -*- coding: utf-8 -*-
"""按中心距布置扁钢，并根据缺口边框截断和分段。"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Dict, Iterable, List, Sequence, Tuple

from .geometry import EPS, NotchGeo, PlateGeometry, Rect, build_geometry
from .model import Spec


@dataclass(frozen=True)
class Segment:
    a: float
    b: float

    @property
    def length(self) -> float:
        return self.b - self.a

    def contains(self, value: float) -> bool:
        return self.a <= value + EPS and value <= self.b + EPS


@dataclass
class Bar:
    orientation: str  # vertical / horizontal
    index: int
    center: float
    thickness: float
    segments: List[Segment]
    full: bool = False
    hole_groups: List[List[float]] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)


@dataclass(frozen=True)
class LayoutResult:
    geometry: PlateGeometry
    vertical_bars: List[Bar]
    horizontal_bars: List[Bar]

    @property
    def spec(self) -> Spec:
        return self.geometry.spec

    @property
    def segment_count(self) -> int:
        return sum(len(bar.segments) for bar in self.vertical_bars + self.horizontal_bars)


def _place_centers(lo: float, hi: float, thickness: float, pitch: float) -> List[float]:
    """按工厂习惯取 count = floor(span / pitch)，但极小净空至少放 1 根。"""

    span = hi - lo
    if span <= EPS or thickness > span + EPS:
        return []
    count = max(1, int(math.floor(span / pitch + EPS)))
    margin = (span - (count - 1) * pitch - thickness) / 2.0
    if margin < -EPS:
        count = max(1, count - 1)
        margin = (span - (count - 1) * pitch - thickness) / 2.0
    first = lo + margin + thickness / 2.0
    return [first + i * pitch for i in range(count)]


def _band(center: float, thickness: float) -> Tuple[float, float]:
    return center - thickness / 2.0, center + thickness / 2.0


def _band_inside(band: Tuple[float, float], lo: float, hi: float) -> bool:
    return band[0] >= lo - EPS and band[1] <= hi + EPS


def _band_overlaps(band: Tuple[float, float], lo: float, hi: float) -> bool:
    return band[0] < hi - EPS and lo < band[1] - EPS


def _near_interval(band: Tuple[float, float], lo: float, hi: float, clearance: float) -> bool:
    """扁钢与区间重叠，或扁钢边缘到区间的距离小于允许间隙。"""

    if _band_overlaps(band, lo, hi):
        return True
    if band[1] < lo:
        return lo - band[1] < clearance - EPS
    return band[0] - hi < clearance - EPS


def _truncate_start(segments: List[Segment], limit: float) -> List[Segment]:
    return [Segment(max(s.a, limit), s.b) for s in segments if s.b > limit + EPS]


def _truncate_end(segments: List[Segment], limit: float) -> List[Segment]:
    return [Segment(s.a, min(s.b, limit)) for s in segments if s.a < limit - EPS]


def _subtract_interval(segments: List[Segment], lo: float, hi: float) -> List[Segment]:
    result: List[Segment] = []
    for seg in segments:
        if seg.b <= lo + EPS or seg.a >= hi - EPS:
            result.append(seg)
            continue
        if seg.a < lo - EPS:
            result.append(Segment(seg.a, lo))
        if hi < seg.b - EPS:
            result.append(Segment(hi, seg.b))
    return result


def _vertical_segments(geo: PlateGeometry, center: float, thickness: float) -> List[Segment]:
    net = geo.net
    segments = [Segment(net.y0, net.y1)]
    band = _band(center, thickness)

    # 上/下缺口：扁钢边到缺口边框不足一个边框厚度的纵条被截短。
    for notch in geo.notches:
        if notch.source.edge == "top" and _near_interval(
            band, notch.frame.x0, notch.frame.x1, geo.spec.frame_t
        ):
            segments = _truncate_start(segments, notch.frame.y1)
        elif notch.source.edge == "bottom" and _near_interval(
            band, notch.frame.x0, notch.frame.x1, geo.spec.frame_t
        ):
            segments = _truncate_end(segments, notch.frame.y0)

    # 左/右缺口：纵条与缺口边框相交处断开。
    for notch in geo.notches:
        if notch.source.edge == "left" and _near_interval(
            band, notch.frame.x0, notch.frame.x1, geo.spec.frame_t
        ):
            segments = _subtract_interval(segments, notch.frame.y0, notch.frame.y1)
        elif notch.source.edge == "right" and _near_interval(
            band, notch.frame.x0, notch.frame.x1, geo.spec.frame_t
        ):
            segments = _subtract_interval(segments, notch.frame.y0, notch.frame.y1)

    return segments


def _horizontal_segments(geo: PlateGeometry, center: float, thickness: float) -> List[Segment]:
    net = geo.net
    segments = [Segment(net.x0, net.x1)]
    band = _band(center, thickness)

    # 左/右缺口：扁钢边到缺口边框不足一个边框厚度的横条被截短。
    for notch in geo.notches:
        if notch.source.edge == "left" and _near_interval(
            band, notch.frame.y0, notch.frame.y1, geo.spec.frame_t
        ):
            segments = _truncate_start(segments, notch.frame.x1)
        elif notch.source.edge == "right" and _near_interval(
            band, notch.frame.y0, notch.frame.y1, geo.spec.frame_t
        ):
            segments = _truncate_end(segments, notch.frame.x0)

    # 上/下缺口：横条与缺口边框相交处断开。
    for notch in geo.notches:
        if notch.source.edge == "top" and _near_interval(
            band, notch.frame.y0, notch.frame.y1, geo.spec.frame_t
        ):
            segments = _subtract_interval(segments, notch.frame.x0, notch.frame.x1)
        elif notch.source.edge == "bottom" and _near_interval(
            band, notch.frame.y0, notch.frame.y1, geo.spec.frame_t
        ):
            segments = _subtract_interval(segments, notch.frame.x0, notch.frame.x1)

    return segments


def _covers(segments: Sequence[Segment], value: float) -> bool:
    return any(seg.contains(value) for seg in segments)


def _set_warnings(bar: Bar, geo: PlateGeometry) -> None:
    edge_names = {"top": "上", "bottom": "下", "left": "左", "right": "右"}
    clearance = geo.spec.frame_t
    bar.warnings = []
    for notch in geo.notches:
        if bar.orientation == "vertical":
            near_edge = notch.source.edge in ("top", "bottom") and _near_interval(
                _band(bar.center, bar.thickness), notch.frame.x0, notch.frame.x1, clearance
            )
            crosses = notch.source.edge in ("left", "right") and _near_interval(
                _band(bar.center, bar.thickness), notch.frame.x0, notch.frame.x1, clearance
            )
            action = "截短" if notch.source.edge in ("top", "bottom") else "分段"
        else:
            near_edge = notch.source.edge in ("left", "right") and _near_interval(
                _band(bar.center, bar.thickness), notch.frame.y0, notch.frame.y1, clearance
            )
            crosses = notch.source.edge in ("top", "bottom") and _near_interval(
                _band(bar.center, bar.thickness), notch.frame.y0, notch.frame.y1, clearance
            )
            action = "分段" if notch.source.edge in ("top", "bottom") else "截短"
        if near_edge or crosses:
            text = f"距{edge_names[notch.source.edge]}缺口边框不足{clearance:g}mm，已{action}"
            if text not in bar.warnings:
                bar.warnings.append(text)


def _set_hole_groups(bar: Bar, cross_bars: Sequence[Bar]) -> None:
    bar.hole_groups = []
    for seg in bar.segments:
        holes: List[float] = []
        for cross in cross_bars:
            if seg.contains(cross.center) and _covers(cross.segments, bar.center):
                holes.append(cross.center)
        bar.hole_groups.append(holes)


def layout(spec: Spec) -> LayoutResult:
    geo = build_geometry(spec)
    net = geo.net
    v_centers = _place_centers(net.x0, net.x1, spec.vertical.thickness, spec.vertical.pitch)
    h_centers = _place_centers(net.y0, net.y1, spec.horizontal.thickness, spec.horizontal.pitch)

    vertical_bars: List[Bar] = []
    for i, center in enumerate(v_centers):
        segments = _vertical_segments(geo, center, spec.vertical.thickness)
        is_full = len(segments) == 1 and abs(segments[0].length - net.h) <= EPS
        bar = Bar("vertical", i + 1, center, spec.vertical.thickness, segments, is_full)
        _set_warnings(bar, geo)
        vertical_bars.append(bar)

    horizontal_bars: List[Bar] = []
    for i, center in enumerate(h_centers):
        segments = _horizontal_segments(geo, center, spec.horizontal.thickness)
        is_full = len(segments) == 1 and abs(segments[0].length - net.w) <= EPS
        bar = Bar("horizontal", i + 1, center, spec.horizontal.thickness, segments, is_full)
        _set_warnings(bar, geo)
        horizontal_bars.append(bar)

    for bar in vertical_bars:
        _set_hole_groups(bar, horizontal_bars)
    for bar in horizontal_bars:
        _set_hole_groups(bar, vertical_bars)
    return LayoutResult(geo, vertical_bars, horizontal_bars)
