# Board Racing

Board Racing is a tactile, top-down slot-car game for 1–4 players on [Board](https://board.fun/). Each player uses two physical Pieces: one to drive and one to operate the pit crew.

> Status: Tranche 4 wireframe experience proof is in progress. The two-player state contract and Corner Controllers direction are approved; staged implementation captures, Android comparison, and the physical readability gate remain before passage.

## Player experience

One hand manages speed while the other prepares tires, cooling, and repairs. Races should be immediately understandable, last roughly 6–10 minutes, and create meaningful decisions without becoming a driving simulation.

The initial concept uses:

- A **Ship Piece** for tactile Brake, Drive, and Boost control.
- A **Robot Piece** for Call Pit and service interactions.
- A fixed top-down track with automatic path following.
- Tire wear and motor heat as the first strategic systems.

## Technical direction

The first prototype will use Unity and the Board Unity SDK. Unity currently provides Board's most mature SDK and a Piece-input simulator, reducing risk while the physical interaction is still being discovered.

## Documentation

- [Product vision](docs/vision.md)
- [Car and Pit Crew interaction](docs/gameplay/car-and-pit-crew.md)
- [Wireframe UI contract](docs/gameplay/wireframe-ui.md)
- [Roadmap](docs/roadmap.md)
- [Technical direction](docs/technical-direction.md)
- [Development setup](docs/development-setup.md)
- [Tranche 1 control lab](docs/testing/tranche-1-control-lab.md)
- [Tranche 1 validation record](docs/testing/tranche-1-validation.md)
- [Tranche 2 racing-proof gate](docs/testing/tranche-2-racing-proof.md)
- [Tranche 3 pit-crew proof](docs/testing/tranche-3-pit-crew-proof.md)
- [GitHub workflow](docs/workflow.md)

## Immediate milestone

Implement and validate the Tranche 4 wireframe state matrix at 1920×1080, then run the two-person physical Board readability gate. Final aesthetics, additional tracks, AI, setup, profiles, tutorials, championships, and broader group-flow work remain deferred.
