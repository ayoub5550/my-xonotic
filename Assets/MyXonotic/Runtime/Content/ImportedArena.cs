using UnityEngine;

namespace MyXonotic.Content
{
    /// <summary>
    /// Root marker component placed on the GameObject that
    /// MyXonotic.EditorTools.BspImportPipeline.Import() returns. Identifies
    /// where the arena's geometry came from and surfaces every diagnostic
    /// collected while importing it (unsupported surfaces, missing
    /// textures/entities, skipped patches, etc.) so a designer can see them
    /// in the Inspector instead of only in the console.
    /// </summary>
    public sealed class ImportedArena : MonoBehaviour
    {
        [Tooltip("Original .bsp file name (or PK3-relative path) this arena was imported from.")]
        public string sourceName;

        [Tooltip("Non-fatal diagnostics collected during import: unsupported surfaces, missing shaders/entities, skipped patches, etc.")]
        public string[] warnings = System.Array.Empty<string>();

        /// <summary>Number of original MD3 map decorations placed by BspMapModelImporter.</summary>
        public int mapModelCount;
    }
}
