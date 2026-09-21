# Source and content provenance

## Original source in this repository

Unless a file states otherwise, the C# and Python code and generated development
arena are original work for this project, licensed under `LICENSE` (MIT).
No DarkPlaces engine implementation or Xonotic QuakeC was copied into the runtime.
This does **not** make Xonotic's original game code or assets MIT.

`my-librequake` was an engineering reference for the owner's Unity version,
local-build workflow, handoff structure and previously encountered Unity pitfalls.
It was not renamed and copied as Xonotic. No LibreQuake game assets are bundled.

## Unity

Unity Editor and runtime are proprietary software under Unity's applicable terms.
Install the Editor and its Android modules locally. Do not redistribute the Editor,
activation files or the owner's account details with this repository.
The UGUI package and Unity modules retain their package-specific notices.

## Xonotic / DarkPlaces

Primary sources consulted on 2026-09-20:

- [Xonotic project README](https://gitlab.com/xonotic/xonotic/-/blob/master/README.md)
  states GPL version 3 or later.
- [Xonotic COPYING](https://gitlab.com/xonotic/xonotic/-/blob/master/COPYING)
  grants GPLv3-or-later for source files in scope; the DarkPlaces engine is
  GPLv2-or-later. Some parts have additional licence options.
- The `Xonotic/COPYING` member of the official
  [0.8.6 release archive](https://dl.xonotic.org/xonotic-0.8.6.zip)
  was read directly and makes the same distinction.
- Individual assets may include their own notices; for example
  [Atelier's map notice](https://gitlab.com/xonotic/xonotic-maps.pk3dir/-/blob/master/maps/atelier.license.txt).

Actual upstream originals, editable source and licence records are committed
under `ThirdParty/Xonotic/` at the owner's request. That directory is a separately
licensed aggregate, outside Unity Assets; it is not covered by our MIT licence
and is not included in the default original-fixture APK build.
The per-file provenance, preserved author notices and known source-review gaps
are documented in [its README](ThirdParty/Xonotic/README.md) and manifests.
Copyright, attribution and corresponding-source obligations remain applicable.
A parser accepting a file or an asset being present in Git is not a distribution
clearance.

Specific notices preserved include Philip Klevestav's GPLv2-or-later texture
notice, Cuinn (cuinnton) Herrick's sky attribution, and Georges “TRaK” Grondin's
CC BY 3.0 (TRaK4 source pack) and MIT (TRaK5 source pack) grants.
Do not silently replace these notices with this project's MIT or a blanket GPL
label. Source-file presence does not prove exact release-export correspondence.

**Distribution gate:** obtain an asset-by-asset licence review and resolve how
copyleft obligations interact with the proposed Unity distribution before shipping
upstream content. This is a conservative engineering gate, not legal advice and
not a claim that all GPL assets can or cannot be used with Unity.

This repository is an independent development project. It is not endorsed by or
an official Android release from Team Xonotic. Upstream resources retain their
original artwork/credits; they are not our invented brand or claim of endorsement.

## Verification maps (included unmodified with source)

Two official release members were read through bounded HTTPS byte ranges for
parser validation. ZIP member CRC32 was checked during extraction; **the full
1.24 GB release was not downloaded and its whole-archive SHA512 was not checked**.

| Member in `xonotic-20230620-maps.pk3` | Bytes | SHA256 |
|---|---:|---|
| `maps/_hudsetup.bsp` | 347552 | `d358a3cd897a13f322e271423a6039188026a117082664ba3b3ccde547a561e8` |
| `maps/boil.bsp` | 1923316 | `5edd1224e89eb9d31cfd8a05cb532546d6f5a43e438078f25234b8f2011093fb` |

Both have the `IBSP` magic and little-endian version `46`, directly verified from
the files. This is not evidence that every community map uses the same format.
