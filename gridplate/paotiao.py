# -*- coding: utf-8 -*-
"""钢格板自动排条命令行入口。"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import List, Tuple

from .geometry import build_geometry
from .layout import layout
from .model import Notch, BarSpec, Spec, spec_from_file
from .report import cut_list, export_csv, export_workbook, fmt, frame_table, hole_table, print_report
from .svgrender import save_svg
from .dxfexport import save_dxf, save_dxf_many


def _selftest() -> None:
    spec = Spec(
        opening_w=1980.0,
        opening_h=1330.0,
        shrink=5.0,
        frame_t=5.0,
        vertical=BarSpec(5.0, 36.85),
        horizontal=BarSpec(5.0, 36.85),
        notches=[
            Notch("top", 585.0, 130.0, 250.0),
            Notch("top", 1205.0, 130.0, 250.0),
        ],
    )
    result = layout(spec)
    geo = result.geometry
    assert abs(geo.plate.w - 1970.0) < 1e-9
    assert abs(geo.plate.h - 1320.0) < 1e-9
    assert (geo.net.x0, geo.net.y0, geo.net.x1, geo.net.y1) == (5.0, 5.0, 1965.0, 1315.0)

    assert len(result.vertical_bars) == 53
    assert abs(result.vertical_bars[0].center - 26.9) < 1e-9
    short_ids = [bar.index for bar in result.vertical_bars if not bar.full]
    assert short_ids == [16, 17, 18, 19, 20, 33, 34, 35, 36]
    for bar in result.vertical_bars:
        if not bar.full:
            assert len(bar.segments) == 1
            assert abs(bar.segments[0].a - 255.0) < 1e-9
            assert abs(bar.segments[0].b - 1315.0) < 1e-9
            assert abs(bar.segments[0].length - 1060.0) < 1e-9

    assert len(result.horizontal_bars) == 35
    assert abs(result.horizontal_bars[0].center - 33.55) < 1e-9
    cut_rows = [bar for bar in result.horizontal_bars if not bar.full]
    assert len(cut_rows) == 7
    for bar in cut_rows:
        assert [(s.a, s.b) for s in bar.segments] == [(5.0, 570.0), (720.0, 1190.0), (1340.0, 1965.0)]

    holes = hole_table(result)
    expected = {
        ("纵向", 1310.0, 28.55, 28.55, 35, 44),
        ("纵向", 1060.0, 36.50, 28.55, 28, 9),
        ("横向", 1960.0, 21.90, 21.90, 53, 28),
        ("横向", 565.0, 21.90, 27.20, 15, 7),
        ("横向", 470.0, 43.90, 20.75, 12, 7),
        ("横向", 625.0, 13.50, 21.90, 17, 7),
    }
    actual = {
        (item.direction, round(item.length, 2), round(item.first_hole, 2), round(item.last_hole, 2), item.holes, item.count)
        for item in holes
    }
    assert actual == expected, actual

    cuts = cut_list(result)
    cut_expected = {
        ("纵向", "满长", 1310.0): 44,
        ("纵向", "短条", 1060.0): 9,
        ("横向", "整长", 1960.0): 28,
        ("横向", "分段", 565.0): 7,
        ("横向", "分段", 470.0): 7,
        ("横向", "分段", 625.0): 7,
    }
    actual_cuts = {(item.direction, item.type, round(item.length, 2)): item.count for item in cuts}
    assert actual_cuts == cut_expected, actual_cuts
    assert sum(item.count for item in cuts) == 102

    assert all(bar.warnings for bar in result.vertical_bars if not bar.full)
    assert result.horizontal_bars[6].warnings

    frame_rows = frame_table(result)
    frame_expected = {
        ("横向", 1970.0, 1),
        ("纵向", 1310.0, 2),
        ("横向", 575.0, 1),
        ("横向", 480.0, 1),
        ("横向", 635.0, 1),
        ("纵向", 250.0, 4),
        ("横向", 140.0, 2),
    }
    actual_frames = {
        (item.direction, round(item.length, 2), item.count) for item in frame_rows
    }
    assert actual_frames == frame_expected, actual_frames
    assert all(item.first_hole is None and item.last_hole is None and item.holes == 0 for item in frame_rows)

    no_shrink = Spec(
        opening_w=1000.0,
        opening_h=500.0,
        shrink=0.0,
        frame_t=5.0,
        vertical=BarSpec(5.0, 36.85),
        horizontal=BarSpec(5.0, 36.85),
        notches=[],
    )
    assert no_shrink.plate_w == 1000.0
    assert no_shrink.plate_h == 500.0
    assert build_geometry(no_shrink).net.w == 990.0


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="钢格板自动排条：生成合并清单和 SVG 排条图")
    parser.add_argument(
        "specs",
        nargs="*",
        help="一个或多个 JSON/DXF 文件路径，或包含 DXF 的目录；用 - 从标准输入读取 JSON",
    )
    parser.add_argument("--out-dir", default="cache/output", help="程序留存目录，默认 cache/output")
    parser.add_argument("--shrink", type=float, default=5.0, help="缩尺量，默认 5")
    parser.add_argument("--frame-t", type=float, default=5.0, help="边框厚度，默认 5")
    parser.add_argument("--vertical-thickness", type=float, default=5.0, help="纵向扁钢厚度，默认 5")
    parser.add_argument("--vertical-pitch", type=float, default=36.85, help="纵向中心距，默认 36.85")
    parser.add_argument("--horizontal-thickness", type=float, default=5.0, help="横向扁钢厚度，默认 5")
    parser.add_argument("--horizontal-pitch", type=float, default=36.85, help="横向中心距，默认 36.85")
    parser.add_argument("--no-svg", action="store_true", help="只输出文本报告，不生成 SVG")
    parser.add_argument("--csv", action="store_true", help="导出排条清单；多图形源 DXF 会合并成一个 xlsx 工作簿")
    parser.add_argument("--dxf", action="store_true", help="额外导出 DXF CAD 文件")
    parser.add_argument("--selftest", action="store_true", help="运行内置校验示例")
    parser.add_argument("--version", action="version", version="gridplate 0.1")
    return parser


def _emit_spec(
    spec: Spec,
    out_dir: str | Path,
    no_svg: bool,
    csv: bool,
    dxf: bool = False,
    stem: str = "",
    extra_out_dir: str | Path | None = None,
    extra_stem: str | None = None,
) -> None:
    result = layout(spec)
    print(print_report(result))
    out_dir = Path(out_dir)
    dimension = f"{fmt(result.spec.plate_w)}x{fmt(result.spec.plate_h)}"
    destinations: List[Tuple[str, Path, str]] = [("程序目录", out_dir, stem)]
    if extra_out_dir is not None:
        extra = Path(extra_out_dir)
        if extra.resolve() != out_dir.resolve():
            destinations.append(("DXF 同目录", extra, extra_stem if extra_stem is not None else stem))

    for label, dest, use_stem in destinations:
        suffix = f"{use_stem}{dimension}" if use_stem else dimension
        if not no_svg:
            svg_path = save_svg(result, dest / f"排条图_{suffix}.svg")
            print(f"{label} SVG：{svg_path}")
        if csv:
            csv_path = export_csv(result, dest, filename=f"排条清单_{suffix}.csv")
            print(f"{label} CSV：{csv_path}")
        if dxf:
            dxf_path = save_dxf(result, dest / f"排条图_{suffix}.dxf")
            print(f"{label} DXF：{dxf_path}")
    return result


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = _build_parser()
    args = parser.parse_args(argv)

    if args.selftest:
        _selftest()
        print("内置校验通过：板 2 全部尺寸、数量和首尾孔距匹配。")
        return 0

    if not args.specs:
        parser.error("请提供参数 JSON/DXF、目录，或使用 --selftest")

    try:
        inputs: list[str] = []
        for item in args.specs:
            path = Path(item)
            if path.is_dir():
                inputs.extend(str(p) for p in sorted(path.glob("*.dxf")))
            else:
                inputs.append(item)
        if not inputs:
            raise ValueError("目录里没有找到 DXF 文件")

        out_dir = Path(args.out_dir)
        multi_file = len(inputs) > 1
        stdin_used = False
        for item in inputs:
            if item == "-":
                if stdin_used:
                    raise ValueError("标准输入 JSON 只能出现一次")
                stdin_used = True
                data = json.load(sys.stdin)
                from .model import spec_from_dict

                spec = spec_from_dict(data)
                stem = "stdin_" if multi_file else ""
                _emit_spec(spec, out_dir, args.no_svg, args.csv, stem=stem, dxf=args.dxf)
                continue

            spec_path = Path(item)
            source_stem = spec_path.stem
            if spec_path.suffix.lower() == ".dxf":
                from .dxfimport import specs_from_dxf

                specs = specs_from_dxf(
                    spec_path,
                    shrink=args.shrink,
                    frame_t=args.frame_t,
                    vertical=BarSpec(args.vertical_thickness, args.vertical_pitch),
                    horizontal=BarSpec(args.horizontal_thickness, args.horizontal_pitch),
                )
                print(f"[{spec_path.name}] DXF 解析：识别到 {len(specs)} 个图形")
                same_dir = spec_path.parent / source_stem
                results = []
                for no, spec in enumerate(specs, 1):
                    program_stem = f"{source_stem}_{no}_" if len(specs) > 1 else f"{source_stem}_"
                    local_stem = f"{no}_" if len(specs) > 1 else ""
                    results.append(
                        _emit_spec(
                            spec,
                            out_dir,
                            args.no_svg,
                            args.csv and len(specs) <= 1,
                            stem=program_stem,
                            extra_out_dir=same_dir,
                            extra_stem=local_stem,
                        )
                    )
                if args.csv and len(specs) > 1:
                    workbook_path = export_workbook(
                        results,
                        out_dir,
                        filename=f"排条清单_{source_stem}.xlsx",
                    )
                    print(f"程序目录 合并清单：{workbook_path}")
                if args.dxf:
                    dxf_path = save_dxf_many(results, out_dir / f"排条图_{source_stem}.dxf")
                    print(f"程序目录 合并 DXF：{dxf_path}")
            else:
                spec = spec_from_file(spec_path)
                stem = f"{source_stem}_" if multi_file else ""
                _emit_spec(spec, out_dir, args.no_svg, args.csv, stem=stem, dxf=args.dxf)
        return 0
    except Exception as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
