using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MosquitoSim
{
    /// <summary>
    /// The five planner cameras of one room: the mosquito's view and four
    /// overview cameras in the upper corners. Cameras stay disabled and are
    /// rendered on demand. Skin (S1..S5) and candidate (P1..P8) IDs are drawn
    /// as text labels on the Labels layer, which only these cameras see
    /// (Set-of-Mark). Needs a build with graphics (not -nographics).
    /// </summary>
    public class PlannerCameras : MonoBehaviour
    {
        public Camera mosquitoCam;
        public Camera[] overviewCams = new Camera[4];

        readonly List<TextMesh> m_Labels = new List<TextMesh>();
        Font m_Font;

        public void PlaceOverview(RoomArea area)
        {
            Vector3 s = area.Size;
            Vector3 o = area.transform.position;
            Vector3 look = o + Vector3.up * 0.8f;
            // In an L-shaped room the (+x,+z) corner is inside the cut-out block (run17: camera 0 saw
            // through the block from inside), so camera 0 hangs in the corner next to it instead.
            var corners = RoomPlacement.Corners(area, 0.15f);
            bool lShape = corners.Length > 4;
            // Aim at the middle of the L, not the middle of its bounding box (run20: half of
            // camera 0's view was the cut-out block's wall).
            look = RoomPlacement.OverviewLook(area);
            for (int i = 0; i < overviewCams.Length && i < 4; i++)
            {
                var cam = overviewCams[i];
                if (cam == null) continue;
                cam.transform.position = o + new Vector3(corners[i].x, s.y - 0.2f, corners[i].y);
                // Camera 0 of an L-shaped room sits against the block: turn it away from the block.
                cam.transform.LookAt(lShape && i == 0 ? look + new Vector3(0f, 0f, -0.8f) : look);
            }
        }

        /// <summary>
        /// Render all five views with labels and write PNGs.
        /// Returns [mosquito, overview0..3] file paths.
        /// </summary>
        public string[] Capture(string dir, string prefix, int size, Vector3 mosquitoPos, Vector3 lookAt,
            IList<Candidate> candidates, IList<SkinZone> skins)
        {
            Directory.CreateDirectory(dir);
            mosquitoCam.transform.position = mosquitoPos;
            Vector3 fwd = lookAt - mosquitoPos;
            if (fwd.sqrMagnitude > 1e-6f) mosquitoCam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            int n = 0;
            foreach (var c in candidates) SetLabel(n++, c.Id, c.Pos, new Color(1f, 0.85f, 0f));
            foreach (var s in skins) SetLabel(n++, s.id, s.Center + Vector3.up * 0.12f, new Color(0f, 0.9f, 1f));
            for (int i = n; i < m_Labels.Count; i++) m_Labels[i].gameObject.SetActive(false);

            var cams = new List<Camera> { mosquitoCam };
            cams.AddRange(overviewCams);
            var paths = new string[cams.Count];
            for (int i = 0; i < cams.Count; i++)
            {
                string tag = i == 0 ? "mosquito" : "overview" + (i - 1);
                paths[i] = Path.Combine(dir, prefix + "_" + tag + ".png");
                Render(cams[i], size, paths[i], n);
            }

            for (int i = 0; i < m_Labels.Count; i++) m_Labels[i].gameObject.SetActive(false);
            return paths;
        }

        void Render(Camera cam, int size, string path, int labelCount)
        {
            // Labels face the camera and keep a readable size at any distance.
            for (int i = 0; i < labelCount; i++)
            {
                Transform t = m_Labels[i].transform;
                t.rotation = cam.transform.rotation;
                float d = Vector3.Distance(cam.transform.position, t.position);
                // fontSize 64 x characterSize 0.1 is about 0.64 m tall at scale 1;
                // 0.15 * d keeps it near 5% of a 90-degree view (~20 px at 448 px).
                t.localScale = Vector3.one * Mathf.Clamp(0.15f * d, 0.01f, 1f);
            }

            var rt = RenderTexture.GetTemporary(size, size, 24);
            var prevActive = RenderTexture.active;
            cam.cullingMask = Layers.PlannerView;
            CameraCapture.Render(cam, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
        }

        void SetLabel(int i, string text, Vector3 pos, Color color)
        {
            while (m_Labels.Count <= i)
            {
                if (m_Font == null) m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var go = new GameObject("Label");
                go.layer = Layers.Labels;
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.font = m_Font;
                tm.fontSize = 64;
                tm.characterSize = 0.1f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                go.GetComponent<MeshRenderer>().sharedMaterial = m_Font.material;
                m_Labels.Add(tm);
            }
            var label = m_Labels[i];
            label.text = text;
            label.color = color;
            label.transform.position = pos;
            label.gameObject.SetActive(true);
        }
    }
}
