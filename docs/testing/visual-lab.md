# Visual Lab development workflow

The Visual Lab is a development-only uGUI overlay on the normal Board Racing
app. The live lobby and race remain the stage; the lab does not boot a second
scene, simulation, or renderer.

## Unity editor

1. Open the normal `Main` scene and enter Play mode.
2. Press **F10** to expose the `VISUAL LAB` launcher.
3. Click the launcher to open the shell.
4. Use **CARS** and **HUD** to compose the live stage.
5. Use **COURSE SURFACE** to tune the temporary rendering style.
6. Close the panel with **×** to return to the narrow visible `LAB` edge tab.
7. Press **F10** again when an exact chrome-free editor capture is required.

The lab starts unavailable in the editor so automated capture runs retain their
existing clean comparison images.

## Board development build

Build an Android **Development Build** through the normal Unity build flow. A
narrow `LAB` tab remains visible at the extreme right edge when the panel is
collapsed, giving the Board an explicit restore control without an invisible
gesture. It uses Board finger `Began` contacts. A press consumed by the panel is
not forwarded to the lobby or race-navigation controls beneath it.

The normal player setup starts races. Visual Lab v1 does not synthesize players,
Piece claims, or a second development race lifecycle.

## Course Surface Composer

The first panel edits the rendering style consumed by the production
`RaceSurfaceGeometry` builder. It never changes the authored course,
driveable width, pit paths, or simulation.

- Tap **COURSE** during setup to cycle Wedge, Hourglass, Infinity, and Fishhook.
  Course selection locks once a race begins so the rendered and simulated
  course cannot diverge.
- Tune shoulder opacity, solid width, and feather width. The feather is built
  from opaque, ground-precomposed ribbons, so self-crossings and pit-adjacent
  coverage do not accumulate transparency.
- Toggle stripes and the pit surface, or cycle through composed,
  shoulder-only, and authoritative-road-boundary views.
- Toggle **MESH WIREFRAME** to overlay the triangle edges derived from the
  current runtime surface mesh. It follows every surface rebuild but does not
  itself rebuild the course or change simulation state.
- **RESET** restores the deterministic committed style. **LOG** emits one
  `[CourseSurfaceComposer]` record containing the course and every exposed
  semantic value.

Surface-style control changes replace only the static course mesh and ground
color. The wireframe control only toggles its derived line overlay. Cars, HUD
objects, and simulation state remain live and retain their identity and pose.

For a non-play-mode review of the same production mesh path across all four
courses, run:

```bash
Unity -batchmode -projectPath . \
  -executeMethod CourseGalleryCaptures.RunShoulderReview
```

The review images are written to the operating system's temporary directory;
experiments do not modify shared Unity assets or the committed course gallery.

## Release exclusion

The shell and its integration references are guarded by:

```csharp
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
```

Normal Android release players therefore compile without a Visual Lab code
path. No player-facing gesture or runtime setting enables it.
