using System.Collections.Generic;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// Room details built from primitives, pooled and re-placed each episode.
    /// Structure (every stage): a door with a keep-clear zone, 1-2 windows (some
    /// with curtains), pictures, a rug, sometimes a pillar.
    /// Clutter (stage 1+), the obstacles that make flying harder: pendant lamps
    /// and hanging plants (mid-air), wall shelves with books, a clothesline with
    /// towels across the room, floor lamps, potted plants and box stacks.
    /// Nothing is placed on the human's spot or walking route.
    /// </summary>
    public class RoomDecor : MonoBehaviour
    {
        readonly Dictionary<string, List<GameObject>> m_Pool = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, int> m_Used = new Dictionary<string, int>();
        public readonly List<ClearZone> KeepClear = new List<ClearZone>();
        public readonly List<string> Placed = new List<string>();

        static readonly Color[] s_Wood = { new Color(0.55f, 0.38f, 0.24f), new Color(0.82f, 0.78f, 0.72f), new Color(0.35f, 0.25f, 0.18f), new Color(0.92f, 0.92f, 0.9f) };
        static readonly Color[] s_Cloth = { new Color(0.75f, 0.2f, 0.2f), new Color(0.2f, 0.4f, 0.7f), new Color(0.9f, 0.8f, 0.3f), new Color(0.3f, 0.6f, 0.4f), new Color(0.85f, 0.85f, 0.85f), new Color(0.6f, 0.35f, 0.6f) };

        public void Clear()
        {
            foreach (var list in m_Pool.Values)
                foreach (var go in list) go.SetActive(false);
            m_Used.Clear();
            KeepClear.Clear();
            Placed.Clear();
            m_Hanging.Clear();
        }

        // ---------------------------------------------------------------- pool

        GameObject Get(string kind, System.Func<GameObject> build)
        {
            if (!m_Pool.TryGetValue(kind, out var list)) m_Pool[kind] = list = new List<GameObject>();
            m_Used.TryGetValue(kind, out int used);
            if (used >= list.Count)
            {
                var go = build();
                go.name = kind;
                go.transform.SetParent(transform, false);
                list.Add(go);
            }
            m_Used[kind] = used + 1;
            var item = list[used];
            item.SetActive(true);
            return item;
        }

        void Release(GameObject go)
        {
            go.SetActive(false);
            if (m_Used.TryGetValue(go.name, out int used) && used > 0) m_Used[go.name] = used - 1;
        }

        static GameObject Root(string n) => new GameObject(n);

        static GameObject Part(GameObject root, PrimitiveType type, Vector3 pos, Vector3 scale, Color color, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material.color = color;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        static void Tint(GameObject root, int partIndex, Color c)
        {
            var t = root.transform.GetChild(partIndex);
            t.GetComponent<Renderer>().material.color = c;
        }

        /// <summary>
        /// Within <paramref name="r"/> m (floor distance) of a room corner, where the
        /// planner's overview cameras hang (run15: a floor lamp filled one view).
        /// </summary>
        public static bool NearCorner(RoomArea area, Vector3 p, float r)
        {
            Vector3 l = p - area.transform.position;
            foreach (var c in RoomPlacement.Corners(area, 0f))
                if (Mathf.Abs(l.x - c.x) < r && Mathf.Abs(l.z - c.y) < r) return true;
            return false;
        }

        /// <summary>Overlap test for a placed item (its colliders are ignored by moving it away first).</summary>
        static bool Free(Vector3 center, Vector3 half, Quaternion rot) => !RoomPlacement.Blocked(center, half, rot);

        // ------------------------------------------------------------ structure

        public void PlaceStructure(System.Random rng, RoomArea area, HumanController human, bool pillar)
        {
            PlaceDoor(rng, area);
            int windows = 1 + rng.Next(2);
            for (int i = 0; i < windows; i++) PlaceWindow(rng, area);
            int pictures = rng.Next(3);
            for (int i = 0; i < pictures; i++) PlacePicture(rng, area);
            if (rng.NextDouble() < 0.6) PlaceRug(rng, area, human);
            if (rng.NextDouble() < 0.3 && pillar) PlacePillar(rng, area, human);
            Physics.SyncTransforms();
        }

        void PlaceDoor(System.Random rng, RoomArea area)
        {
            var door = Get("Door", () =>
            {
                var r = Root("Door");
                Part(r, PrimitiveType.Cube, new Vector3(0f, 1.025f, 0f), new Vector3(0.9f, 2.05f, 0.05f), Color.white);
                Part(r, PrimitiveType.Sphere, new Vector3(0.35f, 1.0f, 0.05f), Vector3.one * 0.06f, new Color(0.8f, 0.75f, 0.5f));
                return r;
            });
            Tint(door, 0, s_Wood[rng.Next(s_Wood.Length)]);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int side = rng.Next(4);
                float along = RoomPlacement.WallHalfLength(area, side);
                if (along < 0.9f) continue;
                float u = RoomPlacement.Range(rng, -along + 0.6f, along - 0.6f);
                Vector3 p = RoomPlacement.WallPoint(area, side, u, 0.025f);
                var rot = Quaternion.Euler(0f, RoomPlacement.WallYaw(side), 0f);
                door.transform.SetPositionAndRotation(new Vector3(-1000f, 0f, 0f), rot);
                Physics.SyncTransforms();
                // Not into the L block or the partition.
                if (!Free(p + Vector3.up * 1.05f + rot * Vector3.forward * 0.3f, new Vector3(0.45f, 1.0f, 0.25f), rot)) continue;
                door.transform.SetPositionAndRotation(p, rot);
                KeepClear.Add(new ClearZone
                {
                    Center = p + rot * Vector3.forward * 0.55f, Rot = rot, Half = new Vector2(0.55f, 0.55f)
                });
                Placed.Add("문");
                return;
            }
            Release(door);
        }

        void PlaceWindow(System.Random rng, RoomArea area)
        {
            float w = RoomPlacement.Range(rng, 0.8f, 1.4f), h = RoomPlacement.Range(rng, 0.9f, 1.2f);
            bool curtains = rng.NextDouble() < 0.5;
            var win = Get("Window", () =>
            {
                var r = Root("Window");
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, new Color(0.75f, 0.88f, 0.95f));               // glass (scaled below)
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, Color.white);                                    // frame top
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, Color.white);                                    // frame bottom / sill
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, Color.white);                                    // frame left
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, Color.white);                                    // frame right
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, s_Cloth[0]);                                    // curtain left
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, s_Cloth[0]);                                    // curtain right
                return r;
            });
            // Layout in local space: wall plane z = 0, room side +z.
            Transform t = win.transform;
            t.GetChild(0).localPosition = new Vector3(0f, 0f, 0.01f); t.GetChild(0).localScale = new Vector3(w, h, 0.02f);
            t.GetChild(1).localPosition = new Vector3(0f, h / 2f + 0.03f, 0.03f); t.GetChild(1).localScale = new Vector3(w + 0.12f, 0.06f, 0.06f);
            t.GetChild(2).localPosition = new Vector3(0f, -h / 2f - 0.03f, 0.06f); t.GetChild(2).localScale = new Vector3(w + 0.2f, 0.05f, 0.12f);
            t.GetChild(3).localPosition = new Vector3(-w / 2f - 0.03f, 0f, 0.03f); t.GetChild(3).localScale = new Vector3(0.06f, h, 0.06f);
            t.GetChild(4).localPosition = new Vector3(w / 2f + 0.03f, 0f, 0.03f); t.GetChild(4).localScale = new Vector3(0.06f, h, 0.06f);
            Color cloth = s_Cloth[rng.Next(s_Cloth.Length)];
            for (int i = 5; i <= 6; i++)
            {
                var c = t.GetChild(i);
                c.gameObject.SetActive(curtains);
                c.localPosition = new Vector3((i == 5 ? -1f : 1f) * (w / 2f + 0.2f), 0.1f, 0.1f);
                c.localScale = new Vector3(0.35f, h + 0.5f, 0.05f);
                c.GetComponent<Renderer>().material.color = cloth;
            }
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int side = rng.Next(4);
                float along = RoomPlacement.WallHalfLength(area, side);
                float half = w / 2f + (curtains ? 0.4f : 0.1f);
                // Curtains and frames keep 0.5 m from the corners (overview cameras).
                if (along < half + 0.6f) continue;
                float u = RoomPlacement.Range(rng, -along + half + 0.5f, along - half - 0.5f);
                float bottom = RoomPlacement.Range(rng, 0.85f, 1.05f);
                Vector3 p = RoomPlacement.WallPoint(area, side, u, 0f) + Vector3.up * (bottom + h / 2f);
                var rot = Quaternion.Euler(0f, RoomPlacement.WallYaw(side), 0f);
                t.position = new Vector3(-1000f, 0f, 0f);
                Physics.SyncTransforms();
                if (!Free(p + rot * Vector3.forward * 0.1f, new Vector3(half, h / 2f + 0.1f, 0.08f), rot)) continue;
                t.SetPositionAndRotation(p, rot);
                Physics.SyncTransforms();
                Placed.Add(curtains ? "창문(커튼)" : "창문");
                return;
            }
            Release(win);
        }

        void PlacePicture(System.Random rng, RoomArea area)
        {
            var pic = Get("Picture", () =>
            {
                var r = Root("Picture");
                Part(r, PrimitiveType.Cube, Vector3.zero, new Vector3(1f, 1f, 0.03f), new Color(0.2f, 0.15f, 0.1f));
                Part(r, PrimitiveType.Cube, new Vector3(0f, 0f, 0.012f), new Vector3(0.85f, 0.8f, 0.01f), Color.white, false);
                return r;
            });
            float w = RoomPlacement.Range(rng, 0.4f, 0.8f), h = RoomPlacement.Range(rng, 0.3f, 0.6f);
            pic.transform.localScale = new Vector3(w, h, 1f);
            Tint(pic, 1, Color.HSVToRGB((float)rng.NextDouble(), 0.4f, 0.8f));
            for (int attempt = 0; attempt < 15; attempt++)
            {
                int side = rng.Next(4);
                float along = RoomPlacement.WallHalfLength(area, side);
                if (along < w) continue;
                float u = RoomPlacement.Range(rng, -along + w, along - w);
                Vector3 p = RoomPlacement.WallPoint(area, side, u, 0.016f) + Vector3.up * RoomPlacement.Range(rng, 1.4f, 1.8f);
                var rot = Quaternion.Euler(0f, RoomPlacement.WallYaw(side), 0f);
                pic.transform.position = new Vector3(-1000f, 0f, 0f);
                Physics.SyncTransforms();
                if (!Free(p + rot * Vector3.forward * 0.05f, new Vector3(w / 2f + 0.05f, h / 2f + 0.05f, 0.04f), rot)) continue;
                pic.transform.SetPositionAndRotation(p, rot);
                Placed.Add("액자");
                return;
            }
            Release(pic);
        }

        void PlaceRug(System.Random rng, RoomArea area, HumanController human)
        {
            var rug = Get("Rug", () =>
            {
                var r = Root("Rug");
                Part(r, PrimitiveType.Cube, new Vector3(0f, 0.005f, 0f), new Vector3(1f, 0.01f, 1f), s_Cloth[0], false);
                return r;
            });
            rug.transform.localScale = new Vector3(RoomPlacement.Range(rng, 1.4f, 2.4f), 1f, RoomPlacement.Range(rng, 1.0f, 1.7f));
            Tint(rug, 0, Color.HSVToRGB((float)rng.NextDouble(), 0.35f, RoomPlacement.Range(rng, 0.4f, 0.8f)));
            Vector3 p = area.RandomFloorPoint(rng, 1.0f);
            rug.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 90f * rng.Next(2), 0f));
            Placed.Add("러그");
        }

        void PlacePillar(System.Random rng, RoomArea area, HumanController human)
        {
            float wdt = RoomPlacement.Range(rng, 0.3f, 0.45f);
            var pillar = Get("Pillar", () =>
            {
                var r = Root("Pillar");
                Part(r, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), Vector3.one, new Color(0.88f, 0.87f, 0.84f));
                return r;
            });
            pillar.transform.localScale = new Vector3(wdt, area.Size.y, wdt);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Vector3 p = area.RandomFloorPoint(rng, 0.9f);
                var rot = Quaternion.identity;
                var fp = new Vector2(wdt, wdt);
                if (RoomPlacement.OnRoute(p, rot, fp, human, 0.8f) || RoomPlacement.InClearZone(p, rot, fp, KeepClear)) continue;
                pillar.transform.position = new Vector3(-1000f, 0f, 0f);
                Physics.SyncTransforms();
                if (!Free(p + Vector3.up * 1.2f, new Vector3(wdt / 2f + 0.05f, 1.1f, wdt / 2f + 0.05f), rot)) continue;
                pillar.transform.SetPositionAndRotation(p, rot);
                Placed.Add("기둥");
                return;
            }
            Release(pillar);
        }

        // -------------------------------------------------------------- clutter

        public void PlaceClutter(System.Random rng, RoomArea area, HumanController human, int count)
        {
            for (int i = 0; i < count; i++)
            {
                switch (rng.Next(8))
                {
                    case 0: PlacePendant(rng, area, human, false); break;
                    case 1: PlacePendant(rng, area, human, true); break;
                    case 2: PlaceShelf(rng, area); break;
                    case 3: PlaceClothesline(rng, area, human); break;
                    case 4: PlaceFloorItem(rng, area, human, "FloorLamp"); break;
                    case 5: PlaceFloorItem(rng, area, human, "Plant"); break;
                    case 6: PlaceFloorItem(rng, area, human, "Boxes"); break;
                    default: PlaceShelf(rng, area); break;
                }
                Physics.SyncTransforms();
            }
        }

        /// <summary>Lamp shade (or a potted plant) hanging on a cable from the ceiling.</summary>
        readonly List<Vector3> m_Hanging = new List<Vector3>();

        bool TooCloseToHanging(Vector3 p)
        {
            foreach (var q in m_Hanging)
                { float dx = p.x - q.x, dz = p.z - q.z; if (dx * dx + dz * dz < 1.44f) return true; }
            return false;
        }

        void PlacePendant(System.Random rng, RoomArea area, HumanController human, bool plant)
        {
            string kind = plant ? "HangingPlant" : "PendantLamp";
            var go = Get(kind, () =>
            {
                var r = Root(kind);
                Part(r, PrimitiveType.Cylinder, Vector3.zero, Vector3.one, new Color(0.1f, 0.1f, 0.1f));          // cable (scaled below)
                if (plant)
                {
                    Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0.1f, 0f), new Vector3(0.22f, 0.1f, 0.22f), new Color(0.75f, 0.45f, 0.3f));
                    Part(r, PrimitiveType.Sphere, new Vector3(0f, 0.12f, 0f), new Vector3(0.45f, 0.35f, 0.45f), new Color(0.25f, 0.5f, 0.25f));
                }
                else
                {
                    Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0.09f, 0f), new Vector3(0.38f, 0.09f, 0.38f), new Color(0.9f, 0.85f, 0.7f));
                }
                return r;
            });
            string label = plant ? "매단 화분" : "펜던트 조명";
            // run18: two pendants and a plant at head height in a 3.8 m office hid the person from
            // three of the four overview cameras. One of each per room; a lamp hangs over a table
            // (where nobody walks) or high up, a plant hangs next to a wall.
            if (Placed.Contains(label)) { Release(go); return; }
            FurnitureItem table = null;
            if (!plant)
                foreach (var f in area.Obstacles.Active)
                {
                    var fi = f.GetComponent<FurnitureItem>();
                    if (fi != null && (fi.category == "Table" || fi.category == "Desk")) { table = fi; break; }
                }
            float h = area.Size.y;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                Vector3 p;
                float bottom;
                if (table != null && attempt < 5)
                {
                    p = table.transform.position + table.transform.rotation * table.centerOffset;
                    p.y = area.transform.position.y;
                    bottom = RoomPlacement.Range(rng, 1.55f, 1.75f);
                }
                else if (plant)
                {
                    // 0.4-0.6 m in front of a wall, away from the corners.
                    int side = rng.Next(4);
                    // 1.3 m from the corners: run19 had a plant filling an overview camera's view.
                    float lim = RoomPlacement.WallHalfLength(area, side) - 1.3f;
                    if (lim < 0f) continue;
                    p = RoomPlacement.WallPoint(area, side, RoomPlacement.Range(rng, -lim, lim), RoomPlacement.Range(rng, 0.4f, 0.6f));
                    bottom = RoomPlacement.Range(rng, 1.75f, 2.0f);
                    if (NearCorner(area, p, 1.2f)) continue; // L-shaped rooms: the corners next to the block
                }
                else
                {
                    // Away from the walls (overview cameras) and from the ceiling light in the middle.
                    p = area.RandomFloorPoint(rng, 0.8f);
                    Vector3 fromCentre = p - area.transform.position;
                    fromCentre.y = 0f;
                    if (fromCentre.magnitude < 0.6f) continue;
                    bottom = RoomPlacement.Range(rng, 1.95f, 2.1f);
                }
                if (TooCloseToHanging(p)) continue;
                float cableLen = h - bottom - 0.2f;
                if (cableLen < 0.1f) continue;
                var cable = go.transform.GetChild(0);
                cable.localPosition = new Vector3(0f, 0.2f + cableLen / 2f, 0f);
                cable.localScale = new Vector3(0.012f, cableLen / 2f, 0.012f);
                go.transform.position = new Vector3(-1000f, 0f, 0f);
                Physics.SyncTransforms();
                if (!Free(p + Vector3.up * (bottom + 0.15f), new Vector3(0.25f, 0.15f, 0.25f), Quaternion.identity)) continue;
                go.transform.SetPositionAndRotation(p + Vector3.up * bottom, Quaternion.identity);
                m_Hanging.Add(p);
                Placed.Add(label);
                return;
            }
            Release(go);
        }

        /// <summary>Wall shelf at 1.2-1.8 m with a few books on it.</summary>
        void PlaceShelf(System.Random rng, RoomArea area)
        {
            var go = Get("Shelf", () =>
            {
                var r = Root("Shelf");
                Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, s_Wood[0]);   // board (scaled below)
                for (int i = 0; i < 5; i++) Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, s_Cloth[i % s_Cloth.Length]);
                return r;
            });
            float w = RoomPlacement.Range(rng, 0.6f, 1.1f), d = 0.22f;
            Transform t = go.transform;
            t.GetChild(0).localPosition = new Vector3(0f, 0f, d / 2f);
            t.GetChild(0).localScale = new Vector3(w, 0.03f, d);
            Tint(go, 0, s_Wood[rng.Next(s_Wood.Length)]);
            float x = -w / 2f + 0.05f;
            for (int i = 1; i <= 5; i++)
            {
                var b = t.GetChild(i);
                float bw = RoomPlacement.Range(rng, 0.03f, 0.07f), bh = RoomPlacement.Range(rng, 0.16f, 0.26f);
                bool on = x + bw < w / 2f - 0.05f && rng.NextDouble() < 0.85;
                b.gameObject.SetActive(on);
                b.localPosition = new Vector3(x + bw / 2f, 0.015f + bh / 2f, d / 2f);
                b.localScale = new Vector3(bw, bh, 0.16f);
                b.GetComponent<Renderer>().material.color = Color.HSVToRGB((float)rng.NextDouble(), 0.5f, 0.6f);
                x += bw + 0.005f;
            }
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int side = rng.Next(4);
                float along = RoomPlacement.WallHalfLength(area, side);
                if (along < w / 2f + 0.1f) continue;
                float u = RoomPlacement.Range(rng, -along + w / 2f + 0.05f, along - w / 2f - 0.05f);
                float y = RoomPlacement.Range(rng, 1.2f, 1.8f);
                Vector3 p = RoomPlacement.WallPoint(area, side, u, 0.002f) + Vector3.up * y;
                var rot = Quaternion.Euler(0f, RoomPlacement.WallYaw(side), 0f);
                t.position = new Vector3(-1000f, 0f, 0f);
                Physics.SyncTransforms();
                if (!Free(p + Vector3.up * 0.15f + rot * Vector3.forward * (d / 2f + 0.01f), new Vector3(w / 2f + 0.03f, 0.17f, d / 2f), rot)) continue;
                t.SetPositionAndRotation(p, rot);
                Placed.Add("벽 선반");
                return;
            }
            Release(go);
        }

        /// <summary>A line from wall to wall at 1.8-2.1 m with towels hanging from it.</summary>
        void PlaceClothesline(System.Random rng, RoomArea area, HumanController human)
        {
            var go = Get("Clothesline", () =>
            {
                var r = Root("Clothesline");
                Part(r, PrimitiveType.Cylinder, Vector3.zero, Vector3.one, new Color(0.9f, 0.9f, 0.9f));   // line (scaled below)
                for (int i = 0; i < 4; i++) Part(r, PrimitiveType.Cube, Vector3.zero, Vector3.one, s_Cloth[i]);
                return r;
            });
            if (Placed.Contains("빨랫줄")) { Release(go); return; } // one per room (run16: two parallel lines)
            for (int attempt = 0; attempt < 10; attempt++)
            {
                bool alongX = rng.NextDouble() < 0.5;
                float len = alongX ? area.Size.x : area.Size.z;
                // At least 1 m from the walls it runs along: the overview cameras sit in the corners.
                float across = (alongX ? area.Size.z : area.Size.x) * 0.5f - 1.0f;
                if (across < 0.1f) continue;
                float off = RoomPlacement.Range(rng, -across, across);
                float y = RoomPlacement.Range(rng, 1.85f, 2.15f);
                var rot = Quaternion.Euler(0f, alongX ? 90f : 0f, 0f); // local Z along the line
                Vector3 c = area.transform.position + (alongX ? new Vector3(0f, y, off) : new Vector3(off, y, 0f));
                Transform t = go.transform;
                t.position = new Vector3(-1000f, 0f, 0f);
                var line = t.GetChild(0);
                line.localRotation = Quaternion.Euler(90f, 0f, 0f);
                line.localPosition = Vector3.zero;
                line.localScale = new Vector3(0.008f, len / 2f, 0.008f);
                bool ok = true;
                int towels = 2 + rng.Next(3);
                for (int i = 1; i <= 4; i++)
                {
                    var towel = t.GetChild(i);
                    bool on = i <= towels;
                    towel.gameObject.SetActive(on);
                    if (!on) continue;
                    // Hand-towel size, kept off the last 0.9 m at each wall (overview cameras in the corners).
                    float tw = RoomPlacement.Range(rng, 0.22f, 0.38f), th = RoomPlacement.Range(rng, 0.28f, 0.45f);
                    float z = RoomPlacement.Range(rng, -len / 2f + 0.9f, len / 2f - 0.9f);
                    towel.localPosition = new Vector3(0f, -th / 2f, z);
                    towel.localScale = new Vector3(0.01f, th, tw);
                    towel.localRotation = Quaternion.identity;
                    towel.GetComponent<Renderer>().material.color = s_Cloth[rng.Next(s_Cloth.Length)];
                    // Towels must not hang into the human's head height on the route.
                    Vector3 wp = c + rot * new Vector3(0f, 0f, z);
                    if (y - th < 1.85f && RoomPlacement.OnRoute(new Vector3(wp.x, area.transform.position.y, wp.z), rot,
                            new Vector2(0.05f, tw), human, 0.4f)) ok = false;
                }
                if (!ok) continue;
                Physics.SyncTransforms();
                // Only the towels are tested for overlap; the line passes over furniture.
                if (!Free(c + Vector3.down * 0.3f, new Vector3(0.02f, 0.25f, len / 2f - 0.35f), rot)) continue;
                t.SetPositionAndRotation(c, rot);
                Placed.Add("빨랫줄");
                return;
            }
            Release(go);
        }

        void PlaceFloorItem(System.Random rng, RoomArea area, HumanController human, string kind)
        {
            var go = Get(kind, () =>
            {
                var r = Root(kind);
                switch (kind)
                {
                    case "FloorLamp":
                        Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.02f, 0.3f), new Color(0.2f, 0.2f, 0.2f));
                        Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0.8f, 0f), new Vector3(0.03f, 0.8f, 0.03f), new Color(0.2f, 0.2f, 0.2f));
                        Part(r, PrimitiveType.Cylinder, new Vector3(0f, 1.6f, 0f), new Vector3(0.4f, 0.14f, 0.4f), new Color(0.95f, 0.9f, 0.75f));
                        break;
                    case "Plant":
                        Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f), new Vector3(0.32f, 0.18f, 0.32f), new Color(0.7f, 0.42f, 0.28f));
                        Part(r, PrimitiveType.Sphere, new Vector3(0f, 0.65f, 0f), new Vector3(0.55f, 0.7f, 0.55f), new Color(0.2f, 0.5f, 0.25f));
                        Part(r, PrimitiveType.Sphere, new Vector3(0.12f, 0.95f, 0.05f), new Vector3(0.35f, 0.45f, 0.35f), new Color(0.25f, 0.55f, 0.28f));
                        break;
                    default: // Boxes
                        Part(r, PrimitiveType.Cube, new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 0.4f), new Color(0.65f, 0.5f, 0.32f));
                        Part(r, PrimitiveType.Cube, new Vector3(0.03f, 0.58f, -0.02f), new Vector3(0.42f, 0.36f, 0.36f), new Color(0.7f, 0.55f, 0.36f));
                        Part(r, PrimitiveType.Cube, new Vector3(-0.02f, 0.9f, 0.01f), new Vector3(0.34f, 0.28f, 0.3f), new Color(0.62f, 0.48f, 0.3f));
                        break;
                }
                return r;
            });
            float s = RoomPlacement.Range(rng, 0.85f, 1.15f);
            go.transform.localScale = Vector3.one * s;
            Vector2 fp = kind == "Boxes" ? new Vector2(0.5f, 0.4f) * s : new Vector2(0.45f, 0.45f) * s;
            float height = kind == "FloorLamp" ? 1.75f : kind == "Plant" ? 1.2f : 1.05f;
            if (kind == "Boxes")
            {
                int n = 1 + rng.Next(3);
                for (int i = 0; i < 3; i++) go.transform.GetChild(i).gameObject.SetActive(i < n);
                height = n == 1 ? 0.4f : n == 2 ? 0.76f : 1.05f;
            }
            height *= s;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                // Mostly along the walls and in corners, sometimes in the open.
                Vector3 p;
                float yaw = 90f * rng.Next(4) + RoomPlacement.Range(rng, -15f, 15f);
                if (rng.NextDouble() < 0.7)
                {
                    int side = rng.Next(4);
                    float along = RoomPlacement.WallHalfLength(area, side) - fp.x;
                    p = RoomPlacement.WallPoint(area, side, RoomPlacement.Range(rng, -along, along), fp.y / 2f + 0.08f);
                }
                else p = area.RandomFloorPoint(rng, 0.5f);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                if (RoomPlacement.OnRoute(p, rot, fp, human, 0.8f) || RoomPlacement.InClearZone(p, rot, fp, KeepClear)) continue;
                if (height > 1.1f && NearCorner(area, p, 0.9f)) continue;
                go.transform.position = new Vector3(-1000f, 0f, 0f);
                Physics.SyncTransforms();
                if (!Free(p + Vector3.up * (height / 2f + 0.02f), new Vector3(fp.x / 2f, height / 2f - 0.01f, fp.y / 2f), rot)) continue;
                go.transform.SetPositionAndRotation(p, rot);
                Placed.Add(kind == "FloorLamp" ? "스탠드" : kind == "Plant" ? "화분" : "상자");
                return;
            }
            Release(go);
        }
    }
}
