using UnityEngine;
using UnityEngine.AI;

namespace MyXonotic
{
    /// <summary>
    /// dev.16: NavMesh path following for bots. The bot keeps its own Xonotic
    /// movement (CharacterController + PM_Accelerate); the navigator only
    /// answers "which way should I push right now to reach the goal?".
    /// Xonotic bots walk a waypoint graph (qcsrc/server/bot/default/navigation.qc);
    /// Unity's NavMesh plays that role here.
    /// </summary>
    public sealed class BotNavigator
    {
        /// bot_ai_thinkinterval 0.05 * (10 - skill) is how often Xonotic bots re-think
        /// navigation; we repath at most every RepathInterval seconds (cheap on mobile).
        public const float RepathInterval = 0.5f;
        /// A corner counts as reached within this horizontal distance (agent radius).
        public const float CornerReach = 0.6f;
        /// Corners higher than the step height need a jump.
        public const float JumpStep = 31f / 32f;
        /// bot_ai_ignoregoal_timeout 3: give up a goal after being stuck this long.
        public const float IgnoreGoalTimeout = 3f;
        /// NavMesh sampling radius when snapping goals/positions onto the mesh.
        public const float SampleRadius = 4f;

        // Created lazily: NavMeshPath cannot be constructed from a MonoBehaviour field initializer.
        NavMeshPath _pathStore;
        NavMeshPath _path => _pathStore ?? (_pathStore = new NavMeshPath());
        Vector3 _goal;
        bool _hasGoal;
        float _nextRepath;
        int _corner;

        public bool HasPath => _hasGoal && _path.status != NavMeshPathStatus.PathInvalid && _path.corners.Length > 1;
        public bool GoalUnreachable { get; private set; }
        public Vector3 Goal => _goal;
        public bool HasGoal => _hasGoal;
        public int CornerCount => _path.corners.Length;

        public void Clear()
        {
            _hasGoal = false;
            GoalUnreachable = false;
            _corner = 0;
            _nextRepath = 0f;
            _path.ClearCorners();
        }

        /// Set (or move) the goal. Repaths immediately when the goal changed by more than a metre.
        public void SetGoal(Vector3 goal)
        {
            if (_hasGoal && (goal - _goal).sqrMagnitude < 1f) { _goal = goal; return; }
            _goal = goal;
            _hasGoal = true;
            _nextRepath = 0f;
            _corner = 0;
        }

        /// <summary>
        /// Horizontal wish direction (unit or zero) towards the goal from <paramref name="position"/>.
        /// <paramref name="wantJump"/> is set when the next corner is above step height.
        /// Falls back to the straight line when no NavMesh is loaded or no path exists.
        /// </summary>
        public Vector3 Steer(Vector3 position, out bool wantJump)
        {
            wantJump = false;
            if (!_hasGoal) return Vector3.zero;
            if (!MapNavMesh.Available) return Flat(_goal - position);

            if (Time.time >= _nextRepath)
            {
                _nextRepath = Time.time + RepathInterval;
                Repath(position);
            }
            if (_path.status == NavMeshPathStatus.PathInvalid || _path.corners.Length < 2)
                return Flat(_goal - position);

            _corner = AdvanceCorner(_path.corners, _corner, position, CornerReach);
            Vector3 next = _path.corners[_corner];
            wantJump = next.y - position.y > JumpStep && FlatDistance(next, position) < 3f * (next.y - position.y);
            return Flat(next - position);
        }

        void Repath(Vector3 position)
        {
            Vector3 from = position, to = _goal;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, SampleRadius, NavMesh.AllAreas)) from = hit.position;
            if (NavMesh.SamplePosition(_goal, out hit, SampleRadius, NavMesh.AllAreas)) to = hit.position;
            bool ok = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path);
            GoalUnreachable = !ok || _path.status == NavMeshPathStatus.PathInvalid;
            _corner = 0;
        }

        /// Pure helper (tested): index of the first corner not yet reached, skipping
        /// corners that are within <paramref name="reach"/> horizontally. Never returns the start corner 0
        /// when there is a next one — corner 0 is the bot's own position.
        public static int AdvanceCorner(Vector3[] corners, int current, Vector3 position, float reach)
        {
            if (corners == null || corners.Length == 0) return 0;
            int i = Mathf.Clamp(current, 0, corners.Length - 1);
            if (i == 0 && corners.Length > 1) i = 1;
            while (i < corners.Length - 1 && FlatDistance(corners[i], position) <= reach) i++;
            return i;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.zero;
        }
    }
}
