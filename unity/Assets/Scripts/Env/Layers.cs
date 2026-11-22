namespace MosquitoSim
{
    /// <summary>
    /// Layer indices. Unnamed layers can be used by number, so no project
    /// settings change is needed (naming them in Tags and Layers is optional).
    /// </summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int Human = 8;   // body parts and skin zones
        public const int Labels = 9;  // ID labels drawn only for planner cameras
        public const int DebugViz = 10; // debug drawings (target line) never shown to planner cameras
        public const int MosquitoModel = 11; // mosquito 3D model: main and chase views only

        /// <summary>Everything except the human and labels: walls, obstacles, net, traps.</summary>
        public static int WorldNoHuman => ~((1 << Human) | (1 << Labels));

        /// <summary>Everything except labels (main view: debug drawings stay visible).</summary>
        public static int AllButLabels => ~(1 << Labels);

        /// <summary>Planner cameras (VLM input): everything but debug drawings.</summary>
        public static int PlannerView => ~((1 << DebugViz) | (1 << MosquitoModel));

        /// <summary>Auto test still shots: no labels, no debug drawings.</summary>
        public static int ShotView => ~((1 << Labels) | (1 << DebugViz));
    }
}
