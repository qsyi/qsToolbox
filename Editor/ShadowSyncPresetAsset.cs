#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace qsyi
{
    [System.Serializable]
    internal class ShadowSyncPresetProp
    {
        public string key;
        public float r, g, b, a;
        public float value;
    }

    // 自作プリセットの保存形式（lilToonPresetと同じ1プリセット=1アセット方式）。表示名はファイル名（Object.name）が正。
    // ファイル名をクラス名と一致させているのは、同居型だとMonoScript.FromScriptableObjectがnullを返す不具合があったため。
    internal class ShadowSyncPresetAsset : ScriptableObject
    {
        public string category;
        public List<ShadowSyncPresetProp> props = new List<ShadowSyncPresetProp>();
    }
}
#endif
