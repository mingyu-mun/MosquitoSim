using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// A skin area on the human that the mosquito can land on. IDs are fixed
    /// per human: S1 head, S2 left forearm, S3 right forearm, S4 left calf,
    /// S5 right calf. Use a primitive collider (sphere, capsule, box): landing
    /// uses Collider.ClosestPoint, which needs a convex shape.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SkinZone : MonoBehaviour
    {
        [Tooltip("Planner ID, S1..S5")]
        public string id = "S1";

        [Tooltip("Name used in the language instruction, e.g. 왼팔")]
        public string zoneName = "skin";

        [Tooltip("Covered by a blanket or sleeve (new-object evaluation): no landing")]
        public bool covered;

        Collider m_Collider;

        public Collider Collider
        {
            get
            {
                if (m_Collider == null) m_Collider = GetComponent<Collider>();
                return m_Collider;
            }
        }

        public Vector3 Center => Collider.bounds.center;

        /// <summary>Closest point on the zone surface and its outward normal.</summary>
        public Vector3 ClosestSurfacePoint(Vector3 from, out Vector3 normal)
        {
            Vector3 p = Collider.ClosestPoint(from);
            Vector3 d = from - p;
            if (d.sqrMagnitude < 1e-10f)
            {
                // "from" is inside the collider; push out from the center.
                d = from - Center;
                if (d.sqrMagnitude < 1e-10f) d = Vector3.up;
            }
            normal = d.normalized;
            return p;
        }
    }
}
