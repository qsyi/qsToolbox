#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace qsyi
{
    internal partial class QsToolBox
    {
        private enum PoiyomiShadowParamGroup { LightData, RimLight, RimShade, Backlight }

        // Light Data セクションの一般プロパティ。_LightingMode に関係なく常に同期対象（テクスチャ・デバッグ表示を除く）。
        private static readonly ShadowParam[] POIYOMI_LIGHTDATA_PARAMS = {
            new ShadowParam("_LightingColorMode",   "ライトカラーモード",       ShadowParamType.Int,   ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingMapMode",     "ライトマップモード",       ShadowParamType.Int,   ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingDirectionMode","ライト方向モード",        ShadowParamType.Int,   ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightngForcedDirection","強制方向",               ShadowParamType.Vector,ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingViewDirOffsetPitch","視点方向オフセットPitch", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingViewDirOffsetYaw",  "視点方向オフセットYaw",   ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingForceColorEnabled","ライトカラー強制ON/OFF",  ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingForcedColor", "強制ライトカラー",          ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingForcedColorThemeIndex", "強制ライトカラーのテーマ", ShadowParamType.Int, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_Unlit_Intensity",     "Unlit強度",                ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingCapEnabled",  "明るさ上限ON/OFF",         ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingCap",         "最大輝度",                 ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingMinLightBrightness","最小輝度",           ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingIndirectUsesNormals","間接光にノーマル反映", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingCastedShadows","キャストシャドウを受け取る", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingShadowSoftness","キャストシャドウの柔らかさ", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingMonochromatic","グレースケールライティング", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingVertexLightingEnabled","頂点ライト(非重要)", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingMirrorVertexLightingEnabled","頂点ライトの鏡映反転", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            // 加算パス（Point/Spot/その他Directionalライト）
            new ShadowParam("_LightingAdditiveEnable","ピクセルライト(重要)ON/OFF", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_DisableDirectionalInAdd","加算パスでDirectional無視", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingAdditiveLimited","加算パス明るさ上限ON/OFF", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingAdditiveLimit", "加算パス最大輝度",        ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingAdditiveCastedShadows","加算パスキャストシャドウ", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingAdditiveMonochromatic","加算パスグレースケール", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingAdditivePassthrough","ライト透過",         ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingAdditiveAttenCurve","ライト減衰カーブ",    ShadowParamType.Vector, ShadowParamGroup.ShadowSettings),
            // ライトボリューム
            new ShadowParam("_LightingEnableLightVolumes","ライトボリュームON/OFF", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingLightVolumesSpecular","LVスペキュラ",      ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingLightVolumesIntensity","LV強度",          ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingLightVolumesPointShadows","LV影",         ShadowParamType.Int,   ShadowParamGroup.ShadowSettings),
            new ShadowParam("_LightingLightVolumesNormalBias","LVノーマルバイアス", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
        };
        // 除外: AO/ディテールシャドウ/シャドウマスクの各テクスチャと強度スライダー・グローバルマスク（テクスチャ前提）、
        // SDFマップ関連（テクスチャ前提）、デバッグ可視化2項目（設定ではないため）

        // _LightingMode の値ごとの「Shadows」プロパティ表。未登録の値はMultilayerMath表にフォールバック。
        private static readonly Dictionary<int, ShadowParam[]> POIYOMI_SHADOWS_PARAMS_BY_MODE = new Dictionary<int, ShadowParam[]>
        {
            // _LightingMode == 1: Multilayer Math（既定・メイン対応）
            [1] = new[] {
                new ShadowParam("_ShadowColor",        "影色",           ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow2ndColor",     "2影色",          ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow3rdColor",     "3影色",          ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowBorderColor",  "影ボーダー色",    ShadowParamType.Color, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowBorder",       "影境界",          ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowBlur",         "影ぼかし",        ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowReceive",      "影を受け取る 1",  ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowNormalStrength","ノーマルマップ強度 1", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow2ndBorder",    "2影境界",         ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow2ndBlur",      "2影ぼかし",       ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow2ndReceive",   "影を受け取る 2",  ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow3rdBorder",    "3影境界",         ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow3rdBlur",      "3影ぼかし",       ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_Shadow3rdReceive",   "影を受け取る 3",  ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowBorderRange",  "境界の幅",        ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowMainStrength", "ベース色ブレンド", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowEnvStrength",  "環境光影響度",     ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_LightingMulitlayerNonLinear","非線形ライトマップ", ShadowParamType.Int, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_ShadowStrength",     "影強度",          ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
                new ShadowParam("_LightingIgnoreAmbientColor","間接光の影色を無視", ShadowParamType.Float, ShadowParamGroup.ShadowSettings),
            },
            // 他の8方式（Wrapped/Skin/ShadeMap/Flat/Realistic/Cloth/SDF/TextureRamp）は対応しない。
            // マットキャップ等テクスチャベースの方式が多く、テクスチャは一括コピー対象外の方針と相性が悪いため。
            // 必要になれば[モード番号]をキーに追加すれば拡張できる。
        };
        private const int POIYOMI_DEFAULT_LIGHTING_MODE = 1; // Multilayer Math（フォールバック先）

        // 影カテゴリの実同期時は POIYOMI_LIGHTDATA_PARAMS（常時）＋ POIYOMI_SHADOWS_PARAMS_BY_MODE[現在の_LightingMode]（無ければ[1]）を合算して使う。

        private static readonly ShadowParam[] POIYOMI_RIM0_PARAMS = { // → リムライト
            new ShadowParam("_RimLightColor",   "リムカラー",       ShadowParamType.Color, ShadowParamGroup.Rim),
            new ShadowParam("_RimWidth",        "幅",               ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimBlur",         "ぼかし",           ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimPower",        "リムパワー",        ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimBrightness",   "明るさ",           ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimStrength",     "エミッション",      ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimBlendStrength","ブレンド割合",      ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimBaseColorMix", "ベース色ミックス",   ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimPoiBlendMode", "ブレンドモード",     ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimLinear",       "リニア",            ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimLightingInvert","反転",             ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_Is_NormalMapToRimLight", "ノーマルマップ強度", ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowToggle", "光方向マスクON/OFF", ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowMaskRampType", "光の落ち方",   ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowMaskInvert", "影マスク反転",  ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowMaskStrength", "影マスク強度", ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowAlpha",  "影での隠れ具合",    ShadowParamType.Vector, ShadowParamGroup.Rim),
            new ShadowParam("_RimShadowWidth",  "影で縮小",          ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimHueShiftEnabled", "色相シフトON/OFF", ShadowParamType.Int,  ShadowParamGroup.Rim),
            new ShadowParam("_RimHueShiftColorSpace", "色空間",       ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimHueSelectOrShift", "選択/シフト",    ShadowParamType.Int,   ShadowParamGroup.Rim),
            new ShadowParam("_RimHueShiftSpeed", "シフト速度",        ShadowParamType.Float, ShadowParamGroup.Rim),
            new ShadowParam("_RimHueShift",      "色相シフト量",      ShadowParamType.Float, ShadowParamGroup.Rim),
        };

        private static readonly ShadowParam[] POIYOMI_RIM1_PARAMS = { // → リムシェード（"Rim2"系プロパティ）
            new ShadowParam("_Rim2LightColor",   "リムカラー",       ShadowParamType.Color, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2Width",        "幅",               ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2Blur",         "ぼかし",           ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2Power",        "リムパワー",        ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2Brightness",   "明るさ",           ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2Strength",     "エミッション",      ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2BlendStrength","ブレンド割合",      ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2BaseColorMix", "ベース色ミックス",   ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_RimPoi2BlendMode", "ブレンドモード",     ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2Linear",       "リニア",            ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2LightingInvert","反転",             ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Is_NormalMapToRim2Light", "ノーマルマップ強度", ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2ShadowToggle", "光方向マスクON/OFF", ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2ShadowMaskRampType", "光の落ち方",   ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2ShadowMaskInvert", "影マスク反転",  ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2ShadowMaskStrength", "影マスク強度", ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2ShadowAlpha", "影での隠れ具合",     ShadowParamType.Vector, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2ShadowWidth",  "影で縮小",          ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2HueShiftEnabled", "色相シフトON/OFF", ShadowParamType.Int, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2HueShiftColorSpace", "色空間",       ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2HueSelectOrShift", "選択/シフト",    ShadowParamType.Int,   ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2HueShiftSpeed", "シフト速度",        ShadowParamType.Float, ShadowParamGroup.RimShade),
            new ShadowParam("_Rim2HueShift",      "色相シフト量",      ShadowParamType.Float, ShadowParamGroup.RimShade),
        };

        private static readonly ShadowParam[] POIYOMI_BACKLIGHT_PARAMS = {
            new ShadowParam("_BacklightColor",          "逆光色",             ShadowParamType.Color, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightMainStrength",   "メインカラー影響度",  ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightNormalStrength", "ノーマルマップ強度",  ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightBorder",         "境界",               ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightBlur",           "ぼかし",             ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightDirectivity",    "指向性",             ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightViewStrength",   "正面時の強さ",       ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightReceiveShadow",  "影を受ける",         ShadowParamType.Float, ShadowParamGroup.Backlight),
            new ShadowParam("_BacklightBackfaceMask",   "裏面で無効化",       ShadowParamType.Float, ShadowParamGroup.Backlight),
        };

        // 各カテゴリのON/OFFマスタースイッチ（liltoon版の_UseShadow等に相当）
        // 影: _ShadingEnabled / リムライト: _EnableRimLighting / リムシェード: _EnableRim2Lighting / 逆光ライト: _BacklightEnabled

        // ─── poiyomiサブモードの状態（liltoon版の _shadowSync* とは完全に分離） ─
        private Material _poiShadowSyncSource;
        private Renderer _poiShadowSyncSourceRenderer;
        private int      _poiShadowSyncSourceSlot;
        private readonly HashSet<PoiyomiShadowParamGroup> _poiShadowSyncEnabledCategories = new HashSet<PoiyomiShadowParamGroup>();
        private readonly HashSet<Material> _poiShadowSyncDestSet = new HashSet<Material>();
        private readonly Dictionary<string, List<Action<Color, bool>>> _poiShadowSwatchUpdaters = new Dictionary<string, List<Action<Color, bool>>>();
        private readonly List<Toggle> _poiShadowCategoryToggles = new List<Toggle>();
        private Toggle _poiShadowSyncMasterToggle;
        // 影カテゴリのみ「影色」「影数値」に分けて個別ON/OFFできる（liltoon版と同じ構成）
        private bool _poiShadowSyncColorEnabled;
        private bool _poiShadowSyncNumEnabled;
        private Toggle _poiShadowShadowToggle;
        private Toggle _poiShadowColorToggle;
        private Toggle _poiShadowNumToggle;
        private ObjectField _poiShadowSyncSourceField;
        private Button _poiShadowSyncSlotButton;
        private VisualElement _poiShadowSyncListEl;
        private Label _poiShadowSyncCountLabel;
        private Button _poiShadowSyncAllSelBtn;
        private Button _poiShadowSyncSyncBtn;
        private float _poiShadowHueShift        = 0f;
        private float _poiShadowSaturationScale = 1f;

        // ─── プリセット棚（liltoon版と同じ ShadowSyncPresetAsset 方式。フォルダ・リストだけ別に持つ）───
        private const string POI_SHADOW_PRESET_ASSET_FOLDER = "Assets/qsyi/poiyomipreset";
        private readonly List<ShadowSyncPresetEntry> _poiPresets = new List<ShadowSyncPresetEntry>();
        private readonly Dictionary<string, bool> _poiShadowPresetCategoryOpen = new Dictionary<string, bool>();
        private ShadowSyncPresetEntry _poiActiveShadowPreset;
        private VisualElement _poiShadowPresetListEl;
        private Button _poiShadowSyncCreatePresetBtn;

        // 影テーブル(LightData+Shadows[Multilayer Math]) + リムライト + リムシェード + 逆光ライトの全項目を横断するヘルパー。
        // プリセット保存プレビューとプリセット保存本体で共通利用する。
        private static IEnumerable<ShadowParam> AllPoiyomiShadowParams() =>
            POIYOMI_LIGHTDATA_PARAMS
                .Concat(POIYOMI_SHADOWS_PARAMS_BY_MODE[POIYOMI_DEFAULT_LIGHTING_MODE])
                .Concat(POIYOMI_RIM0_PARAMS)
                .Concat(POIYOMI_RIM1_PARAMS)
                .Concat(POIYOMI_BACKLIGHT_PARAMS);

        // カテゴリ見出しのスウォッチに使う色プロパティ（liltoon版の各カテゴリ見出しと同じ考え方。影は4色まとめて表示）
        private static readonly Dictionary<PoiyomiShadowParamGroup, string[]> POIYOMI_SWATCH_PROPS = new Dictionary<PoiyomiShadowParamGroup, string[]>
        {
            [PoiyomiShadowParamGroup.LightData] = new[] { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor", "_ShadowBorderColor" },
            [PoiyomiShadowParamGroup.RimLight]  = new[] { "_RimLightColor" },
            [PoiyomiShadowParamGroup.RimShade]  = new[] { "_Rim2LightColor" },
            [PoiyomiShadowParamGroup.Backlight] = new[] { "_BacklightColor" },
        };

        private VisualElement BuildPoiyomiShadowSyncSubPane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow      = 1;
            pane.style.minHeight     = 0;

            // ── コピー元マテリアル カード（liltoon版のsrcCardと同じ見た目） ──
            var srcCard = new VisualElement();
            srcCard.style.flexDirection     = FlexDirection.Row;
            srcCard.style.alignItems        = Align.Center;
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

            var srcLbl = new Label("コピー元");
            srcLbl.style.fontSize                = 12;
            srcLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            srcLbl.style.color                   = TextColor;
            srcLbl.style.marginRight             = 6;
            srcLbl.style.flexShrink              = 0;
            srcCard.Add(srcLbl);

            _poiShadowSyncSourceField = new ObjectField();
            _poiShadowSyncSourceField.objectType        = typeof(Material);
            _poiShadowSyncSourceField.allowSceneObjects = false;
            _poiShadowSyncSourceField.value             = _poiShadowSyncSource;
            FixFieldOverflow(_poiShadowSyncSourceField);
            _poiShadowSyncSourceField.RegisterValueChangedCallback(evt =>
            {
                _poiShadowSyncSourceRenderer = null;
                ApplyPoiyomiShadowSyncSourceMaterial(evt.newValue as Material);
            });
            srcCard.Add(_poiShadowSyncSourceField);

            _poiShadowSyncSlotButton = new Button(() =>
            {
                if (_poiShadowSyncSourceRenderer != null) ShowPoiyomiShadowSyncSlotMenu(_poiShadowSyncSourceRenderer);
            });
            _poiShadowSyncSlotButton.style.height       = 20;
            _poiShadowSyncSlotButton.style.fontSize     = 10;
            _poiShadowSyncSlotButton.style.marginLeft   = 4;
            _poiShadowSyncSlotButton.style.paddingLeft  = _poiShadowSyncSlotButton.style.paddingRight = 8;
            _poiShadowSyncSlotButton.style.flexShrink   = 0;
            _poiShadowSyncSlotButton.style.display      = DisplayStyle.None;
            srcCard.Add(_poiShadowSyncSlotButton);

            // レンダラー（またはレンダラーを含むGameObject）のドラッグ＆ドロップを受け付ける。マテリアル自体はsrcField自身が処理する。
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
                if (DragAndDrop.objectReferences.Any(o => o is Material)) return;
                var rend = DragAndDrop.objectReferences.Select(FindRendererFromDraggedObject).FirstOrDefault(r => r != null);
                if (rend == null) return;
                DragAndDrop.AcceptDrag();
                SetPoiyomiShadowSyncSourceFromRenderer(rend, 0);
                evt.StopPropagation();
            });

            pane.Add(srcCard);

            // ── プリセット棚（liltoon版と同じ見た目・構造） ──
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

            _poiShadowPresetListEl = new VisualElement();
            _poiShadowPresetListEl.style.flexDirection = FlexDirection.Column;
            presetScroll.Add(_poiShadowPresetListEl);
            presetRow.Add(presetScroll);

            pane.Add(presetRow);

            // ── Body: 左=カテゴリ選択 / 右=コピー先（liltoon版と同じ左右分割） ──
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow      = 1;
            body.style.minHeight     = 0;

            // ── 左ペイン: カテゴリのON/OFF（プリセット・色相彩度シフトは対象外。カテゴリ単位のオールオアナッシング） ──
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

            _poiShadowSyncMasterToggle = new Toggle();
            _poiShadowSyncMasterToggle.value = false;
            _poiShadowSyncMasterToggle.style.marginRight = 2;
            _poiShadowSyncMasterToggle.style.flexShrink  = 0;
            _poiShadowSyncMasterToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
            _poiShadowSyncMasterToggle.RegisterValueChangedCallback(evt =>
            {
                _poiShadowSyncEnabledCategories.Clear();
                if (evt.newValue)
                    foreach (var g in new[] { PoiyomiShadowParamGroup.RimLight, PoiyomiShadowParamGroup.RimShade, PoiyomiShadowParamGroup.Backlight })
                        _poiShadowSyncEnabledCategories.Add(g);
                _poiShadowSyncColorEnabled = evt.newValue;
                _poiShadowSyncNumEnabled   = evt.newValue;
                foreach (var t in _poiShadowCategoryToggles) t.SetValueWithoutNotify(evt.newValue);
                _poiShadowShadowToggle?.SetValueWithoutNotify(evt.newValue);
                _poiShadowColorToggle?.SetValueWithoutNotify(evt.newValue);
                _poiShadowNumToggle?.SetValueWithoutNotify(evt.newValue);
                UpdatePoiyomiShadowSyncButtons();
            });
            masterRow.Add(_poiShadowSyncMasterToggle);

            var masterLbl = new Label("全項目を同期");
            masterLbl.style.fontSize                = 11;
            masterLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            masterLbl.style.color                   = TextColor;
            masterLbl.style.marginLeft              = 3;
            masterRow.Add(masterLbl);

            leftScroll.Add(masterRow);
            _poiShadowCategoryToggles.Clear();

            void AddCategoryToggle(string label, PoiyomiShadowParamGroup group)
            {
                var row = new VisualElement();
                row.style.flexDirection     = FlexDirection.Row;
                row.style.alignItems        = Align.Center;
                row.style.paddingLeft       = 6;
                row.style.paddingRight      = 8;
                row.style.paddingTop        = 5;
                row.style.paddingBottom     = 5;
                row.style.borderTopWidth    = 1;
                row.style.borderTopColor    = PaneBorderColor;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = PaneBorderColor;

                var toggle = new Toggle();
                toggle.value             = _poiShadowSyncEnabledCategories.Contains(group);
                toggle.style.marginRight = 2;
                toggle.style.flexShrink  = 0;
                toggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _poiShadowSyncEnabledCategories.Add(group);
                    else              _poiShadowSyncEnabledCategories.Remove(group);
                    UpdatePoiyomiMasterToggle();
                    UpdatePoiyomiShadowSyncButtons();
                });
                row.Add(toggle);
                _poiShadowCategoryToggles.Add(toggle);

                var rowLbl = new Label(label);
                rowLbl.style.fontSize                = 11;
                rowLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                rowLbl.style.color                    = TextColor;
                rowLbl.style.marginLeft               = 3;
                rowLbl.style.flexShrink                = 1;
                rowLbl.style.minWidth                  = 0;
                rowLbl.style.overflow                  = Overflow.Hidden;
                rowLbl.style.textOverflow              = TextOverflow.Ellipsis;
                rowLbl.style.whiteSpace                = WhiteSpace.NoWrap;
                row.Add(rowLbl);

                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                row.Add(spacer);

                foreach (var prop in POIYOMI_SWATCH_PROPS[group])
                {
                    var swatch = MakeShadowSwatchVisual(26, 4, out var update);
                    swatch.style.marginLeft = 3;
                    RegisterPoiyomiColorBox(prop, update);
                    row.Add(swatch);
                }

                leftScroll.Add(row);
            }

            BuildPoiyomiShadowCategory(leftScroll);
            AddCategoryToggle("リムライト1", PoiyomiShadowParamGroup.RimLight);
            AddCategoryToggle("リムライト2", PoiyomiShadowParamGroup.RimShade);
            AddCategoryToggle("逆光ライト", PoiyomiShadowParamGroup.Backlight);
            UpdatePoiyomiMasterToggle();

            leftPane.Add(leftScroll);

            // ── 色相/彩度スライダー（左カラム下端に固定。liltoon版と同じMakeSliderRowを共用） ──
            var hsArea = new VisualElement();
            hsArea.style.flexShrink     = 0;
            hsArea.style.minWidth       = 0;
            hsArea.style.overflow       = Overflow.Hidden;
            hsArea.style.paddingLeft    = 8;
            hsArea.style.paddingRight   = 8;
            hsArea.style.paddingTop     = 6;
            hsArea.style.paddingBottom  = 6;
            hsArea.style.borderTopWidth = 1;
            hsArea.style.borderTopColor = PaneBorderColor;

            hsArea.Add(MakeSliderRow("色相", -180f, 180f, _poiShadowHueShift, 0f,
                v => $"{v:+0;-0;+0}°",
                v => { _poiShadowHueShift = v; RefreshPoiyomiShadowSyncSwatches(); }));

            hsArea.Add(MakeSliderRow("彩度", 0f, 2f, _poiShadowSaturationScale, 1f,
                v => $"×{v:0.0}",
                v => { _poiShadowSaturationScale = v; RefreshPoiyomiShadowSyncSwatches(); }));

            leftPane.Add(hsArea);
            body.Add(leftPane);

            // ── 右ペイン: コピー先マテリアル一覧（liltoon版・poiyomi変換サブモードと同じ見た目） ──
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

            _poiShadowSyncCountLabel = new Label("コピー先 (0/0)");
            _poiShadowSyncCountLabel.style.fontSize                = 12;
            _poiShadowSyncCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _poiShadowSyncCountLabel.style.color                   = TextColor;
            _poiShadowSyncCountLabel.style.flexGrow = 1;
            rightHd.Add(_poiShadowSyncCountLabel);

            _poiShadowSyncAllSelBtn = new Button(() =>
            {
                var avail = _materials.Where(m => m != null && m != _poiShadowSyncSource).ToList();
                bool allSel = avail.Count > 0 && avail.All(m => _poiShadowSyncDestSet.Contains(m));
                if (allSel) _poiShadowSyncDestSet.Clear();
                else        foreach (var m in avail) _poiShadowSyncDestSet.Add(m);
                RebuildPoiyomiShadowSyncDestList();
            });
            _poiShadowSyncAllSelBtn.text             = "全選択";
            _poiShadowSyncAllSelBtn.style.marginLeft = 4;
            _poiShadowSyncAllSelBtn.style.fontSize   = 10;
            _poiShadowSyncAllSelBtn.style.height     = 20;
            rightHd.Add(_poiShadowSyncAllSelBtn);

            rightPane.Add(rightHd);

            var destScroll = new ScrollView();
            destScroll.style.flexGrow  = 1;
            destScroll.style.minHeight = 0;
            _poiShadowSyncListEl       = new VisualElement();
            destScroll.Add(_poiShadowSyncListEl);
            rightPane.Add(destScroll);

            body.Add(rightPane);
            pane.Add(body);

            // ── Footer ──
            var footer = new VisualElement();
            footer.style.flexDirection  = FlexDirection.Row;
            footer.style.alignItems     = Align.Center;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.flexShrink     = 0;
            footer.style.paddingLeft    = 10;
            footer.style.paddingRight   = 10;
            footer.style.paddingTop     = 8;
            footer.style.paddingBottom  = 8;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = PaneBorderColor;

            _poiShadowSyncCreatePresetBtn = new Button(OpenPoiyomiShadowPresetSaveWindow);
            _poiShadowSyncCreatePresetBtn.text = "プリセット保存";
            _poiShadowSyncCreatePresetBtn.style.height      = 28;
            _poiShadowSyncCreatePresetBtn.style.minHeight   = 28;
            _poiShadowSyncCreatePresetBtn.style.maxHeight   = 28;
            _poiShadowSyncCreatePresetBtn.style.flexShrink  = 0;
            _poiShadowSyncCreatePresetBtn.style.paddingLeft  = _poiShadowSyncCreatePresetBtn.style.paddingRight = 12;
            _poiShadowSyncCreatePresetBtn.style.marginRight  = new StyleLength(StyleKeyword.Auto);
            _poiShadowSyncCreatePresetBtn.style.fontSize     = 12;
            _poiShadowSyncCreatePresetBtn.style.whiteSpace   = WhiteSpace.NoWrap;
            _poiShadowSyncCreatePresetBtn.SetEnabled(false);
            footer.Add(_poiShadowSyncCreatePresetBtn);

            _poiShadowSyncSyncBtn              = new Button(ExecutePoiyomiShadowSync);
            _poiShadowSyncSyncBtn.text         = "同期";
            _poiShadowSyncSyncBtn.style.height = 28;
            _poiShadowSyncSyncBtn.style.minHeight    = 28;
            _poiShadowSyncSyncBtn.style.maxHeight    = 28;
            _poiShadowSyncSyncBtn.style.paddingLeft  = 18;
            _poiShadowSyncSyncBtn.style.paddingRight = 18;
            _poiShadowSyncSyncBtn.style.fontSize     = 12;
            footer.Add(_poiShadowSyncSyncBtn);

            pane.Add(footer);

            LoadPoiyomiPresets();
            RebuildPoiyomiShadowPresetList();
            RebuildPoiyomiShadowSyncDestList();
            RefreshPoiyomiShadowSyncSwatches();
            RefreshPoiyomiShadowSyncSourceCard();
            return pane;
        }

        // コピー元マテリアルを実際に切り替える
        private void ApplyPoiyomiShadowSyncSourceMaterial(Material m)
        {
            _poiShadowSyncSource = m;
            if (_poiShadowSyncSource != null && _poiActiveShadowPreset != null)
            {
                _poiActiveShadowPreset = null;
                RebuildPoiyomiShadowPresetList();
            }
            RebuildPoiyomiShadowSyncDestList();
            RefreshPoiyomiShadowSyncSwatches();
            RefreshPoiyomiShadowSyncSourceCard();
            UpdatePoiyomiShadowSyncButtons();
            if (_poiShadowSyncCreatePresetBtn != null)
                _poiShadowSyncCreatePresetBtn.SetEnabled(_poiShadowSyncSource != null);
        }

        // レンダラーをコピー元にする。マテリアルが複数ある場合はスロットを指定できる（デフォルト0＝一番上）
        private void SetPoiyomiShadowSyncSourceFromRenderer(Renderer r, int slot)
        {
            var mats = r.sharedMaterials;
            if (mats.Length == 0) return;
            if (slot < 0 || slot >= mats.Length) slot = 0;
            _poiShadowSyncSourceRenderer = r;
            _poiShadowSyncSourceSlot     = slot;
            ApplyPoiyomiShadowSyncSourceMaterial(mats[slot]);
        }

        // レンダラーが複数マテリアルを持つ場合に、選択肢を出すトグルリスト（ポップアップメニュー）
        private void ShowPoiyomiShadowSyncSlotMenu(Renderer r)
        {
            var mats = r.sharedMaterials;
            var menu = new GenericMenu();
            for (int i = 0; i < mats.Length; i++)
            {
                int idx = i;
                string label = $"{i}: {(mats[i] != null ? mats[i].name : "(なし)")}";
                menu.AddItem(new GUIContent(label), idx == _poiShadowSyncSourceSlot, () => SetPoiyomiShadowSyncSourceFromRenderer(r, idx));
            }
            menu.ShowAsContext();
        }

        private void RefreshPoiyomiShadowSyncSourceCard()
        {
            _poiShadowSyncSourceField?.SetValueWithoutNotify(_poiShadowSyncSource);
            if (_poiShadowSyncSlotButton != null)
            {
                bool multi = _poiShadowSyncSourceRenderer != null && _poiShadowSyncSourceRenderer.sharedMaterials.Length > 1;
                _poiShadowSyncSlotButton.style.display = multi ? DisplayStyle.Flex : DisplayStyle.None;
                if (multi) _poiShadowSyncSlotButton.text = $"スロット{_poiShadowSyncSourceSlot} ▾";
            }
        }

        // 同じプロパティのスウォッチが複数箇所（影の見出し＋影色サブ行）にある場合、全てを一括登録して同時に更新できるようにする
        private void RegisterPoiyomiColorBox(string prop, Action<Color, bool> update)
        {
            if (!_poiShadowSwatchUpdaters.TryGetValue(prop, out var list))
            {
                list = new List<Action<Color, bool>>();
                _poiShadowSwatchUpdaters[prop] = list;
            }
            list.Add(update);
        }

        private void RefreshPoiyomiShadowSyncSwatches()
        {
            foreach (var kv in _poiShadowSwatchUpdaters)
            {
                string prop = kv.Key;
                bool has = TryGetRawPoiyomiColor(prop, out var raw);
                bool isNone = has && raw.a <= 0.0001f;
                var color = has ? AdjustColor(raw, _poiShadowHueShift, _poiShadowSaturationScale) : new Color(0.25f, 0.25f, 0.25f);
                foreach (var update in kv.Value) update(color, isNone);
            }
        }

        // コピー元マテリアル／選択中プリセットから、補正前の生の色を取得する（liltoon版TryGetRawShadowColorと同じ役割）
        private bool TryGetRawPoiyomiColor(string prop, out Color raw)
        {
            if (_poiShadowSyncSource != null && _poiShadowSyncSource.HasProperty(prop))
            {
                raw = _poiShadowSyncSource.GetColor(prop);
                return true;
            }
            if (_poiActiveShadowPreset != null)
            {
                var p = _poiActiveShadowPreset.props.FirstOrDefault(x => x.key == prop);
                if (p != null) { raw = new Color(p.r, p.g, p.b, p.a); return true; }
            }
            raw = default;
            return false;
        }

        // コピー元マテリアル／選択中プリセットから、補正前の生の数値(Float/Int/Vector)を取得する（liltoon版TryGetRawShadowValueと同じ役割）
        // Poiyomiの列挙値プロパティ（Int）もシェーダー上はFloatとして保持されているため、CopyPoiyomiParamsと同様にIntはFloat経路に合流させる
        private bool TryGetRawPoiyomiValue(string prop, ShadowParamType type, out Vector4 raw)
        {
            if (_poiShadowSyncSource != null && _poiShadowSyncSource.HasProperty(prop))
            {
                raw = type == ShadowParamType.Vector
                    ? _poiShadowSyncSource.GetVector(prop)
                    : new Vector4(_poiShadowSyncSource.GetFloat(prop), 0, 0, 0);
                return true;
            }
            if (_poiActiveShadowPreset != null)
            {
                var p = _poiActiveShadowPreset.props.FirstOrDefault(x => x.key == prop);
                if (p != null)
                {
                    raw = type == ShadowParamType.Vector ? new Vector4(p.r, p.g, p.b, p.a) : new Vector4(p.value, 0, 0, 0);
                    return true;
                }
            }
            raw = default;
            return false;
        }

        private void UpdatePoiyomiMasterToggle()
        {
            if (_poiShadowSyncMasterToggle == null) return;
            bool allOn = _poiShadowSyncColorEnabled && _poiShadowSyncNumEnabled
                && _poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimLight)
                && _poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimShade)
                && _poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.Backlight);
            _poiShadowSyncMasterToggle.SetValueWithoutNotify(allOn);
        }

        // 「影」だけliltoon版と同じ折りたたみ構造（見出し＋影色/影数値の個別ON/OFF）にする
        private void BuildPoiyomiShadowCategory(VisualElement leftScroll)
        {
            var shadowContent = new VisualElement();
            shadowContent.style.display = DisplayStyle.None;
            bool[] shadowOpen = { false };

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
            hdr.tooltip = "対応しているのはMultilayer Mathのみです。他のライティング方式（Skin/Wrapped等）のマテリアルは正しく同期されません。";

            _poiShadowShadowToggle = new Toggle();
            _poiShadowShadowToggle.value = _poiShadowSyncColorEnabled && _poiShadowSyncNumEnabled;
            _poiShadowShadowToggle.style.marginRight = 2;
            _poiShadowShadowToggle.style.flexShrink  = 0;
            _poiShadowShadowToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
            _poiShadowShadowToggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            _poiShadowShadowToggle.RegisterValueChangedCallback(evt =>
            {
                _poiShadowSyncColorEnabled = evt.newValue;
                _poiShadowSyncNumEnabled   = evt.newValue;
                _poiShadowColorToggle?.SetValueWithoutNotify(evt.newValue);
                _poiShadowNumToggle?.SetValueWithoutNotify(evt.newValue);
                UpdatePoiyomiMasterToggle();
                UpdatePoiyomiShadowSyncButtons();
            });
            hdr.Add(_poiShadowShadowToggle);

            var title = new Label("影");
            title.style.fontSize                = 11;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color                   = TextColor;
            title.style.marginLeft              = 3;
            title.style.flexShrink              = 1;
            title.style.minWidth                = 0;
            title.style.overflow                = Overflow.Hidden;
            title.style.textOverflow            = TextOverflow.Ellipsis;
            title.style.whiteSpace              = WhiteSpace.NoWrap;
            hdr.Add(title);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            hdr.Add(spacer);

            foreach (var prop in POIYOMI_SWATCH_PROPS[PoiyomiShadowParamGroup.LightData])
            {
                var swatch = MakeShadowSwatchVisual(26, 4, out var update);
                swatch.style.marginLeft = 3;
                RegisterPoiyomiColorBox(prop, update);
                hdr.Add(swatch);
            }

            var chevron = new Label("▶");
            chevron.style.fontSize     = 10;
            chevron.style.color        = DimColor;
            chevron.style.flexShrink   = 0;
            chevron.style.paddingLeft  = 6;
            chevron.style.paddingRight = 4;
            chevron.style.paddingTop   = 2;
            chevron.style.paddingBottom = 2;
            chevron.RegisterCallback<MouseDownEvent>(e =>
            {
                shadowOpen[0] = !shadowOpen[0];
                shadowContent.style.display = shadowOpen[0] ? DisplayStyle.Flex : DisplayStyle.None;
                chevron.text = shadowOpen[0] ? "▼" : "▶";
                e.StopPropagation();
            });
            hdr.Add(chevron);

            leftScroll.Add(hdr);

            // ── 影色 ──
            {
                var row = new VisualElement();
                row.style.flexDirection     = FlexDirection.Row;
                row.style.alignItems        = Align.Center;
                row.style.paddingLeft       = 20;
                row.style.paddingRight      = 8;
                row.style.paddingTop        = 4;
                row.style.paddingBottom     = 4;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = PaneBorderColor;

                _poiShadowColorToggle = new Toggle();
                _poiShadowColorToggle.value = _poiShadowSyncColorEnabled;
                _poiShadowColorToggle.style.marginRight = 2;
                _poiShadowColorToggle.style.flexShrink  = 0;
                _poiShadowColorToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                _poiShadowColorToggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                _poiShadowColorToggle.RegisterValueChangedCallback(evt =>
                {
                    _poiShadowSyncColorEnabled = evt.newValue;
                    _poiShadowShadowToggle?.SetValueWithoutNotify(_poiShadowSyncColorEnabled && _poiShadowSyncNumEnabled);
                    UpdatePoiyomiMasterToggle();
                    UpdatePoiyomiShadowSyncButtons();
                });
                row.Add(_poiShadowColorToggle);

                var colorTitle = new Label("影色");
                colorTitle.style.fontSize   = 10;
                colorTitle.style.color      = DimColor;
                colorTitle.style.marginLeft = 3;
                row.Add(colorTitle);

                foreach (var prop in POIYOMI_SWATCH_PROPS[PoiyomiShadowParamGroup.LightData])
                {
                    var swatch = MakeShadowSwatchVisual(20, 3, out var update, showAlphaLabel: false);
                    swatch.style.marginLeft = 3;
                    RegisterPoiyomiColorBox(prop, update);
                    row.Add(swatch);
                }

                shadowContent.Add(row);
            }

            // ── 影数値 ──
            {
                var row = new VisualElement();
                row.style.flexDirection     = FlexDirection.Row;
                row.style.alignItems        = Align.Center;
                row.style.paddingLeft       = 20;
                row.style.paddingRight      = 8;
                row.style.paddingTop        = 4;
                row.style.paddingBottom     = 4;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = PaneBorderColor;

                _poiShadowNumToggle = new Toggle();
                _poiShadowNumToggle.value = _poiShadowSyncNumEnabled;
                _poiShadowNumToggle.style.marginRight = 2;
                _poiShadowNumToggle.style.flexShrink  = 0;
                _poiShadowNumToggle.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                _poiShadowNumToggle.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                _poiShadowNumToggle.RegisterValueChangedCallback(evt =>
                {
                    _poiShadowSyncNumEnabled = evt.newValue;
                    _poiShadowShadowToggle?.SetValueWithoutNotify(_poiShadowSyncColorEnabled && _poiShadowSyncNumEnabled);
                    UpdatePoiyomiMasterToggle();
                    UpdatePoiyomiShadowSyncButtons();
                });
                row.Add(_poiShadowNumToggle);

                var numTitle = new Label("影数値");
                numTitle.style.fontSize   = 10;
                numTitle.style.color      = DimColor;
                numTitle.style.flexGrow   = 1;
                numTitle.style.marginLeft = 3;
                row.Add(numTitle);

                shadowContent.Add(row);
            }

            leftScroll.Add(shadowContent);
        }

        private void RebuildPoiyomiShadowSyncDestList()
        {
            if (_poiShadowSyncListEl == null) return;

            _poiShadowSyncSourceField?.SetValueWithoutNotify(_poiShadowSyncSource);
            _poiShadowSyncDestSet.IntersectWith(_materials);
            _poiShadowSyncListEl.Clear();

            bool dark = EditorGUIUtility.isProSkin;
            var thumbImages = new Dictionary<Material, Image>();
            var pending     = new HashSet<Material>();

            if (_materials.Count == 0)
            {
                var empty = new Label("マテリアルが見つかりません。");
                empty.style.fontSize   = 11;
                empty.style.color      = DimColor;
                empty.style.unityFontStyleAndWeight = FontStyle.Italic;
                empty.style.marginTop    = 16;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                _poiShadowSyncListEl.Add(empty);
            }

            foreach (var mat in _materials)
            {
                if (mat == null || mat == _poiShadowSyncSource) continue;
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
                chk.value             = _poiShadowSyncDestSet.Contains(mat);
                chk.style.marginRight = 4;
                chk.style.marginTop   = 7;
                chk.style.flexShrink  = 0;
                chk.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                chk.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                chk.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _poiShadowSyncDestSet.Add(matRef);
                    else              _poiShadowSyncDestSet.Remove(matRef);
                    UpdatePoiyomiShadowSyncButtons();
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
                nameLbl.style.fontSize                = 12;
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
                _poiShadowSyncListEl.Add(row);
            }

            if (pending.Count > 0)
            {
                _poiShadowSyncListEl.schedule.Execute(() =>
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

            UpdatePoiyomiShadowSyncButtons();
        }

        private bool HasActivePoiyomiSource => _poiShadowSyncSource != null || _poiActiveShadowPreset != null;

        private void UpdatePoiyomiShadowSyncButtons()
        {
            if (_poiShadowSyncCountLabel != null)
            {
                int avail = _materials.Count(m => m != null && m != _poiShadowSyncSource);
                _poiShadowSyncCountLabel.text = $"コピー先 ({_poiShadowSyncDestSet.Count}/{avail})";
            }
            if (_poiShadowSyncSyncBtn != null)
            {
                if (_poiActiveShadowPreset != null)
                    _poiShadowSyncSyncBtn.text = $"{_poiActiveShadowPreset.name} に同期";
                else if (_poiShadowSyncSource != null)
                    _poiShadowSyncSyncBtn.text = $"{_poiShadowSyncSource.name} に同期";
                else
                    _poiShadowSyncSyncBtn.text = "同期";

                _poiShadowSyncSyncBtn.SetEnabled(HasActivePoiyomiSource
                    && _poiShadowSyncDestSet.Count > 0
                    && HasAnyPoiyomiCategoryEnabled());
            }
        }

        private bool HasAnyPoiyomiCategoryEnabled() =>
            _poiShadowSyncEnabledCategories.Count > 0 || _poiShadowSyncColorEnabled || _poiShadowSyncNumEnabled;

        private void ExecutePoiyomiShadowSync()
        {
            if (!HasActivePoiyomiSource || _poiShadowSyncDestSet.Count == 0 || !HasAnyPoiyomiCategoryEnabled())
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Poiyomi Shadow Sync");

            foreach (var dest in _poiShadowSyncDestSet.Where(m => m != null))
            {
                Undo.RecordObject(dest, "Poiyomi Shadow Sync");
                CopyPoiyomiShadowSyncToMaterial(dest);
                EditorUtility.SetDirty(dest);
                // _LightingMode/_RimStyle/_ShadingEnabled等はKeywordEnum/ThryToggleでシェーダーキーワードと連動しており、
                // SetFloatだけではキーワードが追従しないため明示的に再同期する（インスペクターを触ると直って見えるのはこのため）
                MaterialEditor.ApplyMaterialPropertyDrawers(dest);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private void CopyPoiyomiShadowSyncToMaterial(Material dest)
        {
            if (!HasActivePoiyomiSource || dest == null) return;

            if (_poiShadowSyncColorEnabled || _poiShadowSyncNumEnabled)
            {
                int lightingMode = POIYOMI_DEFAULT_LIGHTING_MODE;
                if (_poiShadowSyncSource != null)
                {
                    lightingMode = _poiShadowSyncSource.HasProperty("_LightingMode")
                        ? (int)_poiShadowSyncSource.GetFloat("_LightingMode")
                        : POIYOMI_DEFAULT_LIGHTING_MODE;
                    if (!POIYOMI_SHADOWS_PARAMS_BY_MODE.ContainsKey(lightingMode))
                        lightingMode = POIYOMI_DEFAULT_LIGHTING_MODE;
                }

                // 値だけでなくモード自体をコピー先に強制しないと、コピー先の見た目のモードが違う場合に値が反映されない。
                // プリセットのみの場合はモード自体を保存していないため、Multilayer Math固定として扱う。
                if (dest.HasProperty("_LightingMode"))
                    dest.SetFloat("_LightingMode", _poiShadowSyncSource != null && _poiShadowSyncSource.HasProperty("_LightingMode")
                        ? _poiShadowSyncSource.GetFloat("_LightingMode")
                        : POIYOMI_DEFAULT_LIGHTING_MODE);
                if (dest.HasProperty("_ShadingEnabled"))
                    dest.SetFloat("_ShadingEnabled", 1f);

                // 影色（Colorタイプ）は_poiShadowSyncColorEnabled、影数値（それ以外）は_poiShadowSyncNumEnabledで個別に絞り込む
                bool ShadowFilter(ShadowParam sp) => sp.Type == ShadowParamType.Color ? _poiShadowSyncColorEnabled : _poiShadowSyncNumEnabled;
                CopyPoiyomiParams(dest, POIYOMI_LIGHTDATA_PARAMS, ShadowFilter);
                if (POIYOMI_SHADOWS_PARAMS_BY_MODE.TryGetValue(lightingMode, out var shadowParams))
                    CopyPoiyomiParams(dest, shadowParams, ShadowFilter);
            }

            if (_poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimLight))
            {
                if (dest.HasProperty("_RimStyle")) dest.SetFloat("_RimStyle", 0f);
                if (dest.HasProperty("_EnableRimLighting")) dest.SetFloat("_EnableRimLighting", 1f);
                CopyPoiyomiParams(dest, POIYOMI_RIM0_PARAMS);
            }

            if (_poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimShade))
            {
                if (dest.HasProperty("_Rim2Style")) dest.SetFloat("_Rim2Style", 0f);
                if (dest.HasProperty("_EnableRim2Lighting")) dest.SetFloat("_EnableRim2Lighting", 1f);
                CopyPoiyomiParams(dest, POIYOMI_RIM1_PARAMS);
            }

            if (_poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.Backlight))
            {
                if (dest.HasProperty("_BacklightEnabled")) dest.SetFloat("_BacklightEnabled", 1f);
                CopyPoiyomiParams(dest, POIYOMI_BACKLIGHT_PARAMS);
            }
        }

        private void CopyPoiyomiParams(Material dest, ShadowParam[] table, Func<ShadowParam, bool> filter = null)
        {
            foreach (var sp in table)
            {
                if (filter != null && !filter(sp)) continue;
                if (!dest.HasProperty(sp.Prop)) continue;
                switch (sp.Type)
                {
                    case ShadowParamType.Color:
                        if (!TryGetRawPoiyomiColor(sp.Prop, out var srcColor)) continue;
                        // 強制ライトカラーはシーンのライト色を直接指定する用途のため、色相/彩度シフトの対象外にする
                        dest.SetColor(sp.Prop, sp.Prop == "_LightingForcedColor"
                            ? srcColor
                            : AdjustColor(srcColor, _poiShadowHueShift, _poiShadowSaturationScale));
                        break;
                    case ShadowParamType.Vector:
                        if (!TryGetRawPoiyomiValue(sp.Prop, sp.Type, out var vecRaw)) continue;
                        dest.SetVector(sp.Prop, vecRaw);
                        break;
                    // Poiyomiの列挙値プロパティ（Int）もシェーダー上はFloatとして保持されているため、liltoon版と違いIntはFloat経路に合流させる
                    default:
                        if (!TryGetRawPoiyomiValue(sp.Prop, sp.Type, out var floatRaw)) continue;
                        dest.SetFloat(sp.Prop, floatRaw.x);
                        break;
                }
            }
        }

        // ─── プリセット棚：読み込み・保存・削除・リネーム（liltoon版 LoadBuiltinPresets/SaveBuiltinPreset/… と同じ構造） ─

        private void LoadPoiyomiPresets()
        {
            _poiPresets.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:ShadowSyncPresetAsset", new[] { POI_SHADOW_PRESET_ASSET_FOLDER }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var so   = AssetDatabase.LoadAssetAtPath<ShadowSyncPresetAsset>(path);
                if (so == null) continue;
                _poiPresets.Add(new ShadowSyncPresetEntry
                {
                    name      = so.name,
                    category  = so.category,
                    props     = so.props,
                    assetPath = path,
                    isBundled = false, // poiyomiプリセットは常にユーザー作成（同梱プリセットは無い）
                });
            }
        }

        // 現在のコピー元・カテゴリ選択から作ったプリセットを、liltoon版と同じ方式で
        // Assets/qsyi/poiyomipreset 配下に1プリセット=1アセットとして保存する。
        private void SavePoiyomiPreset(ShadowSyncPresetEntry entry)
        {
            EnsureAssetFolder(POI_SHADOW_PRESET_ASSET_FOLDER);

            var so = ScriptableObject.CreateInstance<ShadowSyncPresetAsset>();
            so.category = entry.category;
            so.props    = entry.props;

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
                $"{POI_SHADOW_PRESET_ASSET_FOLDER}/{SanitizeAssetFileName(entry.name)}.asset");
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();

            LoadPoiyomiPresets();
            RebuildPoiyomiShadowPresetList();
        }

        // プリセットカードの右クリックメニューから呼ばれる。対応する.assetファイルを削除する。
        private void DeletePoiyomiPreset(ShadowSyncPresetEntry entry)
        {
            if (!EditorUtility.DisplayDialog("プリセット削除", $"プリセット「{entry.name}」を削除しますか？", "削除", "キャンセル"))
                return;
            if (string.IsNullOrEmpty(entry.assetPath)) return;

            AssetDatabase.DeleteAsset(entry.assetPath);

            if (_poiActiveShadowPreset != null && _poiActiveShadowPreset.name == entry.name)
            {
                _poiActiveShadowPreset = null;
                UpdatePoiyomiShadowSyncButtons();
                RefreshPoiyomiShadowSyncSwatches();
                RefreshPoiyomiShadowSyncSourceCard();
            }

            LoadPoiyomiPresets();
            RebuildPoiyomiShadowPresetList();
        }

        // プリセットカードの右クリックメニューから呼ばれる。.assetファイル名（＝表示名）を変更する。
        private void RenamePoiyomiPreset(ShadowSyncPresetEntry entry, string newName)
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

            bool wasActive = _poiActiveShadowPreset == entry;
            LoadPoiyomiPresets();
            if (wasActive)
                _poiActiveShadowPreset = _poiPresets.FirstOrDefault(x => x.assetPath == entry.assetPath);

            RebuildPoiyomiShadowPresetList();
            UpdatePoiyomiShadowSyncButtons();
        }

        // 同期可能な全項目から、プリセット保存ウィンドウ用のプレビューデータを作る（liltoon版BuildShadowPresetPreviewItemsと同じ役割）。
        // included初期値はカテゴリごとのON/OFF状態から決める（影色/影数値はチェック split、リム系はカテゴリ単位のオールオアナッシング）。
        private List<ShadowPresetSaveEntryData> BuildPoiyomiShadowPresetPreviewItems()
        {
            var items = new List<ShadowPresetSaveEntryData>();
            var rim0Props = new HashSet<string>(POIYOMI_RIM0_PARAMS.Select(p => p.Prop));
            var rim1Props = new HashSet<string>(POIYOMI_RIM1_PARAMS.Select(p => p.Prop));
            var backProps = new HashSet<string>(POIYOMI_BACKLIGHT_PARAMS.Select(p => p.Prop));

            bool IsIncluded(ShadowParam sp)
            {
                if (rim0Props.Contains(sp.Prop)) return _poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimLight);
                if (rim1Props.Contains(sp.Prop)) return _poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimShade);
                if (backProps.Contains(sp.Prop)) return _poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.Backlight);
                return sp.Type == ShadowParamType.Color ? _poiShadowSyncColorEnabled : _poiShadowSyncNumEnabled;
            }

            foreach (var sp in AllPoiyomiShadowParams())
            {
                bool included = IsIncluded(sp);

                if (sp.Type == ShadowParamType.Color)
                {
                    if (!TryGetRawPoiyomiColor(sp.Prop, out var raw)) continue;
                    var adjusted = AdjustColor(raw, _poiShadowHueShift, _poiShadowSaturationScale);
                    items.Add(new ShadowPresetSaveEntryData { prop = sp.Prop, label = sp.Label, isColor = true, color = adjusted, group = sp.Group, included = included });
                }
                else
                {
                    if (!TryGetRawPoiyomiValue(sp.Prop, sp.Type, out var raw)) continue;
                    string text = sp.Type == ShadowParamType.Vector
                        ? $"({raw.x:0.###}, {raw.y:0.###}, {raw.z:0.###}, {raw.w:0.###})"
                        : sp.Type == ShadowParamType.Int
                            ? Mathf.RoundToInt(raw.x).ToString()
                            : raw.x.ToString("0.###");
                    items.Add(new ShadowPresetSaveEntryData { prop = sp.Prop, label = sp.Label, isColor = false, valueText = text, group = sp.Group, included = included });
                }
            }
            return items;
        }

        // 「プリセット保存」ボタンから呼ばれる。保存内容のプレビューを表示する別ウィンドウを開く。
        private void OpenPoiyomiShadowPresetSaveWindow()
        {
            if (!HasActivePoiyomiSource) return;
            var items = BuildPoiyomiShadowPresetPreviewItems();
            string defaultName = _poiShadowSyncSource != null ? _poiShadowSyncSource.name
                                : _poiActiveShadowPreset != null ? _poiActiveShadowPreset.name
                                : "";
            ShadowPresetSaveWindow.Open(defaultName, items, DoSavePoiyomiPreset);
        }

        // プリセット保存ウィンドウの保存ボタンから呼ばれる。ウィンドウ内でチェックが付いている項目のみを保存する。
        private void DoSavePoiyomiPreset(string presetName, string category, HashSet<string> includedProps)
        {
            var entry = new ShadowSyncPresetEntry { name = presetName, category = category };
            foreach (var sp in AllPoiyomiShadowParams())
            {
                if (!includedProps.Contains(sp.Prop)) continue;

                if (sp.Type == ShadowParamType.Color)
                {
                    if (!TryGetRawPoiyomiColor(sp.Prop, out var raw)) continue;
                    var adjusted = AdjustColor(raw, _poiShadowHueShift, _poiShadowSaturationScale);
                    entry.props.Add(new ShadowSyncPresetProp { key = sp.Prop, r = adjusted.r, g = adjusted.g, b = adjusted.b, a = adjusted.a });
                }
                else
                {
                    if (!TryGetRawPoiyomiValue(sp.Prop, sp.Type, out var raw)) continue;
                    if (sp.Type == ShadowParamType.Vector)
                        entry.props.Add(new ShadowSyncPresetProp { key = sp.Prop, r = raw.x, g = raw.y, b = raw.z, a = raw.w });
                    else
                        entry.props.Add(new ShadowSyncPresetProp { key = sp.Prop, value = raw.x });
                }
            }
            SavePoiyomiPreset(entry);
        }

        private void RebuildPoiyomiShadowPresetList()
        {
            if (_poiShadowPresetListEl == null) return;
            _poiShadowPresetListEl.Clear();

            if (_poiPresets.Count == 0)
            {
                var none = new Label("(なし)");
                none.style.fontSize = 10;
                none.style.color    = DimColor;
                _poiShadowPresetListEl.Add(none);
                return;
            }

            List<Color> PresetSwatchColors(ShadowSyncPresetEntry preset)
            {
                var colors = new List<Color>();
                foreach (var sp in AllPoiyomiShadowParams())
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
                bool isActive = _poiActiveShadowPreset == p;

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

                // poiyomiプリセットは常にユーザー作成（同梱なし）だが、liltoon版と同じ削除可否パターンを将来の同梱プリセット追加に備えて残す
                bool deletable = !p.isBundled;

                var nameLbl = new Label(p.name);
                nameLbl.style.fontSize                = 11;
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.color                   = TextColor;
                nameLbl.style.unityTextAlign          = TextAnchor.MiddleCenter;
                nameLbl.style.whiteSpace              = WhiteSpace.NoWrap;
                nameLbl.style.overflow                = Overflow.Hidden;
                card.Add(nameLbl);

                if (p.isBundled)
                    card.tooltip = "qsToolBox付属のプリセットのため削除できません";

                card.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    evt.StopPropagation();
                    if (_poiActiveShadowPreset == p)
                    {
                        _poiActiveShadowPreset = null;
                    }
                    else
                    {
                        _poiActiveShadowPreset = p;
                        _poiShadowSyncSource   = null;
                        _poiShadowSyncSourceRenderer = null;
                        if (_poiShadowSyncCreatePresetBtn != null)
                            _poiShadowSyncCreatePresetBtn.SetEnabled(false);
                        RestorePoiyomiPresetCheckboxes(p);
                    }
                    RebuildPoiyomiShadowPresetList();
                    UpdatePoiyomiShadowSyncButtons();
                    RefreshPoiyomiShadowSyncSwatches();
                    RefreshPoiyomiShadowSyncSourceCard();
                });

                card.RegisterCallback<ContextClickEvent>(evt =>
                {
                    var menu = new GenericMenu();
                    if (deletable)
                    {
                        menu.AddItem(new GUIContent("名前を変更"), false, () =>
                            ShadowPresetRenameWindow.Open(p.name, newName => RenamePoiyomiPreset(p, newName)));
                        menu.AddItem(new GUIContent("削除"), false, () => DeletePoiyomiPreset(p));
                    }
                    else
                        menu.AddDisabledItem(new GUIContent("削除（qsToolBox付属のプリセットのため不可）"));
                    menu.ShowAsContext();
                    evt.StopPropagation();
                });
                return card;
            }

            foreach (var g in _poiPresets.GroupBy(p => string.IsNullOrEmpty(p.category) ? "その他" : p.category))
            {
                var shown = g.ToList();
                if (shown.Count == 0) continue;

                bool open = !_poiShadowPresetCategoryOpen.TryGetValue(g.Key, out var o) || o;

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

                var catLbl = new Label($"{g.Key} ({shown.Count})");
                catLbl.style.fontSize                = 11;
                catLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                catLbl.style.color                   = TextColor;
                catHdr.Add(catLbl);

                string catKey  = g.Key;
                bool   curOpen = open;
                catHdr.RegisterCallback<MouseDownEvent>(evt =>
                {
                    _poiShadowPresetCategoryOpen[catKey] = !curOpen;
                    RebuildPoiyomiShadowPresetList();
                });
                _poiShadowPresetListEl.Add(catHdr);

                if (open)
                {
                    var wrap = new VisualElement();
                    wrap.style.flexDirection = FlexDirection.Row;
                    wrap.style.flexWrap      = Wrap.Wrap;
                    wrap.style.marginBottom  = 2;
                    foreach (var preset in shown)
                        wrap.Add(MakePresetCard(preset));
                    _poiShadowPresetListEl.Add(wrap);
                }
            }
        }

        // プリセット選択時、そのプリセットが持つキーからカテゴリ単位のON/OFFを逆算してUIに反映する
        // （poiyomiにはliltoon版のような単一プロパティ粒度の選択が無く、影色/影数値split + カテゴリ単位オールオアナッシングのみのため）
        private void RestorePoiyomiPresetCheckboxes(ShadowSyncPresetEntry preset)
        {
            var keys = new HashSet<string>(preset.props.Select(p => p.key));
            var shadowTable = POIYOMI_LIGHTDATA_PARAMS.Concat(POIYOMI_SHADOWS_PARAMS_BY_MODE[POIYOMI_DEFAULT_LIGHTING_MODE]);

            bool HasAny(IEnumerable<ShadowParam> table, Func<ShadowParam, bool> pred = null) =>
                table.Any(sp => (pred == null || pred(sp)) && keys.Contains(sp.Prop));

            _poiShadowSyncColorEnabled = HasAny(shadowTable, sp => sp.Type == ShadowParamType.Color);
            _poiShadowSyncNumEnabled   = HasAny(shadowTable, sp => sp.Type != ShadowParamType.Color);

            _poiShadowSyncEnabledCategories.Clear();
            if (HasAny(POIYOMI_RIM0_PARAMS))      _poiShadowSyncEnabledCategories.Add(PoiyomiShadowParamGroup.RimLight);
            if (HasAny(POIYOMI_RIM1_PARAMS))      _poiShadowSyncEnabledCategories.Add(PoiyomiShadowParamGroup.RimShade);
            if (HasAny(POIYOMI_BACKLIGHT_PARAMS)) _poiShadowSyncEnabledCategories.Add(PoiyomiShadowParamGroup.Backlight);

            _poiShadowShadowToggle?.SetValueWithoutNotify(_poiShadowSyncColorEnabled && _poiShadowSyncNumEnabled);
            _poiShadowColorToggle?.SetValueWithoutNotify(_poiShadowSyncColorEnabled);
            _poiShadowNumToggle?.SetValueWithoutNotify(_poiShadowSyncNumEnabled);

            // _poiShadowCategoryToggles はAddCategoryToggle呼び出し順（リムライト1→リムライト2→逆光ライト）で並んでいる
            if (_poiShadowCategoryToggles.Count >= 3)
            {
                _poiShadowCategoryToggles[0].SetValueWithoutNotify(_poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimLight));
                _poiShadowCategoryToggles[1].SetValueWithoutNotify(_poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.RimShade));
                _poiShadowCategoryToggles[2].SetValueWithoutNotify(_poiShadowSyncEnabledCategories.Contains(PoiyomiShadowParamGroup.Backlight));
            }

            UpdatePoiyomiMasterToggle();
        }
    }
}
#endif
