#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace qsyi
{
    internal class qsToolBox : EditorWindow
    {
        private enum Mode { BlendShape, Material, Scale }
        
        [SerializeField] private List<GameObject> targets = new List<GameObject>();
        [SerializeField] private Transform avatarArmature;
        
        private Mode currentMode = Mode.BlendShape;
        private Vector2 scrollPos;
        private string searchQuery = "";
        private float windowWidth;
        
        // UI状態管理
        private readonly Dictionary<Object, bool> foldouts = new Dictionary<Object, bool>();
        private readonly Dictionary<string, bool> boneFoldouts = new Dictionary<string, bool>();
        
        // データキャッシュ
        private readonly List<SkinnedMeshRenderer> meshRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly Dictionary<Material, List<(Renderer renderer, int slot)>> materialUsage = new Dictionary<Material, List<(Renderer, int)>>();
        private readonly Dictionary<GameObject, Dictionary<string, Transform>> outfitBones = new Dictionary<GameObject, Dictionary<string, Transform>>();
        private readonly Dictionary<string, Transform> avatarBones = new Dictionary<string, Transform>();
        
        // 最適化用の再利用リスト
        private readonly List<SkinnedMeshRenderer> tempSMRList = new List<SkinnedMeshRenderer>();
        private readonly List<Renderer> tempRendererList = new List<Renderer>();
        
        // SerializedProperty
        private SerializedObject so;
        private SerializedProperty targetsProp;
        private SerializedProperty avatarArmatureProp;
        private int prevTargetCount = -1;
        private bool needsRescan = false;
        
        // 定数
        private static readonly string[] TabLabels = { "ブレンドシェイプ", "マテリアル", "スケール" };
        private static GUIStyle[] buttonStyles;
        
        // ボーン階層とスケール同期用の順序
        private static readonly string[] BoneOrder = {
            "Hips", "Spine", "Chest", "Breast L", "Breast R", "Neck", "Head", 
            "Butt L", "Butt R", "Upper Leg L", "Upper Leg R", "Lower Leg L", "Lower Leg R", 
            "Foot L", "Foot R", "Shoulder L", "Shoulder R", "Upper Arm L", "Upper Arm R", 
            "Lower Arm L", "Lower Arm R", "Hand L", "Hand R"
        };
        
        private static readonly Dictionary<string, string> BoneHierarchy = new Dictionary<string, string>
        {
            { "Hips", null },
            { "Butt L", "Hips" },
            { "Butt R", "Hips" },
            { "Upper Leg L", "Hips" },
            { "Upper Leg R", "Hips" },
            { "Spine", "Hips" },
            { "Chest", "Spine" },
            { "Breast L", "Chest" },
            { "Breast R", "Chest" },
            { "Neck", "Chest" },
            { "Shoulder L", "Chest" },
            { "Shoulder R", "Chest" },
            { "Head", "Neck" },
            { "Upper Arm L", "Shoulder L" },
            { "Lower Arm L", "Upper Arm L" },
            { "Hand L", "Lower Arm L" },
            { "Upper Arm R", "Shoulder R" },
            { "Lower Arm R", "Upper Arm R" },
            { "Hand R", "Lower Arm R" },
            { "Lower Leg L", "Upper Leg L" },
            { "Foot L", "Lower Leg L" },
            { "Lower Leg R", "Upper Leg R" },
            { "Foot R", "Lower Leg R" },
        };
        
        [MenuItem("Tools/qs/ツールボックス %q")]
        public static void ShowWindow()
        {
            var window = GetWindow<qsToolBox>("qsToolBox");
            window.targets = new List<GameObject>(Selection.gameObjects);
            window.ScanCurrentMode();
        }
        
        private void OnEnable()
        {
            so = new SerializedObject(this);
            targetsProp = so.FindProperty("targets");
            avatarArmatureProp = so.FindProperty("avatarArmature");
            targetsProp.isExpanded = true;
            
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnPostUndoModifications;
            
            ScanCurrentMode();
            prevTargetCount = targets.Count;
        }
        
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnPostUndoModifications;
        }
        
        private void OnHierarchyChanged() => needsRescan = true;
        
        private UndoPropertyModification[] OnPostUndoModifications(UndoPropertyModification[] modifications)
        {
            needsRescan = true;
            return modifications;
        }
        
        private void OnGUI()
        {
            windowWidth = EditorGUIUtility.currentViewWidth;
            HandleTargetChanges();
            DrawUI();
        }
        
        private void HandleTargetChanges()
        {
            so.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(targetsProp, true);
            bool changed = EditorGUI.EndChangeCheck();
            
            int currentCount = targetsProp.arraySize;
            so.ApplyModifiedProperties();
            
            if (changed || currentCount != prevTargetCount || needsRescan)
            {
                ScanCurrentMode();
                prevTargetCount = currentCount;
                needsRescan = false;
            }
        }
        
        private Transform FindAvatarArmature()
        {
            // targetsの親階層を探索してVRCAvatarDescriptorを探し、そのArmatureを取得
            foreach (var target in targets)
            {
                if (target == null) continue;
                
                // 対象オブジェクト自身から開始して親を辿る
                Transform current = target.transform;
                while (current != null)
                {
                    var descriptor = current.GetComponent<VRCAvatarDescriptor>();
                    if (descriptor != null)
                    {
                        // VRCAvatarDescriptorが見つかったら、そのArmatureを探す
                        return FindArmature(descriptor.transform);
                    }
                    current = current.parent;
                }
            }
            
            return null;
        }
        
        private void DrawUI()
        {
            EditorGUILayout.Space(4);
            DrawTabs();
            EditorGUILayout.Space();
            
            switch (currentMode)
            {
                case Mode.BlendShape: DrawBlendShapeUI(); break;
                case Mode.Material: DrawMaterialUI(); break;
                case Mode.Scale: DrawScaleUI(); break;
            }
        }
        
        private void DrawTabs()
        {
            // スタイルの初期化
            if (buttonStyles == null)
            {
                buttonStyles = new GUIStyle[] { EditorStyles.miniButtonLeft, EditorStyles.miniButtonMid, EditorStyles.miniButtonRight };
            }
            
            var originalBG = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();
            float buttonWidth = (windowWidth - 60) / TabLabels.Length;
            
            for (int i = 0; i < TabLabels.Length; i++)
            {
                bool selected = (int)currentMode == i;
                GUI.backgroundColor = selected ? new Color(0.8f, 0.85f, 1f) : originalBG;
                
                if (GUILayout.Button(TabLabels[i], buttonStyles[i], GUILayout.Width(buttonWidth)) && !selected)
                {
                    currentMode = (Mode)i;
                    GUI.FocusControl(null);
                    ScanCurrentMode();
                    scrollPos = Vector2.zero;
                }
            }
            
            GUI.backgroundColor = originalBG;
            
            if (GUILayout.Button("スキャン", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                ScanCurrentMode();
                Repaint();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawBlendShapeUI()
        {
            // 検索ボックス
            using (new EditorGUILayout.VerticalScope("box"))
                searchQuery = EditorGUILayout.TextField("検索", searchQuery);
            
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos))
            {
                scrollPos = scroll.scrollPosition;
                bool hasSearch = !string.IsNullOrEmpty(searchQuery);
                
                foreach (var smr in meshRenderers)
                    if (smr?.sharedMesh != null && (!hasSearch || HasMatchingBlendShape(smr, searchQuery)))
                        DrawBlendShapeRenderer(smr);
            }
        }
        
        private void DrawBlendShapeRenderer(SkinnedMeshRenderer smr)
        {
            if (!foldouts.TryGetValue(smr, out bool expanded)) 
                foldouts[smr] = expanded = true;
            
            foldouts[smr] = EditorGUILayout.Foldout(expanded, smr.name, true, EditorStyles.foldoutHeader);
            if (!foldouts[smr]) return;
            
            EditorGUI.indentLevel++;
            var mesh = smr.sharedMesh;
            bool hasSearch = !string.IsNullOrEmpty(searchQuery);
            
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (!hasSearch || name.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    DrawBlendShapeSlider(smr, i, name);
            }
            EditorGUI.indentLevel--;
        }
        
        private void DrawBlendShapeSlider(SkinnedMeshRenderer smr, int index, string name)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("検索", GUILayout.Width(40)))
            {
                searchQuery = name;
                GUI.FocusControl(null);
            }
            
            float totalWidth = Mathf.Max(0, windowWidth - 100);
            float labelWidth = totalWidth * 0.4f;
            EditorGUILayout.LabelField(name, EditorStyles.label, GUILayout.Width(labelWidth));
            
            float current = smr.GetBlendShapeWeight(index);
            float newValue = EditorGUILayout.Slider(current, 0f, 100f);
            
            if (!Mathf.Approximately(newValue, current))
            {
                Undo.RecordObject(smr, "Change BlendShape Weight");
                smr.SetBlendShapeWeight(index, newValue);
                EditorUtility.SetDirty(smr);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawMaterialUI()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos))
            {
                scrollPos = scroll.scrollPosition;
                
                foreach (var mat in materials)
                {
                    EditorGUI.BeginChangeCheck();
                    var newMat = (Material)EditorGUILayout.ObjectField(mat, typeof(Material), false);
                    
                    if (EditorGUI.EndChangeCheck() && newMat != null && newMat != mat)
                    {
                        ReplaceMaterial(mat, newMat);
                        break;
                    }
                }
            }
        }
        
        private void DrawScaleUI()
        {
            // バリデーション
            bool allHaveMeshSettings = ValidateOutfits();
            
            if (!allHaveMeshSettings)
            {
                EditorGUILayout.HelpBox("SetupOutfitした衣装を入れてください。", MessageType.Error);
                return;
            }
            
            if (outfitBones.Count == 0)
            {
                ScanOutfitBones();
                if (outfitBones.Count == 0)
                {
                    EditorGUILayout.HelpBox("衣装のボーンを見つけられません。", MessageType.Error);
                    return;
                }
            }
            
            DrawBoneScales();
            
            // スクロール部分の下にコントロールを移動
            EditorGUILayout.Space(10);
            
            so.Update();
            EditorGUILayout.PropertyField(avatarArmatureProp, new GUIContent("素体Armature"));
            so.ApplyModifiedProperties();
            
            var newArmature = avatarArmatureProp.objectReferenceValue as Transform;
            if (newArmature != avatarArmature)
            {
                avatarArmature = newArmature;
                ScanOutfitBones();
            }
            
            if (avatarArmature == null)
            {
                EditorGUILayout.HelpBox("素体のArmatureを設定してください。", MessageType.Warning);
            }
            
            EditorGUILayout.Space(5);
            
            // 衣装のスケールを身体に合わせるボタン
            if (GUILayout.Button("衣装のスケールを身体に合わせる", GUILayout.Height(30)))
            {
                SyncAllOutfitScalesToAvatar();
            }
        }
        
        private void DrawBoneScales()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            
            foreach (var boneName in BoneOrder)
            {
                if (!boneFoldouts.ContainsKey(boneName)) boneFoldouts[boneName] = true;
                
                // ボーン名を太字で表示
                var boldStyle = new GUIStyle(EditorStyles.foldout);
                boldStyle.fontStyle = FontStyle.Bold;
                boneFoldouts[boneName] = EditorGUILayout.Foldout(boneFoldouts[boneName], boneName, true, boldStyle);
                if (!boneFoldouts[boneName]) continue;
                
                EditorGUI.indentLevel++;
                
                // 素体ボーン
                if (avatarBones.TryGetValue(boneName, out var avatarBone))
                {
                    DrawBoneScale("素体", avatarBone, true, boneName);
                }
                else
                {
                    EditorGUILayout.LabelField("素体にボーンがありません");
                }
                
                // 区切り線
                var rect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
                
                // 衣装ボーン
                for (int i = 0; i < targets.Count; i++)
                {
                    var outfit = targets[i];
                    if (outfit == null) continue;
                    
                    if (outfitBones.TryGetValue(outfit, out var boneMap) && 
                        boneMap.TryGetValue(boneName, out var outfitBone))
                    {
                        DrawBoneScale("衣装", outfitBone, false, boneName);
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"「{outfit.name}」にボーンがありません");
                    }
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawBoneScale(string label, Transform bone, bool isAvatar, string boneName)
        {
            var scaleAdjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(label, EditorStyles.label, GUILayout.Width(30)))
                {
                    Selection.activeTransform = bone;
                    EditorGUIUtility.PingObject(bone);
                }
                
                if (scaleAdjuster == null)
                {
                    if (GUILayout.Button("ScaleAdjusterを追加", GUILayout.ExpandWidth(true)))
                        Undo.AddComponent<ModularAvatarScaleAdjuster>(bone.gameObject);
                }
                else
                {
                    var scale = scaleAdjuster.Scale;
                    EditorGUI.BeginChangeCheck();
                    
                    float fieldWidth = (windowWidth - 80) / 3;
                    EditorGUIUtility.labelWidth = 26;
                    
                    float newX = EditorGUILayout.FloatField("X", scale.x, GUILayout.Width(fieldWidth));
                    float newY = EditorGUILayout.FloatField("Y", scale.y, GUILayout.Width(fieldWidth));
                    float newZ = EditorGUILayout.FloatField("Z", scale.z, GUILayout.Width(fieldWidth));
                    
                    EditorGUIUtility.labelWidth = 0;
                    
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(scaleAdjuster, "Change ScaleAdjuster Scale");
                        scaleAdjuster.Scale = new Vector3(newX, newY, newZ);
                        EditorUtility.SetDirty(scaleAdjuster);
                    }
                }
            }
        }
        
        // スキャン関数
        private void ScanCurrentMode()
        {
            switch (currentMode)
            {
                case Mode.BlendShape: ScanBlendShapes(); break;
                case Mode.Material: ScanMaterials(); break;
                case Mode.Scale: ScanOutfitBones(); break;
            }
        }
        
        private void ScanBlendShapes()
        {
            meshRenderers.Clear();
            var oldFoldouts = new Dictionary<Object, bool>(foldouts);
            foldouts.Clear();
            
            foreach (var go in targets)
            {
                if (go?.CompareTag("EditorOnly") == true) continue;
                
                tempSMRList.Clear();
                go.GetComponentsInChildren(true, tempSMRList);
                
                foreach (var smr in tempSMRList)
                    if (smr.sharedMesh?.blendShapeCount > 0)
                    {
                        meshRenderers.Add(smr);
                        foldouts[smr] = oldFoldouts.TryGetValue(smr, out bool fold) ? fold : true;
                    }
            }
        }
        
        private void ScanMaterials()
        {
            materials.Clear();
            materialUsage.Clear();
            
            foreach (var go in targets)
            {
                if (go == null || go.CompareTag("EditorOnly")) continue;
                
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    var mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null) continue;
                        
                        if (!materialUsage.ContainsKey(mat))
                        {
                            materialUsage[mat] = new List<(Renderer, int)>();
                            materials.Add(mat);
                        }
                        materialUsage[mat].Add((renderer, i));
                    }
                }
            }
        }
        
        private void ScanOutfitBones()
        {
            outfitBones.Clear();
            avatarBones.Clear();
            
            // 素体Armatureが未設定の場合、自動検索
            if (avatarArmature == null)
            {
                avatarArmature = FindAvatarArmature();
                if (avatarArmature != null)
                {
                    // SerializedPropertyも更新
                    so.Update();
                    avatarArmatureProp.objectReferenceValue = avatarArmature;
                    so.ApplyModifiedProperties();
                }
            }

            // アバターボーンをスキャン
            if (avatarArmature != null)
            {
                BuildBoneMap(avatarArmature, avatarBones);
            }

            // 衣装ボーンのみスキャン
            foreach (var outfit in targets)
            {
                if (outfit == null) continue;
                
                var outfitArmature = FindArmature(outfit.transform);
                if (outfitArmature != null)
                {
                    var boneMap = new Dictionary<string, Transform>();
                    BuildBoneMap(outfitArmature, boneMap);
                    if (boneMap.Count > 0)
                    {
                        outfitBones[outfit] = boneMap;
                    }
                }
            }
        }
        
        // ユーティリティ関数
        private bool HasMatchingBlendShape(SkinnedMeshRenderer smr, string searchQuery)
        {
            var mesh = smr.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (mesh.GetBlendShapeName(i).IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
        
        private void ReplaceMaterial(Material oldMat, Material newMat)
        {
            if (!materialUsage.TryGetValue(oldMat, out var users)) return;
            
            foreach (var (renderer, index) in users)
            {
                if (renderer == null) continue;
                
                Undo.RecordObject(renderer, "Change Material");
                var mats = renderer.sharedMaterials;
                if (index >= 0 && index < mats.Length)
                {
                    mats[index] = newMat;
                    renderer.sharedMaterials = mats;
                    EditorUtility.SetDirty(renderer);
                }
            }
            
            // 参照を更新
            materialUsage.Remove(oldMat);
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] == oldMat)
                {
                    materials[i] = newMat;
                    break;
                }
            }
            
            if (!materialUsage.ContainsKey(newMat))
                materialUsage[newMat] = users;
            else
                materialUsage[newMat].AddRange(users);
        }
        
        private void SyncAllOutfitScalesToAvatar()
        {
            if (avatarArmature == null)
            {
                EditorUtility.DisplayDialog("エラー", "素体のArmatureが見つかりません。素体のArmatureを設定してください。", "OK");
                return;
            }
            
            if (avatarBones.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "素体のボーンが見つかりません。", "OK");
                return;
            }
            
            Undo.SetCurrentGroupName("Sync All Outfit Scales");
            int undoGroup = Undo.GetCurrentGroup();
            
            foreach (var boneName in BoneOrder)
            {
                if (avatarBones.TryGetValue(boneName, out var avatarBone))
                {
                    var avatarScaleAdjuster = avatarBone.GetComponent<ModularAvatarScaleAdjuster>();
                    if (avatarScaleAdjuster == null) continue;
            
                    Vector3 avatarScale = avatarScaleAdjuster.Scale;
            
                    // 各衣装のボーンを同じスケールに設定
                    foreach (var outfit in targets)
                    {
                        if (outfit == null) continue;
            
                        if (outfitBones.TryGetValue(outfit, out var boneMap) && 
                            boneMap.TryGetValue(boneName, out var outfitBone))
                        {
                            var outfitScaleAdjuster = outfitBone.GetComponent<ModularAvatarScaleAdjuster>();
                            if (outfitScaleAdjuster != null)
                            {
                                Undo.RecordObject(outfitScaleAdjuster, "Sync Outfit Scale");
                                outfitScaleAdjuster.Scale = avatarScale;
                                EditorUtility.SetDirty(outfitScaleAdjuster);
                            }
                            else
                            {
                                outfitScaleAdjuster = Undo.AddComponent<ModularAvatarScaleAdjuster>(outfitBone.gameObject);
                                outfitScaleAdjuster.Scale = avatarScale;
                                EditorUtility.SetDirty(outfitScaleAdjuster);
                            }
                        }
                    }
                }
            }
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("完了", "衣装のスケールを身体に合わせました。", "OK");
        }
        
        private bool ValidateOutfits()
        {
            foreach (var go in targets)
            {
                if (go != null && go.GetComponent<ModularAvatarMeshSettings>() == null)
                    return false;
            }
            return true;
        }
        
        private Transform FindArmature(Transform parent)
        {
            return FindChildWithKeyword(parent, "armature");
        }
        
        private Transform FindChildWithKeyword(Transform parent, string keyword)
        {
            if (parent == null) return null;
            
            string normalizedKeyword = keyword.ToLowerInvariant().Replace(" ", "");
            
            var enumerator = parent.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var child = (Transform)enumerator.Current;
                string childName = child.name.ToLowerInvariant().Replace("_", "").Replace(" ", "");
                if (childName.Contains(normalizedKeyword))
                    return child;
            }
            return null;
        }
        
        private void BuildBoneMap(Transform armature, Dictionary<string, Transform> boneMap)
        {
            foreach (var boneName in BoneOrder)
            {
                Transform found = null;
                
                if (BoneHierarchy.TryGetValue(boneName, out var parent) && parent != null)
                {
                    // 親ボーンから辿る
                    if (boneMap.TryGetValue(parent, out var parentBone) && parentBone != null)
                    {
                        found = FindChildWithKeyword(parentBone, boneName);
                    }
                }
                else
                {
                    // 直接armature下を検索
                    found = FindChildWithKeyword(armature, boneName);
                }
                
                if (found != null) 
                    boneMap[boneName] = found;
            }
        }
    }
}
#endif