using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MosquitoSim
{
    /// <summary>
    /// Render one camera into a texture outside the render loop. URP does not
    /// support Camera.Render(); it takes a render request instead. The
    /// built-in pipeline (no pipeline asset) still uses Camera.Render().
    /// </summary>
    public static class CameraCapture
    {
        public static string PipelineName =>
            GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in";

        public static void Render(Camera cam, RenderTexture rt)
        {
            cam.aspect = rt.width / (float)rt.height;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                    cam.ResetAspect();
                    return;
                }
            }
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            cam.ResetAspect();
        }
    }
}
