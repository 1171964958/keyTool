#!/usr/bin/env python3

import struct
from pathlib import Path

root = Path(__file__).resolve().parent.parent
iconset = root / "Support" / "AppIcon.iconset"
output = root / "windows" / "KeyTool.Windows" / "Assets" / "KeyTool.ico"

entries = [
    (16, iconset / "icon_16x16.png"),
    (32, iconset / "icon_32x32.png"),
    (64, iconset / "icon_128x128@2x.png"),
    (128, iconset / "icon_128x128.png"),
    (256, iconset / "icon_256x256.png"),
]

images = [(size, path.read_bytes()) for size, path in entries]
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + (16 * len(images))
directory = bytearray()

for size, data in images:
    directory.extend(
        struct.pack(
            "<BBBBHHII",
            0 if size == 256 else size,
            0 if size == 256 else size,
            0,
            0,
            1,
            32,
            len(data),
            offset,
        )
    )
    offset += len(data)

output.parent.mkdir(parents=True, exist_ok=True)
output.write_bytes(header + directory + b"".join(data for _, data in images))
print(output)
