#!/usr/bin/env python3
"""Pack embedded-theme/ into an XOR-obfuscated binary for the Windows resource."""

import io
import os
import sys
import zipfile

KEY = bytes([
    0xB3, 0x57, 0x9E, 0x24, 0xC8, 0x6A, 0x11, 0xFD,
    0x45, 0x8B, 0xD2, 0x37, 0xE9, 0x02, 0xAC, 0x71
])

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EMBEDDED_ROOT = os.path.join(ROOT, "embedded-theme")
OUTPUT = os.path.join(ROOT, "es-app", "src", "embedded_theme.bin")


def resolve_theme_dir() -> str:
    if not os.path.isdir(EMBEDDED_ROOT):
        return EMBEDDED_ROOT

    direct_theme = os.path.join(EMBEDDED_ROOT, "theme.xml")
    if os.path.isfile(direct_theme):
        return EMBEDDED_ROOT

    for entry in sorted(os.listdir(EMBEDDED_ROOT)):
        candidate = os.path.join(EMBEDDED_ROOT, entry)
        if os.path.isdir(candidate) and os.path.isfile(os.path.join(candidate, "theme.xml")):
            return candidate

    return EMBEDDED_ROOT


def xor_bytes(data: bytes) -> bytes:
    return bytes(b ^ KEY[i % len(KEY)] for i, b in enumerate(data))


def pack_theme(theme_dir: str) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for base, _, files in os.walk(theme_dir):
            for name in files:
                full_path = os.path.join(base, name)
                rel_path = os.path.relpath(full_path, theme_dir).replace("\\", "/")
                archive.write(full_path, rel_path)
    return buffer.getvalue()


def main() -> int:
    theme_dir = resolve_theme_dir()
    if not os.path.isdir(theme_dir):
        print(f"Theme folder not found: {theme_dir}", file=sys.stderr)
        return 1

    if not os.path.isfile(os.path.join(theme_dir, "theme.xml")):
        print(f"theme.xml not found in: {theme_dir}", file=sys.stderr)
        return 1

    print(f"Packing theme from {theme_dir} ...")
    zip_data = pack_theme(theme_dir)
    obfuscated = xor_bytes(zip_data)
    source_count = sum(len(files) for _, _, files in os.walk(theme_dir))

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "wb") as handle:
        handle.write(obfuscated)

    print(f"Packed {len(zip_data)} bytes zip -> {len(obfuscated)} bytes encrypted ({source_count} files)")
    print(f"Output: {OUTPUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())