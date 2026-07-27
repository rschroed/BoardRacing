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

## How the theme works

The mechanism — the blend model, world-space mapping, the tile-size/source-
resolution coupling, and what to touch to change a slot — is documented once in
[docs/course-surface-theme.md](../../../docs/course-surface-theme.md). This file
stays about where these particular assets came from.

Two things about the assets follow from it and are worth repeating here:

- The tiles **carry their own colour**, so replacing one changes colour as well
  as grain. The per-surface tint in the theme is a grade on top, not the colour
  source, and defaults to white.
- Their **128 px source resolution is not arbitrary** — it matches the 128 px
  tile size so the mapping is 1:1. A tile shown smaller than its source gets
  mipped and loses the authored grain. Regenerate at a matching resolution
  rather than reusing these at a very different tile size.

## Import settings (pinned, not editor defaults)

Committed in each `.png.meta` rather than left to whatever the editor picks:

- Texture type Default, shape 2D
- Wrap **Repeat** — required, not preferred; world-space mapping produces UVs
  far outside 0-1 and clamp would stretch one copy across the board
- Filter **Bilinear**, aniso 1
- **Mipmaps on**, streaming off — the mesh carries no per-vertex tiling UVs for
  streaming to key off, so mips ship in the texture
- sRGB on (the project is Gamma colour space, retained by #153)
- Max size 128, Android override on with normal compression

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
