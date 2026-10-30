"""Render our original integer-coordinate pixel assets. Standard library only; no game art inputs."""
from pathlib import Path
import argparse
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1] / "assets/game/Textures/Items"


def render(size, rectangles):
    pixels = bytearray(size * size * 4)
    for x0, y0, x1, y1, color in rectangles:
        for y in range(y0, y1):
            for x in range(x0, x1):
                offset = (y * size + x) * 4
                pixels[offset:offset + 4] = bytes(color)
    raw = b"".join(b"\0" + pixels[y * size * 4:(y + 1) * size * 4] for y in range(size))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def assets():
    dark = (21, 30, 42, 255)
    metal = (75, 94, 112, 255)
    cyan = (90, 230, 246, 255)
    gold = (249, 183, 62, 255)
    result = {}
    for mask in range(16):
        # Faint complete cell remains clickable, including unconnected ends.
        rectangles = [(0, 0, 16, 16, (21, 30, 42, 8)), (5, 5, 11, 11, metal)]
        for bit, rect in enumerate([(6, 0, 10, 8), (8, 6, 16, 10), (6, 8, 10, 16), (0, 6, 8, 10)]):
            if mask & (1 << bit):
                rectangles.append((*rect, cyan))
        rectangles.append((6, 6, 10, 10, cyan))
        result[f"pipeline-{mask}.png"] = render(16, rectangles)
    result["pipeline.png"] = result["pipeline-15.png"]  # Directory/spawner preview only.
    core = [(0, 0, 48, 48, dark), (1, 1, 47, 47, metal), (3, 3, 45, 45, dark)]
    for y in (8, 24, 40):
        for x in (8, 24, 40):
            core.extend([(x - 5, y - 5, x + 5, y + 5, metal), (x - 2, y - 2, x + 2, y + 2, cyan)])
    core.extend([(20, 20, 28, 28, gold), (22, 22, 26, 26, cyan)])
    result["core.png"] = render(48, core)
    bus = [(0, 0, 16, 16, dark), (0, 5, 8, 11, metal), (0, 7, 12, 9, gold)]
    for x in range(8, 15):
        half = (14 - x) // 2 + 1
        bus.append((x, 8 - half, x + 1, 8 + half, gold))
    result["item-storage-bus.png"] = render(16, bus)
    terminal = [(0, 0, 16, 16, dark), (1, 1, 15, 14, metal), (3, 3, 13, 11, dark),
                (4, 4, 11, 5, cyan), (4, 7, 10, 8, cyan), (5, 14, 11, 16, metal)]
    result["terminal.png"] = render(16, terminal)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for name, content in assets().items():
        path = ROOT / name
        if args.check:
            if not path.is_file() or path.read_bytes() != content:
                raise SystemExit(f"Asset differs from authored geometry: {name}")
        else:
            path.write_bytes(content)
    print("Verified 20 authored PNGs." if args.check else "Rendered 20 authored PNGs.")
