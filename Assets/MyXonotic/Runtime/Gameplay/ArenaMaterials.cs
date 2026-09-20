using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Material fallback chain for the development slice:
    /// 1) Resources-shipped MyXonotic/VertexColor shader;
    /// 2) built-in Standard shader (explicitly pinned by LocalBuild).
    /// Colors used across the slice are original, non-branded debug tints only.
    /// </summary>
    public static class ArenaMaterials
    {
        static Shader _shader;
        static Material _template;
        static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        static Shader ResolveShader()
        {
            if (_shader != null) return _shader;
            _shader = Shader.Find("MyXonotic/VertexColor");
            if (_shader == null) _shader = Shader.Find("Standard");
            return _shader;
        }

        static Material Template()
        {
            if (_template != null) return _template;
            var shader = ResolveShader();
            if (shader == null) throw new System.InvalidOperationException("Development shader not included.");
            _template = new Material(shader);
            return _template;
        }

        public static Material Get(Color color)
        {
            if (Cache.TryGetValue(color, out var cached) && cached != null) return cached;
            var mat = new Material(Template()) { name = "Development tint" };
            if (mat.HasProperty("_Color")) mat.color = color;
            if (mat.HasProperty("_VertexWeight")) mat.SetFloat("_VertexWeight", 0f);
            Cache[color] = mat;
            return mat;
        }

        public static void Release()
        {
            foreach (var material in Cache.Values) if (material != null) Object.Destroy(material);
            Cache.Clear();
            if (_template != null) Object.Destroy(_template);
            _template = null;
            _shader = null;
        }
    }
}
