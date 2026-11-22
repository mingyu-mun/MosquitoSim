using UnityEngine;

namespace MosquitoSim
{
    /// <summary>Everything the observation needs, in world axes and metres.</summary>
    public struct ObsInput
    {
        public Vector3 Velocity;
        public Vector3 TargetSkinRel;     // target skin centre - mosquito
        public Vector3 SubgoalTargetRel;  // subgoal target point - mosquito
        public Vector3 HumanVelocity;
        public bool HumanAlert;
        public Vector3 HandRel;
        public Vector3 HandVelocity;
        public bool HandActive;
        public Vector3 HazardRel;
        public bool HazardActive;
        public float HazardExposure01;
        public Subgoal Subgoal;
        public float RemainingTime01;
        public bool Landed;
    }

    /// <summary>
    /// Writes the 59-dim observation by index (ObsLayout), so the order does
    /// not depend on call order. Absent elements stay 0.
    /// </summary>
    public static class ObsBuilder
    {
        public static void Build(in ObsInput o, float[] rays01, float[] buf)
        {
            System.Array.Clear(buf, 0, ObsLayout.ObsSize);

            WriteVec(buf, ObsLayout.SelfVelocity, o.Velocity / ObsLayout.MaxSpeed);
            WriteRel(buf, ObsLayout.TargetSkinRelPos, ObsLayout.TargetSkinDist, o.TargetSkinRel);
            WriteRel(buf, ObsLayout.SubgoalTargetRelPos, ObsLayout.SubgoalTargetDist, o.SubgoalTargetRel);

            WriteVec(buf, ObsLayout.HumanVelocity, o.HumanVelocity / ObsLayout.HumanVelScale);
            buf[ObsLayout.HumanAlert] = o.HumanAlert ? 1f : 0f;

            if (o.HandActive)
            {
                WriteVec(buf, ObsLayout.HandRelPos, o.HandRel / ObsLayout.PosScale);
                WriteVec(buf, ObsLayout.HandVelocity, o.HandVelocity / ObsLayout.HandVelScale);
                buf[ObsLayout.HandActive] = 1f;
            }

            if (o.HazardActive)
            {
                WriteVec(buf, ObsLayout.HazardRelPos, o.HazardRel / ObsLayout.PosScale);
                buf[ObsLayout.HazardActive] = 1f;
                buf[ObsLayout.HazardExposure] = Mathf.Clamp01(o.HazardExposure01);
            }

            for (int i = 0; i < ObsLayout.NumRays; i++)
                buf[ObsLayout.Rays + i] = Mathf.Clamp01(rays01[i]);

            buf[ObsLayout.SubgoalOneHot + (int)o.Subgoal] = 1f;
            buf[ObsLayout.RemainingTime] = Mathf.Clamp01(o.RemainingTime01);
            buf[ObsLayout.Landed] = o.Landed ? 1f : 0f;
        }

        /// <summary>Hit distance / RayLength for the 26 directions (1 = no hit).</summary>
        public static void CastRays(Vector3 origin, LayerMask mask, float[] rays01)
        {
            Vector3[] dirs = ObsLayout.RayDirections;
            for (int i = 0; i < ObsLayout.NumRays; i++)
            {
                rays01[i] = Physics.Raycast(origin, dirs[i], out RaycastHit hit, ObsLayout.RayLength,
                    mask, QueryTriggerInteraction.Ignore)
                    ? hit.distance / ObsLayout.RayLength
                    : 1f;
            }
        }

        static void WriteVec(float[] buf, int start, Vector3 v)
        {
            buf[start] = Mathf.Clamp(v.x, -1f, 1f);
            buf[start + 1] = Mathf.Clamp(v.y, -1f, 1f);
            buf[start + 2] = Mathf.Clamp(v.z, -1f, 1f);
        }

        static void WriteRel(float[] buf, int posStart, int distIndex, Vector3 rel)
        {
            WriteVec(buf, posStart, rel / ObsLayout.PosScale);
            buf[distIndex] = Mathf.Min(rel.magnitude / ObsLayout.PosScale, 1f);
        }
    }
}
