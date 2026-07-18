#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace qsyi
{
    [System.Serializable]
    public class BlinkPresetTarget
    {
        public string shapeKeyName;
        // side は持たない。QsToolBox側で補正適用時にメッシュから自動判定するため、プリセットに保存する必要がない。
    }

    // まばたき修正プリセットの保存形式（ShadowSyncPresetAssetと同じ1プリセット=1アセット方式）。表示名はファイル名（Object.name）が正。
    // トップレベル型にしているのは、QsToolBoxのネストクラスだとAssetDatabase.FindAssetsで検出できない不具合があったため。
    // public にしているのは、パッケージ外（Assets側）のプリセット作成補助ツールから直接コンストラクトできるようにするため
    // （qsToolBox本体の内部状態へのアクセスはリフレクションのみで行い、このデータ形式だけを公開している）。
    public class BlinkPresetAsset : ScriptableObject
    {
        // 「改変済み目元シェイプキー」側の選択状態（QsToolBox._blinkSourceNamesに対応）
        public List<string> sourceShapeKeyNames = new List<string>();
        // 「まばたきシェイプキー」側の選択状態（QsToolBox._blinkTargetsのうちenabled==trueのものに対応）
        public List<BlinkPresetTarget> targets = new List<BlinkPresetTarget>();
    }
}
#endif
