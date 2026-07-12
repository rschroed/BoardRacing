# Development setup

This document records the local prerequisites for Tranche 0. The Board SDK and Piece Set Model are authenticated, restricted downloads and are not stored in this public repository.

## Verified Board dependencies

Verified through the Board Developer Portal on July 12, 2026:

| Dependency | Version | Source | Repository policy |
| --- | --- | --- | --- |
| Board Unity SDK | 3.3.0 | Authenticated Board Developer Portal | Download manually; do not commit |
| Board Arcade Piece Set Model | 1.3.7 | Authenticated Board Developer Portal or Unity SDK settings UI | Download manually; do not commit |
| Board Connect | 1.0.0-beta.5 | Board installer at `https://dev.board.fun/connect/install` | Install locally; do not commit the binary |

The SDK package and Arcade model have been downloaded on the initial development machine. The Unity project references their ignored, project-relative installation paths; no committed configuration contains a machine-specific absolute path.

Verified SHA-256 checksums:

| Artifact | SHA-256 |
| --- | --- |
| `fun.board-3.3.0.tar` | `865f53c4cbb2011896348d8277ca1223176090c91608ef75352970526340307d` |
| `arcade_v1.3.7.tflite` | `32fb80482b974e09e142985cf0210c712726f1ad289a532b21d2edcd84fbc8a5` |

## Pinned development toolchain

The initial development environment is Apple Silicon macOS and uses:

| Tool | Pinned version | Notes |
| --- | --- | --- |
| Unity Hub | 3.19.5 | Installation and editor management only |
| Unity Editor | 2022.3.62f3 Apple Silicon | Changeset `96770f904ca7`; standard LTS security-patched release dated October 28, 2025 |
| Git LFS | 3.7.1 | Installed and initialized in the user Git configuration |
| Board Connect | 1.0.0-beta.5 | Installed by Board's official macOS/Linux installer |

Unity 2022.3.62f3 was selected because Board recommends Unity 2022.3 LTS for the current SDK and this is the latest security-patched standard LTS build available to Unity Personal licenses. Later 2022.3 releases are Extended LTS builds that require Unity Industry or Enterprise licensing; Unity 2022.3.74f1 was tested and correctly rejected the project's Personal license.

Do not silently upgrade the project editor. Upgrade through a dedicated issue that confirms Board SDK and license compatibility and records the new `ProjectVersion.txt` value.

### Unity Android modules

Install the editor with **Android Build Support** and its child modules:

- OpenJDK `11.0.14.1+1` supplied by Unity.
- Android NDK r23b supplied by Unity.
- Android SDK Build Tools 34.0.0.
- Android SDK Platform Tools 32.0.0.
- Android SDK Platforms 34, 35, and 36 installed by the current Unity Hub module definition.
- Android SDK Command Line Tools 6.0.

Use Unity-managed SDK, NDK, and JDK paths. The Board project setup wizard remains responsible for selecting the application target settings required by the SDK, including API level, ARM64, IL2CPP, orientation, and Input System configuration.

Unity Hub's Android module does not currently include Android SDK Platform 33, which the Board configuration targets. After installing the Hub modules, use Unity's bundled OpenJDK and SDK manager to review the Android SDK licenses and install Platform 33:

```bash
UNITY_ANDROID="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer"

JAVA_HOME="$UNITY_ANDROID/OpenJDK" \
  "$UNITY_ANDROID/SDK/cmdline-tools/6.0/bin/sdkmanager" \
  --sdk_root="$UNITY_ANDROID/SDK" \
  --licenses

JAVA_HOME="$UNITY_ANDROID/OpenJDK" \
  "$UNITY_ANDROID/SDK/cmdline-tools/6.0/bin/sdkmanager" \
  --sdk_root="$UNITY_ANDROID/SDK" \
  "platforms;android-33"
```

Review and accept the licenses interactively; do not pipe automatic confirmation into the license command. On non-macOS systems, use the equivalent Unity editor installation path.

On another machine, install the same editor patch and Android modules through Unity Hub before opening the project. `ProjectSettings/ProjectVersion.txt` is the source of truth for the required editor.

## Open the Unity project

The repository root is the Unity project. Add that directory in Unity Hub and open it with Unity `2022.3.62f3`; do not create a second Unity project inside the repository.

The committed baseline contains:

- An empty startup scene at `Assets/Scenes/Main.unity`.
- A fixed 1920×1080 landscape-left presentation.
- Android as the development build target expectation.
- Unity package and project metadata, including the editor version and package lock file.

