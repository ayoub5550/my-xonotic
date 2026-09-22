using UnityEngine;

namespace MyXonotic.Content
{
    /// <summary>
    /// Import-time marker for item_flag_team1 / item_flag_team2 (CTF flag
    /// stands) at their real map position. <c>MyXonotic.CtfFlag</c> is attached
    /// at runtime when the CTF mode is selected; in other modes the stand is
    /// hidden.
    /// </summary>
    public sealed class CtfFlagBase : MonoBehaviour
    {
        /// 1 = red (team1), 2 = blue (team2).
        public int team;
        public float yaw;
    }
}
