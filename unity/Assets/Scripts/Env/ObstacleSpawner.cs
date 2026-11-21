using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Furniture for one room.
    /// Asset mode (Furniture Mega Pack under Assets/ThirdParty, instanced by the
    /// scene builder): each episode picks a layout that suits the room type
    /// (bedroom, living room, dining room, office, studio; set B: kitchen,
    /// bathroom) and places it step by step, each step relative to the walls
    /// or to an earlier item: a bed with its head on a wall and nightstands
    /// beside it, a coffee table in front of the sofa and an armchair facing
    /// it, chairs around the dining table, a chair at the desk, cushions on the
    /// sofa or bed, closets in corners. Optional steps, which wall, which model
    /// variant and all offsets come from the episode RNG.
    /// Set A (training and most evaluations): bed, sofa, armchair, tables,
    /// chairs, closet, drawers, cushions. Set B (new-object evaluation only):
    /// kitchen and bathroom items.
    /// Primitive mode (no asset pack): the old box furniture placed freely;
    /// set A table, chair, bookshelf, sofa, floor lamp; set B bed, wardrobe,
    /// plant, drying rack, partition.
    /// Either way nothing is placed on the human's spot or walking route.
    /// </summary>
    public class ObstacleSpawner : MonoBehaviour
    {
        struct Part
        {
            public PrimitiveType Type;
            public Vector3 Pos, Scale;
            public Part(PrimitiveType type, Vector3 pos, Vector3 scale) { Type = type; Pos = pos; Scale = scale; }
        }

        const float WallGap = 0.03f;      // between furniture and wall

        readonly List<FurnitureItem> m_Items = new List<FurnitureItem>();
        public readonly List<GameObject> Active = new List<GameObject>();
        public bool AssetMode { get; private set; }

        void EnsureBuilt()
        {
            if (m_Items.Count > 0) return;
            // Asset furniture made by the scene builder (Furniture Mega Pack), if any.
            foreach (var f in GetComponentsInChildren<FurnitureItem>(true)) m_Items.Add(f);
            if (m_Items.Count > 0)
            {
                AssetMode = true;
                return;
            }
            var wood = new Color(0.55f, 0.4f, 0.28f);
            var fabric = new Color(0.35f, 0.45f, 0.6f);
            var white = new Color(0.9f, 0.9f, 0.88f);
            var green = new Color(0.25f, 0.55f, 0.3f);
            var metal = new Color(0.7f, 0.7f, 0.72f);

            // Set A
            Add(false, "Table", new Vector3(1.2f, 0.75f, 0.7f), wood,
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.725f, 0f), new Vector3(1.2f, 0.05f, 0.7f)),
                new Part(PrimitiveType.Cube, new Vector3(0.55f, 0.35f, 0.3f), new Vector3(0.05f, 0.7f, 0.05f)),
                new Part(PrimitiveType.Cube, new Vector3(-0.55f, 0.35f, 0.3f), new Vector3(0.05f, 0.7f, 0.05f)),
                new Part(PrimitiveType.Cube, new Vector3(0.55f, 0.35f, -0.3f), new Vector3(0.05f, 0.7f, 0.05f)),
                new Part(PrimitiveType.Cube, new Vector3(-0.55f, 0.35f, -0.3f), new Vector3(0.05f, 0.7f, 0.05f)));
            Add(false, "Chair", new Vector3(0.5f, 0.95f, 0.5f), wood,
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.225f, 0f), new Vector3(0.45f, 0.45f, 0.45f)),
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.7f, -0.2f), new Vector3(0.45f, 0.5f, 0.05f)));
            Add(false, "Bookshelf", new Vector3(0.8f, 1.8f, 0.3f), wood,
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.9f, 0f), new Vector3(0.8f, 1.8f, 0.3f)));
            Add(false, "Sofa", new Vector3(1.8f, 0.85f, 0.8f), fabric,
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.225f, 0f), new Vector3(1.8f, 0.45f, 0.8f)),
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.6f, -0.32f), new Vector3(1.8f, 0.5f, 0.16f)));
            Add(false, "FloorLamp", new Vector3(0.4f, 1.65f, 0.4f), metal,
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.35f, 0.02f, 0.35f)),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 0f), new Vector3(0.04f, 0.75f, 0.04f)),
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 1.52f, 0f), new Vector3(0.4f, 0.12f, 0.4f)));

            // Set B
            Add(true, "Bed", new Vector3(2.0f, 0.55f, 1.4f), white,
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.275f, 0f), new Vector3(2.0f, 0.55f, 1.4f)));
            Add(true, "Wardrobe", new Vector3(1.0f, 2.0f, 0.6f), wood,
                new Part(PrimitiveType.Cube, new Vector3(0f, 1.0f, 0f), new Vector3(1.0f, 2.0f, 0.6f)));
            Add(true, "Plant", new Vector3(0.6f, 1.0f, 0.6f), green,
                new Part(PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0f), new Vector3(0.35f, 0.2f, 0.35f)),
                new Part(PrimitiveType.Sphere, new Vector3(0f, 0.7f, 0f), new Vector3(0.6f, 0.6f, 0.6f)));
            Add(true, "DryingRack", new Vector3(1.4f, 1.1f, 0.6f), metal,
                new Part(PrimitiveType.Cube, new Vector3(0.68f, 0.55f, 0f), new Vector3(0.03f, 1.1f, 0.6f)),
                new Part(PrimitiveType.Cube, new Vector3(-0.68f, 0.55f, 0f), new Vector3(0.03f, 1.1f, 0.6f)),
                new Part(PrimitiveType.Cube, new Vector3(0f, 1.05f, 0.25f), new Vector3(1.4f, 0.02f, 0.02f)),
                new Part(PrimitiveType.Cube, new Vector3(0f, 1.05f, -0.25f), new Vector3(1.4f, 0.02f, 0.02f)),
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 0.02f, 0.02f)));
            Add(true, "Partition", new Vector3(1.2f, 1.8f, 0.05f), white,
                new Part(PrimitiveType.Cube, new Vector3(0f, 0.9f, 0f), new Vector3(1.2f, 1.8f, 0.05f)));
        }

        void Add(bool setB, string itemName, Vector3 size, Color color, params Part[] parts)
        {
            var root = new GameObject(itemName);
            root.transform.SetParent(transform, false);
            foreach (var p in parts)
            {
                var go = GameObject.CreatePrimitive(p.Type);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = p.Pos;
                go.transform.localScale = p.Scale;
                go.GetComponent<Renderer>().material.color = color;
            }
            var f = root.AddComponent<FurnitureItem>();
            f.category = itemName;
            f.setB = setB;
            f.placement = FurnitureItem.Placement.Free;
            f.size = size;
            f.centerOffset = new Vector3(0f, size.y * 0.5f, 0f);
            root.SetActive(false);
            m_Items.Add(f);
        }

        public void Clear()
        {
            EnsureBuilt();
            foreach (var it in m_Items) it.gameObject.SetActive(false);
            Active.Clear();
        }

        enum P { Wall, Corner, NextTo, InFront, Facing, Around, On, Free }

        struct Step
        {
            public string Cat;
            public P Place;
            public int Anchor;   // index of an earlier step (-1: none)
            public float Prob;
            public int Max;      // Around: up to this many
            public Step(string cat, P place, int anchor = -1, float prob = 1f, int max = 1)
            { Cat = cat; Place = place; Anchor = anchor; Prob = prob; Max = max; }
        }

        static readonly Dictionary<string, Step[]> s_Layouts = new Dictionary<string, Step[]>
        {
            { "Bedroom", new[] {
                new Step("Bed", P.Wall), new Step("Nightstand", P.NextTo, 0, 0.9f), new Step("Nightstand", P.NextTo, 0, 0.4f),
                new Step("Closet", P.Corner, -1, 0.9f), new Step("Drawer", P.Wall, -1, 0.6f),
                new Step("Desk", P.Wall, -1, 0.5f), new Step("Chair", P.Facing, 5), new Step("Cushion", P.On, 0, 0.6f) } },
            { "Living", new[] {
                new Step("Sofa", P.Wall), new Step("CoffeeTable", P.InFront, 0, 0.9f), new Step("Armchair", P.Facing, 1, 0.7f),
                new Step("Drawer", P.Wall, -1, 0.8f), new Step("Closet", P.Corner, -1, 0.5f),
                new Step("Cushion", P.On, 0, 0.7f), new Step("Cushion", P.On, 0, 0.4f), new Step("Nightstand", P.NextTo, 0, 0.4f) } },
            { "Dining", new[] {
                new Step("Table", P.Free), new Step("Chair", P.Around, 0, 1f, 4), new Step("Drawer", P.Wall, -1, 0.7f),
                new Step("Closet", P.Corner, -1, 0.6f), new Step("Sofa", P.Wall, -1, 0.4f) } },
            { "Office", new[] {
                new Step("Desk", P.Wall), new Step("Chair", P.Facing, 0), new Step("Closet", P.Corner, -1, 0.8f),
                new Step("Drawer", P.Wall, -1, 0.7f), new Step("Sofa", P.Wall, -1, 0.5f), new Step("CoffeeTable", P.InFront, 4, 0.6f),
                new Step("Desk", P.Wall, -1, 0.3f), new Step("Chair", P.Facing, 6) } },
            { "Studio", new[] {
                new Step("Bed", P.Wall), new Step("Nightstand", P.NextTo, 0, 0.7f), new Step("Sofa", P.Wall, -1, 0.8f),
                new Step("CoffeeTable", P.InFront, 2, 0.7f), new Step("Table", P.Free, -1, 0.6f), new Step("Chair", P.Around, 4, 1f, 2),
                new Step("Closet", P.Corner, -1, 0.6f), new Step("Cushion", P.On, 2, 0.5f) } },
            // Set B: never seen in training
            { "Kitchen", new[] {
                new Step("Refrigerator", P.Corner), new Step("Cabinet", P.NextTo, 0), new Step("Oven", P.NextTo, 1, 0.9f),
                new Step("Cabinet", P.NextTo, 2, 0.7f), new Step("Cabinet", P.Wall, -1, 0.5f), new Step("WashBasin", P.Wall, -1, 0.3f) } },
            { "Bathroom", new[] {
                new Step("BathTub", P.Corner), new Step("Toilet", P.Wall), new Step("WashBasin", P.Wall),
                new Step("Cabinet", P.Wall, -1, 0.5f) } },
            { "KitchenBath", new[] {
                new Step("Refrigerator", P.Corner), new Step("Cabinet", P.NextTo, 0), new Step("Oven", P.NextTo, 1, 0.8f),
                new Step("BathTub", P.Corner, -1, 0.8f), new Step("Toilet", P.Wall), new Step("WashBasin", P.Wall, -1, 0.6f) } },
        };

        /// <summary>Layouts that suit a room type (asset mode).</summary>
        public static string[] Layouts(RoomType room, bool setB)
        {
            if (setB)
            {
                switch (room)
                {
                    case RoomType.R4x3: return new[] { "Kitchen", "Bathroom" };
                    case RoomType.R5x4: return new[] { "Kitchen", "Bathroom", "KitchenBath" };
                    default: return new[] { "KitchenBath", "Kitchen" };
                }
            }
            switch (room)
            {
                case RoomType.R4x3: return new[] { "Bedroom", "Office" };
                case RoomType.R5x4: return new[] { "Living", "Dining", "Bedroom" };
                case RoomType.LShape: return new[] { "Studio", "Living", "Office" };
                case RoomType.Long6x3: return new[] { "Living", "Dining", "Studio" };
                default: return new[] { "Studio", "Bedroom", "Dining" };
            }
        }

        public string Layout { get; private set; } = "";

        /// <summary>
        /// Furnish the room. Asset mode places one layout (count is ignored);
        /// primitive mode places up to <paramref name="count"/> shuffled items.
        /// The human's start spot keeps <paramref name="keepAway"/> m of free floor,
        /// the walking route a body width, door zones stay clear.
        /// </summary>
        public void Spawn(System.Random rng, RoomArea area, bool setB, int count, HumanController human, float keepAway)
        {
            Clear();
            IList<ClearZone> clear = area.Decor != null ? area.Decor.KeepClear : null;
            if (!AssetMode)
            {
                Layout = "primitives";
                var order = new List<FurnitureItem>();
                foreach (var it in m_Items) if (it.setB == setB) order.Add(it);
                Shuffle(order, rng);
                foreach (var it in order)
                {
                    if (Active.Count >= count) break;
                    if (TryPlace(it, P.Free, null, rng, area, human, keepAway, clear)) Active.Add(it.gameObject);
                }
                return;
            }

            var names = Layouts(area.Generator.Current, setB);
            Layout = names[rng.Next(names.Length)];
            var steps = s_Layouts[Layout];
            var placed = new FurnitureItem[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                Step s = steps[i];
                if (rng.NextDouble() > s.Prob) continue;
                FurnitureItem anchor = s.Anchor >= 0 ? placed[s.Anchor] : null;
                if (s.Anchor >= 0 && anchor == null) continue;
                int n = s.Place == P.Around ? 2 + rng.Next(Mathf.Max(1, s.Max - 1)) : 1;
                for (int k = 0; k < n; k++)
                {
                    FurnitureItem it = PickFree(s.Cat, setB, rng);
                    if (it == null) break;
                    if (TryPlace(it, s.Place, anchor, rng, area, human, keepAway, clear))
                    {
                        Active.Add(it.gameObject);
                        if (placed[i] == null) placed[i] = it;
                    }
                }
            }
        }

        /// <summary>A random not-yet-used model of the category.</summary>
        FurnitureItem PickFree(string cat, bool setB, System.Random rng)
        {
            var free = new List<FurnitureItem>();
            foreach (var it in m_Items)
                if (it.category == cat && it.setB == setB && !it.gameObject.activeSelf) free.Add(it);
            return free.Count == 0 ? null : free[rng.Next(free.Count)];
        }

        /// <summary>Centre (floor), face yaw and footprint of a placed item.</summary>
        static void Pose(FurnitureItem a, out Vector3 c, out float yaw, out Vector3 fwd, out Vector3 right)
        {
            c = a.transform.position + a.transform.rotation * a.centerOffset;
            yaw = a.transform.eulerAngles.y - a.frontYaw;
            var r = Quaternion.Euler(0f, yaw, 0f);
            fwd = r * Vector3.forward;
            right = r * Vector3.right;
        }

        bool TryPlace(FurnitureItem it, P mode, FurnitureItem anchor, System.Random rng, RoomArea area,
            HumanController human, float keepAway, IList<ClearZone> clear)
        {
            Vector3 o = area.transform.position;
            Vector2 fp = it.Footprint; // x width, y depth when facing +Z
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Vector3 c;
                float yaw;
                bool onTop = false;
                float floorY = o.y;
                switch (mode)
                {
                    case P.Wall:
                    case P.Corner:
                    {
                        // Back to a wall, facing into the room; corners for closets and the like.
                        int side = rng.Next(4);
                        float along = RoomPlacement.WallHalfLength(area, side);
                        if (fp.x * 0.5f > along - 0.05f) continue;
                        float lim = along - fp.x * 0.5f - 0.02f;
                        float cornerGap = it.size.y > 1.5f ? 0.95f : 0f; // tall: next to the corner, not in it
                        if (mode == P.Corner && lim - cornerGap < 0f) continue;
                        float u = mode == P.Corner ? (rng.NextDouble() < 0.5 ? -1f : 1f) * (lim - cornerGap - RoomPlacement.Range(rng, 0f, 0.1f))
                                                   : RoomPlacement.Range(rng, -lim, lim);
                        c = RoomPlacement.WallPoint(area, side, u, fp.y * 0.5f + WallGap);
                        yaw = RoomPlacement.WallYaw(side);
                        break;
                    }
                    case P.NextTo:
                    {
                        // Beside the anchor, backs on the same line (nightstand by the bed, cabinets in a row).
                        Pose(anchor, out Vector3 ac, out float ay, out Vector3 af, out Vector3 ar);
                        Vector2 afp = anchor.Footprint;
                        float sideSign = rng.NextDouble() < 0.5 ? -1f : 1f;
                        if (attempt >= 20) sideSign = -sideSign;
                        c = ac + ar * sideSign * (afp.x * 0.5f + fp.x * 0.5f + 0.04f) + af * (fp.y * 0.5f - afp.y * 0.5f);
                        yaw = ay;
                        break;
                    }
                    case P.InFront:
                    {
                        // In front of the anchor, same facing (coffee table before the sofa).
                        Pose(anchor, out Vector3 ac, out float ay, out Vector3 af, out Vector3 ar);
                        c = ac + af * (anchor.Footprint.y * 0.5f + RoomPlacement.Range(rng, 0.35f, 0.5f) + fp.y * 0.5f)
                               + ar * RoomPlacement.Range(rng, -0.1f, 0.1f);
                        yaw = ay;
                        break;
                    }
                    case P.Facing:
                    {
                        // In front of the anchor, turned towards it (chair at the desk, armchair across the coffee table).
                        Pose(anchor, out Vector3 ac, out float ay, out Vector3 af, out Vector3 ar);
                        bool desk = anchor.category == "Desk" || anchor.category == "Table";
                        float gap = desk ? 0.02f : RoomPlacement.Range(rng, 0.3f, 0.5f);
                        c = ac + af * (anchor.Footprint.y * 0.5f + gap + fp.y * 0.5f) + ar * RoomPlacement.Range(rng, -0.15f, 0.15f);
                        yaw = ay + 180f + RoomPlacement.Range(rng, -12f, 12f);
                        break;
                    }
                    case P.Around:
                    {
                        // At one of the four sides of the table, facing it.
                        Pose(anchor, out Vector3 ac, out float ay, out Vector3 af, out Vector3 ar);
                        Vector2 afp = anchor.Footprint;
                        int side = rng.Next(4);
                        float dist = (side % 2 == 0 ? afp.y : afp.x) * 0.5f + fp.y * 0.5f + 0.05f;
                        float dirYaw = ay + side * 90f;
                        Vector3 dir = Quaternion.Euler(0f, dirYaw, 0f) * Vector3.forward;
                        Vector3 along = Quaternion.Euler(0f, dirYaw, 0f) * Vector3.right;
                        float spread = (side % 2 == 0 ? afp.x : afp.y) * 0.5f - fp.x * 0.5f;
                        c = ac + dir * dist + along * (spread > 0f ? RoomPlacement.Range(rng, -spread, spread) * 0.7f : 0f);
                        yaw = dirYaw + 180f + RoomPlacement.Range(rng, -8f, 8f);
                        break;
                    }
                    case P.On:
                    {
                        // On the seat or mattress of the anchor.
                        Pose(anchor, out Vector3 ac, out float ay, out Vector3 af, out Vector3 ar);
                        Vector2 afp = anchor.Footprint;
                        c = ac + ar * RoomPlacement.Range(rng, -0.35f, 0.35f) * afp.x + af * RoomPlacement.Range(rng, -0.1f, 0.2f) * afp.y;
                        yaw = ay + RoomPlacement.Range(rng, -30f, 30f);
                        floorY = o.y + anchor.size.y * (anchor.category == "Bed" ? 0.55f : 0.5f);
                        onTop = true;
                        break;
                    }
                    default:
                    {
                        float r = 0.5f * Mathf.Max(fp.x, fp.y);
                        c = area.RandomFloorPoint(rng, r + (it.category == "Table" ? 0.6f : 0.1f));
                        yaw = 90f * rng.Next(4) + RoomPlacement.Range(rng, -8f, 8f);
                        break;
                    }
                }

                var rot = Quaternion.Euler(0f, yaw, 0f);
                // Tall pieces (closets, fridges) stay out of the corners, where the overview cameras
                // hang 2.3 m up (run16: a closet top filled one planner view). Their corner spot is
                // the wall next to the corner.
                if (it.size.y > 1.5f && RoomDecor.NearCorner(area, c, 0.9f + 0.5f * Mathf.Max(fp.x, fp.y))) continue;
                if (it.size.y > 1.5f && RoomPlacement.BlocksOverview(area, c, 0.5f * Mathf.Max(fp.x, fp.y))) continue;
                if (!onTop)
                {
                    Vector3 half = new Vector3(fp.x * 0.5f + 0.01f, it.size.y * 0.5f - 0.01f, fp.y * 0.5f + 0.01f);
                    // Bottom of the test box sits 3 cm above the floor so the floor itself never counts.
                    Vector3 boxCentre = new Vector3(c.x, o.y, c.z) + Vector3.up * (it.size.y * 0.5f + 0.02f);
                    if (Physics.CheckBox(boxCentre, half, rot, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (RoomPlacement.OnRoute(c, rot, fp, human, keepAway)) continue;
                    if (RoomPlacement.InClearZone(c, rot, fp, clear)) continue;
                    if (!InsideRoom(area, c, rot, fp)) continue;
                }

                it.Place(c, yaw, floorY);
                it.gameObject.SetActive(true);
                Physics.SyncTransforms();
                return true;
            }
            return false;
        }

        /// <summary>All four footprint corners inside the room walls.</summary>
        static bool InsideRoom(RoomArea area, Vector3 c, Quaternion rot, Vector2 fp)
        {
            Vector3 o = area.transform.position;
            float hx = area.Size.x * 0.5f, hz = area.Size.z * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 q = c + rot * new Vector3((i & 1) == 0 ? -fp.x * 0.5f : fp.x * 0.5f, 0f, (i & 2) == 0 ? -fp.y * 0.5f : fp.y * 0.5f) - o;
                if (Mathf.Abs(q.x) > hx + 0.01f || Mathf.Abs(q.z) > hz + 0.01f) return false;
            }
            return true;
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
