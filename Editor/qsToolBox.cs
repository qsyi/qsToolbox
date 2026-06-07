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
        
        [SerializeField] private List<GameObject> _targets = new List<GameObject>();
        [SerializeField] private Transform _avatarArmature;
        [SerializeField] private List<OutfitArmatureEntry> _outfitArmatureEntries = new List<OutfitArmatureEntry>();
        [SerializeField] private bool _autoSyncPosition;
        [SerializeField] private bool _autoSyncRotation;
        [SerializeField] private bool _menuPreviewEnabled = true;
        [SerializeField] private List<MenuMeshEntry> _menuMeshEntries = new List<MenuMeshEntry>();
        
        private Mode _mode = Mode.Material;
        private Vector2 _scrollPosition;
        private Vector2 _composeShapeScroll;
        private Vector2 _shapeListScroll;
        private Vector2 _scaleStatusScroll;
        private Vector2 _menuRendererScroll;
        
        private readonly List<SkinnedMeshRenderer> _skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly Dictionary<Material, List<(Renderer renderer, int slot)>> _materialUsage = new Dictionary<Material, List<(Renderer, int)>>();
        private readonly Dictionary<GameObject, Dictionary<string, Transform>> _outfitBones = new Dictionary<GameObject, Dictionary<string, Transform>>();
        private readonly Dictionary<string, Transform> _avatarBones = new Dictionary<string, Transform>();
        private SkinnedMeshRenderer _composeTarget;
        private string _baseShapeName = "";
        private readonly List<(string name, float weight)> _composeShapes = new List<(string, float)>();
        private string _composeSearchText = "";
        private readonly List<string> _shapeNames = new List<string>();
        private string _newShapeName = "";
        private bool _overwriteShape = true;
        private string _menuFolderName = "";
        
        private SerializedObject _serializedObject;
        private SerializedProperty _targetsProperty;
        private SerializedProperty _armatureProperty;
        private SerializedProperty _outfitArmatureEntriesProperty;
        private SerializedProperty _menuMeshEntriesProperty;
        private ReorderableList _menuMeshEntriesList;
        private int _targetHash = -1;
        private bool _isDirty = true;
        private readonly Dictionary<GameObject, bool> _menuPreviewOriginalStates = new Dictionary<GameObject, bool>();
        private static FieldInfo _adjustChildPositionsField;
        private static bool _adjustChildPositionsResolved;
        private static GUIStyle _tabLeft, _tabMid, _tabRight, _tabScan;
        private static GUIStyle _tabLeftSel, _tabMidSel, _tabRightSel;
        private static GUIStyle _dimBoneStyle;
        private static GUIStyle _versionBadgeStyle;
        
        private const float BUTTON_WIDTH_SMALL = 20f;
        private const float BUTTON_WIDTH_MEDIUM = 60f;
        private const float BUTTON_WIDTH_LARGE = 80f;
        private const float SCROLL_HEIGHT = 300f;
        private const float EXECUTE_BUTTON_HEIGHT = 40f;
        private const float VIEW_WIDTH_RATIO = 0.5f;

        private static Color HeaderColor  => EditorGUIUtility.isProSkin ? new Color(0.26f, 0.28f, 0.32f, 1f) : new Color(0.80f, 0.86f, 0.93f, 1f);
        private static Color ContentColor => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.23f, 1f) : new Color(0.86f, 0.86f, 0.87f, 1f);
        private static Color SelectColor  => EditorGUIUtility.isProSkin ? new Color(0.21f, 0.30f, 0.43f, 1f) : new Color(0.74f, 0.87f, 0.99f, 1f);
        private static Color TargetColor  => EditorGUIUtility.isProSkin ? new Color(0.28f, 0.26f, 0.20f, 1f) : new Color(0.96f, 0.93f, 0.82f, 1f);
        private static Color BaseColor    => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.26f, 0.23f, 1f) : new Color(0.85f, 0.92f, 0.86f, 1f);
        private static Color AccentColor  => new Color(0.30f, 0.60f, 1.00f, 1f);
        
        private static readonly string[] TAB_NAMES = { "マテリアル", "ブレンドシェイプ", "スケール", "メニュー生成" };
        private static readonly GUIContent[] TAB_TOOLTIPS = {
            new GUIContent("マテリアル", "探索対象のマテリアルを置換できます"),
            new GUIContent("ブレンドシェイプ", "探索対象のブレンドシェイプを表示・編集します"),
            new GUIContent("スケール", "ModularAvatarのスケール調整機能を使用します"),
            new GUIContent("メニュー生成", "lilycalInventory用の簡易メニューを生成します")
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
                private readonly Dictionary<Material, bool> _materialFoldouts = new Dictionary<Material, bool>();

        [System.Serializable]
        private class OutfitArmatureEntry
        {
            public GameObject Outfit;
            public List<Transform> Armatures = new List<Transform>();
            [HideInInspector] public bool AutoAssigned;
        }

        [System.Serializable]
        private class MenuMeshEntry
        {
            public SkinnedMeshRenderer Renderer;
            public bool Include;
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
            ScanData();
        }

        private void OnDisable()
        {
            RestoreMenuPreviewState();
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }
        
        private void InitializeSerializedObject()
        {
            _serializedObject = new SerializedObject(this);
            _targetsProperty = _serializedObject.FindProperty("_targets");
            _armatureProperty = _serializedObject.FindProperty("_avatarArmature");
            _outfitArmatureEntriesProperty = _serializedObject.FindProperty("_outfitArmatureEntries");
            _menuMeshEntriesProperty = _serializedObject.FindProperty("_menuMeshEntries");
            _targetsProperty.isExpanded = true;
            _outfitArmatureEntriesProperty.isExpanded = true;
            InitializeMenuMeshEntriesList();
        }

        private void InitializeMenuMeshEntriesList()
        {
            _menuMeshEntriesList = new ReorderableList(_serializedObject, _menuMeshEntriesProperty, false, true, true, true);
            _menuMeshEntriesList.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "生成対象");
            };
            _menuMeshEntriesList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = _menuMeshEntriesProperty.GetArrayElementAtIndex(index);
                var includeProperty = element.FindPropertyRelative("Include");
                var rendererProperty = element.FindPropertyRelative("Renderer");

                rect.y += 2f;
                var toggleRect = new Rect(rect.x, rect.y, 18f, EditorGUIUtility.singleLineHeight);
                var fieldRect = new Rect(rect.x + 22f, rect.y, rect.width - 22f, EditorGUIUtility.singleLineHeight);

                includeProperty.boolValue = EditorGUI.Toggle(toggleRect, includeProperty.boolValue);
                EditorGUI.PropertyField(fieldRect, rendererProperty, GUIContent.none);
            };
            _menuMeshEntriesList.elementHeight = EditorGUIUtility.singleLineHeight + 4f;
            _menuMeshEntriesList.onAddCallback = list =>
            {
                int index = _menuMeshEntriesProperty.arraySize;
                _menuMeshEntriesProperty.arraySize++;
                var element = _menuMeshEntriesProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("Renderer").objectReferenceValue = null;
                element.FindPropertyRelative("Include").boolValue = false;
                _serializedObject.ApplyModifiedProperties();
            };
        }
        
        private void OnHierarchyChanged() => _isDirty = true;
        
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

                // 探索対象ラベル + バージョンバッジを同一行に
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("探索対象", EditorStyles.boldLabel);

                if (QsVersionChecker.HasUpdate || QsVersionChecker.CheckComplete)
                {
                    Color accentColor = QsVersionChecker.HasUpdate
                        ? new Color(0.80f, 0.42f, 0.08f)
                        : new Color(0.18f, 0.58f, 0.28f);

                    if (_versionBadgeStyle == null)
                        _versionBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            fontStyle = FontStyle.Normal,
                            alignment = TextAnchor.MiddleLeft,
                            padding   = new RectOffset(8, 8, 2, 2),
                        };
                    _versionBadgeStyle.normal.textColor = accentColor;

                    var badgeContent = new GUIContent(QsVersionChecker.HasUpdate
                        ? $"↑ v{QsVersionChecker.LatestVersion} が公開されています"
                        : $"✓ v{QsVersionChecker.CurrentVersion} 最新バージョンです");

                    float lineH = EditorGUIUtility.singleLineHeight;
                    Vector2 badgeSize = _versionBadgeStyle.CalcSize(badgeContent);
                    Rect badgeRect = GUILayoutUtility.GetRect(badgeSize.x + 4f, lineH, GUILayout.ExpandWidth(false));
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(badgeRect, new Color(accentColor.r, accentColor.g, accentColor.b, 0.10f));
                        EditorGUI.DrawRect(new Rect(badgeRect.x, badgeRect.y, 3f, badgeRect.height), accentColor);
                    }
                    GUI.Label(badgeRect, badgeContent, _versionBadgeStyle);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_targetsProperty, GUIContent.none, true);
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
            foreach (var target in _targets)
                if (target != null)
                    hash = hash * 31 + target.GetInstanceID();
            return hash;
        }
        
        private void DrawMainTabs()
        {
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawTabButtons(TAB_NAMES, TAB_TOOLTIPS, (int)_mode, (index) =>
            {
                if (_mode == Mode.MenuGenerator && _mode != (Mode)index)
                    RestoreMenuPreviewState();

                _mode = (Mode)index;
                GUI.FocusControl(null);
                ScanData();
                _scrollPosition = Vector2.zero;
            }, true);
        }
        
        private void DrawTabButtons(string[] tabNames, GUIContent[] tooltips, int selectedIndex, System.Action<int> onTabSelected, bool showScanButton)
        {
            const float tabHeight = 28f;
            const float accentH   = 3f;

            if (_tabLeft == null)
            {
                _tabLeft    = new GUIStyle(EditorStyles.miniButtonLeft)  { fixedHeight = tabHeight, fontSize = 11, fontStyle = FontStyle.Normal };
                _tabMid     = new GUIStyle(EditorStyles.miniButtonMid)   { fixedHeight = tabHeight, fontSize = 11, fontStyle = FontStyle.Normal };
                _tabRight   = new GUIStyle(EditorStyles.miniButtonRight) { fixedHeight = tabHeight, fontSize = 11, fontStyle = FontStyle.Normal };
                _tabLeftSel  = new GUIStyle(EditorStyles.miniButtonLeft)  { fixedHeight = tabHeight, fontSize = 11, fontStyle = FontStyle.Bold };
                _tabMidSel   = new GUIStyle(EditorStyles.miniButtonMid)   { fixedHeight = tabHeight, fontSize = 11, fontStyle = FontStyle.Bold };
                _tabRightSel = new GUIStyle(EditorStyles.miniButtonRight) { fixedHeight = tabHeight, fontSize = 11, fontStyle = FontStyle.Bold };
                _tabScan    = new GUIStyle(EditorStyles.miniButton) { fixedHeight = tabHeight, fontSize = 14 };
            }

            var originalBg = GUI.backgroundColor;
            float totalWidth = EditorGUIUtility.currentViewWidth - (showScanButton ? 64f : 8f);
            float buttonWidth = totalWidth / tabNames.Length;

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isSelected = selectedIndex == i;
                bool isLast = i == tabNames.Length - 1;
                GUI.backgroundColor = isSelected ? new Color(0.82f, 0.88f, 1f) : originalBg;

                GUIStyle style = isSelected
                    ? (i == 0 ? _tabLeftSel : isLast ? _tabRightSel : _tabMidSel)
                    : (i == 0 ? _tabLeft    : isLast ? _tabRight    : _tabMid);

                if (GUILayout.Button(tooltips[i], style, GUILayout.Width(buttonWidth)) && !isSelected)
                    onTabSelected(i);

                if (isSelected && Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawRect(new Rect(r.x, r.yMax - accentH, r.width, accentH), AccentColor);
                }
            }

            GUI.backgroundColor = originalBg;

            if (showScanButton)
            {
                if (GUILayout.Button(new GUIContent("↺", "再スキャン"), _tabScan, GUILayout.Width(54f)))
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
                EditorGUILayout.BeginHorizontal();

                var preview = AssetPreview.GetAssetPreview(material);
                if (preview == null)
                {
                    Repaint();
                    preview = AssetPreview.GetMiniThumbnail(material);
                }
                if (preview != null)
                    GUILayout.Label(preview, GUILayout.Width(48f), GUILayout.Height(48f));

                EditorGUILayout.BeginVertical();

                EditorGUI.BeginChangeCheck();
                var newMaterial = (Material)EditorGUILayout.ObjectField(material, typeof(Material), false);
                if (EditorGUI.EndChangeCheck() && newMaterial != null && newMaterial != material)
                    ReplaceMaterial(material, newMaterial);

                if (_materialUsage.TryGetValue(material, out var usages))
                {
                    if (!_materialFoldouts.ContainsKey(material))
                        _materialFoldouts[material] = false;

                    _materialFoldouts[material] = EditorGUILayout.Foldout(
                        _materialFoldouts[material],
                        $"使用箇所  {usages.Count} 件",
                        true,
                        EditorStyles.foldout);

                    if (_materialFoldouts[material])
                    {
                        EditorGUI.indentLevel++;
                        foreach (var (renderer, _) in usages)
                        {
                            if (renderer == null) continue;
                            GUI.enabled = false;
                            EditorGUILayout.ObjectField(renderer, typeof(Renderer), true);
                            GUI.enabled = true;
                        }
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                DrawSeparator();
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawBlendShapeCompose()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

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
                EditorGUILayout.HelpBox("右の一覧から「ベース」を押して選択してください。", MessageType.Info);
            }
            else
            {
                var nameStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleLeft,
                };
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(32f)))
                {
                    EditorGUILayout.LabelField("✓", nameStyle, GUILayout.Width(18f));
                    EditorGUILayout.LabelField(new GUIContent(_baseShapeName, _baseShapeName), nameStyle);
                    if (GUILayout.Button("クリア", EditorStyles.miniButton, GUILayout.Width(BUTTON_WIDTH_MEDIUM), GUILayout.Height(32f)))
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

                var lineRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandHeight(true), GUILayout.Width(1f));
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(lineRect, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.60f, 0.60f, 0.60f));

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
                    
                    if (_composeShapes.Count == 0 && string.IsNullOrEmpty(_baseShapeName))
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
            bool hasBase = !string.IsNullOrEmpty(_baseShapeName);

            if (hasBase)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(BUTTON_WIDTH_SMALL + 4f);
                    EditorGUILayout.LabelField(new GUIContent(_baseShapeName, _baseShapeName), GUILayout.Width(120));
                    GUI.enabled = false;
                    EditorGUILayout.Slider(100f, -100f, 100f);
                    GUI.enabled = true;
                }
            }

            for (int i = 0; i < _composeShapes.Count; i++)
            {
                if (i > 0 || hasBase)
                    DrawPlusSeparator();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("×", GUILayout.Width(BUTTON_WIDTH_SMALL)))
                    {
                        _composeShapes.RemoveAt(i);
                        i--;
                        continue;
                    }

                    var item = _composeShapes[i];
                    EditorGUILayout.LabelField(new GUIContent(item.name, item.name), GUILayout.Width(120));

                    float weight = EditorGUILayout.Slider(item.weight, -100f, 100f);
                    if (!Mathf.Approximately(weight, item.weight))
                        _composeShapes[i] = (item.name, weight);
                }
            }
        }

        private static void DrawPlusSeparator()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("＋", GUILayout.Width(20f));
                GUILayout.FlexibleSpace();
            }
        }
        
        private void DrawShapeSelectionList()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawColoredBox(ContentColor, () => 
                {
                    EditorGUILayout.LabelField("シェイプキー一覧", EditorStyles.boldLabel);
                    
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _composeSearchText = EditorGUILayout.TextField("検索", _composeSearchText);
                        if (!string.IsNullOrEmpty(_composeSearchText) && GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20f)))
                            _composeSearchText = "";
                    }
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
            var prevBg = GUI.backgroundColor;
            if (isBase)
                GUI.backgroundColor = new Color(0.30f, 0.75f, 0.35f);
            if (GUILayout.Button(isBase ? "✓ ベース" : "ベース", GUILayout.Width(BUTTON_WIDTH_MEDIUM)) && !isBase)
            {
                _baseShapeName = shapeName;
                if (_overwriteShape)
                    _newShapeName = shapeName;
            }
            GUI.backgroundColor = prevBg;
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
                bool canCompose = CanExecuteCompose();
                if (!canCompose)
                {
                    string reason = _composeTarget?.sharedMesh == null ? "対象メッシュを選択してください。"
                        : string.IsNullOrEmpty(_baseShapeName)         ? "ベースシェイプキーを選択してください。"
                        :                                                 "出力名を入力してください。";
                    EditorGUILayout.HelpBox(reason, MessageType.Info);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全クリア", GUILayout.Height(EXECUTE_BUTTON_HEIGHT), GUILayout.Width(BUTTON_WIDTH_LARGE)))
                        ResetComposeData();

                    GUILayout.FlexibleSpace();

                    GUI.enabled = canCompose;
                    if (GUILayout.Button("合成実行", GUILayout.Height(EXECUTE_BUTTON_HEIGHT), GUILayout.Width(BUTTON_WIDTH_LARGE)))
                        ExecuteShapeCompose();
                    GUI.enabled = true;
                }
            });
        }
        
        private bool CanExecuteCompose()
        {
            return _composeTarget?.sharedMesh != null &&
                   !string.IsNullOrEmpty(_baseShapeName) &&
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
                mesh.ClearBlendShapes();
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

            int lastFrame = mesh.GetBlendShapeFrameCount(baseIndex) - 1;
            mesh.GetBlendShapeFrameVertices(baseIndex, lastFrame, deltaVertices, deltaNormals, deltaTangents);
            
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += deltaVertices[i];
                normals[i] += deltaNormals[i];
                tangents[i] += deltaTangents[i];
            }
        }
        
        private void ApplyComposeShapeDeltas(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            if (_composeShapes.Count == 0) return;

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

            int lastFrame = mesh.GetBlendShapeFrameCount(index) - 1;
            mesh.GetBlendShapeFrameVertices(index, lastFrame, deltaVertices, deltaNormals, deltaTangents);
            
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
        
        private static Mesh CreateMeshWithReplacedShape(Mesh mesh, Mesh originalMesh, string targetName, (Vector3[] vertices, Vector3[] normals, Vector3[] tangents) deltas)
        {
            mesh.ClearBlendShapes();
            for (int i = 0; i < originalMesh.blendShapeCount; i++)
            {
                string shapeName = originalMesh.GetBlendShapeName(i);
                if (shapeName == targetName)
                    mesh.AddBlendShapeFrame(shapeName, 100f, deltas.vertices, deltas.normals, deltas.tangents);
                else
                    CopyExistingBlendShape(originalMesh, mesh, i, shapeName);
            }
            return mesh;
        }
        
        private static void CopyExistingBlendShape(Mesh originalMesh, Mesh targetMesh, int shapeIndex, string shapeName)
        {
            int vertexCount = originalMesh.vertexCount;
            int frameCount = originalMesh.GetBlendShapeFrameCount(shapeIndex);
            for (int f = 0; f < frameCount; f++)
            {
                var deltaVertices = new Vector3[vertexCount];
                var deltaNormals = new Vector3[vertexCount];
                var deltaTangents = new Vector3[vertexCount];
                float frameWeight = originalMesh.GetBlendShapeFrameWeight(shapeIndex, f);
                originalMesh.GetBlendShapeFrameVertices(shapeIndex, f, deltaVertices, deltaNormals, deltaTangents);
                targetMesh.AddBlendShapeFrame(shapeName, frameWeight, deltaVertices, deltaNormals, deltaTangents);
            }
        }
        
        private string SaveMeshAsset(Mesh mesh, string shapeName)
        {
            string saveDirectory = "Assets/qsyi/GeneratedMeshes";
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
                AssetDatabase.Refresh();
            }

            string timestamp = System.DateTime.Now.ToString("yy_MMdd_HHmmss");
            string fileName = $"{timestamp}.asset";
            string filePath = Path.Combine(saveDirectory, fileName);

            int counter = 1;
            while (File.Exists(filePath))
            {
                fileName = $"{timestamp}_{counter++}.asset";
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
                DrawArmatureSettings();
                
                bool hasOutfitBones = _outfitBones.Count > 0;
                bool hasAvatarBones = _avatarBones.Count > 0;
                
                DrawColoredBox(HeaderColor, () => 
                {
                    bool isValidTarget = _targets.Count > 0 && _targets.All(t => t?.GetComponent<ModularAvatarMeshSettings>() != null);
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
                DrawSeparator();
                DrawAvatarScaleStatusList();
                DrawSeparator();
                DrawAutoScaleSyncControls(hasAvatarBones && hasOutfitBones);
            }
        }

        private void DrawBoneDetectionWarnings()
        {
            var missingAvatarBones = BONE_ORDER.Where(b => !_avatarBones.ContainsKey(b)).ToList();
            if (missingAvatarBones.Count > 0)
                EditorGUILayout.HelpBox($"素体: {missingAvatarBones.Count} 件未検出 — {string.Join(", ", missingAvatarBones)}", MessageType.Warning);

            foreach (var outfit in _targets.Where(t => t != null))
            {
                if (!_outfitBones.TryGetValue(outfit, out var boneMap))
                {
                    EditorGUILayout.HelpBox($"「{outfit.name}」: ボーンを検出できませんでした。", MessageType.Warning);
                    continue;
                }

                var missing = BONE_ORDER.Where(b => !boneMap.ContainsKey(b)).ToList();
                if (missing.Count > 0)
                    EditorGUILayout.HelpBox($"「{outfit.name}」: {missing.Count} 件未検出 — {string.Join(", ", missing)}", MessageType.Warning);
            }
        }

        private void DrawAvatarScaleStatusList()
        {
            DrawColoredBox(ContentColor, () =>
            {
                EditorGUILayout.LabelField("素体ボーンスケール", EditorStyles.boldLabel);
                _scaleStatusScroll = EditorGUILayout.BeginScrollView(_scaleStatusScroll, GUILayout.ExpandHeight(true));

                foreach (var boneName in BONE_ORDER.Where(b => _avatarBones.ContainsKey(b) && _avatarBones[b] != null))
                    DrawBoneEntry(boneName);

                var undetected = BONE_ORDER.Where(b => !_avatarBones.ContainsKey(b) || _avatarBones[b] == null).ToList();
                if (undetected.Count > 0)
                {
                    DrawSeparator(padding: 2f);
                    foreach (var boneName in undetected)
                        DrawBoneEntry(boneName);
                }

                EditorGUILayout.EndScrollView();
            }, GUILayout.ExpandHeight(true));
        }

        private void DrawBoneEntry(string boneName)
        {
            if (_avatarBones.TryGetValue(boneName, out var avatarBone) && avatarBone != null)
            {
                EditorGUILayout.LabelField(boneName, EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Transform", GUILayout.Width(80f));
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(avatarBone, typeof(Transform), true);
                    GUI.enabled = true;
                }

                EditorGUI.BeginChangeCheck();
                var newScale = EditorGUILayout.Vector3Field("Scale", avatarBone.localScale);
                if (EditorGUI.EndChangeCheck() && !Approximately(newScale, avatarBone.localScale))
                {
                    Undo.RecordObject(avatarBone, "Change Bone Transform Scale");
                    avatarBone.localScale = newScale;
                    EditorUtility.SetDirty(avatarBone);
                }

                var adjuster = avatarBone.GetComponent<ModularAvatarScaleAdjuster>();
                if (adjuster != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var newAdjScale = EditorGUILayout.Vector3Field("ScaleAdjuster", adjuster.Scale);
                    if (EditorGUI.EndChangeCheck() && !Approximately(newAdjScale, adjuster.Scale))
                        ApplyScaleAdjusterScale(adjuster, newAdjScale, true, "Change Bone ScaleAdjuster Scale");
                }

                DrawSeparator(padding: 2f);
            }
            else
            {
                if (_dimBoneStyle == null)
                    _dimBoneStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.45f, 0.45f, 0.45f) : new Color(0.6f, 0.6f, 0.6f) }
                    };
                EditorGUILayout.LabelField($"− {boneName}", _dimBoneStyle);
            }
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

            DrawSeparator();
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
                _autoSyncPosition = EditorGUILayout.ToggleLeft("Position も同期する（実験的機能）", _autoSyncPosition);
                _autoSyncRotation = EditorGUILayout.ToggleLeft("Rotation も同期する（実験的機能）", _autoSyncRotation);

                if (!canSync)
                    EditorGUILayout.HelpBox("素体と衣装の両方にボーンが必要です。", MessageType.Info);

                GUI.enabled = canSync && !EditorApplication.isPlaying;
                if (GUILayout.Button("同期", GUILayout.Height(EXECUTE_BUTTON_HEIGHT)))
                {
                    ScanBones();
                    ApplyAvatarScalesToOutfits();
                }
                GUI.enabled = true;
            });
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

            Undo.SetCurrentGroupName("Sync Bones");
            int undoGroup = Undo.GetCurrentGroup();
            bool hasAnyChange = false;

            try
            {
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

                        if (_autoSyncPosition)
                        {
                            if (!Approximately(outfitBone.localPosition, avatarLocalPosition))
                            {
                                Undo.RecordObject(outfitBone, "Auto Sync Transform Position");
                                outfitBone.localPosition = avatarLocalPosition;
                                EditorUtility.SetDirty(outfitBone);
                                hasAnyChange = true;
                            }
                        }

                        if (_autoSyncRotation)
                        {
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
            catch (System.Exception e)
            {
                Debug.LogError($"[qsToolBox] Sync error: {e}");
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
                case Mode.MenuGenerator: ScanMenuMeshEntries(); break;
            }
        }
        
        private void DrawMenuGenerator()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                DrawColoredBox(HeaderColor, () =>
                {
                    EditorGUILayout.LabelField("簡易メニュー生成", EditorStyles.boldLabel);
                    _menuFolderName = EditorGUILayout.TextField(new GUIContent("フォルダ名", "生成先フォルダ名"), _menuFolderName);
                    _menuPreviewEnabled = EditorGUILayout.ToggleLeft("プレビュー", _menuPreviewEnabled);
                    if (string.IsNullOrWhiteSpace(_menuFolderName))
                        EditorGUILayout.HelpBox("フォルダ名を入力してください。", MessageType.Warning);
                });

                DrawColoredBox(ContentColor, () =>
                {
                    EditorGUILayout.LabelField("生成対象", EditorStyles.boldLabel);
                    _menuRendererScroll = EditorGUILayout.BeginScrollView(_menuRendererScroll, GUILayout.ExpandHeight(true));
                    DrawMenuMeshEntries();
                    EditorGUILayout.EndScrollView();
                });

                DrawMenuGenerateExecuteButton();
            }

            SyncMenuPreviewState();
        }

        private void DrawMenuGenerateExecuteButton()
        {
            DrawColoredBox(SelectColor, () =>
            {
                bool canGenerate = CanGenerateMenu();
                if (!canGenerate)
                    EditorGUILayout.HelpBox(GetMenuGenerateWarningMessage(), MessageType.Info);

                GUI.enabled = canGenerate;
                if (GUILayout.Button("生成", GUILayout.Height(EXECUTE_BUTTON_HEIGHT)))
                    GenerateMenu();
                GUI.enabled = true;
            });
        }

        private void GenerateMenu()
        {
            var parentTarget = FindMenuGenerationRoot();
            var entriesToGenerate = GetGeneratableMenuMeshEntries();
            if (parentTarget == null || entriesToGenerate.Count == 0)
                return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Menu");

            try
            {
                string folderName = GetGeneratedMenuFolderName();
                var folderObject = new GameObject(folderName);
                Undo.RegisterCreatedObjectUndo(folderObject, "Generate Menu");
                folderObject.transform.SetParent(parentTarget.transform, false);

                var generatedItemNames = new List<string>();

                foreach (var entry in entriesToGenerate)
                {
                    string itemName = entry.Renderer.name;
                    CreateToggleObject(
                        folderObject.transform,
                        itemName,
                        new[] { entry.Renderer != null ? entry.Renderer.gameObject : null },
                        new[] { false });
                    generatedItemNames.Add(itemName);
                }

                var menuInstaller = Undo.AddComponent<ModularAvatarMenuInstaller>(folderObject);
                ConfigureMenuInstaller(menuInstaller);
                var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(folderObject);
                ConfigureFolderMenuItem(menuItem);
                ForceRefreshGeneratedMenu(folderObject, menuItem);

                EditorUtility.SetDirty(folderObject);
                ScanData();
                EditorUtility.DisplayDialog(
                    "簡易メニュー生成",
                    BuildMenuGeneratedDialogMessage(folderName, generatedItemNames),
                    "OK");
                Debug.Log($"[qsToolBox] Generated menu '{folderObject.name}' with {generatedItemNames.Count} item(s).");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
              }
          }

        private GameObject FindMenuGenerationRoot()
        {
            foreach (var target in _targets.Where(IsValidTarget))
            {
                var current = target.transform;
                while (current != null)
                {
                    if (current.GetComponent<VRCAvatarDescriptor>() != null)
                        return current.gameObject;

                    current = current.parent;
                }
            }

            return _targets.FirstOrDefault(IsValidTarget);
        }
  
          private string GetGeneratedMenuFolderName()
          {
            string baseName = string.IsNullOrWhiteSpace(_menuFolderName)
                ? _targets.FirstOrDefault(IsValidTarget)?.name ?? "Menu"
                : _menuFolderName.Trim();

            return baseName.StartsWith("Menu_") ? baseName : $"Menu_{baseName}";
        }

        private static void ApplyMenuItemDefaults(ModularAvatarMenuItem menuItem, PortableControlType type)
        {
            menuItem.PortableControl.Type = type;
            menuItem.PortableControl.Value = 1f;
            menuItem.PortableControl.Parameter = string.Empty;
            menuItem.PortableControl.Icon = null;
            menuItem.MenuSource = SubmenuSource.Children;
            menuItem.isSynced = true;
            menuItem.isSaved = true;
            menuItem.isDefault = false;
            menuItem.automaticValue = true;
            menuItem.label = string.Empty;
        }

        private void ConfigureFolderMenuItem(ModularAvatarMenuItem menuItem)
        {
            ApplyMenuItemDefaults(menuItem, PortableControlType.SubMenu);
            PrefabUtility.RecordPrefabInstancePropertyModifications(menuItem);
            PrefabUtility.RecordPrefabInstancePropertyModifications(menuItem.gameObject);
            EditorUtility.SetDirty(menuItem);
            EditorUtility.SetDirty(menuItem.gameObject);
        }

        private void ConfigureMenuInstaller(ModularAvatarMenuInstaller menuInstaller)
        {
            menuInstaller.menuToAppend = null;
            menuInstaller.installTargetMenu = null;

            PrefabUtility.RecordPrefabInstancePropertyModifications(menuInstaller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(menuInstaller.gameObject);
            EditorUtility.SetDirty(menuInstaller);
            EditorUtility.SetDirty(menuInstaller.gameObject);
        }

        private void CreateToggleObject(Transform parent, string objectName, IEnumerable<GameObject> targetObjects, IEnumerable<bool> activeWhenOnValues)
        {
            var toggleObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(toggleObject, "Generate Menu");
            toggleObject.transform.SetParent(parent, false);

            var childMenuItem = Undo.AddComponent<ModularAvatarMenuItem>(toggleObject);
            ConfigureChildMenuItem(childMenuItem);

            var itemToggler = Undo.AddComponent<ItemToggler>(toggleObject);
            ConfigureItemToggler(
                itemToggler,
                childMenuItem,
                objectName,
                targetObjects.Zip(activeWhenOnValues, (target, isOn) => (target, isOn))
                    .Where(pair => pair.target != null)
                    .Distinct()
                    .ToList());
            PrefabUtility.RecordPrefabInstancePropertyModifications(childMenuItem);
            PrefabUtility.RecordPrefabInstancePropertyModifications(childMenuItem.gameObject);
            EditorUtility.SetDirty(childMenuItem);
            EditorUtility.SetDirty(childMenuItem.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(toggleObject);
            EditorUtility.SetDirty(toggleObject);
        }

        private static void ConfigureChildMenuItem(ModularAvatarMenuItem menuItem)
        {
            ApplyMenuItemDefaults(menuItem, PortableControlType.Toggle);
        }

        private void ConfigureItemToggler(
            ItemToggler itemToggler,
            ModularAvatarMenuItem parentMenuItem,
            string menuName,
            IReadOnlyList<(GameObject target, bool activeWhenOn)> targetObjects)
        {
            var serializedObject = new SerializedObject(itemToggler);
            serializedObject.Update();

            serializedObject.FindProperty("menuName").stringValue = menuName;
            serializedObject.FindProperty("parentOverride").objectReferenceValue = null;
            serializedObject.FindProperty("icon").objectReferenceValue = null;
            serializedObject.FindProperty("parentOverrideMA").objectReferenceValue = parentMenuItem;
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
                objectElement.FindPropertyRelative("obj").objectReferenceValue = targetObjects[i].target;
                objectElement.FindPropertyRelative("value").boolValue = targetObjects[i].activeWhenOn;
            }

            parameterProperty.FindPropertyRelative("blendShapeModifiers").arraySize = 0;
            parameterProperty.FindPropertyRelative("materialReplacers").arraySize = 0;
            parameterProperty.FindPropertyRelative("materialPropertyModifiers").arraySize = 0;
            parameterProperty.FindPropertyRelative("clips").arraySize = 0;

            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(itemToggler);
            PrefabUtility.RecordPrefabInstancePropertyModifications(itemToggler.gameObject);
            EditorUtility.SetDirty(itemToggler);
            EditorUtility.SetDirty(itemToggler.gameObject);
        }

        private void ForceRefreshGeneratedMenu(GameObject folderObject, ModularAvatarMenuItem menuItem)
        {
            if (folderObject == null)
                return;

            var childTogglers = folderObject.GetComponentsInChildren<ItemToggler>(true);
            var childMenuItems = folderObject.GetComponentsInChildren<ModularAvatarMenuItem>(true);
            MarkGeneratedMenuDirty(folderObject, menuItem, childMenuItems, childTogglers);

            EditorApplication.delayCall += () =>
            {
                if (folderObject == null)
                    return;

                var delayedMenuItem = menuItem != null
                    ? menuItem
                    : folderObject.GetComponent<ModularAvatarMenuItem>();
                var delayedChildMenuItems = folderObject.GetComponentsInChildren<ModularAvatarMenuItem>(true);
                var delayedChildTogglers = folderObject.GetComponentsInChildren<ItemToggler>(true);
                MarkGeneratedMenuDirty(folderObject, delayedMenuItem, delayedChildMenuItems, delayedChildTogglers);
            };
        }

        private void MarkGeneratedMenuDirty(
            GameObject folderObject,
            ModularAvatarMenuItem menuItem,
            IEnumerable<ModularAvatarMenuItem> childMenuItems,
            IEnumerable<ItemToggler> childTogglers)
        {
            if (folderObject == null)
                return;

            if (menuItem != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(menuItem);
                EditorUtility.SetDirty(menuItem);
            }

            foreach (var childMenuItem in childMenuItems.Where(child => child != null))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(childMenuItem);
                PrefabUtility.RecordPrefabInstancePropertyModifications(childMenuItem.gameObject);
                EditorUtility.SetDirty(childMenuItem);
                EditorUtility.SetDirty(childMenuItem.gameObject);
            }

            foreach (var toggler in childTogglers.Where(toggler => toggler != null))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(toggler);
                PrefabUtility.RecordPrefabInstancePropertyModifications(toggler.gameObject);
                EditorUtility.SetDirty(toggler);
                EditorUtility.SetDirty(toggler.gameObject);
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(folderObject);
            EditorUtility.SetDirty(folderObject);
            EditorSceneManager.MarkSceneDirty(folderObject.scene);
        }

        private string BuildMenuGeneratedDialogMessage(string folderName, IReadOnlyList<string> generatedItemNames)
        {
            var lines = new List<string> { folderName };
            lines.AddRange(generatedItemNames.Select(name => $"・{name}"));
            lines.Add(string.Empty);
            lines.Add("メニューを生成しました");
            return string.Join("\n", lines);
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

            foreach (var key in _materialFoldouts.Keys.Where(k => !_materials.Contains(k)).ToList())
                _materialFoldouts.Remove(key);
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
        
        private static void DrawColoredBox(Color _, System.Action content, params GUILayoutOption[] options)
        {
            EditorGUILayout.BeginVertical(options);
            content();
            EditorGUILayout.EndVertical();
        }

        private static void DrawSeparator(float thickness = 1f, float padding = 3f)
        {
            var rect = GUILayoutUtility.GetRect(0f, padding * 2f + thickness, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var line = new Rect(rect.x, rect.y + padding, rect.width, thickness);
                EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.60f, 0.60f, 0.60f));
            }
        }

        private void ScanMenuMeshEntries()
        {
            EnsureDefaultMenuFolderName();

            var previousSettings = _menuMeshEntries
                .Where(entry => entry != null && IsValidMenuRenderer(entry.Renderer))
                .GroupBy(entry => entry.Renderer)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Include);

            var scannedRenderers = _targets
                .Where(IsValidTarget)
                .SelectMany(target => target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                .Where(IsValidMenuRenderer)
                .Distinct()
                .ToList();

            var existingEntries = _menuMeshEntries
                .Where(entry => entry != null)
                .ToList();

            existingEntries.RemoveAll(e => e.Renderer == null || !scannedRenderers.Contains(e.Renderer));

            foreach (var renderer in scannedRenderers)
            {
                bool alreadyExists = existingEntries.Any(entry => entry.Renderer == renderer);
                if (alreadyExists)
                    continue;

                bool include = previousSettings.TryGetValue(renderer, out var settings)
                    ? settings
                    : false;

                existingEntries.Add(new MenuMeshEntry
                {
                    Renderer = renderer,
                    Include = include
                });
            }

            _menuMeshEntries.Clear();
            foreach (var e in existingEntries)
                _menuMeshEntries.Add(e);
        }

        private void EnsureDefaultMenuFolderName()
        {
            if (!string.IsNullOrWhiteSpace(_menuFolderName))
                return;

            var defaultTarget = _targets.FirstOrDefault(IsValidTarget);
            if (defaultTarget != null)
                _menuFolderName = defaultTarget.name;
        }

        private void DrawMenuMeshEntries()
        {
            if (_menuMeshEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("探索対象配下にメッシュが見つかりません。", MessageType.Info);
                return;
            }

            _serializedObject.Update();
            _menuMeshEntriesList.DoLayoutList();
            _serializedObject.ApplyModifiedProperties();
        }

        private void SyncMenuPreviewState()
        {
            if (_mode != Mode.MenuGenerator || !_menuPreviewEnabled)
            {
                RestoreMenuPreviewState();
                return;
            }

            var previewTargets = new HashSet<GameObject>(
                _menuMeshEntries
                    .Where(entry => entry != null && entry.Include && IsValidMenuRenderer(entry.Renderer))
                    .Select(entry => entry.Renderer.gameObject)
                    .Where(target => target != null));

            var toRestore = _menuPreviewOriginalStates.Keys
                .Where(target => target == null || !previewTargets.Contains(target))
                .ToList();

            foreach (var target in toRestore)
            {
                if (target != null && _menuPreviewOriginalStates.TryGetValue(target, out var originalState))
                    target.SetActive(originalState);

                _menuPreviewOriginalStates.Remove(target);
            }

            foreach (var target in previewTargets)
            {
                if (!_menuPreviewOriginalStates.ContainsKey(target))
                    _menuPreviewOriginalStates[target] = target.activeSelf;

                if (target.activeSelf)
                    target.SetActive(false);
            }
        }

        private void RestoreMenuPreviewState()
        {
            foreach (var pair in _menuPreviewOriginalStates.ToList())
            {
                if (pair.Key != null)
                    pair.Key.SetActive(pair.Value);
            }

            _menuPreviewOriginalStates.Clear();
        }

        private bool CanGenerateMenu()
        {
            return !string.IsNullOrWhiteSpace(_menuFolderName) &&
                GetGeneratableMenuMeshEntries().Count > 0;
        }

        private List<MenuMeshEntry> GetGeneratableMenuMeshEntries()
        {
            return _menuMeshEntries
                .Where(entry => entry != null && entry.Include && IsValidMenuRenderer(entry.Renderer))
                .GroupBy(entry => entry.Renderer)
                .Select(group => group.First())
                .ToList();
        }

        private bool IsValidMenuRenderer(SkinnedMeshRenderer renderer)
        {
            return renderer != null &&
                renderer.sharedMesh != null &&
                IsValidTarget(renderer.gameObject);
        }

        private string GetMenuGenerateWarningMessage()
        {
            if (string.IsNullOrWhiteSpace(_menuFolderName))
                return "フォルダ名を入力してください。";

            return "メニューを生成するには、含めるメッシュを 1 つ以上チェックしてください。";
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
            Transform bestMatch = null;
            int bestExtra = int.MaxValue;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                string normalizedName = NormalizeBoneToken(child.name);

                if (normalizedName == normalizedKeyword)
                    return child;

                if (normalizedName.Contains(normalizedKeyword))
                {
                    int extra = normalizedName.Length - normalizedKeyword.Length;
                    if (extra < bestExtra)
                    {
                        bestExtra = extra;
                        bestMatch = child;
                    }
                }
            }
            return bestMatch;
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
