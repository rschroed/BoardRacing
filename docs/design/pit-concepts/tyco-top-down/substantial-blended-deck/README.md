# #183 exploration — substantial blended deck

These studies build from the compact **Blended Deck** direction. They keep the
strict top-down view, production-scale cars, side-mounted controls, continuous
pit asphalt, and tan course apron.

## Shared correction

The stop marker is transverse to the car: a single short strip beneath the rear
axle, perpendicular to the car's left-to-right long axis. Only its ends extend
beyond the car, so it reads as a physical stopping reference rather than a lane
line.

## Options

### A — Transverse stop

The lightest treatment. A full-car-width stop marker gives each bay more visual
weight without adding another major object. This preserves the clearest road
surface and is the smallest implementation step.

### B — Full-length service bench

The compact pod becomes a low, car-length toy-console running beside the car.
Player identity, activity lights, service control, and a central service flap
become one substantial silhouette. This most directly answers the need for the
pit equipment to feel as substantial as the car while retaining the open-bay
layout.

### C — Shared lap counter

Compact individual pods remain, with a single flat four-player lap counter at
the end of pit row. The counter gives the whole row a stronger destination and
evokes the slot-car inspiration without cluttering every bay. Its placement can
be retained independently of whether live lap-count behavior ships in #183;
that behavior should remain a separate HUD/race-state decision.

## Geometry note

These images explore the compact 88 px cadence from the earlier Blended Deck
study. Production pit centers are currently authored at 114 px with a 94 px
service footprint and 20 px edge gap. Adopting the compact cadence would reopen
the #182 stall positions, car paths, and geometry validation; the visual kit can
instead be scaled to the existing centers.

## Generation notes

Created with built-in ImageGen in edit/reference mode. The prompt set held the
approved top-down Blended Deck composition and production car scale constant,
then varied one structural idea at a time: a corrected transverse stop marker,
a full-length side service bench, and a shared end-of-row lap counter.

The production assets use the approved option B image as their style and
proportion reference:

- `PitKit_ServiceBenchB` asks for one isolated, strict-top-down cobalt-blue
  molded-plastic console with an empty player-marker socket, three dark lamp
  bezels, and one mustard-yellow button. It excludes cars, roads, text, arms,
  tools, and extra props.
- `PitKit_ServiceTongueB` asks for one isolated charcoal connector with three
  mustard contact bars, sized to bridge the console and parked car.

Both were generated on a flat magenta chroma field with built-in ImageGen,
keyed locally to alpha, trimmed, downscaled for the production sprite budget,
and saved under `Assets/BoardRacing/Presentation/Resources/Pits`.
