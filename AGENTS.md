# Board Racing agent instructions

Build Board Racing for the [Board platform](https://board.fun/) with Unity and the Board Unity SDK. Treat [Board's developer documentation](https://docs.dev.board.fun/) as the authoritative source when this file and the installed SDK disagree.

Project context:

- Read `README.md`, `docs/vision.md`, `docs/gameplay/car-and-pit-crew.md`, `docs/roadmap.md`, and `docs/technical-direction.md` before changing game behavior or architecture.
- Keep Board contacts behind a player-input adapter. Race rules must consume abstract commands and remain testable without Board hardware.
- Preserve mouse/keyboard input for editor iteration.
- Keep the race simulation deterministic and independent from visual effects.
- Do not expand prototype scope into final art, multiple tracks, championships, online play, or combat before the relevant roadmap gate passes.

Official Unity AI-assistant reference: <https://docs.dev.board.fun/unity/ai-assistant>

## Required namespaces

Import individual SDK namespaces. Do not use `using Board;`.

```csharp
using Board.Core;
using Board.Input;
using Board.Session;
using Board.Save;
```

These provide `BoardApplication`, input contacts, sessions and players, and save-game APIs respectively.

## Project setup and platform

- Run **Board > Configure Unity Project...** instead of manually reproducing SDK settings. The wizard configures the Android target, API levels, scripting backend, and Input System.
- `BoardGeneralSettings` and `BoardInputSettings` assets are created when the SDK is imported.
- Download the Piece Set Model through **Edit > Project Settings > Board > Input Settings > Load Available Models**.
- Target Unity 2022.3 LTS or later; Unity 6 is supported.
- Target Android 13 / API 33, ARM64, and IL2CPP.
- Use Unity Input System 1.7.0 or later.
- On Unity 6, set Android **Application Entry Point** to **Activity**, not Game Activity.
- Verify current BoardOS minimums in the official setup and deployment documentation. Board Connect requires BoardOS 1.10.0 or later.

Setup reference: <https://docs.dev.board.fun/unity/getting-started/setup-reference>

## Touch and Piece input

Read active contacts each frame:

```csharp
BoardContact[] contacts = BoardInput.GetActiveContacts();
BoardContact[] pieces = BoardInput.GetActiveContacts(BoardContactType.Glyph);
BoardContact[] fingers = BoardInput.GetActiveContacts(BoardContactType.Finger);
```

Important `BoardContact` properties:

- `contactId`: unique identity for this active contact.
- `glyphId`: Piece type within the active Piece Set; fingers report `-1`.
- `screenPosition`: screen-space position.
- `orientation`: rotation in radians.
- `phase`: `None`, `Began`, `Moved`, `Ended`, `Canceled`, or `Stationary`.
- `isTouched`: whether a player is physically touching the Piece.
- `type`: `Glyph` or `Finger`.

Track individual Pieces by `contactId`, never by `glyphId`. Multiple physical Pieces may share a Glyph type. A contact ID lasts only for the active contact; Piece removal and reacquisition must be handled explicitly.

Never retain stale throttle or crew input after a contact disappears. Reconcile tracked contacts against the full active-contact snapshot every frame and transition missing contacts to a safe state.

Touch guide: <https://docs.dev.board.fun/guides/touch>

## Board Racing input rules

- Translate SDK contacts into player-scoped commands such as throttle, pit request, service selection, and crew position.
- Centralize Glyph-to-player and Glyph-to-role association. Do not scatter raw Glyph IDs through gameplay code.
- Smooth orientation input in the adapter, not in the race simulation.
- Make smoothing, throttle dead zones, release behavior, and reacquisition behavior explicit and testable.
- Treat `Canceled` and missing contacts as release/removal events.
- Reinforce player colors with shape or iconography.
- Test crossing Pieces, simultaneous manipulation, wrong-player placement, lost recognition, and reacquisition.

## Input settings

Configure `BoardInputSettings` in **Edit > Project Settings > Board > Input Settings**.

Key settings include:

| Setting | Typical default | Meaning |
| --- | ---: | --- |
| Translation Smoothing | 0.5 | Higher values smooth motion but add lag |
| Rotation Smoothing | 0.5 | Higher values smooth rotation but add lag |
| Persistence | 4 | Frames a contact persists without confirmation |
| Piece Set Model | — | `.tflite` model in StreamingAssets |

Settings are read-only at runtime. To test alternatives, create multiple settings assets in the editor and switch the entire asset:

```csharp
BoardInput.settings = alternateSettings;
```

Changing `BoardInput.settings` cancels all active contacts. Switching Piece Set models also creates a short interval with no input. Route both through the same safe contact-loss behavior used for physical removal.

Only one Piece Set Model can be active at a time.

## Players and sessions

```csharp
BoardSessionPlayer[] players = BoardSession.players;
BoardPlayer activeProfile = BoardSession.activeProfile;

bool added = await BoardSession.PresentAddPlayerSelector();
bool replaced = await BoardSession.PresentReplacePlayerSelector(existingPlayer);

BoardSession.ResetPlayers();
BoardSession.playersChanged += OnPlayersChanged;
BoardSession.activeProfileChanged += OnActiveProfileChanged;
```

- A session always requires at least one Profile player.
- `BoardSessionPlayer` extends `BoardPlayer` with `sessionId`.
- Player changes should update Piece-role association through one coordinator rather than directly mutating race state from UI callbacks.

Player guide: <https://docs.dev.board.fun/guides/player-management>

## Save games

Use `BoardSaveGameManager` rather than writing Board-specific persistence elsewhere.

```csharp
var change = new BoardSaveGameMetadataChange {
    description = "Championship progress",
    playedTime = playedSeconds,
    gameVersion = Application.version,
    coverImage = screenshotTexture
};

BoardSaveGameMetadata saved =
    await BoardSaveGameManager.CreateSaveGame(saveData, change);

byte[] data = await BoardSaveGameManager.LoadSaveGame(saved.id);
BoardSaveGameMetadata updated =
    await BoardSaveGameManager.UpdateSaveGame(saved.id, newData, change);

BoardSaveGameMetadata[] saves =
    await BoardSaveGameManager.GetSaveGamesMetadata();
```

Loading a save activates the save's players in `BoardSession.players`. Do not allow overlapping save operations, and make version compatibility explicit when persistent game data is introduced.

Save guide: <https://docs.dev.board.fun/guides/save-games>

## Pause menu and exit

Configure the system pause screen once during startup:

```csharp
BoardApplication.SetPauseScreenContext(
    applicationName: "Board Racing",
    showSaveOptionUponExit: true
);
```

Handle pause actions and finish by calling `BoardApplication.Exit()`:

```csharp
BoardApplication.pauseScreenActionReceived += (action, audioTracks) => {
    switch (action) {
        case BoardPauseAction.Resume:
            break;
        case BoardPauseAction.ExitGameSaved:
            // Complete the save, then exit.
            BoardApplication.Exit();
            break;
        case BoardPauseAction.ExitGameUnsaved:
            BoardApplication.Exit();
            break;
    }
};
```

Use `BoardApplication.ShowProfileSwitcher()` and `HideProfileSwitcher()` for the system profile overlay. Do not quit Unity directly when leaving the game; always return through `BoardApplication.Exit()`.

Pause guide: <https://docs.dev.board.fun/guides/pause-menu>

## Build and deployment

Build an APK using Unity's Android build flow. Deploy over the LAN with Board Connect; USB is not required.

```bash
# Install once on macOS/Linux.
curl -fsSL https://dev.board.fun/connect/install | sh

# Pair once; the user approves on the Board.
board-connect pair <address>

# Install and foreground the game.
board-connect install path/to/board-racing.apk --launch

# Use the Unity Android package name for later commands.
board-connect launch <package-id>
board-connect logs <package-id> --follow
board-connect stop <package-id>

board-connect apps
board-connect status
board-connect remove <package-id>
```

The Board address is shown under **Settings > System**. After pairing, commands resolve the saved default Board unless a host override is supplied.

Deployment reference: <https://docs.dev.board.fun/unity/getting-started/build-and-deploy>

## Verification expectations

For every behavior change, report the highest verification level completed:

1. Race-domain unit tests.
2. Mouse/keyboard fallback in the Unity editor.
3. Board Unity SDK simulator.
4. Physical Board hardware.

Do not imply simulator or hardware coverage when only editor controls were tested. Hardware-dependent acceptance criteria that remain unverified require an explicit follow-up issue.

Before handing off a change:

- Run relevant automated tests.
- Confirm contact-loss behavior fails safe.
- Check top-down readability at 1920×1080.
- Check simultaneous input when the change touches Piece behavior.
- Record simulator and hardware coverage in the pull request.

## GitHub issue formatting

When creating or editing an issue with GitHub CLI, pass Markdown through a real
file with `--body-file`. Do not put escaped newlines such as `\\n` in a `--body`
argument; GitHub stores those characters literally and renders the issue as one
line.

Before finishing, read the saved body back with `gh issue view <number> --json
body --jq .body` and confirm headings and lists contain real line breaks.

## Additional references

- Unity API: <https://docs.dev.board.fun/unity/api/>
- Simulator: <https://docs.dev.board.fun/unity/simulator>
- Unity changelog: <https://docs.dev.board.fun/unity/changelog>
- Board architecture: <https://docs.dev.board.fun/learn/architecture>
