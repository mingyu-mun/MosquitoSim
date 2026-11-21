namespace MosquitoSim
{
    /// <summary>Subgoal ids; values are the one-hot slot (interface v3).</summary>
    public enum Subgoal
    {
        Approach = 0,
        Hide = 1,
        Evade = 2,  // trained and evaluated from stage 2 (hand)
        MoveTo = 3, // reserved slot, used only if MOVE_TO is adopted
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
