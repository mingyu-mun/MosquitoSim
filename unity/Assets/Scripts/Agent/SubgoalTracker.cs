using UnityEngine;

namespace MosquitoSim
{
    public enum SubgoalStatus { Running, Achieved, TimedOut }

    /// <summary>
    /// Tracks the current command: its target point, shaping reward and when
    /// it ends. Ends report once, then the tracker idles until the next Set.
    /// APPROACH: never times out once landed (feeding lock); success is the
    ///   agent's feeding check.
    /// HIDE: within 0.15 m for 1 s. MOVE_TO: within 0.15 m.
    /// EVADE: 1.5 m from the hazard, or the hazard stops.
    /// All: 8 s timeout.
    /// </summary>
    public class SubgoalTracker
    {
        public PlannerCommand Command { get; private set; }
        public Vector3 Target { get; private set; }
        public float StartTime { get; private set; }
        public bool Ended { get; private set; }

        /// <summary>HIDE hold time and MOVE_TO radius in use (training experiments can change them).</summary>
        public float HideHoldSeconds = SubgoalConstants.HideHoldSeconds;
        public float MoveToRadius = SubgoalConstants.MoveToRadius;

        float m_Hold;
        float m_PrevDist;

        public void Set(PlannerCommand cmd, Vector3 target, float now, Vector3 mosquitoPos)
        {
            Command = cmd;
            Target = target;
            StartTime = now;
            Ended = false;
            m_Hold = 0f;
            m_PrevDist = Vector3.Distance(mosquitoPos, target);
        }

        public void UpdateTarget(Vector3 target) => Target = target;

        /// <summary>HIDE: inside the hiding radius and counting the hold.</summary>
        public bool Holding => !Ended && Command.Subgoal == Subgoal.Hide && m_Hold > 0f;

        /// <summary>Distance change toward (EVADE: away from) the target since the last call.</summary>
        public float Shaping(Vector3 pos) => Shaping(Vector3.Distance(pos, Target));

        /// <summary>Same, with the distance given (route length for guided MOVE_TO / HIDE).</summary>
        public float Shaping(float d)
        {
            float r = Command.Subgoal == Subgoal.Evade ? d - m_PrevDist : m_PrevDist - d;
            m_PrevDist = d;
            return r;
        }

        public SubgoalStatus Step(float now, float dt, Vector3 pos, bool landed, bool hazardActive)
        {
            if (Ended) return SubgoalStatus.Running;
            float d = Vector3.Distance(pos, Target);
            bool timedOut = now - StartTime > SubgoalConstants.TimeoutSeconds;

            switch (Command.Subgoal)
            {
                case Subgoal.Approach:
                    if (landed) return SubgoalStatus.Running;
                    break;
                case Subgoal.Hide:
                    m_Hold = d <= SubgoalConstants.HideRadius ? m_Hold + dt : 0f;
                    if (m_Hold >= HideHoldSeconds) return End(SubgoalStatus.Achieved);
                    if (m_Hold > 0f) return SubgoalStatus.Running; // the 8 s limit is for getting there, not the hold
                    break;
                case Subgoal.MoveTo:
                    if (d <= MoveToRadius) return End(SubgoalStatus.Achieved);
                    break;
                case Subgoal.Evade:
                    if (!hazardActive || d >= SubgoalConstants.EvadeDistance) return End(SubgoalStatus.Achieved);
                    break;
            }
            return timedOut ? End(SubgoalStatus.TimedOut) : SubgoalStatus.Running;
        }

        SubgoalStatus End(SubgoalStatus s)
        {
            Ended = true;
            return s;
        }
    }
}
