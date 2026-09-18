from __future__ import annotations

from collections import Counter, defaultdict
from pathlib import Path


PATH = Path(r"C:\Users\Miyna\Desktop\Drawing2.dxf")


def read_groups(path: Path) -> list[tuple[int, str]]:
    lines = path.read_text(encoding="cp936", errors="replace").splitlines()
    return [(int(lines[i].strip()), lines[i + 1]) for i in range(0, len(lines) - 1, 2)]


def read_entities(groups: list[tuple[int, str]]) -> list[dict[str, object]]:
    entities: list[dict[str, object]] = []
    in_entities = False
    saw_section_code = False
    current: dict[str, object] | None = None

    for code, value in groups:
        text = value.strip()
        if (code, text) == (0, "SECTION"):
            saw_section_code = True
            in_entities = False
            current = None
            continue
        if saw_section_code and code == 2:
            in_entities = text == "ENTITIES"
            saw_section_code = False
            continue
        if (code, text) == (0, "ENDSEC"):
            saw_section_code = False
            in_entities = False
            if current is not None:
                entities.append(current)
                current = None
            continue
        if not in_entities:
            continue
        if code == 0:
            if current is not None:
                entities.append(current)
            current = {"type": text, "values": defaultdict(list)}
            continue
        if current is not None:
            values: defaultdict[int, list[str]] = current["values"]  # type: ignore[assignment]
            values[code].append(value)

    return entities


def main() -> None:
    import sys

    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    entities = read_entities(read_groups(PATH))
    counts = Counter(str(entity["type"]) for entity in entities)
    print("entities:", dict(counts))

    for name in ("LINE", "LWPOLYLINE", "POLYLINE", "CIRCLE", "ARC", "TEXT", "MTEXT", "INSERT", "DIMENSION"):
        items = [entity for entity in entities if entity["type"] == name]
        print(name, len(items))
        for entity in items[:10]:
            values: defaultdict[int, list[str]] = entity["values"]  # type: ignore[assignment]
            layer = values.get(8, ["0"])[0]
            if name == "LINE":
                start = (float(values[10][0]), float(values[20][0]))
                end = (float(values[11][0]), float(values[21][0]))
                print("  LINE", start, end, layer)
            elif name == "LWPOLYLINE":
                xs = [float(v) for v in values.get(10, [])]
                ys = [float(v) for v in values.get(20, [])]
                points = list(zip(xs, ys))
                closed = int(values.get(70, ["0"])[0]) & 1
                print("  LWPOLYLINE", len(points), "closed=", bool(closed), layer)
                for x, y in points:
                    print("    ", round(x, 6), round(y, 6))
            elif name in ("TEXT", "MTEXT"):
                text = values.get(1, [""])[0]
                x = float(values.get(10, ["0"])[0])
                y = float(values.get(20, ["0"])[0])
                print(" ", name, text, (round(x, 3), round(y, 3)), layer)
            elif name == "INSERT":
                print("  INSERT", values.get(2, [""])[0], (float(values.get(10, ["0"])[0]), float(values.get(20, ["0"])[0])), layer)


if __name__ == "__main__":
    main()
