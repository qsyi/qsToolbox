#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace qsyi
{
    internal class ToggleEditorOnly
    {
        private const string EditorOnlyTag = "EditorOnly";
        private const string UntaggedTag = "Untagged";

        [MenuItem("Tools/qs/Toggle EditorOnly %e")]
        private static void ToggleSelectedObjects()
        {
            GameObject[] selected = Selection.gameObjects;
            if (selected.Length == 0) return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            foreach (GameObject obj in selected)
            {
                Undo.RecordObject(obj, "Toggle EditorOnly");

                bool isHidden = !obj.activeSelf;
                bool isEditorOnly = obj.tag == EditorOnlyTag;

                if (isHidden && isEditorOnly)
                {
                    obj.tag = UntaggedTag;
                    obj.SetActive(true);
                }
                else
                {
                    obj.tag = EditorOnlyTag;
                    obj.SetActive(false);
                }

                EditorUtility.SetDirty(obj);
            }

            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("Tools/qs/Toggle EditorOnly %e", true)]
        private static bool ValidateToggle()
        {
            return Selection.activeGameObject != null;
        }
    }
}
#endif