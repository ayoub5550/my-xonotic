# xonotic-android — developer and agent handoff

## Read this first: what this is (2026-09-20)

**Current increment: 0.1.1 offline APK.** Owner rejected first-run downloads and requires
one APK with all resources. `tools/bundle_data.py` stages all seven official release PK3s
(including music, nexcompat, xoncompat) plus the touch PK3 and a SHA-256 manifest.
Launcher now verifies/copies these assets locally, with atomic replacement, corruption
repair, partial-file cleanup, storage checks, and retry. No HTTP downloader remains.
Manifest/data staging is ignored by Git. Run bundling after regenerating touch assets.
Java installer tests are in `tools/OfflineInstallerTest.java`; compile with the pure-Java
`OfflineInstaller.java` and run `com.ayoub.xonotic.OfflineInstallerTest`.
Phone: owner uses POCO F3 and reported unspecified defects in 0.1.0. Android runtime
remains unverified. Host Linux execution is NOT proof of APK execution.
Local emulator attempts reached ADB but did not complete stable Android boot; stopped
at owner's request. Do not claim screenshots extracted from APK assets are runtime shots.

**Goal:** the *original* Xonotic 0.8.6 running on Android — not a remake.
**Approach:** compile the real game engine (DarkPlaces, C, GPLv2+) for ARM64 with the
Android NDK, host it in an SDL2 `SDLActivity`, and load the official Xonotic 0.8.6
`.pk3` data (GPL) bundled in the APK and copied locally at first launch.
Original maps, weapons, bots, game modes, menus and QuakeC are retained as upstream data.
Their presence is not proof that all features work correctly on Android.

This replaces the earlier Unity re-implementation attempt in `ayoub5550/my-xonotic`
(kept as a separate MIT project; do not mix code between the two).

| Gate | Current result |
|---|---|
| Engine + deps cross-compile (arm64-v8a) | PASS: `libmain.so` 5.6 MB, `libSDL2.so`, `libpng.so` |
| Gradle APK (release, debug-signed) | PASS 0.1.1: 1,191,463,450 bytes, v2 signature verified, same certificate as 0.1.0 |
| Offline contents / installer | PASS: eight manifest SHA-256 checks on embedded files; 26 Java installer assertions |
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
  app/src/main/java/com/ayoub/xonotic/ LauncherActivity (local setup), XonoticActivity, GameData, OfflineInstaller
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
   Quake touch pseudo-mouse coordinates now use normalized 0..1, not 0..32768.
   Added crouch/zoom/next/previous controls with release on leaving gameplay.
   Explicit android.cfg bindings avoid upstream SHIFT=crouch and prevent move/aim
   stick presses from triggering MOUSE4=weaplast / MOUSE5=hook.
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
- LauncherActivity copies bundled `assets/game/*.pk3` locally, verifying lengths and
  SHA-256 against `game-manifest.tsv`. Valid files are hashed but not copied again.
  No downloads or manual PK3 copying. INTERNET permission remains for multiplayer.

## Build from scratch

```sh
. ./env.sh                 # or export ANDROID_NDK / JAVA_HOME / GRADLE_JAR yourself
tools/fetch_deps.sh        # third-party sources (pinned + sha256)
./build_native.sh          # ≈ 3 min on 17 cores
python3 tools/bundle_data.py /path/to/xonotic-0.8.6.zip
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
2. Touch layout now includes weapon switching, zoom and crouch. Verify real multitouch,
   size and placement on POCO F3. Quake layout still uses fixed console coordinates;
   the density cvar affects SteelStorm layout, not this one.
3. Performance presets for phones (Xonotic "low" preset via `android.cfg`; `r_shadow_*` off).
4. Single APK is required by owner: do not reintroduce OBB / external downloads.
5. Keyboard for console/chat: SDL text input already wired via `vid_touchscreen_showkeyboard`.
6. Consider building for `armeabi-v7a` too if the owner needs old devices (32-bit).
7. Delivery blocked: Drive upload returned storageQuotaExceeded; Slack upload of the
   single offline APK returned HTTP 400. Owner asked to free Drive space or create
   xonotic-android GitHub repo. Do not delete their files or split APK without consent.
   Artifact `/work/temp/xonotic-android-0.1.1-offline.apk`; receipt in docs/.
   Private Drive folder already created: `1DshQ0J0SgwfrEteNxZvqxTfTjB8vbKFx`.
