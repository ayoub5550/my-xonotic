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
    /// Only MD3 sources are supported (bullet.mdl / elaser.mdl / hagarmissile.mdl are
    /// Quake-1 MDL, which this project has no reader for) — Hagar uses tagrocket.md3
    /// as a documented stand-in.
    /// </summary>
    public static class ProjectileModelImporter
    {
        public const string GeneratedRoot = "Assets/MyXonotic/Generated/Weapons";
        public const string ResourcesRoot = "Assets/MyXonotic/Resources/Weapons";
        const string ShaderName = "MyXonotic/Lightmapped";

        public struct Source { public WeaponType Weapon; public string ContentPath; public bool StandIn; }

        public static readonly Source[] Sources =
        {
            new Source { Weapon = WeaponType.Devastator, ContentPath = "models/rocket.md3" },
            new Source { Weapon = WeaponType.Mortar,     ContentPath = "models/grenademodel.md3" },
            new Source { Weapon = WeaponType.Minelayer,  ContentPath = "models/mine.md3" },
            new Source { Weapon = WeaponType.Hagar,      ContentPath = "models/tagrocket.md3", StandIn = true },
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
                string name = src.Weapon + "Projectile";
                MyXonotic.Content.Md3.Md3StaticModel model;
                if (MyXonotic.Content.Md3.Md3Reader.IsMd3(bytes)) model = MyXonotic.Content.Md3.Md3Reader.Read(bytes, path);
                else if (IsIqm(bytes)) model = IqmAsMd3(bytes, path, name);
                else { notes.Add(src.Weapon + ": neither MD3 nor IQM " + src.ContentPath); continue; }
                var built = Md3WeaponModelBuilder.Build(model, resolver, name, GeneratedRoot, ShaderName);

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
                notes.Add(string.Format("{0}: {1} -> {2} ({3} verts, {4}/{5} surfaces textured{6})",
                    src.Weapon, src.ContentPath, prefabPath, built.Mesh.vertexCount, textured, built.Textures.Count,
                    src.StandIn ? ", STAND-IN model" : ""));
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
