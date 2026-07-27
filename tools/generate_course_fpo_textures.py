#!/usr/bin/env python3
"""Generate the FPO course-surface detail tiles (issue #161).

These are NOT final art. They exist to validate composition, tiling density,
filtering, and device performance for the approved Quarry direction (#162)
before the project decides whether final textures are generated, commissioned,
purchased, photographed, or drawn.

The tiles carry their own colour. An earlier revision kept them neutral and let
the shader modulate a theme colour by them, which coupled absolute grain to base
brightness: the road, being dark, received a tenth of the ground's variation
from a tile authored with more amplitude, and read as flat. Authoring colour
here means a dark asphalt gets the contrast it actually needs, and means hue can
vary at all — greyscale times a colour can only ever vary value, and real
aggregate varies in hue.

Amplitudes below are therefore absolute 0-255 levels, not fractions of some base
colour, and are per channel so flecks can lean warm or cool independently.

Seamlessness is structural rather than fixed up afterwards: every blur is a
wrap-around convolution (np.roll), so opposite edges are already continuous.

    python3 tools/generate_course_fpo_textures.py

Writes into Assets/BoardRacing/Presentation/CourseSurface/Textures/.
"""

import pathlib

import numpy as np
from PIL import Image

# Source resolution tracks the tile's world size on purpose. A 256 px tile
# shown across 130 reference pixels is a 2x downscale, so the GPU picks a
# lower mip and averages the fine grain away -- measured, that cost the
# ground 13 authored levels down to 5 on screen and erased the hue variation
# entirely. At 1:1 the authored amplitude is what reaches the panel.
SIZE = 128
OUT = (pathlib.Path(__file__).resolve().parent.parent
       / "Assets/BoardRacing/Presentation/CourseSurface/Textures")


def wrap_blur(field, radius, passes=3):
    """Box blur that wraps at the edges, so the result stays tileable."""
    out = field.astype(np.float32)
    for _ in range(passes):
        acc = np.zeros_like(out)
        for dy in (-radius, 0, radius):
            for dx in (-radius, 0, radius):
                acc += np.roll(np.roll(out, dy, 0), dx, 1)
        out = acc / 9.0
    return out


def normalised(field):
    peak = np.abs(field).max()
    return field / peak if peak > 1e-6 else field


def speckle(rng):
    noise = rng.random((SIZE, SIZE)).astype(np.float32)
    return normalised(noise - wrap_blur(noise, 1))


def mottle(rng, radius):
    blobs = rng.random((SIZE, SIZE)).astype(np.float32)
    return normalised(wrap_blur(blobs, radius) - 0.5)


def hexrgb(value):
    value = value.lstrip("#")
    return np.array([int(value[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


def tile(base, fine, coarse, coarse_radius, warm, seed):
    """base colour + achromatic grain + an independent warm/cool hue drift."""
    rng = np.random.default_rng(seed)
    value = speckle(rng) * fine + mottle(rng, coarse_radius) * coarse
    # A second, lower-frequency field pushes red up and blue down (or the
    # reverse) so the surface varies in hue and not only in value.
    hue = mottle(rng, coarse_radius * 2) * warm

    rgb = np.repeat(base[None, None, :], SIZE, 0).repeat(SIZE, 1).copy()
    rgb += value[:, :, None]
    rgb[:, :, 0] += hue
    rgb[:, :, 2] -= hue
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))


def write(name, image):
    path = OUT / name
    image.save(path, "PNG")
    a = np.asarray(image).astype(float)
    lum = a.mean(axis=2)
    span = np.percentile(lum, 95) - np.percentile(lum, 5)
    hue = (a.max(axis=2) - a.min(axis=2)).std()
    print(f"{name:38s} mean {lum.mean():5.1f}  p5-p95 {span:4.1f} levels  "
          f"hue-var {hue:4.2f}")


TILES = {
    # Limestone dust. The ground is most of the board, so its grain has to
    # survive being looked at without becoming noise.
    "FPO_CourseGround_Limestone.png": dict(
        base=hexrgb("#D2CCBD"), fine=9.0, coarse=6.0, coarse_radius=5, warm=4.0, seed=11),
    # Dry slate asphalt, authored dark with its own contrast rather than
    # relying on a tint to supply it.
    "FPO_CourseRoad_Slate.png": dict(
        base=hexrgb("#4A4C4B"), fine=11.0, coarse=7.0, coarse_radius=4, warm=3.0, seed=12),
    # Gravel runoff, the coarsest of the three, so the shoulder still reads as
    # a different material where it meets the road edge.
    "FPO_CourseShoulder_Gravel.png": dict(
        base=hexrgb("#BFB5A1"), fine=14.0, coarse=10.0, coarse_radius=3, warm=6.0, seed=31),
}

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, spec in TILES.items():
        write(name, tile(**spec))
