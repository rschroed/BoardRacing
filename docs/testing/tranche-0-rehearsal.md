# Tranche 0 clean-checkout rehearsal

This records the initial onboarding rehearsal for the development runway. Repeat it after a material toolchain, Board SDK, or dependency-installation change.

## Rehearsal procedure

1. Clone the `main` branch into a new directory with no existing `Library`, package cache, or Unity user settings.
2. Follow [development setup](../development-setup.md), including Unity `2022.3.62f3`, Android modules, Android SDK Platform 33, Git LFS, Board Connect, and authenticated Board artifacts.
3. Place the verified SDK archive and Arcade model at their documented ignored paths.
4. Open the repository root in Unity with Android selected and confirm package import succeeds.
5. Import the official Board SDK Input sample through Package Manager.
6. Run the [Board input simulator smoke test](board-input-simulator.md) or a representative place, touch, move, rotate, and remove sequence.
7. Produce and inspect an APK using the [Android development build procedure](android-development-build.md).
8. Remove any temporary local test/build scripts and confirm `git status --short` is empty.

Restricted downloads, imported vendor samples, generated Unity state, and build output are expected to appear only as ignored files.

## Initial result

Passed on July 12, 2026 from commit `93390be` in a separate temporary clone:

- The SDK and model checksums matched the documented values.
- Unity `2022.3.62f3` resolved Board SDK `3.3.0`, Input System `1.14.0`, and Unity UI without compiler errors.
- The official Input sample imported and its `BoardInputManager` ran in Play Mode.
- A representative Arcade Piece reported placement, touch, movement, 30-degree rotation, and removal through Board's simulator contact path.
- The development APK built with package `com.wholestudios.boardracing`, API 33, ARM64-only native code, and `libnativeBoardSDK.so`.
- After temporary harness removal, the clean clone remained identical to `origin/main`; all authenticated dependencies, imported sample files, Unity-generated state, and APK output remained ignored.

No undocumented setup knowledge was required after adding the explicit Android Platform 33 installation and bundled-Java instructions to development setup.

## Hardware exception

Physical Board validation is tracked in Issue [#13](https://github.com/rschroed/BoardRacing/issues/13). At initial Tranche 0 sign-off, Board Connect could not discover hardware on the LAN, so #13 is assigned and marked `blocked` with its owner and unblock condition documented.

This exception satisfies the Tranche 0 roadmap rule but does not satisfy Tranche 1. Physical Board evidence is a blocking Tranche 1 exit requirement.
