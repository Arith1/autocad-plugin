# 钢格板自动排条

用 Python 标准库实现的钢格板排条程序。输入洞口尺寸、缩尺、边框厚度、纵横扁钢厚度/中心距和边缘矩形缺口，输出下料清单、首尾孔距和 SVG 排条图。

## 运行

在项目目录执行：

```powershell
python -m gridplate.paotiao --selftest
python -m gridplate.paotiao examples/plate2.json --csv
```

也可以直接喂 DXF 外轮廓：

```powershell
python -m gridplate.paotiao drawing.dxf --csv --dxf
```

DXF 用法：程序读取文件里所有闭合 `LWPOLYLINE/POLYLINE` 外轮廓，逐条自动识别洞口宽高和上/下/左/右边缘矩形缺口。缩尺、边框厚度和扁钢参数通过命令行指定，默认缩尺 `5`、边框厚 `5`、扁钢厚 `5`、中心距 `36.85`。

一个 DXF 里有多条闭合外轮廓时，会把每条轮廓按源文件中的出现顺序当作一块板分别排条，文件名用 `1_`、`2_` 编号区分，例如 `排条图_1_990x490.svg`。合并清单和合并 DXF 的顺序也保持一致。

```powershell
python -m gridplate.paotiao drawing.dxf --csv `
  --shrink 5 --frame-t 5 `
  --vertical-thickness 5 --vertical-pitch 36.85 `
  --horizontal-thickness 5 --horizontal-pitch 36.85
```

加 `--dxf` 后会额外生成 CAD 可打开的 DXF 排条图，图层按 `外轮廓`、`净空`、`纵条`、`横条`、`临界提示`、`标注`、`尺寸`、`首尾孔距`、`表格`、`分组边框` 分开。多图形合并 DXF 会用 `分组边框` 把每块板件和它的表格圈起来，便于区分。

也可以从标准输入读 JSON：

```powershell
Get-Content examples/plate2.json -Raw | python -m gridplate.paotiao -
```

多个 DXF 可以一次排完，直接列出文件，或传一个目录：

```powershell
python -m gridplate.paotiao a.dxf b.dxf --csv
python -m gridplate.paotiao C:\path\to\dxf-folder --csv
```

批量时输出文件名会带来源 DXF 名和图形编号，例如 `排条图_Drawing3_1_2600x2110.svg`，避免不同文件之间覆盖。

DXF 排条完成后会在 DXF 同目录生成一个以 DXF 文件名命名的文件夹，里面放该文件的排条图、清单和 DXF；同时程序目录（`--out-dir`，默认 `cache/output`）也留存一份，文件名带 DXF 名，方便汇总。

输出文件名会带上缩尺后的板件尺寸，例如板件 `2060 x 1220` 时生成：

- `cache/output/排条图_2060x1220.svg`
- `cache/output/排条清单_2060x1220.csv`

SVG、CSV 和 DXF 会把下料分成三张清单：

- `边框下料（不冲孔）`：只列规格、尺寸、方向和数量。
- `纵向扁钢下料 / 首尾孔距`：列纵向扁钢的规格、尺寸、方向、数量、首孔距、尾孔距和孔数。
- `横向扁钢下料 / 首尾孔距`：列横向扁钢的规格、尺寸、方向、数量、首孔距、尾孔距和孔数。

三张表都使用列头：`序号、规格、尺寸、方向、数量、首孔距、尾孔距、孔数`；边框行的三个冲孔字段留空。纵向和横向分开后，也方便以后支持“扁钢+扭绞方钢”只给扁钢方向冲孔。

## 参数含义

JSON 的坐标单位都是 mm：

```json
{
  "opening_w": 1980,
  "opening_h": 1330,
  "shrink": 5,
  "frame_t": 5,
  "vertical": {"thickness": 5, "pitch": 36.85},
  "horizontal": {"thickness": 5, "pitch": 36.85},
  "notches": []
}
```

- `opening_w/opening_h`：原始洞口尺寸。
- `shrink`：缩尺量。每边缩 `shrink`，所以板件宽高为 `opening_w - 2 * shrink` 和 `opening_h - 2 * shrink`。不需要缩尺时设为 `0`。
- `frame_t`：边框厚度，可变。
- `vertical.thickness/pitch`：纵向扁钢厚度和中心距，可变。
- `horizontal.thickness/pitch`：横向扁钢厚度和中心距，可变。
- `notches`：边缘矩形缺口列表。

缺口的 `start` 按**原始洞口坐标**填写：`top/bottom` 的 `start` 从原洞口左边算，`left/right` 的 `start` 从原洞口顶边算。程序会先按缩尺平移缺口，再计算边框内边。如果图纸标的是缩尺后的板件坐标，填写前要先加回缩尺量。

SVG 按缩尺后的板件坐标标注；JSON 里的缺口 `start` 仍按原始洞口坐标填写。

缺口方向：

```json
{"edge": "top", "start": 585, "width": 130, "depth": 250}
```

`edge` 可为 `top`、`bottom`、`left`、`right`。`width` 是沿板边方向的长度，`depth` 是向板内凹进的深度。

## 排条规则

1. 每边按 `shrink` 缩尺，得到整块板件。
2. 板件四周保留 `frame_t` 边框，剩下的是真正钢格板净空。
3. 纵向和横向扁钢分别按各自中心距布置；程序采用工厂习惯：主网格数量取 `floor(净空尺寸 / 中心距)`，首根位置由居中余量推导。
4. 上/下缺口把进入缺口宽度、或扁钢边到缺口边框不足 `frame_t` 的纵条截短。左/右缺口与纵条相交处则断开。
5. 左/右缺口把进入缺口高度、或扁钢边到缺口边框不足 `frame_t` 的横条截短。上/下缺口与横条相交处则断开。
6. 孔位不追求两端均孔：先布置整个垂直方向的扁钢网格，再用“纵条段 ∩ 横条段”确定每个孔。首孔和尾孔由实际第一/最后一个交点反推。

SVG 会用橙色描边标出“扁钢边距缺口边框不足 `frame_t`”的临界截断条，并在右侧参数区说明数量和处理方式。

`--selftest` 使用的示例校验了已知板件：净空、53 根纵向、35 排横向、8 根缺口短条、7 排切段、102 段下料和首尾孔距。

## 当前限制

当前版本支持外轮廓边缘上的矩形凹口，并会拒绝跨板角或贯穿板件的缺口。DXF 入口目前解析闭合多段线外轮廓（`LWPOLYLINE/POLYLINE`），每条闭合轮廓按一块板处理；依赖文件的开口尺寸和边缘缺口用外轮廓线表达。只由零散 `LINE` 组成的图、一块板内部再挖洞的嵌套轮廓、凸出轮廓、凹凸混合轮廓、圆弧和异形孔还没实现。
