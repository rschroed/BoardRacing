# Board Racing

Board Racing is a tactile, top-down slot-car game for 1–4 players on [Board](https://board.fun/). Each player uses two physical Pieces: one to drive and one to operate the pit crew.

> Status: Tranche 1 physical-control proof passed on July 12, 2026. Tranche 2 racing-proof work may begin; mechanics and visual direction remain provisional until later roadmap gates pass.

## Player experience

One hand manages speed while the other prepares tires, cooling, and repairs. Races should be immediately understandable, last roughly 6–10 minutes, and create meaningful decisions without becoming a driving simulation.

The initial concept uses:

- A **Car Piece** for tactile throttle control.
- A **Pit Crew Piece** for preparation and pit-stop interactions.
- A fixed top-down track with automatic path following.
- Tire wear and motor heat as the first strategic systems.

## Technical direction

The first prototype will use Unity and the Board Unity SDK. Unity currently provides Board's most mature SDK and a Piece-input simulator, reducing risk while the physical interaction is still being discovered.

## Documentation

- [Product vision](docs/vision.md)
- [Car and Pit Crew interaction](docs/gameplay/car-and-pit-crew.md)
- [Roadmap](docs/roadmap.md)
- [Technical direction](docs/technical-direction.md)
- [Development setup](docs/development-setup.md)
- [Tranche 1 control lab](docs/testing/tranche-1-control-lab.md)
- [Tranche 1 validation record](docs/testing/tranche-1-validation.md)
- [GitHub workflow](docs/workflow.md)

## Immediate milestone

Build one understandable five-lap placeholder race using the proven Car controls. Art, additional tracks, championships, and content production remain deferred until the racing and pit-strategy gates pass.
