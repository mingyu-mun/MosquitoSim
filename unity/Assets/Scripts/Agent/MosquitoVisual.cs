using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Turns the mosquito model to its flight direction (yaw and a little pitch);
    /// on skin it faces the surface. Flaps the wings in flight around the spread
    /// pose taken from the model's own take (the file has no flap cycle), folds
    /// them on landing. Visual only: the agent's transform is not rotated.
    /// </summary>
    public class MosquitoVisual : MonoBehaviour
    {
        public Transform pivot;
        [Tooltip("Rotation from the model's own axes to 'forward = +Z, up = +Y'")]
        public Vector3 modelEuler;

        [Header("Wings")]
        public Transform[] wings = new Transform[0];
        public Quaternion[] wingRest = new Quaternion[0];
        public Quaternion[] wingSpread = new Quaternion[0];
        [Tooltip("Shown flap rate (a real mosquito beats ~600 Hz; on screen that would only alias)")]
        public float flapHz = 22f;
        [Tooltip("Flap swing around the spread pose, as a fraction of the rest-to-spread rotation")]
        public float flapAmount = 0.45f;

        float m_Open; // 0 folded (landed) .. 1 spread (flying)

        MosquitoMotor m_Motor;
        Vector3 m_Fwd = Vector3.forward;

        void Awake() => m_Motor = GetComponent<MosquitoMotor>();

        void LateUpdate()
        {
            if (pivot == null || m_Motor == null) return;
            Vector3 up = Vector3.up;
            Vector3 v = m_Motor.Velocity;
            if (m_Motor.IsLanded)
            {
                up = m_Motor.LandedNormal;
                Vector3 f = Vector3.ProjectOnPlane(m_Fwd, up);
                if (f.sqrMagnitude > 1e-6f) m_Fwd = f.normalized;
            }
            else if (v.sqrMagnitude > 0.05f * 0.05f)
            {
                Vector3 f = v.normalized;
                f.y = Mathf.Clamp(f.y, -0.5f, 0.5f); // mosquitoes fly mostly level
                m_Fwd = Vector3.Slerp(m_Fwd, f.normalized, 1f - Mathf.Exp(-10f * Time.deltaTime)).normalized;
            }
            if (Mathf.Abs(Vector3.Dot(m_Fwd, up)) <= 0.99f)
                pivot.rotation = Quaternion.LookRotation(m_Fwd, up) * Quaternion.Euler(modelEuler);
            Flap();
        }

        void Flap()
        {
            int n = Mathf.Min(wings.Length, Mathf.Min(wingRest.Length, wingSpread.Length));
            if (n == 0) return;
            m_Open = Mathf.MoveTowards(m_Open, m_Motor.IsLanded ? 0f : 1f, 6f * Time.deltaTime);
            float beat = Mathf.Sin(Time.time * flapHz * 2f * Mathf.PI);
            for (int i = 0; i < n; i++)
            {
                if (wings[i] == null) continue;
                // Around the spread pose: through the take's own opening motion, a bit past it and back.
                float k = m_Open * (1f - flapAmount * 0.5f + flapAmount * 0.5f * beat);
                wings[i].localRotation = Quaternion.SlerpUnclamped(wingRest[i], wingSpread[i], k);
            }
        }
    }
}
