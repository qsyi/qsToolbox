#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;
using jp.lilxyzw.lilycalinventory.runtime;
using System.Reflection;
using System.IO;
using System.Linq;

namespace qsyi
{
    internal class QsToolBox : EditorWindow
    {
        private enum Mode { Material, BlendShape, Scale, MenuGenerator }
        private enum MenuGenerationMode { PerRenderer, Combined }
        private static readonly string[] MenuGenerationDescriptions =
        {
            "選択したものを一つずつメニューにします",
            "選択したものを一つのメニューにまとめます"
        };
        
        // Core Fields
        [SerializeField] private List<GameObject> _targets = new List<GameObject>();
        [SerializeField] private Transform _avatarArmature;
        [SerializeField] private List<OutfitArmatureEntry> _outfitArmatureEntries = new List<OutfitArmatureEntry>();
        [SerializeField] private bool _autoScaleSyncEnabled;
        [SerializeField] private bool _autoSyncPositionAndRotation;
        
        private Mode _mode = Mode.Material;
        private Vector2 _scrollPosition;
        private Vector2 _composeShapeScroll;
        private Vector2 _shapeListScroll;
        private Vector2 _scaleStatusScroll;
        private Vector2 _menuRendererScroll;
        
        // Data Cache
        private readonly List<SkinnedMeshRenderer> _skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly Dictionary<Material, List<(Renderer renderer, int slot)>> _materialUsage = new Dictionary<Material, List<(Renderer, int)>>();
        private readonly Dictionary<GameObject, Dictionary<string, Transform>> _outfitBones = new Dictionary<GameObject, Dictionary<string, Transform>>();
        private readonly Dictionary<string, Transform> _avatarBones = new Dictionary<string, Transform>();
        [SerializeField] private List<Renderer> _menuRenderers = new List<Renderer>();
        private readonly Dictionary<int, bool> _menuRendererSelection = new Dictionary<int, bool>();
        
        // Compose Mode
        private SkinnedMeshRenderer _composeTarget;
        private string _baseShapeName = "";
        private readonly List<(string name, float weight)> _composeShapes = new List<(string, float)>();
        private string _composeSearchText = "";
        private readonly List<string> _shapeNames = new List<string>();
        private string _newShapeName = "";
        private bool _overwriteShape = true;
        private MenuGenerationMode _menuGenerationMode = MenuGenerationMode.PerRenderer;
        private string _menuFolderName = "";
        
        // Cache Control
        private SerializedObject _serializedObject;
        private SerializedProperty _targetsProperty;
        private SerializedProperty _armatureProperty;
        private SerializedProperty _outfitArmatureEntriesProperty;
        private SerializedProperty _menuRenderersProperty;
        private ReorderableList _menuRenderersList;
        private int _targetHash = -1;
        private bool _isDirty = true;
        private bool _isApplyingAutoSync;
        private double _nextAutoSyncTime;
        private readonly Dictionary<string, (Vector3 localScale, Vector3 localPosition, Quaternion localRotation, bool hasAdjuster, Vector3 adjusterScale)> _avatarScaleCache
            = new Dictionary<string, (Vector3, Vector3, Quaternion, bool, Vector3)>();
        private static FieldInfo _adjustChildPositionsField;
        private static bool _adjustChildPositionsResolved;
        
        // Constants
        private const float BUTTON_WIDTH_SMALL = 20f;
        private const float BUTTON_WIDTH_MEDIUM = 60f;
        private const float BUTTON_WIDTH_LARGE = 80f;
        private const float SCROLL_HEIGHT = 300f;
        private const float EXECUTE_BUTTON_HEIGHT = 30f;
        private const float VIEW_WIDTH_RATIO = 0.5f;
        private const double AUTO_SYNC_INTERVAL_SECONDS = 0.2d;
        
        // UI Colors
        private static readonly Color HeaderColor = new Color(0.6f, 0.8f, 1f, 0.8f);
        private static readonly Color ContentColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        private static readonly Color SelectColor = new Color(0.5f, 0.7f, 1f, 0.8f);
        private static readonly Color TargetColor = new Color(0.9f, 0.9f, 0.6f, 0.8f);
        private static readonly Color BaseColor = new Color(0.8f, 1f, 0.8f, 0.8f);
        
        private static readonly string[] TAB_NAMES = { "マテリアル", "ブレンドシェイプ", "スケール", "メニュー生成" };
        private static readonly GUIContent[] TAB_TOOLTIPS = {
            new GUIContent("マテリアル", "探索対象のマテリアルを置換できます"),
            new GUIContent("ブレンドシェイプ", "探索対象のブレンドシェイプを表示・編集します"),
            new GUIContent("スケール", "ModularAvatarのスケール調整機能を使用します"),
            new GUIContent("メニュー生成", "メニュー生成機能の追加予定エリアです")
        };
        private static readonly string[] BONE_ORDER = {
            "Hips", "Spine", "Chest", "Breast L", "Breast R", "Neck", "Head", 
            "Butt L", "Butt R", "Upper Leg L", "Upper Leg R", "Lower Leg L", "Lower Leg R", 
            "Foot L", "Foot R", "Shoulder L", "Shoulder R", "Upper Arm L", "Upper Arm R", 
            "Lower Arm L", "Lower Arm R", "Hand L", "Hand R"
        };
        
        private static readonly Dictionary<string, string> BONE_PARENT = new Dictionary<string, string>
        {
            ["Spine"] = "Hips", ["Chest"] = "Spine", ["Neck"] = "Chest", ["Head"] = "Neck",
            ["Butt L"] = "Hips", ["Butt R"] = "Hips",
            ["Upper Leg L"] = "Hips", ["Upper Leg R"] = "Hips",
            ["Lower Leg L"] = "Upper Leg L", ["Lower Leg R"] = "Upper Leg R",
            ["Foot L"] = "Lower Leg L", ["Foot R"] = "Lower Leg R",
            ["Shoulder L"] = "Chest", ["Shoulder R"] = "Chest",
            ["Upper Arm L"] = "Shoulder L", ["Upper Arm R"] = "Shoulder R",
            ["Lower Arm L"] = "Upper Arm L", ["Lower Arm R"] = "Upper Arm R",
            ["Hand L"] = "Lower Arm L", ["Hand R"] = "Lower Arm R",
            ["Breast L"] = "Chest", ["Breast R"] = "Chest"
        };

        [System.Serializable]
        private class OutfitArmatureEntry
        {
            public GameObject Outfit;
            public List<Transform> Armatures = new List<Transform>();
            [HideInInspector] public bool AutoAssigned;
        }
        
        [MenuItem("Tools/qs/ツールボックス %q")]
        public static void ShowWindow()
        {
            var window = GetWindow<QsToolBox>("qsToolBox");
            window._targets = new List<GameObject>(Selection.gameObjects);
            window.ScanData();
        }
        
        private void OnEnable()
        {
            InitializeSerializedObject();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnUndo;
            EditorApplication.update += OnEditorUpdate;
            ScanData();
        }
        
        private void OnDisable()
        {
            if (_autoScaleSyncEnabled)
                SetAutoScaleSyncEnabled(false);

            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnUndo;
            EditorApplication.update -= OnEditorUpdate;
        }
        
        private void InitializeSerializedObject()
        {
            _serializedObject = new SerializedObject(this);
            _targetsProperty = _serializedObject.FindProperty("_targets");
            _armatureProperty = _serializedObject.FindProperty("_avatarArmature");
            _outfitArmatureEntriesProperty = _serializedObject.FindProperty("_outfitArmatureEntries");
            _menuRenderersProperty = _serializedObject.FindProperty("_menuRenderers");
            _targetsProperty.isExpanded = true;
            _outfitArmatureEntriesProperty.isExpanded = true;
            InitializeMenuRenderersList();
        }
        
        private void OnHierarchyChanged() => _isDirty = true;
        private UndoPropertyModification[] OnUndo(UndoPropertyModification[] modifications)
        {
            _isDirty = true;
            return modifications;
        }
        
