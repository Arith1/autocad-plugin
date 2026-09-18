# -*- coding: utf-8 -*-

from .model import BarSpec, Notch, Spec, spec_from_dict, spec_from_file, spec_to_dict
from .dxfimport import spec_from_dxf, specs_from_dxf
from .geometry import PlateGeometry, build_geometry, net_outline, plate_outline
from .layout import Bar, LayoutResult, Segment, layout
from .dxfexport import render_dxf, render_dxf_many, save_dxf, save_dxf_many

__all__ = [
    "BarSpec",
    "Notch",
    "Spec",
    "spec_from_dict",
    "spec_from_file",
    "spec_to_dict",
    "spec_from_dxf",
    "specs_from_dxf",
    "PlateGeometry",
    "build_geometry",
    "net_outline",
    "plate_outline",
    "Bar",
    "Segment",
    "LayoutResult",
    "layout",
    "render_dxf",
    "render_dxf_many",
    "save_dxf",
    "save_dxf_many",
]
