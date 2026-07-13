# Android development build

Use this procedure to produce a local APK for simulator-independent inspection and later Board deployment. Release signing and release automation are intentionally out of scope.

## Build identity and output

| Setting | Value |
| --- | --- |
| Android package identifier | `com.wholestudios.boardracing` |
| Minimum Android API | 33 |
| Target Android API | 33 |
| Scripting backend | IL2CPP |
| Architecture | ARM64 only |
| Output | `Builds/Android/BoardRacing-development.apk` |

The `Builds` directory and APK files are ignored. Do not add APKs, generated Gradle projects, debug keystores, signing credentials, or machine-local Android configuration to Git.

## Prerequisites

- Complete [development setup](../development-setup.md).
- Install Android SDK Platform 33 in Unity's bundled SDK.
- Review and accept the Android SDK licenses on each development machine. Do not automate acceptance of third-party legal terms.
- Confirm **Board > Configure Unity Project...** reports the project correctly configured.

## Unity procedure

1. Open **File > Build Settings** and select **Android**.
2. Confirm `Assets/Scenes/Main.unity` is the only enabled scene.
3. Enable **Development Build**. Enable script debugging only when needed for local diagnosis.
4. Build to `Builds/Android/BoardRacing-development.apk`.
5. Confirm the Unity build report succeeds without Board SDK validation warnings.
6. Inspect the APK using Android Build Tools `aapt` and `unzip`:

```bash
aapt dump badging Builds/Android/BoardRacing-development.apk
unzip -l Builds/Android/BoardRacing-development.apk
```

Expected metadata includes package `com.wholestudios.boardracing`, minimum and target API 33, `application-debuggable`, and native code `arm64-v8a`.

Expected native libraries include:

```text
lib/arm64-v8a/libnativeBoardSDK.so
lib/arm64-v8a/libtensorflowlite.so
lib/arm64-v8a/libil2cpp.so
lib/arm64-v8a/libunity.so
```

The archive must contain exactly one unnumbered copy of each Unity runtime library. Files such as `libil2cpp 2.so` indicate stale Bee output and fail inspection. The repository's `BoardRacingBuild.BuildAndroidDevelopment` entry point requests `BuildOptions.CleanBuildCache` to prevent that contamination in scripted verification builds.

To verify reproducibility, delete `Builds/Android`, repeat the procedure, and re-run the inspections. APK hashes may differ between development builds; success is defined by the build result and required package/native metadata, not byte-for-byte output.

## Initial validation evidence

Validated on July 12, 2026 with Unity `2022.3.62f3`, Board SDK `3.3.0`, and Android SDK Platform 33.

- The initial ARM64 development build succeeded after Android Platform 33 was installed.
- The generated APK used package `com.wholestudios.boardracing`, API 33 minimum/target, and only `arm64-v8a` native code.
- `libnativeBoardSDK.so` and `libtensorflowlite.so` were present alongside Unity's IL2CPP libraries.
- After deleting the complete `Builds/Android` directory, a second build succeeded and passed the same inspections.
- The clean-rebuild APK SHA-256 was `fab0445b4a9d205d34af2d167cb36540751fd2563eecd9d38f843618f6aa6f47` for this validation run. This is evidence, not a pinned artifact checksum.
- No release keystore, release signing, deployment, or release automation was introduced.