        private void OnGUI()
        {
            CheckForTargetChanges();
            DrawMainTabs();
            
            EditorGUILayout.Space();
            
            switch (_mode)
            {
                case Mode.Material: 
                    DrawMaterialReplace();
                    break;
                case Mode.BlendShape: 
                    DrawBlendShapeCompose();
                    break;
                case Mode.Scale: DrawScaleAdjustment(); break;
                case Mode.MenuGenerator: DrawMenuGenerator(); break;
            }
        }
        
        private void CheckForTargetChanges()
        {
            DrawColoredBox(TargetColor, () => 
            {
                _serializedObject.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_targetsProperty, new GUIContent("探索対象（自動）", "スキャン対象を指定"), true);
                bool changed = EditorGUI.EndChangeCheck();
                _serializedObject.ApplyModifiedProperties();
                
                int currentHash = GetTargetHash();
                if (changed || _isDirty || currentHash != _targetHash)
                {
                    ScanData();
                    _targetHash = currentHash;
                    _isDirty = false;
                }
            });
        }
        
        private int GetTargetHash()
        {
            int hash = _targets.Count;
            foreach (var target in _targets.Where(t => t != null))
                hash = hash * 31 + target.GetInstanceID();
            return hash;
        }
        
        private void DrawMainTabs()
        {
            EditorGUILayout.Space(4);
            DrawTabButtons(TAB_NAMES, TAB_TOOLTIPS, (int)_mode, (index) => 
            {
                if (_autoScaleSyncEnabled && _mode != (Mode)index)
                    SetAutoScaleSyncEnabled(false);

                _mode = (Mode)index;
                GUI.FocusControl(null);
                ScanData();
                _scrollPosition = Vector2.zero;
            }, true);
        }
        
        private void DrawTabButtons(string[] tabNames, GUIContent[] tooltips, int selectedIndex, System.Action<int> onTabSelected, bool showScanButton)
        {
            var originalColor = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();
            
            float totalWidth = EditorGUIUtility.currentViewWidth - (showScanButton ? 60 : 10);
            float buttonWidth = totalWidth / tabNames.Length;
            
            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isSelected = selectedIndex == i;
                GUI.backgroundColor = isSelected ? new Color(0.8f, 0.85f, 1f) : originalColor;
                
                var buttonStyle = GetButtonStyle(i, tabNames.Length);
                
                if (GUILayout.Button(tooltips[i], buttonStyle, GUILayout.Width(buttonWidth)) && !isSelected)
                {
                    onTabSelected(i);
                }
            }
            
            GUI.backgroundColor = originalColor;
            
            if (showScanButton)
            {
                if (GUILayout.Button(new GUIContent("スキャン", "再スキャン"), EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    ScanData();
                    Repaint();
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private GUIStyle GetButtonStyle(int index, int totalCount)
        {
            if (index == 0) return EditorStyles.miniButtonLeft;
            if (index == totalCount - 1) return EditorStyles.miniButtonRight;
            return EditorStyles.miniButtonMid;
        }
        
        private void DrawMaterialReplace()
        {
            DrawColoredBox(HeaderColor, () => 
            {
                EditorGUILayout.LabelField("マテリアル置換", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("マテリアルを直接ドラッグ＆ドロップで置換", MessageType.Info);
            });
            
            if (_materials.Count == 0)
            {
                EditorGUILayout.HelpBox("マテリアルが見つかりません。", MessageType.Info);
                return;
            }
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            foreach (var material in _materials)
            {
                DrawColoredBox(ContentColor, () => 
                {
                    EditorGUI.BeginChangeCheck();
                    var newMaterial = (Material)EditorGUILayout.ObjectField(material, typeof(Material), false);
                    
                    if (EditorGUI.EndChangeCheck() && newMaterial != null && newMaterial != material)
                        ReplaceMaterial(material, newMaterial);
                });
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawBlendShapeCompose()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                float upperContentHeight = position.height - 190f;
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(upperContentHeight));
                
                ScanForCompose();
                
                DrawComposeTargetSelection();
                DrawBaseShapeSelection();
                DrawComposeShapeAndListSelection();
                
                EditorGUILayout.EndScrollView();
                
                DrawComposeExecuteButton();
            }
        }
        
        private void DrawComposeTargetSelection()
        {
            DrawColoredBox(HeaderColor, () => 
            {
                EditorGUILayout.LabelField("シェイプキー合成", EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                _composeTarget = EditorGUILayout.ObjectField(
                    new GUIContent("対象メッシュ", "合成対象のSkinnedMeshRenderer"), 
                    _composeTarget, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;
                
                if (EditorGUI.EndChangeCheck())
                {
                    ResetComposeData();
                    ScanForCompose();
                }
            });
        }
        
        private void DrawBaseShapeSelection()
        {
            DrawColoredBox(BaseColor, () => 
            {
                EditorGUILayout.LabelField("ベースシェイプキー", EditorStyles.boldLabel);
                DrawBaseShapeInfo();
                DrawOverwriteSettings();
                DrawNewShapeNameField();
            });
        }
        
        private void DrawBaseShapeInfo()
        {
            if (string.IsNullOrEmpty(_baseShapeName))
            {
                EditorGUILayout.LabelField("下の一覧から「ベース」ボタンを押して選択");
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("選択中: " + _baseShapeName);
                    if (GUILayout.Button("クリア", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
                    {
                        _baseShapeName = "";
                        if (_overwriteShape)
                            _newShapeName = "";
                    }
                }
            }
        }
        
        private void DrawOverwriteSettings()
        {
            EditorGUI.BeginChangeCheck();
            _overwriteShape = EditorGUILayout.Toggle(new GUIContent("シェイプキーを上書きする", "チェックを入れるとベースシェイプキーを上書きします"), _overwriteShape);
            
            if (EditorGUI.EndChangeCheck())
            {
                if (_overwriteShape && !string.IsNullOrEmpty(_baseShapeName))
                    _newShapeName = _baseShapeName;
                else if (!_overwriteShape)
                    _newShapeName = string.IsNullOrEmpty(_baseShapeName) ? "" : _baseShapeName + "_合成";
            }
        }
        
        private void DrawNewShapeNameField()
        {
            if (!_overwriteShape)
            {
                _newShapeName = EditorGUILayout.TextField(new GUIContent("新しい名前", "新しいシェイプキー名"), _newShapeName);
            }
            else
            {
                EditorGUILayout.LabelField("上書き対象", string.IsNullOrEmpty(_baseShapeName) ? "未選択" : _baseShapeName);
            }
        }
        
        private void DrawComposeShapeAndListSelection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawComposeShapeSelection();
                DrawShapeSelectionList();
            }
        }
        
        private void DrawComposeShapeSelection()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorGUIUtility.currentViewWidth * VIEW_WIDTH_RATIO)))
            {
                DrawColoredBox(SelectColor, () => 
                {
                    EditorGUILayout.LabelField("合成するシェイプキー", EditorStyles.boldLabel);
                    
                    if (_composeShapes.Count == 0)
                    {
                        EditorGUILayout.LabelField("右の一覧から「追加」ボタンを押して選択");
                    }
                    else
                    {
                        _composeShapeScroll = EditorGUILayout.BeginScrollView(_composeShapeScroll, GUILayout.Height(SCROLL_HEIGHT));
                        DrawComposeShapeList();
                        EditorGUILayout.EndScrollView();
                    }
                });
            }
        }
        
