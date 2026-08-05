#!/usr/bin/env python3
"""Create a deterministic, XOR-obfuscated Windows theme resource.

The resource identity is the MD5 of the decrypted ZIP. MD5 is used only as a
cache identity, never as a security boundary. A small clear-text header keeps
that identity in the payload itself, so the runtime can reject stale caches
without decrypting the large archive first.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import sys
import tempfile
import zipfile
from pathlib import Path


KEY = bytes([
    0xB3, 0x57, 0x9E, 0x24, 0xC8, 0x6A, 0x11, 0xFD,
    0x45, 0x8B, 0xD2, 0x37, 0xE9, 0x02, 0xAC, 0x71,
])

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_SOURCE = ROOT / "embedded-theme"
DEFAULT_OUTPUT = ROOT / "es-app" / "src" / "embedded_theme.bin"
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)
COPY_CHUNK_SIZE = 4 * 1024 * 1024
HEADER_PREFIX = b"TRTHEME1:"
HEADER_SIZE = len(HEADER_PREFIX) + 32 + 1


def resolve_theme_dir(embedded_root: Path) -> Path:
    if not embedded_root.is_dir():
        return embedded_root
    if (embedded_root / "theme.xml").is_file():
        return embedded_root
    for entry in sorted(embedded_root.iterdir(), key=lambda item: item.name.casefold()):
        if entry.is_dir() and (entry / "theme.xml").is_file():
            return entry
    return embedded_root


def iter_theme_files(theme_dir: Path):
    for base, directories, files in os.walk(theme_dir):
        directories.sort(key=str.casefold)
        files.sort(key=str.casefold)
        base_path = Path(base)
        for name in files:
            path = base_path / name
            relative = path.relative_to(theme_dir).as_posix()
            yield path, relative


def create_deterministic_zip(theme_dir: Path, destination: Path) -> int:
    source_count = 0
    with zipfile.ZipFile(
        destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6
    ) as archive:
        for source, relative in iter_theme_files(theme_dir):
            info = zipfile.ZipInfo(relative, date_time=ZIP_TIMESTAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            with source.open("rb") as input_stream, archive.open(info, "w") as output_stream:
                shutil.copyfileobj(input_stream, output_stream, COPY_CHUNK_SIZE)
            source_count += 1
    return source_count


def xor_archive(source_zip: Path, destination: Path) -> tuple[str, int]:
    digest = hashlib.md5()  # Cache identity only; see module docstring.
    total = 0
    key_length = len(KEY)
    with source_zip.open("rb") as input_stream, destination.open("w+b") as output_stream:
        # Reserve the fixed-size header; fill it after the streaming digest is known.
        output_stream.write(b"\0" * HEADER_SIZE)
        while True:
            chunk = input_stream.read(COPY_CHUNK_SIZE)
            if not chunk:
                break
            digest.update(chunk)
            obfuscated = bytes(value ^ KEY[(total + index) % key_length] for index, value in enumerate(chunk))
            output_stream.write(obfuscated)
            total += len(chunk)
        identity = digest.hexdigest()
        header = HEADER_PREFIX + identity.encode("ascii") + b"\n"
        if len(header) != HEADER_SIZE:
            raise RuntimeError("internal theme header size mismatch")
        output_stream.seek(0)
        output_stream.write(header)
        output_stream.flush()
        os.fsync(output_stream.fileno())
    return identity, total


def pack_theme(theme_dir: Path, output: Path) -> tuple[str, int, int]:
    output.parent.mkdir(parents=True, exist_ok=True)
    output_temp = output.with_name(output.name + ".tmp")
    output_temp.unlink(missing_ok=True)

    zip_fd, zip_name = tempfile.mkstemp(prefix="turborama-theme-", suffix=".zip", dir=output.parent)
    os.close(zip_fd)
    zip_temp = Path(zip_name)
    try:
        source_count = create_deterministic_zip(theme_dir, zip_temp)
        identity, size = xor_archive(zip_temp, output_temp)
        os.replace(output_temp, output)
        return identity, size, source_count
    finally:
        output_temp.unlink(missing_ok=True)
        zip_temp.unlink(missing_ok=True)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    theme_dir = resolve_theme_dir(args.source.resolve())
    output = args.output.resolve()

    if not theme_dir.is_dir():
        print(f"Theme folder not found: {theme_dir}", file=sys.stderr)
        return 1
    if not (theme_dir / "theme.xml").is_file():
        print(f"theme.xml not found in: {theme_dir}", file=sys.stderr)
        return 1

    print(f"Packing deterministic theme from {theme_dir} ...")
    identity, size, source_count = pack_theme(theme_dir, output)
    print(f"Packed {size} bytes ({source_count} files); payload identity: {identity}")
    print(f"Payload: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
