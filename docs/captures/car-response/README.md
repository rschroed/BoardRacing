# Car response capture evidence

Issue #176 implements the owner-approved Direction E response vocabulary from
`docs/design/car-concepts/direction-e-asset-study/` on the current Quarry course
treatment.

The captures are deterministic 1920×1080, no-MSAA renders from the production
surface and car-response paths:

- `01-Brake`: existing body dive plus paired rear brake cues.
- `02-Drive`: settled body with a restrained pulsing exhaust response.
- `03-Boost`: bounded body strain, rear flare, and short attached streaks.
- `04-FastCorner`: existing drift plus two short attached contact streaks.
- `05-FourCarBoostCorner`: the maximum-density case with all four cars boosting
  and cornering simultaneously.

Generate them with:

```bash
Unity -batchmode -projectPath . -executeMethod CarResponseCaptures.Run
```

The effects are retained car children. They never express world position or
heading, leave no surface marks, and instantiate nothing while racing.
