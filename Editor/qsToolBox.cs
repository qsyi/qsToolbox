#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;
using System.IO;
using System.Linq;

namespace qsyi
{
    internal class QsToolBox : EditorWindow
    {
        private enum Mode { Material, BlendShape, Scale }
        private enum BlendShapeMode { Search, Compose }
        private enum MaterialMode { Replace, Copy }
        
        // Core Fields
        [SerializeField] private List<GameObject> _targets = new List<GameObject>();
        [SerializeField] private Transform _avatarArmature;
        
        private Mode _mode = Mode.Material;
        private BlendShapeMode _blendShapeMode = BlendShapeMode.Search;
        private MaterialMode _materialMode = MaterialMode.Replace;
        private Vector2 _scrollPosition;
        private Vector2 _materialListScroll;
        private Vector2 _propertyScroll;
        private Vector2 _composeShapeScroll;
        private Vector2 _shapeListScroll;
        private Vector2 _destinationMaterialScroll;
        private string _searchText = "";
        
        // UI State
        private readonly Dictionary<Object, bool> _foldoutStates = new Dictionary<Object, bool>();
        private readonly Dictionary<string, bool> _boneFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _childGroupFoldouts = new Dictionary<string, bool>();
        
        // Data Cache
        private readonly List<SkinnedMeshRenderer> _skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly Dictionary<Material, List<(Renderer renderer, int slot)>> _materialUsage = new Dictionary<Material, List<(Renderer, int)>>();
        private readonly Dictionary<GameObject, Dictionary<string, Transform>> _outfitBones = new Dictionary<GameObject, Dictionary<string, Transform>>();
        private readonly Dictionary<string, Transform> _avatarBones = new Dictionary<string, Transform>();
        
        // Material Copy Fields
        private Material _sourceMaterial;
        private readonly List<Material> _destinationMaterials = new List<Material>();
        private readonly Dictionary<string, bool> _propertySelections = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _groupFoldouts = new Dictionary<string, bool>();
        private readonly List<PropertyGroup> _availableGroups = new List<PropertyGroup>();
        
        // Compose Mode
        private SkinnedMeshRenderer _composeTarget;
        private string _baseShapeName = "";
        private readonly List<(string name, float weight)> _composeShapes = new List<(string, float)>();
        private string _composeSearchText = "";
        private readonly List<string> _shapeNames = new List<string>();
        private string _newShapeName = "";
        private bool _overwriteShape = true;
        
        // Cache Control
        private SerializedObject _serializedObject;
        private SerializedProperty _targetsProperty;
        private SerializedProperty _armatureProperty;
        private int _targetHash = -1;
        private bool _isDirty = true;
        
        // Constants
        private const float BUTTON_WIDTH_SMALL = 20f;
        private const float BUTTON_WIDTH_MEDIUM = 60f;
        private const float BUTTON_WIDTH_LARGE = 80f;
        private const float SCROLL_HEIGHT = 300f;
        private const float SCROLL_HEIGHT_SMALL = 120f;
        private const float EXECUTE_BUTTON_HEIGHT = 30f;
        private const int LABEL_WIDTH_SMALL = 15;
        private const int LABEL_WIDTH_MEDIUM = 26;
        private const int LABEL_WIDTH_LARGE = 30;
        private const float VIEW_WIDTH_RATIO = 0.5f;
        private const int MAX_DESTINATION_ROWS_BEFORE_SCROLL = 4;
        
        // UI Colors
        private static readonly Color HeaderColor = new Color(0.6f, 0.8f, 1f, 0.8f);
        private static readonly Color ContentColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        private static readonly Color SelectColor = new Color(0.5f, 0.7f, 1f, 0.8f);
        private static readonly Color TargetColor = new Color(0.9f, 0.9f, 0.6f, 0.8f);
        private static readonly Color SubTabColor = new Color(0.8f, 0.9f, 1f, 0.7f);
        private static readonly Color BaseColor = new Color(0.8f, 1f, 0.8f, 0.8f);
        private static readonly Color CopyColor = new Color(1f, 0.9f, 0.8f, 0.8f);
        private static readonly Color GroupColor = new Color(0.9f, 0.9f, 1f, 0.7f);
        
        private static readonly string[] TAB_NAMES = { "マテリアル", "ブレンドシェイプ", "スケール" };
        private static readonly GUIContent[] TAB_TOOLTIPS = {
            new GUIContent("マテリアル", "探索対象のマテリアルを置換・コピーできます"),
            new GUIContent("ブレンドシェイプ", "探索対象のブレンドシェイプを表示・編集します"),
            new GUIContent("スケール", "ModularAvatarのスケール調整機能を使用します")
        };
        
        private static readonly string[] BLEND_SHAPE_TAB_NAMES = { "シェイプキー検索", "シェイプキー合成" };
        private static readonly GUIContent[] BLEND_SHAPE_TAB_TOOLTIPS = {
            new GUIContent("シェイプキー検索", "ブレンドシェイプを検索・編集します"),
            new GUIContent("シェイプキー合成", "ベースシェイプキーに他のシェイプキーを合成します")
        };
        
        private static readonly string[] MATERIAL_TAB_NAMES = { "マテリアル置換", "マテリアルコピー" };
        private static readonly GUIContent[] MATERIAL_TAB_TOOLTIPS = {
            new GUIContent("マテリアル置換", "マテリアルを直接置換します"),
            new GUIContent("マテリアルコピー", "マテリアルプロパティを選択的にコピーします")
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
        
        public class PropertyGroup
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public List<PropertyItem> Properties { get; set; } = new List<PropertyItem>();
            public bool HasParent { get; set; }
            public string ParentId { get; set; }
            
            public PropertyGroup() { }
            
            public PropertyGroup(string name, string displayName, bool hasParent = false, string parentId = null)
            {
                Name = name;
                DisplayName = displayName;
                HasParent = hasParent;
                ParentId = parentId;
            }
        }
        
