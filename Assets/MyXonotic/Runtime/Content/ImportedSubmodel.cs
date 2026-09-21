using UnityEngine;

namespace MyXonotic.Content
{
    /// <summary>
    /// Marks a visible inline brush submodel (func_wall, func_door, ...) that
    /// the import pipeline placed as static geometry at its BSP rest position.
    /// Kept as a data marker so a future mover system can find and animate
    /// these pieces without re-reading the BSP.
    /// </summary>
    public sealed class ImportedSubmodel : MonoBehaviour
    {
        public string classname;
        public int modelIndex;
        public string targetName;
    }
}
