# Local Build

> Start with **[BUYER-GUIDE.md](../BUYER-GUIDE.md)** — it covers the normal setup path
> (`tools/setup_content.py` + the Unity menu). This page keeps the lower-level details.

## Requirements

- Unity **2022.3.62f3** with a valid license activated locally.
- Android Build Support, along with the compatible Android SDK/NDK and OpenJDK.
- Python **3.10+**, Pillow for DDS conversion, and Mono for standalone C# tests.
- The previous setup used NDK r23b, OpenJDK 11, and Android platform 36.
  Do not assume these are installed on a new machine, and do not treat downloading them as proof that the build succeeded.

The project is built locally only, without Unity Cloud Build or GitHub Actions.
Do not commit license files, passwords, account records, or signing keys to Git.
Logging in to the Unity website does not prove that the editor is activated on the build machine.
Use Unity Hub for normal activation; do not bypass licensing or account-eligibility requirements.


## Checks That Do Not Require Unity

```sh
python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated
python3 -m unittest discover -s tests/python -p 'test_*.py' -v
python3 tools/asset_meta.py
python3 tools/content/verify_resources.py
bash tests/run_all.sh mono mcs \
  ExternalContent/maps/maps/_hudsetup.bsp \
  ExternalContent/maps/maps/boil.bsp
```

Python checks and file inspection do not prove that textures render or that Android gameplay works.
DDS conversion tests require Pillow; check for skipped tests.

## Preparing the Original Map Resources

`python3 tools/setup_content.py` does all of this for you (download, SHA-512 check, safe
extraction, DDS→PNG decoding, environment variables). The manual equivalent, if you already have
an extracted 0.8.6 data tree:

```sh
# <extracted-data> is the data package content root, not the root of the outer ZIP file.
python3 tools/content/prepare_unity_textures.py <extracted-data> --output ExternalContent/decoded \
  --texture models/weapons/laser   # repeat --texture for every skin listed in tools/setup_content.py
export XONOTIC_CONTENT_ROOTS="ExternalContent/decoded:ExternalContent/worlddecoded:ExternalContent/maps:ExternalContent/data"
export XONOTIC_BSP="ExternalContent/maps/maps/boil.bsp"   # single-map development builds only
export XONOTIC_INCLUDE_EXTERNAL=1
```

The original is never relicensed; game data stays in the git-ignored `ExternalContent/`.

## Running Unity and Building

```sh
export UNITY_EDITOR="<local-editor-executable>"
export XONOTIC_REVISION="<exact-source-commit>"
python3 tools/local_unity.py compile
python3 tools/local_unity.py configure
python3 tools/local_unity.py test
python3 tools/local_unity.py sky-test
python3 tools/local_unity.py gameplay-test
python3 tools/local_unity.py gameplay-playtest
python3 tools/local_unity.py original-playtest
python3 tools/local_unity.py android --timeout 3600
```

Without `XONOTIC_INCLUDE_EXTERNAL=1`, the Android command builds the synthetic development arena.
The exception is `XONOTIC_ALL_MAPS=1`, which selects the all-maps package and manifest;
use the appropriate `XONOTIC_VERSION_CODE` as described in the release document.
When enabled, it imports the `XONOTIC_BSP` path and generates the original map resources before
building; the output is named `plasma-verge-boil.apk`; with `XONOTIC_ALL_MAPS=1` the full game is `plasma-verge-full.apk`.

- `test` is an Editor test; `playtest` is a synthetic-arena test;
  `original-playtest` tests the original map.
- Tests without `--graphics` do not prove rendering correctness. Graphics testing requires
  a display or Xvfb and suitable OpenGL support.
- The runner prevents Unity from being launched twice for the project and enforces a timeout.
- `android` and `linux` do not consider a zero exit code sufficient for success: a new receipt is required,
  matching the run ID, target, filename, size, and SHA256.
- The tool preserves the previous build file if a new run fails, but removes the old receipt before starting;
  do not share the old file as a new build.
- A SHA256 receipt proves that the file matches, not that gameplay works or that the APK is signed.
  Verify the signature with `apksigner`, then test installation, rendering, and touch input on a phone.

## Current Android Setup and Release Limitations

`com.ayoub.plasmaverge` (change it in `Editor/LocalBuild.cs`), ARM64/IL2CPP, landscape orientation, OpenGLES3,
min API 26 and target API 36. The signing is for development and is not ready for a store release.
Any change to versionCode or the keystore must be documented.

The map uses approximate combat and movement rules. Not all Xonotic weapons, characters,
animations, AI, game modes, or networking are complete.
Reviewing source and license parity and distribution compatibility with Unity remains separate work.

## Environment Constraints

Check `nproc`, RAM and disk space on the build machine: a full 29-map APK needs ~10 GB of
scratch space and 40–60 minutes on a 16-core CPU (the first compile after a large change can
take 6–12 minutes because of IL2CPP post-processing). Run only one Unity instance per project;
if a headless run is interrupted, delete `Temp/UnityLockfile` and `Artifacts/local-unity.lock`.
