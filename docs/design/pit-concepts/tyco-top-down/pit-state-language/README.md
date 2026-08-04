# #183 exploration — physical pit-state language

The production race canvas previously attached these text overlays directly to
moving cars:

- `SLOWDOWN!`
- `FINISHED · <place>`
- `PIT @ LINE`
- `PIT QUEUE`
- `WAITING FOR PIT LANE`
- `PIT ENTRY`
- `CAR PARKED · REPAIR OR LEAVE`
- `IN BOX · TIRES`
- `IN BOX · FUEL`
- `PIT EXIT`

It also drew each player's Piece symbol over their pit box. Those overlays are
being removed. The authored cars already carry player color and shape, and the
pit hardware should feel like a physical part of the course rather than a HUD
projected onto it.

## Reduced state vocabulary

The pit apparatus only needs to communicate five physical states:

1. **Idle** — dormant hardware.
2. **Called** — the player's pit wakes as soon as Call Pit is active.
3. **Working** — the car is occupied, Fuel and Tires are visibly distinct, and
   progress is legible.
4. **Ready** — service is complete and the car can leave.
5. **Releasing** — a brief directional light or motion cue confirms departure.

The apparatus does not need to explain queue arbitration, pit-entry navigation,
slowdown penalties, or finishing place. Queue/wait detail can be omitted unless
playtesting shows that it is needed. Slowdown and finishing place belong to a
future race HUD treatment, not to the pit.

## Directions for review

### A — Service cartridge

Retain the approved three lamp bezels as coarse progress. The large service
socket receives a chunky physical cartridge: amber fuel hardware or a cyan tire
unit. This is the smallest change to the approved bench and keeps its current
silhouette.

### B — Mechanical progress window

Replace the three round lamps with a broad four-segment recessed window. Fuel
fills it amber and Tires fills it cyan, while a physical service piece rises by
the large button. This reads more clearly as progress but is a larger revision
and moves slightly closer to interface language.

Both directions are strict top-down, opaque, text-free, and designed to remain
legible at the production car scale. The studies are layout references rather
than production sprite sheets.
