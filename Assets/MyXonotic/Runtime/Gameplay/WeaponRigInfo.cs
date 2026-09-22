using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Generated per weapon by <c>WeaponRigImporter</c> (Editor) from Xonotic's
    /// first-person <c>h_&lt;weapon&gt;</c> model: the animated skeleton
    /// (fire / fire2 / idle / reload clips) as a <see cref="CharacterRig"/>, plus
    /// either an own skinned mesh (DarkPlaces-format models that carry the
    /// whole gun) or the index of the <c>weapon</c> joint the static <c>v_</c>
    /// visual is attached to (IQM skeleton-only models). <c>ShotJoint</c> is the
    /// muzzle (<c>shot</c> / <c>tag_shot</c>) used for the muzzle flash.
    /// Loaded at runtime as <c>Resources/Weapons/&lt;Name&gt;WeaponRig</c>.
    /// </summary>
    public sealed class WeaponRigInfo : ScriptableObject
    {
        public string WeaponName;
        public string SourceFormat;   // "IQM" or "DPM"
        public CharacterRig Rig;
        public Mesh SkinnedMesh;       // null for skeleton-only rigs
        public Material[] Materials;
        public int WeaponJoint = -1;   // attachment joint for the static v_ visual, -1 if none
        public int ShotJoint = -1;     // muzzle joint, -1 if none
        public int HandleJoint = -1;   // DPM "tag_handle" (grip) joint, -1 if none

        public bool HasOwnMesh => SkinnedMesh != null;
    }
}
