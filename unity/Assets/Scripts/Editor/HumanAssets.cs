// Licensed character and mosquito models for the scene builder. The files live in
// Assets/ThirdParty/ (Mixamo and Sketchfab; not in git). When they are missing the
// scene builder falls back to the primitive human and the 5 mm sphere mosquito.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MosquitoSim.EditorTools
{
    public static class HumanAssets
    {
        public const string CharacterPath = ThirdPartyImport.Root + "Ch42_nonPBR.fbx";
        public const string IdlePath = ThirdPartyImport.Root + "Standing Idle.fbx";
        public const string WalkPath = ThirdPartyImport.Root + "Walking.fbx";
        public const string TurnPath = ThirdPartyImport.Root + "Walking Turn 180.fbx";
        public const string MosquitoPath = ThirdPartyImport.Root + "mosquito/source/MOSQUITO.fbx";
        public const string MosquitoTexture = ThirdPartyImport.Root + "mosquito/textures/dif.jpg";
        const string GeneratedDir = ThirdPartyImport.Root + "_Generated";
        const string ControllerPath = GeneratedDir + "/Human.controller";
        const string MosquitoMatPath = GeneratedDir + "/Mosquito.mat";

        public const float DefaultWalkSpeed = 1.3f; // m/s, used if the clip reports no root motion

        public static bool HasCharacter =>
            AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath) != null &&
            AssetDatabase.LoadAssetAtPath<GameObject>(IdlePath) != null &&
            AssetDatabase.LoadAssetAtPath<GameObject>(WalkPath) != null;

        public static bool HasMosquito => AssetDatabase.LoadAssetAtPath<GameObject>(MosquitoPath) != null;

        const string TextureDir = ThirdPartyImport.Root + "Textures";

        /// <summary>Reimport files that were imported before ThirdPartyImport existed.</summary>
        public static void EnsureImported()
        {
            // Mixamo embeds the textures in the FBX; the URP material importer only links
            // textures that exist as assets (run08: the character came out untextured).
            if (AssetImporter.GetAtPath(CharacterPath) is ModelImporter ci &&
                (!AssetDatabase.IsValidFolder(TextureDir) || AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }).Length == 0))
            {
                System.IO.Directory.CreateDirectory(TextureDir);
                if (ci.ExtractTextures(TextureDir))
                {
                    AssetDatabase.Refresh();
                    AssetDatabase.ImportAsset(CharacterPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
                Debug.Log("HumanAssets: textures extracted to " + TextureDir + ": " +
                          AssetDatabase.FindAssets("t:Texture2D", new[] { TextureDir }).Length);
            }
            foreach (var p in new[] { CharacterPath, IdlePath, WalkPath, TurnPath })
            {
                if (!(AssetImporter.GetAtPath(p) is ModelImporter mi)) continue;
                bool turnWrong = p == TurnPath && mi.clipAnimations.Length > 0 && mi.clipAnimations[0].loopTime;
                if (mi.animationType != ModelImporterAnimationType.Human || turnWrong)
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            if (AssetImporter.GetAtPath(MosquitoPath) is ModelImporter mm && mm.animationType != ModelImporterAnimationType.Legacy)
                AssetDatabase.ImportAsset(MosquitoPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>
        /// Animator controller: one 1D blend tree on "Speed" (m/s) from idle (0) to
        /// walk (the walk clip's own speed), IK pass on for look-at and the swat.
        /// </summary>
        public static RuntimeAnimatorController Controller(out float walkSpeed, out float turnLength)
        {
            var idle = Clip(IdlePath);
            var walk = Clip(WalkPath);
            var turn = Clip(TurnPath);
            turnLength = turn != null ? turn.length : 0f;
            walkSpeed = walk != null && walk.averageSpeed.magnitude > 0.3f ? walk.averageSpeed.magnitude : DefaultWalkSpeed;

            System.IO.Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter(HumanAvatar.SpeedParam, AnimatorControllerParameterType.Float);
            ctrl.AddParameter(HumanAvatar.TurnParam, AnimatorControllerParameterType.Bool);
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = HumanAvatar.SpeedParam;
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, walkSpeed);
            if (turn != null)
            {
                var sm = ctrl.layers[0].stateMachine;
                var turnState = sm.AddState("Turn");
                turnState.motion = turn;
                var go = loco.AddTransition(turnState);
                go.hasExitTime = false;
                go.duration = 0.15f;
                go.AddCondition(AnimatorConditionMode.If, 0f, HumanAvatar.TurnParam);
                var back = turnState.AddTransition(loco);
                back.hasExitTime = false;
                back.duration = 0.2f;
                back.AddCondition(AnimatorConditionMode.IfNot, 0f, HumanAvatar.TurnParam);
            }
            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        /// <summary>Character instance under the given human root, animated in the physics step.</summary>
        public static GameObject InstantiateCharacter(Transform parent, RuntimeAnimatorController ctrl)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = "Character";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Human;
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.updateMode = AnimatorUpdateMode.Fixed;       // bones (and skin colliders) move in the physics step
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; // colliders must move even when nobody looks
            // Saved scene shows the standing pose instead of the T-pose of the model file.
            var idle = Clip(IdlePath);
            if (idle != null)
            {
                try { idle.SampleAnimation(go, 0f); }
                catch (System.Exception e) { Debug.LogWarning("HumanAssets: idle pose sample failed: " + e.Message); }
            }
            return go;
        }

        /// <summary>
        /// The model's take opens the wings (5 keys over 3.3 s). Rest pose = the
        /// file pose; spread pose = the take's frame where the first wing is turned
        /// furthest from rest. The Animation component is removed (it would replay
        /// the take); MosquitoVisual animates the wings itself.
        /// </summary>
        static void SampleWings(GameObject go, out Transform[] wings, out Quaternion[] rest, out Quaternion[] spread)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("ala")) list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            wings = list.ToArray();
            rest = new Quaternion[wings.Length];
            spread = new Quaternion[wings.Length];
            for (int i = 0; i < wings.Length; i++) rest[i] = spread[i] = wings[i].localRotation;

            var clip = AssetDatabase.LoadAllAssetsAtPath(MosquitoPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null && wings.Length > 0)
            {
                float best = -1f, bestT = 0f;
                for (int k = 0; k <= 40; k++)
                {
                    float time = clip.length * k / 40f;
                    clip.SampleAnimation(go, time);
                    float ang = Quaternion.Angle(rest[0], wings[0].localRotation);
                    if (ang > best) { best = ang; bestT = time; }
                }
                clip.SampleAnimation(go, bestT);
                for (int i = 0; i < wings.Length; i++) spread[i] = wings[i].localRotation;
                for (int i = 0; i < wings.Length; i++) wings[i].localRotation = rest[i];
                Debug.Log($"HumanAssets: mosquito wings {wings.Length}, spread pose at {bestT:F2} s ({best:F0} deg)");
            }
            var anim = go.GetComponent<Animation>();
            if (anim != null) Object.DestroyImmediate(anim);
        }

        /// <summary>
        /// Mosquito model scaled to an 8 mm wingspan, textured, no colliders, on its own
        /// layer: planner cameras and the first-person view skip it (at 8 mm it is under
        /// one pixel in planner images anyway); the chase view shows it.
        /// </summary>
        public static GameObject InstantiateMosquito(Transform parent, out Transform[] wings, out Quaternion[] rest, out Quaternion[] spread)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MosquitoPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Model";
            SampleWings(go, out wings, out rest, out spread);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.MosquitoModel;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MosquitoMatPath);
            if (mat == null)
            {
                var src = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mat = new Material(src.GetComponent<Renderer>().sharedMaterial);
                Object.DestroyImmediate(src);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MosquitoTexture);
                if (tex != null) mat.mainTexture = tex;
                mat.color = new Color(0.55f, 0.5f, 0.45f);
                System.IO.Directory.CreateDirectory(GeneratedDir);
                AssetDatabase.CreateAsset(mat, MosquitoMatPath);
            }
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float k = size > 1e-6f ? 0.008f / size : 1f;
            go.transform.localScale *= k;
            // Centre the model on the mosquito point.
            Vector3 centre = (bounds.center - go.transform.position) * k;
            go.transform.localPosition -= go.transform.parent.InverseTransformVector(centre);
            return go;
        }
    }
}