        public class PropertyItem
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            
            public PropertyItem(string name, string displayName)
            {
                Name = name;
                DisplayName = displayName;
            }
        }
        
        private static readonly List<PropertyGroup> PROPERTY_GROUPS = new List<PropertyGroup>
        {
            new PropertyGroup("CustomSafetyFallback", "Custom Safety Fallback")
            {
                Properties = new List<PropertyItem> { new PropertyItem("_CustomSafetyFallback", "Custom Safety Fallback") }
            },
            
            new PropertyGroup("Shadow", "影設定"),
            new PropertyGroup("ShadowValues", "影数値", true, "Shadow")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseShadow", "影使用"), 
                    new PropertyItem("_ShadowStrength", "影強度"),
                    new PropertyItem("_ShadowBorder", "影境界"),
                    new PropertyItem("_ShadowBlur", "影ぼかし"), 
                    new PropertyItem("_ShadowNormalStrength", "法線強度"),
                    new PropertyItem("_ShadowReceive", "影受け取り"),
                    new PropertyItem("_Shadow2ndBorder", "2nd影境界"), 
                    new PropertyItem("_Shadow2ndBlur", "2nd影ぼかし"),
                    new PropertyItem("_Shadow2ndNormalStrength", "2nd法線強度"),
                    new PropertyItem("_Shadow2ndReceive", "2nd影受け取り"),
                    new PropertyItem("_Shadow3rdBorder", "3rd影境界"), 
                    new PropertyItem("_Shadow3rdBlur", "3rd影ぼかし"),
                    new PropertyItem("_Shadow3rdNormalStrength", "3rd法線強度"),
                    new PropertyItem("_Shadow3rdReceive", "3rd影受け取り"),
                    new PropertyItem("_ShadowMainStrength", "影強度（メイン）"), 
                    new PropertyItem("_ShadowEnvStrength", "影強度（環境）"),
                    new PropertyItem("_ShadowBorderBlur", "影境界ぼかし"), 
                    new PropertyItem("_Shadow2ndMainStrength", "2nd影強度（メイン）"),
                    new PropertyItem("_Shadow2ndEnvStrength", "2nd影強度（環境）"), 
                    new PropertyItem("_Shadow3rdMainStrength", "3rd影強度（メイン）"),
                    new PropertyItem("_Shadow3rdEnvStrength", "3rd影強度（環境）"),
                    new PropertyItem("_ShadowBorderRange", "影境界範囲"),
                    new PropertyItem("_ShadowColorType", "影色タイプ"),
                    new PropertyItem("_ShadowPostAO", "AO後適用"),
                    new PropertyItem("_ShadowMaskType", "マスクタイプ"),
                    new PropertyItem("_ShadowFlatBorder", "フラット境界"),
                    new PropertyItem("_ShadowFlatBlur", "フラットぼかし")
                }
            },
            new PropertyGroup("ShadowColors", "影色", true, "Shadow")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_ShadowColor", "影色"), 
                    new PropertyItem("_Shadow2ndColor", "2nd影色"),
                    new PropertyItem("_Shadow3rdColor", "3rd影色"),
                    new PropertyItem("_ShadowBorderColor", "影境界色")
                }
            },
            
            new PropertyGroup("RimShade", "RimShade"),
            new PropertyGroup("RimShadeValues", "RimShade数値", true, "RimShade")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseRimShade", "RimShade使用"), 
                    new PropertyItem("_RimShadeBorder", "RimShade境界"),
                    new PropertyItem("_RimShadeBlur", "RimShadeぼかし"), 
                    new PropertyItem("_RimShadeFresnelPower", "RimShadeフレネル"),
                    new PropertyItem("_RimShadeNormalStrength", "RimShadeノーマル強度")
                }
            },
            new PropertyGroup("RimShadeColors", "RimShade色", true, "RimShade")
            {
                Properties = new List<PropertyItem> { new PropertyItem("_RimShadeColor", "RimShade色") }
            },
            
            new PropertyGroup("BackLight", "逆光ライト"),
            new PropertyGroup("BackLightValues", "逆光ライト数値", true, "BackLight")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseBacklight", "逆光ライト使用"), 
                    new PropertyItem("_BacklightPower", "逆光ライト強度"),
                    new PropertyItem("_BacklightMainStrength", "メイン色強度"),
                    new PropertyItem("_BacklightBorder", "逆光ライト境界"), 
                    new PropertyItem("_BacklightBlur", "逆光ライトぼかし"),
                    new PropertyItem("_BacklightDirectivity", "逆光ライト指向性"), 
                    new PropertyItem("_BacklightViewStrength", "逆光ライトビュー強度"),
                    new PropertyItem("_BacklightNormalStrength", "逆光ライトノーマル強度"),
                    new PropertyItem("_BacklightReceiveShadow", "影受け取り"),
                    new PropertyItem("_BacklightBackfaceMask", "裏面マスク")
                }
            },
            new PropertyGroup("BackLightColors", "逆光ライト色", true, "BackLight")
            {
                Properties = new List<PropertyItem> { new PropertyItem("_BacklightColor", "逆光ライト色") }
            },
            
            new PropertyGroup("Rim", "リムライト"),
            new PropertyGroup("RimValues", "リムライト数値", true, "Rim")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseRim", "リムライト使用"), 
                    new PropertyItem("_RimBorder", "リム境界"),
                    new PropertyItem("_RimBlur", "リムぼかし"), 
                    new PropertyItem("_RimFresnelPower", "リムフレネル"),
                    new PropertyItem("_RimPower", "リム強度"), 
                    new PropertyItem("_RimMainStrength", "メイン色強度"),
                    new PropertyItem("_RimNormalStrength", "リムノーマル強度"),
                    new PropertyItem("_RimEnableLighting", "リムライティング"),
                    new PropertyItem("_RimShadowMask", "リム影マスク"), 
                    new PropertyItem("_RimBackfaceMask", "リム裏面マスク"),
                    new PropertyItem("_RimApplyTransparency", "リム透明度適用"),
                    new PropertyItem("_RimVRParallaxStrength", "VR視差強度"),
                    new PropertyItem("_RimDirStrength", "リム方向強度"), 
                    new PropertyItem("_RimDirRange", "リム方向範囲"),
                    new PropertyItem("_RimIndirRange", "リム間接範囲"), 
                    new PropertyItem("_RimIndirBorder", "リム間接境界"), 
                    new PropertyItem("_RimIndirBlur", "リム間接ぼかし")
                }
            },
            new PropertyGroup("RimColors", "リムライト色", true, "Rim")
            {
                Properties = new List<PropertyItem> 
                { 
                    new PropertyItem("_RimColor", "リムライト色"),
                    new PropertyItem("_RimIndirColor", "リム間接色")
                }
            },
            
            new PropertyGroup("Outline", "輪郭線設定"),
            new PropertyGroup("OutlineValues", "輪郭線数値", true, "Outline")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_OutlineWidth", "アウトライン幅"), 
                    new PropertyItem("_OutlineFixWidth", "固定幅"),
                    new PropertyItem("_OutlineVertexR2Width", "頂点R2幅"), 
                    new PropertyItem("_OutlineEnableLighting", "アウトラインライティング"), 
                    new PropertyItem("_OutlineZBias", "アウトラインZバイアス"), 
                    new PropertyItem("_OutlineLitScale", "Litスケール"),
                    new PropertyItem("_OutlineLitOffset", "Litオフセット"),
                    new PropertyItem("_OutlineLitApplyTex", "Litテクスチャ適用"),
                    new PropertyItem("_OutlineLitShadowReceive", "Lit影受け取り"),
                    new PropertyItem("_OutlineDeleteMesh", "メッシュ削除"),
                    new PropertyItem("_OutlineVectorScale", "ベクタースケール"),
                    new PropertyItem("_OutlineVectorUVMode", "ベクターUVモード"),
                    new PropertyItem("_OutlineDisableInVR", "VRで無効化"),
                    new PropertyItem("_OutlineTexHSVG", "テクスチャHSVG設定")
                }
            },
            new PropertyGroup("OutlineColors", "輪郭線色", true, "Outline")
            {
                Properties = new List<PropertyItem> 
                { 
                    new PropertyItem("_OutlineColor", "アウトライン色"),
                    new PropertyItem("_OutlineLitColor", "アウトラインLit色")
                }
            },
            
            new PropertyGroup("DistanceFade", "距離フェード"),
            new PropertyGroup("DistanceFadeValues", "距離フェード数値", true, "DistanceFade")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_DistanceFade", "フェード設定（開始・終了距離含む）"), 
                    new PropertyItem("_DistanceFadeMode", "フェードモード"),
                    new PropertyItem("_DistanceFadeRimFresnelPower", "フェードリムフレネル")
                }
            },
            new PropertyGroup("DistanceFadeColors", "距離フェード色", true, "DistanceFade")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_DistanceFadeColor", "フェード色"), 
                    new PropertyItem("_DistanceFadeRimColor", "フェードリム色")
                }
            }
        };
        
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
            ScanData();
        }
        
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnUndo;
        }
        
        private void InitializeSerializedObject()
        {
            _serializedObject = new SerializedObject(this);
            _targetsProperty = _serializedObject.FindProperty("_targets");
            _armatureProperty = _serializedObject.FindProperty("_avatarArmature");
            _targetsProperty.isExpanded = true;
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
            
            if (_mode == Mode.BlendShape) DrawBlendShapeTabs();
            else if (_mode == Mode.Material) DrawMaterialTabs();
            
            EditorGUILayout.Space();
            
            switch (_mode)
            {
                case Mode.Material: 
                    if (_materialMode == MaterialMode.Replace) DrawMaterialReplace();
                    else DrawMaterialCopy();
                    break;
                case Mode.BlendShape: 
                    if (_blendShapeMode == BlendShapeMode.Search) DrawBlendShapeSearch();
                    else DrawBlendShapeCompose();
                    break;
                case Mode.Scale: DrawScaleAdjustment(); break;
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
                _mode = (Mode)index;
                GUI.FocusControl(null);
                ScanData();
                _scrollPosition = Vector2.zero;
            }, true);
        }
        
        private void DrawMaterialTabs()
        {
            EditorGUILayout.Space(2);
            DrawColoredBox(SubTabColor, () => 
            {
                DrawTabButtons(MATERIAL_TAB_NAMES, MATERIAL_TAB_TOOLTIPS, (int)_materialMode, (index) => 
                {
                    _materialMode = (MaterialMode)index;
                    GUI.FocusControl(null);
                    ResetScrollPositions();
                    if (_materialMode == MaterialMode.Copy) UpdateAvailableProperties();
                }, false);
            });
        }
        
        private void DrawBlendShapeTabs()
        {
            EditorGUILayout.Space(2);
            DrawColoredBox(SubTabColor, () => 
            {
                DrawTabButtons(BLEND_SHAPE_TAB_NAMES, BLEND_SHAPE_TAB_TOOLTIPS, (int)_blendShapeMode, (index) => 
                {
                    _blendShapeMode = (BlendShapeMode)index;
                    GUI.FocusControl(null);
                    _scrollPosition = Vector2.zero;
                    if (_blendShapeMode == BlendShapeMode.Compose) ScanForCompose();
                }, false);
            });
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
        
        private void ResetScrollPositions()
        {
            _scrollPosition = Vector2.zero;
            _materialListScroll = Vector2.zero;
            _propertyScroll = Vector2.zero;
            _destinationMaterialScroll = Vector2.zero;
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
        
        private void DrawMaterialCopy()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                float upperContentHeight = position.height - 230f;
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(upperContentHeight));
                
                DrawSourceMaterialSelection();
                DrawDestinationMaterialSelection();
                DrawPropertyAndMaterialSelection();
                
                EditorGUILayout.EndScrollView();
                
                DrawMaterialCopyExecuteButton();
            }
        }
        
        private void DrawSourceMaterialSelection()
        {
            DrawColoredBox(BaseColor, () => 
            {
                EditorGUILayout.LabelField("コピー元マテリアル", EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                _sourceMaterial = (Material)EditorGUILayout.ObjectField("コピー元", _sourceMaterial, typeof(Material), false);
                
                if (EditorGUI.EndChangeCheck()) UpdateAvailableProperties();
            });
        }
        
        private void DrawDestinationMaterialSelection()
        {
            DrawColoredBox(CopyColor, () => 
            {
                EditorGUILayout.LabelField("コピー先マテリアル", EditorStyles.boldLabel);
                
                DrawDestinationMaterialList();
            });
        }
        
        private void DrawDestinationMaterialList()
        {
            if (_destinationMaterials.Count == 0)
            {
                EditorGUILayout.LabelField("下のマテリアル一覧から「追加」ボタンで選択するか、ここにドラッグ＆ドロップ");
            }
            
            // スクロール領域の高さを決定
            float scrollHeight = _destinationMaterials.Count > MAX_DESTINATION_ROWS_BEFORE_SCROLL ? SCROLL_HEIGHT_SMALL : -1;
            
            if (scrollHeight > 0)
            {
                _destinationMaterialScroll = EditorGUILayout.BeginScrollView(_destinationMaterialScroll, GUILayout.Height(scrollHeight));
            }
            
            // 既存のマテリアルを表示
            for (int i = _destinationMaterials.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("×", GUILayout.Width(BUTTON_WIDTH_SMALL)))
                    {
                        _destinationMaterials.RemoveAt(i);
                        continue;
                    }
                    
                    EditorGUI.BeginChangeCheck();
                    var newMaterial = (Material)EditorGUILayout.ObjectField(_destinationMaterials[i], typeof(Material), false);
                    
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newMaterial == null)
                        {
                            _destinationMaterials.RemoveAt(i);
                        }
                        else if (newMaterial != _destinationMaterials[i])
                        {
                            // 既存のマテリアルと重複チェック
                            if (!_destinationMaterials.Contains(newMaterial) && newMaterial != _sourceMaterial)
                            {
                                _destinationMaterials[i] = newMaterial;
                            }
                            else
                            {
                                _destinationMaterials.RemoveAt(i);
                            }
                        }
                    }
                }
            }
            
            // 常に空のフィールドを1つ表示
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(BUTTON_WIDTH_SMALL + 4f);
                
                EditorGUI.BeginChangeCheck();
                var draggedMaterial = (Material)EditorGUILayout.ObjectField((Material)null, typeof(Material), false);
                
                if (EditorGUI.EndChangeCheck() && draggedMaterial != null)
                {
                    if (!_destinationMaterials.Contains(draggedMaterial) && draggedMaterial != _sourceMaterial)
                    {
                        _destinationMaterials.Add(draggedMaterial);
                    }
                }
            }
            
            if (scrollHeight > 0)
            {
                EditorGUILayout.EndScrollView();
            }
            
            // 全クリアボタンは常に表示
            if (_destinationMaterials.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("全クリア", GUILayout.Width(BUTTON_WIDTH_LARGE)))
                    {
                        _destinationMaterials.Clear();
                    }
                }
            }
        }
        
        private void DrawPropertyAndMaterialSelection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPropertySelection();
                DrawMaterialList();
            }
        }
        
        private void DrawPropertySelection()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorGUIUtility.currentViewWidth * VIEW_WIDTH_RATIO)))
            {
                if (_sourceMaterial != null && _availableGroups.Count > 0)
                {
                    DrawColoredBox(SelectColor, () => 
                    {
                        EditorGUILayout.LabelField("コピーするプロパティ", EditorStyles.boldLabel);
                        DrawPropertySelectionControls();
                        EditorGUILayout.Space(5);
                        
                        _propertyScroll = EditorGUILayout.BeginScrollView(_propertyScroll, GUILayout.Height(SCROLL_HEIGHT));
                        DrawPropertyGroups();
                        EditorGUILayout.EndScrollView();
                    });
                }
                else
                {
                    string message = _sourceMaterial == null ? "コピー元マテリアルを選択してください。" : "利用可能なプロパティがありません。";
                    MessageType messageType = _sourceMaterial == null ? MessageType.Info : MessageType.Warning;
                    EditorGUILayout.HelpBox(message, messageType);
                }
            }
        }
        
        private void DrawPropertySelectionControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全選択", GUILayout.Width(BUTTON_WIDTH_LARGE)))
                    SetAllPropertiesSelection(true);
                
                if (GUILayout.Button("全解除", GUILayout.Width(BUTTON_WIDTH_LARGE)))
                    SetAllPropertiesSelection(false);
            }
        }
        
        private void DrawMaterialList()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_materials.Count > 0)
                {
                    DrawColoredBox(ContentColor, () => 
                    {
                        EditorGUILayout.LabelField("マテリアル一覧", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("下のボタンでコピー元・コピー先マテリアルを選択");
                        EditorGUILayout.Space(5);
                        
                        _materialListScroll = EditorGUILayout.BeginScrollView(_materialListScroll, GUILayout.Height(SCROLL_HEIGHT));
                        DrawMaterialSelectionList();
                        EditorGUILayout.EndScrollView();
                    });
                }
                else
                {
                    EditorGUILayout.HelpBox("マテリアルが見つかりません。", MessageType.Info);
                }
            }
        }
        
        private void DrawMaterialSelectionList()
        {
            foreach (var material in _materials.Where(m => m != null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawSourceMaterialButton(material);
                    DrawDestinationMaterialButton(material);
                    EditorGUILayout.ObjectField(material, typeof(Material), false);
                }
            }
        }
        
        private void DrawSourceMaterialButton(Material material)
        {
            bool isSource = material == _sourceMaterial;
            GUI.enabled = !isSource;
            if (GUILayout.Button(isSource ? "元選択中" : "コピー元", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
            {
                _sourceMaterial = material;
                UpdateAvailableProperties();
            }
            GUI.enabled = true;
        }
        
        private void DrawDestinationMaterialButton(Material material)
        {
            bool isDestination = _destinationMaterials.Contains(material);
            GUI.enabled = !isDestination && material != _sourceMaterial;
            if (GUILayout.Button(isDestination ? "選択済" : "追加", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
            {
                if (!_destinationMaterials.Contains(material))
                    _destinationMaterials.Add(material);
            }
            GUI.enabled = true;
        }
        
        private void DrawMaterialCopyExecuteButton()
        {
            DrawColoredBox(HeaderColor, () => 
            {
                bool canCopy = CanExecuteMaterialCopy();
                
                GUI.enabled = canCopy;
                if (GUILayout.Button($"マテリアルプロパティをコピー ({_destinationMaterials.Count}個)", GUILayout.Height(EXECUTE_BUTTON_HEIGHT)))
                {
                    if (EditorUtility.DisplayDialog("確認", 
                        $"「{_sourceMaterial.name}」から{_destinationMaterials.Count}個のマテリアルに選択したプロパティをコピーしますか？", 
                        "コピー", "キャンセル"))
                    {
                        ExecuteMaterialPropertyCopy();
                    }
                }
                GUI.enabled = true;
                
                if (!canCopy) DrawMaterialCopyWarnings();
            });
        }
        
        private bool CanExecuteMaterialCopy()
        {
            return _sourceMaterial != null && 
                   _destinationMaterials.Count > 0 && 
                   _propertySelections.Any(p => p.Value);
        }
        
        private void DrawMaterialCopyWarnings()
        {
            if (_sourceMaterial == null)
                EditorGUILayout.HelpBox("コピー元マテリアルを選択してください。", MessageType.Warning);
            else if (_destinationMaterials.Count == 0)
                EditorGUILayout.HelpBox("コピー先マテリアルを選択してください。", MessageType.Warning);
            else if (!_propertySelections.Any(p => p.Value))
                EditorGUILayout.HelpBox("コピーするプロパティを選択してください。", MessageType.Warning);
        }
        
        private void DrawPropertyGroups()
        {
            foreach (var group in _availableGroups.Where(g => !g.HasParent))
            {
                DrawPropertyGroup(group);
            }
        }
        
        private void DrawPropertyGroup(PropertyGroup group)
        {
            var childGroups = _availableGroups.Where(g => g.HasParent && g.ParentId == group.Name).ToList();
            var allProperties = GetAllGroupProperties(group);
            
            if (allProperties.Count == 0) return;
            
            DrawColoredBox(GroupColor, () => 
            {
                DrawGroupHeader(group, childGroups, allProperties);
                
                if (!_groupFoldouts.GetValueOrDefault(group.Name, true)) return;
                
                EditorGUI.indentLevel++;
                DrawChildGroups(childGroups);
                DrawGroupProperties(group);
                EditorGUI.indentLevel--;
            });
        }
        
        private void DrawGroupHeader(PropertyGroup group, List<PropertyGroup> childGroups, List<PropertyItem> allProperties)
        {
            if (!_groupFoldouts.TryGetValue(group.Name, out bool expanded))
                _groupFoldouts[group.Name] = expanded = true;
            
            using (new EditorGUILayout.HorizontalScope())
            {
                if (childGroups.Count > 0)
                {
                    DrawGroupSelectionToggle(allProperties);
                }
                else
                {
                    GUILayout.Space(LABEL_WIDTH_SMALL);
                }
                
                EditorGUILayout.LabelField(group.DisplayName, EditorStyles.boldLabel, GUILayout.Width(BUTTON_WIDTH_LARGE));
                
                GUILayout.FlexibleSpace();
                string arrow = expanded ? "▽" : "▷";
                if (GUILayout.Button(arrow, EditorStyles.label, GUILayout.Width(LABEL_WIDTH_SMALL)))
                {
                    _groupFoldouts[group.Name] = !expanded;
                }
            }
        }
        
        private void DrawGroupSelectionToggle(List<PropertyItem> allProperties)
        {
            bool allSelected = allProperties.All(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
            bool anySelected = allProperties.Any(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
            
            EditorGUI.showMixedValue = anySelected && !allSelected;
            bool newState = EditorGUILayout.Toggle(allSelected, GUILayout.Width(LABEL_WIDTH_SMALL));
            EditorGUI.showMixedValue = false;
            
            if (newState != allSelected)
            {
                foreach (var prop in allProperties)
                    _propertySelections[prop.Name] = newState;
            }
        }
        
        private void DrawChildGroups(List<PropertyGroup> childGroups)
        {
            foreach (var childGroup in childGroups)
            {
                DrawChildGroup(childGroup);
            }
        }
        
        private void DrawChildGroup(PropertyGroup childGroup)
        {
            var availableProps = childGroup.Properties.Where(p => _sourceMaterial.HasProperty(p.Name)).ToList();
            if (availableProps.Count == 0) return;
            
            if (!_childGroupFoldouts.TryGetValue(childGroup.Name, out bool childExpanded))
                _childGroupFoldouts[childGroup.Name] = childExpanded = false;
            
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawChildGroupSelectionToggle(availableProps, childGroup.DisplayName);
                
                GUILayout.FlexibleSpace();
                string arrow = childExpanded ? "▽" : "▷";
                if (GUILayout.Button(arrow, EditorStyles.label, GUILayout.Width(LABEL_WIDTH_SMALL)))
                {
                    _childGroupFoldouts[childGroup.Name] = !childExpanded;
                }
            }
            
            if (!childExpanded) return;
            
            EditorGUI.indentLevel++;
            foreach (var prop in availableProps)
            {
                DrawPropertyToggle(prop);
            }
            EditorGUI.indentLevel--;
        }
        
        private void DrawChildGroupSelectionToggle(List<PropertyItem> availableProps, string displayName)
        {
            bool allSelected = availableProps.All(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
            bool anySelected = availableProps.Any(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
            
            EditorGUI.showMixedValue = anySelected && !allSelected;
            bool newState = EditorGUILayout.ToggleLeft(displayName, allSelected, EditorStyles.boldLabel);
            EditorGUI.showMixedValue = false;
            
            if (newState != allSelected)
            {
                foreach (var prop in availableProps)
                    _propertySelections[prop.Name] = newState;
            }
        }
        
        private void DrawGroupProperties(PropertyGroup group)
        {
            foreach (var prop in group.Properties)
            {
                if (_sourceMaterial.HasProperty(prop.Name))
                {
                    DrawPropertyToggle(prop);
                }
            }
        }
        
        private void DrawPropertyToggle(PropertyItem prop)
        {
            if (!_propertySelections.ContainsKey(prop.Name))
                _propertySelections[prop.Name] = false;
            
            _propertySelections[prop.Name] = EditorGUILayout.ToggleLeft(prop.DisplayName, _propertySelections[prop.Name]);
        }
        
        private List<PropertyItem> GetAllGroupProperties(PropertyGroup group)
        {
            var allProps = new List<PropertyItem>(group.Properties);
            
            var childGroups = _availableGroups.Where(g => g.HasParent && g.ParentId == group.Name);
            foreach (var childGroup in childGroups)
                allProps.AddRange(childGroup.Properties);
            
            return allProps.Where(prop => _sourceMaterial != null && _sourceMaterial.HasProperty(prop.Name)).ToList();
        }
        
        private void SetAllPropertiesSelection(bool select)
        {
            foreach (var group in _availableGroups)
            {
                foreach (var prop in group.Properties)
                {
                    if (_sourceMaterial.HasProperty(prop.Name))
                        _propertySelections[prop.Name] = select;
                }
            }
        }
        
        private void UpdateAvailableProperties()
        {
            _propertySelections.Clear();
            _availableGroups.Clear();
            
            if (_sourceMaterial == null) return;
            
            foreach (var group in PROPERTY_GROUPS)
            {
                var availableGroup = new PropertyGroup(group.Name, group.DisplayName, group.HasParent, group.ParentId);
                
                foreach (var prop in group.Properties)
                {
                    if (_sourceMaterial.HasProperty(prop.Name))
                    {
                        availableGroup.Properties.Add(prop);
                        _propertySelections[prop.Name] = false;
                    }
                }
                
                if (availableGroup.Properties.Count > 0 || 
                    (!group.HasParent && PROPERTY_GROUPS.Any(g => g.HasParent && g.ParentId == group.Name && 
                    g.Properties.Any(p => _sourceMaterial.HasProperty(p.Name)))))
                {
                    _availableGroups.Add(availableGroup);
                }
            }
        }
        
        private void ExecuteMaterialPropertyCopy()
        {
            if (_sourceMaterial == null || _destinationMaterials.Count == 0) return;
            
            int totalCopied = 0;
            
            foreach (var targetMaterial in _destinationMaterials.Where(m => m != null))
            {
                Undo.RecordObject(targetMaterial, "Copy Material Properties");
                totalCopied += CopyPropertiesToMaterial(targetMaterial);
                EditorUtility.SetDirty(targetMaterial);
            }
            
            EditorUtility.DisplayDialog("完了", 
                $"{_destinationMaterials.Count}個のマテリアルに合計{totalCopied}個のプロパティをコピーしました。\n" +
                $"コピー元: {_sourceMaterial.name}", "OK");
        }
        
        private int CopyPropertiesToMaterial(Material targetMaterial)
        {
            int copiedCount = 0;
            
            foreach (var kvp in _propertySelections.Where(p => p.Value))
            {
                string propName = kvp.Key;
                if (!_sourceMaterial.HasProperty(propName) || !targetMaterial.HasProperty(propName))
                    continue;
                
                if (CopyMaterialProperty(propName, targetMaterial))
                    copiedCount++;
            }
            
            return copiedCount;
        }
        
        private bool CopyMaterialProperty(string propName, Material targetMaterial)
        {
            var shader = _sourceMaterial.shader;
            int propIndex = shader.FindPropertyIndex(propName);
            if (propIndex < 0) return false;
            
            var propType = shader.GetPropertyType(propIndex);
            
            try
            {
                switch (propType)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        targetMaterial.SetColor(propName, _sourceMaterial.GetColor(propName));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        targetMaterial.SetVector(propName, _sourceMaterial.GetVector(propName));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        targetMaterial.SetFloat(propName, _sourceMaterial.GetFloat(propName));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                        targetMaterial.SetInt(propName, _sourceMaterial.GetInt(propName));
                        break;
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"プロパティ {propName} のコピーに失敗 ({targetMaterial.name}): {e.Message}");
                return false;
            }
        }
        
        private void DrawBlendShapeSearch()
        {
            DrawColoredBox(HeaderColor, () => _searchText = EditorGUILayout.TextField("検索", _searchText));
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            foreach (var smr in _skinnedMeshRenderers.Where(s => s?.sharedMesh != null))
            {
                if (!string.IsNullOrEmpty(_searchText) && !HasBlendShape(smr, _searchText))
                    continue;
                    
                DrawSkinnedMeshRenderer(smr);
            }
            
            EditorGUILayout.EndScrollView();
            
            DrawBlendShapeFoldoutControls();
        }
        
        private void DrawBlendShapeFoldoutControls()
        {
            DrawColoredBox(SelectColor, () => 
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    
                    if (GUILayout.Button("展開", GUILayout.Width(BUTTON_WIDTH_MEDIUM)))
                        SetAllFoldouts(true);
                        
                    if (GUILayout.Button("折り畳む", GUILayout.Width(BUTTON_WIDTH_LARGE)))
                        SetAllFoldouts(false);
                        
                    GUILayout.FlexibleSpace();
                }
            });
        }
        
        private void DrawSkinnedMeshRenderer(SkinnedMeshRenderer smr)
        {
            if (!_foldoutStates.TryGetValue(smr, out bool isExpanded)) 
                _foldoutStates[smr] = isExpanded = true;
            
            DrawColoredBox(ContentColor, () => 
            {
                _foldoutStates[smr] = EditorGUILayout.Foldout(isExpanded, smr.name, true, EditorStyles.foldoutHeader);
                if (!_foldoutStates[smr]) return;
                
                EditorGUI.indentLevel++;
                var mesh = smr.sharedMesh;
                
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string shapeName = mesh.GetBlendShapeName(i);
                    if (!string.IsNullOrEmpty(_searchText) && !shapeName.Contains(_searchText, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                        
                    DrawBlendShapeSlider(smr, i, shapeName);
                }
                EditorGUI.indentLevel--;
            });
        }
        
        private void DrawBlendShapeSlider(SkinnedMeshRenderer smr, int index, string shapeName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(shapeName, GUILayout.Width(EditorGUIUtility.currentViewWidth * VIEW_WIDTH_RATIO));
                
                float currentValue = smr.GetBlendShapeWeight(index);
                float newValue = EditorGUILayout.Slider(currentValue, 0f, 100f);
                
                if (!Mathf.Approximately(newValue, currentValue))
                {
                    Undo.RecordObject(smr, "Change BlendShape");
                    smr.SetBlendShapeWeight(index, newValue);
                    EditorUtility.SetDirty(smr);
                }
            }
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
                name = mesh.name
            };
            
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
            // アーマーチュア設定は常に表示
            DrawArmatureSettings();
            
            bool hasOutfitBones = _outfitBones.Count > 0;
            bool hasAvatarBones = _avatarBones.Count > 0;
            
            DrawColoredBox(HeaderColor, () => 
            {
                EditorGUILayout.LabelField("スケール調整", EditorStyles.boldLabel);
                
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
            
            // ボーンがある場合はスケールリストを表示
            if (hasAvatarBones || hasOutfitBones)
            {
                DrawBoneScaleList();
            }
            
            // 同期ボタンは素体・衣装両方のボーンがある場合のみ有効
            DrawScaleSyncButton(hasAvatarBones && hasOutfitBones);
        }
        
        private void DrawBoneScaleList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            foreach (var boneName in BONE_ORDER)
            {
                bool hasAvatarBone = _avatarBones.ContainsKey(boneName);
                bool hasAnyOutfitBone = _outfitBones.Values.Any(boneMap => boneMap.ContainsKey(boneName));
                
                if (hasAvatarBone || hasAnyOutfitBone)
                {
                    DrawBoneSection(boneName);
                }
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(10);
        }
        
        private void DrawBoneSection(string boneName)
        {
            if (!_boneFoldouts.TryGetValue(boneName, out bool isExpanded))
                _boneFoldouts[boneName] = isExpanded = true;
            
            DrawColoredBox(ContentColor, () => 
            {
                var style = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
                _boneFoldouts[boneName] = EditorGUILayout.Foldout(isExpanded, boneName, true, style);
                if (!_boneFoldouts[boneName]) return;
                
                EditorGUI.indentLevel++;
                
                DrawAvatarBoneScale(boneName);
                DrawSeparator();
                DrawOutfitBoneScales(boneName);
                
                EditorGUI.indentLevel--;
            });
        }
        
        private void DrawAvatarBoneScale(string boneName)
        {
            if (_avatarBones.TryGetValue(boneName, out var avatarBone))
                DrawBoneScaleControls("素体", avatarBone);
            else
                EditorGUILayout.LabelField("素体にボーンなし");
        }
        
        private void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
        
        private void DrawOutfitBoneScales(string boneName)
        {
            foreach (var outfit in _targets.Where(t => t != null))
            {
                if (_outfitBones.TryGetValue(outfit, out var boneMap) && boneMap.TryGetValue(boneName, out var outfitBone))
                    DrawBoneScaleControls("衣装", outfitBone);
                else
                    EditorGUILayout.LabelField($"「{outfit.name}」にボーンなし");
            }
        }
        
        private void DrawArmatureSettings()
        {
            DrawColoredBox(HeaderColor, () => 
            {
                _serializedObject.Update();
                EditorGUILayout.PropertyField(_armatureProperty, new GUIContent("素体Armature"));
                _serializedObject.ApplyModifiedProperties();
                
                var armature = _armatureProperty.objectReferenceValue as Transform;
                if (armature != _avatarArmature)
                {
                    _avatarArmature = armature;
                    ScanBones();
                }
                
                if (_avatarArmature == null)
                    EditorGUILayout.HelpBox("素体のArmatureを設定してください。", MessageType.Warning);
            });
            
            EditorGUILayout.Space(5);
        }
        
        private void DrawScaleSyncButton(bool canSync)
        {
            DrawColoredBox(SelectColor, () => 
            {
                GUI.enabled = canSync;
                if (GUILayout.Button("衣装のスケールを身体に合わせる", GUILayout.Height(EXECUTE_BUTTON_HEIGHT)))
                    SynchronizeScales();
                GUI.enabled = true;
                
                if (!canSync)
                {
                    EditorGUILayout.HelpBox("素体と衣装の両方にボーンが必要です。", MessageType.Info);
                }
            });
        }
        
        private void DrawBoneScaleControls(string label, Transform bone)
        {
            var scaleAdjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(label, EditorStyles.label, GUILayout.Width(LABEL_WIDTH_LARGE)))
                {
                    Selection.activeTransform = bone;
                    EditorGUIUtility.PingObject(bone);
                }
                
                if (scaleAdjuster == null)
                {
                    if (GUILayout.Button("ScaleAdjusterを追加"))
                        Undo.AddComponent<ModularAvatarScaleAdjuster>(bone.gameObject);
                }
                else
                {
                    DrawScaleFields(scaleAdjuster);
                }
            }
        }
        
        private void DrawScaleFields(ModularAvatarScaleAdjuster adjuster)
        {
            var scale = adjuster.Scale;
            EditorGUI.BeginChangeCheck();
            
            float fieldWidth = (EditorGUIUtility.currentViewWidth - BUTTON_WIDTH_LARGE) / 3;
            EditorGUIUtility.labelWidth = LABEL_WIDTH_MEDIUM;
            
            float x = EditorGUILayout.FloatField("X", scale.x, GUILayout.Width(fieldWidth));
            float y = EditorGUILayout.FloatField("Y", scale.y, GUILayout.Width(fieldWidth));
            float z = EditorGUILayout.FloatField("Z", scale.z, GUILayout.Width(fieldWidth));
            
            EditorGUIUtility.labelWidth = 0;
            
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(adjuster, "Change Scale");
                adjuster.Scale = new Vector3(x, y, z);
                EditorUtility.SetDirty(adjuster);
            }
        }
        
        private void ScanData()
        {
            switch (_mode)
            {
                case Mode.Material: ScanMaterials(); break;
                case Mode.BlendShape: 
                    ScanSkinnedMeshRenderers();
                    if (_blendShapeMode == BlendShapeMode.Compose)
                        ScanForCompose();
                    break;
                case Mode.Scale: ScanBones(); break;
            }
        }
        
        private void ScanSkinnedMeshRenderers()
        {
            _skinnedMeshRenderers.Clear();
            var previousFoldouts = new Dictionary<Object, bool>(_foldoutStates);
            _foldoutStates.Clear();
            
            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                foreach (var smr in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh?.blendShapeCount > 0)
                    {
                        _skinnedMeshRenderers.Add(smr);
                        _foldoutStates[smr] = previousFoldouts.GetValueOrDefault(smr, true);
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
            
            FindAndSetAvatarArmature();
            
            if (_avatarArmature != null)
                BuildBoneMap(_avatarArmature, _avatarBones);

            foreach (var outfit in _targets.Where(t => t != null))
            {
                var armature = FindChildByKeyword(outfit.transform, "armature");
                if (armature != null)
                {
                    var boneMap = new Dictionary<string, Transform>();
                    BuildBoneMap(armature, boneMap);
                    if (boneMap.Count > 0)
                        _outfitBones[outfit] = boneMap;
                }
            }
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
        
        private void DrawColoredBox(Color color, System.Action content)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUI.backgroundColor = originalColor;
                content();
            }
            GUI.backgroundColor = originalColor;
        }
        
        private bool IsValidTarget(GameObject target) => target != null && !target.CompareTag("EditorOnly");
        
        private bool HasBlendShape(SkinnedMeshRenderer smr, string searchText)
        {
            var mesh = smr.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).Contains(searchText, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        
        private void SetAllFoldouts(bool expand)
        {
            foreach (var smr in _skinnedMeshRenderers.Where(s => s?.sharedMesh != null))
            {
                if (!string.IsNullOrEmpty(_searchText) && !HasBlendShape(smr, _searchText))
                    continue;
                
                _foldoutStates[smr] = expand;
            }
        }
        
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
            
            string normalizedKeyword = keyword.ToLowerInvariant().Replace(" ", "");
            
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                string normalizedName = child.name.ToLowerInvariant().Replace("_", "").Replace(" ", "");
                
                if (normalizedName.Contains(normalizedKeyword))
                    return child;
            }
            return null;
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
        
        private void SynchronizeScales()
        {
            if (_avatarArmature == null)
            {
                EditorUtility.DisplayDialog("エラー", "素体のArmatureが見つかりません。", "OK");
                return;
            }
            
            if (_avatarBones.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "素体のボーンが見つかりません。", "OK");
                return;
            }
            
            Undo.SetCurrentGroupName("Sync Scales");
            int undoGroup = Undo.GetCurrentGroup();
            
            SyncBoneScales();
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("完了", "スケールを同期しました。", "OK");
        }
        
        private void SyncBoneScales()
        {
            foreach (var boneName in BONE_ORDER)
            {
                if (!_avatarBones.TryGetValue(boneName, out var avatarBone)) continue;
                
                var avatarAdjuster = avatarBone.GetComponent<ModularAvatarScaleAdjuster>();
                if (avatarAdjuster == null) continue;
                
                var scale = avatarAdjuster.Scale;
                SyncOutfitBoneScale(boneName, scale);
            }
        }
        
        private void SyncOutfitBoneScale(string boneName, Vector3 scale)
        {
            foreach (var outfit in _targets.Where(t => t != null))
            {
                if (!_outfitBones.TryGetValue(outfit, out var boneMap) || 
                    !boneMap.TryGetValue(boneName, out var outfitBone))
                    continue;
                
                var adjuster = outfitBone.GetComponent<ModularAvatarScaleAdjuster>();
                if (adjuster != null)
                {
                    Undo.RecordObject(adjuster, "Sync Scale");
                    adjuster.Scale = scale;
                    EditorUtility.SetDirty(adjuster);
                }
                else
                {
                    adjuster = Undo.AddComponent<ModularAvatarScaleAdjuster>(outfitBone.gameObject);
                    adjuster.Scale = scale;
                    EditorUtility.SetDirty(adjuster);
                }
            }
        }
    }
}
#endif