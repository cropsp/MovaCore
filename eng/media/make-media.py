"""Renders the README illustrations in docs/media from the pages next to this script.

Needs Node.js with Playwright (Chromium) and Python with Pillow and NumPy. Run from the repository root:

    python eng/media/make-media.py

convert.html and mouse.html draw deterministic frames on a canvas and list them as PNG data URLs (capture.js saves
them); the frames are laid on GitHub's page colours and saved as light and dark GIFs. social.html is the 1280x640
social preview (shot.js takes a screenshot); it shows the logo made transparent by eng/generate-icons.py.
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
HERE = ROOT / "eng" / "media"
MEDIA = ROOT / "docs" / "media"
BACKGROUNDS = {"light": (255, 255, 255), "dark": (13, 17, 23)}  # GitHub's page colours


def node(script: str, *args: str) -> None:
    subprocess.run(["node", str(HERE / script), *args], check=True, cwd=HERE)


def save_gif(frames: list[Image.Image], path: Path, colors: int) -> None:
    sample = Image.new("RGB", (frames[0].width, frames[0].height * 4))
    for k, i in enumerate(range(0, len(frames), max(1, len(frames) // 4))[:4]):
        sample.paste(frames[i], (0, frames[0].height * k))
    palette = sample.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    quantized = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    quantized[0].save(path, save_all=True, append_images=quantized[1:], duration=50, loop=0, optimize=True, disposal=1)
    print(f"{path.relative_to(ROOT)}: {path.stat().st_size // 1024} KB")


def frames_of(page: str, query: str) -> list[Image.Image]:
    with tempfile.TemporaryDirectory() as folder:
        node("capture.js", page, query, folder)
        return [Image.open(Path(folder) / name).convert("RGBA").copy() for name in sorted(os.listdir(folder))]


def main() -> None:
    for theme, colour in BACKGROUNDS.items():
        # The conversion demo paints its own page colour
        save_gif([f.convert("RGB") for f in frames_of("convert.html", f"theme={theme}")],
                 MEDIA / f"convert-{theme}.gif", colors=64)

    mouse = frames_of("mouse.html", "")
    for theme, colour in BACKGROUNDS.items():
        flat = []
        for frame in mouse:
            base = Image.new("RGBA", frame.size, colour + (255,))
            base.alpha_composite(frame)
            flat.append(base.convert("RGB"))
        save_gif(flat, MEDIA / f"mouse-in-grass-{theme}.gif", colors=128)

    spec = importlib.util.spec_from_file_location("icons", ROOT / "eng" / "generate-icons.py")
    icons = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(icons)
    art = icons.crop_square(icons.make_transparent(Image.open(ROOT / "Resources" / "mouse_icon.png").convert("RGB")))
    art.resize((440, 440), Image.Resampling.LANCZOS).save(HERE / "mouse-440.png")
    try:
        node("shot.js", "social.html", str(MEDIA / "social-preview.png"))
    finally:
        (HERE / "mouse-440.png").unlink(missing_ok=True)
    print("docs/media/social-preview.png")


if __name__ == "__main__":
    sys.exit(main())
