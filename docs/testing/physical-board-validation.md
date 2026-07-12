# Physical Board validation

This records the initial end-to-end hardware validation for the development runway. Repeat the relevant checks after BoardOS, Board Connect, Board SDK, Android toolchain, or physical-input changes.

## Environment

Validated July 12, 2026:

| Component | Observed value |
| --- | --- |
| BoardOS | 2.0.3 |
| Board Connect | 1.0.0-beta.5, protocol v1 |
| Application | `com.wholestudios.boardracing` version 1.0 |
| Unity | 2022.3.62f3 development build |
| Runtime | IL2CPP, ARM64 |
| Board SDK | 3.3.0 |
| Piece Set Model | Board Arcade 1.3.7 |

No device address, credentials, pairing tokens, or saved pairing state are committed.

## Board Connect loop

The paired Board reported ready and exposed the required `install`, `launch`, `stop`, `logs`, and `screenshot` v1 capabilities.

Verified operations:

- Installed and launched the baseline development APK.
- Streamed Unity startup logs confirming the expected package, editor, Development/IL2CPP build, and ARM64 CPU target.
- Captured the Board screen to a temporary local PNG.
- Stopped and relaunched the application successfully.
- Built, installed, and launched a local APK containing the official Board SDK Input sample under the same development package ID.

## Physical Piece input

The official Input sample recognized physical Board Arcade Pieces and displayed live contact state.

Initial evidence showed Glyph 5 as contact 24 in `Stationary`, at `(1472.57, 527.87)` with orientation `4.990491` radians.

After hands-on manipulation, the sample simultaneously displayed:

| Glyph | Contact | Phase | Position | Orientation |
| ---: | ---: | --- | --- | ---: |
| 5 | 884 | `Stationary` | `(1135.27, 343.93)` | 3.82087 |
| 2 | 893 | `Moved` | `(1632.00, 630.15)` | 0.2540586 |
| 0 | 868 | `Moved` | `(529.32, 692.00)` | 5.648162 |

The operator completed placement, touch, movement, rotation, release, and removal with no unexpected behavior. Distinct Piece identities, contact IDs, simultaneous contacts, movement, and orientation changes were observable on physical hardware.

## Result

Passed. The Unity-to-Board loop is operational, and the initial physical-input prerequisite for Tranche 1 is no longer blocked. This validates the vendor sample and platform loop; it does not yet validate the planned Car and Pit Crew control design.
