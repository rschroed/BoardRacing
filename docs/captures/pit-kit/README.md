# Pit kit verification captures

These deterministic 1920×1080 captures exercise the approved issue #183
full-length service bench over every production course without changing the
issue #182 pit geometry.

- `wedge-quiet.png`, `fishhook-quiet.png`, `hourglass-quiet.png`, and
  `infinity-quiet.png` show four occupied stalls at their authored parked poses.
- `wedge-inactive.png` confirms that the physical benches, connectors, stops,
  and identity markers remain fully opaque before any racer becomes active.
- `wedge-active-states.png` shows, from left to right, occupied, servicing,
  ready, and releasing treatments.

Regenerate them with:

```bash
Unity -batchmode -projectPath . -executeMethod PitKitCaptures.Run
```

The capture utility is editor-only and is not included in Android players.
