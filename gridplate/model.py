# -*- coding: utf-8 -*-
"""排条参数模型。坐标和尺寸单位均为 mm。"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, List


@dataclass(frozen=True)
class Notch:
    """外轮廓边缘上的矩形缺口。

    top/bottom 的 start 从原始洞口左侧算；left/right 的 start 从原始洞口顶部算。
    """

    edge: str
    start: float
    width: float
    depth: float


@dataclass(frozen=True)
class BarSpec:
    thickness: float
    pitch: float


@dataclass(frozen=True)
class Spec:
    opening_w: float
    opening_h: float
    shrink: float
    frame_t: float
    vertical: BarSpec
    horizontal: BarSpec
    notches: List[Notch]

    @property
    def plate_w(self) -> float:
        return self.opening_w - 2 * self.shrink

    @property
    def plate_h(self) -> float:
        return self.opening_h - 2 * self.shrink


def _notch_from_dict(data: dict[str, Any]) -> Notch:
    return Notch(
        edge=str(data["edge"]).lower(),
        start=float(data["start"]),
        width=float(data["width"]),
        depth=float(data["depth"]),
    )


def spec_from_dict(data: dict[str, Any]) -> Spec:
    vertical = data.get("vertical", data.get("bar", {}))
    horizontal = data.get("horizontal", data.get("bar", {}))
    return Spec(
        opening_w=float(data["opening_w"]),
        opening_h=float(data["opening_h"]),
        shrink=float(data.get("shrink", 5.0)),
        frame_t=float(data.get("frame_t", 5.0)),
        vertical=BarSpec(
            thickness=float(vertical.get("thickness", 5.0)),
            pitch=float(vertical.get("pitch", 36.85)),
        ),
        horizontal=BarSpec(
            thickness=float(horizontal.get("thickness", 5.0)),
            pitch=float(horizontal.get("pitch", 36.85)),
        ),
        notches=[_notch_from_dict(item) for item in data.get("notches", [])],
    )


def spec_from_file(path: str | Path) -> Spec:
    with open(path, "r", encoding="utf-8-sig") as fp:
        return spec_from_dict(json.load(fp))


def spec_to_dict(spec: Spec) -> dict[str, Any]:
    return asdict(spec)


def validate_basic(spec: Spec) -> None:
    if spec.opening_w <= 0 or spec.opening_h <= 0:
        raise ValueError("洞口尺寸必须大于 0")
    if spec.shrink < 0:
        raise ValueError("缩尺不能为负数；不需要缩尺时请设为 0")
    if spec.plate_w <= 0 or spec.plate_h <= 0:
        raise ValueError("缩尺后的板件尺寸必须大于 0")
    if spec.frame_t <= 0:
        raise ValueError("边框厚度必须大于 0")
    for name, bars in (("纵向", spec.vertical), ("横向", spec.horizontal)):
        if bars.thickness <= 0 or bars.pitch <= 0:
            raise ValueError(f"{name}扁钢厚度和中心距必须大于 0")
        if bars.thickness > bars.pitch + 1e-9:
            raise ValueError(f"{name}扁钢厚度不能大于中心距")

    valid_edges = {"top", "bottom", "left", "right"}
    for i, notch in enumerate(spec.notches, 1):
        if notch.edge not in valid_edges:
            raise ValueError(f"第 {i} 个缺口的 edge 必须是 {sorted(valid_edges)}")
        if notch.width <= 0 or notch.depth <= 0:
            raise ValueError(f"第 {i} 个缺口的宽度和深度必须大于 0")
