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
