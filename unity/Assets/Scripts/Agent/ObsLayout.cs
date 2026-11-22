using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Observation layout of the low-level policy (interface v3, 59 dims).
    /// Mirrors python/mosquitosim/obs_spec.py; python/tests/test_cs_layout.py
    /// parses this file and fails if the two disagree. Keep "public const int"
    /// lines in the form NAME = value; so the test can read them.
    /// </summary>
    public static class ObsLayout
    {
        public const float MaxSpeed = 1.5f;
        public const float MaxAccel = 5.0f;
        public const float RayLength = 2.0f;
        public const float PosScale = 5.0f;
        public const float HumanVelScale = 2.0f;
        public const float HandVelScale = 8.0f;

        public const int NumSubgoalSlots = 4;

        // Start index of each field.
        public const int SelfVelocity = 0;          // 3
        public const int TargetSkinRelPos = 3;      // 3
        public const int TargetSkinDist = 6;        // 1
        public const int SubgoalTargetRelPos = 7;   // 3
        public const int SubgoalTargetDist = 10;    // 1
        public const int HumanVelocity = 11;        // 3
        public const int HumanAlert = 14;           // 1
        public const int HandRelPos = 15;           // 3
        public const int HandVelocity = 18;         // 3
        public const int HandActive = 21;           // 1
        public const int HazardRelPos = 22;         // 3
        public const int HazardActive = 25;         // 1
        public const int HazardExposure = 26;       // 1
        public const int Rays = 27;                 // 26
        public const int SubgoalOneHot = 53;        // 4
        public const int RemainingTime = 57;        // 1
        public const int Landed = 58;               // 1

        public const int ObsSize = 59;
        public const int NumRays = 26;

        static Vector3[] s_RayDirections;

        /// <summary>
        /// Every (x, y, z) in {-1, 0, 1}^3 except the origin, nested x, y, z
        /// order, normalized. Same order as obs_spec.ray_directions().
        /// </summary>
        public static Vector3[] RayDirections
        {
            get
            {
                if (s_RayDirections != null) return s_RayDirections;
                var dirs = new Vector3[NumRays];
                int i = 0;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    dirs[i++] = new Vector3(x, y, z).normalized;
                }
                s_RayDirections = dirs;
                return dirs;
            }
        }
    }
}
