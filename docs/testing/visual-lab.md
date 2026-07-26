# Visual Lab development workflow

The Visual Lab is a development-only uGUI overlay on the normal Board Racing
app. The live lobby and race remain the stage; the lab does not boot a second
scene, simulation, or renderer.

## Unity editor

1. Open the normal `Main` scene and enter Play mode.
2. Press **F10** to expose the `VISUAL LAB` launcher.
3. Click the launcher to open the shell.
4. Use **CARS** and **HUD** to compose the live stage.
5. Use **CLEAN VIEW** to hide all lab pixels. Click the launcher's right-edge
   location again to restore the shell.
6. Close the panel with **×**. Press **F10** again to remove the launcher.

The lab starts unavailable in the editor so automated capture runs retain their
existing clean comparison images.

## Board development build

Build an Android **Development Build** through the normal Unity build flow. The
`VISUAL LAB` launcher is visible at the right edge when the app starts and uses
Board finger `Began` contacts. A press consumed by the panel is not forwarded to
the lobby or race-navigation controls beneath it.

The normal player setup starts races. Visual Lab v1 does not synthesize players,
Piece claims, or a second development race lifecycle.

## Release exclusion

The shell and its integration references are guarded by:

```csharp
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
```

Normal Android release players therefore compile without a Visual Lab code
path. No player-facing gesture or runtime setting enables it.
