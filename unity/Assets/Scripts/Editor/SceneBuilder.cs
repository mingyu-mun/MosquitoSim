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
    /// Menu: MosquitoSim > Build Main Scene.
    /// Builds Assets/Scenes/Main.unity: 16 rooms 12 m apart. Each room has the
    /// room components (shell, furniture, candidates, cues), a net, a human
    /// with 5 skin zones (S1..S5) and a hand, a mosquito agent and the five
    /// planner cameras. Room shape, furniture and everything else that changes
    /// per episode is generated at runtime from environment parameters, so the
    /// same scene serves every stage and split.
    /// </summary>
    public static class SceneBuilder
    {
        const int k_Rooms = 16;
        const float k_Spacing = 12f;
        const string k_ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("MosquitoSim/Build Main Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildWithoutPrompt();
        }

        /// <summary>Build and save the scene without asking (used by AutoTestWatcher).</summary>
        public static void BuildWithoutPrompt()
        {
            URPSetup.Ensure(); // URP before any material is made (run05 switch, 11/21)
            // Licensed models in Assets/ThirdParty (not in git): animated human and mosquito model.
            HumanAssets.EnsureImported();
            s_HumanCtrl = HumanAssets.HasCharacter ? HumanAssets.Controller(out s_WalkSpeed, out s_TurnLength) : null;
            s_MosquitoModel = HumanAssets.HasMosquito;
            s_Furniture = FurnitureCatalog.Ensure();
            s_FurnitureLog.Clear();
            Debug.Log($"SceneBuilder: human = {(s_HumanCtrl != null ? "Mixamo character" : "primitives")}, " +
                      $"mosquito = {(s_MosquitoModel ? "model" : "sphere")}");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // Lighting: a weak sun without shadows (thin ceilings leaked shadow-map
            // light as streaks in run01/run02), one ceiling light per room, flat ambient.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.shadows = LightShadows.None;
                l.intensity = 0.3f;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.32f);

            var global = new GameObject("Global");
            global.AddComponent<SimSettings>();
            global.AddComponent<PlannerChannelRegistrar>();

            int perRow = Mathf.CeilToInt(Mathf.Sqrt(k_Rooms));
            for (int i = 0; i < k_Rooms; i++)
            {
                var origin = new Vector3((i % perRow) * k_Spacing, 0f, (i / perRow) * k_Spacing);
                BuildRoom(i, origin);
            }

            var cam = Camera.main;
            if (cam != null)
            {
                // Inside room 0 (the smallest room is 4 x 3 m), looking down at the centre.
                cam.transform.position = new Vector3(0f, 2.2f, -1.3f);
                cam.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
                cam.nearClipPlane = 0.01f;
                cam.cullingMask = Layers.AllButLabels;
            }

            // Furniture sizes of room 0 for checking (AutoTest/furniture_catalog.txt).
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "AutoTest"));
                System.IO.Directory.CreateDirectory(dir);
                s_FurnitureLog.Insert(0, s_Furniture ? "Furniture Mega Pack: 방 0의 가구 (폭 x 높이 x 깊이, 크기 보정 후)" : "가구 팩 없음: 도형 가구");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "furniture_catalog.txt"), s_FurnitureLog);
                if (s_Furniture)
                {
                    var tmp = new GameObject("SurveyTmp");
                    var survey = new System.Collections.Generic.List<string> { "원본 크기 (폭x높이x깊이 m)" };
                    FurnitureCatalog.Survey(tmp.transform, survey);
                    Object.DestroyImmediate(tmp);
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "furniture_survey.txt"), survey);
                }
            }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            Debug.Log($"Saved {k_ScenePath} with {k_Rooms} rooms");
        }

        static void BuildRoom(int index, Vector3 origin)
        {
            var room = new GameObject($"Room_{index:00}");
            room.transform.position = origin;
            var area = room.AddComponent<RoomArea>(); // adds generator, spawner, candidates, cues
            area.roomIndex = index; // walls are built at runtime by RoomGenerator

            var netGo = new GameObject("Net");
            netGo.transform.SetParent(room.transform, false);
            area.net = netGo.AddComponent<NetObstacle>();

            if (s_Furniture) FurnitureCatalog.BuildRoomFurniture(room.transform, index, s_FurnitureLog);

            area.human = BuildHuman(room.transform, out HandController hand);
            area.hand = hand;
            area.cameras = BuildCameras(room.transform);

            // Ceiling light (URP point light; falloff is inverse-square, so the
            // intensity is higher than a built-in pipeline light would need).
            var lightGo = new GameObject("CeilingLight");
            lightGo.transform.SetParent(room.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, RoomGenerator.Height - 0.3f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = 2.2f; // run06: 6 blew out the floor and faces
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.None;
            BuildMosquito(room.transform);
        }

        static RuntimeAnimatorController s_HumanCtrl;
        static float s_WalkSpeed, s_TurnLength;
        static bool s_MosquitoModel, s_Furniture;
        static readonly System.Collections.Generic.List<string> s_FurnitureLog = new System.Collections.Generic.List<string>();

        static HumanController BuildHuman(Transform parent, out HandController hand)
        {
            if (s_HumanCtrl != null) return BuildCharacterHuman(parent, out hand);
            var root = new GameObject("Human");
            root.transform.SetParent(parent, false);
            root.layer = Layers.Human;
            var human = root.AddComponent<HumanController>();
            var t = root.transform;

            // Non-skin body parts are obstacles.
            Prim(PrimitiveType.Capsule, "Torso", t, new Vector3(0f, 1.15f, 0f), new Vector3(0.35f, 0.4f, 0.25f), true);
            Prim(PrimitiveType.Capsule, "Thigh_L", t, new Vector3(-0.1f, 0.7f, 0f), new Vector3(0.14f, 0.22f, 0.14f), true);
            Prim(PrimitiveType.Capsule, "Thigh_R", t, new Vector3(0.1f, 0.7f, 0f), new Vector3(0.14f, 0.22f, 0.14f), true);
            Prim(PrimitiveType.Capsule, "UpperArm_L", t, new Vector3(-0.25f, 1.3f, 0f), new Vector3(0.08f, 0.15f, 0.08f), true);
            Prim(PrimitiveType.Capsule, "UpperArm_R", t, new Vector3(0.25f, 1.3f, 0f), new Vector3(0.08f, 0.15f, 0.08f), true);

            var head = Skin(PrimitiveType.Sphere, "Head", "S1", "머리", t, new Vector3(0f, 1.6f, 0f), Vector3.one * 0.22f);
            Skin(PrimitiveType.Capsule, "Forearm_L", "S2", "왼팔", t, new Vector3(-0.25f, 1.0f, 0f), new Vector3(0.07f, 0.14f, 0.07f));
            Skin(PrimitiveType.Capsule, "Forearm_R", "S3", "오른팔", t, new Vector3(0.25f, 1.0f, 0f), new Vector3(0.07f, 0.14f, 0.07f));
            Skin(PrimitiveType.Capsule, "Calf_L", "S4", "왼쪽 종아리", t, new Vector3(-0.1f, 0.25f, 0f), new Vector3(0.1f, 0.22f, 0.1f));
            Skin(PrimitiveType.Capsule, "Calf_R", "S5", "오른쪽 종아리", t, new Vector3(0.1f, 0.25f, 0f), new Vector3(0.1f, 0.22f, 0.1f));
            human.head = head.transform;
            human.skins.AddRange(root.GetComponentsInChildren<SkinZone>());

            // Swatting hand: visual sphere without a collider, moved by HandController.
            var handVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handVis.name = "Hand";
            Object.DestroyImmediate(handVis.GetComponent<Collider>());
            handVis.transform.SetParent(parent, false);
            handVis.transform.localScale = Vector3.one * 0.1f;
            handVis.GetComponent<Renderer>().sharedMaterial = SkinMaterial();
            hand = root.AddComponent<HandController>();
            hand.human = human;
            hand.hand = handVis.transform;
            return human;
        }

        static PlannerCameras BuildCameras(Transform parent)
        {
            var go = new GameObject("PlannerCameras");
            go.transform.SetParent(parent, false);
            var pc = go.AddComponent<PlannerCameras>();
            pc.mosquitoCam = NewCamera("MosquitoCam", go.transform, 90f);
            for (int i = 0; i < 4; i++) pc.overviewCams[i] = NewCamera("OverviewCam" + i, go.transform, 75f);
            return pc;
        }

        static Camera NewCamera(string camName, Transform parent, float fov)
        {
            var go = new GameObject(camName);
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; // rendered on demand by PlannerCameras
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.005f;
            cam.farClipPlane = 20f;
            cam.cullingMask = Layers.PlannerView; // labels yes, debug drawings no
            return cam;
        }

        /// <summary>
        /// Mixamo character: the skin zones and body colliders are made on the bones
        /// at start (HumanAvatar), the swatting hand is the character's right hand (IK).
        /// </summary>
        static HumanController BuildCharacterHuman(Transform parent, out HandController hand)
        {
            var root = new GameObject("Human");
            root.transform.SetParent(parent, false);
            root.layer = Layers.Human;
            var human = root.AddComponent<HumanController>();
            var character = HumanAssets.InstantiateCharacter(root.transform, s_HumanCtrl);
            var avatar = character.AddComponent<HumanAvatar>();
            avatar.human = human;
            avatar.walkClipSpeed = s_WalkSpeed;
            avatar.turnClipLength = s_TurnLength;

            // HandController still needs a point to move; it is invisible here.
            var handPoint = new GameObject("Hand");
            handPoint.transform.SetParent(parent, false);
            hand = root.AddComponent<HandController>();
            hand.human = human;
            hand.hand = handPoint.transform;
            avatar.hand = hand;
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
            if (s_MosquitoModel)
            {
                Object.DestroyImmediate(vis);
                var pivot = new GameObject("Visual");
                pivot.transform.SetParent(go.transform, false);
                HumanAssets.InstantiateMosquito(pivot.transform, out var wings, out var rest, out var spread);
                var visual = go.AddComponent<MosquitoVisual>();
                visual.pivot = pivot.transform;
                visual.wings = wings;
                visual.wingRest = rest;
                visual.wingSpread = spread;
            }

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

        static GameObject Prim(PrimitiveType type, string objName, Transform parent, Vector3 localPos, Vector3 scale, bool human)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (human)
            {
                go.layer = Layers.Human;
                go.GetComponent<Renderer>().sharedMaterial = ClothMaterial();
            }
            return go;
        }

        static GameObject Skin(PrimitiveType type, string objName, string id, string label, Transform parent, Vector3 localPos, Vector3 scale)
        {
            var go = Prim(type, objName, parent, localPos, scale, true);
            go.GetComponent<Renderer>().sharedMaterial = SkinMaterial();
            var z = go.AddComponent<SkinZone>();
            z.id = id;
            z.zoneName = label;
            return go;
        }

        static Material s_Skin, s_Cloth;

        static Material SkinMaterial()
        {
            if (s_Skin == null) s_Skin = NewMaterial("MosquitoSim_Skin", new Color(0.93f, 0.76f, 0.64f));
            return s_Skin;
        }

        static Material ClothMaterial()
        {
            if (s_Cloth == null) s_Cloth = NewMaterial("MosquitoSim_Cloth", new Color(0.3f, 0.35f, 0.45f));
            return s_Cloth;
        }

        static Material NewMaterial(string matName, Color color)
        {
            System.IO.Directory.CreateDirectory("Assets/Materials");
            string path = "Assets/Materials/" + matName + ".mat";
            // The pipeline's default material (URP/Lit under URP).
            var src = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var def = src.GetComponent<Renderer>().sharedMaterial;
            Object.DestroyImmediate(src);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                // Made under another pipeline (Standard shader shows pink in URP): switch it.
                if (mat.shader != def.shader)
                {
                    mat.shader = def.shader;
                    mat.color = color;
                    EditorUtility.SetDirty(mat);
                    AssetDatabase.SaveAssets();
                }
                return mat;
            }
            mat = new Material(def) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
