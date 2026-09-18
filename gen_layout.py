# -*- coding: utf-8 -*-
"""生成 1970x1320 钢格板排条图 (SVG)。1 单位 = 1 mm。"""

from pathlib import Path

PITCH = 36.85
T = 5.0  # 扁钢厚度 / 边框厚度

PLATE_W, PLATE_H = 1970.0, 1320.0
NOTCH = [(580.0, 710.0), (1200.0, 1330.0)]  # 缺口 x 区间(缩尺后)
NOTCH_D = 250.0

NET_X0, NET_X1 = 5.0, 1965.0
NET_Y0, NET_Y1 = 5.0, 1315.0

# 纵向扁钢
v_bars = []
for k in range(1, 54):
    cx = 26.9 + (k - 1) * PITCH
    short = k in (17, 18, 19, 33, 34, 35, 36)
    v_bars.append((k, cx, 255.0 if short else NET_Y0, short))

# 横向扁钢
cut_segs = [(NET_X0, 575.0), (715.0, 1195.0), (1335.0, NET_X1)]
h_rows = []
for r in range(1, 36):
    cy = 33.55 + (r - 1) * PITCH
    h_rows.append((r, cy, cut_segs if r <= 7 else [(NET_X0, NET_X1)]))

# ---- 断言核对 ----
short_bars = [b for b in v_bars if b[3]]
assert len(short_bars) == 7
for k, cx, _, _ in short_bars:
    in_notch = any(x0 <= cx - T / 2 and cx + T / 2 <= x1 for x0, x1 in NOTCH)
    assert in_notch, k
assert abs((v_bars[0][1] - NET_X0) - 21.9) < 1e-9
assert abs((v_bars[52][1] - 26.9) - 52 * PITCH) < 1e-9
assert abs((h_rows[0][1] - NET_Y0) - 28.55) < 1e-9
assert abs((h_rows[34][1] + T / 2) - (NET_Y1 - 26.05)) < 1e-9
row8_bottom = h_rows[7][1] - T / 2
assert row8_bottom > NOTCH_D + T, row8_bottom            # 第8排避开底框
assert h_rows[6][1] - T / 2 < NOTCH_D + T                # 第7排压底框 -> 切段
row_count = sum(len(s) for _, _, s in h_rows)
assert row_count == 28 + 21

