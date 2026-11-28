using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// The swatting hand (stage 2+). When the human is alert and can see the
    /// mosquito within reach, the hand winds up for a reaction delay and then
    /// swings in a straight line at the mosquito's (slightly predicted)
    /// position. Passing within 6 cm catches the mosquito.
    /// Training: reaction 0.3-0.5 s, 3-5 m/s. New human: 0.15-0.3 s, 6-8 m/s.
    /// </summary>
    public class HandController : MonoBehaviour
    {
        public enum State { Rest, WindUp, Swing, Cooldown }

        public HumanController human;
        public Transform hand; // visual only, no collider

        public const float Reach = 0.9f;
        public const float CatchRadius = 0.06f;
        public const float CooldownSeconds = 1f;
        /// <summary>
        /// Costly-to-ignore alert (11/28, HumanController.Lethal): once the alert human sees the mosquito the hand is
        /// quick and keeps coming: reaction 0.15-0.3 s, 6-8 m/s, reach 1.2 m (leaning), 0.4 s between swings.
        /// Out of sight, nothing happens; that is what hiding buys.
        /// </summary>
        float ReachNow => human.Lethal ? 1.2f : Reach;
        bool Quick => m_Fast || human.Lethal;
        public const float Lead = 0.1f;

        public State Current { get; private set; }
        public bool Active => Current == State.WindUp || Current == State.Swing;
        public Vector3 Position => hand.position;
        public Vector3 Velocity { get; private set; }

        System.Random m_Rng;
        bool m_Fast;
        float m_Timer, m_Speed, m_SwingTime;
        const float MaxSwingSeconds = 0.6f; // a following swing ends after this
        Vector3 m_Target;

        Vector3 RestPos => human.transform.TransformPoint(new Vector3(0.3f, 0.9f, 0.15f));
        public Vector3 Shoulder => human.transform.TransformPoint(new Vector3(0.22f, 1.35f, 0f));

        public void ResetHand(System.Random rng, bool fast)
        {
            m_Rng = rng;
            m_Fast = fast;
            Current = State.Rest;
            Velocity = Vector3.zero;
            hand.position = RestPos;
        }

        /// <summary>Wind up now and swing after <paramref name="delay"/> s (EVADE probe start).</summary>
        public void ForceSwing(float delay)
        {
            Current = State.WindUp;
            m_Timer = delay;
        }

        /// <summary>Advance one step. Returns true if the mosquito is caught.</summary>
        public bool Step(float dt, Vector3 mosquitoPos, Vector3 mosquitoVel)
        {
            Vector3 prev = hand.position;
            switch (Current)
            {
                case State.Rest:
                    hand.position = RestPos;
                    // Swat as soon as the alert human has the mosquito in view and within reach.
                    if (human.Spotted && (human.BiteLocked || Vector3.Distance(mosquitoPos, Shoulder) < ReachNow))
                    {
                        Current = State.WindUp;
                        m_Timer = Quick ? Range(0.15f, 0.3f) : Range(0.3f, 0.5f);
                    }
                    break;

                case State.WindUp:
                    hand.position = RestPos;
                    m_Timer -= dt;
                    if (m_Timer <= 0f)
                    {
                        m_Target = mosquitoPos + mosquitoVel * Lead;
                        m_Speed = Quick ? Range(6f, 8f) : Range(3f, 5f);
                        m_SwingTime = 0f;
                        Current = State.Swing;
                    }
                    break;

                case State.Swing:
                {
                    // Strict human (11/28): the hand follows a mosquito it can still see within reach; one that
                    // left the reach or the line of sight is swung at where it was last seen.
                    if ((human.Strict || human.Lethal) && human.Spotted &&
                        (human.BiteLocked || Vector3.Distance(mosquitoPos, Shoulder) < ReachNow))
                        m_Target = mosquitoPos + mosquitoVel * Lead;
                    m_SwingTime += dt;
                    if (m_SwingTime > MaxSwingSeconds) { Current = State.Cooldown; m_Timer = human.Lethal ? 0.4f : CooldownSeconds; break; }
                    Vector3 d = m_Target - prev;
                    float step = m_Speed * dt;
                    bool arrived = d.magnitude <= step;
                    Vector3 next = arrived ? m_Target : prev + d.normalized * step;
                    hand.position = next;
                    if (SegmentDistance(prev, next, mosquitoPos) < CatchRadius)
                    {
                        Velocity = (next - prev) / dt;
                        return true;
                    }
                    if (arrived)
                    {
                        Current = State.Cooldown;
                        m_Timer = human.Lethal ? 0.4f : CooldownSeconds;
                    }
                    break;
                }

                case State.Cooldown:
                    hand.position = Vector3.MoveTowards(prev, RestPos, 2f * dt);
                    m_Timer -= dt;
                    if (m_Timer <= 0f) Current = State.Rest;
                    break;
            }
            Velocity = (hand.position - prev) / dt;
            return false;
        }

        static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f) return Vector3.Distance(a, p);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return Vector3.Distance(a + ab * t, p);
        }

        float Range(float a, float b) => a + (float)m_Rng.NextDouble() * (b - a);
    }
}
