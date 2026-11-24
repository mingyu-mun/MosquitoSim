using System;

namespace MosquitoSim
{
    /// <summary>
    /// Subgoal ids; values are the one-hot slot (interface v3). Planners send a
    /// subgoal plus a target ID (skin S1..S5 or candidate point P1..P8); the
    /// target only decides the subgoal target point given to the policy.
    /// </summary>
    public enum Subgoal
    {
        Approach = 0,
        Hide = 1,
        Evade = 2,  // trained and evaluated from stage 2 (hand)
        MoveTo = 3, // adopted 11/21: go to a candidate point
    }

    /// <summary>
    /// Who decides, matching the "planner" environment parameter.
    /// External = the Python evaluation loop answers over PlannerChannel; it
    /// covers the fixed, rule, VLM and image-free VLM conditions in evaluation.
    /// </summary>
    public enum PlannerKind
    {
        Fixed = 0,    // APPROACH nearest skin
        Random = 1,   // training
        Rule = 2,     // in Unity, for quick checks
        External = 3, // evaluation
        Probe = 4,    // direct subgoal check: one subgoal per episode, no replanning (11/22)
    }

    public static class SubgoalConstants
    {
        public const float TimeoutSeconds = 8f;
        public const float HideRadius = 0.15f;
        public const float HideHoldSeconds = 5f; // 11/22: same 5 s as the HIDE evaluation's alert
        public const float MoveToRadius = 0.15f;
        public const float EvadeDistance = 1.5f;
    }

    /// <summary>Wire format of a command: {"subgoal": "HIDE", "target": "P3"}.</summary>
    [Serializable]
    public class CommandDto
    {
        public string subgoal;
        public string target;
    }

    /// <summary>A planner command: subgoal plus target ID (null for EVADE).</summary>
    public struct PlannerCommand
    {
        public Subgoal Subgoal;
        public string Target;

        public PlannerCommand(Subgoal subgoal, string target)
        {
            Subgoal = subgoal;
            Target = subgoal == Subgoal.Evade ? null : target;
        }

        public static string WireName(Subgoal s)
        {
            switch (s)
            {
                case Subgoal.Approach: return "APPROACH";
                case Subgoal.Hide: return "HIDE";
                case Subgoal.Evade: return "EVADE";
                default: return "MOVE_TO";
            }
        }

        public static bool TryParseName(string name, out Subgoal s)
        {
            s = Subgoal.Approach;
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.Trim().ToUpperInvariant())
            {
                case "APPROACH": s = Subgoal.Approach; return true;
                case "HIDE": s = Subgoal.Hide; return true;
                case "EVADE": s = Subgoal.Evade; return true;
                case "MOVE_TO": s = Subgoal.MoveTo; return true;
                default: return false;
            }
        }

        public static bool TryFromDto(CommandDto dto, out PlannerCommand cmd)
        {
            cmd = default(PlannerCommand);
            if (dto == null || !TryParseName(dto.subgoal, out Subgoal s)) return false;
            string target = string.IsNullOrEmpty(dto.target) ? null : dto.target.Trim().ToUpperInvariant();
            if (s != Subgoal.Evade && target == null) return false;
            cmd = new PlannerCommand(s, target);
            return true;
        }

        public void WriteJson(JsonBuilder j)
        {
            j.BeginObject().Key("subgoal").Value(WireName(Subgoal));
            if (Target != null) j.Key("target").Value(Target);
            j.EndObject();
        }

        public override string ToString() => Target == null ? WireName(Subgoal) : WireName(Subgoal) + " " + Target;

        public bool SameAs(PlannerCommand o) => Subgoal == o.Subgoal && Target == o.Target;
    }
}