def f(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return s if s else "0"

def fmt_pts(pts):
    return " ".join(f"{f(x)},{f(y)}" for x, y in pts)

plate_pts = [
    (0, 0), (580, 0), (580, 250), (710, 250), (710, 0),
    (1200, 0), (1200, 250), (1330, 250), (1330, 0), (1970, 0),
    (1970, 1320), (0, 1320),
]
net_pts = [
    (5, 5), (575, 5), (575, 250), (580, 250), (580, 255), (710, 255),
    (710, 250), (715, 250), (715, 5), (1195, 5), (1195, 250), (1200, 250),
    (1200, 255), (1330, 255), (1330, 250), (1335, 250), (1335, 5), (1965, 5),
    (1965, 1315), (5, 1315),
]

FONT = "Microsoft YaHei, SimHei, sans-serif"
GREEN, GREEN_D = "#a8d5aa", "#2e9e46"
PINK = "#f6b8d0"
svg = []
svg.append('<svg xmlns="http://www.w3.org/2000/svg" width="2950" height="1640" viewBox="0 0 2950 1640">')
svg.append(f'<rect x="0" y="0" width="2950" height="1640" fill="#ffffff"/>')
svg.append(f'<g transform="translate(90,140)">')

# 板与边框
svg.append(f'<polygon points="{fmt_pts(plate_pts)}" fill="#f3dcdc" stroke="#c0392b" stroke-width="3"/>')
svg.append(f'<polygon points="{fmt_pts(net_pts)}" fill="#ffffff"/>')

# 纵向扁钢
for k, cx, y0, short in v_bars:
    fill = GREEN_D if short else GREEN
    stroke = "#1b5e20" if short else "#2e7d32"
    svg.append(f'<rect x="{f(cx-T/2)}" y="{f(y0)}" width="{f(T)}" height="{f(NET_Y1-y0)}" '
               f'fill="{fill}" stroke="{stroke}" stroke-width="0.4"/>')

# 横向扁钢
for r, cy, segs in h_rows:
    for x0, x1 in segs:
        svg.append(f'<rect x="{f(x0)}" y="{f(cy-T/2)}" width="{f(x1-x0)}" height="{f(T)}" '
                   f'fill="{PINK}" stroke="#ad1457" stroke-width="0.4"/>')

# 底框下表面虚线
for x0, x1 in NOTCH:
    svg.append(f'<line x1="{f(x0)}" y1="255" x2="{f(x1)}" y2="255" stroke="#c0392b" '
               f'stroke-width="1.2" stroke-dasharray="8,5"/>')

# 首孔标注
svg.append('<line x1="0" y1="1428" x2="26.9" y2="1428" stroke="#c0392b" stroke-width="1.5"/>')
svg.append('<circle cx="26.9" cy="1428" r="3" fill="#c0392b"/>')
svg.append(f'<text x="34" y="1434" font-family="{FONT}" font-size="18" fill="#c0392b">首孔21.9</text>')
svg.append('<line x1="150" y1="0" x2="150" y2="33.55" stroke="#c0392b" stroke-width="1.5"/>')
svg.append('<circle cx="150" cy="33.55" r="3" fill="#c0392b"/>')
svg.append(f'<text x="160" y="26" font-family="{FONT}" font-size="18" fill="#c0392b">首孔28.55</text>')
svg.append('</g>')

# 顶部分段尺寸
segs_top = [(0, 580, "580"), (580, 710, "130"), (710, 1200, "490"),
            (1200, 1330, "130"), (1330, 1970, "640")]
for x0, x1, label in segs_top:
    a, b = 90 + x0, 90 + x1
    svg.append(f'<line x1="{f(a)}" y1="118" x2="{f(b)}" y2="118" stroke="#555" stroke-width="1"/>')
    svg.append(f'<line x1="{f(a)}" y1="112" x2="{f(a)}" y2="124" stroke="#555" stroke-width="1"/>')
    svg.append(f'<line x1="{f(b)}" y1="112" x2="{f(b)}" y2="124" stroke="#555" stroke-width="1"/>')
    svg.append(f'<text x="{f((a+b)/2)}" y="105" text-anchor="middle" font-family="{FONT}" '
               f'font-size="20" fill="#333">{label}</text>')

# 缺口深标注
for x0, x1 in NOTCH:
    c = 90 + (x0 + x1) / 2
    svg.append(f'<text x="{f(c)}" y="270" text-anchor="middle" font-family="{FONT}" font-size="18" '
               f'fill="#c0392b" transform="rotate(90 {f(c)} 270)">深250</text>')

# 纵向关键编号
for k in (1, 16, 17, 19, 33, 36, 53):
    cx = 90 + 26.9 + (k - 1) * PITCH
    svg.append(f'<line x1="{f(cx)}" y1="1600" x2="{f(cx)}" y2="1582" stroke="#555" stroke-width="1"/>')
    svg.append(f'<text x="{f(cx)}" y="1596" text-anchor="middle" font-family="{FONT}" '
               f'font-size="17" fill="#333">{k}</text>')

# 总尺寸
svg.append('<line x1="90" y1="1540" x2="2060" y2="1540" stroke="#333" stroke-width="1.4"/>')
svg.append('<line x1="90" y1="1532" x2="90" y2="1548" stroke="#333" stroke-width="1.4"/>')
svg.append('<line x1="2060" y1="1532" x2="2060" y2="1548" stroke="#333" stroke-width="1.4"/>')
svg.append(f'<text x="1075" y="1572" text-anchor="middle" font-family="{FONT}" font-size="24" fill="#111">1970</text>')
svg.append('<line x1="48" y1="140" x2="48" y2="1460" stroke="#333" stroke-width="1.4"/>')
svg.append('<line x1="40" y1="140" x2="56" y2="140" stroke="#333" stroke-width="1.4"/>')
svg.append('<line x1="40" y1="1460" x2="56" y2="1460" stroke="#333" stroke-width="1.4"/>')
svg.append(f'<text x="30" y="800" text-anchor="middle" font-family="{FONT}" font-size="24" '
           f'fill="#111" transform="rotate(-90 30 800)">1320</text>')
svg.append(f'<text x="2100" y="265" text-anchor="middle" font-family="{FONT}" font-size="18" '
           f'fill="#c0392b" transform="rotate(90 2100 265)">深250</text>')

# 标题
svg.append(f'<text x="90" y="55" font-family="{FONT}" font-size="30" font-weight="bold" fill="#111">'
           f'钢格板排条图 1970 × 1320（原洞口 1980 × 1330，缩尺 5，长边包短边）</text>')

# 图例
LX, LY = 2130, 150
svg.append(f'<rect x="{LX}" y="{LY}" width="790" height="1030" fill="#fbf9f4" stroke="#bbb" stroke-width="1"/>')
lines = [
    ("下料清单（共 102 段）", None, True),
    ("1310 × 46   纵向满长", GREEN, False),
    ("1060 × 7    纵向缺口短条（17~19、33~36 号）", GREEN_D, False),
    ("1960 × 28   横向整长", PINK, False),
    ("570 × 7     横向左段", PINK, False),
    ("480 × 7     横向中段", PINK, False),
    ("630 × 7     横向右段", PINK, False),
    ("首尾孔距（首 / 尾，孔数）", None, True),
    ("1960: 21.9 / 21.9（53 孔）", None, False),
    ("1310: 28.55 / 28.55（35 孔）", None, False),
    ("1060: 36.5 / 28.55（28 孔，缺口端为首）", None, False),
    ("570: 21.9 / 32.2（15 孔，穿 1~15 号）", None, False),
    ("480: 12.05 / 25.75（13 孔，穿 20~32 号）", None, False),
    ("630: 18.5 / 21.9（17 孔，穿 37~53 号）", None, False),
    ("排条参数", None, True),
    ("缩尺 5 固定｜边框厚 5｜扁钢厚 5", None, False),
    ("中心距 36.85｜净孔 31.85", None, False),
    ("纵向首根中心 x=26.9（边面留量 19.4）", None, False),
    ("横向首排中心 y=33.55（边面留量 26.05）", None, False),
    ("16 号骑缺口边界 → 保持满长", None, False),
    ("顶部 7 排切段（缺口深 250 + 底框 5）", None, False),
    ("红虚线 = 底框下表面 y=255（短条起点）", None, False),
]
y = LY + 42
for text, swatch, bold in lines:
    weight = ' font-weight="bold"' if bold else ""
    svg.append(f'<text x="{LX+24}" y="{y}" font-family="{FONT}" font-size="20"{weight} fill="#222">{text}</text>')
    if swatch:
        svg.append(f'<rect x="{LX+750}" y="{y-15}" width="26" height="18" fill="{swatch}" stroke="#666" stroke-width="0.6"/>')
    y += 44 if bold else 36

svg.append("</svg>")

out_dir = Path("cache/previews")
out_dir.mkdir(parents=True, exist_ok=True)
with open(out_dir / "排条图.svg", "w", encoding="utf-8") as fp:
    fp.write("\n".join(svg))
print(f"SVG 已生成：{out_dir / '排条图.svg'}")
