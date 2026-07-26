# Course presentation assets — provenance

Home for production presentation assets: course textures, materials, shaders,
and theme definitions. Deliberately separate from `Runtime/` and `Domain/` so
art inputs never mix into gameplay or simulation code.

## Status: FPO — not final art

Everything under `CourseSurface/Textures/` is **for placement only**. The `FPO_`
prefix is the contract: no file with that prefix may be treated as shippable
art, and #161 does not decide whether final textures are generated,
commissioned, purchased, photographed, or drawn.

These tiles exist to validate composition, tiling density, filtering, and
device performance for the Quarry direction approved in
[#162](https://github.com/rschroed/BoardRacing/issues/162) — specifically the
question a desk monitor could not answer: whether this grain frequency survives
physical Board distance and hand occlusion, or turns to noise and moiré.

## What is here

| Asset | Source | Replacement status |
| --- | --- | --- |
| `FPO_CourseGround_Limestone.png` | Generated, `tools/generate_course_fpo_textures.py` | Replace with final ground material |
| `FPO_CourseRoad_Slate.png` | Generated, same script | Replace with final road material |
| `FPO_CourseShoulder_Gravel.png` | Generated, same script | Replace with final runoff material |
| `CourseSurface/Shaders/CourseSurface.shader` | Authored for #161 | Production shader, not FPO |
| `Resources/CourseSurfaceTheme.asset` | Authored for #161 | Production theme, FPO texture references |

No third-party or licensed source material is used. The generator is committed,
so every tile is reproducible from the repository alone:

```bash
python3 tools/generate_course_fpo_textures.py
```

## Why the tiles are grey

They are neutral grain maps centred on mid grey, not coloured surfaces. The
shader modulates the authored vertex colour by `detail * 2`, so a flat 0.5 tile
is a no-op and the theme's colours stay the thing that decides what a surface
looks like.

This is what makes the swap test in #161 meaningful: changing a texture
reference on the theme changes the surface without touching mesh generation,
shader, or gameplay code, and without disturbing the palette.

## Import settings (pinned, not editor defaults)

Committed in each `.png.meta` rather than left to whatever the editor picks:

- Texture type Default, shape 2D
- Wrap **Repeat** — required; world-space tiling depends on it
- Filter **Bilinear**, aniso 1
- **Mipmaps on** — see the mip note below
- sRGB on (the project is Gamma colour space, retained by #153)
- Max size 256, Android override on with normal compression

### Mip selection

The surface uses world-space mapping computed in the fragment shader, so the
mesh carries no per-vertex tiling UVs — UV0 is repurposed as the detail-weight
channel. Mip level therefore comes from the screen-space derivatives of the
shader-computed UV, which `SAMPLE_TEXTURE2D` handles automatically.

There is consequently no vertex UV distribution for Unity's mipmap streaming to
key off. That is why mips are **built into the textures** and streaming is left
off, rather than relying on runtime-mesh UV metadata that this mapping does not
produce.

## Theme composition

`CourseSurfaceTheme` is deliberately only the course child of a future
composition:

```text
BoardRacingTheme (future)
├── CourseSurfaceTheme  (this issue)
├── CarTheme            (later)
├── HudTheme            (later)
└── EffectsTheme        (later)
```

Player accent colours, HUD colours, and condition-state semantics are **not**
course-owned and are not defined here, so the course phase cannot accidentally
claim them. The downstream theme assets are intentionally not created yet.
