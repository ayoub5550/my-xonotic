# Texture authoring-source intake

Five unmodified archive members are included: three layered PSD files and two
original author/licence notices. They came from official `xonotic/mediasource`
commit `624caa3e2d5eddb011009ea3a7016b406a7b6d65`.

| Archive | Bytes | Verified Git blob SHA-1 |
|---|---:|---|
| [textures/trak4-source.zip](https://gitlab.com/xonotic/mediasource/-/raw/624caa3e2d5eddb011009ea3a7016b406a7b6d65/textures/trak4-source.zip) | 46950395 | `9e36703575520f9fff5715ae41f01ba76d10cac4` |
| [textures/trak5-src.zip](https://gitlab.com/xonotic/mediasource/-/raw/624caa3e2d5eddb011009ea3a7016b406a7b6d65/textures/trak5-src.zip) | 82395016 | `1b109499de930a5e1608743092b4a0bda183f1e8` |

Both archive blob IDs were checked against the pinned GitLab tree and recomputed
as `sha1("blob <length>\0" + archive_bytes)`. Parent review independently
recomputed both and compared every extracted file byte-for-byte with its archive
member. ZIP CRC was verified while reading the selected members; content was not
executed. The original archives are not duplicated in this repository.

Archive SHA256:
- trak4: `9e4fdb0e6e06bec9674efcd996164922d5405842f70f92265bf9bcbe800c6dae`
- trak5: `32bc687db712ba053152f1fd3502e5456a0eb2ae953b68bd81436cc3417548ae`

## Candidate source-to-game relationships

| In-game texture family | Included source | Evidence boundary |
|---|---|---|
| `trak4x/decal-theater2` | `trak4/trak4-src/theater2.psd` | Name-family match; archive contains both theater1 and theater2. No pixel/export comparison performed. |
| `trak5x/base/base_holes1b` | `trak5/src/holes1.psd` | Plausible name-family match; exact variant regeneration not verified. |
| `trak5x/light/light_light3a` | `trak5/src/light3.psd` | Plausible name-family match; exact variant regeneration not verified. |
| `trak5x/base/base_base1b` | Not found in these two archive listings | Do not invent a matching PSD. |
| `trak5x/misc-glass` | Not found in these two archive listings | Do not invent a matching PSD. |

Presence of genuine upstream PSDs is verified; exact release-texture
correspondence is not. Per-file hashes/member paths are in the top-level
`resource-index.json`. No raster was relabelled as a layered source.

## Preserved licence notices

- `trak4/trak4-readme.txt`: Georges “TRaK” Grondin, **CC BY 3.0**, including
  the explicit statement that the PSD files use the same licence. Attribution
  and [licence link](https://creativecommons.org/licenses/by/3.0/) must remain.
  Its kitchen/CGTextures caveat concerns files not included here.
- `trak5/textures-trak5.txt`: Georges “TRaK” Grondin, **MIT**, copyright 2009,
  with original source credits and full permission text.

Do not replace these specific notices with an indiscriminate GPL or project-MIT
label. These are author-source pack licences; they do not silently relicense
modified Xonotic shaders or establish the provenance of every downstream variant.
