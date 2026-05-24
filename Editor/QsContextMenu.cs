#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using Anatawa12.AvatarOptimizer;

namespace qsyi
{
    internal static class QsContextMenu
    {
        [MenuItem("GameObject/qs_toolbox/アバタールート用コンポーネント追加", false, 10)]
        private static void SetupAvatarRoot(MenuCommand command)
        {
            var go = command.context as GameObject ?? Selection.activeGameObject;
            if (go == null) return;

            var animator = go.GetComponent<Animator>();
            var chestBone = (animator != null && animator.isHuman) ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            var hipsBone  = (animator != null && animator.isHuman) ? animator.GetBoneTransform(HumanBodyBones.Hips)  : null;

            if (chestBone == null || hipsBone == null)
                Debug.LogWarning($"[qs_toolbox] {go.name}: Humanoid Animator が見つからないため Chest/Hips の参照をスキップします。");

            Undo.SetCurrentGroupName("アバタールート用コンポーネント追加");
            int undoGroup = Undo.GetCurrentGroup();

            var existingMeshSettings = go.GetComponent<ModularAvatarMeshSettings>();
            var meshSettings = existingMeshSettings
                            ?? Undo.AddComponent<ModularAvatarMeshSettings>(go);

            if (existingMeshSettings != null)
                Undo.RecordObject(meshSettings, "アバタールート用コンポーネント追加");

            meshSettings.InheritProbeAnchor = ModularAvatarMeshSettings.InheritMode.Set;
            if (chestBone != null)
            {
                if (meshSettings.ProbeAnchor == null)
                    meshSettings.ProbeAnchor = new AvatarObjectReference();
                meshSettings.ProbeAnchor.Set(chestBone.gameObject);
            }

            meshSettings.InheritBounds = ModularAvatarMeshSettings.InheritMode.Set;
            if (hipsBone != null)
            {
                if (meshSettings.RootBone == null)
                    meshSettings.RootBone = new AvatarObjectReference();
                meshSettings.RootBone.Set(hipsBone.gameObject);
            }
            meshSettings.Bounds = new Bounds(Vector3.zero, Vector3.one);
            EditorUtility.SetDirty(go);

            if (go.GetComponent<ModularAvatarConvertConstraints>() == null)
                Undo.AddComponent<ModularAvatarConvertConstraints>(go);

            if (go.GetComponent<TraceAndOptimize>() == null)
                Undo.AddComponent<TraceAndOptimize>(go);

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("GameObject/qs_toolbox/アバタールート用コンポーネント追加", true)]
        private static bool ValidateSetupAvatarRoot()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>() != null;
        }


        [MenuItem("GameObject/qs_toolbox/頭に追従(MABoneProxy)", false, 20)]
        private static void AddHeadBoneProxy(MenuCommand command)
        {
            var go = command.context as GameObject ?? Selection.activeGameObject;
            if (go == null) return;

            if (go.GetComponent<ModularAvatarBoneProxy>() != null)
            {
                Debug.LogWarning($"[qs_toolbox] {go.name} には既に MA Bone Proxy が付いています。");
                return;
            }

            Undo.IncrementCurrentGroup();
            var proxy = Undo.AddComponent<ModularAvatarBoneProxy>(go);
            proxy.boneReference = HumanBodyBones.Head;
            proxy.subPath = "";
            EditorUtility.SetDirty(go);
            Undo.SetCurrentGroupName("頭に追従(MABoneProxy)を追加");
        }

        [MenuItem("GameObject/qs_toolbox/頭に追従(MABoneProxy)", true)]
        private static bool ValidateAddHeadBoneProxy()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>() == null;
        }

        [MenuItem("GameObject/qs_toolbox/腰に追従(MABoneProxy)", false, 21)]
        private static void AddHipsBoneProxy(MenuCommand command)
        {
            var go = command.context as GameObject ?? Selection.activeGameObject;
            if (go == null) return;

            if (go.GetComponent<ModularAvatarBoneProxy>() != null)
            {
                Debug.LogWarning($"[qs_toolbox] {go.name} には既に MA Bone Proxy が付いています。");
                return;
            }

            Undo.IncrementCurrentGroup();
            var proxy = Undo.AddComponent<ModularAvatarBoneProxy>(go);
            proxy.boneReference = HumanBodyBones.Hips;
            proxy.subPath = "";
            EditorUtility.SetDirty(go);
            Undo.SetCurrentGroupName("腰に追従(MABoneProxy)を追加");
        }

        [MenuItem("GameObject/qs_toolbox/腰に追従(MABoneProxy)", true)]
        private static bool ValidateAddHipsBoneProxy()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>() == null;
        }
    }
}
#endif
