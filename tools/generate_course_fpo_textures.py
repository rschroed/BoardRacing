#!/usr/bin/env python3
"""Generate the FPO course-surface detail tiles (issue #161).

These are NOT final art. They exist to validate composition, tiling density,
filtering, and device performance for the approved Quarry direction (#162)
before the project decides whether final textures are generated, commissioned,
purchased, photographed, or drawn.

They are neutral grain maps centred on mid grey, not coloured surfaces. The
CourseSurface shader modulates the authored vertex colour by (detail * 2), so
a flat 0.5 tile is a no-op and the theme's colours remain the thing that
decides what a surface looks like. Keeping the tiles neutral is also what lets
one texture be swapped without touching the palette.

Seamlessness is structural rather than fixed up afterwards: every blur is a
wrap-around convolution (np.roll), so opposite edges are already continuous.

    python3 tools/generate_course_fpo_textures.py

Writes into Assets/BoardRacing/Presentation/CourseSurface/Textures/.
"""

import pathlib

import numpy as np
from PIL import Image

SIZE = 256
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


def grain(seed, fine, coarse, coarse_radius):
    """Fine speckle plus low-frequency mottling, both zero-centred."""
    rng = np.random.default_rng(seed)
    noise = rng.random((SIZE, SIZE)).astype(np.float32)
    speckle = normalised(noise - wrap_blur(noise, 1))
    field = speckle * fine
    if coarse > 0:
        blobs = rng.random((SIZE, SIZE)).astype(np.float32)
        field = field + normalised(wrap_blur(blobs, coarse_radius) - 0.5) * coarse
    return field


def write(name, field):
    grey = np.clip(0.5 + field, 0.0, 1.0) * 255.0
    rgb = np.repeat(grey.astype(np.uint8)[:, :, None], 3, axis=2)
    path = OUT / name
    Image.fromarray(rgb).save(path, "PNG")
    span = grey.max() - grey.min()
    print(f"{name}: {SIZE}x{SIZE}, level span {span:.0f}/255")


TILES = {
    # Limestone dust: fine and quiet. The ground is most of the board, so its
    # grain has to survive being looked at without becoming noise.
    "FPO_CourseGround_Limestone.png": dict(seed=11, fine=0.055, coarse=0.030,
                                           coarse_radius=8),
    # Dry slate asphalt: coarser aggregate, read at 88 px so roughly two
    # repeats span the 64 px road.
    "FPO_CourseRoad_Slate.png": dict(seed=12, fine=0.075, coarse=0.026,
                                     coarse_radius=6),
    # Gravel runoff: the coarsest of the three, so the shoulder still reads as
    # a different material where it meets the road edge.
    "FPO_CourseShoulder_Gravel.png": dict(seed=31, fine=0.090, coarse=0.045,
                                          coarse_radius=5),
}

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, spec in TILES.items():
        write(name, grain(**spec))
