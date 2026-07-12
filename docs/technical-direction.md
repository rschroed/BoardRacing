# Technical direction

## Decision

Use Unity and the Board Unity SDK for the first prototype.

Board currently describes Unity as its most mature SDK with the broadest API surface. Its built-in Piece simulator is particularly valuable while the project is tuning rotation, touch state, simultaneous contacts, and Piece recovery.

This decision can be revisited; game rules should not depend directly on Unity components or Board contact objects.

## Architecture boundary

```text
Board contacts / simulated controls
                ↓
        Player input adapter
                ↓
       Abstract game commands
                ↓
    Deterministic race simulation
                ↓
      Visual/audio presentation
```

Example commands:

```csharp
SetThrottle(playerId, amount);
RequestPit(playerId);
SelectService(playerId, service);
UpdateCrewPiece(playerId, position, orientation, touched);
```

The simulation should run with Board input, Unity's Board simulator, or ordinary mouse/keyboard controls.

## Rendering target

- Fixed 1920×1080 landscape presentation.
- Orthographic, top-down 2D scene.
- Cars constrained to track splines rather than free rigid-body steering.
- Sprite or simple mesh rendering with unlit materials.
- Restrained glow, trails, and pooled particles.
- Readability and stable frame time before decorative density.

## Testing strategy

- Unit-test race rules independently from rendering and Board input.
- Use deterministic inputs for lap, heat, wear, and pit-service tests.
- Maintain mouse/keyboard controls for rapid editor iteration.
- Use the Board simulator for Piece behavior before device deployment.
- Validate all important interaction assumptions on retail hardware.

## Primary technical risks

- Orientation noise and choosing a useful throttle mapping.
- Reliably associating eight physical Pieces with four players.
- Lost contacts accidentally retaining control state.
- Hand occlusion and simultaneous Piece manipulation.
- Differences between simulation and physical hardware.
- Table-wide readability and performance at native resolution.

## Revisit triggers

Reconsider Godot or Web if Unity materially slows iteration, licensing becomes unacceptable, or the proven game is simple enough that portability outweighs the Unity SDK's simulator and maturity advantages.

## References

- [Board developer documentation](https://docs.dev.board.fun/)
- [Board Unity SDK documentation](https://docs.dev.board.fun/unity/)
- [Board architecture](https://docs.dev.board.fun/learn/architecture)