After a fresh clone, switch **File > Build Settings > Platform** to **Android** before Board SDK configuration or deployment work. The selected platform is local editor state and is not relied upon as committed configuration. The Board configuration wizard owns the committed Android API, ARM64, IL2CPP, and Input System settings.

### Install the restricted Unity dependencies

Before opening a fresh clone in Unity:

1. Download **Board Unity SDK v3.3.0** and verify its checksum above.
2. Copy it to `Packages/fun.board-3.3.0.tar` without extracting it. The committed package manifest resolves `fun.board` from this relative, ignored path.
3. Create `Assets/StreamingAssets` if necessary.
4. Download **Board Arcade v1.3.7**, verify its checksum, and copy it to `Assets/StreamingAssets/arcade_v1.3.7.tflite`.
5. Open the repository root with Unity `2022.3.62f3`. Confirm that **Board SDK 3.3.0**, **Input System**, and **Unity UI** resolve without compiler errors.
6. In **Window > Package Manager**, select **Board SDK**, open the **Samples** tab, and import the **Input** sample. The authorized local copy under `Assets/Samples/Board SDK/3.3.0/Input` is ignored and must not be committed.

The Board SDK uses Unity UI and EventSystem types but v3.3.0 does not declare `com.unity.ugui` in its own package dependencies. This project therefore declares Unity UI `1.0.0` directly in `Packages/manifest.json`.

The committed Board settings select the Arcade model by filename and use the SDK defaults of translation smoothing `0.5`, rotation smoothing `0.5`, and persistence `4`. The model binary and its Unity metadata remain ignored.

The Board configuration wizard has been applied. Its committed project settings are Android API 33 minimum and target, IL2CPP, ARM64 only, the new Input System, and landscape-left orientation. Rerun **Board > Configure Unity Project...** after a Board SDK upgrade or when Unity reports a configuration warning, then restore ARM64-only targeting if the wizard adds another architecture.

### Local command verification

Run:

```bash
git lfs version
board-connect --version
```

Expected versions for the initial environment:

```text
git-lfs/3.7.1
board-connect version v1.0.0-beta.5
```

Board's installer places `board-connect` in `~/.local/bin`. Add that directory to `PATH` locally when it is not already available. Do not commit shell-specific absolute paths or modify repository scripts to depend on one developer's home directory.

## Why restricted artifacts are not committed

The [Board Developer Terms of Use](https://docs.dev.board.fun/more/license) grant a non-exclusive, non-sublicensable, non-transferable, revocable license to download, install, and use the SDK for Board development. Section 3 prohibits distributing, publishing, transferring, or otherwise making the SDK available to third parties without prior written consent, except for permitted distributable elements incorporated into developed programs or tools.

Because this repository is public, the conservative project policy is:

- Do not commit the Board SDK archive or an extracted copy of the SDK.
- Do not commit Piece Set Model files unless Board provides explicit written redistribution permission for them.
- Do not mirror either artifact in Git LFS, release attachments, package registries, or CI caches accessible to third parties.
- Document the required versions and authenticated installation steps instead.
- Re-check the current terms before changing this policy.

This is a repository-handling decision, not legal advice.

## Obtain the restricted dependencies

1. Join the [Board Developer Program](https://board.fun/pages/create).
2. Sign in to the [Board Developer Portal](https://dev.board.fun/) with the Board account authorized for development.
3. Download **Board Unity SDK v3.3.0**.
4. Download **Board Arcade v1.3.7** under Piece Set Models. The Unity SDK may also retrieve available models through **Edit > Project Settings > Board > Input Settings** after the SDK is installed.
5. Keep the original downloads in secure local storage. Install copies only at the ignored project-relative paths documented above.

Do not record portal credentials, download tokens, signed URLs, or personal download locations in tracked files.

## Hardware status

Initial [physical Board validation](testing/physical-board-validation.md) passed on BoardOS 2.0.3. Pairing, application install/launch, logs, screenshot, stop/relaunch, and physical Arcade Piece placement, touch, movement, rotation, release, and removal were verified.

Board addressing and pairing state remain local. Never add device addresses, credentials, tokens, or Board Connect user state to repository documentation or configuration.

## Next setup steps

- Use the [Board input simulator smoke test](testing/board-input-simulator.md) after dependency or input-setting changes.
- Use the [Android development build procedure](testing/android-development-build.md) to produce and inspect local APKs.
- Repeat the [physical Board validation](testing/physical-board-validation.md) after platform or physical-input changes.
