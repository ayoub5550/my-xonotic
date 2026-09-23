using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Parsed subset of a Q3-style material script (scripts/*.shader).
    /// Only the keys the importer needs; everything else is ignored.
    /// </summary>
    public sealed class MaterialScript
    {
        public string Name;
        public string EditorImage;
        public readonly List<string> SurfaceParms = new List<string>();
        public readonly List<StageInfo> Stages = new List<StageInfo>();
        public string SkyEnv;          // skyParms <env/name> ...
        public bool CullNone;
        public bool PolygonOffset;
        /// dev.18: DarkPlaces water/refraction (dp_water, dp_refract) — rendered translucent here.
        public bool WaterLike;

        public sealed class StageInfo
        {
            public string Map;
            public string BlendFunc;   // raw tokens joined by space
            public string AlphaFunc;
            /// dev.18: tcMod scroll s t (texture units per second), zero when absent.
            public UnityEngine.Vector2 TcModScroll;
            public bool UsesLightmap => Map == "$lightmap";
        }

        public bool Has(string parm) => SurfaceParms.Contains(parm, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Locates upstream Xonotic content (textures, material scripts, external
    /// lightmaps, skyboxes) on the local disk. Roots, in priority order, come
    /// from XONOTIC_CONTENT_ROOTS (path-separator separated) or default to
    /// ExternalContent/maps, ExternalContent/data (extracted upstream pk3s,
    /// git-ignored) and ThirdParty/Xonotic/maps-pk3 (bounded resources in Git).
    /// Nothing here executes script content; scripts are parsed as data only.
    /// </summary>
    public sealed class XonoticContentResolver
    {
        static readonly string[] ImageExtensions = { ".tga", ".jpg", ".jpeg", ".png", ".dds" };

        public readonly List<string> Roots = new List<string>();
        readonly Dictionary<string, MaterialScript> _scripts = new Dictionary<string, MaterialScript>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, MaterialScript> Scripts => _scripts;

        static readonly string[] DefaultCandidates =
            { "ExternalContent/maps", "ExternalContent/data", "ThirdParty/Xonotic/maps-pk3", "ThirdParty/Xonotic/data" };

        public XonoticContentResolver()
        {
            // XONOTIC_CONTENT_ROOTS, when set, is searched FIRST (it is
            // normally a full/unbounded local extraction of the upstream
            // packs, e.g. a downloaded xonotic-data pk3), but the default
            // candidates are always appended after it rather than replaced.
            // ThirdParty/Xonotic/maps-pk3 is the bounded set actually
            // committed to this repo, so a machine with no env var set (or
            // one that only points at a partial extraction) still resolves
            // whatever the committed pack covers instead of silently
            // finding nothing.
            var env = Environment.GetEnvironmentVariable("XONOTIC_CONTENT_ROOTS");
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(env))
            {
                candidates.AddRange(env.Split(Path.PathSeparator));
            }
            foreach (var d in DefaultCandidates)
            {
                if (!candidates.Contains(d, StringComparer.OrdinalIgnoreCase)) candidates.Add(d);
            }
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var full = Path.GetFullPath(c.Trim());
                if (Directory.Exists(full) && !Roots.Contains(full)) Roots.Add(full);
            }
            LoadScripts();
        }

        void LoadScripts()
        {
            // Later roots have lower priority; only fill names not yet defined.
            foreach (var root in Roots)
            {
                var dir = Path.Combine(root, "scripts");
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*.shader").OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        foreach (var script in ParseScriptFile(File.ReadAllText(file)))
                            if (!_scripts.ContainsKey(script.Name)) _scripts[script.Name] = script;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[XonoticContentResolver] failed to parse " + file + ": " + e.Message);
                    }
                }
            }
        }

        public MaterialScript GetScript(string name)
        {
            MaterialScript s;
            return _scripts.TryGetValue(name, out s) ? s : null;
        }

        /// <summary>Find any content file by exact relative path (e.g. "models/foo.md3"); null when absent.</summary>
        public string FindFile(string contentPath)
        {
            if (string.IsNullOrEmpty(contentPath)) return null;
            contentPath = contentPath.Replace('\\', '/');
            foreach (var root in Roots)
            {
                var exact = Path.Combine(root, contentPath);
                if (File.Exists(exact)) return exact;
            }
            return null;
        }

        /// <summary>Find an image for a bare content path such as "textures/exx/base-metal01" (no extension).</summary>
        public string FindImage(string contentPath)
        {
            if (string.IsNullOrEmpty(contentPath)) return null;
            contentPath = contentPath.Replace('\\', '/');
            var withoutExt = Path.ChangeExtension(contentPath, null);
            foreach (var root in Roots)
            {
                var exact = Path.Combine(root, contentPath);
                if (File.Exists(exact) && ImageExtensions.Contains(Path.GetExtension(exact).ToLowerInvariant())) return exact;
                foreach (var ext in ImageExtensions)
                {
                    var p = Path.Combine(root, withoutExt + ext);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        /// <summary>
        /// Resolve the diffuse image for a BSP shader name: the literal texture file,
        /// otherwise the first non-lightmap stage map of its script, otherwise
        /// the editor image. Returns null when nothing is found.
        /// </summary>
        public string ResolveDiffuse(string shaderName, out MaterialScript script)
        {
            script = GetScript(shaderName);
            var direct = FindImage(shaderName);
            if (script == null) return direct;

            foreach (var stage in script.Stages)
            {
                if (stage.UsesLightmap || string.IsNullOrEmpty(stage.Map)) continue;
                if (stage.Map.StartsWith("$")) continue;
                var img = FindImage(stage.Map);
                if (img != null) return img;
            }
            if (direct != null) return direct;
            if (!string.IsNullOrEmpty(script.EditorImage)) return FindImage(script.EditorImage);
            return null;
        }

        /// <summary>External lightmap maps/&lt;map&gt;/lm_XXXX.(tga|jpg|png).</summary>
        public string FindExternalLightmap(string mapName, int index)
        {
            return FindImage(string.Format("maps/{0}/lm_{1:D4}", mapName, index));
        }

        /// <summary>Six sky images env/&lt;name&gt;_{rt,lf,ft,bk,up,dn}. Returns null if any side is missing.</summary>
        public Dictionary<string, string> FindSkybox(string envBase)
        {
            var result = new Dictionary<string, string>();
            foreach (var side in new[] { "rt", "lf", "ft", "bk", "up", "dn" })
            {
                var p = FindImage(envBase + "_" + side);
                if (p == null) return null;
                result[side] = p;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Minimal tokenizer for the brace-structured script format.
        // ------------------------------------------------------------------
        /// dev.18 test alias for <see cref="ParseScriptFile"/>.
        public static List<MaterialScript> ParseShaderScripts(string text) => ParseScriptFile(text);

        public static List<MaterialScript> ParseScriptFile(string text)
        {
            var tokens = Tokenize(text);
            var result = new List<MaterialScript>();
            int i = 0;
            while (i < tokens.Count)
            {
                var name = tokens[i++];
                // A bare newline at top level (blank line between shader
                // blocks, or the near-universal style where the shader name
                // is on its own line and "{" is on the next one) is not a
                // candidate name — skip it instead of letting it become one
                // (which previously made every such shader's Name literally
                // "\n", so real lookups by content path never matched and
                // every subsequent same-name definition silently overwrote
                // the dictionary slot: this made essentially all real
                // Xonotic material scripts unresolvable).
                if (name == "{" || name == "}" || name == "\n") continue;
                while (i < tokens.Count && tokens[i] == "\n") i++;
                if (i >= tokens.Count || tokens[i] != "{") continue;
                i++; // consume {
                var script = new MaterialScript { Name = name };
                int depth = 1;
                MaterialScript.StageInfo stage = null;
                var line = new List<string>();
                while (i < tokens.Count && depth > 0)
                {
                    var t = tokens[i++];
                    if (t == "{")
                    {
                        FlushLine(script, stage, line);
                        depth++;
                        if (depth == 2) stage = new MaterialScript.StageInfo();
                        continue;
                    }
                    if (t == "}")
                    {
                        FlushLine(script, stage, line);
                        depth--;
                        if (depth == 1 && stage != null) { script.Stages.Add(stage); stage = null; }
                        continue;
                    }
                    if (t == "\n") { FlushLine(script, stage, line); continue; }
                    line.Add(t);
                }
                result.Add(script);
            }
            return result;
        }

        static void FlushLine(MaterialScript script, MaterialScript.StageInfo stage, List<string> line)
        {
            if (line.Count == 0) return;
            var key = line[0].ToLowerInvariant();
            if (stage == null)
            {
                switch (key)
                {
                    case "qer_editorimage": if (line.Count > 1) script.EditorImage = line[1]; break;
                    case "surfaceparm": if (line.Count > 1) script.SurfaceParms.Add(line[1].ToLowerInvariant()); break;
                    case "skyparms": if (line.Count > 1 && line[1] != "-") script.SkyEnv = line[1]; break;
                    case "cull":
                        if (line.Count > 1)
                        {
                            var v = line[1].ToLowerInvariant();
                            script.CullNone = v == "none" || v == "disable" || v == "twosided";
                        }
                        break;
                    case "polygonoffset": script.PolygonOffset = true; break;
                    case "dp_water":
                    case "dp_refract":
                        script.WaterLike = true; break; // (dp_reflect / dpreflectcube are opaque reflections, not translucency)
                }
            }
            else
            {
                switch (key)
                {
                    case "map":
                    case "clampmap":
                        if (line.Count > 1) stage.Map = line[1];
                        break;
                    case "animmap":
                        if (line.Count > 2) stage.Map = line[2];
                        break;
                    case "blendfunc": stage.BlendFunc = string.Join(" ", line.Skip(1)).ToUpperInvariant(); break;
                    case "alphafunc": stage.AlphaFunc = string.Join(" ", line.Skip(1)).ToUpperInvariant(); break;
                    case "tcmod":
                        if (line.Count > 3 && line[1].ToLowerInvariant() == "scroll")
                        {
                            float s, t;
                            if (float.TryParse(line[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s) &&
                                float.TryParse(line[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out t))
                                stage.TcModScroll = new UnityEngine.Vector2(s, t);
                        }
                        break;
                }
            }
            line.Clear();
        }

        static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\n') { tokens.Add("\n"); i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    continue;
                }
                if (c == '{' || c == '}') { tokens.Add(c.ToString()); i++; continue; }
                if (c == '"')
                {
                    int end = text.IndexOf('"', i + 1);
                    if (end < 0) end = text.Length;
                    tokens.Add(text.Substring(i + 1, end - i - 1));
                    i = end + 1;
                    continue;
                }
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}') i++;
                tokens.Add(text.Substring(start, i - start));
            }
            return tokens;
        }
    }
}
