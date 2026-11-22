// Furniture Mega Pack (Unity Asset Store, under Assets/ThirdParty; not in git).
// The scene builder puts one set of items in every room, inactive; ObstacleSpawner
// picks and places them per episode by room theme. Each room gets different
// model variants (chosen from the room index) so the 16 rooms look different.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MosquitoSim.EditorTools
{
    public static class FurnitureCatalog
    {
        public const string PackFolder = ThirdPartyImport.Root + "Furniture Mega Pack";
        const string ImportedFolder = "Assets/Furniture Mega Pack";
        const string Prefabs = PackFolder + "/Prefabs/";

        struct Kind
        {
            public string Category, Folder, Prefix;
            public bool SetB;
            public int Copies;          // model variants per room (an episode picks among them)
            public char Dim;            // H: height, L: longest side
            public float Metres;        // typical size on that dimension
            public float MaxLong, MaxShort; // footprint limits after scaling
            public Kind(string cat, string folder, string prefix, bool setB, int copies, char dim, float metres,
                float maxLong = 2.3f, float maxShort = 1.6f)
            {
                Category = cat; Folder = folder; Prefix = prefix; SetB = setB; Copies = copies;
                Dim = dim; Metres = metres; MaxLong = maxLong; MaxShort = maxShort;
            }
        }

        // The pack's models come in inconsistent scales (run10/11: 2 m chairs, a 1.8 m
        // toilet, 5.6 m closets): every model is scaled uniformly so its key dimension
        // is the typical one for its category. Several categories reuse a folder at
        // another size (nightstands are small drawers, desks and coffee tables are tables).
        static readonly Kind[] Kinds =
        {
            // Set A: training and most evaluations
            new Kind("Bed", "Beds", "Bed", false, 2, 'L', 2.05f),
            new Kind("Sofa", "Sofas", "Sofa", false, 2, 'H', 0.85f),
            new Kind("Armchair", "Sofas", "Sofa", false, 1, 'H', 0.85f, 1.15f, 1.0f),
            new Kind("Table", "Tables", "Table", false, 1, 'H', 0.75f, 1.8f, 1.1f),
            new Kind("Desk", "Tables", "Table", false, 2, 'H', 0.75f, 1.5f, 0.8f),
            new Kind("CoffeeTable", "Tables", "Table", false, 1, 'H', 0.42f, 1.3f, 0.8f),
            new Kind("Chair", "Chairs", "Chair", false, 5, 'H', 0.9f, 0.7f, 0.7f),
            new Kind("Closet", "Closets", "Closet", false, 2, 'H', 2.0f, 1.6f, 0.7f),
            new Kind("Drawer", "Drawers", "Drawer", false, 2, 'H', 0.9f, 1.5f, 0.6f),
            new Kind("Nightstand", "Drawers", "Drawer", false, 2, 'H', 0.55f, 0.65f, 0.55f),
            new Kind("Cushion", "Cushioins", "Cushion", false, 3, 'L', 0.45f, 0.6f, 0.6f),
            // Set B: new-object evaluation (never seen in training)
            new Kind("Refrigerator", "Kitchen", "Refrigerator", true, 1, 'H', 1.8f),
            new Kind("Cabinet", "Kitchen", "Cabinet", true, 4, 'H', 0.9f),
            new Kind("Oven", "Kitchen", "KitchenOven", true, 1, 'H', 0.9f),
            new Kind("BathTub", "Bathroom", "BathTub", true, 1, 'L', 1.7f),
            new Kind("Toilet", "Bathroom", "Toilet", true, 1, 'H', 0.78f),
            new Kind("WashBasin", "Bathroom", "WashBasin", true, 1, 'H', 0.85f),
        };

        /// <summary>
        /// Yaw that turns each category's front to +Z. 0 assumes the model faces +Z;
        /// fixed per category from the auto test front shots (09_front_*).
        /// </summary>
        static readonly Dictionary<string, float> FrontYaw = new Dictionary<string, float>
        {
            { "Drawer", 180f }, { "Nightstand", 180f }, // run10: the back faced the room
        };

        const float MaxHeight = 2.2f, MinHeight = 0.15f;

        /// <summary>Move the pack under ThirdParty (git-ignored) the first time it is seen.</summary>
        public static bool Ensure()
        {
            if (AssetDatabase.IsValidFolder(ImportedFolder) && !AssetDatabase.IsValidFolder(PackFolder))
            {
                string err = AssetDatabase.MoveAsset(ImportedFolder, PackFolder);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning("FurnitureCatalog: move failed: " + err);
                else Debug.Log("FurnitureCatalog: moved " + ImportedFolder + " to " + PackFolder);
            }
            return AssetDatabase.IsValidFolder(PackFolder + "/Prefabs");
        }

        static readonly Dictionary<string, List<string>> s_Paths = new Dictionary<string, List<string>>();

        static List<string> Candidates(Kind k)
        {
            string key = k.Folder + "/" + k.Prefix;
            if (s_Paths.TryGetValue(key, out var list)) return list;
            list = AssetDatabase.FindAssets("t:Prefab", new[] { Prefabs + k.Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p =>
                {
                    string n = System.IO.Path.GetFileNameWithoutExtension(p);
                    return n.StartsWith(k.Prefix) && !n.Contains("Corner") && !n.Contains("Sink");
                })
                .OrderBy(p => p).ToList();
            s_Paths[key] = list;
            return list;
        }

        /// <summary>Instantiate this room's items under a "Furniture" child, inactive.</summary>
        public static int BuildRoomFurniture(Transform room, int roomIndex, List<string> log)
        {
            var parent = new GameObject("Furniture").transform;
            parent.SetParent(room, false);
            int made = 0;
            for (int ki = 0; ki < Kinds.Length; ki++)
            {
                Kind k = Kinds[ki];
                var paths = Candidates(k);
                if (paths.Count == 0) { log?.Add($"{k.Category}: 프리팹 없음"); continue; }
                var used = new HashSet<string>();
                for (int copy = 0; copy < k.Copies; copy++)
                {
                    // Room-dependent variant; skip models that do not fit a small room.
                    int start = (roomIndex * 7 + ki * 3 + copy * 11) % paths.Count;
                    for (int t = 0; t < paths.Count; t++)
                    {
                        string path = paths[(start + t) % paths.Count];
                        var item = Make(path, k, parent);
                        if (item == null) continue;
                        Vector2 fp = item.Footprint;
                        bool fits = Mathf.Max(fp.x, fp.y) <= k.MaxLong && Mathf.Min(fp.x, fp.y) <= k.MaxShort &&
                                    item.size.y <= MaxHeight && item.size.y >= MinHeight && !used.Contains(path);
                        if (!fits)
                        {
                            Object.DestroyImmediate(item.gameObject);
                            continue;
                        }
                        if (roomIndex == 0)
                            log?.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "{0} {1}: {2:F2} x {3:F2} x {4:F2} m", k.Category, item.name, item.size.x, item.size.y, item.size.z));
                        used.Add(path);
                        made++;
                        break;
                    }
                }
            }
            return made;
        }

        static Bounds Measure(Renderer[] renderers)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Raw sizes of every model of every category (AutoTest/furniture_survey.txt).</summary>
        public static void Survey(Transform tmpParent, List<string> lines)
        {
            foreach (var k in Kinds)
            {
                var sizes = new List<string>();
                foreach (var path in Candidates(k))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tmpParent);
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length > 0)
                    {
                        Bounds b = Measure(rs);
                        sizes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:F2}x{2:F2}x{3:F2}",
                            go.name.Replace(k.Prefix, ""), b.size.x, b.size.y, b.size.z));
                    }
                    Object.DestroyImmediate(go);
                }
                lines.Add($"[{k.Category}] " + string.Join(", ", sizes));
            }
        }

        static FurnitureItem Make(string path, Kind k, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Object.DestroyImmediate(go); return null; }
            Bounds b = Measure(renderers);
            {
                float cur = k.Dim == 'H' ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                float ratio = cur > 1e-3f ? k.Metres / cur : 1f;
                if (ratio < 0.85f || ratio > 1.18f)
                {
                    go.transform.localScale *= ratio;
                    b = Measure(renderers);
                }
            }

            // The pack has no colliders: one mesh collider per mesh (static, so concave is fine).
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Default;

            var item = go.AddComponent<FurnitureItem>();
            item.category = k.Category;
            item.setB = k.SetB;
            item.placement = FurnitureItem.Placement.Free; // layouts decide (ObstacleSpawner)
            item.size = b.size;
            item.centerOffset = b.center - go.transform.position;
            item.frontYaw = FrontYaw.TryGetValue(k.Category, out float fy) ? fy : 0f;
            go.SetActive(false);
            return item;
        }
    }
}