        private void DrawComposeShapeList()
        {
            for (int i = _composeShapes.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("×", GUILayout.Width(BUTTON_WIDTH_SMALL)))
                    {
                        _composeShapes.RemoveAt(i);
                        continue;
                    }
                    
                    var item = _composeShapes[i];
                    EditorGUILayout.LabelField(item.name, GUILayout.Width(120));
                    
                    float weight = EditorGUILayout.Slider(item.weight, -100f, 100f);
                    if (!Mathf.Approximately(weight, item.weight))
                        _composeShapes[i] = (item.name, weight);
                }
            }
        }
        
        private void DrawShapeSelectionList()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawColoredBox(ContentColor, () => 
                {
                    EditorGUILayout.LabelField("シェイプキー一覧", EditorStyles.boldLabel);
                    
                    _composeSearchText = EditorGUILayout.TextField("検索", _composeSearchText);
                    EditorGUILayout.Space(5);
                    
                    _shapeListScroll = EditorGUILayout.BeginScrollView(_shapeListScroll, GUILayout.Height(SCROLL_HEIGHT));
                    DrawAvailableShapeList();
                    EditorGUILayout.EndScrollView();
                });
            }
        }
        
        private void DrawAvailableShapeList()
        {
            if (_shapeNames.Count == 0) 
            {
                EditorGUILayout.LabelField("シェイプキーがありません");
                return;
            }
            
            foreach (var shapeName in _shapeNames)
            {
                if (!string.IsNullOrEmpty(_composeSearchText) && 
                    !shapeName.Contains(_composeSearchText, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawBaseShapeSelectionButton(shapeName);
                    DrawAddShapeButton(shapeName);
                    EditorGUILayout.LabelField(shapeName);
                }
            }
        }
        
        private void DrawBaseShapeSelectionButton(string shapeName)
        {
            bool isBase = shapeName == _baseShapeName;
            GUI.enabled = !isBase;
            if (GUILayout.Button(isBase ? "ベース中" : "ベース", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
            {
                _baseShapeName = shapeName;
                if (_overwriteShape)
                    _newShapeName = shapeName;
            }
            GUI.enabled = true;
        }
        
        private void DrawAddShapeButton(string shapeName)
        {
            if (GUILayout.Button("追加", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
            {
                _composeShapes.Add((shapeName, 100f));
            }
        }
        
        private void DrawComposeExecuteButton()
        {
            DrawColoredBox(HeaderColor, () => 
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全クリア", GUILayout.Height(EXECUTE_BUTTON_HEIGHT), GUILayout.Width(BUTTON_WIDTH_LARGE)))
                    {
                        ResetComposeData();
                    }
                    
                    GUILayout.FlexibleSpace();
                    
                    bool canCompose = CanExecuteCompose();
                    
                    GUI.enabled = canCompose;
                    if (GUILayout.Button("合成実行", GUILayout.Height(EXECUTE_BUTTON_HEIGHT), GUILayout.Width(BUTTON_WIDTH_LARGE)))
                        ExecuteShapeCompose();
                    GUI.enabled = true;
                }
            });
        }
        
        private bool CanExecuteCompose()
        {
            return !string.IsNullOrEmpty(_baseShapeName) && 
                   (_overwriteShape || !string.IsNullOrEmpty(_newShapeName));
        }
        
        private void ResetComposeData()
        {
            _composeShapes.Clear();
            _baseShapeName = "";
            _newShapeName = "";
        }
        
        private void ExecuteShapeCompose()
        {
            if (_composeTarget?.sharedMesh == null || string.IsNullOrEmpty(_baseShapeName))
            {
                EditorUtility.DisplayDialog("エラー", "ベースシェイプキーが選択されていません。", "OK");
                return;
            }
            
            string targetName = _overwriteShape ? _baseShapeName : _newShapeName;
            
            if (string.IsNullOrEmpty(targetName))
            {
                EditorUtility.DisplayDialog("エラー", "出力名が指定されていません。", "OK");
                return;
            }
            
            var originalMesh = _composeTarget.sharedMesh;
            
            if (!_overwriteShape && CheckForDuplicateShapeName(originalMesh, targetName))
                return;
            
            try
            {
                EditorUtility.DisplayProgressBar("合成中", "メッシュ処理中...", 0f);
                
                var newMesh = CreateComposedMesh(originalMesh, targetName);
                if (newMesh == null) return;
                
                EditorUtility.DisplayProgressBar("合成中", "保存中...", 0.8f);
                
                string savePath = SaveMeshAsset(newMesh, targetName);
                if (string.IsNullOrEmpty(savePath)) return;
                
                EditorUtility.DisplayProgressBar("合成中", "適用中...", 0.9f);
                
                ApplyComposedMesh(newMesh, savePath, targetName);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("エラー", $"合成エラー:\n{e.Message}", "OK");
                Debug.LogError($"Compose error: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        private bool CheckForDuplicateShapeName(Mesh mesh, string targetName)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i) == targetName)
                {
                    return !EditorUtility.DisplayDialog("警告", $"「{targetName}」は既に存在します。続行しますか？", "続行", "キャンセル");
                }
            }
            return false;
        }
        
        private Mesh CreateComposedMesh(Mesh originalMesh, string targetName)
        {
            var mesh = Object.Instantiate(originalMesh);
            mesh.name = $"{originalMesh.name}_Composed";
            
            int baseIndex = FindBlendShapeIndex(originalMesh, _baseShapeName);
            if (baseIndex < 0)
            {
                EditorUtility.DisplayDialog("エラー", $"ベースシェイプキー「{_baseShapeName}」が見つかりません。", "OK");
                return null;
            }
            
            var composedDeltas = ComputeComposedDeltas(originalMesh, baseIndex);
            
            if (_overwriteShape)
            {
                return CreateMeshWithReplacedShape(mesh, originalMesh, targetName, composedDeltas);
            }
            else
            {
                mesh.ClearBlendShapes();  // 追加
                // 既存のブレンドシェイプを再追加
                for (int i = 0; i < originalMesh.blendShapeCount; i++)
                {
                    CopyExistingBlendShape(originalMesh, mesh, i, originalMesh.GetBlendShapeName(i));
                }
                mesh.AddBlendShapeFrame(targetName, 100f, composedDeltas.vertices, composedDeltas.normals, composedDeltas.tangents);
                return mesh;
            }
        }
        
        private int FindBlendShapeIndex(Mesh mesh, string shapeName)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i) == shapeName)
                    return i;
            }
            return -1;
        }
        
        private (Vector3[] vertices, Vector3[] normals, Vector3[] tangents) ComputeComposedDeltas(Mesh originalMesh, int baseIndex)
        {
            var vertices = originalMesh.vertices;
            var normals = originalMesh.normals;
            var tangents = originalMesh.tangents;
            
            var composedVertices = new Vector3[vertices.Length];
            var composedNormals = new Vector3[normals.Length];
            var composedTangents = new Vector3[tangents.Length];
            
            System.Array.Copy(vertices, composedVertices, vertices.Length);
            System.Array.Copy(normals, composedNormals, normals.Length);
            for (int i = 0; i < tangents.Length; i++)
                composedTangents[i] = tangents[i];
            
            ApplyBaseShapeDeltas(originalMesh, baseIndex, composedVertices, composedNormals, composedTangents);
            ApplyComposeShapeDeltas(originalMesh, composedVertices, composedNormals, composedTangents);
            
            var finalDeltas = ComputeFinalDeltas(vertices, normals, tangents, composedVertices, composedNormals, composedTangents);
            return finalDeltas;
        }
        
        private void ApplyBaseShapeDeltas(Mesh mesh, int baseIndex, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            var deltaVertices = new Vector3[vertices.Length];
            var deltaNormals = new Vector3[normals.Length];
            var deltaTangents = new Vector3[tangents.Length];
            
            mesh.GetBlendShapeFrameVertices(baseIndex, 0, deltaVertices, deltaNormals, deltaTangents);
            
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += deltaVertices[i];
                normals[i] += deltaNormals[i];
                tangents[i] += deltaTangents[i];
            }
        }
        
        private void ApplyComposeShapeDeltas(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            float progress = 0.2f;
            float step = 0.6f / _composeShapes.Count;
            
            foreach (var (name, weight) in _composeShapes)
            {
                EditorUtility.DisplayProgressBar("合成中", $"処理中: {name}", progress);
                
                int index = FindBlendShapeIndex(mesh, name);
                if (index >= 0)
                {
                    ApplyShapeDelta(mesh, index, weight, vertices, normals, tangents);
                }
                
                progress += step;
            }
        }
        
        private void ApplyShapeDelta(Mesh mesh, int index, float weight, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            var deltaVertices = new Vector3[vertices.Length];
            var deltaNormals = new Vector3[normals.Length];
            var deltaTangents = new Vector3[tangents.Length];
            
            mesh.GetBlendShapeFrameVertices(index, 0, deltaVertices, deltaNormals, deltaTangents);
            
            float multiplier = weight / 100f;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += deltaVertices[i] * multiplier;
                normals[i] += deltaNormals[i] * multiplier;
                tangents[i] += deltaTangents[i] * multiplier;
            }
        }
        
        private (Vector3[] vertices, Vector3[] normals, Vector3[] tangents) ComputeFinalDeltas(
            Vector3[] originalVertices, Vector3[] originalNormals, Vector4[] originalTangents,
            Vector3[] composedVertices, Vector3[] composedNormals, Vector3[] composedTangents)
        {
            var deltaVertices = new Vector3[originalVertices.Length];
            var deltaNormals = new Vector3[originalNormals.Length];
            var deltaTangents = new Vector3[originalTangents.Length];
            
            for (int i = 0; i < originalVertices.Length; i++)
            {
                deltaVertices[i] = composedVertices[i] - originalVertices[i];
                deltaNormals[i] = composedNormals[i] - originalNormals[i];
                deltaTangents[i] = composedTangents[i] - new Vector3(originalTangents[i].x, originalTangents[i].y, originalTangents[i].z);
            }
            
            return (deltaVertices, deltaNormals, deltaTangents);
        }
        
        private Mesh CreateMeshWithReplacedShape(Mesh mesh, Mesh originalMesh, string targetName, (Vector3[] vertices, Vector3[] normals, Vector3[] tangents) deltas)
        {
            var tempMesh = new Mesh
            {
                vertices = mesh.vertices,
                triangles = mesh.triangles,
                normals = mesh.normals,
                tangents = mesh.tangents,
                uv = mesh.uv,
                uv2 = mesh.uv2,  // 追加
                uv3 = mesh.uv3,  // 追加
                uv4 = mesh.uv4,  // 追加
                colors = mesh.colors,  // 追加
                boneWeights = mesh.boneWeights,  // 追加
                bindposes = mesh.bindposes,  // 追加
                bounds = mesh.bounds,  // 追加：これが重要
                name = mesh.name
            };
            
            // サブメッシュ情報もコピー（追加）
            tempMesh.subMeshCount = mesh.subMeshCount;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                tempMesh.SetSubMesh(i, mesh.GetSubMesh(i));
            }
            
            for (int i = 0; i < originalMesh.blendShapeCount; i++)
            {
                string shapeName = originalMesh.GetBlendShapeName(i);
                if (shapeName == targetName)
                {
                    tempMesh.AddBlendShapeFrame(shapeName, 100f, deltas.vertices, deltas.normals, deltas.tangents);
                }
                else
                {
                    CopyExistingBlendShape(originalMesh, tempMesh, i, shapeName);
                }
            }
            
            return tempMesh;
        }
        
        private void CopyExistingBlendShape(Mesh originalMesh, Mesh targetMesh, int shapeIndex, string shapeName)
        {
            var vertices = originalMesh.vertices;
            var deltaVertices = new Vector3[vertices.Length];
            var deltaNormals = new Vector3[vertices.Length];
            var deltaTangents = new Vector3[vertices.Length];
            
            originalMesh.GetBlendShapeFrameVertices(shapeIndex, 0, deltaVertices, deltaNormals, deltaTangents);
            targetMesh.AddBlendShapeFrame(shapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
        }
        
        private string SaveMeshAsset(Mesh mesh, string shapeName)
        {
            string saveDirectory = "Assets/qsyi/GeneratedMeshes";
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
                AssetDatabase.Refresh();
            }
            
            string fileName = $"{mesh.name}_{shapeName}.asset";
            string filePath = Path.Combine(saveDirectory, fileName);
            
            int counter = 1;
            while (File.Exists(filePath))
            {
                fileName = $"{mesh.name}_{shapeName}_{counter++}.asset";
                filePath = Path.Combine(saveDirectory, fileName);
            }
            
            AssetDatabase.CreateAsset(mesh, filePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            return filePath;
        }
        
        private void ApplyComposedMesh(Mesh newMesh, string savePath, string targetName)
        {
            Undo.RecordObject(_composeTarget, "Compose BlendShapes");
            _composeTarget.sharedMesh = newMesh;
            EditorUtility.SetDirty(_composeTarget);
            
            EditorUtility.DisplayDialog("完了", $"「{targetName}」を合成しました。\n{savePath}", "OK");
            ScanForCompose();
        }
        
        private void DrawScaleAdjustment()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                // アーマーチュア設定は常に表示
                DrawArmatureSettings();
                
                bool hasOutfitBones = _outfitBones.Count > 0;
                bool hasAvatarBones = _avatarBones.Count > 0;
                
                DrawColoredBox(HeaderColor, () => 
                {
                    bool isValidTarget = _targets.All(t => t?.GetComponent<ModularAvatarMeshSettings>() != null);
                    if (!isValidTarget)
                    {
                        EditorGUILayout.HelpBox("SetupOutfitした衣装を入れてください。", MessageType.Error);
                    }
                    
                    if (!hasOutfitBones)
                    {
                        EditorGUILayout.HelpBox("衣装のボーンが見つかりません。", MessageType.Warning);
                    }
                    
                    if (!hasAvatarBones && _avatarArmature != null)
                    {
                        EditorGUILayout.HelpBox("素体のボーンが見つかりません。", MessageType.Warning);
                    }
                });

                DrawBoneDetectionWarnings();
                DrawAvatarScaleStatusList();
                DrawAutoScaleSyncControls(hasAvatarBones && hasOutfitBones);
            }
        }

        private void DrawBoneDetectionWarnings()
        {
            var missingAvatarBones = BONE_ORDER.Where(b => !_avatarBones.ContainsKey(b)).ToList();
            if (missingAvatarBones.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"素体で未検出のボーンがあります ({missingAvatarBones.Count}/{BONE_ORDER.Length})\n{string.Join(", ", missingAvatarBones)}",
                    MessageType.Warning);
            }

            var outfits = _targets.Where(t => t != null).ToList();
            foreach (var outfit in outfits)
            {
                if (!_outfitBones.TryGetValue(outfit, out var boneMap))
                {
                    EditorGUILayout.HelpBox($"「{outfit.name}」のボーンを検出できませんでした。", MessageType.Warning);
                    continue;
                }

                var missingOutfitBones = BONE_ORDER.Where(b => !boneMap.ContainsKey(b)).ToList();
                if (missingOutfitBones.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"「{outfit.name}」で未検出のボーンがあります ({missingOutfitBones.Count}/{BONE_ORDER.Length})\n{string.Join(", ", missingOutfitBones)}",
                        MessageType.Warning);
                }
            }
        }

        private void DrawAvatarScaleStatusList()
        {
            DrawColoredBox(ContentColor, () =>
            {
                EditorGUILayout.LabelField("素体ボーンスケール", EditorStyles.boldLabel);
                _scaleStatusScroll = EditorGUILayout.BeginScrollView(_scaleStatusScroll, GUILayout.ExpandHeight(true));

                foreach (var boneName in BONE_ORDER)
                {
                    DrawColoredBox(BaseColor, () =>
                    {
                        if (_avatarBones.TryGetValue(boneName, out var avatarBone) && avatarBone != null)
                        {
                            EditorGUILayout.LabelField(boneName, EditorStyles.boldLabel);

                            EditorGUI.BeginChangeCheck();
                            Vector3 newTransformScale = EditorGUILayout.Vector3Field("Transform", avatarBone.localScale);
                            if (EditorGUI.EndChangeCheck() && !Approximately(newTransformScale, avatarBone.localScale))
                            {
                                Undo.RecordObject(avatarBone, "Change Bone Transform Scale");
                                avatarBone.localScale = newTransformScale;
                                EditorUtility.SetDirty(avatarBone);
                            }

                            var adjuster = avatarBone.GetComponent<ModularAvatarScaleAdjuster>();
                            if (adjuster == null)
                            {
                                EditorGUILayout.LabelField("ScaleAdjuster: なし");
                            }
                            else
                            {
                                EditorGUI.BeginChangeCheck();
                                Vector3 newAdjusterScale = EditorGUILayout.Vector3Field("ScaleAdjuster", adjuster.Scale);
                                if (EditorGUI.EndChangeCheck() && !Approximately(newAdjusterScale, adjuster.Scale))
                                {
                                    ApplyScaleAdjusterScale(adjuster, newAdjusterScale, true, "Change Bone ScaleAdjuster Scale");
                                }
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"{boneName}: 未検出");
                        }
                    });
                }

                EditorGUILayout.EndScrollView();
            }, GUILayout.ExpandHeight(true));
        }
        
        private void DrawArmatureSettings()
        {
            DrawColoredBox(HeaderColor, () => 
            {
                _serializedObject.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_armatureProperty, new GUIContent("素体Armature"));
                DrawOutfitArmatureSettings();
                bool changed = EditorGUI.EndChangeCheck();
                _serializedObject.ApplyModifiedProperties();
                
                var armature = _armatureProperty.objectReferenceValue as Transform;
                if (armature != _avatarArmature)
                {
                    _avatarArmature = armature;
                }
                
                if (_avatarArmature == null)
                    EditorGUILayout.HelpBox("素体のArmatureを設定してください。", MessageType.Warning);
                
                if (changed)
                    ScanBones();
            });
            
            EditorGUILayout.Space(5);
        }

        private void DrawOutfitArmatureSettings()
        {
            EditorGUILayout.Space(4);

            var targetOutfits = _targets.Where(t => t != null).ToList();
            if (targetOutfits.Count == 0)
            {
                EditorGUILayout.HelpBox("探索対象に衣装を追加すると設定できます。", MessageType.Info);
                return;
            }

            foreach (var outfit in targetOutfits)
            {
                var entry = GetOrCreateOutfitArmatureEntry(outfit);
                int index = _outfitArmatureEntries.IndexOf(entry);
                if (index < 0) continue;

                var entryProperty = _outfitArmatureEntriesProperty.GetArrayElementAtIndex(index);
                var armaturesProperty = entryProperty.FindPropertyRelative("Armatures");
                EditorGUILayout.PropertyField(armaturesProperty, new GUIContent("衣装Armature"), true);
            }
        }
        
        private void DrawAutoScaleSyncControls(bool canSync)
        {
            DrawColoredBox(SelectColor, () =>
            {
                bool contextAvailable = IsAutoSyncContextAvailable(out var contextMessage);
                if (!contextAvailable && _autoScaleSyncEnabled)
                    SetAutoScaleSyncEnabled(false);

                bool previousSyncPositionAndRotation = _autoSyncPositionAndRotation;
                _autoSyncPositionAndRotation = EditorGUILayout.ToggleLeft(
                    "Position/Rotationも同期する(実験的機能)",
                    _autoSyncPositionAndRotation);

                if (_autoScaleSyncEnabled && previousSyncPositionAndRotation != _autoSyncPositionAndRotation)
                {
                    _avatarScaleCache.Clear();
                    _nextAutoSyncTime = 0d;
                    ApplyAvatarScalesToOutfits();
                }

                bool previousEnabled = _autoScaleSyncEnabled;
                GUI.enabled = canSync && contextAvailable;
                string buttonLabel = _autoScaleSyncEnabled ? "同期終了" : "同期開始";
                bool newEnabled = GUILayout.Toggle(_autoScaleSyncEnabled, buttonLabel, "Button", GUILayout.Height(EXECUTE_BUTTON_HEIGHT));
                GUI.enabled = true;

                if (newEnabled != previousEnabled)
                {
                    SetAutoScaleSyncEnabled(newEnabled);
                }

                if (canSync && contextAvailable)
                    EditorGUILayout.HelpBox("同期中は常にスケール等の変更を反映します。", MessageType.Info);

                if (!canSync)
                    EditorGUILayout.HelpBox("素体と衣装の両方にボーンが必要です。", MessageType.Info);
                else if (!contextAvailable)
                    EditorGUILayout.HelpBox(contextMessage, MessageType.Info);
            });
        }
        
        private void OnEditorUpdate()
        {
            if (!_autoScaleSyncEnabled || _isApplyingAutoSync)
                return;

            if (!IsAutoSyncContextAvailable(out _))
            {
                SetAutoScaleSyncEnabled(false);
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextAutoSyncTime)
                return;

            _nextAutoSyncTime = EditorApplication.timeSinceStartup + AUTO_SYNC_INTERVAL_SECONDS;

            if (_isDirty || _avatarBones.Count == 0 || _outfitBones.Count == 0)
                ScanBones();

            if (!HasAvatarSyncSourceChanges())
                return;

            ApplyAvatarScalesToOutfits();
        }

        private void SetAutoScaleSyncEnabled(bool enabled)
        {
            _autoScaleSyncEnabled = enabled;
            _avatarScaleCache.Clear();
            _nextAutoSyncTime = 0d;

            if (_autoScaleSyncEnabled)
            {
                ScanBones();
                ApplyAvatarScalesToOutfits();
            }
        }

        private bool IsAutoSyncContextAvailable(out string message)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                message = "Play中は同期を停止します。";
                return false;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                message = "Prefab編集中は同期を停止します。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private bool HasAvatarSyncSourceChanges()
        {
            bool changed = false;
            var seen = new HashSet<string>();

            foreach (var boneName in BONE_ORDER)
            {
                if (!_avatarBones.TryGetValue(boneName, out var avatarBone) || avatarBone == null)
                    continue;

                seen.Add(boneName);
                var adjuster = avatarBone.GetComponent<ModularAvatarScaleAdjuster>();
                bool hasAdjuster = adjuster != null;
                Vector3 adjusterScale = hasAdjuster ? adjuster.Scale : Vector3.zero;
                var snapshot = (
                    avatarBone.localScale,
                    avatarBone.localPosition,
                    avatarBone.localRotation,
                    hasAdjuster,
                    adjusterScale);

                if (!_avatarScaleCache.TryGetValue(boneName, out var cached) ||
                    !AreSyncSnapshotsEqual(cached, snapshot, _autoSyncPositionAndRotation))
                {
                    _avatarScaleCache[boneName] = snapshot;
                    changed = true;
                }
            }

            var removed = _avatarScaleCache.Keys.Where(k => !seen.Contains(k)).ToList();
            if (removed.Count > 0)
            {
                foreach (var key in removed)
                    _avatarScaleCache.Remove(key);
                changed = true;
            }

            return changed;
        }

        private static bool AreSyncSnapshotsEqual(
            (Vector3 localScale, Vector3 localPosition, Quaternion localRotation, bool hasAdjuster, Vector3 adjusterScale) a,
            (Vector3 localScale, Vector3 localPosition, Quaternion localRotation, bool hasAdjuster, Vector3 adjusterScale) b,
            bool includePositionAndRotation)
        {
            if (!Approximately(a.localScale, b.localScale))
                return false;
            if (includePositionAndRotation)
            {
                if (!Approximately(a.localPosition, b.localPosition))
                    return false;
                if (!Approximately(a.localRotation, b.localRotation))
                    return false;
            }
            if (a.hasAdjuster != b.hasAdjuster)
                return false;
            if (a.hasAdjuster && !Approximately(a.adjusterScale, b.adjusterScale))
                return false;
            return true;
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                   Mathf.Approximately(a.y, b.y) &&
                   Mathf.Approximately(a.z, b.z);
        }

        private static bool Approximately(Quaternion a, Quaternion b)
        {
            return Quaternion.Angle(a, b) < 0.01f;
        }

        private static bool IsAdjustChildPositionsEnabled()
        {
            if (!_adjustChildPositionsResolved)
            {
                _adjustChildPositionsResolved = true;

                var toolType = global::System.Type.GetType(
                    "nadena.dev.modular_avatar.core.editor.ScaleAdjusterTool, nadena.dev.modular-avatar.editor");

                if (toolType == null)
                {
                    foreach (var assembly in global::System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        toolType = assembly.GetType("nadena.dev.modular_avatar.core.editor.ScaleAdjusterTool");
                        if (toolType != null)
                            break;
                    }
                }

                if (toolType != null)
                {
                    _adjustChildPositionsField = toolType.GetField(
                        "AdjustChildPositions",
                        BindingFlags.Public | BindingFlags.Static);
                }
            }

            if (_adjustChildPositionsField == null)
                return true;

            try
            {
                return _adjustChildPositionsField.GetValue(null) is bool enabled && enabled;
            }
            catch
            {
                return true;
            }
        }

        private static bool ApplyScaleAdjusterScale(
            ModularAvatarScaleAdjuster adjuster,
            Vector3 targetScale,
            bool recordUndo,
            string undoLabel)
        {
            if (adjuster == null)
                return false;

            Vector3 oldScale = adjuster.Scale;
            if (Approximately(oldScale, targetScale))
                return false;

            if (recordUndo)
                Undo.RecordObject(adjuster, undoLabel);

            adjuster.Scale = targetScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(adjuster);
            EditorUtility.SetDirty(adjuster);

            if (!IsAdjustChildPositionsEnabled())
                return true;

            Vector3 scaleDelta = new Vector3(
                SafeDivide(targetScale.x, oldScale.x),
                SafeDivide(targetScale.y, oldScale.y),
                SafeDivide(targetScale.z, oldScale.z));
            Matrix4x4 updateTransform = Matrix4x4.Scale(scaleDelta);

            foreach (Transform child in adjuster.transform)
            {
                if (recordUndo)
                    Undo.RecordObject(child, undoLabel);

                child.localPosition = updateTransform.MultiplyPoint(child.localPosition);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                EditorUtility.SetDirty(child);
            }

            return true;
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            return Mathf.Abs(denominator) < 0.000001f ? 1f : numerator / denominator;
        }

        private void ApplyAvatarScalesToOutfits()
        {
            if (_avatarBones.Count == 0 || _outfitBones.Count == 0)
                return;

            _isApplyingAutoSync = true;
            try
            {
                Undo.SetCurrentGroupName("Auto Sync Bones");
                int undoGroup = Undo.GetCurrentGroup();
                bool hasAnyChange = false;

                foreach (var boneName in BONE_ORDER)
                {
                    if (!_avatarBones.TryGetValue(boneName, out var avatarBone) || avatarBone == null)
                        continue;

                    var avatarAdjuster = avatarBone.GetComponent<ModularAvatarScaleAdjuster>();
                    Vector3 avatarLocalScale = avatarBone.localScale;
                    Vector3 avatarLocalPosition = avatarBone.localPosition;
                    Quaternion avatarLocalRotation = avatarBone.localRotation;

                    foreach (var outfit in _targets.Where(t => t != null))
                    {
                        if (!_outfitBones.TryGetValue(outfit, out var boneMap) ||
                            !boneMap.TryGetValue(boneName, out var outfitBone) ||
                            outfitBone == null ||
                            outfitBone == avatarBone)
                            continue;

                        if (!Approximately(outfitBone.localScale, avatarLocalScale))
                        {
                            Undo.RecordObject(outfitBone, "Auto Sync Transform Scale");
                            outfitBone.localScale = avatarLocalScale;
                            EditorUtility.SetDirty(outfitBone);
                            hasAnyChange = true;
                        }

                        if (_autoSyncPositionAndRotation)
                        {
                            if (!Approximately(outfitBone.localPosition, avatarLocalPosition))
                            {
                                Undo.RecordObject(outfitBone, "Auto Sync Transform Position");
                                outfitBone.localPosition = avatarLocalPosition;
                                EditorUtility.SetDirty(outfitBone);
                                hasAnyChange = true;
                            }

                            if (!Approximately(outfitBone.localRotation, avatarLocalRotation))
                            {
                                Undo.RecordObject(outfitBone, "Auto Sync Transform Rotation");
                                outfitBone.localRotation = avatarLocalRotation;
                                EditorUtility.SetDirty(outfitBone);
                                hasAnyChange = true;
                            }
                        }

                        if (avatarAdjuster == null)
                            continue;

                        var outfitAdjuster = outfitBone.GetComponent<ModularAvatarScaleAdjuster>();
                        if (outfitAdjuster == null)
                        {
                            outfitAdjuster = Undo.AddComponent<ModularAvatarScaleAdjuster>(outfitBone.gameObject);
                            hasAnyChange = true;
                        }

                        if (!Approximately(outfitAdjuster.Scale, avatarAdjuster.Scale))
                        {
                            hasAnyChange |= ApplyScaleAdjusterScale(
                                outfitAdjuster,
                                avatarAdjuster.Scale,
                                true,
                                "Auto Sync ScaleAdjuster");
                        }
                    }
                }

                if (hasAnyChange)
                    Undo.CollapseUndoOperations(undoGroup);
            }
            finally
            {
                _isApplyingAutoSync = false;
            }
        }
        
        private void ScanData()
        {
            switch (_mode)
            {
                case Mode.Material: ScanMaterials(); break;
                case Mode.BlendShape: 
                    ScanSkinnedMeshRenderers();
                    ScanForCompose();
                    break;
                case Mode.Scale: ScanBones(); break;
                case Mode.MenuGenerator: ScanMenuRenderers(); break;
            }
        }

        private void DrawMenuGenerator()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawColoredBox(HeaderColor, () =>
                {
                    EditorGUILayout.LabelField("メニュー生成", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("lilycalInventory用のメニュー生成設定です。\nフォルダ名と対象レンダラーを指定して、メニューを生成します。", MessageType.Info);
                    _menuFolderName = EditorGUILayout.TextField(new GUIContent("メニューフォルダ名", "生成先フォルダ名"), _menuFolderName);
                });

                DrawColoredBox(BaseColor, () =>
                {
                    EditorGUILayout.LabelField("生成方法", EditorStyles.boldLabel);
                    DrawTabButtons(
                        new[] { "個別に生成", "まとめて生成" },
                        new[]
                        {
                            new GUIContent("個別に生成"),
                            new GUIContent("まとめて生成")
                        },
                        (int)_menuGenerationMode,
                        index => _menuGenerationMode = (MenuGenerationMode)index,
                        false
                    );

                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(MenuGenerationDescriptions[(int)_menuGenerationMode], MessageType.None);
                });

                DrawColoredBox(ContentColor, () =>
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("探索配下のメッシュレンダラー", EditorStyles.boldLabel);

                        if (GUILayout.Button("全選択", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
                            SetAllMenuRendererSelections(true);

                        if (GUILayout.Button("全解除", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
                            SetAllMenuRendererSelections(false);
                    }

                    if (_menuRenderers.Count == 0)
                    {
                        EditorGUILayout.HelpBox("探索対象配下にメッシュレンダラーが見つかりません。", MessageType.Info);
                    }

                    EditorGUILayout.LabelField($"検出数: {_menuRenderers.Count} / 選択数: {GetSelectedMenuRendererCount()}");
                    _menuRendererScroll = EditorGUILayout.BeginScrollView(_menuRendererScroll, GUILayout.ExpandHeight(true));
                    DrawMenuRendererArray();
                    EditorGUILayout.EndScrollView();
                });

                DrawMenuGenerateExecuteButton();
            }
        }

        private void DrawMenuGenerateExecuteButton()
        {
            DrawColoredBox(SelectColor, () =>
            {
                bool hasSelection = GetSelectedMenuRendererCount() > 0;
                GUI.enabled = hasSelection;
                if (GUILayout.Button("メニュー生成", GUILayout.Height(EXECUTE_BUTTON_HEIGHT)))
                    GenerateMenu();
                GUI.enabled = true;

                if (!hasSelection)
                    EditorGUILayout.HelpBox("メニューを生成するには対象レンダラーを 1 つ以上選択してください。", MessageType.Info);
            });
        }

        private void GenerateMenu()
        {
            var parentTarget = _targets.FirstOrDefault(IsValidTarget);
            var selectedRenderers = GetSelectedMenuRenderers();
            if (parentTarget == null || selectedRenderers.Count == 0)
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Menu");

            try
            {
                string folderName = GetGeneratedMenuFolderName();
                var folderObject = new GameObject(folderName);
                Undo.RegisterCreatedObjectUndo(folderObject, "Generate Menu");
                folderObject.transform.SetParent(parentTarget.transform, false);

                var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(folderObject);
                ConfigureFolderMenuItem(menuItem);
                var generatedItemNames = new List<string>();

                if (_menuGenerationMode == MenuGenerationMode.PerRenderer)
                {
                    foreach (var renderer in selectedRenderers)
                    {
                        CreateToggleObject(folderObject.transform, renderer.name, new[] { renderer.gameObject });
                        generatedItemNames.Add(renderer.name);
                    }
                }
                else
                {
                    string mergedName = string.Join("/", selectedRenderers.Select(r => r.name));
                    CreateToggleObject(folderObject.transform, mergedName, selectedRenderers.Select(r => r.gameObject));
                    generatedItemNames.Add(mergedName);
                }

                EditorUtility.SetDirty(folderObject);
                ScanData();
                EditorUtility.DisplayDialog(
                    "メニュー生成",
                    BuildMenuGeneratedDialogMessage(folderName, generatedItemNames),
                    "OK");
                Debug.Log($"[qsToolBox] Generated menu '{folderObject.name}' with {selectedRenderers.Count} renderer target(s).");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private string GetGeneratedMenuFolderName()
        {
            string baseName = string.IsNullOrWhiteSpace(_menuFolderName)
                ? _targets.FirstOrDefault(IsValidTarget)?.name ?? "Menu"
                : _menuFolderName.Trim();

            return baseName.StartsWith("Menu_") ? baseName : $"Menu_{baseName}";
        }

        private void ConfigureFolderMenuItem(ModularAvatarMenuItem menuItem)
        {
            menuItem.PortableControl.Type = PortableControlType.SubMenu;
            menuItem.PortableControl.Value = 1f;
            menuItem.PortableControl.Parameter = string.Empty;
            menuItem.PortableControl.Icon = null;
            menuItem.MenuSource = SubmenuSource.Children;
            menuItem.isSynced = true;
            menuItem.isSaved = true;
            menuItem.isDefault = false;
            menuItem.automaticValue = true;
            menuItem.label = string.Empty;

            EditorUtility.SetDirty(menuItem);
        }

        private void CreateToggleObject(Transform parent, string objectName, IEnumerable<GameObject> targetObjects)
        {
            var toggleObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(toggleObject, "Generate Menu");
            toggleObject.transform.SetParent(parent, false);

            var itemToggler = Undo.AddComponent<ItemToggler>(toggleObject);
            ConfigureItemToggler(itemToggler, targetObjects.Where(target => target != null).Distinct().ToList());
            EditorUtility.SetDirty(toggleObject);
        }

        private void ConfigureItemToggler(ItemToggler itemToggler, IReadOnlyList<GameObject> targetObjects)
        {
            var serializedObject = new SerializedObject(itemToggler);
            serializedObject.Update();

            serializedObject.FindProperty("menuName").stringValue = string.Empty;
            serializedObject.FindProperty("parentOverride").objectReferenceValue = null;
            serializedObject.FindProperty("icon").objectReferenceValue = null;
            serializedObject.FindProperty("parentOverrideMA").objectReferenceValue = null;
            serializedObject.FindProperty("isSave").boolValue = true;
            serializedObject.FindProperty("isLocalOnly").boolValue = false;
            serializedObject.FindProperty("autoFixDuplicate").boolValue = true;
            serializedObject.FindProperty("defaultValue").boolValue = false;

            var parameterProperty = serializedObject.FindProperty("parameter");
            var objectsProperty = parameterProperty.FindPropertyRelative("objects");
            objectsProperty.arraySize = targetObjects.Count;
            for (int i = 0; i < targetObjects.Count; i++)
            {
                var objectElement = objectsProperty.GetArrayElementAtIndex(i);
                objectElement.FindPropertyRelative("obj").objectReferenceValue = targetObjects[i];
                objectElement.FindPropertyRelative("value").boolValue = false;
            }

            parameterProperty.FindPropertyRelative("blendShapeModifiers").arraySize = 0;
            parameterProperty.FindPropertyRelative("materialReplacers").arraySize = 0;
            parameterProperty.FindPropertyRelative("materialPropertyModifiers").arraySize = 0;
            parameterProperty.FindPropertyRelative("clips").arraySize = 0;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(itemToggler);
        }

        private string BuildMenuGeneratedDialogMessage(string folderName, IReadOnlyList<string> generatedItemNames)
        {
            var lines = new List<string> { folderName };
            lines.AddRange(generatedItemNames.Select(name => $"・{name}"));
            lines.Add(string.Empty);
            lines.Add("メニューを生成しました");
            return string.Join("\n", lines);
        }

        private void ScanMenuRenderers()
        {
            if (string.IsNullOrWhiteSpace(_menuFolderName))
            {
                var firstTarget = _targets.FirstOrDefault(IsValidTarget);
                if (firstTarget != null)
                    _menuFolderName = firstTarget.name;
            }

            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (!IsMenuRendererCandidate(renderer) || _menuRenderers.Contains(renderer))
                        continue;

                    _menuRenderers.Add(renderer);

                    int instanceId = renderer.GetInstanceID();
                    if (!_menuRendererSelection.ContainsKey(instanceId))
                        _menuRendererSelection[instanceId] = false;
                }
            }

            _menuRenderers = _menuRenderers
                .Where(renderer => renderer == null || IsMenuRendererCandidate(renderer))
                .Distinct()
                .ToList();

            var activeIds = _menuRenderers.Where(r => r != null).Select(r => r.GetInstanceID()).ToHashSet();
            var staleIds = _menuRendererSelection.Keys.Where(id => !activeIds.Contains(id)).ToArray();
            foreach (var staleId in staleIds)
                _menuRendererSelection.Remove(staleId);
        }
        
        private void ScanSkinnedMeshRenderers()
        {
            _skinnedMeshRenderers.Clear();
            
            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                foreach (var smr in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh?.blendShapeCount > 0)
                    {
                        _skinnedMeshRenderers.Add(smr);
                    }
                }
            }
        }
        
        private void ScanMaterials()
        {
            _materials.Clear();
            _materialUsage.Clear();
            
            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                {
                    ProcessRendererMaterials(renderer);
                }
            }
        }
        
        private void ProcessRendererMaterials(Renderer renderer)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                
                if (!_materialUsage.TryGetValue(material, out var usageList))
                {
                    usageList = new List<(Renderer, int)>();
                    _materialUsage[material] = usageList;
                    _materials.Add(material);
                }
                usageList.Add((renderer, i));
            }
        }
        
        private void ScanBones()
        {
            _outfitBones.Clear();
            _avatarBones.Clear();
            CleanupOutfitArmatureEntries();
            
            FindAndSetAvatarArmature();
            
            if (_avatarArmature != null)
                BuildBoneMap(_avatarArmature, _avatarBones);

            foreach (var outfit in _targets.Where(t => t != null))
            {
                var entry = GetOrCreateOutfitArmatureEntry(outfit);
                TryAutoAssignOutfitArmatureOnce(entry, outfit);
                var armatures = ResolveOutfitArmatures(outfit);
                if (armatures.Count > 0)
                {
                    var boneMap = new Dictionary<string, Transform>();
                    foreach (var armature in armatures)
                    {
                        var partialBoneMap = new Dictionary<string, Transform>();
                        BuildBoneMap(armature, partialBoneMap);

                        foreach (var kv in partialBoneMap)
                        {
                            if (!boneMap.ContainsKey(kv.Key))
                                boneMap[kv.Key] = kv.Value;
                        }
                    }

                    if (boneMap.Count > 0)
                        _outfitBones[outfit] = boneMap;
                }
            }
        }

        private OutfitArmatureEntry GetOrCreateOutfitArmatureEntry(GameObject outfit)
        {
            var entry = _outfitArmatureEntries.FirstOrDefault(e => e != null && e.Outfit == outfit);
            if (entry != null) return entry;

            entry = new OutfitArmatureEntry { Outfit = outfit };
            _outfitArmatureEntries.Add(entry);
            return entry;
        }

        private void TryAutoAssignOutfitArmatureOnce(OutfitArmatureEntry entry, GameObject outfit)
        {
            if (entry == null || outfit == null || entry.AutoAssigned)
                return;

            entry.AutoAssigned = true;
            if (entry.Armatures != null && entry.Armatures.Any(a => a != null))
                return;

            var autoArmature = FindChildByKeyword(outfit.transform, "armature");
            if (autoArmature == null)
                return;

            if (entry.Armatures == null)
                entry.Armatures = new List<Transform>();

            entry.Armatures.Add(autoArmature);
        }

        private void CleanupOutfitArmatureEntries()
        {
            _outfitArmatureEntries.RemoveAll(e => e == null || e.Outfit == null || !_targets.Contains(e.Outfit));
            foreach (var entry in _outfitArmatureEntries)
            {
                if (entry.Armatures == null)
                    entry.Armatures = new List<Transform>();
            }
        }

        private List<Transform> ResolveOutfitArmatures(GameObject outfit)
        {
            var entry = _outfitArmatureEntries.FirstOrDefault(e => e != null && e.Outfit == outfit);
            if (entry?.Armatures != null)
            {
                var manualArmatures = entry.Armatures
                    .Where(a => a != null)
                    .Distinct()
                    .ToList();
                if (manualArmatures.Count > 0)
                    return manualArmatures;
            }
            return new List<Transform>();
        }
        
        private void FindAndSetAvatarArmature()
        {
            if (_avatarArmature == null)
            {
                _avatarArmature = FindAvatarArmature();
                if (_avatarArmature != null)
                {
                    _serializedObject.Update();
                    _armatureProperty.objectReferenceValue = _avatarArmature;
                    _serializedObject.ApplyModifiedProperties();
                }
            }
        }
        
        private void ScanForCompose()
        {
            UpdateComposeTarget();
            
            _shapeNames.Clear();
            
            if (_composeTarget?.sharedMesh != null)
            {
                var mesh = _composeTarget.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    _shapeNames.Add(mesh.GetBlendShapeName(i));
            }
        }
        
        private void UpdateComposeTarget()
        {
            if (NeedsComposeTargetUpdate())
            {
                var newTarget = FindFirstValidSkinnedMeshRenderer();
                if (newTarget != null && newTarget != _composeTarget)
                {
                    _composeTarget = newTarget;
                    ResetComposeData();
                }
            }
        }
        
        private bool NeedsComposeTargetUpdate()
        {
            return _composeTarget == null || 
                   !_skinnedMeshRenderers.Contains(_composeTarget) || 
                   _composeTarget.sharedMesh?.blendShapeCount == 0;
        }
        
        private void DrawColoredBox(Color color, System.Action content, params GUILayoutOption[] options)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            using (new EditorGUILayout.VerticalScope("box", options))
            {
                GUI.backgroundColor = originalColor;
                content();
            }
            GUI.backgroundColor = originalColor;
        }

        private void InitializeMenuRenderersList()
        {
            _menuRenderersList = new ReorderableList(_serializedObject, _menuRenderersProperty, true, false, true, true);
            _menuRenderersList.elementHeight = EditorGUIUtility.singleLineHeight + 4f;
            _menuRenderersList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty element = _menuRenderersProperty.GetArrayElementAtIndex(index);
                var renderer = element.objectReferenceValue as Renderer;
                int instanceId = renderer != null ? renderer.GetInstanceID() : 0;
                bool isSelected = renderer != null &&
                    _menuRendererSelection.TryGetValue(instanceId, out bool selected) &&
                    selected;

                rect.y += 2f;
                Rect toggleRect = new Rect(rect.x, rect.y, 18f, EditorGUIUtility.singleLineHeight);
                Rect fieldRect = new Rect(rect.x + 22f, rect.y, rect.width - 22f, EditorGUIUtility.singleLineHeight);

                bool newSelected = EditorGUI.Toggle(toggleRect, isSelected);
                if (renderer != null && newSelected != isSelected)
                    _menuRendererSelection[instanceId] = newSelected;

                EditorGUI.BeginChangeCheck();
                Object newValue = EditorGUI.ObjectField(fieldRect, GUIContent.none, element.objectReferenceValue, typeof(Renderer), true);
                if (EditorGUI.EndChangeCheck())
                {
                    element.objectReferenceValue = IsMenuRendererCandidate(newValue as Renderer) ? newValue : null;
                    if (renderer != null && renderer != newValue)
                        _menuRendererSelection.Remove(instanceId);
                }
            };
            _menuRenderersList.onRemoveCallback = list =>
            {
                SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(list.index);
                if (element.objectReferenceValue is Renderer renderer)
                    _menuRendererSelection.Remove(renderer.GetInstanceID());

                ReorderableList.defaultBehaviours.DoRemoveButton(list);
            };
            _menuRenderersList.onAddCallback = list =>
            {
                int index = list.serializedProperty.arraySize;
                list.serializedProperty.InsertArrayElementAtIndex(index);
                list.serializedProperty.GetArrayElementAtIndex(index).objectReferenceValue = null;
            };
        }

        private void DrawMenuRendererArray()
        {
            _serializedObject.Update();
            _menuRenderersList.DoLayoutList();
            _serializedObject.ApplyModifiedProperties();
        }

        private void SetAllMenuRendererSelections(bool isSelected)
        {
            foreach (var renderer in _menuRenderers.Where(r => r != null))
                _menuRendererSelection[renderer.GetInstanceID()] = isSelected;
        }

        private int GetSelectedMenuRendererCount()
        {
            return _menuRenderers.Count(renderer =>
                renderer != null &&
                _menuRendererSelection.TryGetValue(renderer.GetInstanceID(), out bool isRendererSelected) &&
                isRendererSelected);
        }

        private List<Renderer> GetSelectedMenuRenderers()
        {
            return _menuRenderers
                .Where(renderer =>
                    renderer != null &&
                    _menuRendererSelection.TryGetValue(renderer.GetInstanceID(), out bool isRendererSelected) &&
                    isRendererSelected)
                .Distinct()
                .ToList();
        }

        private bool IsMenuRendererCandidate(Renderer renderer)
        {
            if (renderer == null)
                return false;

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                return skinnedMeshRenderer.sharedMesh != null;

            if (renderer is MeshRenderer meshRenderer)
                return meshRenderer.GetComponent<MeshFilter>()?.sharedMesh != null;

            return false;
        }

        private bool IsValidTarget(GameObject target) => target != null && !target.CompareTag("EditorOnly");
        
        private SkinnedMeshRenderer FindFirstValidSkinnedMeshRenderer()
        {
            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                var smr = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(s => s.sharedMesh?.blendShapeCount > 0);
                if (smr != null) return smr;
            }
            return null;
        }
        
        private Transform FindAvatarArmature()
        {
            foreach (var target in _targets.Where(t => t != null))
            {
                var current = target.transform;
                while (current != null)
                {
                    var descriptor = current.GetComponent<VRCAvatarDescriptor>();
                    if (descriptor != null)
                        return FindChildByKeyword(descriptor.transform, "armature");
                    current = current.parent;
                }
            }
            return null;
        }
        
        private Transform FindChildByKeyword(Transform parent, string keyword)
        {
            if (parent == null) return null;
            
            string normalizedKeyword = NormalizeBoneToken(keyword);
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                string normalizedName = NormalizeBoneToken(child.name);

                if (normalizedName.Contains(normalizedKeyword))
                    return child;
            }
            return null;
        }

        private static string NormalizeBoneToken(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            var chars = source
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray();
            return new string(chars);
        }
        
        private void BuildBoneMap(Transform armature, Dictionary<string, Transform> boneMap)
        {
            foreach (var boneName in BONE_ORDER)
            {
                Transform foundBone = null;

                if (BONE_PARENT.TryGetValue(boneName, out var parentName) && boneMap.TryGetValue(parentName, out var parent))
                    foundBone = FindChildByKeyword(parent, boneName);
                else
                    foundBone = FindChildByKeyword(armature, boneName);
                
                if (foundBone != null)
                    boneMap[boneName] = foundBone;
            }
        }
        
        private void ReplaceMaterial(Material oldMaterial, Material newMaterial)
        {
            if (!_materialUsage.TryGetValue(oldMaterial, out var usageList)) return;
            
            foreach (var (renderer, index) in usageList.Where(u => u.Item1 != null))
            {
                Undo.RecordObject(renderer, "Change Material");
                var materials = renderer.sharedMaterials;
                if (index >= 0 && index < materials.Length)
                {
                    materials[index] = newMaterial;
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
            }
            
            UpdateMaterialReferences(oldMaterial, newMaterial, usageList);
        }
        
        private void UpdateMaterialReferences(Material oldMaterial, Material newMaterial, List<(Renderer, int)> usageList)
        {
            _materialUsage.Remove(oldMaterial);
            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] == oldMaterial)
                {
                    _materials[i] = newMaterial;
                    break;
                }
            }
            
            if (!_materialUsage.ContainsKey(newMaterial))
                _materialUsage[newMaterial] = usageList;
            else
                _materialUsage[newMaterial].AddRange(usageList);
        }
        
    }
}
#endif
