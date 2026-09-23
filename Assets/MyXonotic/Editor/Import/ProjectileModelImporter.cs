using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.17: imports the original Xonotic projectile models (MD3) into
    /// Resources/Weapons/&lt;Weapon&gt;Projectile prefabs so shots are no longer
    /// tinted spheres. Runs as part of the "weapons" gate (IqmWeaponImporter).
    /// dev.18: the full projectile.qc table — laser.mdl / elaser.mdl / ebomb.mdl /
    /// plasmatrail.mdl are MD3 files under a .mdl name (dispatch by magic, not
    /// extension), hagarmissile.mdl is a real Quake-1 IDPO model read by
    /// <see cref="MyXonotic.Content.Mdl.MdlReader"/> with its embedded palette skin.
    /// No stand-in models remain.
    /// </summary>
    public static class ProjectileModelImporter
    {
        public const string GeneratedRoot = "Assets/MyXonotic/Generated/Weapons";
        public const string ResourcesRoot = "Assets/MyXonotic/Resources/Weapons";
        const string ShaderName = "MyXonotic/Lightmapped";

        public struct Source
        {
            public WeaponType Weapon; public string ContentPath; public bool StandIn;
            /// Prefab base name (default &lt;Weapon&gt;Projectile); Electro's secondary ball is ElectroBallProjectile.
            public string Name;
            public string ResourceName => "Weapons/" + (Name ?? Weapon + "Projectile");
        }

        public static readonly Source[] Sources =
        {
            new Source { Weapon = WeaponType.Devastator, ContentPath = "models/rocket.md3" },
            new Source { Weapon = WeaponType.Mortar,     ContentPath = "models/grenademodel.md3" },
            new Source { Weapon = WeaponType.Minelayer,  ContentPath = "models/mine.md3" },
            new Source { Weapon = WeaponType.Hagar,      ContentPath = "models/hagarmissile.mdl" },
            new Source { Weapon = WeaponType.Blaster,    ContentPath = "models/laser.mdl" },
            new Source { Weapon = WeaponType.Electro,    ContentPath = "models/elaser.mdl" },
            new Source { Weapon = WeaponType.Electro,    ContentPath = "models/ebomb.mdl", Name = "ElectroBallProjectile" },
            new Source { Weapon = WeaponType.Crylink,    ContentPath = "models/plasmatrail.mdl" },
        };

        public static List<string> ImportAll(XonoticContentResolver resolver)
        {
            var notes = new List<string>();
            Directory.CreateDirectory(GeneratedRoot);
            Directory.CreateDirectory(ResourcesRoot);
            foreach (var src in Sources)
            {
                string path = resolver.FindFile(src.ContentPath);
                if (path == null) { notes.Add(src.Weapon + ": missing " + src.ContentPath); continue; }
                byte[] bytes = File.ReadAllBytes(path);
                string name = src.Name ?? src.Weapon + "Projectile";
                MyXonotic.Content.Md3.Md3StaticModel model;
                MyXonotic.Content.Mdl.MdlReader.MdlSkin mdlSkin = null;
                string format;
                if (MyXonotic.Content.Md3.Md3Reader.IsMd3(bytes)) { model = MyXonotic.Content.Md3.Md3Reader.Read(bytes, path); format = "MD3"; }
                else if (IsIqm(bytes)) { model = IqmAsMd3(bytes, path, name); format = "IQM"; }
                else if (MyXonotic.Content.Mdl.MdlReader.IsMdl(bytes))
                {
                    var mdl = MyXonotic.Content.Mdl.MdlReader.Read(bytes, path, name);
                    model = mdl.Model; mdlSkin = mdl.Skin; format = "MDL";
                }
                else { notes.Add(src.Weapon + ": neither MD3, IQM nor MDL " + src.ContentPath); continue; }
                var built = Md3WeaponModelBuilder.Build(model, resolver, name, GeneratedRoot, ShaderName);
                // dev.18: bolt sprites (laser/electro/crylink *_projectile_* scripts) are "blendfunc add" over a
                // black texture — as an opaque material they would be black planes. Honour the script's blend.
                for (int s = 0; s < built.Materials.Length && s < model.Surfaces.Length; s++)
                {
                    var surf = model.Surfaces[s];
                    var script = surf.ShaderNames != null && surf.ShaderNames.Length > 0 ? resolver.GetScript(surf.ShaderNames[0]) : null;
                    var kind = BspImportPipeline.ClassifyBlend(script);
                    var scroll = BspImportPipeline.ScrollOf(script);
                    if (scroll != Vector2.zero && built.Materials[s] != null) { built.Materials[s].SetVector("_Scroll", new Vector4(scroll.x, scroll.y, 0f, 0f)); EditorUtility.SetDirty(built.Materials[s]); }
                    string want = kind == BspImportPipeline.BlendKind.Additive ? "MyXonotic/LightmappedAdd"
                        : kind == BspImportPipeline.BlendKind.Blend ? "MyXonotic/LightmappedBlend" : null;
                    if (want == null || built.Materials[s] == null) continue;
                    var sh = Shader.Find(want);
                    if (sh == null) continue;
                    built.Materials[s].shader = sh;
                    built.Materials[s].SetFloat("_LightMode", 0f);
                    EditorUtility.SetDirty(built.Materials[s]);
                    notes.Add(name + ": surface " + surf.Name + " uses " + want + " (" + (script != null ? script.Name : "?") + ")");
                }
                if (mdlSkin != null && built.Materials.Length > 0)
                {
                    // MDL skins are embedded (8-bit palette) — no content-root texture to resolve.
                    var tex = new Texture2D(mdlSkin.Width, mdlSkin.Height, TextureFormat.RGBA32, false, false) { name = name + "_Skin" };
                    var flipped = new byte[mdlSkin.Rgba.Length];
                    int stride = mdlSkin.Width * 4;
                    for (int row = 0; row < mdlSkin.Height; row++) // MDL rows are top-down, Unity bottom-up
                        System.Buffer.BlockCopy(mdlSkin.Rgba, row * stride, flipped, (mdlSkin.Height - 1 - row) * stride, stride);
                    tex.LoadRawTextureData(flipped);
                    tex.Apply(false, false);
                    var finalTex = ImportedTexturePolicy.Finalize(tex, repeat: true);
                    string texPath = GeneratedRoot + "/" + name + "_Skin.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (existing == null) { AssetDatabase.CreateAsset(finalTex, texPath); existing = finalTex; }
                    else { EditorUtility.CopySerialized(finalTex, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(finalTex); }
                    built.Materials[0].mainTexture = existing;
                    EditorUtility.SetDirty(built.Materials[0]);
                    built.Textures.Clear();
                    built.Textures.Add(new Md3WeaponModelBuilder.SurfaceTextureProvenance { SurfaceName = name, ShaderName = "(embedded MDL skin)", ResolvedTexturePath = path, Resolved = true });
                }

                var go = new GameObject(name);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = built.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = built.Materials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                string prefabPath = ResourcesRoot + "/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);
                int textured = built.Textures.FindAll(t => t.Resolved).Count;
                notes.Add(string.Format("{0}: {1} [{7}] -> {2} ({3} verts, {4}/{5} surfaces textured{6})",
                    src.Weapon, src.ContentPath, prefabPath, built.Mesh.vertexCount, textured, built.Textures.Count,
                    src.StandIn ? ", STAND-IN model" : "", format));
            }
            return notes;
        }

        static bool IsIqm(byte[] bytes) =>
            bytes.Length > 16 && System.Text.Encoding.ASCII.GetString(bytes, 0, 16) == "INTERQUAKEMODEL\0";

        /// rocket.md3 / grenademodel.md3 in the 0.8.6 pack are IQM files despite the extension;
        /// present each IQM mesh as an MD3 surface (same trick as IqmWeaponImporter) so the
        /// shared builder resolves scripts/*.shader materials (RL -> textures/rl_new).
        static MyXonotic.Content.Md3.Md3StaticModel IqmAsMd3(byte[] bytes, string path, string name)
        {
            var meshes = IqmReader.ReadStaticAll(bytes, path, allowAnimated: false);
            var model = new MyXonotic.Content.Md3.Md3StaticModel { Name = name, Surfaces = new MyXonotic.Content.Md3.Md3Surface[meshes.Length] };
            for (int m = 0; m < meshes.Length; m++)
            {
                var im = meshes[m];
                string mat = im.MaterialName ?? "";
                string ext = Path.GetExtension(mat);
                if (ext == ".tga" || ext == ".png" || ext == ".jpg" || ext == ".dds") mat = mat.Substring(0, mat.Length - ext.Length);
                var surf = new MyXonotic.Content.Md3.Md3Surface
                {
                    Name = im.MeshName,
                    ShaderNames = new[] { mat },
                    Positions = new MyXonotic.Content.Bsp.BspVec3[im.Positions.Length],
                    Normals = new MyXonotic.Content.Bsp.BspVec3[im.Normals.Length],
                    TexCoords = new MyXonotic.Content.Bsp.BspVec2[im.TexCoords.Length],
                    Triangles = im.Triangles
                };
                for (int v = 0; v < im.Positions.Length; v++) surf.Positions[v] = new MyXonotic.Content.Bsp.BspVec3(im.Positions[v].x, im.Positions[v].y, im.Positions[v].z);
                for (int v = 0; v < im.Normals.Length; v++) surf.Normals[v] = new MyXonotic.Content.Bsp.BspVec3(im.Normals[v].x, im.Normals[v].y, im.Normals[v].z);
                for (int v = 0; v < im.TexCoords.Length; v++) surf.TexCoords[v] = new MyXonotic.Content.Bsp.BspVec2(im.TexCoords[v].x, im.TexCoords[v].y);
                model.Surfaces[m] = surf;
            }
            return model;
        }
    }
}
