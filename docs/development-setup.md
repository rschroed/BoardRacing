# Development setup

This document records the local prerequisites for Tranche 0. The Board SDK and Piece Set Model are authenticated, restricted downloads and are not stored in this public repository.

## Verified Board dependencies

Verified through the Board Developer Portal on July 12, 2026:

| Dependency | Version | Source | Repository policy |
| --- | --- | --- | --- |
| Board Unity SDK | 3.3.0 | Authenticated Board Developer Portal | Download manually; do not commit |
| Board Arcade Piece Set Model | 1.3.7 | Authenticated Board Developer Portal or Unity SDK settings UI | Download manually; do not commit |
| Board Connect | 1.0.0-beta.5 | Board installer at `https://dev.board.fun/connect/install` | Install locally; do not commit the binary |

The SDK package and Arcade model have been downloaded on the initial development machine. Their local locations are machine-specific and must not be referenced by committed Unity configuration using absolute paths.

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

On another machine, install the same editor patch and Android modules through Unity Hub before opening the project. `ProjectSettings/ProjectVersion.txt` will become the source of truth after the Unity baseline is created in Issue #9.

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
5. Keep the downloaded files outside the repository.

Do not record portal credentials, download tokens, signed URLs, or personal download locations in tracked files.

## Hardware status

At the start of Tranche 0, Board Connect discovery returned no reachable Board on the local network. Physical hardware availability and BoardOS compatibility therefore remain unverified.

Issue [#13](https://github.com/rschroed/BoardRacing/issues/13) owns pairing, deployment, logs, screenshots, and physical Piece-input validation. Tranche 0 may otherwise complete under the roadmap's conditional hardware exception, but Tranche 1 cannot pass without physical-hardware evidence.

## Next setup steps

- Pin and install the Unity/Android toolchain in [#8](https://github.com/rschroed/BoardRacing/issues/8).
- Create the clean Unity baseline in [#9](https://github.com/rschroed/BoardRacing/issues/9).
- Import and configure the restricted dependencies in [#10](https://github.com/rschroed/BoardRacing/issues/10).
