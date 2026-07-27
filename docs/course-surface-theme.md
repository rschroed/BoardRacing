# Building a course surface theme

How the course gets its look, and what to touch to change it. The assets
themselves — what they are, where they came from, and their replacement status —
are recorded in [`Assets/BoardRacing/Presentation/PROVENANCE.md`](../Assets/BoardRacing/Presentation/PROVENANCE.md).

Established by [#161](https://github.com/rschroed/BoardRacing/issues/161),
implementing the direction approved in
[#162](https://github.com/rschroed/BoardRacing/issues/162).

## The model

The whole surface is **one mesh with one material**, drawn in paint order. That
is load-bearing: append order is the layering rule, which is what keeps the
figure-eight crossings reading as over/under. Splitting into per-material
submeshes would trade that for renderer sorting, so it is not done.

Every vertex instead carries its own blend, in UV0:

| channel | meaning |
| --- | --- |
| `x` | road weight |
| `y` | shoulder weight |
| `z` | detail strength |

Ground is the **complement** — a vertex claiming neither road nor shoulder
samples ground — so three samplers cover four surfaces.

The fragment resolves:

```
sample_s = tex_s * tint_s          // per surface, skipped if that slot is empty
detail   = ground*(1-r-s) + road*r + shoulder*s
rgb      = lerp(vertexColor, detail, strength)
```

Three consequences worth holding onto:

- **`strength = 0` means flat vertex colour.** That is how markings — stripes,
  start/finish, pit boxes, crossing shadow and parapets — stay crisp over a
  textured road, and it is also how the pre-texture treatment stays reachable as
  a deterministic comparison baseline.
- **Tiles carry colour; tint is a grade on top**, defaulting to white.
- **An empty slot falls back to flat vertex colour**, not to white. A missing
  sampler reads white, which would blow the surface out, so the shader is told
  per surface whether a tile exists.

## Mapping is world-space

The surface camera pins world XY to `RaceLayout`'s 1920×1080 reference pixels.
A fragment's own world position divided by a tile size *in those same pixels* is
therefore already a tiling UV. Tile sizes are quoted in reference pixels for
exactly this reason: `128` reads directly as "repeats every 128 px of board",
and the 64 px road width is the natural yardstick.

This is why there are no seams to resolve at closed-loop joins, pit entry and
merge, or the self-crossings — adjacent geometry samples one continuous field
rather than each carrying its own unwrapped coordinates.

It also means the mesh carries **no per-vertex tiling UVs at all**, so there is
nothing for Unity's mipmap streaming to key off. Mips are built into the
textures and streaming is left off.

### Repeat wrap is required, not preferred

World-space mapping produces UVs far outside 0–1. With clamp wrap the surface
would show one stretched copy of each tile. `Wrap Mode: Repeat` is pinned in
each `.png.meta` and must stay.

## Two traps

Both of these produced a surface that looked plausible and was measurably wrong.
Neither is visible by inspection.

### Do not modulate a flat colour by a neutral tile

The first revision kept tiles neutral and multiplied a theme colour by them.
Multiplication couples *absolute* variation to base brightness, so a dark
surface cannot receive much grain however hard its tile is authored:

| surface | base luminance | grain delivered |
| --- | --- | --- |
| ground | 206 | 10.3 levels |
| road | 75 | **1.0 level** |

One level in 255 is invisible. The road tile carried *more* authored amplitude
than the ground tile and still lost by 10×. Colour lives in the art now, where a
dark asphalt can be authored with the contrast it actually needs — and where hue
can vary at all, since greyscale times a colour can only ever vary value.

### Source resolution must track tile size

A 256 px tile displayed across 128 reference pixels is a 2× downscale, so the
GPU selects a lower mip and averages the authored grain away. This stacked
underneath the first trap and hid it: after fixing the combine, the road *still*
rendered 2 levels against 15 authored.

The rule: **source resolution ≈ tile size in reference pixels.** The committed
set is 128 px sources at 128 px tiles, which is 1:1.

Consequence for tuning — pushing tile size far from the source resolution
degrades in both directions. Much larger upscales and softens; much smaller
mips the grain away again. If a tile size settles somewhere far from 128,
regenerate the source at that resolution rather than living with the loss.

## What lives where

| Thing | Where | Notes |
| --- | --- | --- |
| Colours, textures, tile sizes, tints, strength | `Presentation/Resources/CourseSurfaceTheme.asset` | The committed treatment |
| Shader | `Presentation/CourseSurface/Shaders/CourseSurface.shader` | |
| Material | `Presentation/CourseSurface/Materials/CourseSurface.mat` | Exists so the shader is asset-referenced — a shader reached only via `Shader.Find` is **stripped from a player build** |
| Tiles | `Presentation/CourseSurface/Textures/` | `FPO_` prefix means not final art |
| Generator | `tools/generate_course_fpo_textures.py` | Reproduces every FPO tile |
| Runtime value | `RaceSurfaceStyle` | What the renderer and Visual Lab pass around |

`RaceSurfaceStyle.Default` is deliberately **textureless**. The theme asset is
what opts a build into detail, so the flat treatment stays available as a
fallback and as the gallery comparison baseline.

## Changing things

### Replacing a tile

Drop a new PNG in `Textures/`, point the theme slot at it. Nothing in mesh
generation, the shader, or gameplay changes. Match the existing import settings
— they are pinned in the `.meta` rather than left to editor defaults.

Since tiles carry colour, a swap now changes colour as well as grain. That is
the intended trade, but it means a swap is a visual decision, not just a
texture-detail one.

### Tuning on the Board

The Visual Lab's Course Surface panel exposes shoulder opacity, solid width,
feather, and all three tile sizes, stepping 16 reference pixels. `LOG` writes
every exposed value plus each texture's asset name.

The panel tunes a **runtime copy**; it never writes back to the committed asset.
`RESET` returns to whatever the build committed — not to the flat default.

Anything not on the panel — colours, tints, textures themselves — needs a theme
edit and a rebuild.

### Adding a detail slot

Currently pit lane and corners share the road tile ungraded, so every
road-family surface reads identically. That is a deliberate baseline, not a
limitation. Sharing a tile keeps grain flowing across the boundary, which reads
as *wear*; a separate tile breaks the pattern and reads as a *different
material*. Choose on that basis, not on authoring convenience.

To add one, five places need touching:

1. `SurfaceDetail` — a weight for the new surface
2. The UV packing — UV0 has one spare channel; beyond that needs UV1
3. The shader — sampler, tile, tint, enable, and a term in the blend
4. `RaceSurfaceStyle` and `CourseSurfaceTheme` — the fields
5. `RaceSurfaceRenderer.ApplyStyle` — binding them

Sampler count is not the constraint; mobile handles far more than this. The real
cost is keeping tiles coherent with each other.

## What this theme does not own

Player accent colours, HUD colours, and condition-state semantics are **not**
course-owned and are deliberately absent. The composition sketch is:

```text
BoardRacingTheme (future)
├── CourseSurfaceTheme  (exists)
├── CarTheme            (later)
├── HudTheme            (later)
└── EffectsTheme        (later)
```

The downstream assets are intentionally not created yet. The sketch exists so
the course phase does not quietly claim things that belong to cars or UI.

## Verifying a change

```bash
# Flat baseline — must stay byte-identical unless the flat treatment changed
Unity -batchmode -projectPath . -executeMethod CourseGalleryCaptures.Run

# The committed theme
Unity -batchmode -projectPath . -executeMethod CourseGalleryCaptures.RunThemeReview
```

`docs/captures/courses` is the flat baseline; `docs/captures/course-theme` is the
themed artifact. A theme change should move the second and leave the first alone.

Measuring what actually reaches the screen is worth doing rather than trusting
the eye — both traps above looked fine and were an order of magnitude wrong.
Sample a patch of clean surface away from markings and compare its p5–p95 spread
against the tile's authored spread; they should be close.

On hardware, `FrameTimeProbe` reports in development Android builds. Note that a
~0.05 ms spread across a window is vsync pinning the frame, so such a number is
evidence the target holds and **not** a headroom measurement.
