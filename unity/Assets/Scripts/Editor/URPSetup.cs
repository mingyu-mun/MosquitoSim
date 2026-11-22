// Makes the project render with URP: creates Assets/Settings/MosquitoSim_URP.asset
// (and its Universal Renderer) once and sets it as the default render pipeline.
// Runs on every editor load and before the scene builder; does nothing when
// a URP asset is already assigned.
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MosquitoSim.EditorTools
{
    [InitializeOnLoad]
    public static class URPSetup
    {
        const string Dir = "Assets/Settings";
        const string AssetPath = Dir + "/MosquitoSim_URP.asset";

        static URPSetup()
        {
            EditorApplication.delayCall += () => Ensure();
        }

        [MenuItem("MosquitoSim/Use URP")]
        static void Menu() => Debug.Log("URP: " + AssetDatabase.GetAssetPath(Ensure()));

        public static UniversalRenderPipelineAsset Ensure()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset current)
            {
                ClearQualityOverrides();
                return current;
            }

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                Directory.CreateDirectory(Dir);
                var renderer = CreateRenderer();
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.msaaSampleCount = 4;           // label edges and thin net bars in planner images
                asset.maxAdditionalLightsCount = 4;  // one ceiling light per room
                AssetDatabase.CreateAsset(asset, AssetPath);
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            ClearQualityOverrides();
            AssetDatabase.SaveAssets();
            Debug.Log("MosquitoSim: render pipeline set to " + AssetPath);
            return asset;
        }

        /// <summary>
        /// The menu path (Assets > Create > Rendering > URP Asset) uses the internal
        /// CreateRendererAsset, which also attaches the default post-process data.
        /// Call it by reflection; if its signature changes, fall back to a plain
        /// renderer (post-processing off, which this project does not use).
        /// </summary>
        static ScriptableRendererData CreateRenderer()
        {
            const string rendererPath = Dir + "/MosquitoSim_URP_Renderer.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (existing != null) return existing;
            try
            {
                var m = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null && m.GetParameters().Length == 4)
                    return (ScriptableRendererData)m.Invoke(null,
                        new object[] { rendererPath, RendererType.UniversalRenderer, false, "Renderer" });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("MosquitoSim: CreateRendererAsset failed, using a plain renderer: " + e.Message);
            }
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, rendererPath);
            return data;
        }

        /// <summary>Quality levels must not point at another pipeline (or none).</summary>
        static void ClearQualityOverrides()
        {
            int keep = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != null && !(QualitySettings.renderPipeline is UniversalRenderPipelineAsset))
                    QualitySettings.renderPipeline = null;
            }
            QualitySettings.SetQualityLevel(keep, false);
        }
    }
}
