# Weapon art — source files, not runtime integration

This directory contains eleven unmodified engine-model/icon/audio files from
the official Xonotic 0.8.6 data snapshot, seven `.blend` files from the official
[Xonotic mediasource repository](https://gitlab.com/xonotic/mediasource),
pinned to `624caa3e2d5eddb011009ea3a7016b406a7b6d65`, plus its original root
and weapon-source README files. The GPL notices in `../LICENSES` apply; these
files are not covered by this project's MIT licence.

| Source family | Included |
|---|---|
| Devastator | world (`g_`), player-held (`h_`) and first-person (`v_`) Blender files |
| Blaster | world (`g_`), player-held (`h_`) and first-person (`v_`) Blender files |
| Projectiles | `newprojectiles2.blend`; its correspondence to the released rocket is unverified |

Read the preserved `mediasource-src/models/weapons/README.txt` before editing.
The upstream Blender files were prepared from SMD/other interchange formats.
For Blaster, upstream explicitly describes importing the exported world model
and importing an OBJ for the view model. These are upstream-provided editable
sources, **not evidence that we recovered pristine original authoring files or
can reproduce every model from the 0.8.6 release**. The source snapshot is newer
than the pinned game-data snapshot.

We have checked file bytes and provenance, not opened the models in Blender or
Unity. Complete model texture dependencies are not supplied with this subset;
the two `*_simple.tga` files are icons, not proven skins for the full models.
Do not enable Blender auto-run scripts on untrusted downloaded files.

## Included exported resources and source-review limits

The eleven exported/final-form files from `xonotic-data.pk3dir` are included,
unmodified, at commit `45df581bba67832b61e5041f4b7d87cf59c80657`
(`xonotic-v0.8.6`): four engine-model files, two simple icon textures and five
weapon/pickup sounds. Their checksums and original URLs are recorded in
`resources-provenance.json`. All retain the upstream licence grant, not MIT.

All four included `.md3`-named files actually begin with the
`INTERQUAKEMODEL\0` magic: they are **IQM**, not MD3. Choose any future importer
by header, not extension. Neither extension nor internal format proves that a
file is the preferred form for modification. A matching weapon name does not prove exact
source correspondence. No corresponding source was confirmed for the rocket
pickup or the selected audio; the origin of recorded versus synthesized audio
was not established. The two icon textures were not separately source-reviewed.
These are explicit review gaps, not a claim that the upstream assets are illegal
or that repository publication establishes complete source or Unity-distribution
compliance. Preserve the source links and original notices with redistribution.
