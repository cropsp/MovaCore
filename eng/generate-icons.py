#!/usr/bin/env python3
"""Regenerate the app icon assets from Resources/mouse_icon.png.

The source art is 1024x1024 RGB on a white background with wide margins, which shows up in the tray as a white
square with a tiny mouse. This script makes the background transparent (flood fill from the image border, so the
cream belly enclosed by the outline is kept), softens the anti-aliased outline edge into partial alpha, and crops
the margins.

Outputs (committed; the app embeds them):
  Resources/movacore.ico               exe and tray icon, 16..256 px
  Resources/logo.png                   256 px logo for the settings window and README
  Resources/movacore-recording.ico     tray icon while dictating: a red dot, 16..64 px
  Resources/movacore-transcribing.ico  tray icon while the speech is being recognized: an amber dot, 16..64 px

Usage: pip install pillow numpy && python3 eng/generate-icons.py
"""
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Resources" / "mouse_icon.png"
ICO = ROOT / "Resources" / "movacore.ico"
LOGO = ROOT / "Resources" / "logo.png"
RECORDING = ROOT / "Resources" / "movacore-recording.ico"
TRANSCRIBING = ROOT / "Resources" / "movacore-transcribing.ico"

BACKGROUND_MIN = 235  # background pixels have every channel at least this bright
EDGE_BAND = 3         # outline pixels this close to the background get alpha from their darkness
PADDING = 0.04        # transparent margin around the cropped art, as a fraction of its size
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BADGE_SIZES = [16, 20, 24, 32, 40, 48, 64]  # tray icons only
BADGE_SCALE = 0.55   # badge diameter as a fraction of the icon
SUPERSAMPLE = 8      # the badge is drawn larger and scaled down, for smooth edges at 16 px
RED = (220, 38, 38, 255)
AMBER = (245, 158, 11, 255)


def background_mask(rgb: np.ndarray) -> np.ndarray:
    """Light pixels connected to the image border."""
    h, w, _ = rgb.shape
    light = rgb.min(axis=2) >= BACKGROUND_MIN
    mask = np.zeros((h, w), dtype=bool)
    border = [(y, x) for x in range(w) for y in (0, h - 1)] + [(y, x) for y in range(h) for x in (0, w - 1)]
    queue = deque()
    for y, x in border:
        if light[y, x] and not mask[y, x]:
            mask[y, x] = True
            queue.append((y, x))
    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and light[ny, nx] and not mask[ny, nx]:
                mask[ny, nx] = True
                queue.append((ny, nx))
    return mask


def dilate(mask: np.ndarray, steps: int) -> np.ndarray:
    result = mask.copy()
    for _ in range(steps):
        grown = result.copy()
        grown[1:, :] |= result[:-1, :]
        grown[:-1, :] |= result[1:, :]
        grown[:, 1:] |= result[:, :-1]
        grown[:, :-1] |= result[:, 1:]
        result = grown
    return result


def make_transparent(image: Image.Image) -> Image.Image:
    rgb = np.asarray(image.convert("RGB")).astype(np.float64)
    background = background_mask(rgb.astype(np.uint8))
    band = dilate(background, EDGE_BAND) & ~background

    luminance = rgb.mean(axis=2)
    outline = rgb[~background & (luminance <= np.percentile(luminance[~background], 5))].mean(axis=0)
    outline_luminance = outline.mean()

    alpha = np.full(luminance.shape, 255.0)
    alpha[background] = 0
    band_alpha = np.clip((255 - luminance[band]) / (255 - outline_luminance), 0, 1) * 255
    alpha[band] = band_alpha

    rgba = np.dstack([rgb, alpha])
    rgba[band, :3] = outline  # anti-aliased edge pixels are outline colour blended with white
    return Image.fromarray(rgba.round().astype(np.uint8), "RGBA")


def crop_square(image: Image.Image) -> Image.Image:
    left, top, right, bottom = image.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    side = max(right - left, bottom - top)
    side += 2 * round(side * PADDING)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(image.crop((left, top, right, bottom)), ((side - (right - left)) // 2, (side - (bottom - top)) // 2))
    return square


def with_badge(art: Image.Image, size: int, color: tuple) -> Image.Image:
    """The icon at one size with a dot in the bottom-right corner, ringed in white so it reads on any taskbar."""
    big = size * SUPERSAMPLE
    icon = art.resize((big, big), Image.Resampling.LANCZOS)
    diameter = round(big * BADGE_SCALE)
    ring = max(SUPERSAMPLE, round(diameter * 0.12))
    draw = ImageDraw.Draw(icon)
    draw.ellipse((big - diameter, big - diameter, big - 1, big - 1), fill=(255, 255, 255, 255))
    draw.ellipse((big - diameter + ring, big - diameter + ring, big - 1 - ring, big - 1 - ring), fill=color)
    return icon.resize((size, size), Image.Resampling.LANCZOS)


def save_badged(art: Image.Image, path: Path, color: tuple) -> None:
    images = [with_badge(art, size, color) for size in BADGE_SIZES]
    images[-1].save(path, sizes=[(s, s) for s in BADGE_SIZES], append_images=images[:-1])


def main() -> None:
    art = crop_square(make_transparent(Image.open(SOURCE)))
    art.save(ICO, sizes=[(s, s) for s in ICO_SIZES])
    art.resize((256, 256), Image.Resampling.LANCZOS).save(LOGO, optimize=True)
    save_badged(art, RECORDING, RED)
    save_badged(art, TRANSCRIBING, AMBER)
    for path in (ICO, LOGO, RECORDING, TRANSCRIBING):
        print(f"{path.relative_to(ROOT)}: {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
