# Direction E asset and effects study

Status: **Direction E owner-approved on July 28, 2026**

This study keeps one stable, recognizable widebody muscle-coupe silhouette and
tests how it can support the four Board Racing player colors plus runtime visual
response.

## Proposed first-pass asset set

- `direction-e-{color}.png`: enlarged review exports for orange, purple, pink,
  and yellow.
- `actual-size/direction-e-{color}.png`: `54 × 26 px` gameplay-size exports.
- `direction-e-shadow.png`: independently composited ground shadow.
- `direction-e-impact-flash.png`: example neutral impact overlay.
- `direction-e-master.png`: transparent high-resolution generated master.
- `direction-e-asset-and-effects-study.png`: annotated review board with the
  four colors, asset stack, response states, and in-situ scale check.
- `direction-e-approved-evidence.png`: approval evidence covering four
  orientations, non-color identity, side-by-side racing, corner drift, adjacent
  pit boxes, and actual-size readability.
- `direction-e-approved-response-vocabulary.png`: the approved direction shown
  with the ticket's Brake, Drive, Boost, Fast Corner, and High Heat vocabulary.

The first production pass should use one flattened sprite per player color plus
the separate shadow and effect anchors. It does not need a skeletal or
part-swapping car rig.

## Runtime-only response

The deterministic race transform owns position and rotation. A child visual
root may add temporary squash, squat, nose dip, yaw lag, recoil, or bounce.
Particles and overlays attach at front, rear, rear-left, and rear-right anchors.

Initial response vocabulary:

- acceleration: brief squat and exhaust puff;
- braking: nose dip and body compression;
- turning: yaw lag, settle, and occasional skid;
- impact: directional recoil, neutral flash, and sparks.

The issue acceptance vocabulary is documented separately as Brake, Drive,
Boost, Fast Corner, and High Heat. These are visual-language studies rather
than timing, particle, pooling, or implementation specifications.

## Non-color player reinforcement

The first-pass roof-marker mapping is:

- Orange: triangle;
- Purple: circle;
- Pink: diamond;
- Yellow: square.

The markers reinforce the body color and remain distinguishable in the
grayscale evidence. Their final production rendering may be adjusted after
actual device testing without reopening the approved car silhouette.

## Generation note

The master was created with the built-in image-generation workflow from the
existing Round 2 widebody muscle-coupe concept. The prompt requested one
orthographic top-down orange coupe with a long hood, wide stance, bold ivory
stripe, simplified dark glass and tires, restrained satin highlights, and
readability at `54 × 26 px`. It explicitly excluded perspective, cast shadows,
text, logos, numbers, smoke, speed lines, and micro-detail. The four player
colors were derived from that single master so their silhouettes remain
identical.
