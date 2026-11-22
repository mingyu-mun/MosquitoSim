using UnityEngine;

namespace MosquitoSim
{
    /// <summary>Result of one motor step.</summary>
    public struct MotorStepResult
    {
        public bool Collided;      // hit something that is not a landing
        public SkinZone HitSkin;   // set if the collision was with a skin zone
        public bool JustLanded;
        public bool TookOff;
        public bool HitTrap;       // touched a sticky trap: the episode fails
    }

    /// <summary>
    /// Point-mass flight for a 5 mm mosquito. No Rigidbody: velocity is
    /// integrated directly and collisions use SphereCast, so a fast, tiny body
    /// cannot tunnel through walls. The mosquito object must have no collider.
    /// </summary>
    public class MosquitoMotor : MonoBehaviour
    {
        public float bodyRadius = 0.003f;
        public float contactOffset = 0.001f;
        public LayerMask collisionMask = ~(1 << 9); // everything but the Labels layer

        [Tooltip("Action component along the skin normal needed to take off")]
        public float takeoffThreshold = 0.5f;
        public float takeoffSpeed = 0.3f;

        public Vector3 Velocity { get; private set; }
        public bool IsLanded { get; private set; }
        public SkinZone LandedZone { get; private set; }
        public Vector3 LandedNormal { get; private set; }

        Vector3 m_LandedLocalPos;
        Vector3 m_LandedLocalNormal;

        public void ResetMotor(Vector3 position)
        {
            transform.position = position;
            Velocity = Vector3.zero;
            IsLanded = false;
            LandedZone = null;
        }

        /// <summary>
        /// Advance one fixed step.
        /// action: desired acceleration direction, each axis in [-1, 1].
        /// landingTarget: the only zone the mosquito may land on (null = none).
        /// </summary>
        public MotorStepResult Step(Vector3 action, float dt, SkinZone landingTarget,
            float landingRadius, float landingSpeed)
        {
            var result = new MotorStepResult();
            action = Vector3.ClampMagnitude(action, 1f);

            if (IsLanded)
            {
                // Follow the skin (the human may move from stage 2).
                Transform t = LandedZone.transform;
                transform.position = t.TransformPoint(m_LandedLocalPos);
                LandedNormal = t.TransformDirection(m_LandedLocalNormal).normalized;
                if (Vector3.Dot(action, LandedNormal) > takeoffThreshold)
                {
                    IsLanded = false;
                    LandedZone = null;
                    Velocity = LandedNormal * takeoffSpeed;
                    result.TookOff = true;
                }
                return result;
            }

            Vector3 v = Velocity + action * (ObsLayout.MaxAccel * dt);
            v = Vector3.ClampMagnitude(v, ObsLayout.MaxSpeed);
            Vector3 pos = transform.position;

            // Landing within the tolerance of the target zone surface.
            if (landingTarget != null && v.magnitude <= landingSpeed)
            {
                Vector3 p = landingTarget.ClosestSurfacePoint(pos, out Vector3 n);
                if ((pos - p).magnitude <= landingRadius)
                {
                    Land(landingTarget, p, n);
                    result.JustLanded = true;
                    return result;
                }
            }

            Vector3 delta = v * dt;
            float dist = delta.magnitude;
            if (dist > 1e-7f)
            {
                Vector3 dir = delta / dist;
                if (Physics.SphereCast(pos, bodyRadius, dir, out RaycastHit hit, dist + contactOffset,
                        collisionMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<StickyTrap>() != null)
                    {
                        transform.position = pos + dir * Mathf.Max(hit.distance - contactOffset, 0f);
                        Velocity = Vector3.zero;
                        result.HitTrap = true;
                        return result;
                    }

                    var skin = hit.collider.GetComponentInParent<SkinZone>();
                    if (skin != null && skin == landingTarget && v.magnitude <= landingSpeed)
                    {
                        Land(skin, hit.point, hit.normal);
                        result.JustLanded = true;
                        return result;
                    }

                    pos += dir * Mathf.Max(hit.distance - contactOffset, 0f);
                    float into = Vector3.Dot(v, hit.normal);
                    if (into < 0f) v -= into * hit.normal; // slide along the surface
                    result.Collided = true;
                    result.HitSkin = skin;
                }
                else
                {
                    pos += delta;
                }
            }

            transform.position = pos;
            Velocity = v;
            return result;
        }

        void Land(SkinZone zone, Vector3 surfacePoint, Vector3 normal)
        {
            IsLanded = true;
            LandedZone = zone;
            LandedNormal = normal;
            Velocity = Vector3.zero;
            transform.position = surfacePoint + normal * (bodyRadius + contactOffset);
            m_LandedLocalPos = zone.transform.InverseTransformPoint(transform.position);
            m_LandedLocalNormal = zone.transform.InverseTransformDirection(normal);
        }
    }
}
