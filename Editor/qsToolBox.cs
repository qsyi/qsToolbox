#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;
using jp.lilxyzw.lilycalinventory.runtime;
using System.Reflection;
using System.IO;
using System.Linq;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace qsyi
{
    internal partial class QsToolBox : EditorWindow
    {
        private enum Mode { Material, BlendShape, Scale, ShadowSync, MenuGenerator }

        [SerializeField] private List<GameObject> _targets = new List<GameObject>();
        [SerializeField] private Transform _avatarArmature;
        [SerializeField] private List<OutfitArmatureEntry> _outfitArmatureEntries = new List<OutfitArmatureEntry>();
        [SerializeField] private bool _autoSyncPosition;
        [SerializeField] private bool _autoSyncRotation;
        [SerializeField] private List<MenuMeshEntry> _menuMeshEntries = new List<MenuMeshEntry>();

        private Mode _mode = Mode.Material;

        private readonly List<SkinnedMeshRenderer> _skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly Dictionary<Material, List<(Renderer renderer, int slot)>> _materialUsage = new Dictionary<Material, List<(Renderer, int)>>();
        private readonly Dictionary<GameObject, Dictionary<string, Transform>> _outfitBones = new Dictionary<GameObject, Dictionary<string, Transform>>();
        private readonly Dictionary<string, Transform> _avatarBones = new Dictionary<string, Transform>();
        private int? _lastMaterialHash;
        private int? _lastSmrHash;
        private int? _lastBonesHash;

        private SerializedObject _serializedObject;
        private SerializedProperty _targetsProperty;
        private SerializedProperty _armatureProperty;
        private SerializedProperty _outfitArmatureEntriesProperty;
        private bool _isDirty = true;
        private static FieldInfo _adjustChildPositionsField;
        private static bool _adjustChildPositionsResolved;

        // スキャン対象フィルタ（探索対象エリアの⚙から変更、EditorPrefsでUnity/PC単位に永続化）
        private const string PREF_SCAN_INCLUDE_EDITOR_ONLY = "qsToolBox.scanIncludeEditorOnly";
        private const string PREF_SCAN_INCLUDE_INACTIVE    = "qsToolBox.scanIncludeInactive";
        private bool _scanIncludeEditorOnly = false;
        private bool _scanIncludeInactive   = true;

        // UI Toolkit
        private VisualElement[] _tabElements;
        private VisualElement[] _tabAccents;
        private Label _versionLabel;
        private VisualElement _targetChipsWrap;
        private VisualElement _targetChipsSlot;
        private const float TARGET_AREA_COLLAPSED_HEIGHT = 28f;
        private int _lastTargetHash = -1;
        private bool _targetAreaExpanded = false;
        private bool _targetNeedsFoldout = false;
        private int  _targetRebuildId   = 0;
        private Label _targetFoldoutArrow;
        private Label _targetTitleLabel;
        private VisualElement _materialPane;
        private ScrollView _materialScrollView;
        private VisualElement _menuPane;

        internal static readonly Color AccentColor       = new Color(0.30f, 0.60f, 1.00f, 1f);
        internal static Color PaneBorderColor   => EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.20f) : new Color(0.70f, 0.70f, 0.72f);
        internal static Color ChromeBorderColor => EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.60f, 0.60f, 0.60f);
        internal static Color TextColor         => EditorGUIUtility.isProSkin ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.15f, 0.15f, 0.15f);
        internal static readonly Color DimColor = new Color(0.50f, 0.50f, 0.50f);

        private static readonly (string icon, string label, string tooltip)[] UITOOLKIT_TABS = {
            ("◧", "マテリアル",   "探索対象のマテリアルを置換できます"),
            ("◈", "シェイプキー", "探索対象のブレンドシェイプを表示・編集します"),
            ("⊞", "スケール",     "ModularAvatarのスケール調整機能を使用します"),
            ("◑", "影同期",       "lilToonの影設定を一括同期します"),
            ("☰", "メニュー生成", "lilycalInventory用の簡易メニューを生成します"),
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

        private static readonly Dictionary<string, string[]> BONE_ALIASES = new Dictionary<string, string[]>
        {
            ["Butt L"]      = new[] { "hipsl",        "hipl"         },
            ["Butt R"]      = new[] { "hipsr",        "hipr"         },
            ["Upper Leg L"] = new[] { "leftleg",      "leftthigh",   "legl",    "thighl"   },
            ["Upper Leg R"] = new[] { "rightleg",     "rightthigh",  "legr",    "thighr"   },
            ["Lower Leg L"] = new[] { "leftknee",     "kneeleft",    "kneeleftl"            },
            ["Lower Leg R"] = new[] { "rightknee",    "kneeright",   "kneerightl"           },
            ["Foot L"]      = new[] { "leftankle",    "ankleleft",   "anklel"               },
            ["Foot R"]      = new[] { "rightankle",   "ankleright",  "ankler"               },
            ["Shoulder L"]  = new[] { "leftshoulder", "shoulderleft"                        },
            ["Shoulder R"]  = new[] { "rightshoulder","shoulderright"                       },
            ["Upper Arm L"] = new[] { "leftarm",      "armleft",     "arml"                 },
            ["Upper Arm R"] = new[] { "rightarm",     "armright",    "armr"                 },
            ["Lower Arm L"] = new[] { "leftelbow",    "elbowleft",   "elbowl"               },
            ["Lower Arm R"] = new[] { "rightelbow",   "elbowright",  "elbowr"               },
            ["Hand L"]      = new[] { "leftwrist",    "wristleft",   "wristl"               },
            ["Hand R"]      = new[] { "rightwrist",   "wristright",  "wristr"               },
        };

        [MenuItem("Tools/qs/ツールボックス %q")]
        public static void ShowWindow()
        {
            var window = GetWindow<QsToolBox>("qsToolBox");
            var selected = Selection.gameObjects;
            if (selected.Length > 0)
            {
                window._serializedObject.Update();
                window._targetsProperty.arraySize = selected.Length;
                for (int i = 0; i < selected.Length; i++)
                    window._targetsProperty.GetArrayElementAtIndex(i).objectReferenceValue = selected[i];
                window._serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
            window.ScanData();
            window.Repaint();
        }

        private void OnEnable()
        {
            InitializeSerializedObject();
            _scanIncludeEditorOnly = EditorPrefs.GetBool(PREF_SCAN_INCLUDE_EDITOR_ONLY, false);
            _scanIncludeInactive   = EditorPrefs.GetBool(PREF_SCAN_INCLUDE_INACTIVE, true);
            EditorApplication.hierarchyChanged    += OnHierarchyChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            ScanData();
        }

        private void SetScanFilter(bool includeEditorOnly, bool includeInactive)
        {
            _scanIncludeEditorOnly = includeEditorOnly;
            _scanIncludeInactive   = includeInactive;
            EditorPrefs.SetBool(PREF_SCAN_INCLUDE_EDITOR_ONLY, _scanIncludeEditorOnly);
            EditorPrefs.SetBool(PREF_SCAN_INCLUDE_INACTIVE, _scanIncludeInactive);
            InvalidateContentCaches();
            ScanData();
            RebuildTargetChips();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged    -= OnHierarchyChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            RestoreMenuPreview();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange _)
        {
            if (_scaleSyncButton == null) return;
            _scaleSyncButton.SetEnabled(!EditorApplication.isPlaying);
        }

        private void OnUndoRedo()
        {
            // Undo/Redo でレンダラーのマテリアルや Transform が戻るため、キャッシュを無効化して再スキャンする
            InvalidateContentCaches();
            ScanData();
            RebuildScaleBoneDetail();
        }

        // ターゲットやコンテンツが変わったときに、各モードの変更検知ハッシュを無効化して次回スキャンを強制する
        private void InvalidateContentCaches()
        {
            _lastMaterialHash = null;
            _lastSmrHash = null;
            _lastBonesHash = null;
        }

        // 各Scan*系メソッドの「ハッシュが前回と同じなら変化なし」判定を共通化する
        private static bool HashChanged(int newHash, ref int? cache)
        {
            if (cache == newHash) return false;
            cache = newHash;
            return true;
        }

        private void InitializeSerializedObject()
        {
            _serializedObject = new SerializedObject(this);
            _targetsProperty = _serializedObject.FindProperty("_targets");
            _armatureProperty = _serializedObject.FindProperty("_avatarArmature");
            _outfitArmatureEntriesProperty = _serializedObject.FindProperty("_outfitArmatureEntries");
        }

        private void OnHierarchyChanged() { _isDirty = true; }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            root.Add(BuildHeader());
            root.Add(BuildTargetArea());
            root.Add(BuildTabBar());

            bool startMaterial = _mode == Mode.Material;
            bool startMenu     = _mode == Mode.MenuGenerator;

            _materialPane = BuildMaterialPane();
            _materialPane.style.flexGrow  = 1;
            _materialPane.style.minHeight = 0;
            _materialPane.style.display   = startMaterial ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(_materialPane);

            _menuPane = BuildMenuPane();
            _menuPane.style.flexGrow  = 1;
            _menuPane.style.minHeight = 0;
            _menuPane.style.display   = startMenu ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(_menuPane);

            bool startBlend = _mode == Mode.BlendShape;
            _blendShapePane = BuildBlendShapePane();
            _blendShapePane.style.flexGrow  = 1;
            _blendShapePane.style.minHeight = 0;
            _blendShapePane.style.display   = startBlend ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(_blendShapePane);

            bool startScale = _mode == Mode.Scale;
            _scalePane = BuildScalePane();
            _scalePane.style.flexGrow  = 1;
            _scalePane.style.minHeight = 0;
            _scalePane.style.display   = startScale ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(_scalePane);

            bool startShadowSync = _mode == Mode.ShadowSync;
            _shadowSyncPane = BuildShadowSyncPane();
            _shadowSyncPane.style.flexGrow  = 1;
            _shadowSyncPane.style.minHeight = 0;
            _shadowSyncPane.style.display   = startShadowSync ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(_shadowSyncPane);

            root.schedule.Execute(UpdateVersionLabel).Every(500);
            root.schedule.Execute(PollTargetChanges).Every(150);
        }

        private int GetTargetHash()
        {
            int hash = _targets.Count;
            foreach (var target in _targets)
                if (target != null)
                    hash = hash * 31 + target.GetInstanceID();
            return hash;
        }

        private void ScanData()
        {
            switch (_mode)
            {
                case Mode.Material:
                    if (ScanMaterials()) RebuildMaterialPane();
                    break;
                case Mode.BlendShape:
                    if (ScanSkinnedMeshRenderers()) { ScanForCompose(); RebuildBlendShapePane(); }
                    break;
                case Mode.Scale:
                    if (ScanBones()) RebuildScalePane();
                    break;
                case Mode.ShadowSync:
                    if (ScanMaterials()) RebuildShadowSyncDestList();
                    break;
                case Mode.MenuGenerator:
                    if (ScanMenuMeshEntries()) RebuildMenuPane();
                    else UpdateMenuGenerateButton();
                    break;
            }
        }

        private bool ScanSkinnedMeshRenderers()
        {
            _skinnedMeshRenderers.Clear();

            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                foreach (var smr in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(_scanIncludeInactive))
                {
                    if (smr.sharedMesh?.blendShapeCount > 0)
                        _skinnedMeshRenderers.Add(smr);
                }
            }

            int h = 17;
            foreach (var smr in _skinnedMeshRenderers)
                h = unchecked(h * 31 + (smr?.GetInstanceID() ?? 0));

            return HashChanged(h, ref _lastSmrHash);
        }

        private bool ScanMaterials()
        {
            _materials.Clear();
            _materialUsage.Clear();

            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(_scanIncludeInactive))
                    ProcessRendererMaterials(renderer);
            }

            int h = 17;
            foreach (var m in _materials)
                h = unchecked(h * 31 + (m?.GetInstanceID() ?? 0));

            return HashChanged(h, ref _lastMaterialHash);
        }

        private static bool IsUnderEditorOnly(Transform t)
        {
            while (t != null) { if (t.CompareTag("EditorOnly")) return true; t = t.parent; }
            return false;
        }

        private void ProcessRendererMaterials(Renderer renderer)
        {
            if (!_scanIncludeEditorOnly && IsUnderEditorOnly(renderer.transform)) return;
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

        private bool IsValidTarget(GameObject target) =>
            target != null && (_scanIncludeEditorOnly || !target.CompareTag("EditorOnly"));

        // ─── UI Toolkit ───────────────────────────────────────────────

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection  = FlexDirection.Row;
            header.style.flexShrink     = 0;
            header.style.alignItems     = Align.Center;
            header.style.paddingLeft    = 12;
            header.style.paddingRight   = 12;
            header.style.paddingTop     = 5;
            header.style.paddingBottom  = 5;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = ChromeBorderColor;

            _versionLabel = new Label();
            _versionLabel.style.marginLeft    = 8;
            _versionLabel.style.fontSize      = 11;
            _versionLabel.style.paddingLeft   = 6;
            _versionLabel.style.paddingRight  = 6;
            _versionLabel.style.paddingTop    = 2;
            _versionLabel.style.paddingBottom = 2;
            header.Add(_versionLabel);

            var manualBtn = new Button(() => Application.OpenURL("https://qsyi.github.io/qsToolbox/"));
            manualBtn.text = "マニュアル";
            manualBtn.style.marginLeft    = new StyleLength(StyleKeyword.Auto);
            manualBtn.style.fontSize      = 11;
            manualBtn.style.paddingLeft   = 6;
            manualBtn.style.paddingRight  = 6;
            manualBtn.style.paddingTop    = 2;
            manualBtn.style.paddingBottom = 2;
            header.Add(manualBtn);

            return header;
        }

        private VisualElement BuildTabBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection   = FlexDirection.Row;
            bar.style.flexShrink      = 0;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = ChromeBorderColor;

            _tabElements = new VisualElement[UITOOLKIT_TABS.Length];
            _tabAccents  = new VisualElement[UITOOLKIT_TABS.Length];

            for (int i = 0; i < UITOOLKIT_TABS.Length; i++)
            {
                int idx = i;
                var (icon, label, tooltip) = UITOOLKIT_TABS[i];

                var tab = new VisualElement();
                tab.tooltip                  = tooltip;
                tab.style.flexGrow           = 1;
                tab.style.flexDirection      = FlexDirection.Row;
                tab.style.alignItems         = Align.Center;
                tab.style.justifyContent     = Justify.Center;
                tab.style.paddingLeft        = 6;
                tab.style.paddingRight       = 6;
                tab.style.paddingTop         = 7;
                tab.style.paddingBottom      = 7;
                tab.style.position           = Position.Relative;

                var iconLbl = new Label(icon);
                iconLbl.name             = "tab-icon";
                iconLbl.style.fontSize   = 13;
                iconLbl.style.marginRight = 4;
                tab.Add(iconLbl);

                var textLbl = new Label(label);
                textLbl.name           = "tab-text";
                textLbl.style.fontSize = 11;
                tab.Add(textLbl);


                var accent = new VisualElement();
                accent.style.position          = Position.Absolute;
                accent.style.bottom            = 0;
                accent.style.left              = 0;
                accent.style.right             = 0;
                accent.style.height            = 2;
                accent.style.backgroundColor   = AccentColor;
                accent.style.display           = DisplayStyle.None;
                tab.Add(accent);
                _tabAccents[i] = accent;

                tab.RegisterCallback<MouseEnterEvent>(_ =>
                    tab.style.backgroundColor = EditorGUIUtility.isProSkin
                        ? new Color(0.26f, 0.26f, 0.28f, 1f)
                        : new Color(0.78f, 0.78f, 0.80f, 1f));
                tab.RegisterCallback<MouseLeaveEvent>(_ =>
                    tab.style.backgroundColor = new StyleColor(StyleKeyword.Null));
                tab.RegisterCallback<MouseDownEvent>(_ => OnTabClicked(idx));

                _tabElements[i] = tab;
                bar.Add(tab);
            }

            SetActiveTab((int)_mode, false);
            return bar;
        }

        private void OnTabClicked(int index)
        {
            if ((int)_mode == index) return;

            _mode = (Mode)index;
            GUI.FocusControl(null);
            _lastMaterialHash = null;
            ScanData();
            SetActiveTab(index, true);

            bool matActive        = _mode == Mode.Material;
            bool menuActive       = _mode == Mode.MenuGenerator;
            bool blendActive      = _mode == Mode.BlendShape;
            bool scaleActive      = _mode == Mode.Scale;
            bool shadowSyncActive = _mode == Mode.ShadowSync;
            if (_materialPane != null)
                _materialPane.style.display   = matActive        ? DisplayStyle.Flex : DisplayStyle.None;
            if (_menuPane != null)
                _menuPane.style.display       = menuActive       ? DisplayStyle.Flex : DisplayStyle.None;
            if (_blendShapePane != null)
                _blendShapePane.style.display = blendActive      ? DisplayStyle.Flex : DisplayStyle.None;
            if (_scalePane != null)
                _scalePane.style.display      = scaleActive      ? DisplayStyle.Flex : DisplayStyle.None;
            if (_shadowSyncPane != null)
                _shadowSyncPane.style.display = shadowSyncActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetActiveTab(int index, bool repaint)
        {
            bool dark = EditorGUIUtility.isProSkin;
            Color activeText   = new Color(0.40f, 0.72f, 1.00f, 1f);
            Color inactiveText = dark
                ? new Color(0.65f, 0.65f, 0.65f, 1f)
                : new Color(0.35f, 0.35f, 0.35f, 1f);

            if (_tabElements == null) return;

            for (int i = 0; i < _tabElements.Length; i++)
            {
                bool active = i == index;
                _tabAccents[i].style.display = active ? DisplayStyle.Flex : DisplayStyle.None;

                Color c = active ? activeText : inactiveText;
                _tabElements[i].Q<Label>("tab-icon").style.color = c;
                var tl = _tabElements[i].Q<Label>("tab-text");
                tl.style.color = c;
                tl.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }

            if (repaint) Repaint();
        }

        private void UpdateVersionLabel()
        {
            if (_versionLabel == null) return;

            if (QsVersionChecker.HasUpdate || QsVersionChecker.CheckComplete)
            {
                bool hasUpdate = QsVersionChecker.HasUpdate;
                Color c = hasUpdate
                    ? new Color(0.80f, 0.42f, 0.08f, 1f)
                    : new Color(0.18f, 0.58f, 0.28f, 1f);
                _versionLabel.text = hasUpdate
                    ? $"↑ v{QsVersionChecker.LatestVersion} が公開されています"
                    : $"✓ v{QsVersionChecker.CurrentVersion} 最新バージョンです";
                _versionLabel.style.color = c;
                _versionLabel.style.backgroundColor = new Color(c.r, c.g, c.b, 0.10f);
            }
            else if (QsVersionChecker.IsFetching)
            {
                _versionLabel.text = "確認中…";
                _versionLabel.style.color = new Color(0.50f, 0.50f, 0.50f, 1f);
                _versionLabel.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            }
            else
            {
                _versionLabel.text = "";
                _versionLabel.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            }
        }

        // ── 複数モードで共有するヘルパー ────────────────────────────────

        private static VisualElement MakeRendererField(Renderer renderer)
        {
            var wrap = new VisualElement();
            wrap.style.flexGrow = 1;
            if (renderer != null)
                wrap.RegisterCallback<MouseDownEvent>(evt =>
                {
                    EditorGUIUtility.PingObject(renderer);
                    evt.StopPropagation();
                });

            var field = new ObjectField();
            field.objectType        = typeof(Renderer);
            field.allowSceneObjects = true;
            field.value             = renderer;
            field.label             = "";
            field.style.flexGrow    = 1;
            field.SetEnabled(false);

            // Remove the empty label area
            field.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();

            wrap.Add(field);
            return wrap;
        }

        // ── Target Area (UI Toolkit) ──────────────────────────────────

        private VisualElement BuildTargetArea()
        {
            var area = new VisualElement();
            area.style.flexShrink    = 0;
            area.style.paddingLeft   = 8;
            area.style.paddingRight  = 8;
            area.style.paddingTop    = 5;
            area.style.paddingBottom = 5;
            area.style.borderBottomWidth = 1;
            area.style.borderBottomColor = ChromeBorderColor;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.marginBottom  = 3;

            _targetFoldoutArrow = new Label(_targetAreaExpanded ? "▼" : "▶");
            _targetFoldoutArrow.style.fontSize    = 9;
            _targetFoldoutArrow.style.color       = TextColor;
            _targetFoldoutArrow.style.marginRight = 3;
            _targetFoldoutArrow.style.display     = DisplayStyle.None; // 測定後に ApplyTargetAreaHeight が制御
            header.Add(_targetFoldoutArrow);

            _targetTitleLabel = new Label("探索対象");
            _targetTitleLabel.style.fontSize = 12;
            _targetTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _targetTitleLabel.style.color    = TextColor;
            _targetTitleLabel.style.flexGrow = 1;
            header.Add(_targetTitleLabel);

            var ctrlQHint = new Label("もう一度 Ctrl+Q で選択物を再スキャン");
            ctrlQHint.style.fontSize   = 11;
            ctrlQHint.style.color      = EditorGUIUtility.isProSkin
                ? new Color(0.55f, 0.55f, 0.55f, 1f)
                : new Color(0.45f, 0.45f, 0.45f, 1f);
            ctrlQHint.style.marginRight = 6;
            header.Add(ctrlQHint);

            var filterBtn = new Button(() =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("EditorOnlyも含める"), _scanIncludeEditorOnly,
                    () => SetScanFilter(!_scanIncludeEditorOnly, _scanIncludeInactive));
                menu.AddItem(new GUIContent("非アクティブも含める"), _scanIncludeInactive,
                    () => SetScanFilter(_scanIncludeEditorOnly, !_scanIncludeInactive));
                menu.ShowAsContext();
            });
            filterBtn.text    = "フィルター";
            filterBtn.tooltip = "スキャン対象フィルター（EditorOnly / 非アクティブの含め方を切り替えます）";
            filterBtn.style.height       = 20;
            filterBtn.style.fontSize     = 10;
            filterBtn.style.marginRight  = 4;
            filterBtn.style.paddingLeft  = filterBtn.style.paddingRight = 8;
            header.Add(filterBtn);

            var rescanBtn = new Button(() => { ScanData(); RebuildTargetChips(); });
            rescanBtn.text    = "↺";
            rescanBtn.tooltip = "再スキャン";
            rescanBtn.style.width        = 26;
            rescanBtn.style.height       = 20;
            rescanBtn.style.fontSize     = 12;
            rescanBtn.style.paddingLeft  = rescanBtn.style.paddingRight = 0;
            header.Add(rescanBtn);

            // ▶/▼ とラベルをクリックで折りたたみトグル（左クリックのみ、⚙/↺ボタン除外）
            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (rescanBtn == evt.target || rescanBtn.Contains(evt.target as VisualElement)) return;
                if (filterBtn == evt.target || filterBtn.Contains(evt.target as VisualElement)) return;
                _targetAreaExpanded = !_targetAreaExpanded;
                _targetFoldoutArrow.text = _targetAreaExpanded ? "▼" : "▶";
                ApplyTargetAreaHeight();
                evt.StopPropagation();
            });

            area.Add(header);

            // 折りたたみ高さの測定中に下のモード選択行が動かないよう、
            // 表示用の固定高さスロットと、実測定用の中身を分離する。
            _targetChipsSlot = new VisualElement();
            _targetChipsSlot.style.position = Position.Relative;
            _targetChipsSlot.style.overflow = Overflow.Hidden;
            area.Add(_targetChipsSlot);

            _targetChipsWrap = new VisualElement();
            _targetChipsWrap.style.flexDirection = FlexDirection.Row;
            _targetChipsWrap.style.flexWrap      = Wrap.Wrap;
            _targetChipsWrap.style.alignItems    = Align.Center;
            _targetChipsWrap.style.overflow      = Overflow.Hidden;
            _targetChipsWrap.style.position      = Position.Relative;
            _targetChipsSlot.Add(_targetChipsWrap);

            RebuildTargetChips();

            area.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool any = false;
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is GameObject) { any = true; break; }
                DragAndDrop.visualMode = any
                    ? DragAndDropVisualMode.Link
                    : DragAndDropVisualMode.Rejected;
                evt.StopPropagation();
            });

            area.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                    if (obj is GameObject go)
                        AddTargetObject(go);
                // 折りたたみ中でも追加したチップが見えるよう自動展開
                if (!_targetAreaExpanded)
                {
                    _targetAreaExpanded = true;
                    if (_targetFoldoutArrow != null)
                        _targetFoldoutArrow.text = "▼";
                    ApplyTargetAreaHeight();
                }
                evt.StopPropagation();
            });

            return area;
        }

        private void RebuildTargetChips()
        {
            if (_targetChipsWrap == null) return;
            bool dark = EditorGUIUtility.isProSkin;

            _targetChipsWrap.Clear();

            Color chipBg     = dark ? new Color(0.28f, 0.28f, 0.31f, 1f) : new Color(0.88f, 0.88f, 0.90f, 1f);
            Color chipBorder = dark ? new Color(0.38f, 0.38f, 0.42f, 1f) : new Color(0.62f, 0.62f, 0.66f, 1f);

            bool allNull = _targets.Count == 0 || _targets.All(t => t == null);

            for (int i = 0; i < _targets.Count; i++)
            {
                int capturedI = i;
                var target = _targets[i];

                var chip = new VisualElement();
                chip.style.flexDirection  = FlexDirection.Row;
                chip.style.alignItems     = Align.Center;
                chip.style.paddingLeft    = 7;
                chip.style.paddingRight   = 4;
                chip.style.paddingTop     = 2;
                chip.style.paddingBottom  = 2;
                chip.style.marginRight    = 4;
                chip.style.marginBottom   = 2;
                chip.style.backgroundColor = chipBg;
                chip.style.borderTopWidth = chip.style.borderRightWidth =
                    chip.style.borderBottomWidth = chip.style.borderLeftWidth = 1;
                chip.style.borderTopColor = chip.style.borderRightColor =
                    chip.style.borderBottomColor = chip.style.borderLeftColor = chipBorder;
                chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius =
                    chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 10;

                if (target == null)
                {
                    var missingLbl = new Label("(Missing)");
                    missingLbl.style.fontSize = 11;
                    missingLbl.style.color    = DimColor;
                    chip.Add(missingLbl);
                }
                else
                {
                    var dot = new Label("◼");
                    dot.style.fontSize    = 7;
                    dot.style.color       = AccentColor;
                    dot.style.marginRight = 4;
                    chip.Add(dot);

                    var nameLbl = new Label(target.name);
                    nameLbl.style.fontSize = 11;
                    nameLbl.style.color    = TextColor;
                    chip.Add(nameLbl);

                    // Ping on click (chip body, not × button)
                    var pingTarget = target;
                    dot.RegisterCallback<MouseDownEvent>(_ => EditorGUIUtility.PingObject(pingTarget));
                    nameLbl.RegisterCallback<MouseDownEvent>(_ => EditorGUIUtility.PingObject(pingTarget));
                    nameLbl.RegisterCallback<MouseEnterEvent>(_ => nameLbl.style.color = AccentColor);
                    nameLbl.RegisterCallback<MouseLeaveEvent>(_ => nameLbl.style.color = TextColor);
                }

                var xBtn = new Label("×");
                xBtn.style.fontSize   = 11;
                xBtn.style.color      = DimColor;
                xBtn.style.marginLeft = 4;
                xBtn.RegisterCallback<MouseEnterEvent>(_ =>
                    xBtn.style.color = new Color(0.90f, 0.30f, 0.25f, 1f));
                xBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                    xBtn.style.color = DimColor);
                xBtn.RegisterCallback<MouseDownEvent>(_ => RemoveTargetAt(capturedI));
                chip.Add(xBtn);

                _targetChipsWrap.Add(chip);
            }

            if (allNull)
            {
                var hint = new Label("Hierarchy からドラッグ");
                hint.style.fontSize = 11;
                hint.style.color    = DimColor;
                hint.style.unityFontStyleAndWeight = FontStyle.Italic;
                _targetChipsWrap.Add(hint);
            }
            else
            {
                var addHint = new Label("+ D&D");
                addHint.style.fontSize        = 10;
                addHint.style.color           = DimColor;
                addHint.style.paddingLeft     = 5;
                addHint.style.paddingRight    = 5;
                addHint.style.paddingTop      = 2;
                addHint.style.paddingBottom   = 2;
                addHint.style.borderTopWidth  = addHint.style.borderRightWidth =
                    addHint.style.borderBottomWidth = addHint.style.borderLeftWidth = 1;
                addHint.style.borderTopColor  = addHint.style.borderRightColor =
                    addHint.style.borderBottomColor = addHint.style.borderLeftColor = chipBorder;
                addHint.style.borderTopLeftRadius = addHint.style.borderTopRightRadius =
                    addHint.style.borderBottomLeftRadius = addHint.style.borderBottomRightRadius = 10;
                _targetChipsWrap.Add(addHint);
            }

            // 表示中のスロット高さは変えず、中身だけ absolute+hidden にして
            // 折りたたみが必要かどうかを1フレーム後に測定する（モード選択行のジャンプを防止）。
            _targetChipsWrap.style.position   = Position.Absolute;
            _targetChipsWrap.style.visibility = Visibility.Hidden;
            _targetChipsWrap.style.width      = _targetChipsSlot.resolvedStyle.width;
            _targetChipsWrap.style.maxHeight  = new StyleLength(StyleKeyword.None);
            int rebuildId = ++_targetRebuildId;
            _targetChipsWrap.schedule.Execute(() =>
            {
                if (_targetRebuildId != rebuildId) return;
                _targetNeedsFoldout = _targetChipsWrap.layout.height > 36f;
                _targetChipsWrap.style.position   = Position.Relative;
                _targetChipsWrap.style.visibility = Visibility.Visible;
                _targetChipsWrap.style.width      = new StyleLength(StyleKeyword.Auto);
                ApplyTargetAreaHeight();
            });
        }

        private void ApplyTargetAreaHeight()
        {
            if (_targetChipsSlot == null) return;

            bool collapsed = _targetNeedsFoldout && !_targetAreaExpanded;

            _targetChipsSlot.style.height = collapsed
                ? new StyleLength(TARGET_AREA_COLLAPSED_HEIGHT)
                : new StyleLength(StyleKeyword.Auto);

            if (_targetFoldoutArrow != null)
                _targetFoldoutArrow.style.display = _targetNeedsFoldout ? DisplayStyle.Flex : DisplayStyle.None;

            if (_targetTitleLabel != null)
            {
                int count = _targets.Count(t => t != null);
                _targetTitleLabel.text = count > 0 ? $"探索対象 ({count}件)" : "探索対象";
            }
        }

        private void PollTargetChanges()
        {
            _serializedObject.Update();
            int currentHash = GetTargetHash();
            bool hashChanged = currentHash != _lastTargetHash;

            if (!_isDirty && !hashChanged) return;

            _isDirty = false;

            if (hashChanged)
            {
                _lastTargetHash = currentHash;
                InvalidateContentCaches();
                RebuildTargetChips();
            }

            ScanData();

            if (_mode == Mode.ShadowSync)
            {
                int srcHash = ComputeShadowSourceHash();
                if (_lastShadowSourceHash != srcHash)
                {
                    _lastShadowSourceHash = srcHash;
                    RefreshShadowSyncSwatches();
                }
            }
        }

        private void AddTargetObject(GameObject go)
        {
            if (_targets.Contains(go)) return;
            _serializedObject.Update();
            int idx = _targetsProperty.arraySize;
            _targetsProperty.arraySize++;
            _targetsProperty.GetArrayElementAtIndex(idx).objectReferenceValue = go;
            _serializedObject.ApplyModifiedProperties();
            _lastTargetHash = GetTargetHash();
            ScanData();
            RebuildTargetChips();
        }

        private void RemoveTargetAt(int index)
        {
            if (index < 0 || index >= _targets.Count) return;
            _serializedObject.Update();
            var elem = _targetsProperty.GetArrayElementAtIndex(index);
            if (elem.objectReferenceValue != null)
                elem.objectReferenceValue = null;
            _targetsProperty.DeleteArrayElementAtIndex(index);
            _serializedObject.ApplyModifiedProperties();
            _lastTargetHash = GetTargetHash();
            ScanData();
            RebuildTargetChips();
        }
    }
}
#endif
