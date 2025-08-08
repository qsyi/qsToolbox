#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace qsyi
{
    internal class ToggleEditorOnly
    {
        private const string EditorOnlyTag = "EditorOnly";
        private const string Untagged = "Untagged";

        [MenuItem("Tools/qs/Toggle EditorOnly %e")] // Ctrl+E
        private static void ToggleSelectedObjects()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
                return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();

            foreach (GameObject obj in selectedObjects)
            {
                Undo.RecordObject(obj, "Toggle EditorOnly");

                bool isCurrentlyHidden = !obj.activeSelf;
                bool isCurrentlyEditorOnly = obj.tag == EditorOnlyTag;

                if (isCurrentlyHidden && isCurrentlyEditorOnly)
                {
                    obj.tag = Untagged;
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