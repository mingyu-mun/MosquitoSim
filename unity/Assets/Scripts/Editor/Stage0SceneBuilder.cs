using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MosquitoSim.EditorTools
{
    /// <summary>
    /// Menu: MosquitoSim > Build Stage 0 Scene.
    /// Builds Assets/Scenes/Stage0.unity with 16 copies of a 4 x 3 m room, a
    /// standing human with 5 skin zones, and a mosquito agent in each room.
    /// Scene files are generated instead of committed by hand so that the
    /// setup stays reviewable in code.
    /// </summary>
    public static class Stage0SceneBuilder
    {
        const int k_Rooms = 16;
        const float k_Spacing = 10f;
        const string k_ScenePath = "Assets/Scenes/Stage0.unity";

        [MenuItem("MosquitoSim/Build Stage 0 Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            new GameObject("SimSettings").AddComponent<SimSettings>();

            int perRow = Mathf.CeilToInt(Mathf.Sqrt(k_Rooms));
            for (int i = 0; i < k_Rooms; i++)
            {
                var origin = new Vector3((i % perRow) * k_Spacing, 0f, (i / perRow) * k_Spacing);
                BuildRoom(i, origin);
            }

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 2.2f, -2.8f);
                cam.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
                cam.nearClipPlane = 0.01f;
            }

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            Debug.Log($"Saved {k_ScenePath} with {k_Rooms} rooms");
        }

        static void BuildRoom(int index, Vector3 origin)
        {
            var size = new Vector3(4f, 2.5f, 3f);
            var room = new GameObject($"Room_{index:00}");
            room.transform.position = origin;
            var area = room.AddComponent<RoomArea>();
            area.roomSize = size;

            const float t = 0.1f;
            Box("Floor", room.transform, new Vector3(0, -t / 2, 0), new Vector3(size.x, t, size.z));
            Box("Ceiling", room.transform, new Vector3(0, size.y + t / 2, 0), new Vector3(size.x, t, size.z));
            Box("Wall_N", room.transform, new Vector3(0, size.y / 2, size.z / 2 + t / 2), new Vector3(size.x, size.y, t));
            Box("Wall_S", room.transform, new Vector3(0, size.y / 2, -size.z / 2 - t / 2), new Vector3(size.x, size.y, t));
            Box("Wall_E", room.transform, new Vector3(size.x / 2 + t / 2, size.y / 2, 0), new Vector3(t, size.y, size.z));
            Box("Wall_W", room.transform, new Vector3(-size.x / 2 - t / 2, size.y / 2, 0), new Vector3(t, size.y, size.z));

            Transform human = BuildHuman(room.transform);
            area.human = human;
            area.skinZones.AddRange(human.GetComponentsInChildren<SkinZone>());

            BuildMosquito(room.transform);
        }

        static Transform BuildHuman(Transform parent)
        {
            var human = new GameObject("Human").transform;
            human.SetParent(parent, false);

            // Non-skin body parts are obstacles.
            Prim(PrimitiveType.Capsule, "Torso", human, new Vector3(0, 1.15f, 0), new Vector3(0.35f, 0.4f, 0.25f));
            Prim(PrimitiveType.Capsule, "Thigh_L", human, new Vector3(-0.1f, 0.7f, 0), new Vector3(0.14f, 0.22f, 0.14f));
            Prim(PrimitiveType.Capsule, "Thigh_R", human, new Vector3(0.1f, 0.7f, 0), new Vector3(0.14f, 0.22f, 0.14f));
            Prim(PrimitiveType.Capsule, "UpperArm_L", human, new Vector3(-0.25f, 1.3f, 0), new Vector3(0.08f, 0.15f, 0.08f));
            Prim(PrimitiveType.Capsule, "UpperArm_R", human, new Vector3(0.25f, 1.3f, 0), new Vector3(0.08f, 0.15f, 0.08f));

            Skin(PrimitiveType.Sphere, "Head", "머리", human, new Vector3(0, 1.6f, 0), Vector3.one * 0.22f);
            Skin(PrimitiveType.Capsule, "Forearm_L", "왼팔", human, new Vector3(-0.25f, 1.0f, 0), new Vector3(0.07f, 0.14f, 0.07f));
            Skin(PrimitiveType.Capsule, "Forearm_R", "오른팔", human, new Vector3(0.25f, 1.0f, 0), new Vector3(0.07f, 0.14f, 0.07f));
            Skin(PrimitiveType.Capsule, "Calf_L", "왼쪽 종아리", human, new Vector3(-0.1f, 0.25f, 0), new Vector3(0.1f, 0.22f, 0.1f));
            Skin(PrimitiveType.Capsule, "Calf_R", "오른쪽 종아리", human, new Vector3(0.1f, 0.25f, 0), new Vector3(0.1f, 0.22f, 0.1f));
            return human;
        }

        static void BuildMosquito(Transform parent)
        {
            var go = new GameObject("Mosquito");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(1.2f, 1.5f, 0.8f);

            // Visual only: 5 mm sphere without a collider.
            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Visual";
            Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = Vector3.one * 0.005f;

            go.AddComponent<MosquitoMotor>();
            var agent = go.AddComponent<MosquitoAgent>();
            agent.MaxStep = Mathf.RoundToInt(agent.episodeSeconds / 0.01f);

            var bp = go.GetComponent<BehaviorParameters>();
            if (bp == null) bp = go.AddComponent<BehaviorParameters>();
            bp.BehaviorName = "Mosquito";
            bp.BrainParameters.VectorObservationSize = ObsLayout.ObsSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(3);

            var dr = go.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 5;
            dr.TakeActionsBetweenDecisions = true;
        }

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale)
        {
            return Prim(PrimitiveType.Cube, name, parent, localPos, scale);
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.isStatic = parent.name.StartsWith("Room_");
            return go;
        }

        static void Skin(PrimitiveType type, string name, string label, Transform parent, Vector3 localPos, Vector3 scale)
        {
            var go = Prim(type, name, parent, localPos, scale);
            go.AddComponent<SkinZone>().zoneName = label;
        }
    }
}
