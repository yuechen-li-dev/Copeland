"""Encode actual Vulkan proof captures as an animated preview; no generated poses."""

import argparse
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    frames = []
    for frame in range(0, 240, 4):
        path = args.directory / f"frame-{frame:03d}.png"
        with Image.open(path) as image:
            image = image.convert("RGB")
            image.thumbnail((640, 480), Image.Resampling.LANCZOS)
            frames.append(image.quantize(colors=128))
    output = args.directory / "character-animation.gif"
    frames[0].save(output, save_all=True, append_images=frames[1:], loop=0,
                   duration=70, disposal=2)
    print(output.resolve())


if __name__ == "__main__":
    main()
