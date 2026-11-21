// Import settings for licensed assets under Assets/ThirdParty/ (not in git; see
// docs/unity_setup.md). Mixamo FBX files get a Humanoid rig; animation-only
// files bring no materials; clips loop and stay in place (root motion is
// extracted and then ignored, HumanController moves the body).
using UnityEditor;

namespace MosquitoSim.EditorTools
{
    public class ThirdPartyImport : AssetPostprocessor
    {
        public const string Root = "Assets/ThirdParty/";

        public static bool IsMixamo(string path) =>
            path.StartsWith(Root) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
            !path.ToLowerInvariant().Contains("/mosquito/");

        public static bool IsCharacter(string path) => path == HumanAssets.CharacterPath;

        public override uint GetVersion() => 4;

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            if (assetPath.ToLowerInvariant().Contains("/mosquito/"))
            {
                // Its one take spreads the wings; HumanAssets samples the spread pose
                // from it and MosquitoVisual flaps around that pose.
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.importAnimation = true;
                return;
            }
            if (!IsMixamo(assetPath)) return;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (!IsCharacter(assetPath))
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessAnimation()
        {
            if (!IsMixamo(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            bool turn = assetPath == HumanAssets.TurnPath;
            foreach (var c in clips)
            {
                // The turn plays once; its rotation stays root motion (not applied):
                // HumanController turns the body over the clip's length.
                c.loopTime = !turn;
                c.lockRootRotation = !turn;    // walk straight; HumanController turns the body
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = false;  // extracted as root motion, not applied
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
