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

    /// <summary>Planner ids, matching the "planner" environment parameter.</summary>
    public enum PlannerKind
    {
        Fixed = 0,    // always Approach (baseline; stage 0 training)
        Random = 1,   // training from stage 1
        Rule = 2,
        Vlm = 3,
        VlmNoImage = 4,
    }

    public static class SubgoalConstants
    {
        public const float TimeoutSeconds = 8f;
        public const float HideRadius = 0.15f;
        public const float HideHoldSeconds = 1f;
        public const float EvadeDistance = 1.5f;
    }
}
