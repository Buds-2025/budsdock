from __future__ import annotations

import binascii
import math
import struct
import zlib
from pathlib import Path

SIZE = 256
OUTPUT = Path(__file__).resolve().parents[1] / "src" / "BudsDock" / "Assets" / "BudsDock.ico"

pixels = bytearray(SIZE * SIZE * 4)


def blend_pixel(x: int, y: int, color: tuple[int, int, int, int]) -> None:
    if not (0 <= x < SIZE and 0 <= y < SIZE):
        return
    index = (y * SIZE + x) * 4
    pixels[index:index + 4] = bytes(color)


def circle(cx: float, cy: float, radius: float, color: tuple[int, int, int, int]) -> None:
    left = max(0, int(cx - radius - 1))
    right = min(SIZE, int(cx + radius + 2))
    top = max(0, int(cy - radius - 1))
    bottom = min(SIZE, int(cy + radius + 2))
    radius_sq = radius * radius
    for y in range(top, bottom):
        for x in range(left, right):
            if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= radius_sq:
                blend_pixel(x, y, color)


circle(128, 128, 120, (31, 35, 48, 255))
circle(82, 88, 24, (126, 153, 255, 255))
circle(128, 64, 24, (126, 153, 255, 255))
circle(174, 88, 24, (126, 153, 255, 255))
circle(128, 156, 62, (234, 238, 255, 255))
circle(128, 158, 36, (31, 35, 48, 255))


def png_chunk(kind: bytes, data: bytes) -> bytes:
    payload = kind + data
    return struct.pack(">I", len(data)) + payload + struct.pack(">I", binascii.crc32(payload) & 0xFFFFFFFF)

scanlines = bytearray()
for y in range(SIZE):
    scanlines.append(0)
    start = y * SIZE * 4
    scanlines.extend(pixels[start:start + SIZE * 4])

png = bytearray(b"\x89PNG\r\n\x1a\n")
png.extend(png_chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)))
png.extend(png_chunk(b"IDAT", zlib.compress(bytes(scanlines), 9)))
png.extend(png_chunk(b"IEND", b""))

header = struct.pack("<HHH", 0, 1, 1)
entry = struct.pack("<BBBBHHII", 0, 0, 0, 0, 1, 32, len(png), 22)
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_bytes(header + entry + png)
print(OUTPUT)
