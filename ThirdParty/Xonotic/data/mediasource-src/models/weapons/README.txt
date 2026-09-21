The .blend files in this directory were created from the .smd files so that they are easily editable and ready to be exported.

They were created with Blender 2.79b.


NOTE ABOUT TEXTURES IN BLENDER:

- To load Blaster model textures, you should load them in the same directory where .blend file resides.
- To load Vaporizer model textures, you should load them in the same directory where .blend file resides.
- To load Overkill weapon model textures, you should load them in ../../textures/weapons directory.
- The rest of weapon model textures can be loaded correctly by putting them in the ../../textures directory.

If you want to visualize model materials, .blend files should be put in xonotic-data.pk3dir/models/weapons, so that they can load the textures in xonotic-data.pk3dir/textures directory.


IMPORTANT NOTE ABOUT THESE BLENDER FILES:

- Blaster model, about g_laser.blend: 
Imported from g_laser.md3 (which is a IQM) using a IQM/IQE importer addon (from 3d Asset Tools: https://github.com/ccxvii/asstools) 
for Blender 3.1.2 and exporting as FBX to import into Blender 2.79b.
- About h_laser.blend, the new model, Blaster, uses the same animations as the old ones. It's identically the same, even uses old methodology.
- And about v_laser.blend: 
Imported from LaserLowPoly.obj into Blender 2.79b.
- About v_* ones: Devastator, Vortex and Fireball models are exported to SMD and compiled by dpmodel tool to DPM and MD3.
- mortargrenade_mesh.blend is a grenade from Mortar, uses the same weapon textures as Mortar.

- Overkill weapons, about g_ok_shotgun.blend: 
Imported from g_ok_shotgun.md3 using MD3 importer/exporter addon for Blender 2.79b.
