#!/usr/bin/env python3
"""Generate cyberpunk splash background for TurboRama theme."""

import math
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT = os.path.join(
    ROOT,
    "embedded-theme",
    "TURBORAMA",
    "_theme_options",
    "colorsets",
    "turborama",
    "background.jpg",
)

W, H = 1920, 1080


def lerp(a, b, t):
    return int(a + (b - a) * t)


def blend(top, bottom, t):
    return tuple(lerp(top[i], bottom[i], t) for i in range(3))


def draw_vertical_gradient(img):
    top = (8, 0, 24)
    mid = (18, 0, 48)
    bottom = (6, 0, 18)
    pixels = img.load()
    for y in range(H):
        t = y / (H - 1)
        if t < 0.55:
            color = blend(top, mid, t / 0.55)
        else:
            color = blend(mid, bottom, (t - 0.55) / 0.45)
        for x in range(W):
            pixels[x, y] = color


def draw_horizon_glow(draw):
    cx, cy = W // 2, int(H * 0.58)
    for radius in range(420, 0, -2):
        t = 1 - radius / 420
        alpha = int(90 * (t ** 2))
        color = (255, 0, 180, alpha) if radius % 4 == 0 else (0, 240, 255, alpha // 2)
        draw.ellipse(
            (cx - radius, cy - radius // 3, cx + radius, cy + radius // 3),
            fill=None,
            outline=(color[0], color[1], color[2]),
            width=1,
        )


def draw_perspective_grid(draw):
    horizon = int(H * 0.58)
    vanish_x = W // 2

    for i in range(40):
        t = i / 39
        y = horizon + int((H - horizon) * (t ** 1.6))
        shade = int(20 + 180 * (1 - t))
        cyan = (0, shade, min(255, shade + 80))
        draw.line((0, y, W, y), fill=cyan, width=1)

    for i in range(-24, 25):
        x_bottom = vanish_x + i * 90
        fade = max(40, 220 - abs(i) * 7)
        magenta = (fade, 0, min(255, fade + 60))
        draw.line((vanish_x, horizon, x_bottom, H), fill=magenta, width=2 if abs(i) % 3 == 0 else 1)


def draw_scanlines(img):
    pixels = img.load()
    for y in range(0, H, 3):
        for x in range(W):
            r, g, b = pixels[x, y]
            pixels[x, y] = (max(0, r - 10), max(0, g - 10), max(0, b - 8))


def draw_side_glow(draw):
    for x in range(0, 180):
        t = 1 - x / 180
        c = int(80 * t)
        draw.line((x, 0, x, H), fill=(0, c, c + 40))
    for x in range(W - 180, W):
        t = (x - (W - 180)) / 180
        c = int(80 * t)
        draw.line((x, 0, x, H), fill=(c + 40, 0, c + 80))


def draw_particles(draw):
    points = [
        (120, 90), (340, 160), (1560, 120), (1780, 200), (260, 320),
        (1420, 280), (880, 140), (1040, 220), (640, 180), (1280, 150),
    ]
    for x, y in points:
        draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(0, 255, 255))
        draw.ellipse((x - 5, y - 5, x + 5, y + 5), outline=(255, 0, 200))


def main():
    img = Image.new("RGB", (W, H), (8, 0, 24))
    draw_vertical_gradient(img)
    draw = ImageDraw.Draw(img)
    draw_horizon_glow(draw)
    draw_perspective_grid(draw)
    draw_side_glow(draw)
    draw_particles(draw)
    draw_scanlines(img)

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    img.save(OUTPUT, "JPEG", quality=92, optimize=True)
    print(f"Generated: {OUTPUT} ({os.path.getsize(OUTPUT)} bytes)")


if __name__ == "__main__":
    main()