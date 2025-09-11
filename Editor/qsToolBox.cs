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
        private Vector2 _scroll;
        private Vector2 _materialListScroll;
        private Vector2 _propertyScroll;
        private Vector2 _composeShapeScroll;
        private Vector2 _shapeListScroll;
        private string _search = "";
        
        // UI State
        private readonly Dictionary<Object, bool> _foldouts = new Dictionary<Object, bool>();
        private readonly Dictionary<string, bool> _boneFolds = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _childGroupFoldouts = new Dictionary<string, bool>();
        
        // Data Cache
        private readonly List<SkinnedMeshRenderer> _smrs = new List<SkinnedMeshRenderer>();
        private readonly List<Material> _mats = new List<Material>();
        private readonly Dictionary<Material, List<(Renderer r, int slot)>> _matUsage = new Dictionary<Material, List<(Renderer, int)>>();
        private readonly Dictionary<GameObject, Dictionary<string, Transform>> _outfitBones = new Dictionary<GameObject, Dictionary<string, Transform>>();
        private readonly Dictionary<string, Transform> _avatarBones = new Dictionary<string, Transform>();
        
        // Material Copy Fields
        private Material _baseMaterial;
        private readonly List<Material> _targetMaterials = new List<Material>();
        private readonly Dictionary<string, bool> _propertySelections = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _groupFoldouts = new Dictionary<string, bool>();
        private readonly List<PropertyGroup> _availableGroups = new List<PropertyGroup>();
        
        // Compose Mode
        private SkinnedMeshRenderer _composeTarget;
        private string _baseShapeName = "";
        private readonly List<(string name, float weight)> _composeShapes = new List<(string, float)>();
        private string _composeSearch = "";
        private readonly List<string> _shapeNames = new List<string>();
        private string _newShapeName = "";
        private bool _overwriteShape = true;
        
        // Cache Control
        private SerializedObject _so;
        private SerializedProperty _targetsProp;
        private SerializedProperty _armatureProp;
        private int _targetHash = -1;
        private bool _dirty = true;
        
        // UI Colors
        private static readonly Color HeaderCol = new Color(0.6f, 0.8f, 1f, 0.8f);
        private static readonly Color ContentCol = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        private static readonly Color SelectCol = new Color(0.5f, 0.7f, 1f, 0.8f);
        private static readonly Color TargetCol = new Color(0.9f, 0.9f, 0.6f, 0.8f);
        private static readonly Color SubTabCol = new Color(0.8f, 0.9f, 1f, 0.7f);
        private static readonly Color BaseCol = new Color(0.8f, 1f, 0.8f, 0.8f);
        private static readonly Color CopyCol = new Color(1f, 0.9f, 0.8f, 0.8f);
        private static readonly Color GroupCol = new Color(0.9f, 0.9f, 1f, 0.7f);
        
        // Constants
        private static readonly string[] TabNames = { "マテリアル", "ブレンドシェイプ", "スケール" };
        private static readonly GUIContent[] TabTips = {
            new GUIContent("マテリアル", "探索対象のマテリアルを置換・コピーできます"),
            new GUIContent("ブレンドシェイプ", "探索対象のブレンドシェイプを表示・編集します"),
            new GUIContent("スケール", "ModularAvatarのスケール調整機能を使用します")
        };
        
        private static readonly string[] BlendShapeTabNames = { "シェイプキー検索", "シェイプキー合成" };
        private static readonly GUIContent[] BlendShapeTabTips = {
            new GUIContent("シェイプキー検索", "ブレンドシェイプを検索・編集します"),
            new GUIContent("シェイプキー合成", "ベースシェイプキーに他のシェイプキーを合成します")
        };
        
        private static readonly string[] MaterialTabNames = { "マテリアル置換", "マテリアルコピー" };
        private static readonly GUIContent[] MaterialTabTips = {
            new GUIContent("マテリアル置換", "マテリアルを直接置換します"),
            new GUIContent("マテリアルコピー", "マテリアルプロパティを選択的にコピーします")
        };
        
        // プロパティグループ定義
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
        
        // プロパティグループ定義
        private static readonly List<PropertyGroup> PropertyGroups = new List<PropertyGroup>
        {
            new PropertyGroup("CustomSafetyFallback", "Custom Safety Fallback")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_CustomSafetyFallback", "Custom Safety Fallback")
                }
            },
            
            new PropertyGroup("Shadow", "影設定")
            {
                Properties = new List<PropertyItem>()
            },
            new PropertyGroup("ShadowValues", "影数値", true, "Shadow")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseShadow", "影使用"),
                    new PropertyItem("_ShadowBorder", "影境界"),
                    new PropertyItem("_ShadowBlur", "影ぼかし"),
                    new PropertyItem("_ShadowStrength", "影強度"),
                    new PropertyItem("_Shadow2ndBorder", "2nd影境界"),
                    new PropertyItem("_Shadow2ndBlur", "2nd影ぼかし"),
                    new PropertyItem("_Shadow3rdBorder", "3rd影境界"),
                    new PropertyItem("_Shadow3rdBlur", "3rd影ぼかし"),
                    new PropertyItem("_ShadowMainStrength", "影強度（メイン）"),
                    new PropertyItem("_ShadowEnvStrength", "影強度（環境）"),
                    new PropertyItem("_ShadowBorderBlur", "影境界ぼかし"),
                    new PropertyItem("_Shadow2ndMainStrength", "2nd影強度（メイン）"),
                    new PropertyItem("_Shadow2ndEnvStrength", "2nd影強度（環境）"),
                    new PropertyItem("_Shadow3rdMainStrength", "3rd影強度（メイン）"),
                    new PropertyItem("_Shadow3rdEnvStrength", "3rd影強度（環境）")
                }
            },
            new PropertyGroup("ShadowColors", "影色", true, "Shadow")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_ShadowColor", "影色"),
                    new PropertyItem("_Shadow2ndColor", "2nd影色"),
                    new PropertyItem("_Shadow3rdColor", "3rd影色")
                }
            },
            
            new PropertyGroup("RimShade", "RimShade")
            {
                Properties = new List<PropertyItem>()
            },
            new PropertyGroup("RimShadeValues", "RimShade数値", true, "RimShade")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseRimShade", "RimShade使用"),
                    new PropertyItem("_RimShadeBorder", "RimShade境界"),
                    new PropertyItem("_RimShadeBlur", "RimShadeぼかし"),
                    new PropertyItem("_RimShadeFresnelPower", "RimShadeフレネル"),
                    new PropertyItem("_RimShadeMin", "RimShade最小値"),
                    new PropertyItem("_RimShadeMax", "RimShade最大値"),
                    new PropertyItem("_RimShadeMix", "RimShade混合"),
                    new PropertyItem("_RimShadeNormalStrength", "RimShadeノーマル強度")
                }
            },
            new PropertyGroup("RimShadeColors", "RimShade色", true, "RimShade")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_RimShadeColor", "RimShade色")
                }
            },
            
            new PropertyGroup("BackLight", "逆光ライト")
            {
                Properties = new List<PropertyItem>()
            },
            new PropertyGroup("BackLightValues", "逆光ライト数値", true, "BackLight")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseBacklight", "逆光ライト使用"),
                    new PropertyItem("_BacklightPower", "逆光ライト強度"),
                    new PropertyItem("_BacklightBorder", "逆光ライト境界"),
                    new PropertyItem("_BacklightBlur", "逆光ライトぼかし"),
                    new PropertyItem("_BacklightDirectivity", "逆光ライト指向性"),
                    new PropertyItem("_BacklightViewStrength", "逆光ライトビュー強度"),
                    new PropertyItem("_BacklightNormalStrength", "逆光ライトノーマル強度")
                }
            },
            new PropertyGroup("BackLightColors", "逆光ライト色", true, "BackLight")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_BacklightColor", "逆光ライト色")
                }
            },
            
            new PropertyGroup("Rim", "リムライト")
            {
                Properties = new List<PropertyItem>()
            },
            new PropertyGroup("RimValues", "リムライト数値", true, "Rim")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_UseRim", "リムライト使用"),
                    new PropertyItem("_RimBorder", "リム境界"),
                    new PropertyItem("_RimBlur", "リムぼかし"),
                    new PropertyItem("_RimFresnelPower", "リムフレネル"),
                    new PropertyItem("_RimPower", "リム強度"),
                    new PropertyItem("_RimEnableLighting", "リムライティング"),
                    new PropertyItem("_RimShadowMask", "リム影マスク"),
                    new PropertyItem("_RimBackfaceMask", "リム裏面マスク"),
                    new PropertyItem("_RimNormalStrength", "リムノーマル強度"),
                    new PropertyItem("_RimApplyTransparency", "リム透明度適用"),
                    new PropertyItem("_RimDirStrength", "リム方向強度"),
                    new PropertyItem("_RimDirRange", "リム方向範囲"),
                    new PropertyItem("_RimIndirRange", "リム間接範囲"),
                    new PropertyItem("_RimIndirColor", "リム間接色"),
                    new PropertyItem("_RimIndirBorder", "リム間接境界"),
                    new PropertyItem("_RimIndirBlur", "リム間接ぼかし")
                }
            },
            new PropertyGroup("RimColors", "リムライト色", true, "Rim")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_RimColor", "リムライト色")
                }
            },
            
            new PropertyGroup("Outline", "輪郭線設定")
            {
                Properties = new List<PropertyItem>()
            },
            new PropertyGroup("OutlineValues", "輪郭線数値", true, "Outline")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_OutlineWidth", "アウトライン幅"),
                    new PropertyItem("_OutlineScaledMaxDistance", "スケール最大距離"),
                    new PropertyItem("_OutlineVertexR2Width", "頂点R2幅"),
                    new PropertyItem("_OutlineFixWidth", "固定幅"),
                    new PropertyItem("_OutlineEnableLighting", "アウトラインライティング"),
                    new PropertyItem("_OutlineCull", "アウトラインカリング"),
                    new PropertyItem("_OutlineZBias", "アウトラインZバイアス"),
                    new PropertyItem("_OutlineDissolveOutputMode", "アウトラインディゾルブ出力"),
                    new PropertyItem("_OutlineStencilRef", "アウトラインステンシル"),
                    new PropertyItem("_OutlineStencilReadMask", "アウトラインステンシル読み取り"),
                    new PropertyItem("_OutlineStencilWriteMask", "アウトラインステンシル書き込み"),
                    new PropertyItem("_OutlineStencilComp", "アウトラインステンシル比較"),
                    new PropertyItem("_OutlineStencilPass", "アウトラインステンシルパス"),
                    new PropertyItem("_OutlineStencilFail", "アウトラインステンシル失敗"),
                    new PropertyItem("_OutlineStencilZFail", "アウトラインステンシルZ失敗")
                }
            },
            new PropertyGroup("OutlineColors", "輪郭線色", true, "Outline")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_OutlineColor", "アウトライン色")
                }
            },
            
            new PropertyGroup("DistanceFade", "距離フェード")
            {
                Properties = new List<PropertyItem>()
            },
            new PropertyGroup("DistanceFadeValues", "距離フェード数値", true, "DistanceFade")
            {
                Properties = new List<PropertyItem>
                {
                    new PropertyItem("_DistanceFadeStart", "フェード開始距離"),
                    new PropertyItem("_DistanceFadeEnd", "フェード終了距離"),
                    new PropertyItem("_DistanceFadePower", "フェード強度"),
                    new PropertyItem("_DistanceFadeMode", "フェードモード"),
                    new PropertyItem("_DistanceFadeRimWidth", "フェードリム幅"),
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
        
        private static readonly string[] BoneOrder = {
            "Hips", "Spine", "Chest", "Breast L", "Breast R", "Neck", "Head", 
            "Butt L", "Butt R", "Upper Leg L", "Upper Leg R", "Lower Leg L", "Lower Leg R", 
            "Foot L", "Foot R", "Shoulder L", "Shoulder R", "Upper Arm L", "Upper Arm R", 
            "Lower Arm L", "Lower Arm R", "Hand L", "Hand R"
        };
        
        private static readonly Dictionary<string, string> BoneParent = new Dictionary<string, string>
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
        
        [MenuItem("Tools/qs/ツールボックス %q")]
        public static void ShowWindow()
        {
            var window = GetWindow<QsToolBox>("qsToolBox");
            window._targets = new List<GameObject>(Selection.gameObjects);
            window.Scan();
        }
        
        private void OnEnable()
        {
            _so = new SerializedObject(this);
            _targetsProp = _so.FindProperty("_targets");
            _armatureProp = _so.FindProperty("_avatarArmature");
            _targetsProp.isExpanded = true;
            
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnUndo;
            
            Scan();
        }
        
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnUndo;
        }
        
        private void OnHierarchyChanged() => _dirty = true;
        private UndoPropertyModification[] OnUndo(UndoPropertyModification[] mods)
        {
            _dirty = true;
            return mods;
        }
        
        private void OnGUI()
        {
            CheckTargetChanges();
            DrawTabs();
            
            if (_mode == Mode.BlendShape)
            {
                DrawBlendShapeTabs();
            }
            else if (_mode == Mode.Material)
            {
                DrawMaterialTabs();
            }
            
            EditorGUILayout.Space();
            
            switch (_mode)
            {
                case Mode.Material: 
                    switch (_materialMode)
                    {
                        case MaterialMode.Replace: DrawMaterialReplace(); break;
                        case MaterialMode.Copy: DrawMaterialCopy(); break;
                    }
                    break;
                case Mode.BlendShape: 
                    switch (_blendShapeMode)
                    {
                        case BlendShapeMode.Search: DrawBlendShapeSearch(); break;
                        case BlendShapeMode.Compose: DrawBlendShapeCompose(); break;
                    }
                    break;
                case Mode.Scale: DrawScale(); break;
            }
        }
        
        private void CheckTargetChanges()
        {
            Box(TargetCol, () => 
            {
                _so.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_targetsProp, new GUIContent("探索対象（自動）", "スキャン対象を指定"), true);
                bool changed = EditorGUI.EndChangeCheck();
                _so.ApplyModifiedProperties();
                
                int hash = GetTargetHash();
                if (changed || _dirty || hash != _targetHash)
                {
                    Scan();
                    _targetHash = hash;
                    _dirty = false;
                }
            });
        }
        
        private int GetTargetHash()
        {
            int hash = _targets.Count;
            foreach (var t in _targets.Where(t => t != null))
                hash = hash * 31 + t.GetInstanceID();
            return hash;
        }
        
        private void DrawTabs()
        {
            EditorGUILayout.Space(4);
            var bg = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();
            
            float w = (EditorGUIUtility.currentViewWidth - 60) / TabNames.Length;
            
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool sel = (int)_mode == i;
                GUI.backgroundColor = sel ? new Color(0.8f, 0.85f, 1f) : bg;
                
                var style = i == 0 ? EditorStyles.miniButtonLeft : 
                           i == TabNames.Length - 1 ? EditorStyles.miniButtonRight : 
                           EditorStyles.miniButtonMid;
                
                if (GUILayout.Button(TabTips[i], style, GUILayout.Width(w)) && !sel)
                {
                    _mode = (Mode)i;
                    GUI.FocusControl(null);
                    Scan();
                    _scroll = Vector2.zero;
                }
            }
            
            GUI.backgroundColor = bg;
            
            if (GUILayout.Button(new GUIContent("スキャン", "再スキャン"), EditorStyles.miniButton, GUILayout.Width(50)))
            {
                Scan();
                Repaint();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawMaterialTabs()
        {
            EditorGUILayout.Space(2);
            var bg = GUI.backgroundColor;
            
            Box(SubTabCol, () => 
            {
                EditorGUILayout.BeginHorizontal();
                
                float w = (EditorGUIUtility.currentViewWidth - 40) / MaterialTabNames.Length;
                
                for (int i = 0; i < MaterialTabNames.Length; i++)
                {
                    bool sel = (int)_materialMode == i;
                    GUI.backgroundColor = sel ? new Color(0.9f, 0.95f, 1f) : new Color(0.8f, 0.8f, 0.8f);
                    
                    var style = i == 0 ? EditorStyles.miniButtonLeft : EditorStyles.miniButtonRight;
                    
                    if (GUILayout.Button(MaterialTabTips[i], style, GUILayout.Width(w)) && !sel)
                    {
                        _materialMode = (MaterialMode)i;
                        GUI.FocusControl(null);
                        _scroll = Vector2.zero;
                        _materialListScroll = Vector2.zero;
                        _propertyScroll = Vector2.zero;
                        if (_materialMode == MaterialMode.Copy)
                        {
                            UpdateAvailableProperties();
                        }
                    }
                }
                
                EditorGUILayout.EndHorizontal();
            });
            
            GUI.backgroundColor = bg;
        }
        
        private void DrawBlendShapeTabs()
        {
            EditorGUILayout.Space(2);
            var bg = GUI.backgroundColor;
            
            Box(SubTabCol, () => 
            {
                EditorGUILayout.BeginHorizontal();
                
                float w = (EditorGUIUtility.currentViewWidth - 40) / BlendShapeTabNames.Length;
                
                for (int i = 0; i < BlendShapeTabNames.Length; i++)
                {
                    bool sel = (int)_blendShapeMode == i;
                    GUI.backgroundColor = sel ? new Color(0.9f, 0.95f, 1f) : new Color(0.8f, 0.8f, 0.8f);
                    
                    var style = i == 0 ? EditorStyles.miniButtonLeft : EditorStyles.miniButtonRight;
                    
                    if (GUILayout.Button(BlendShapeTabTips[i], style, GUILayout.Width(w)) && !sel)
                    {
                        _blendShapeMode = (BlendShapeMode)i;
                        GUI.FocusControl(null);
                        _scroll = Vector2.zero;
                        if (_blendShapeMode == BlendShapeMode.Compose)
                        {
                            ScanCompose();
                        }
                    }
                }
                
                EditorGUILayout.EndHorizontal();
            });
            
            GUI.backgroundColor = bg;
        }
        
        private void DrawMaterialReplace()
        {
            Box(HeaderCol, () => EditorGUILayout.LabelField("マテリアル置換", EditorStyles.boldLabel));
            
            if (_mats.Count == 0)
            {
                EditorGUILayout.HelpBox("マテリアルが見つかりません。", MessageType.Info);
                return;
            }
            
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            
            foreach (var mat in _mats)
            {
                Box(ContentCol, () => 
                {
                    EditorGUI.BeginChangeCheck();
                    var newMat = (Material)EditorGUILayout.ObjectField(mat, typeof(Material), false);
                    
                    if (EditorGUI.EndChangeCheck() && newMat != null && newMat != mat)
                        ReplaceMat(mat, newMat);
                });
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawMaterialCopy()
        {
            // 1. ベースマテリアル選択
            Box(BaseCol, () => 
            {
                EditorGUILayout.LabelField("ベースマテリアル（コピー元）", EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                _baseMaterial = (Material)EditorGUILayout.ObjectField("ベースマテリアル", _baseMaterial, typeof(Material), false);
                
                if (EditorGUI.EndChangeCheck())
                {
                    UpdateAvailableProperties();
                }
            });
            
            // 2. ターゲットマテリアル選択
            Box(CopyCol, () => 
            {
                EditorGUILayout.LabelField("ターゲットマテリアル（コピー先）", EditorStyles.boldLabel);
                
                if (_targetMaterials.Count == 0)
                {
                    EditorGUILayout.LabelField("下のマテリアル一覧から「追加」ボタンで選択");
                }
                else
                {
                    for (int i = _targetMaterials.Count - 1; i >= 0; i--)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("×", GUILayout.Width(20)))
                            {
                                _targetMaterials.RemoveAt(i);
                                continue;
                            }
                            
                            EditorGUILayout.ObjectField(_targetMaterials[i], typeof(Material), false);
                        }
                    }
                    
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("全クリア", GUILayout.Width(80)))
                        {
                            _targetMaterials.Clear();
                        }
                    }
                }
            });
            
            // 3. 横並びレイアウト：プロパティ選択とマテリアル一覧
            using (new EditorGUILayout.HorizontalScope())
            {
                // 左側：プロパティ選択
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.5f)))
                {
                    if (_baseMaterial != null && _availableGroups.Count > 0)
                    {
                        Box(SelectCol, () => 
                        {
                            EditorGUILayout.LabelField("コピーするプロパティ", EditorStyles.boldLabel);
                            
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                if (GUILayout.Button("全選択", GUILayout.Width(80)))
                                {
                                    SelectAllProperties(true);
                                }
                                
                                if (GUILayout.Button("全解除", GUILayout.Width(80)))
                                {
                                    SelectAllProperties(false);
                                }
                            }
                            
                            EditorGUILayout.Space(5);
                            
                            // 横スクロールを削除し、固定の高さでスクロールビューを作成
                            _propertyScroll = EditorGUILayout.BeginScrollView(_propertyScroll, GUILayout.Height(300));
                            
                            DrawPropertyGroups();
                            
                            EditorGUILayout.EndScrollView();
                        });
                    }
                    else if (_baseMaterial == null)
                    {
                        EditorGUILayout.HelpBox("ベースマテリアルを選択してください。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("利用可能なプロパティがありません。", MessageType.Warning);
                    }
                }
                
                // 右側：マテリアル一覧
                using (new EditorGUILayout.VerticalScope())
                {
                    if (_mats.Count > 0)
                    {
                        Box(ContentCol, () => 
                        {
                            EditorGUILayout.LabelField("マテリアル一覧", EditorStyles.boldLabel);
                            EditorGUILayout.LabelField("下のボタンでベース・ターゲットマテリアルを選択");
                            
                            EditorGUILayout.Space(5);
                            
                            _materialListScroll = EditorGUILayout.BeginScrollView(
                                _materialListScroll, 
                                GUILayout.Height(300)
                            );
                            
                            foreach (var mat in _mats)
                            {
                                if (mat == null) continue;
                                
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    // ベース選択ボタン
                                    bool isBase = mat == _baseMaterial;
                                    GUI.enabled = !isBase;
                                    if (GUILayout.Button(isBase ? "ベース中" : "ベース", GUILayout.Width(60)))
                                    {
                                        _baseMaterial = mat;
                                        UpdateAvailableProperties();
                                    }
                                    GUI.enabled = true;
                                    
                                    // ターゲット選択ボタン
                                    bool isTarget = _targetMaterials.Contains(mat);
                                    GUI.enabled = !isTarget && mat != _baseMaterial;
                                    if (GUILayout.Button(isTarget ? "選択済" : "追加", GUILayout.Width(60)))
                                    {
                                        if (!_targetMaterials.Contains(mat))
                                        {
                                            _targetMaterials.Add(mat);
                                        }
                                    }
                                    GUI.enabled = true;
                                    
                                    // マテリアル名とプレビュー
                                    EditorGUILayout.ObjectField(mat, typeof(Material), false);
                                }
                            }
                            
                            EditorGUILayout.EndScrollView();
                        });
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("マテリアルが見つかりません。", MessageType.Info);
                    }
                }
            }
            
            EditorGUILayout.Space();
            
            // 4. 実行ボタン
            Box(HeaderCol, () => 
            {
                bool canCopy = _baseMaterial != null && _targetMaterials.Count > 0 && 
                              _propertySelections.Any(p => p.Value);
                
                GUI.enabled = canCopy;
                if (GUILayout.Button($"マテリアルプロパティをコピー ({_targetMaterials.Count}個)", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("確認", 
                        $"「{_baseMaterial.name}」から{_targetMaterials.Count}個のマテリアルに選択したプロパティをコピーしますか？", 
                        "コピー", "キャンセル"))
                    {
                        CopyMaterialProperties();
                    }
                }
                GUI.enabled = true;
                
                if (!canCopy)
                {
                    if (_baseMaterial == null)
                    {
                        EditorGUILayout.HelpBox("ベースマテリアルを選択してください。", MessageType.Warning);
                    }
                    else if (_targetMaterials.Count == 0)
                    {
                        EditorGUILayout.HelpBox("ターゲットマテリアルを選択してください。", MessageType.Warning);
                    }
                    else if (!_propertySelections.Any(p => p.Value))
                    {
                        EditorGUILayout.HelpBox("コピーするプロパティを選択してください。", MessageType.Warning);
                    }
                }
            });
        }
        
        private void DrawPropertyGroups()
        {
            foreach (var group in _availableGroups)
            {
                if (group.HasParent) continue; // 親グループのみ最初に表示
                
                DrawPropertyGroup(group);
            }
        }
        
        private void DrawPropertyGroup(PropertyGroup group)
        {
            var childGroups = _availableGroups.Where(g => g.HasParent && g.ParentId == group.Name).ToList();
            var allProperties = GetAllGroupProperties(group);
            
            if (allProperties.Count == 0) return;
            
            Box(GroupCol, () => 
            {
                // 親グループの折りたたみ状態を取得
                if (!_groupFoldouts.TryGetValue(group.Name, out bool expanded))
                    _groupFoldouts[group.Name] = expanded = true;
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 親グループの一括選択チェックボックス（左端）
                    if (childGroups.Count > 0)
                    {
                        bool allSelected = allProperties.All(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
                        bool anySelected = allProperties.Any(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
                        
                        EditorGUI.showMixedValue = anySelected && !allSelected;
                        bool newState = EditorGUILayout.Toggle(allSelected, GUILayout.Width(15));
                        EditorGUI.showMixedValue = false;
                        
                        if (newState != allSelected)
                        {
                            foreach (var prop in allProperties)
                            {
                                _propertySelections[prop.Name] = newState;
                            }
                        }
                    }
                    else
                    {
                        // 子グループがない場合は空白を入れてレイアウトを合わせる
                        GUILayout.Space(15);
                    }
                    
                    // グループ名
                    EditorGUILayout.LabelField(group.DisplayName, EditorStyles.boldLabel, GUILayout.Width(80));
                    
                    // 右端に固定表示の折りたたみボタン
                    GUILayout.FlexibleSpace();
                    string arrow = expanded ? "▽" : "▷";
                    if (GUILayout.Button(arrow, EditorStyles.label, GUILayout.Width(15)))
                    {
                        _groupFoldouts[group.Name] = !expanded;
                    }
                }
                
                if (!_groupFoldouts[group.Name]) return;
                
                EditorGUI.indentLevel++;
                
                // 子グループの表示
                foreach (var childGroup in childGroups)
                {
                    DrawChildGroup(childGroup);
                }
                
                // 直接のプロパティがある場合
                foreach (var prop in group.Properties)
                {
                    if (_baseMaterial.HasProperty(prop.Name))
                    {
                        if (!_propertySelections.ContainsKey(prop.Name))
                            _propertySelections[prop.Name] = false;
                        
                        _propertySelections[prop.Name] = EditorGUILayout.ToggleLeft(prop.DisplayName, _propertySelections[prop.Name]);
                    }
                }
                
                EditorGUI.indentLevel--;
            });
        }
        
        private void DrawChildGroup(PropertyGroup childGroup)
        {
            var availableProps = childGroup.Properties.Where(p => _baseMaterial.HasProperty(p.Name)).ToList();
            if (availableProps.Count == 0) return;
            
            // 子グループの折りたたみ状態を取得（デフォルトは折りたたみ）
            if (!_childGroupFoldouts.TryGetValue(childGroup.Name, out bool childExpanded))
                _childGroupFoldouts[childGroup.Name] = childExpanded = false;
            
            using (new EditorGUILayout.HorizontalScope())
            {
                // 子グループの一括選択チェックボックス（左端、親と同じ幅）
                bool allSelected = availableProps.All(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
                bool anySelected = availableProps.Any(prop => _propertySelections.GetValueOrDefault(prop.Name, false));
                
                EditorGUI.showMixedValue = anySelected && !allSelected;
                bool newState = EditorGUILayout.ToggleLeft(childGroup.DisplayName, allSelected, EditorStyles.boldLabel);
                EditorGUI.showMixedValue = false;
                
                if (newState != allSelected)
                {
                    foreach (var prop in availableProps)
                    {
                        _propertySelections[prop.Name] = newState;
                    }
                }
                
                // 右端に固定表示の折りたたみボタン
                GUILayout.FlexibleSpace();
                string arrow = childExpanded ? "▽" : "▷";
                if (GUILayout.Button(arrow, EditorStyles.label, GUILayout.Width(15)))
                {
                    _childGroupFoldouts[childGroup.Name] = !childExpanded;
                }
            }
            
            // 折りたたまれている場合は子項目を表示しない
            if (!_childGroupFoldouts[childGroup.Name]) return;
            
            EditorGUI.indentLevel++;
            foreach (var prop in availableProps)
            {
                if (!_propertySelections.ContainsKey(prop.Name))
                    _propertySelections[prop.Name] = false;
                
                _propertySelections[prop.Name] = EditorGUILayout.ToggleLeft(prop.DisplayName, _propertySelections[prop.Name]);
            }
            EditorGUI.indentLevel--;
        }
        
        private List<PropertyItem> GetAllGroupProperties(PropertyGroup group)
        {
            var allProps = new List<PropertyItem>(group.Properties);
            
            var childGroups = _availableGroups.Where(g => g.HasParent && g.ParentId == group.Name);
            foreach (var childGroup in childGroups)
            {
                allProps.AddRange(childGroup.Properties);
            }
            
            return allProps.Where(prop => _baseMaterial != null && _baseMaterial.HasProperty(prop.Name)).ToList();
        }
        
        private void SelectAllProperties(bool select)
        {
            foreach (var group in _availableGroups)
            {
                foreach (var prop in group.Properties)
                {
                    if (_baseMaterial.HasProperty(prop.Name))
                    {
                        _propertySelections[prop.Name] = select;
                    }
                }
            }
        }
        
        private void UpdateAvailableProperties()
        {
            _propertySelections.Clear();
            _availableGroups.Clear();
            
            if (_baseMaterial == null) return;
            
            foreach (var group in PropertyGroups)
            {
                var availableGroup = new PropertyGroup(group.Name, group.DisplayName, group.HasParent, group.ParentId);
                
                foreach (var prop in group.Properties)
                {
                    if (_baseMaterial.HasProperty(prop.Name))
                    {
                        availableGroup.Properties.Add(prop);
                        _propertySelections[prop.Name] = false;
                    }
                }
                
                // プロパティがあるグループのみ追加
                if (availableGroup.Properties.Count > 0 || 
                    (!group.HasParent && PropertyGroups.Any(g => g.HasParent && g.ParentId == group.Name && 
                    g.Properties.Any(p => _baseMaterial.HasProperty(p.Name)))))
                {
                    _availableGroups.Add(availableGroup);
                }
            }
        }
        
        private void CopyMaterialProperties()
        {
            if (_baseMaterial == null || _targetMaterials.Count == 0) return;
            
            int totalCopied = 0;
            
            foreach (var targetMaterial in _targetMaterials)
            {
                if (targetMaterial == null) continue;
                
                Undo.RecordObject(targetMaterial, "Copy Material Properties");
                
                int copiedCount = 0;
                foreach (var kvp in _propertySelections)
                {
                    if (!kvp.Value) continue;
                    
                    string propName = kvp.Key;
                    if (!_baseMaterial.HasProperty(propName) || !targetMaterial.HasProperty(propName))
                        continue;
                    
                    var shader = _baseMaterial.shader;
                    int propIndex = shader.FindPropertyIndex(propName);
                    if (propIndex < 0) continue;
                    
                    var propType = shader.GetPropertyType(propIndex);
                    
                    try
                    {
                        switch (propType)
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Color:
                                targetMaterial.SetColor(propName, _baseMaterial.GetColor(propName));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                targetMaterial.SetVector(propName, _baseMaterial.GetVector(propName));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Float:
                            case UnityEngine.Rendering.ShaderPropertyType.Range:
                                targetMaterial.SetFloat(propName, _baseMaterial.GetFloat(propName));
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Int:
                                targetMaterial.SetInt(propName, _baseMaterial.GetInt(propName));
                                break;
                            // テクスチャは意図的にコピーしない
                        }
                        copiedCount++;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"プロパティ {propName} のコピーに失敗 ({targetMaterial.name}): {e.Message}");
                    }
                }
                
                EditorUtility.SetDirty(targetMaterial);
                totalCopied += copiedCount;
            }
            
            EditorUtility.DisplayDialog("完了", 
                $"{_targetMaterials.Count}個のマテリアルに合計{totalCopied}個のプロパティをコピーしました。\n" +
                $"ベース: {_baseMaterial.name}", "OK");
        }
        
        private void DrawBlendShapeSearch()
        {
            // 検索ボックス
            Box(HeaderCol, () => _search = EditorGUILayout.TextField("検索", _search));
            
            // シェイプキー一覧（スクロール）
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            
            foreach (var smr in _smrs.Where(s => s?.sharedMesh != null))
            {
                if (!string.IsNullOrEmpty(_search) && !HasShape(smr, _search))
                    continue;
                    
                DrawSmr(smr);
            }
            
            EditorGUILayout.EndScrollView();
            
            // 折りたたみコントロール（スクロール下の固定位置）
            Box(SelectCol, () => 
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    
                    if (GUILayout.Button("展開", GUILayout.Width(60)))
                        ExpandAllFoldouts(true);
                        
                    if (GUILayout.Button("折り畳む", GUILayout.Width(80)))
                        ExpandAllFoldouts(false);
                        
                    GUILayout.FlexibleSpace();
                }
            });
        }
        
        private void DrawSmr(SkinnedMeshRenderer smr)
        {
            if (!_foldouts.TryGetValue(smr, out bool exp)) 
                _foldouts[smr] = exp = true;
            
            Box(ContentCol, () => 
            {
                _foldouts[smr] = EditorGUILayout.Foldout(exp, smr.name, true, EditorStyles.foldoutHeader);
                if (!_foldouts[smr]) return;
                
                EditorGUI.indentLevel++;
                var mesh = smr.sharedMesh;
                
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (!string.IsNullOrEmpty(_search) && !name.Contains(_search, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                        
                    DrawShapeSlider(smr, i, name);
                }
                EditorGUI.indentLevel--;
            });
        }
        
        private void DrawShapeSlider(SkinnedMeshRenderer smr, int idx, string name)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(name, GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.5f));
                
                float val = smr.GetBlendShapeWeight(idx);
                float newVal = EditorGUILayout.Slider(val, 0f, 100f);
                
                if (!Mathf.Approximately(newVal, val))
                {
                    Undo.RecordObject(smr, "Change BlendShape");
                    smr.SetBlendShapeWeight(idx, newVal);
                    EditorUtility.SetDirty(smr);
                }
            }
        }
        
        private void DrawScale()
        {
            bool valid = _targets.All(t => t?.GetComponent<ModularAvatarMeshSettings>() != null);
            
            Box(HeaderCol, () => 
            {
                EditorGUILayout.LabelField("スケール調整", EditorStyles.boldLabel);
                
                if (!valid)
                {
                    EditorGUILayout.HelpBox("SetupOutfitした衣装を入れてください。", MessageType.Error);
                    return;
                }
                
                if (_outfitBones.Count == 0)
                {
                    EditorGUILayout.HelpBox("衣装のボーンが見つかりません。", MessageType.Error);
                    return;
                }
            });
            
            if (!valid || _outfitBones.Count == 0) return;
            
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            
            foreach (var bone in BoneOrder)
            {
                if (!_boneFolds.TryGetValue(bone, out bool exp))
                    _boneFolds[bone] = exp = true;
                
                Box(ContentCol, () => 
                {
                    var style = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
                    _boneFolds[bone] = EditorGUILayout.Foldout(exp, bone, true, style);
                    if (!_boneFolds[bone]) return;
                    
                    EditorGUI.indentLevel++;
                    
                    if (_avatarBones.TryGetValue(bone, out var avBone))
                        DrawBoneScale("素体", avBone);
                    else
                        EditorGUILayout.LabelField("素体にボーンなし");
                    
                    var rect = EditorGUILayout.GetControlRect(false, 1);
                    EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
                    
                    foreach (var outfit in _targets.Where(t => t != null))
                    {
                        if (_outfitBones.TryGetValue(outfit, out var map) && map.TryGetValue(bone, out var ob))
                            DrawBoneScale("衣装", ob);
                        else
                            EditorGUILayout.LabelField($"「{outfit.name}」にボーンなし");
                    }
                    
                    EditorGUI.indentLevel--;
                });
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(10);
            
            Box(HeaderCol, () => 
            {
                _so.Update();
                EditorGUILayout.PropertyField(_armatureProp, new GUIContent("素体Armature"));
                _so.ApplyModifiedProperties();
                
                var arm = _armatureProp.objectReferenceValue as Transform;
                if (arm != _avatarArmature)
                {
                    _avatarArmature = arm;
                    ScanBones();
                }
                
                if (_avatarArmature == null)
                    EditorGUILayout.HelpBox("素体のArmatureを設定してください。", MessageType.Warning);
            });
            
            EditorGUILayout.Space(5);
            
            Box(SelectCol, () => 
            {
                if (GUILayout.Button("衣装のスケールを身体に合わせる", GUILayout.Height(30)))
                    SyncScales();
            });
        }
        
        private void DrawBoneScale(string label, Transform bone)
        {
            var adj = bone.GetComponent<ModularAvatarScaleAdjuster>();
            
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(label, EditorStyles.label, GUILayout.Width(30)))
                {
                    Selection.activeTransform = bone;
                    EditorGUIUtility.PingObject(bone);
                }
                
                if (adj == null)
                {
                    if (GUILayout.Button("ScaleAdjusterを追加"))
                        Undo.AddComponent<ModularAvatarScaleAdjuster>(bone.gameObject);
                }
                else
                {
                    var s = adj.Scale;
                    EditorGUI.BeginChangeCheck();
                    
                    float w = (EditorGUIUtility.currentViewWidth - 80) / 3;
                    EditorGUIUtility.labelWidth = 26;
                    
                    float x = EditorGUILayout.FloatField("X", s.x, GUILayout.Width(w));
                    float y = EditorGUILayout.FloatField("Y", s.y, GUILayout.Width(w));
                    float z = EditorGUILayout.FloatField("Z", s.z, GUILayout.Width(w));
                    
                    EditorGUIUtility.labelWidth = 0;
                    
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(adj, "Change Scale");
                        adj.Scale = new Vector3(x, y, z);
                        EditorUtility.SetDirty(adj);
                    }
                }
            }
        }
        
        private void DrawBlendShapeCompose()
        {
            ScanCompose();
            
            // 1. 対象メッシュ
            Box(HeaderCol, () => 
            {
                EditorGUILayout.LabelField("シェイプキー合成", EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                _composeTarget = EditorGUILayout.ObjectField(
                    new GUIContent("対象メッシュ", "合成対象のSkinnedMeshRenderer"), 
                    _composeTarget, typeof(SkinnedMeshRenderer), true) as SkinnedMeshRenderer;
                
                if (EditorGUI.EndChangeCheck())
                {
                    _composeShapes.Clear();
                    _baseShapeName = "";
                    _newShapeName = "";
                    ScanCompose();
                }
            });
            
            // 2. ベースシェイプキー（一覧から選択）
            Box(BaseCol, () => 
            {
                EditorGUILayout.LabelField("ベースシェイプキー", EditorStyles.boldLabel);
                if (string.IsNullOrEmpty(_baseShapeName))
                {
                    EditorGUILayout.LabelField("下の一覧から「ベース」ボタンを押して選択");
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("選択中: " + _baseShapeName);
                        if (GUILayout.Button("クリア", GUILayout.Width(60)))
                        {
                            _baseShapeName = "";
                            if (_overwriteShape)
                                _newShapeName = "";
                        }
                    }
                }
                
                // 上書き設定をベースシェイプキー欄の直下に移動
                EditorGUI.BeginChangeCheck();
                _overwriteShape = EditorGUILayout.Toggle(new GUIContent("シェイプキーを上書きする", "チェックを入れるとベースシェイプキーを上書きします"), _overwriteShape);
                
                if (EditorGUI.EndChangeCheck())
                {
                    if (_overwriteShape && !string.IsNullOrEmpty(_baseShapeName))
                        _newShapeName = _baseShapeName;
                    else if (!_overwriteShape)
                        _newShapeName = string.IsNullOrEmpty(_baseShapeName) ? "" : _baseShapeName + "_合成";
                }
                
                if (!_overwriteShape)
                {
                    _newShapeName = EditorGUILayout.TextField(new GUIContent("新しい名前", "新しいシェイプキー名"), _newShapeName);
                }
                else
                {
                    EditorGUILayout.LabelField("上書き対象", string.IsNullOrEmpty(_baseShapeName) ? "未選択" : _baseShapeName);
                }
            });
            
            // 3. 横並びレイアウト：合成するシェイプキーとシェイプキー選択リスト
            using (new EditorGUILayout.HorizontalScope())
            {
                // 左側：合成するシェイプキー
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.5f)))
                {
                    Box(SelectCol, () => 
                    {
                        EditorGUILayout.LabelField("合成するシェイプキー", EditorStyles.boldLabel);
                        
                        if (_composeShapes.Count == 0)
                        {
                            EditorGUILayout.LabelField("右の一覧から「追加」ボタンを押して選択");
                        }
                        else
                        {
                            _composeShapeScroll = EditorGUILayout.BeginScrollView(_composeShapeScroll, GUILayout.Height(300));
                            
                            for (int i = _composeShapes.Count - 1; i >= 0; i--)
                            {
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    if (GUILayout.Button("×", GUILayout.Width(20)))
                                    {
                                        _composeShapes.RemoveAt(i);
                                        continue;
                                    }
                                    
                                    var item = _composeShapes[i];
                                    EditorGUILayout.LabelField(item.name, GUILayout.Width(120));
                                    
                                    float w = EditorGUILayout.Slider(item.weight, -100f, 100f);
                                    if (!Mathf.Approximately(w, item.weight))
                                        _composeShapes[i] = (item.name, w);
                                }
                            }
                            
                            EditorGUILayout.EndScrollView();
                        }
                    });
                }
                
                // 右側：シェイプキー選択リスト
                using (new EditorGUILayout.VerticalScope())
                {
                    Box(ContentCol, () => 
                    {
                        EditorGUILayout.LabelField("シェイプキー一覧", EditorStyles.boldLabel);
                        
                        // 検索ボックス
                        _composeSearch = EditorGUILayout.TextField("検索", _composeSearch);
                        
                        EditorGUILayout.Space(5);
                        
                        _shapeListScroll = EditorGUILayout.BeginScrollView(_shapeListScroll, GUILayout.Height(300));
                        DrawShapeSelectionList();
                        EditorGUILayout.EndScrollView();
                    });
                }
            }
            
            EditorGUILayout.Space();
            
            // 実行ボタン
            Box(HeaderCol, () => 
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("全クリア", GUILayout.Height(30), GUILayout.Width(80)))
                    {
                        _composeShapes.Clear();
                        _baseShapeName = "";
                        _newShapeName = "";
                    }
                    
                    GUILayout.FlexibleSpace();
                    
                    bool canCompose = !string.IsNullOrEmpty(_baseShapeName) && 
                                     (_overwriteShape || !string.IsNullOrEmpty(_newShapeName));
                    
                    GUI.enabled = canCompose;
                    if (GUILayout.Button("合成実行", GUILayout.Height(30), GUILayout.Width(150)))
                        ComposeNew();
                    GUI.enabled = true;
                }
            });
        }
        
        private void DrawShapeSelectionList()
        {
            if (_shapeNames.Count == 0) 
            {
                EditorGUILayout.LabelField("シェイプキーがありません");
                return;
            }
            
            foreach (var name in _shapeNames)
            {
                if (!string.IsNullOrEmpty(_composeSearch) && 
                    !name.Contains(_composeSearch, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    // ベース選択ボタン
                    bool isBase = name == _baseShapeName;
                    GUI.enabled = !isBase;
                    if (GUILayout.Button(isBase ? "ベース中" : "ベース", GUILayout.Width(60)))
                    {
                        _baseShapeName = name;
                        if (_overwriteShape)
                            _newShapeName = name;
                    }
                    GUI.enabled = true;
                    
                    // 追加ボタン（何度でも追加可能）
                    if (GUILayout.Button("追加", GUILayout.Width(60)))
                    {
                        _composeShapes.Add((name, 100f));
                    }
                    
                    EditorGUILayout.LabelField(name);
                }
            }
        }
        
        private void ComposeNew()
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
            
            var orig = _composeTarget.sharedMesh;
            
            // 上書きではない場合の名前重複チェック
            if (!_overwriteShape)
            {
                for (int i = 0; i < orig.blendShapeCount; i++)
                {
                    if (orig.GetBlendShapeName(i) == targetName)
                    {
                        if (!EditorUtility.DisplayDialog("警告", $"「{targetName}」は既に存在します。続行しますか？", "続行", "キャンセル"))
                            return;
                        break;
                    }
                }
            }
            
            try
            {
                EditorUtility.DisplayProgressBar("合成中", "メッシュ処理中...", 0f);
                
                var newMesh = CreateComposedNew(orig, targetName);
                if (newMesh == null) return;
                
                EditorUtility.DisplayProgressBar("合成中", "保存中...", 0.8f);
                
                string path = SaveMesh(newMesh, targetName);
                if (string.IsNullOrEmpty(path)) return;
                
                EditorUtility.DisplayProgressBar("合成中", "適用中...", 0.9f);
                
                Undo.RecordObject(_composeTarget, "Compose BlendShapes");
                _composeTarget.sharedMesh = newMesh;
                EditorUtility.SetDirty(_composeTarget);
                
                EditorUtility.DisplayDialog("完了", $"「{targetName}」を合成しました。\n{path}", "OK");
                
                // 合成完了後、データをリフレッシュ
                ScanCompose();
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
        
        private Mesh CreateComposedNew(Mesh orig, string targetName)
        {
            var mesh = Object.Instantiate(orig);
            mesh.name = $"{orig.name}_Composed";
            
            // ベースシェイプキーのインデックスを取得
            int baseIdx = -1;
            for (int i = 0; i < orig.blendShapeCount; i++)
            {
                if (orig.GetBlendShapeName(i) == _baseShapeName)
                {
                    baseIdx = i;
                    break;
                }
            }
            
            if (baseIdx < 0)
            {
                EditorUtility.DisplayDialog("エラー", $"ベースシェイプキー「{_baseShapeName}」が見つかりません。", "OK");
                return null;
            }
            
            var verts = orig.vertices;
            var norms = orig.normals;
            var tans = orig.tangents;
            
            // ベースシェイプキーを適用した状態を作成
            var baseVerts = new Vector3[verts.Length];
            var baseNorms = new Vector3[norms.Length];
            var baseTans = new Vector3[tans.Length];
            
            System.Array.Copy(verts, baseVerts, verts.Length);
            System.Array.Copy(norms, baseNorms, norms.Length);
            for (int i = 0; i < tans.Length; i++)
                baseTans[i] = tans[i];
            
            // ベースシェイプキーを100%適用
            var baseDv = new Vector3[verts.Length];
            var baseDn = new Vector3[norms.Length];
            var baseDt = new Vector3[tans.Length];
            
            orig.GetBlendShapeFrameVertices(baseIdx, 0, baseDv, baseDn, baseDt);
            
            for (int i = 0; i < verts.Length; i++)
            {
                baseVerts[i] += baseDv[i];
                baseNorms[i] += baseDn[i];
                baseTans[i] += baseDt[i];
            }
            
            float prog = 0.2f;
            float step = 0.6f / _composeShapes.Count;
            
            // 追加シェイプキーを適用
            foreach (var (name, weight) in _composeShapes)
            {
                EditorUtility.DisplayProgressBar("合成中", $"処理中: {name}", prog);
                
                int idx = -1;
                for (int i = 0; i < orig.blendShapeCount; i++)
                {
                    if (orig.GetBlendShapeName(i) == name)
                    {
                        idx = i;
                        break;
                    }
                }
                
                if (idx >= 0)
                {
                    var dv = new Vector3[verts.Length];
                    var dn = new Vector3[norms.Length];
                    var dt = new Vector3[tans.Length];
                    
                    orig.GetBlendShapeFrameVertices(idx, 0, dv, dn, dt);
                    
                    float mult = weight / 100f;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        baseVerts[i] += dv[i] * mult;
                        baseNorms[i] += dn[i] * mult;
                        baseTans[i] += dt[i] * mult;
                    }
                }
                
                prog += step;
            }
            
            // 元の形状との差分を計算
            var finalDv = new Vector3[verts.Length];
            var finalDn = new Vector3[norms.Length];
            var finalDt = new Vector3[tans.Length];
            
            for (int i = 0; i < verts.Length; i++)
            {
                finalDv[i] = baseVerts[i] - verts[i];
                finalDn[i] = baseNorms[i] - norms[i];
                finalDt[i] = baseTans[i] - new Vector3(tans[i].x, tans[i].y, tans[i].z);
            }
            
            // 上書きの場合は既存のシェイプキーを更新、そうでなければ新規追加
            if (_overwriteShape)
            {
                // 既存のシェイプキーを削除して再追加（Unity制限のため）
                var tempMesh = new Mesh();
                tempMesh.vertices = mesh.vertices;
                tempMesh.triangles = mesh.triangles;
                tempMesh.normals = mesh.normals;
                tempMesh.tangents = mesh.tangents;
                tempMesh.uv = mesh.uv;
                tempMesh.name = mesh.name;
                
                // 他のシェイプキーをコピー
                for (int i = 0; i < orig.blendShapeCount; i++)
                {
                    string shapeName = orig.GetBlendShapeName(i);
                    if (shapeName == targetName)
                    {
                        // 新しい合成結果で置き換え
                        tempMesh.AddBlendShapeFrame(shapeName, 100f, finalDv, finalDn, finalDt);
                    }
                    else
                    {
                        // 既存のシェイプキーをコピー
                        var dv = new Vector3[verts.Length];
                        var dn = new Vector3[norms.Length];
                        var dt = new Vector3[tans.Length];
                        orig.GetBlendShapeFrameVertices(i, 0, dv, dn, dt);
                        tempMesh.AddBlendShapeFrame(shapeName, 100f, dv, dn, dt);
                    }
                }
                
                return tempMesh;
            }
            else
            {
                // 新規追加
                mesh.AddBlendShapeFrame(targetName, 100f, finalDv, finalDn, finalDt);
                return mesh;
            }
        }
        
        private string SaveMesh(Mesh mesh, string shapeName)
        {
            string saveDir = "Assets/qsyi/GeneratedMeshes";
            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
                AssetDatabase.Refresh();
            }
            
            string file = $"{mesh.name}_{shapeName}.asset";
            string path = Path.Combine(saveDir, file);
            
            int cnt = 1;
            while (File.Exists(path))
            {
                file = $"{mesh.name}_{shapeName}_{cnt++}.asset";
                path = Path.Combine(saveDir, file);
            }
            
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            return path;
        }
        
        // Scan Functions - モード別に最適化
        private void Scan()
        {
            switch (_mode)
            {
                case Mode.Material: ScanMats(); break;
                case Mode.BlendShape: 
                    ScanSmrs();
                    if (_blendShapeMode == BlendShapeMode.Compose)
                        ScanCompose();
                    break;
                case Mode.Scale: ScanBones(); break;
            }
        }
        
        private void ScanSmrs()
        {
            _smrs.Clear();
            var oldFolds = new Dictionary<Object, bool>(_foldouts);
            _foldouts.Clear();
            
            foreach (var go in _targets.Where(t => t != null && !t.CompareTag("EditorOnly")))
            {
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh?.blendShapeCount > 0)
                    {
                        _smrs.Add(smr);
                        _foldouts[smr] = oldFolds.TryGetValue(smr, out bool f) ? f : true;
                    }
                }
            }
        }
        
        private void ScanMats()
        {
            _mats.Clear();
            _matUsage.Clear();
            
            foreach (var go in _targets.Where(t => t != null && !t.CompareTag("EditorOnly")))
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null) continue;
                        
                        if (!_matUsage.TryGetValue(mat, out var list))
                        {
                            list = new List<(Renderer, int)>();
                            _matUsage[mat] = list;
                            _mats.Add(mat);
                        }
                        list.Add((r, i));
                    }
                }
            }
        }
        
        private void ScanBones()
        {
            _outfitBones.Clear();
            _avatarBones.Clear();
            
            if (_avatarArmature == null)
            {
                _avatarArmature = FindAvatarArm();
                if (_avatarArmature != null)
                {
                    _so.Update();
                    _armatureProp.objectReferenceValue = _avatarArmature;
                    _so.ApplyModifiedProperties();
                }
            }

            if (_avatarArmature != null)
                BuildBoneMap(_avatarArmature, _avatarBones);

            foreach (var outfit in _targets.Where(t => t != null))
            {
                var arm = FindArm(outfit.transform);
                if (arm != null)
                {
                    var map = new Dictionary<string, Transform>();
                    BuildBoneMap(arm, map);
                    if (map.Count > 0)
                        _outfitBones[outfit] = map;
                }
            }
        }
        
        private void ScanCompose()
        {
            AutoUpdateComposeTarget();
            
            _shapeNames.Clear();
            
            if (_composeTarget?.sharedMesh != null)
            {
                var mesh = _composeTarget.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    _shapeNames.Add(mesh.GetBlendShapeName(i));
            }
        }
        
        private void AutoUpdateComposeTarget()
        {
            bool needsUpdate = false;
            
            if (_composeTarget == null)
            {
                needsUpdate = true;
            }
            else if (!_smrs.Contains(_composeTarget))
            {
                needsUpdate = true;
            }
            else if (_composeTarget.sharedMesh?.blendShapeCount == 0)
            {
                needsUpdate = true;
            }
            
            if (needsUpdate)
            {
                var newTarget = GetFirstSmr();
                if (newTarget != null && newTarget != _composeTarget)
                {
                    _composeTarget = newTarget;
                    _composeShapes.Clear();
                    _baseShapeName = "";
                    _newShapeName = "";
                }
            }
        }
        
        // Utility Functions
        private void Box(Color col, System.Action content)
        {
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = col;
            using (new EditorGUILayout.VerticalScope("box"))
            {
                GUI.backgroundColor = bg;
                content();
            }
            GUI.backgroundColor = bg;
        }
        
        private bool HasShape(SkinnedMeshRenderer smr, string search)
        {
            var mesh = smr.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (mesh.GetBlendShapeName(i).Contains(search, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        
        private void ExpandAllFoldouts(bool expand)
        {
            foreach (var smr in _smrs.Where(s => s?.sharedMesh != null))
            {
                if (!string.IsNullOrEmpty(_search) && !HasShape(smr, _search))
                    continue;
                
                _foldouts[smr] = expand;
            }
        }
        
        private SkinnedMeshRenderer GetFirstSmr()
        {
            foreach (var go in _targets.Where(t => t != null && !t.CompareTag("EditorOnly")))
            {
                var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(s => s.sharedMesh?.blendShapeCount > 0);
                if (smr != null) return smr;
            }
            return null;
        }
        
        private Transform FindAvatarArm()
        {
            foreach (var t in _targets.Where(t => t != null))
            {
                var cur = t.transform;
                while (cur != null)
                {
                    var desc = cur.GetComponent<VRCAvatarDescriptor>();
                    if (desc != null)
                        return FindArm(desc.transform);
                    cur = cur.parent;
                }
            }
            return null;
        }
        
        private Transform FindArm(Transform parent)
        {
            return FindChild(parent, "armature");
        }
        
        private Transform FindChild(Transform parent, string keyword)
        {
            if (parent == null) return null;
            
            string key = keyword.ToLowerInvariant().Replace(" ", "");
            
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                string name = child.name.ToLowerInvariant().Replace("_", "").Replace(" ", "");
                
                if (name.Contains(key))
                    return child;
            }
            return null;
        }
        
        private void BuildBoneMap(Transform arm, Dictionary<string, Transform> map)
        {
            foreach (var bone in BoneOrder)
            {
                Transform found = null;
                
                if (BoneParent.TryGetValue(bone, out var parentName) && map.TryGetValue(parentName, out var parent))
                    found = FindChild(parent, bone);
                else
                    found = FindChild(arm, bone);
                
                if (found != null)
                    map[bone] = found;
            }
        }
        
        private void ReplaceMat(Material old, Material newMat)
        {
            if (!_matUsage.TryGetValue(old, out var list)) return;
            
            foreach (var (r, idx) in list)
            {
                if (r == null) continue;
                
                Undo.RecordObject(r, "Change Material");
                var mats = r.sharedMaterials;
                if (idx >= 0 && idx < mats.Length)
                {
                    mats[idx] = newMat;
                    r.sharedMaterials = mats;
                    EditorUtility.SetDirty(r);
                }
            }
            
            _matUsage.Remove(old);
            for (int i = 0; i < _mats.Count; i++)
            {
                if (_mats[i] == old)
                {
                    _mats[i] = newMat;
                    break;
                }
            }
            
            if (!_matUsage.ContainsKey(newMat))
                _matUsage[newMat] = list;
            else
                _matUsage[newMat].AddRange(list);
        }
        
        private void SyncScales()
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
            
            foreach (var bone in BoneOrder)
            {
                if (!_avatarBones.TryGetValue(bone, out var avBone)) continue;
                
                var avAdj = avBone.GetComponent<ModularAvatarScaleAdjuster>();
                if (avAdj == null) continue;
                
                var scale = avAdj.Scale;
                
                foreach (var outfit in _targets.Where(t => t != null))
                {
                    if (!_outfitBones.TryGetValue(outfit, out var map) || !map.TryGetValue(bone, out var ob))
                        continue;
                    
                    var adj = ob.GetComponent<ModularAvatarScaleAdjuster>();
                    if (adj != null)
                    {
                        Undo.RecordObject(adj, "Sync Scale");
                        adj.Scale = scale;
                        EditorUtility.SetDirty(adj);
                    }
                    else
                    {
                        adj = Undo.AddComponent<ModularAvatarScaleAdjuster>(ob.gameObject);
                        adj.Scale = scale;
                        EditorUtility.SetDirty(adj);
                    }
                }
            }
            
            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("完了", "スケールを同期しました。", "OK");
        }
    }
}
#endif