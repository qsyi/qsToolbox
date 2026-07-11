#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace qsyi
{
    internal partial class QsToolBox
    {
        private VisualElement _shadowSyncPane;
        private Material      _shadowSyncSource;
        private ObjectField   _shadowSyncSourceField;
        private Renderer      _shadowSyncSourceRenderer;
        private int           _shadowSyncSourceSlot;
        private Button        _shadowSyncSlotButton;
        private readonly HashSet<string>   _shadowSyncParams  = new HashSet<string>();
        private readonly HashSet<Material> _shadowSyncDestSet = new HashSet<Material>();
        private readonly Dictionary<string, List<Action<Color, bool>>> _shadowSyncColorBoxes  = new Dictionary<string, List<Action<Color, bool>>>();
        private VisualElement        _shadowSyncDestListEl;
        private Label                _shadowSyncDestCountLabel;
        private Button               _shadowSyncSyncBtn;
        private Button               _shadowSyncAllSelBtn;
        private Button               _shadowSyncCreatePresetBtn;
        private float                _shadowHueShift        = 0f;
        private float                _shadowSaturationScale = 1f;
        private ShadowSyncPresetEntry _activeShadowPreset;
        private Toggle        _shadowSyncShadowGroupToggle;
        private Toggle        _shadowColorGroupToggle;
        private Toggle        _shadowNumGroupToggle;
        private Toggle        _shadowSyncRimGroupToggle;
        private Toggle        _shadowSyncRimshadeGroupToggle;
        private Toggle        _shadowDistanceFadeGroupToggle;
        private Toggle        _shadowSyncBacklightGroupToggle;
        private Toggle        _shadowSyncMasterToggle;
        private Label         _shadowBacklightStrengthLabel;
        private Label         _shadowRimBlendModeLabel;
        private int?          _lastShadowSourceHash;
        private VisualElement _shadowPresetListEl;
        private readonly Dictionary<string, bool> _shadowPresetCategoryOpen = new Dictionary<string, bool>();
        private readonly List<ShadowSyncPresetEntry>    _builtinPresets        = new List<ShadowSyncPresetEntry>();
        private readonly List<ShadowSyncPresetEntry>    _lilToonPresets        = new List<ShadowSyncPresetEntry>();
        private readonly Dictionary<string, Toggle>     _shadowSyncParamToggles= new Dictionary<string, Toggle>();

        // name はアセット由来（so.name＝ファイル名）の場合は assetPath とセットで LoadBuiltinPresets が埋める。lilToon由来の場合 assetPath は空のまま。
        [System.Serializable] private class ShadowSyncPresetEntry  { public string name; public string category; public List<ShadowSyncPresetProp> props = new List<ShadowSyncPresetProp>(); public string assetPath; public bool isBundled; }

        // プリセット保存ウィンドウのプレビュー1行分（色 or 数値）
        private class ShadowPresetSaveEntryData
        {
            public string           prop;
            public string           label;
            public bool             isColor;
            public Color            color;
            public string           valueText;
            public ShadowParamGroup group;
            public bool             included;
        }

        // プリセット保存用の別ウィンドウ。名前入力と、保存される項目・色のプレビューを表示する。
        private class ShadowPresetSaveWindow : EditorWindow
        {
            private string _presetName = "";
            private string _presetCategory = "その他";
            private List<ShadowPresetSaveEntryData> _items;
            private Action<string, string, HashSet<string>> _onSave;

            private static readonly ShadowParamGroup[] GroupOrder =
            {
                ShadowParamGroup.ShadowSettings, ShadowParamGroup.RimShade, ShadowParamGroup.Rim,
                ShadowParamGroup.Backlight, ShadowParamGroup.DistanceFade,
            };

            public static void Open(string defaultName, List<ShadowPresetSaveEntryData> items, Action<string, string, HashSet<string>> onSave)
            {
                var win = CreateInstance<ShadowPresetSaveWindow>();
                win._presetName = defaultName;
                win._items      = items;
                win._onSave     = onSave;
                win.titleContent = new GUIContent("プリセット保存");
                win.minSize = new Vector2(340, 380);
                win.maxSize = new Vector2(460, 820);
                win.ShowUtility();
            }

            public void CreateGUI()
            {
                var root = rootVisualElement;
                root.style.paddingLeft   = root.style.paddingRight  = 12;
                root.style.paddingTop    = root.style.paddingBottom = 12;
                root.style.flexGrow      = 1;

                var nameRow = new VisualElement();
                nameRow.style.flexDirection = FlexDirection.Row;
                nameRow.style.alignItems    = Align.Center;
                nameRow.style.flexShrink    = 0;
                nameRow.style.minHeight     = 24;
                nameRow.style.marginBottom  = 12;

                var nameLbl = new Label("名前");
                nameLbl.style.fontSize   = 11;
                nameLbl.style.width      = 40;
                nameLbl.style.flexShrink = 0;
                nameRow.Add(nameLbl);

                var nameField = new TextField();
                nameField.value          = _presetName;
                nameField.style.flexGrow = 1;
                nameField.style.height   = 22;
                nameField.RegisterValueChangedCallback(evt => _presetName = evt.newValue);
                nameRow.Add(nameField);
                root.Add(nameRow);

                var categoryRow = new VisualElement();
                categoryRow.style.flexDirection = FlexDirection.Row;
                categoryRow.style.alignItems    = Align.Center;
                categoryRow.style.flexShrink    = 0;
                categoryRow.style.minHeight     = 24;
                categoryRow.style.marginBottom  = 12;

                var categoryLbl = new Label("分類");
                categoryLbl.style.fontSize   = 11;
                categoryLbl.style.width      = 40;
                categoryLbl.style.flexShrink = 0;
                categoryRow.Add(categoryLbl);

                var categoryField = new TextField();
                categoryField.value          = _presetCategory;
                categoryField.style.flexGrow = 1;
                categoryField.style.height   = 22;
                categoryField.RegisterValueChangedCallback(evt => _presetCategory = evt.newValue);
                categoryRow.Add(categoryField);
                root.Add(categoryRow);

                var listLbl = new Label();
                listLbl.style.fontSize     = 10;
                listLbl.style.color        = DimColor;
                listLbl.style.flexShrink   = 0;
                listLbl.style.marginBottom = 4;
                root.Add(listLbl);

                void RefreshListLbl() =>
                    listLbl.text = $"保存される項目（{_items.Count(i => i.included)}/{_items.Count}）";
                RefreshListLbl();

                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.flexGrow       = 1;
                scroll.style.minHeight      = 0;
                scroll.style.borderTopWidth = scroll.style.borderBottomWidth = 1;
                scroll.style.borderTopColor = scroll.style.borderBottomColor = PaneBorderColor;

                foreach (var group in GroupOrder)
                {
                    var groupItems = _items.Where(i => i.group == group).ToList();
                    if (groupItems.Count == 0) continue;

                    var groupHdr = new Label(ShadowGroupLabel(group));
                    groupHdr.style.fontSize                = 11;
                    groupHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
                    groupHdr.style.color                   = TextColor;
                    groupHdr.style.paddingLeft             = 6;
                    groupHdr.style.paddingTop              = 6;
                    groupHdr.style.paddingBottom           = 4;
                    groupHdr.style.backgroundColor = EditorGUIUtility.isProSkin
                        ? new Color(0.17f, 0.17f, 0.20f)
                        : new Color(0.78f, 0.78f, 0.82f);
                    scroll.Add(groupHdr);

                    foreach (var item in groupItems)
                    {
                        var row = new VisualElement();
                        row.style.flexDirection     = FlexDirection.Row;
                        row.style.alignItems        = Align.Center;
                        row.style.paddingTop        = row.style.paddingBottom = 4;
                        row.style.paddingLeft       = 6;
                        row.style.paddingRight      = 6;
                        row.style.borderBottomWidth = 1;
                        row.style.borderBottomColor = PaneBorderColor;

                        var toggle = new Toggle();
                        toggle.value             = item.included;
                        toggle.style.marginRight = 4;
                        toggle.style.marginLeft  = 16;
                        toggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                        row.Add(toggle);

                        string text;
                        if (item.isColor)
                        {
                            var box = new VisualElement();
                            box.style.width       = 16;
                            box.style.height      = 16;
                            box.style.marginRight = 6;
                            box.style.flexShrink  = 0;
                            box.style.borderTopLeftRadius = box.style.borderTopRightRadius =
                                box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 3;
                            box.style.borderTopWidth = box.style.borderBottomWidth =
                                box.style.borderLeftWidth = box.style.borderRightWidth = 1;
                            box.style.borderTopColor = box.style.borderBottomColor =
                                box.style.borderLeftColor = box.style.borderRightColor = PaneBorderColor;
                            bool none = item.color.a <= 0.0001f;
                            var opaque = NormalizeForSwatch(item.color);
                            opaque.a = 1f;
                            box.style.backgroundColor = none ? new Color(0.3f, 0.3f, 0.3f) : opaque;
                            row.Add(box);

                            int pct = Mathf.RoundToInt(Mathf.Clamp01(item.color.a) * 100f);
                            text = none ? $"{item.label}（なし）" : pct < 100 ? $"{item.label}（{pct}%）" : item.label;
                        }
                        else
                        {
                            text = $"{item.label} = {item.valueText}";
                        }

                        var lbl = new Label(text);
                        lbl.style.fontSize = 11;
                        lbl.style.flexGrow = 1;
                        row.Add(lbl);

                        void ApplyIncludedStyle()
                        {
                            lbl.style.color   = item.included ? TextColor : DimColor;
                            row.style.opacity = item.included ? 1f : 0.5f;
                        }
                        ApplyIncludedStyle();

                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            item.included = evt.newValue;
                            ApplyIncludedStyle();
                            RefreshListLbl();
                        });

                        scroll.Add(row);
                    }
                }
                root.Add(scroll);

                var btnRow = new VisualElement();
                btnRow.style.flexDirection  = FlexDirection.Row;
                btnRow.style.justifyContent = Justify.FlexEnd;
                btnRow.style.flexShrink     = 0;
                btnRow.style.minHeight      = 26;
                btnRow.style.marginTop      = 12;

                var cancelBtn = new Button(Close) { text = "キャンセル" };
                cancelBtn.style.height       = 24;
                cancelBtn.style.paddingLeft  = cancelBtn.style.paddingRight = 10;
                cancelBtn.style.marginRight  = 6;
                btnRow.Add(cancelBtn);

                var saveBtn = new Button(() =>
                {
                    string n = (_presetName ?? "").Trim();
                    if (string.IsNullOrEmpty(n)) return;
                    string cat = (_presetCategory ?? "").Trim();
                    if (string.IsNullOrEmpty(cat)) cat = "その他";
                    var includedProps = new HashSet<string>(_items.Where(i => i.included).Select(i => i.prop));
                    _onSave?.Invoke(n, cat, includedProps);
                    Close();
                }) { text = "保存" };
                saveBtn.style.height      = 24;
                saveBtn.style.paddingLeft = saveBtn.style.paddingRight = 12;
                btnRow.Add(saveBtn);

                root.Add(btnRow);
            }
        }

        // プリセットの名前変更用の小さな別ウィンドウ。
        private class ShadowPresetRenameWindow : EditorWindow
        {
            private string _name = "";
            private Action<string> _onConfirm;

            public static void Open(string currentName, Action<string> onConfirm)
            {
                var win = CreateInstance<ShadowPresetRenameWindow>();
                win._name      = currentName;
                win._onConfirm = onConfirm;
                win.titleContent = new GUIContent("名前を変更");
                win.minSize = win.maxSize = new Vector2(300, 90);
                win.ShowUtility();
            }

            public void CreateGUI()
            {
                var root = rootVisualElement;
                root.style.paddingLeft   = root.style.paddingRight  = 12;
                root.style.paddingTop    = root.style.paddingBottom = 12;

                void Confirm()
                {
                    string n = (_name ?? "").Trim();
                    if (string.IsNullOrEmpty(n)) return;
                    _onConfirm?.Invoke(n);
                    Close();
                }

                var field = new TextField();
                field.value              = _name;
                field.style.height       = 22;
                field.style.marginBottom = 10;
                field.RegisterValueChangedCallback(evt => _name = evt.newValue);
                field.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) Confirm();
                });
                root.Add(field);
                field.Focus();

                var btnRow = new VisualElement();
                btnRow.style.flexDirection  = FlexDirection.Row;
                btnRow.style.justifyContent = Justify.FlexEnd;

                var cancelBtn = new Button(Close) { text = "キャンセル" };
                cancelBtn.style.marginRight = 6;
                btnRow.Add(cancelBtn);

                var okBtn = new Button(Confirm) { text = "変更" };
                btnRow.Add(okBtn);

                root.Add(btnRow);
            }
        }

        private enum ShadowParamType  { Color, Float, Int, Vector }
        private enum ShadowParamGroup { ShadowSettings, RimShade, Rim, Backlight, DistanceFade }

        private struct ShadowParam
        {
            public readonly string Prop, Label;
            public readonly ShadowParamType  Type;
            public readonly ShadowParamGroup Group;
            public ShadowParam(string prop, string label, ShadowParamType t, ShadowParamGroup g)
            { Prop = prop; Label = label; Type = t; Group = g; }
        }

        private static readonly ShadowParam[] SHADOW_PARAMS =
        {
            // ─ 影色 ────────────────────────────────────────────────
            new ShadowParam("_ShadowColor",    "影色",       ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow2ndColor", "2影色",      ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow3rdColor",    "3影色",         ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowBorderColor", "影ボーダー色",  ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_RimShadeColor",     "リムシェード",  ShadowParamType.Color, ShadowParamGroup.RimShade),
            new ShadowParam("_RimColor",          "リムライト",    ShadowParamType.Color, ShadowParamGroup.Rim),
            new ShadowParam("_RimIndirColor",     "間接光リム色",  ShadowParamType.Color, ShadowParamGroup.Rim),
            // ─ 影設定数値 ───────────────────────────────────────────
            new ShadowParam("_UseShadow",               "影ON/OFF",            ShadowParamType.Int,   ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowStrength",          "影強度",              ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowBorder",            "影境界",              ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowBlur",              "影ぼかし",            ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowNormalStrength",    "ノーマルマップ強度 1",ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowReceive",           "影を受け取る 1",      ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow2ndBorder",         "2影境界",             ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow2ndBlur",           "2影ぼかし",           ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow2ndNormalStrength", "ノーマルマップ強度 2",ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow2ndReceive",        "影を受け取る 2",      ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow3rdBorder",         "3影境界",             ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow3rdBlur",           "3影ぼかし",           ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow3rdNormalStrength", "ノーマルマップ強度 3",ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Shadow3rdReceive",        "影を受け取る 3",      ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowBorderRange",       "境界の幅",            ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowMainStrength",      "コントラスト",        ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_ShadowEnvStrength",       "環境光影響度",        ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            // ─ リムシェード数値 ─────────────────────────────────────
            new ShadowParam("_UseRimShade",            "リムシェードON/OFF",  ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_RimShadeNormalStrength", "リムシェードNM",      ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_RimShadeBorder",         "リムシェード範囲",    ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_RimShadeBlur",           "リムシェードぼかし",  ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_RimShadeFresnelPower",   "リムシェード細さ",    ShadowParamType.Float, ShadowParamGroup.RimShade),
            // ─ リムライト数値 ────────────────────────────────────────
            new ShadowParam("_UseRim",               "リムライトON/OFF",    ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimBorder",            "リム範囲",            ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimBlur",              "リムぼかし",          ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimFresnelPower",      "リムの細さ",          ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimEnableLighting",    "ライティング適用",    ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimMainStrength",      "メイン影響度",        ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowMask",        "影部分で無効化",      ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimBackfaceMask",      "裏面で無効化",        ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimDirStrength",       "ライト方向影響度",    ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimDirRange",          "直接光の幅",          ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimIndirRange",        "間接光の幅",          ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimIndirBorder",       "間接光ボーダー",      ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimIndirBlur",         "間接光ぼかし",        ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimNormalStrength",    "リムNM強度",          ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimVRParallaxStrength","VR視差",              ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimApplyTransparency", "透明度への適用",      ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimBlendMode",         "合成モード",          ShadowParamType.Int,   ShadowParamGroup.Rim),
            // ─ 逆光ライト ────────────────────────────────────────────
            new ShadowParam("_UseBacklight",            "逆光ライトON/OFF",   ShadowParamType.Int,   ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightColor",          "逆光色",             ShadowParamType.Color, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightMainStrength",   "メインカラー影響度", ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightNormalStrength", "ノーマルマップ強度", ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightBorder",         "境界",               ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightBlur",           "ぼかし",             ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightDirectivity",    "指向性",             ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightViewStrength",   "正面時の強さ",       ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightReceiveShadow",  "影を受ける",         ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightBackfaceMask",   "裏面で無効化",       ShadowParamType.Float, ShadowParamGroup.Backlight),
            // ─ 距離フェード ──────────────────────────────────────────
            new ShadowParam("_DistanceFadeColor",           "フェード色",     ShadowParamType.Color,  ShadowParamGroup.DistanceFade),
            new ShadowParam("_DistanceFadeRimColor",        "リムフェード色", ShadowParamType.Color,  ShadowParamGroup.DistanceFade),
            new ShadowParam("_DistanceFade",                "距離・範囲",     ShadowParamType.Vector, ShadowParamGroup.DistanceFade),
            new ShadowParam("_DistanceFadeMode",            "フェードモード", ShadowParamType.Int,    ShadowParamGroup.DistanceFade),
            new ShadowParam("_DistanceFadeRimFresnelPower", "リムの細さ",     ShadowParamType.Float,  ShadowParamGroup.DistanceFade),
        };

        // ─────────────────────────────────────────────────────────────
        // Shadow Sync Pane

        // PollTargetChangesから呼ばれる。コピー元の色/シェーダー/距離フェードが変わったかを安価に検知するためのハッシュ。
        private int ComputeShadowSourceHash()
        {
            if (_shadowSyncSource == null)
                return _activeShadowPreset != null ? (_activeShadowPreset.name ?? "").GetHashCode() ^ _activeShadowPreset.props.Count : 0;
            int h = 17;
            foreach (var sp in SHADOW_PARAMS.Where(p => p.Type == ShadowParamType.Color))
            {
                if (!_shadowSyncSource.HasProperty(sp.Prop)) continue;
                var c = _shadowSyncSource.GetColor(sp.Prop);
                h = unchecked(h * 31 + c.r.GetHashCode());
                h = unchecked(h * 31 + c.g.GetHashCode());
                h = unchecked(h * 31 + c.b.GetHashCode());
            }
            if (_shadowSyncSource.shader != null)
                h = unchecked(h * 31 + _shadowSyncSource.shader.name.GetHashCode());
            if (_shadowSyncSource.HasProperty("_DistanceFade"))
                h = unchecked(h * 31 + _shadowSyncSource.GetVector("_DistanceFade").z.GetHashCode());
            return h;
        }

        private static Color ShadowSyncRow1Bg =>
            EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.86f, 0.86f, 0.86f);

        private VisualElement BuildShadowSyncPane()
        {
            _shadowSyncParams.Clear();
            _shadowSyncColorBoxes.Clear();
            _shadowSyncParamToggles.Clear();
            _shadowSyncDestSet.Clear();

            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow      = 1;
            pane.style.minHeight     = 0;

            // ── コピー元マテリアル カード（スウォッチ付き）──
            var srcCard = new VisualElement();
            srcCard.style.flexDirection     = FlexDirection.Column;
            srcCard.style.paddingLeft       = 10;
            srcCard.style.paddingRight      = 10;
            srcCard.style.paddingTop        = 6;
            srcCard.style.paddingBottom     = 6;
            srcCard.style.flexShrink        = 0;
            srcCard.style.minWidth          = 0;
            srcCard.style.overflow          = Overflow.Hidden;
            srcCard.style.borderBottomWidth = 1;
            srcCard.style.borderBottomColor = PaneBorderColor;
            srcCard.style.backgroundColor   = ShadowSyncRow1Bg;

            var srcTopRow = new VisualElement();
            srcTopRow.style.flexDirection = FlexDirection.Row;
            srcTopRow.style.alignItems    = Align.Center;
            srcTopRow.style.minWidth      = 0;

            var srcLbl = new Label("コピー元");
            srcLbl.style.fontSize                = 12;
            srcLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            srcLbl.style.color                   = TextColor;
            srcLbl.style.marginRight             = 6;
            srcLbl.style.flexShrink              = 0;
            srcTopRow.Add(srcLbl);

            var srcField = new ObjectField();
            srcField.objectType        = typeof(Material);
            srcField.allowSceneObjects = false;
            srcField.value             = _shadowSyncSource;
            srcField.style.flexGrow    = 1;
            srcField.style.flexShrink  = 1;
            srcField.style.minWidth    = 0;
            srcField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            srcField.RegisterValueChangedCallback(evt =>
            {
                _shadowSyncSourceRenderer = null;
                ApplyShadowSyncSourceMaterial(evt.newValue as Material);
            });
            _shadowSyncSourceField = srcField;
            srcTopRow.Add(srcField);

            _shadowSyncSlotButton = new Button(() =>
            {
                if (_shadowSyncSourceRenderer != null) ShowShadowSyncSlotMenu(_shadowSyncSourceRenderer);
            });
            _shadowSyncSlotButton.style.height       = 20;
            _shadowSyncSlotButton.style.fontSize      = 10;
            _shadowSyncSlotButton.style.marginLeft    = 4;
            _shadowSyncSlotButton.style.paddingLeft   = _shadowSyncSlotButton.style.paddingRight = 8;
            _shadowSyncSlotButton.style.flexShrink    = 0;
            _shadowSyncSlotButton.style.display       = DisplayStyle.None;
            srcTopRow.Add(_shadowSyncSlotButton);

            srcCard.Add(srcTopRow);

            // レンダラー（またはレンダラーを含むGameObject）のドラッグ＆ドロップを受け付ける。
            // マテリアル自体のドロップは srcField 自身が処理するのでここでは扱わない。
            srcCard.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool hasMaterial = DragAndDrop.objectReferences.Any(o => o is Material);
                bool hasRenderer = !hasMaterial && DragAndDrop.objectReferences.Any(o => FindRendererFromDraggedObject(o) != null);
                if (hasRenderer)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    evt.StopPropagation();
                }
            });
            srcCard.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Any(o => o is Material)) return; // srcFieldに任せる
                var rend = DragAndDrop.objectReferences.Select(FindRendererFromDraggedObject).FirstOrDefault(r => r != null);
                if (rend == null) return;
                DragAndDrop.AcceptDrag();
                SetShadowSyncSourceFromRenderer(rend, 0);
                evt.StopPropagation();
            });

            pane.Add(srcCard);

            // ── プリセット棚（検索＋カテゴリ折りたたみ）──
            var presetRow = new VisualElement();
            presetRow.style.flexDirection     = FlexDirection.Column;
            presetRow.style.paddingLeft       = 8;
            presetRow.style.paddingRight      = 8;
            presetRow.style.paddingTop        = 5;
            presetRow.style.paddingBottom     = 5;
            presetRow.style.flexShrink        = 0;
            presetRow.style.borderBottomWidth = 1;
            presetRow.style.borderBottomColor = PaneBorderColor;
            presetRow.style.backgroundColor   = ShadowSyncRow1Bg;

            var presetHdr = new VisualElement();
            presetHdr.style.flexDirection = FlexDirection.Row;
            presetHdr.style.alignItems    = Align.Center;
            presetHdr.style.marginBottom  = 4;

            var presetLbl = new Label("プリセット");
            presetLbl.style.fontSize                = 11;
            presetLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            presetLbl.style.color                   = TextColor;
            presetLbl.style.flexGrow                = 1;
            presetHdr.Add(presetLbl);

            var presetHintLbl = new Label("右クリックでリネーム・削除");
            presetHintLbl.style.fontSize   = 9;
            presetHintLbl.style.color      = DimColor;
            presetHintLbl.style.flexShrink = 0;
            presetHdr.Add(presetHintLbl);

            presetRow.Add(presetHdr);

            var presetScroll = new ScrollView(ScrollViewMode.Vertical);
            presetScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            presetScroll.style.maxHeight = 180;
            presetScroll.style.flexShrink = 0;

            _shadowPresetListEl = new VisualElement();
            _shadowPresetListEl.style.flexDirection = FlexDirection.Column;
            presetScroll.Add(_shadowPresetListEl);
            presetRow.Add(presetScroll);

            pane.Add(presetRow);

            // ── Body: 左=色選択+パラメータ / 右=コピー先 ──
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow      = 1;
            body.style.minHeight     = 0;

            // ── 左ペイン ──
            var leftPane = new VisualElement();
            leftPane.style.flexGrow         = 1f;
            leftPane.style.flexBasis        = new StyleLength(0f);
            leftPane.style.flexDirection    = FlexDirection.Column;
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = PaneBorderColor;

            var leftScroll = new ScrollView(ScrollViewMode.Vertical);
            leftScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            leftScroll.style.flexGrow  = 1;
            leftScroll.style.minHeight = 0;
            leftScroll.contentContainer.style.flexGrow = 1;

            // ─ 全項目トグル ─
            var masterRow = new VisualElement();
            masterRow.style.flexDirection     = FlexDirection.Row;
            masterRow.style.alignItems        = Align.Center;
            masterRow.style.paddingLeft       = 6;
            masterRow.style.paddingRight      = 8;
            masterRow.style.paddingTop        = 4;
            masterRow.style.paddingBottom     = 4;
            masterRow.style.borderBottomWidth = 1;
            masterRow.style.borderBottomColor = PaneBorderColor;

            _shadowSyncMasterToggle = new Toggle();
            _shadowSyncMasterToggle.value = false;
            _shadowSyncMasterToggle.style.marginRight = 2;
            _shadowSyncMasterToggle.style.flexShrink  = 0;
            _shadowSyncMasterToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
            _shadowSyncMasterToggle.RegisterValueChangedCallback(evt =>
            {
                _shadowSyncParams.Clear();
                if (evt.newValue)
                    foreach (var p in SHADOW_PARAMS)
                        if (IsShadowPropAvailable(p.Prop)) _shadowSyncParams.Add(p.Prop);
                SyncShadowTogglesFromParams();
                RefreshShadowSyncSwatches();
            });
            masterRow.Add(_shadowSyncMasterToggle);

            var masterLbl = new Label("全項目を同期");
            masterLbl.style.fontSize                = 11;
            masterLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            masterLbl.style.color                   = TextColor;
            masterLbl.style.marginLeft              = 3;
            masterRow.Add(masterLbl);

            leftScroll.Add(masterRow);


            // ─ 影設定 フォールドアウトカテゴリ ─

            var shadowSettingsContent = new VisualElement();
            shadowSettingsContent.style.display = DisplayStyle.None;
            bool[] shadowSettingsOpen = { false };

            var shadowSettingsHdr = new VisualElement();
            shadowSettingsHdr.style.flexDirection   = FlexDirection.Row;
            shadowSettingsHdr.style.alignItems      = Align.Center;
            shadowSettingsHdr.style.paddingLeft     = 6;
            shadowSettingsHdr.style.paddingRight    = 8;
            shadowSettingsHdr.style.paddingTop      = 5;
            shadowSettingsHdr.style.paddingBottom   = 5;
            shadowSettingsHdr.style.borderTopWidth    = 1;
            shadowSettingsHdr.style.borderTopColor    = PaneBorderColor;
            shadowSettingsHdr.style.borderBottomWidth = 1;
            shadowSettingsHdr.style.borderBottomColor = PaneBorderColor;

            _shadowSyncShadowGroupToggle = new Toggle();
            var shadowSettingsGroupToggle = _shadowSyncShadowGroupToggle;
            shadowSettingsGroupToggle.value = false;
            shadowSettingsGroupToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
            shadowSettingsGroupToggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            shadowSettingsGroupToggle.RegisterValueChangedCallback(evt =>
            {
                foreach (var p in SHADOW_PARAMS.Where(p => p.Group == ShadowParamGroup.ShadowSettings))
                {
                    if (evt.newValue && IsShadowPropAvailable(p.Prop)) _shadowSyncParams.Add(p.Prop);
                    else                                               _shadowSyncParams.Remove(p.Prop);
                }
                shadowSettingsContent.Query<Toggle>().Build()
                    .ForEach(t => t.SetValueWithoutNotify(evt.newValue));
                UpdateShadowGroupToggles();
            });
            shadowSettingsHdr.Add(shadowSettingsGroupToggle);

            var shadowSettingsTitle = new Label("影");
            shadowSettingsTitle.style.fontSize                = 11;
            shadowSettingsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            shadowSettingsTitle.style.color                   = TextColor;
            shadowSettingsTitle.style.flexGrow                = 1;
            shadowSettingsTitle.style.marginLeft              = 3;
            shadowSettingsHdr.Add(shadowSettingsTitle);
            AddShadowHeaderSwatches(shadowSettingsHdr, shadowSettingsTitle, true, "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor", "_ShadowBorderColor");

            var shadowChevron = new Label("▶");
            shadowChevron.style.fontSize    = 10;
            shadowChevron.style.color       = DimColor;
            shadowChevron.style.flexShrink  = 0;
            shadowChevron.style.paddingLeft  = 6;
            shadowChevron.style.paddingRight = 4;
            shadowChevron.style.paddingTop   = 2;
            shadowChevron.style.paddingBottom = 2;
            shadowChevron.RegisterCallback<MouseDownEvent>(e =>
            {
                shadowSettingsOpen[0] = !shadowSettingsOpen[0];
                shadowSettingsContent.style.display = shadowSettingsOpen[0]
                    ? DisplayStyle.Flex : DisplayStyle.None;
                shadowChevron.text = shadowSettingsOpen[0] ? "▼" : "▶";
                e.StopPropagation();
            });
            shadowSettingsHdr.Add(shadowChevron);

            leftScroll.Add(shadowSettingsHdr);

            // ── 影色（影の中の小項目1: 単一チェックボックスのみ）──
            {
                var shadowColorProps = new[] { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor", "_ShadowBorderColor" };

                var shadowColorHdr = new VisualElement();
                shadowColorHdr.style.flexDirection     = FlexDirection.Row;
                shadowColorHdr.style.alignItems        = Align.Center;
                shadowColorHdr.style.paddingLeft       = 20;
                shadowColorHdr.style.paddingRight      = 8;
                shadowColorHdr.style.paddingTop        = 4;
                shadowColorHdr.style.paddingBottom     = 4;
                shadowColorHdr.style.borderBottomWidth = 1;
                shadowColorHdr.style.borderBottomColor = PaneBorderColor;

                _shadowColorGroupToggle = new Toggle();
                var shadowColorToggle = _shadowColorGroupToggle;
                shadowColorToggle.value = false;
                shadowColorToggle.style.marginRight = 2;
                shadowColorToggle.style.flexShrink  = 0;
                shadowColorToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                shadowColorToggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                shadowColorToggle.RegisterValueChangedCallback(evt =>
                {
                    foreach (var p in shadowColorProps)
                    {
                        if (evt.newValue && IsShadowPropAvailable(p)) _shadowSyncParams.Add(p);
                        else                                          _shadowSyncParams.Remove(p);
                    }
                    UpdateShadowGroupToggles();
                    RefreshShadowSyncSwatches();
                });
                shadowColorHdr.Add(shadowColorToggle);

                var shadowColorTitle = new Label("影色");
                shadowColorTitle.style.fontSize   = 10;
                shadowColorTitle.style.color      = DimColor;
                shadowColorTitle.style.marginLeft = 3;
                shadowColorHdr.Add(shadowColorTitle);

                AddShadowHeaderSwatches(shadowColorHdr, shadowColorTitle, false, shadowColorProps);

                shadowSettingsContent.Add(shadowColorHdr);
            }

            // ── 影設定（影の中の小項目2: 単一チェックボックスのみ）──
            {
                var shadowNumProps = SHADOW_PARAMS
                    .Where(p => p.Group == ShadowParamGroup.ShadowSettings && p.Type != ShadowParamType.Color)
                    .Select(p => p.Prop).ToArray();

                var shadowNumHdr = new VisualElement();
                shadowNumHdr.style.flexDirection     = FlexDirection.Row;
                shadowNumHdr.style.alignItems        = Align.Center;
                shadowNumHdr.style.paddingLeft       = 20;
                shadowNumHdr.style.paddingRight      = 8;
                shadowNumHdr.style.paddingTop        = 4;
                shadowNumHdr.style.paddingBottom     = 4;
                shadowNumHdr.style.borderBottomWidth = 1;
                shadowNumHdr.style.borderBottomColor = PaneBorderColor;

                _shadowNumGroupToggle = new Toggle();
                var shadowNumToggle = _shadowNumGroupToggle;
                shadowNumToggle.value = false;
                shadowNumToggle.style.marginRight = 2;
                shadowNumToggle.style.flexShrink  = 0;
                shadowNumToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                shadowNumToggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                shadowNumToggle.RegisterValueChangedCallback(evt =>
                {
                    foreach (var p in shadowNumProps)
                    {
                        if (evt.newValue && IsShadowPropAvailable(p)) _shadowSyncParams.Add(p);
                        else                                          _shadowSyncParams.Remove(p);
                    }
                    UpdateShadowGroupToggles();
                });
                shadowNumHdr.Add(shadowNumToggle);

                var shadowNumTitle = new Label("影数値");
                shadowNumTitle.style.fontSize   = 10;
                shadowNumTitle.style.color      = DimColor;
                shadowNumTitle.style.flexGrow   = 1;
                shadowNumTitle.style.marginLeft = 3;
                shadowNumHdr.Add(shadowNumTitle);

                shadowSettingsContent.Add(shadowNumHdr);
            }

            leftScroll.Add(shadowSettingsContent);

            // ─ リムシェード 単一チェックボックス ─
            leftScroll.Add(MakeShadowGroupHeader("リムシェード", ShadowParamGroup.RimShade, false,
                out _shadowSyncRimshadeGroupToggle, "_RimShadeColor"));

            // ─ リムライト 単一チェックボックス ─
            {
                var rimHdr = MakeShadowGroupHeader("リムライト", ShadowParamGroup.Rim, true,
                    out _shadowSyncRimGroupToggle, "_RimColor");

                _shadowRimBlendModeLabel = new Label("");
                StyleShadowSwatchBadge(_shadowRimBlendModeLabel);
                rimHdr.Add(_shadowRimBlendModeLabel);

                leftScroll.Add(rimHdr);
            }

            // ─ 逆光ライト 単一チェックボックス ─
            {
                var backlightHdr = MakeShadowGroupHeader("逆光ライト", ShadowParamGroup.Backlight, true,
                    out _shadowSyncBacklightGroupToggle, "_BacklightColor");

                _shadowBacklightStrengthLabel = new Label("");
                StyleShadowSwatchBadge(_shadowBacklightStrengthLabel);
                backlightHdr.Add(_shadowBacklightStrengthLabel);

                leftScroll.Add(backlightHdr);
            }

            // ─ 距離フェード 単一チェックボックス ─
            // コピー対象: _DistanceFadeColor, _DistanceFadeRimColor, _DistanceFade,
            //             _DistanceFadeMode, _DistanceFadeRimFresnelPower
            leftScroll.Add(MakeShadowGroupHeader("距離フェード", ShadowParamGroup.DistanceFade, false,
                out _shadowDistanceFadeGroupToggle, "_DistanceFadeColor"));

            leftPane.Add(leftScroll);

            // ── 色相/彩度スライダー（左カラム下端に固定）──
            var hsArea = new VisualElement();
            hsArea.style.flexShrink    = 0;
            hsArea.style.minWidth      = 0;
            hsArea.style.overflow      = Overflow.Hidden;
            hsArea.style.paddingLeft   = 8;
            hsArea.style.paddingRight  = 8;
            hsArea.style.paddingTop    = 6;
            hsArea.style.paddingBottom = 6;
            hsArea.style.borderTopWidth = 1;
            hsArea.style.borderTopColor = PaneBorderColor;
            {
                VisualElement MakeSliderRow(string labelText, float min, float max,
                    float initial, float resetVal,
                    System.Func<float, string> fmt, System.Action<float> onChange)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems    = Align.Center;
                    row.style.marginBottom  = 2;
                    row.style.minWidth      = 0;

                    var lbl = new Label(labelText);
                    lbl.style.fontSize   = 10;
                    lbl.style.width      = 26;
                    lbl.style.flexShrink = 0;
                    row.Add(lbl);

                    var slider = new Slider(min, max) { value = initial };
                    slider.style.flexGrow    = 1;
                    slider.style.flexShrink  = 1;
                    slider.style.minWidth    = 0;
                    slider.style.marginLeft  = 2;
                    slider.style.marginRight = 2;
                    slider.Q<Label>()?.RemoveFromHierarchy();
                    row.Add(slider);

                    var valLbl = new Label(fmt(initial));
                    valLbl.style.fontSize       = 10;
                    valLbl.style.width          = 32;
                    valLbl.style.flexShrink     = 0;
                    valLbl.style.unityTextAlign = TextAnchor.MiddleRight;
                    row.Add(valLbl);

                    float snapRange = (max - min) * 0.03f;
                    slider.RegisterValueChangedCallback(evt =>
                    {
                        float v = evt.newValue;
                        if (v != resetVal && Mathf.Abs(v - resetVal) < snapRange)
                        {
                            v = resetVal;
                            slider.SetValueWithoutNotify(v);
                        }
                        valLbl.text = fmt(v);
                        onChange(v);
                    });
                    return row;
                }

                hsArea.Add(MakeSliderRow("色相", -180f, 180f, _shadowHueShift, 0f,
                    v => $"{v:+0;-0;+0}°",
                    v => { _shadowHueShift = v; RefreshShadowSyncSwatches(); }));

                hsArea.Add(MakeSliderRow("彩度", 0f, 2f, _shadowSaturationScale, 1f,
                    v => $"×{v:0.0}",
                    v => { _shadowSaturationScale = v; RefreshShadowSyncSwatches(); }));
            }
            leftPane.Add(hsArea);

            body.Add(leftPane);

            // ── 右ペイン: コピー先マテリアル ──
            var rightPane = new VisualElement();
            rightPane.style.flexGrow      = 1f;
            rightPane.style.flexBasis     = new StyleLength(0f);
            rightPane.style.flexDirection = FlexDirection.Column;
            rightPane.style.minWidth      = 0;

            var rightHd = new VisualElement();
            rightHd.style.flexDirection     = FlexDirection.Row;
            rightHd.style.alignItems        = Align.Center;
            rightHd.style.paddingLeft       = 8;
            rightHd.style.paddingRight      = 8;
            rightHd.style.paddingTop        = 5;
            rightHd.style.paddingBottom     = 5;

            rightHd.style.borderBottomWidth = 1;
            rightHd.style.borderBottomColor = PaneBorderColor;

            _shadowSyncDestCountLabel = new Label("コピー先 (0/0)");
            _shadowSyncDestCountLabel.style.fontSize                = 12;
            _shadowSyncDestCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _shadowSyncDestCountLabel.style.color                   = TextColor;
            _shadowSyncDestCountLabel.style.flexGrow = 1;
            rightHd.Add(_shadowSyncDestCountLabel);

            _shadowSyncAllSelBtn = new Button(() =>
            {
                var avail = _materials.Where(m => m != null && m != _shadowSyncSource).ToList();
                bool allSel = avail.Count > 0 && avail.All(m => _shadowSyncDestSet.Contains(m));
                if (allSel) _shadowSyncDestSet.Clear();
                else        foreach (var m in avail) _shadowSyncDestSet.Add(m);
                RebuildShadowSyncDestList();
            });
            _shadowSyncAllSelBtn.text             = "全選択";
            _shadowSyncAllSelBtn.style.marginLeft = 4;
            _shadowSyncAllSelBtn.style.fontSize   = 10;
            _shadowSyncAllSelBtn.style.height     = 20;
            rightHd.Add(_shadowSyncAllSelBtn);

            rightPane.Add(rightHd);

            var destScroll = new ScrollView();
            destScroll.style.flexGrow  = 1;
            destScroll.style.minHeight = 0;
            _shadowSyncDestListEl      = new VisualElement();
            destScroll.Add(_shadowSyncDestListEl);
            rightPane.Add(destScroll);

            body.Add(rightPane);
            pane.Add(body);

            // ── Footer ──
            var footer = new VisualElement();
            footer.style.flexDirection  = FlexDirection.Row;
            footer.style.alignItems     = Align.Center;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.paddingLeft    = 10;
            footer.style.paddingRight   = 10;
            footer.style.paddingTop     = 8;
            footer.style.paddingBottom  = 8;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = PaneBorderColor;

            _shadowSyncCreatePresetBtn = new Button(OpenShadowPresetSaveWindow);
            _shadowSyncCreatePresetBtn.text = "プリセット保存";
            _shadowSyncCreatePresetBtn.style.height      = 28;
            _shadowSyncCreatePresetBtn.style.minHeight   = 28;
            _shadowSyncCreatePresetBtn.style.maxHeight   = 28;
            _shadowSyncCreatePresetBtn.style.flexShrink  = 0;
            _shadowSyncCreatePresetBtn.style.paddingLeft  = _shadowSyncCreatePresetBtn.style.paddingRight = 12;
            _shadowSyncCreatePresetBtn.style.marginRight  = new StyleLength(StyleKeyword.Auto);
            _shadowSyncCreatePresetBtn.style.fontSize     = 12;
            _shadowSyncCreatePresetBtn.style.whiteSpace   = WhiteSpace.NoWrap;
            _shadowSyncCreatePresetBtn.SetEnabled(false);
            footer.Add(_shadowSyncCreatePresetBtn);

            _shadowSyncSyncBtn              = new Button(ExecuteShadowSync);
            _shadowSyncSyncBtn.text         = "同期";
            _shadowSyncSyncBtn.style.height = 28;
            _shadowSyncSyncBtn.style.minHeight    = 28;
            _shadowSyncSyncBtn.style.maxHeight    = 28;
            _shadowSyncSyncBtn.style.paddingLeft  = 18;
            _shadowSyncSyncBtn.style.paddingRight = 18;
            _shadowSyncSyncBtn.style.fontSize     = 12;
            _shadowSyncSyncBtn.style.flexShrink   = 1;
            _shadowSyncSyncBtn.style.maxWidth     = 220;
            _shadowSyncSyncBtn.style.overflow     = Overflow.Hidden;
            _shadowSyncSyncBtn.style.textOverflow = TextOverflow.Ellipsis;
            _shadowSyncSyncBtn.style.whiteSpace   = WhiteSpace.NoWrap;
            _shadowSyncSyncBtn.SetEnabled(false);
            footer.Add(_shadowSyncSyncBtn);

            pane.Add(footer);

            LoadBuiltinPresets();
            LoadLilToonPresets();
            RebuildShadowPresetList();
            RefreshShadowSyncSourceCard();
            UpdateShadowGroupToggles();

            return pane;
        }

        private void AddShadowHeaderSwatches(VisualElement hdr, Label title, bool allowTitleShrink, params string[] props)
        {
            title.style.flexGrow = 0;
            if (allowTitleShrink)
            {
                // 幅が足りない時はタイトルを縮めて省略表示し、右側のスウォッチ／バッジ（矢印含む）が見切れないようにする
                title.style.flexShrink   = 1;
                title.style.minWidth     = 0;
                title.style.overflow     = Overflow.Hidden;
                title.style.textOverflow = TextOverflow.Ellipsis;
                title.style.whiteSpace   = WhiteSpace.NoWrap;
            }
            foreach (var prop in props)
            {
                var box = MakeShadowSwatchVisual(26, 4, out var update);
                box.style.marginLeft = 3;
                bool has = TryGetRawShadowColor(prop, out var raw);
                update(has ? AdjustColor(raw, _shadowHueShift, _shadowSaturationScale) : new Color(0.25f, 0.25f, 0.25f), has && raw.a <= 0.0001f);
                RegisterShadowColorBox(prop, update);
                hdr.Add(box);
            }
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            hdr.Add(spacer);
        }

        // リムシェード/リムライト/逆光ライト/距離フェードで共通の「単一チェックボックス」見出し行を作る。
        // タイトルの後ろに任意でバッジ（リムの合成モード表示等）を追加したい場合は、戻り値に対して呼び出し側でhdr.Add(...)する。
        private VisualElement MakeShadowGroupHeader(string title, ShadowParamGroup group, bool allowTitleShrink,
            out Toggle groupToggle, string swatchProp)
        {
            var hdr = new VisualElement();
            hdr.style.flexDirection     = FlexDirection.Row;
            hdr.style.alignItems        = Align.Center;
            hdr.style.paddingLeft       = 6;
            hdr.style.paddingRight      = 8;
            hdr.style.paddingTop        = 5;
            hdr.style.paddingBottom     = 5;
            hdr.style.borderTopWidth    = 1;
            hdr.style.borderTopColor    = PaneBorderColor;
            hdr.style.borderBottomWidth = 1;
            hdr.style.borderBottomColor = PaneBorderColor;

            var toggle = new Toggle();
            toggle.value = false;
            toggle.style.marginRight = 2;
            toggle.style.flexShrink  = 0;
            toggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
            toggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            toggle.RegisterValueChangedCallback(evt =>
            {
                foreach (var p in SHADOW_PARAMS.Where(p => p.Group == group))
                {
                    if (evt.newValue && IsShadowPropAvailable(p.Prop)) _shadowSyncParams.Add(p.Prop);
                    else                                               _shadowSyncParams.Remove(p.Prop);
                }
                UpdateShadowGroupToggles();
            });
            hdr.Add(toggle);
            groupToggle = toggle;

            var titleLbl = new Label(title);
            titleLbl.style.fontSize                = 11;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color                   = TextColor;
            titleLbl.style.marginLeft              = 3;
            hdr.Add(titleLbl);

            AddShadowHeaderSwatches(hdr, titleLbl, allowTitleShrink, swatchProp);

            return hdr;
        }

        // HDR等で1.0を超える色をスウォッチ表示用に正規化する（比率を保ったまま最大成分を1.0に収める）
        private static Color NormalizeForSwatch(Color c)
        {
            float m = Mathf.Max(c.r, Mathf.Max(c.g, Mathf.Max(c.b, 1f)));
            if (m > 1f) { c.r /= m; c.g /= m; c.b /= m; }
            return c;
        }

        // コピー元マテリアル／選択中プリセットから、補正前の生の色を取得する
        private bool TryGetRawShadowColor(string prop, out Color raw)
        {
            if (_shadowSyncSource != null && _shadowSyncSource.HasProperty(prop))
            {
                raw = _shadowSyncSource.GetColor(prop);
                return true;
            }
            if (_activeShadowPreset != null)
            {
                var p = _activeShadowPreset.props.FirstOrDefault(x => x.key == prop);
                if (p != null) { raw = new Color(p.r, p.g, p.b, p.a); return true; }
            }
            raw = default;
            return false;
        }

        // 不透明度0の色スウォッチに「なし」を表示する枠を作る（isNone=true時は色を無視してグレー＋テキスト表示）
        // showAlphaLabel=falseの場合、右下の不透明度%表示を出さない（プリセット棚など小さいスウォッチ向け）
        private VisualElement MakeShadowSwatchVisual(float size, float radius, out Action<Color, bool> update, bool showAlphaLabel = true)
        {
            var wrap = new VisualElement();
            wrap.style.width       = size;
            wrap.style.height      = size;
            wrap.style.flexShrink  = 0;
            wrap.style.overflow    = Overflow.Hidden;
            wrap.style.alignItems       = Align.Center;
            wrap.style.justifyContent   = Justify.Center;
            wrap.style.borderTopLeftRadius = wrap.style.borderTopRightRadius =
                wrap.style.borderBottomLeftRadius = wrap.style.borderBottomRightRadius = radius;
            wrap.style.borderTopWidth = wrap.style.borderBottomWidth =
                wrap.style.borderLeftWidth = wrap.style.borderRightWidth = 1;
            wrap.style.borderTopColor = wrap.style.borderBottomColor =
                wrap.style.borderLeftColor = wrap.style.borderRightColor = PaneBorderColor;

            var noneLbl = new Label("なし");
            noneLbl.style.fontSize = Mathf.Min(8, size * 0.35f);
            noneLbl.style.color    = DimColor;
            noneLbl.style.display  = DisplayStyle.None;
            noneLbl.pickingMode    = PickingMode.Ignore;
            wrap.Add(noneLbl);

            // 不透明度を数字で右下に重ねて表示する（スウォッチの色自体は常に不透明で表示）
            Label alphaLbl = null;
            if (showAlphaLabel)
            {
                alphaLbl = new Label("");
                alphaLbl.style.position        = Position.Absolute;
                alphaLbl.style.right           = 0;
                alphaLbl.style.bottom           = 0;
                alphaLbl.style.fontSize        = Mathf.Min(10, size * 0.4f);
                alphaLbl.style.color           = Color.white;
                alphaLbl.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
                alphaLbl.style.paddingLeft     = alphaLbl.style.paddingRight = 2;
                alphaLbl.style.display         = DisplayStyle.None;
                alphaLbl.pickingMode           = PickingMode.Ignore;
                wrap.Add(alphaLbl);
            }

            update = (color, isNone) =>
            {
                if (isNone)
                {
                    wrap.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
                    noneLbl.style.display  = DisplayStyle.Flex;
                    if (alphaLbl != null) alphaLbl.style.display = DisplayStyle.None;
                    return;
                }
                var opaque = NormalizeForSwatch(color);
                opaque.a = 1f;
                wrap.style.backgroundColor = opaque;
                noneLbl.style.display = DisplayStyle.None;

                if (alphaLbl != null)
                {
                    int pct = Mathf.RoundToInt(Mathf.Clamp01(color.a) * 100f);
                    if (pct >= 100)
                    {
                        alphaLbl.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        alphaLbl.text = pct.ToString();
                        alphaLbl.style.display = DisplayStyle.Flex;
                    }
                }
            };
            return wrap;
        }

        // 同じプロパティのスウォッチが複数箇所（見出し＋個別行）にある場合、全てを一括登録して同時に更新できるようにする
        private void RegisterShadowColorBox(string prop, Action<Color, bool> update)
        {
            if (!_shadowSyncColorBoxes.TryGetValue(prop, out var list))
            {
                list = new List<Action<Color, bool>>();
                _shadowSyncColorBoxes[prop] = list;
            }
            list.Add(update);
        }

        private void SyncShadowTogglesFromParams()
        {
            foreach (var p in SHADOW_PARAMS)
                if (_shadowSyncParamToggles.TryGetValue(p.Prop, out var t))
                    t.SetValueWithoutNotify(_shadowSyncParams.Contains(p.Prop));
            UpdateShadowGroupToggles();
        }

        // コピー元マテリアル／選択中プリセットに実際に値が存在するプロパティかどうか
        // （プリセットの場合は保存されている項目だけ、マテリアルの場合はシェーダーが持つ項目だけが true）
        private bool IsShadowPropAvailable(string prop)
        {
            if (_shadowSyncSource != null) return _shadowSyncSource.HasProperty(prop);
            if (_activeShadowPreset != null) return _activeShadowPreset.props.Any(x => x.key == prop);
            return false;
        }

        // 個別チェックの変更に合わせてカテゴリ見出し・全項目トグルを追従させる。
        // 存在しない項目はチェックできないようグレーアウトし、チェック状態も「存在する項目が全部ONか」で判定する。
        private void UpdateShadowGroupToggles()
        {
            void Apply(Toggle t, Func<ShadowParam, bool> predicate)
            {
                if (t == null) return;
                var avail = SHADOW_PARAMS.Where(x => predicate(x) && IsShadowPropAvailable(x.Prop)).ToList();
                t.SetEnabled(avail.Count > 0);
                t.SetValueWithoutNotify(avail.Count > 0 && avail.All(x => _shadowSyncParams.Contains(x.Prop)));
            }

            Apply(_shadowSyncShadowGroupToggle,     x => x.Group == ShadowParamGroup.ShadowSettings);
            Apply(_shadowColorGroupToggle,          x => x.Group == ShadowParamGroup.ShadowSettings && x.Type == ShadowParamType.Color);
            Apply(_shadowNumGroupToggle,            x => x.Group == ShadowParamGroup.ShadowSettings && x.Type != ShadowParamType.Color);
            Apply(_shadowSyncRimshadeGroupToggle,   x => x.Group == ShadowParamGroup.RimShade);
            Apply(_shadowSyncRimGroupToggle,        x => x.Group == ShadowParamGroup.Rim);
            Apply(_shadowSyncBacklightGroupToggle,  x => x.Group == ShadowParamGroup.Backlight);
            Apply(_shadowDistanceFadeGroupToggle,   x => x.Group == ShadowParamGroup.DistanceFade);

            if (_shadowSyncMasterToggle != null)
            {
                var avail = SHADOW_PARAMS.Where(x => IsShadowPropAvailable(x.Prop)).ToList();
                _shadowSyncMasterToggle.SetEnabled(avail.Count > 0);
                _shadowSyncMasterToggle.SetValueWithoutNotify(avail.Count > 0 && avail.All(x => _shadowSyncParams.Contains(x.Prop)));
            }
        }

        // ドラッグされたオブジェクトからRenderer（自身、または子）を探す
        private static Renderer FindRendererFromDraggedObject(UnityEngine.Object o)
        {
            if (o is Renderer r) return r;
            if (o is GameObject go) return go.GetComponent<Renderer>() ?? go.GetComponentInChildren<Renderer>();
            return null;
        }

        // コピー元マテリアルを実際に切り替える（同期対象の全項目ONもここでまとめて行う）
        private void ApplyShadowSyncSourceMaterial(Material m)
        {
            _shadowSyncSource = m;
            if (_shadowSyncSource != null && _activeShadowPreset != null)
            {
                _activeShadowPreset = null;
                RebuildShadowPresetList();
            }
            if (_shadowSyncSource != null)
            {
                // コピー元選択＝まるごと同期（シェーダーに存在する項目を全項目ON）
                _shadowSyncParams.Clear();
                foreach (var mp in SHADOW_PARAMS)
                    if (_shadowSyncSource.HasProperty(mp.Prop)) _shadowSyncParams.Add(mp.Prop);
            }
            SyncShadowTogglesFromParams();
            RefreshShadowSyncSwatches();
            RefreshShadowSyncSyncButton();
            RebuildShadowSyncDestList();
            RefreshShadowSyncSourceCard();
            if (_shadowSyncCreatePresetBtn != null)
                _shadowSyncCreatePresetBtn.SetEnabled(_shadowSyncSource != null);
        }

        // レンダラーをコピー元にする。マテリアルが複数ある場合はスロットを指定できる（デフォルト0＝一番上）
        private void SetShadowSyncSourceFromRenderer(Renderer r, int slot)
        {
            var mats = r.sharedMaterials;
            if (mats.Length == 0) return;
            if (slot < 0 || slot >= mats.Length) slot = 0;
            _shadowSyncSourceRenderer = r;
            _shadowSyncSourceSlot     = slot;
            ApplyShadowSyncSourceMaterial(mats[slot]);
        }

        // レンダラーが複数マテリアルを持つ場合に、選択肢を出すトグルリスト（ポップアップメニュー）
        private void ShowShadowSyncSlotMenu(Renderer r)
        {
            var mats = r.sharedMaterials;
            var menu = new GenericMenu();
            for (int i = 0; i < mats.Length; i++)
            {
                int idx = i;
                string label = $"{i}: {(mats[i] != null ? mats[i].name : "(なし)")}";
                menu.AddItem(new GUIContent(label), idx == _shadowSyncSourceSlot, () => SetShadowSyncSourceFromRenderer(r, idx));
            }
            menu.ShowAsContext();
        }

        private void RefreshShadowSyncSourceCard()
        {
            _shadowSyncSourceField?.SetValueWithoutNotify(_shadowSyncSource);
            if (_shadowSyncSlotButton != null)
            {
                bool multi = _shadowSyncSourceRenderer != null && _shadowSyncSourceRenderer.sharedMaterials.Length > 1;
                _shadowSyncSlotButton.style.display = multi ? DisplayStyle.Flex : DisplayStyle.None;
                if (multi) _shadowSyncSlotButton.text = $"スロット{_shadowSyncSourceSlot} ▾";
            }
        }

        private void RefreshShadowSyncSwatches()
        {
            foreach (var sp in SHADOW_PARAMS.Where(p => p.Type == ShadowParamType.Color))
            {
                if (!_shadowSyncColorBoxes.TryGetValue(sp.Prop, out var updaters)) continue;
                bool has = TryGetRawShadowColor(sp.Prop, out var raw);
                bool isNone = has && raw.a <= 0.0001f;
                var color = has ? AdjustColor(raw, _shadowHueShift, _shadowSaturationScale) : new Color(0.25f, 0.25f, 0.25f);
                foreach (var u in updaters) u(color, isNone);
            }

            if (_shadowBacklightStrengthLabel != null)
            {
                float? strength = null;
                if (TryGetRawShadowColor("_BacklightColor", out var bc)) strength = Mathf.Max(bc.r, Mathf.Max(bc.g, bc.b));
                _shadowBacklightStrengthLabel.text = strength.HasValue ? $"強さ {strength.Value:0.0}" : "";
                _shadowBacklightStrengthLabel.style.display = strength.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_shadowRimBlendModeLabel != null)
            {
                string modeText = null;
                if (_shadowSyncSource != null && _shadowSyncSource.HasProperty("_RimBlendMode"))
                    modeText = RimBlendModeLabel(_shadowSyncSource.GetInt("_RimBlendMode"));
                else if (_activeShadowPreset != null)
                {
                    var pp = _activeShadowPreset.props.FirstOrDefault(x => x.key == "_RimBlendMode");
                    if (pp != null) modeText = RimBlendModeLabel((int)pp.value);
                }
                _shadowRimBlendModeLabel.text = modeText ?? "";
                _shadowRimBlendModeLabel.style.display = modeText != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // 逆光の強さ／リムの加算・乗算など、スウォッチ横の補足ラベルを共通の見た目にする
        private static void StyleShadowSwatchBadge(Label lbl)
        {
            lbl.style.fontSize        = 10;
            lbl.style.color           = TextColor;
            lbl.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.16f)
                : new Color(0f, 0f, 0f, 0.14f);
            lbl.style.paddingLeft     = lbl.style.paddingRight  = 6;
            lbl.style.paddingTop      = lbl.style.paddingBottom = 2;
            lbl.style.marginLeft      = 4;
            lbl.style.borderTopLeftRadius     = lbl.style.borderTopRightRadius    =
                lbl.style.borderBottomLeftRadius = lbl.style.borderBottomRightRadius = 8;
            lbl.style.unityTextAlign  = TextAnchor.MiddleCenter;
            lbl.style.flexShrink      = 0;
            lbl.style.display         = DisplayStyle.None;
        }

        private static string RimBlendModeLabel(int mode)
        {
            switch (mode)
            {
                case 0: return "通常";
                case 1: return "加算";
                case 2: return "スクリーン";
                case 3: return "乗算";
                default: return null;
            }
        }

        // 影同期の各カテゴリの見出し文言（本体UIのカテゴリ見出しと同じ表記に揃える）
        private static string ShadowGroupLabel(ShadowParamGroup g)
        {
            switch (g)
            {
                case ShadowParamGroup.ShadowSettings: return "影";
                case ShadowParamGroup.RimShade:        return "リムシェード";
                case ShadowParamGroup.Rim:              return "リムライト";
                case ShadowParamGroup.Backlight:        return "逆光ライト";
                case ShadowParamGroup.DistanceFade:     return "距離フェード";
                default:                                return "その他";
            }
        }

        private static Color AdjustColor(Color c, float hueShift, float satScale)
        {
            if (hueShift == 0f && satScale == 1f) return c;
            float a = c.a;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hueShift / 360f, 1f);
            s = Mathf.Clamp01(s * satScale);
            var result = Color.HSVToRGB(h, s, v, hdr: true);
            result.a = a;
            return result;
        }

        private void RebuildShadowSyncDestList()
        {
            if (_shadowSyncDestListEl == null) return;
            _shadowSyncDestListEl.Clear();

            var candidates = _materials.Where(m => m != null && m != _shadowSyncSource).ToList();
            _shadowSyncDestSet.IntersectWith(candidates);
            int total = candidates.Count;

            var thumbImages = new Dictionary<Material, Image>();
            var pending     = new HashSet<Material>();
            bool dark = EditorGUIUtility.isProSkin;

            foreach (var mat in candidates)
            {
                var matRef = mat;

                var row = new VisualElement();
                row.style.flexDirection     = FlexDirection.Row;
                row.style.alignItems        = Align.FlexStart;
                row.style.paddingLeft       = 6;
                row.style.paddingRight      = 8;
                row.style.paddingTop        = 5;
                row.style.paddingBottom     = 5;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = PaneBorderColor;
                row.RegisterCallback<MouseDownEvent>(_ => EditorGUIUtility.PingObject(matRef));

                var chk = new Toggle();
                chk.value             = _shadowSyncDestSet.Contains(mat);
                chk.style.marginRight = 4;
                chk.style.marginTop   = 7;
                chk.style.flexShrink  = 0;
                chk.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                chk.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                chk.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _shadowSyncDestSet.Add(matRef);
                    else              _shadowSyncDestSet.Remove(matRef);
                    UpdateShadowSyncDestCount(total);
                    RefreshShadowSyncSyncButton();
                });
                row.Add(chk);

                var thumb = new Image();
                thumb.style.width       = 32;
                thumb.style.height      = 32;
                thumb.style.flexShrink  = 0;
                thumb.style.marginRight = 6;
                thumb.style.marginTop   = 2;
                thumb.style.borderTopLeftRadius    = thumb.style.borderTopRightRadius    =
                    thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 3;
                thumb.style.backgroundColor = dark
                    ? new Color(0.18f, 0.18f, 0.20f, 1f)
                    : new Color(0.78f, 0.78f, 0.80f, 1f);
                var previewTex = AssetPreview.GetAssetPreview(mat);
                if (previewTex != null)
                    thumb.image = previewTex;
                else
                {
                    thumb.image = AssetPreview.GetMiniThumbnail(mat);
                    thumbImages[mat] = thumb;
                    pending.Add(mat);
                }
                row.Add(thumb);

                var rightCol = new VisualElement();
                rightCol.style.flexGrow   = 1;
                rightCol.style.flexShrink = 1;
                rightCol.style.minWidth   = 0;

                var nameLbl = new Label(mat.name);
                nameLbl.style.fontSize                = 11;
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.color                   = TextColor;
                nameLbl.style.overflow                = Overflow.Hidden;
                rightCol.Add(nameLbl);

                if (_materialUsage.TryGetValue(mat, out var usages) && usages.Count > 0)
                {
                    var rendRow = new VisualElement();
                    rendRow.style.flexDirection = FlexDirection.Row;
                    rendRow.style.alignItems    = Align.Center;

                    var r0 = MakeRendererField(usages[0].renderer);
                    r0.style.flexGrow = 1;
                    rendRow.Add(r0);

                    if (usages.Count > 1)
                    {
                        bool rendExpanded = false;
                        var extraList = new VisualElement();
                        extraList.style.display = DisplayStyle.None;
                        for (int i = 1; i < usages.Count; i++)
                            extraList.Add(MakeRendererField(usages[i].renderer));

                        var expandBtn = new Label($"+{usages.Count - 1}件 ▼");
                        expandBtn.style.fontSize   = 10;
                        expandBtn.style.color      = AccentColor;
                        expandBtn.style.marginLeft = 6;
                        expandBtn.style.flexShrink = 0;
                        int cnt = usages.Count;
                        expandBtn.RegisterCallback<MouseDownEvent>(evt =>
                        {
                            rendExpanded = !rendExpanded;
                            extraList.style.display = rendExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                            expandBtn.text = rendExpanded
                                ? $"−{cnt - 1}件 ▲"
                                : $"+{cnt - 1}件 ▼";
                            evt.StopPropagation();
                        });
                        rendRow.Add(expandBtn);
                        rightCol.Add(rendRow);
                        rightCol.Add(extraList);
                    }
                    else
                    {
                        rightCol.Add(rendRow);
                    }
                }

                row.Add(rightCol);
                _shadowSyncDestListEl.Add(row);
            }

            if (pending.Count > 0)
            {
                _shadowSyncDestListEl.schedule.Execute(() =>
                {
                    pending.RemoveWhere(m =>
                    {
                        var p = AssetPreview.GetAssetPreview(m);
                        if (p == null) return false;
                        if (thumbImages.TryGetValue(m, out var img)) img.image = p;
                        return true;
                    });
                }).Every(200).Until(() => pending.Count == 0);
            }

            UpdateShadowSyncDestCount(total);
            RefreshShadowSyncSyncButton();
            RefreshShadowSyncSwatches();
        }

        private void UpdateShadowSyncDestCount(int total)
        {
            if (_shadowSyncDestCountLabel == null) return;
            _shadowSyncDestCountLabel.text = $"コピー先 ({_shadowSyncDestSet.Count}/{total})";
            RefreshAllSelBtn(total);
        }

        private void RefreshAllSelBtn(int total)
        {
            if (_shadowSyncAllSelBtn == null) return;
            bool allSel = total > 0 && _shadowSyncDestSet.Count >= total;
            _shadowSyncAllSelBtn.text = allSel ? "全解除" : "全選択";
        }

        // 自作プリセット（ShadowSyncPresetAsset）をプロジェクト内から検索して読み込む。lilToonPresetと同じ方式。
        private const string PACKAGE_PRESET_PATH_PREFIX = "Packages/jp.qsyi.qs-toolbox/";

        private void LoadBuiltinPresets()
        {
            _builtinPresets.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:ShadowSyncPresetAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var so   = AssetDatabase.LoadAssetAtPath<ShadowSyncPresetAsset>(path);
                if (so == null) continue;
                _builtinPresets.Add(new ShadowSyncPresetEntry
                {
                    name      = so.name,
                    category  = so.category,
                    props     = so.props,
                    assetPath = path,
                    isBundled = path.StartsWith(PACKAGE_PRESET_PATH_PREFIX),
                });
            }
        }

        // lilToon プリセット（lilToonPreset アセット）から影系プロパティを抽出して取り込む。
        private void LoadLilToonPresets()
        {
            _lilToonPresets.Clear();
            var shadowKeys = new HashSet<string>(SHADOW_PARAMS.Select(p => p.Prop));
            foreach (var guid in AssetDatabase.FindAssets("t:lilToonPreset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/jp.lilxyzw.liltoon")) continue; // lilToon標準プリセットは表示しない
                var lp = AssetDatabase.LoadAssetAtPath<lilToonPreset>(path);
                if (lp == null) continue;
                var entry = new ShadowSyncPresetEntry
                {
                    name     = LilPresetName(lp),
                    category = "lilToon・" + LilCategoryLabel(lp.category.ToString()),
                };
                if (lp.colors != null)
                    foreach (var c in lp.colors)
                        if (shadowKeys.Contains(c.name))
                            entry.props.Add(new ShadowSyncPresetProp { key = c.name, r = c.value.r, g = c.value.g, b = c.value.b, a = c.value.a });
                if (lp.floats != null)
                    foreach (var f in lp.floats)
                        if (shadowKeys.Contains(f.name))
                            entry.props.Add(new ShadowSyncPresetProp { key = f.name, value = f.value });
                if (lp.vectors != null)
                    foreach (var v in lp.vectors)
                        if (shadowKeys.Contains(v.name))
                            entry.props.Add(new ShadowSyncPresetProp { key = v.name, r = v.value.x, g = v.value.y, b = v.value.z, a = v.value.w });
                if (entry.props.Count > 0) _lilToonPresets.Add(entry);
            }
        }

        private static string LilPresetName(lilToonPreset lp)
        {
            if (lp.bases == null || lp.bases.Length == 0) return lp.name;
            string en = null, first = null;
            foreach (var b in lp.bases)
            {
                if (string.IsNullOrEmpty(b.name)) continue;
                if (first == null) first = b.name;
                if (b.language == "Japanese") return b.name;
                if (b.language == "English")  en = b.name;
            }
            return en ?? first ?? lp.name;
        }

        private static string LilCategoryLabel(string cat)
        {
            switch (cat)
            {
                case "Skin":      return "肌";
                case "Hair":      return "髪";
                case "Cloth":     return "服";
                case "Nature":    return "自然";
                case "Inorganic": return "無機物";
                case "Effect":    return "エフェクト";
                default:          return "その他";
            }
        }

        private const string SHADOW_PRESET_ASSET_FOLDER = "Assets/qsyi/liltoonpreset";

        // "Assets/qsyi/liltoonpreset" のようなパスを、無ければ親から順にAssetDatabase経由で作成する。
        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string parent = folderPath.Substring(0, folderPath.LastIndexOf('/'));
            string leaf    = folderPath.Substring(folderPath.LastIndexOf('/') + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // プリセット名をアセットのファイル名として使える文字列に変換する。
        private static string SanitizeAssetFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Preset" : name;
        }

        // 現在のコピー元・チェック項目から作ったプリセットを、lilToonPresetと同じ方式で
        // Assets/qsyi/liltoonpreset 配下に1プリセット=1アセットとして保存する。
        private void SaveBuiltinPreset(ShadowSyncPresetEntry entry)
        {
            EnsureAssetFolder(SHADOW_PRESET_ASSET_FOLDER);

            var so = ScriptableObject.CreateInstance<ShadowSyncPresetAsset>();
            so.category = entry.category;
            so.props    = entry.props;

            // UnityがまだShadowSyncPresetAssetのスクリプトを解決できていないと、
            // 保存したアセットのスクリプト参照が空（fileID: 0）のまま焼き付いてしまう。
            // その場合はエラーを出して保存を中止する（壊れたアセットを作らないようにする）。
            if (MonoScript.FromScriptableObject(so) == null)
            {
                UnityEngine.Object.DestroyImmediate(so);
                EditorUtility.DisplayDialog(
                    "プリセット保存に失敗しました",
                    "ShadowSyncPresetAssetのスクリプトをUnityがまだ認識できていません。\n" +
                    "少し待ってからもう一度お試しください。（直前にスクリプトを変更した場合は、コンパイル完了後に再試行してください）",
                    "OK");
                return;
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{SHADOW_PRESET_ASSET_FOLDER}/{SanitizeAssetFileName(entry.name)}.asset");
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();

            // 保存直後にプリセット棚全体（自作アセット＋lilToon）を再スキャンして最新状態を反映する
            LoadBuiltinPresets();
            LoadLilToonPresets();
            RebuildShadowPresetList();
        }

        // プリセットカードの右クリックメニューから呼ばれる。対応する.assetファイルを削除する。
        private void DeleteBuiltinPreset(ShadowSyncPresetEntry entry)
        {
            if (!EditorUtility.DisplayDialog("プリセット削除", $"プリセット「{entry.name}」を削除しますか？", "削除", "キャンセル"))
                return;
            if (string.IsNullOrEmpty(entry.assetPath)) return;

            AssetDatabase.DeleteAsset(entry.assetPath);

            if (_activeShadowPreset != null && _activeShadowPreset.name == entry.name)
            {
                _activeShadowPreset = null;
                RefreshShadowSyncSyncButton();
                RefreshShadowSyncSwatches();
                RefreshShadowSyncSourceCard();
            }

            LoadBuiltinPresets();
            RebuildShadowPresetList();
        }

        // プリセットカードの右クリックメニューから呼ばれる。.assetファイル名（＝表示名）を変更する。
        private void RenameBuiltinPreset(ShadowSyncPresetEntry entry, string newName)
        {
            string sanitized = SanitizeAssetFileName(newName);
            if (string.IsNullOrEmpty(sanitized) || sanitized == entry.name) return;
            if (string.IsNullOrEmpty(entry.assetPath)) return;

            string error = AssetDatabase.RenameAsset(entry.assetPath, sanitized);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog("名前の変更に失敗しました", error, "OK");
                return;
            }

            bool wasActive = _activeShadowPreset == entry;
            LoadBuiltinPresets();
            if (wasActive)
                _activeShadowPreset = _builtinPresets.FirstOrDefault(x => x.assetPath == entry.assetPath);

            RebuildShadowPresetList();
            RefreshShadowSyncSyncButton();
        }

        // 同期可能な全項目から、プリセット保存ウィンドウ用のプレビューデータを作る。
        // チェック済みの項目は included=true で初期表示し、それ以外はグレーアウト表示にする（ウィンドウ内で切り替え可能）。
        private List<ShadowPresetSaveEntryData> BuildShadowPresetPreviewItems()
        {
            var items = new List<ShadowPresetSaveEntryData>();
            foreach (var sp in SHADOW_PARAMS)
            {
                if (!IsShadowPropAvailable(sp.Prop)) continue;
                bool included = _shadowSyncParams.Contains(sp.Prop);

                if (sp.Type == ShadowParamType.Color)
                {
                    if (!TryGetRawShadowColor(sp.Prop, out var raw)) continue;
                    var adjusted = AdjustColor(raw, _shadowHueShift, _shadowSaturationScale);
                    items.Add(new ShadowPresetSaveEntryData { prop = sp.Prop, label = sp.Label, isColor = true, color = adjusted, group = sp.Group, included = included });
                }
                else
                {
                    if (!TryGetRawShadowValue(sp.Prop, sp.Type, out var raw)) continue;
                    string text = sp.Prop == "_RimBlendMode"
                        ? (RimBlendModeLabel(Mathf.RoundToInt(raw.x)) ?? raw.x.ToString("0"))
                        : sp.Type == ShadowParamType.Vector
                            ? $"({raw.x:0.###}, {raw.y:0.###}, {raw.z:0.###}, {raw.w:0.###})"
                            : sp.Type == ShadowParamType.Int
                                ? Mathf.RoundToInt(raw.x).ToString()
                                : raw.x.ToString("0.###");
                    items.Add(new ShadowPresetSaveEntryData { prop = sp.Prop, label = sp.Label, isColor = false, valueText = text, group = sp.Group, included = included });
                }
            }
            return items;
        }

        // コピー元マテリアル／選択中プリセットから、補正前の生の数値(Float/Int/Vector)を取得する
        private bool TryGetRawShadowValue(string prop, ShadowParamType type, out Vector4 raw)
        {
            if (_shadowSyncSource != null && _shadowSyncSource.HasProperty(prop))
            {
                switch (type)
                {
                    case ShadowParamType.Vector: raw = _shadowSyncSource.GetVector(prop); break;
                    case ShadowParamType.Int:    raw = new Vector4(_shadowSyncSource.GetInt(prop), 0, 0, 0); break;
                    default:                     raw = new Vector4(_shadowSyncSource.GetFloat(prop), 0, 0, 0); break;
                }
                return true;
            }
            if (_activeShadowPreset != null)
            {
                var p = _activeShadowPreset.props.FirstOrDefault(x => x.key == prop);
                if (p != null)
                {
                    raw = type == ShadowParamType.Vector ? new Vector4(p.r, p.g, p.b, p.a) : new Vector4(p.value, 0, 0, 0);
                    return true;
                }
            }
            raw = default;
            return false;
        }

        // 「プリセット保存」ボタンから呼ばれる。保存内容のプレビューを表示する別ウィンドウを開く。
        private void OpenShadowPresetSaveWindow()
        {
            if (!HasActiveSource) return;
            var items = BuildShadowPresetPreviewItems();
            string defaultName = _shadowSyncSource != null ? _shadowSyncSource.name
                                : _activeShadowPreset != null ? _activeShadowPreset.name
                                : "";
            ShadowPresetSaveWindow.Open(defaultName, items, DoSaveShadowPreset);
        }

        // プリセット保存ウィンドウの保存ボタンから呼ばれる。ウィンドウ内でチェックが付いている項目のみを保存する。
        private void DoSaveShadowPreset(string presetName, string category, HashSet<string> includedProps)
        {
            var entry = new ShadowSyncPresetEntry { name = presetName, category = category };
            foreach (var sp in SHADOW_PARAMS)
            {
                if (!includedProps.Contains(sp.Prop)) continue;
                if (!IsShadowPropAvailable(sp.Prop)) continue;

                if (sp.Type == ShadowParamType.Color)
                {
                    if (!TryGetRawShadowColor(sp.Prop, out var raw)) continue;
                    var adjusted = AdjustColor(raw, _shadowHueShift, _shadowSaturationScale);
                    entry.props.Add(new ShadowSyncPresetProp { key = sp.Prop, r = adjusted.r, g = adjusted.g, b = adjusted.b, a = adjusted.a });
                }
                else
                {
                    if (!TryGetRawShadowValue(sp.Prop, sp.Type, out var raw)) continue;
                    if (sp.Type == ShadowParamType.Vector)
                        entry.props.Add(new ShadowSyncPresetProp { key = sp.Prop, r = raw.x, g = raw.y, b = raw.z, a = raw.w });
                    else
                        entry.props.Add(new ShadowSyncPresetProp { key = sp.Prop, value = raw.x });
                }
            }
            SaveBuiltinPreset(entry);
        }

        private void RebuildShadowPresetList()
        {
            if (_shadowPresetListEl == null) return;
            _shadowPresetListEl.Clear();

            if (_builtinPresets.Count == 0 && _lilToonPresets.Count == 0)
            {
                var none = new Label("(なし)");
                none.style.fontSize = 10;
                none.style.color    = DimColor;
                _shadowPresetListEl.Add(none);
                return;
            }

            List<Color> PresetSwatchColors(ShadowSyncPresetEntry preset)
            {
                var colors = new List<Color>();
                foreach (var sp in SHADOW_PARAMS)
                {
                    if (sp.Type != ShadowParamType.Color) continue;
                    var pp = preset.props.FirstOrDefault(x => x.key == sp.Prop);
                    if (pp == null) continue;
                    colors.Add(new Color(pp.r, pp.g, pp.b, pp.a));
                    if (colors.Count >= 3) break;
                }
                return colors;
            }

            VisualElement MakePresetCard(ShadowSyncPresetEntry preset)
            {
                var p = preset;
                bool isActive = _activeShadowPreset == p;

                var card = new VisualElement();
                card.style.width        = 90;
                card.style.marginRight  = 6;
                card.style.marginBottom = 6;
                card.style.paddingLeft  = card.style.paddingRight = 5;
                card.style.paddingTop   = card.style.paddingBottom = 5;
                card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                    card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
                float bw = isActive ? 2f : 1f;
                card.style.borderTopWidth = card.style.borderBottomWidth =
                    card.style.borderLeftWidth = card.style.borderRightWidth = bw;
                Color bc = isActive ? AccentColor : PaneBorderColor;
                card.style.borderTopColor = card.style.borderBottomColor =
                    card.style.borderLeftColor = card.style.borderRightColor = bc;
                if (isActive)
                    card.style.backgroundColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f);

                var strip = new VisualElement();
                strip.style.flexDirection = FlexDirection.Row;
                strip.style.marginBottom  = 4;
                var cols = PresetSwatchColors(p);
                bool noCols = cols.Count == 0;
                if (noCols) cols.Add(new Color(0.4f, 0.4f, 0.4f, 1f));
                for (int i = 0; i < cols.Count; i++)
                {
                    var sw = MakeShadowSwatchVisual(26, 4, out var update, showAlphaLabel: false);
                    if (i < cols.Count - 1) sw.style.marginRight = 2;
                    update(cols[i], !noCols && cols[i].a <= 0.0001f);
                    strip.Add(sw);
                }
                card.Add(strip);

                bool isLilToonPreset = !_builtinPresets.Contains(p);
                bool deletable = !isLilToonPreset && !p.isBundled;

                var nameLbl = new Label(p.name);
                nameLbl.style.fontSize                = 11;
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.color                   = TextColor;
                nameLbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
                nameLbl.style.whiteSpace              = WhiteSpace.NoWrap;
                nameLbl.style.overflow                = Overflow.Hidden;
                card.Add(nameLbl);

                if (isLilToonPreset)
                    card.tooltip = "lilToonのプリセットのため削除できません";
                else if (p.isBundled)
                    card.tooltip = "qsToolBox付属のプリセットのため削除できません";

                card.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    evt.StopPropagation();
                    if (_activeShadowPreset == p)
                    {
                        _activeShadowPreset = null;
                    }
                    else
                    {
                        _activeShadowPreset = p;
                        _shadowSyncSource   = null;
                        _shadowSyncSourceRenderer = null;
                        if (_shadowSyncCreatePresetBtn != null)
                            _shadowSyncCreatePresetBtn.SetEnabled(false);
                        RestorePresetCheckboxes(p);
                    }
                    RebuildShadowPresetList();
                    RefreshShadowSyncSyncButton();
                    RefreshShadowSyncSwatches();
                    RefreshShadowSyncSourceCard();
                });

                card.RegisterCallback<ContextClickEvent>(evt =>
                {
                    var menu = new GenericMenu();
                    if (deletable)
                    {
                        menu.AddItem(new GUIContent("名前を変更"), false, () =>
                            ShadowPresetRenameWindow.Open(p.name, newName => RenameBuiltinPreset(p, newName)));
                        menu.AddItem(new GUIContent("削除"), false, () => DeleteBuiltinPreset(p));
                    }
                    else if (isLilToonPreset)
                        menu.AddDisabledItem(new GUIContent("削除（lilToonのプリセットのため不可）"));
                    else
                        menu.AddDisabledItem(new GUIContent("削除（qsToolBox付属のプリセットのため不可）"));
                    menu.ShowAsContext();
                    evt.StopPropagation();
                });
                return card;
            }

            // カテゴリのまとまり（同梱JSON + lilToon）
            var groups = new List<(string cat, List<ShadowSyncPresetEntry> items)>();
            foreach (var g in _builtinPresets.GroupBy(p => string.IsNullOrEmpty(p.category) ? "その他" : p.category))
                groups.Add((g.Key, g.ToList()));
            foreach (var g in _lilToonPresets.GroupBy(p => string.IsNullOrEmpty(p.category) ? "lilToon" : p.category))
                groups.Add((g.Key, g.ToList()));

            foreach (var (cat, items) in groups)
            {
                var shown = items;
                if (shown.Count == 0) continue;

                bool open = !_shadowPresetCategoryOpen.TryGetValue(cat, out var o) || o;

                var catHdr = new VisualElement();
                catHdr.style.flexDirection = FlexDirection.Row;
                catHdr.style.alignItems    = Align.Center;
                catHdr.style.paddingTop    = 2;
                catHdr.style.paddingBottom = 2;

                var chevron = new Label(open ? "▼" : "▶");
                chevron.style.fontSize    = 9;
                chevron.style.color       = DimColor;
                chevron.style.marginRight = 4;
                catHdr.Add(chevron);

                var catLbl = new Label($"{cat} ({shown.Count})");
                catLbl.style.fontSize                = 11;
                catLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                catLbl.style.color                   = TextColor;
                catHdr.Add(catLbl);

                string catKey  = cat;
                bool   curOpen = open;
                catHdr.RegisterCallback<MouseDownEvent>(evt =>
                {
                    _shadowPresetCategoryOpen[catKey] = !curOpen;
                    RebuildShadowPresetList();
                });
                _shadowPresetListEl.Add(catHdr);

                if (open)
                {
                    var wrap = new VisualElement();
                    wrap.style.flexDirection = FlexDirection.Row;
                    wrap.style.flexWrap      = Wrap.Wrap;
                    wrap.style.marginBottom  = 2;
                    foreach (var preset in shown)
                        wrap.Add(MakePresetCard(preset));
                    _shadowPresetListEl.Add(wrap);
                }
            }
        }

        private void RestorePresetCheckboxes(ShadowSyncPresetEntry preset)
        {
            _shadowSyncParams.Clear();
            _shadowSyncParams.UnionWith(preset.props.Select(p => p.key));
            SyncShadowTogglesFromParams();
        }

        private bool HasActiveSource => _shadowSyncSource != null || _activeShadowPreset != null;

        private void RefreshShadowSyncSyncButton()
        {
            if (_shadowSyncSyncBtn == null) return;
            if (_activeShadowPreset != null)
                _shadowSyncSyncBtn.text = $"{_activeShadowPreset.name} に同期";
            else if (_shadowSyncSource != null)
                _shadowSyncSyncBtn.text = $"{_shadowSyncSource.name} に同期";
            else
                _shadowSyncSyncBtn.text = "同期";
            _shadowSyncSyncBtn.SetEnabled(HasActiveSource && _shadowSyncDestSet.Count > 0);
        }

        private void CopyShadowSyncToMaterial(Material dest)
        {
            if (_shadowSyncSource == null || dest == null) return;
            foreach (var sp in SHADOW_PARAMS)
            {
                if (!_shadowSyncParams.Contains(sp.Prop)) continue;
                if (!_shadowSyncSource.HasProperty(sp.Prop) || !dest.HasProperty(sp.Prop)) continue;
                switch (sp.Type)
                {
                    case ShadowParamType.Color:  dest.SetColor(sp.Prop,  AdjustColor(_shadowSyncSource.GetColor(sp.Prop), _shadowHueShift, _shadowSaturationScale));  break;
                    case ShadowParamType.Int:    dest.SetInt(sp.Prop,    _shadowSyncSource.GetInt(sp.Prop));    break;
                    case ShadowParamType.Vector: dest.SetVector(sp.Prop, _shadowSyncSource.GetVector(sp.Prop)); break;
                    default:                     dest.SetFloat(sp.Prop,  _shadowSyncSource.GetFloat(sp.Prop));  break;
                }
            }
        }

        private void ExecuteShadowSync()
        {
            if (!HasActiveSource || _shadowSyncDestSet.Count == 0) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Shadow Sync");

            foreach (var dest in _shadowSyncDestSet.Where(m => m != null))
            {
                Undo.RecordObject(dest, "Shadow Sync");
                if (_activeShadowPreset != null)
                    ApplyPresetToMaterial(_activeShadowPreset, dest);
                else
                    CopyShadowSyncToMaterial(dest);
                EditorUtility.SetDirty(dest);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private void ApplyPresetToMaterial(ShadowSyncPresetEntry preset, Material dest)
        {
            var propMap = preset.props.ToDictionary(p => p.key);
            foreach (var sp in SHADOW_PARAMS)
            {
                if (!_shadowSyncParams.Contains(sp.Prop)) continue;
                if (!dest.HasProperty(sp.Prop)) continue;
                if (!propMap.TryGetValue(sp.Prop, out var p)) continue;
                switch (sp.Type)
                {
                    case ShadowParamType.Color:  dest.SetColor(sp.Prop,  AdjustColor(new Color(p.r, p.g, p.b, p.a), _shadowHueShift, _shadowSaturationScale));  break;
                    case ShadowParamType.Int:    dest.SetInt(sp.Prop,    (int)p.value);                    break;
                    case ShadowParamType.Vector: dest.SetVector(sp.Prop, new Vector4(p.r, p.g, p.b, p.a)); break;
                    default:                     dest.SetFloat(sp.Prop,  p.value);                         break;
                }
            }
        }
    }
}
#endif
