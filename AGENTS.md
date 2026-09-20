# xonotic-android — developer and agent handoff

## Read this first: what this is (2026-09-20)

**Goal:** the *original* Xonotic 0.8.6 running on Android — not a remake.
**Approach:** compile the real game engine (DarkPlaces, C, GPLv2+) for ARM64 with the
Android NDK, host it in an SDL2 `SDLActivity`, and load the official Xonotic 0.8.6
`.pk3` data (GPL) downloaded once from `dl.xonotic.org` at first launch.
Everything the desktop game has (all maps, weapons, bots, game modes, menus,
QuakeC gameplay, multiplayer) comes for free because it *is* the same engine + data.

This replaces the earlier Unity re-implementation attempt in `ayoub5550/my-xonotic`
(kept as a separate MIT project; do not mix code between the two).

| Gate | Current result |
|---|---|
| Engine + deps cross-compile (arm64-v8a) | PASS: `libmain.so` 5.6 MB, `libSDL2.so`, `libpng.so` |
| Gradle APK (release, debug-signed) | PASS: `app-release.apk` ≈ 3.3 MB (data is not bundled) |
| Host Linux build of the same engine boots Xonotic 0.8.6 data | see CHANGELOG |
| Run on a real ARM64 phone | NOT VERIFIED — owner must test; report logcat |
| Touch controls usable | first pass only (engine's built-in `vid_touchscreen` layout) |

## Layout

```
darkplaces/         upstream engine source, vendored from gitlab.com/xonotic/darkplaces @ d93f9c4 (patched)
native/             CMake project that builds SDL2, libpng, ogg/vorbis, freetype, DarkPlaces
native/dp_sources.txt      engine source list (from makefile.inc OBJ_SDL + capture)
native/darkplaces-android.patch   ALL engine modifications — re-apply after re-cloning
deps/               third-party sources, fetched by tools/fetch_deps.sh (SDL2 2.30.11, libjpeg-turbo 3.0.4,
                    libpng 1.6.44, libogg 1.3.5, libvorbis 1.3.7, freetype 2.13.3)
android/            Gradle project (AGP 7.4.2, Gradle 7.5.1 from Unity's Android tools)
  app/src/main/java/org/libsdl/app/    SDL's Java glue (unmodified from SDL 2.30.11)
  app/src/main/java/com/ayoub/xonotic/ LauncherActivity (data download), XonoticActivity, GameData
  app/src/main/jniLibs/arm64-v8a/      staged .so files (built, not committed)
  app/src/main/assets/zz-xonotic-android-touch.pk3   generated touch icons + android.cfg
tools/make_touch_pk3.py   generates that pk3 (PIL)
build_native.sh     cmake/ninja build of all native libs → jniLibs
build_apk.sh        gradle assembleRelease via Unity's bundled Gradle launcher
env.sh              sandbox toolchain paths (NDK r23b, SDK 34/35/36, OpenJDK 11)
```

## Engine patch summary (`native/darkplaces-android.patch`, base commit d93f9c4)

1. `glquake.h`: on `__ANDROID__` include `<GLES3/gl32.h>` + `<GLES2/gl2ext.h>` instead of
   `SDL_opengles2.h`; map `*_ARB` debug enums, `GL_BGRA`, `GL_READ_ONLY`, `GLAPIENTRY`;
   `glDebugMessage*ARB` → core names; `glMapBuffer` → NULL (only video capture uses it).
2. `vid_sdl.c`: request an **OpenGL ES 3.0** context (backend uses PBOs/framebuffer
   targets); fix `DP_MOBILETOUCH` writing to a `const` mode struct (upstream bit-rot).
3. `gl_textures.c`: Steelstorm KTX/ETC1 path behind `DP_USE_KTX` (needs libktx we don't ship).
4. `gl_rmain.c`: Steelstorm shader prewarm behind `DP_STEELSTORM_PREWARM`.

Compile defines: `LINK_TO_LIBJPEG CONFIG_MENU CONFIG_VIDEO_CAPTURE _FILE_OFFSET_BITS=64`.
`sys.h` already sets for Android: `USE_GLES2 USE_RWOPS LINK_TO_ZLIB LINK_TO_LIBVORBIS
DP_MOBILETOUCH DP_FREETYPE_STATIC`. libpng stays dynamic: the engine `dlopen("libpng.so")`,
so the shared libpng is shipped as `libpng.so` and pre-loaded from Java.

## Runtime layout on the phone

- basedir = `Android/data/com.ayoub.xonotic/files/` → engine reads `files/data/*.pk3`.
- userdir = `files/userdata/` (config.cfg, screenshots).
- Arguments (XonoticActivity.getArguments): `-xonotic -basedir … -userdir …
  +vid_touchscreen 1 +vid_fullscreen 1 +vid_conwidth 1024 +vid_conheight 576 +exec android.cfg`.
- LauncherActivity streams `xonotic-0.8.6.zip` (1.24 GB) and extracts only the pk3s in
  `GameData.REQUIRED/OPTIONAL` (skips nexcompat, saves 125 MB). Users may instead copy
  pk3s manually into `files/data/`.

## Build from scratch

```sh
. ./env.sh                 # or export ANDROID_NDK / JAVA_HOME / GRADLE_JAR yourself
tools/fetch_deps.sh        # third-party sources (pinned + sha256)
./build_native.sh          # ≈ 3 min on 17 cores
./build_apk.sh             # ≈ 1 min; output android/app/build/outputs/apk/release/app-release.apk
```
Fresh engine checkout: `git clone https://gitlab.com/xonotic/darkplaces && cd darkplaces &&
git checkout d93f9c4 && git apply ../native/darkplaces-android.patch`.

## Rules (owner)

- Local builds only; no paid services, no cloud build.
- Owner-facing docs in Arabic (README, CHANGELOG); code and this handoff in English.
- Never commit APKs, keystores, credentials, or the 1 GB game data. Third-party sources
  in `deps/` are upstream tarball contents (MIT/BSD/zlib/FTL/IJG); the engine and game data
  are GPLv2+ — the app is GPL, keep `COPYING` and give source with binaries.
- Document every increment in AGENTS.md + CHANGELOG.md; owner tests on device, reports
  bugs, we release the next version (same loop as my-librequake).

## Known gaps / next actions

1. Get first device report (does it reach the menu? logcat tag `SDL`, `Xonotic`).
2. Touch layout: the engine's Quake layout is minimal (move/aim sticks, jump, fire, alt,
   menu). Add weapon switching, zoom, crouch, and make sizes DPI-aware (`vid_touchscreen_density`).
3. Performance presets for phones (Xonotic "low" preset via `android.cfg`; `r_shadow_*` off).
4. Optional: bundle data as OBB / play-asset-delivery; music pk3 (110 MB) currently included.
5. Keyboard for console/chat: SDL text input already wired via `vid_touchscreen_showkeyboard`.
6. Consider building for `armeabi-v7a` too if the owner needs old devices (32-bit).
