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
    internal partial class QsToolBox
    {
        // ─── シェイプキー元タブの2モード ───────────────────────────────
        private enum ShapeKeySubMode { Compose, BlinkCorrection }
        private ShapeKeySubMode _shapeKeySubMode = ShapeKeySubMode.Compose;
        private Button _shapeKeyComposeModeBtn;
        private Button _shapeKeyBlinkModeBtn;
        private VisualElement _shapeKeySubPaneSlot;

        // ─── まばたき修正モード ─────────────────────────────────────
        private enum BlinkShapeSide { Left, Right, Both }

        [Serializable]
        private class BlinkTargetInfo
        {
            public string shapeKeyName;
            public bool enabled;
            // side は持たない。補正適用時（ApplyBlinkCorrection）に DetectTargetSide でメッシュから自動検出する。
        }

        // 組み込みプリセットは同梱の BlinkPresetAsset（1プリセット=1アセット、ShadowSyncPresetAssetと同じ方式）から読み込む。
        // ScriptableObjectアセットなので単体でのエクスポート・配布・将来的な販売にも流用できる。
        private readonly List<BlinkPresetAsset> _builtinBlinkPresets = new List<BlinkPresetAsset>();

        // 左右の按分をどれだけ滑らかにブレンドするか（Wolferia_selestia参考実装のseparateSmoothRangeと同じ役割）
        private const float BLINK_SIDE_SMOOTH_RANGE = 0.001f;

        // 「改変済み目元シェイプキー」の自動検出に使う閾値。
        // BLINK_SOURCE_DETECT_VERTEX_EPS: まばたきターゲットが「動かしている」とみなす頂点変位の下限（浮動小数の誤差程度は無視）。
        // BLINK_SOURCE_DETECT_RATIO_THRESHOLD: 候補シェイプキーの総変位量のうち、ターゲットが動かす頂点に乗っている割合がこれ以上なら候補として拾う。
        private const float BLINK_SOURCE_DETECT_VERTEX_EPS = 1e-6f;
        private const float BLINK_SOURCE_DETECT_RATIO_THRESHOLD = 0.05f;

        // まばたきシェイプキー自身のL/R/両自動判定に使う閾値。
        // 頂点変位の重心が左側にこの割合以上寄っていればLeft、右側にこの割合以上寄っていればRight、
        // どちらでもなければ（左右対称に近い＝両目分含む）Bothと判定する。
        private const float BLINK_TARGET_SIDE_DETECT_THRESHOLD = 0.75f;

        // まばたき修正の焼き込み出力先（固定パス）。二重適用防止は「現在のメッシュがこのパスかどうか」だけで判定する。
        private const string BLINK_FIX_OUTPUT_FOLDER = "Assets/qsyi/GeneratedMeshes/BlinkFix";

        [SerializeField] private int _blinkPresetIndex = -1;
        [SerializeField] private List<BlinkTargetInfo> _blinkTargets = new List<BlinkTargetInfo>();
        [SerializeField] private List<string> _blinkSourceNames = new List<string>();

        // シェイプキー数が数百〜800件規模になることが多いため、ScrollViewへの全件展開ではなく
        // ListViewの仮想化（表示中の行だけ実体化）で軽さを確保する。合成モードのシェイプキー一覧も同じ理由で仮想化する。
        private ListView _blinkSourceListView;
        private ListView _blinkTargetListView;
        private readonly List<string> _blinkSourceFiltered = new List<string>();
        private readonly List<string> _blinkTargetFiltered = new List<string>();
        private string _blinkSourceSearch = "";
        private string _blinkTargetSearch = "";
        private Button _blinkApplyButton;
        private Button _blinkRestoreOriginalButton;
        private Label _blinkSourceHeaderLabel;
        private Label _blinkTargetHeaderLabel;
        private Label _blinkSourceEmptyLabel;
        private Label _blinkTargetEmptyLabel;

        // 目元シェイプキー候補の自動検出結果。まだチェックはせず一覧上でハイライトするだけに留める
        // （自動でチェックを入れる旧仕様から、ユーザーが最終判断してから使用ボタンを押す方式に変更）。
        // まばたきシェイプキーの選択が変わるたびに自動で再計算される（DetectBlinkSourceCandidates呼び出し箇所を参照）。
        private readonly HashSet<string> _blinkSourceCandidates = new HashSet<string>();
        private bool _blinkSourceCandidateFilterOnly = true;

        private class BlinkSourceRowView
        {
            public Label Dot;
            public Label NameLabel;
            public Label CandidateBadge;
            public Button ToggleBtn;
            public string ShapeName;
        }

        private class BlinkTargetRowView
        {
            public Label Dot;
            public Label NameLabel;
            public Label SideBadge;
            public Button ToggleBtn;
            public string ShapeName;
        }

        // メッシュが変わらない限り再利用するキャッシュ群（ベース頂点・スクラッチ配列・L/R/両判定結果）。
        private Mesh _blinkCachedMesh;
        private Vector3[] _blinkCachedBaseVertices;
        private Vector3[] _blinkScratchVertices;
        private Vector3[] _blinkScratchNormals;
        private Vector3[] _blinkScratchTangents;
        // まばたきシェイプキー自身のL/R/両自動判定結果のキャッシュ（メッシュが変わらない限り再計算しない）。
        private readonly Dictionary<string, BlinkShapeSide> _blinkTargetSideCache = new Dictionary<string, BlinkShapeSide>();

        private VisualElement _blendShapePane;
        private ObjectField _blendShapeTargetField;
        private ScrollView _blendShapeComposeScroll;
        private ListView _blendShapeShapeListView;
        private readonly List<string> _blendShapeShapeFiltered = new List<string>();
        private Label _blendShapeShapeHeaderLabel;
        private Label _blendShapeShapeEmptyLabel;
        private VisualElement _blendShapeBaseBand;
        private Label _blendShapeOverwriteTarget;
        private TextField _blendShapeNewNameField;
        private Button _blendShapeExecuteButton;
        private Label _blendShapeExecuteWarning;
        private SkinnedMeshRenderer _composeTarget;
        private string _baseShapeName = "";
        private readonly List<(string name, float weight)> _composeShapes = new List<(string, float)>();
        private string _composeSearchText = "";
        private readonly List<string> _shapeNames = new List<string>();
        private string _newShapeName = "";
        private bool _overwriteShape = true;

        private VisualElement BuildBlendShapePane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1;

            // ── シェイプキー元 / まばたき修正 の切り替え ──
            var modeRow = new VisualElement();
            modeRow.style.flexDirection     = FlexDirection.Row;
            modeRow.style.flexShrink        = 0;
            modeRow.style.paddingLeft       = modeRow.style.paddingRight  = 8;
            modeRow.style.paddingTop        = modeRow.style.paddingBottom = 4;
            modeRow.style.borderBottomWidth = 1;
            modeRow.style.borderBottomColor = PaneBorderColor;

            _shapeKeyComposeModeBtn = new Button(() => SetShapeKeySubMode(ShapeKeySubMode.Compose)) { text = "シェイプキー合成" };
            _shapeKeyComposeModeBtn.style.flexGrow = 1;
            _shapeKeyComposeModeBtn.style.height   = 22;
            _shapeKeyComposeModeBtn.style.fontSize = 11;
            _shapeKeyComposeModeBtn.style.marginRight = 3;
            modeRow.Add(_shapeKeyComposeModeBtn);

            _shapeKeyBlinkModeBtn = new Button(() => SetShapeKeySubMode(ShapeKeySubMode.BlinkCorrection)) { text = "まばたき修正" };
            _shapeKeyBlinkModeBtn.style.flexGrow = 1;
            _shapeKeyBlinkModeBtn.style.height   = 22;
            _shapeKeyBlinkModeBtn.style.fontSize = 11;
            modeRow.Add(_shapeKeyBlinkModeBtn);

            pane.Add(modeRow);

            // Header: target mesh
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems   = Align.Center;
            hdr.style.flexShrink   = 0;
            hdr.style.paddingLeft  = 10;
            hdr.style.paddingRight = 10;
            hdr.style.paddingTop   = hdr.style.paddingBottom = 7;
            hdr.style.borderBottomWidth = 1;
            hdr.style.borderBottomColor = PaneBorderColor;

            var targetLbl = new Label("対象メッシュ");
            targetLbl.style.fontSize = 11;
            targetLbl.style.color = DimColor;
            targetLbl.style.marginRight = 8;
            targetLbl.style.flexShrink = 0;
            hdr.Add(targetLbl);

            _blendShapeTargetField = new ObjectField();
            _blendShapeTargetField.objectType = typeof(SkinnedMeshRenderer);
            _blendShapeTargetField.allowSceneObjects = true;
            _blendShapeTargetField.value = _composeTarget;
            FixFieldOverflow(_blendShapeTargetField);
            _blendShapeTargetField.RegisterValueChangedCallback(evt =>
            {
                _composeTarget = evt.newValue as SkinnedMeshRenderer;
                ResetComposeData();
                ScanForCompose();
                RebuildBlendShapePane();
            });
            hdr.Add(_blendShapeTargetField);
            pane.Add(hdr);

            _shapeKeySubPaneSlot = new VisualElement();
            _shapeKeySubPaneSlot.style.flexGrow  = 1;
            _shapeKeySubPaneSlot.style.minHeight = 0;
            pane.Add(_shapeKeySubPaneSlot);

            RebuildShapeKeySubMode();
            return pane;
        }

        private void SetShapeKeySubMode(ShapeKeySubMode mode)
        {
            if (_shapeKeySubMode == mode) return;
            _shapeKeySubMode = mode;
            RebuildShapeKeySubMode();
        }

        // 外部（対象メッシュ変更・ScanDataからの再スキャン等）から呼ばれる窓口。
        // 静的なUI構造は壊さず、現在アクティブなサブモードの中身だけを更新する。
        private void RebuildBlendShapePane()
        {
            if (_shapeKeySubPaneSlot == null) return;
            if (_shapeKeySubMode == ShapeKeySubMode.Compose)
                RebuildComposeSubPane();
            else
                RebuildBlinkCorrectionSubPane();
        }

        private void RebuildShapeKeySubMode()
        {
            if (_shapeKeySubPaneSlot == null) return;

            bool composeActive = _shapeKeySubMode == ShapeKeySubMode.Compose;
            SetAccentHighlight(_shapeKeyComposeModeBtn, composeActive);
            SetAccentHighlight(_shapeKeyBlinkModeBtn, !composeActive);

            _shapeKeySubPaneSlot.Clear();
            if (composeActive)
            {
                _blendShapeComposeScroll    = null;
                _blendShapeShapeListView    = null;
                _blendShapeShapeHeaderLabel = null;
                _blendShapeShapeEmptyLabel  = null;
                _blendShapeBaseBand         = null;
                _blendShapeOverwriteTarget  = null;
                _blendShapeNewNameField     = null;
                _blendShapeExecuteButton    = null;
                _blendShapeExecuteWarning   = null;
                _shapeKeySubPaneSlot.Add(BuildComposeSubPane());
            }
            else
            {
                _blinkSourceListView    = null;
                _blinkTargetListView    = null;
                _blinkSourceHeaderLabel = null;
                _blinkTargetHeaderLabel = null;
                _blinkSourceEmptyLabel  = null;
                _blinkTargetEmptyLabel  = null;
                _blinkApplyButton       = null;
                _blinkRestoreOriginalButton = null;
                _shapeKeySubPaneSlot.Add(BuildBlinkCorrectionSubPane());
            }
        }

        // ─── UIヘルパー（合成/まばたき修正の両サブモードで共有） ───────────

        private static readonly Color BaseDotColor = new Color(0.30f, 0.75f, 0.35f);
        private static readonly Color InactiveDotColor = new Color(0.35f, 0.35f, 0.35f);
        private static readonly Color ConfirmBgColor = new Color(0.25f, 0.65f, 0.30f, 0.20f);
        private static readonly Color ConfirmTextColor = new Color(0.25f, 0.75f, 0.30f);
        // 自動検出された候補のハイライト色（実行ボタンの警告文と同じ暖色系を流用し、色数を増やさない）
        private static readonly Color CandidateHighlightColor = new Color(0.85f, 0.60f, 0.15f);

        // アクセントカラーでのON/OFFハイライト（モード切替ボタン、L/R/両ボタンなど）
        private static void SetAccentHighlight(VisualElement el, bool active)
        {
            if (el == null) return;
            el.style.backgroundColor = active
                ? new StyleColor(new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.30f))
                : new StyleColor(StyleKeyword.Null);
            el.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        }

        // 緑の「確定済み」ハイライト（ベース選択、使用中、対象など）
        private static void SetConfirmHighlight(VisualElement el, bool active)
        {
            if (el == null) return;
            el.style.backgroundColor = active ? new StyleColor(ConfirmBgColor) : new StyleColor(StyleKeyword.Null);
            el.style.color = active ? new StyleColor(ConfirmTextColor) : new StyleColor(StyleKeyword.Null);
        }

        // ObjectField/TextField/DropdownField共通：内部要素の既定min-widthがコンテナ幅を
        // 突き破ってはみ出すのを防ぐ（右端見切れ対策）。
        private static void FixFieldOverflow(VisualElement field)
        {
            field.style.flexGrow   = 1;
            field.style.flexShrink = 1;
            field.style.minWidth   = 0;
            field.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            field.RegisterCallback<AttachToPanelEvent>(_ =>
                field.Query<VisualElement>().Build().ForEach(e => e.style.minWidth = 0));
        }

        // Dot + 名前ラベルの行の骨格（合成シェイプキー一覧・まばたき修正の左右一覧で共通）
        private static VisualElement BuildListRow(out Label dot, out Label nameLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection     = FlexDirection.Row;
            row.style.alignItems        = Align.Center;
            row.style.paddingLeft       = row.style.paddingRight  = 8;
            row.style.paddingTop        = row.style.paddingBottom = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = PaneBorderColor;

            dot = new Label("●");
            dot.style.fontSize    = 8;
            dot.style.marginRight = 5;
            dot.style.flexShrink  = 0;
            row.Add(dot);

            nameLabel = new Label();
            nameLabel.style.fontSize   = 11;
            nameLabel.style.color      = TextColor;
            nameLabel.style.flexGrow   = 1;
            nameLabel.style.flexShrink = 1;
            nameLabel.style.minWidth   = new StyleLength(0f);
            nameLabel.style.overflow   = Overflow.Hidden;
            nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(nameLabel);

            return row;
        }

        // 列ヘッダー（太字タイトル＋下線）。件数バッジ更新用にタイトルラベルを返す。
        private static Label BuildColumnHeader(VisualElement parent, string title)
        {
            var hd = new VisualElement();
            hd.style.flexDirection     = FlexDirection.Row;
            hd.style.alignItems        = Align.Center;
            hd.style.flexShrink        = 0;
            hd.style.paddingLeft       = hd.style.paddingRight  = 8;
            hd.style.paddingTop        = hd.style.paddingBottom = 5;
            hd.style.borderBottomWidth = 1;
            hd.style.borderBottomColor = PaneBorderColor;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 11;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            hd.Add(titleLabel);

            parent.Add(hd);
            return titleLabel;
        }

        private static void SetHeaderCount(Label headerLabel, string baseTitle, int count)
        {
            if (headerLabel == null) return;
            headerLabel.text = $"{baseTitle}（{count}）";
        }

        // 検索欄＋クリアボタン（合成シェイプキー一覧・まばたき修正の左右一覧で共通）。
        // TextField内部の既定min-widthがコンテナ右端を突き破らないよう overflow:Hidden でラップする。
        private static VisualElement BuildSearchField(string initialValue, Action<string> onChange)
        {
            var searchRow = new VisualElement();
            searchRow.style.flexDirection     = FlexDirection.Row;
            searchRow.style.alignItems        = Align.Center;
            searchRow.style.flexShrink        = 0;
            searchRow.style.paddingLeft       = searchRow.style.paddingRight  = 8;
            searchRow.style.paddingTop        = searchRow.style.paddingBottom = 4;
            searchRow.style.borderBottomWidth = 1;
            searchRow.style.borderBottomColor = PaneBorderColor;

            var searchWrap = new VisualElement();
            searchWrap.style.flexGrow   = 1;
            searchWrap.style.flexShrink = 1;
            searchWrap.style.minWidth   = new StyleLength(0f);
            searchWrap.style.overflow   = Overflow.Hidden;

            var searchField = new TextField();
            searchField.value = initialValue;
            searchField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            searchField.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            searchWrap.Add(searchField);
            searchRow.Add(searchWrap);

            var clearSearchBtn = new Button(() =>
            {
                searchField.SetValueWithoutNotify("");
                onChange("");
            });
            clearSearchBtn.text = "✕";
            clearSearchBtn.style.width  = 20;
            clearSearchBtn.style.height = 20;
            clearSearchBtn.style.fontSize = 10;
            clearSearchBtn.style.paddingLeft = clearSearchBtn.style.paddingRight = 2;
            clearSearchBtn.style.paddingTop  = clearSearchBtn.style.paddingBottom = 2;
            clearSearchBtn.style.marginLeft  = 4;
            clearSearchBtn.style.flexShrink  = 0;
            searchRow.Add(clearSearchBtn);

            return searchRow;
        }

        // 一覧の「該当なし」プレースホルダー
        private static Label BuildEmptyPlaceholder()
        {
            var label = new Label();
            label.style.fontSize    = 11;
            label.style.color       = DimColor;
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            label.style.marginTop   = 12;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace  = WhiteSpace.Normal;
            label.style.display     = DisplayStyle.None;
            return label;
        }

        // フィルタ結果が0件のときプレースホルダーを表示し、ListView側は隠す。
        // 3つの一覧（合成シェイプキー一覧・まばたき修正の左右一覧）はいずれも _shapeNames を母集合とするため共通化できる。
        private void UpdateEmptyPlaceholder(ListView listView, Label emptyLabel, int filteredCount, string search)
        {
            bool empty = filteredCount == 0;
            listView.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            if (emptyLabel == null) return;

            emptyLabel.text = _shapeNames.Count == 0
                ? (_composeTarget == null ? "対象メッシュを選択してください" : "シェイプキーがありません")
                : string.IsNullOrEmpty(search)
                    ? "シェイプキーがありません"
                    : $"「{search}」に一致するシェイプキーがありません";
            emptyLabel.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement BuildComposeSubPane()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.flexGrow      = 1;
            container.style.minHeight     = 0;

            // Body: 2-column
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow  = 1;
            body.style.minHeight = 0;

            // Left: compose list
            var leftPane = new VisualElement();
            leftPane.style.flexGrow  = 1;
            leftPane.style.minHeight = 0;
            leftPane.style.flexBasis = new StyleLength(0f);
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = PaneBorderColor;

            BuildColumnHeader(leftPane, "合成リスト");

            _blendShapeBaseBand = new VisualElement();
            _blendShapeBaseBand.style.flexDirection = FlexDirection.Row;
            _blendShapeBaseBand.style.alignItems = Align.Center;
            _blendShapeBaseBand.style.flexShrink = 0;
            _blendShapeBaseBand.style.paddingLeft = _blendShapeBaseBand.style.paddingRight = 8;
            _blendShapeBaseBand.style.paddingTop = _blendShapeBaseBand.style.paddingBottom = 6;
            _blendShapeBaseBand.style.borderBottomWidth = 1;
            _blendShapeBaseBand.style.borderBottomColor = PaneBorderColor;
            leftPane.Add(_blendShapeBaseBand);

            _blendShapeComposeScroll = new ScrollView();
            _blendShapeComposeScroll.style.flexGrow  = 1;
            _blendShapeComposeScroll.style.minHeight = 0;
            leftPane.Add(_blendShapeComposeScroll);
            body.Add(leftPane);

            // Right: shape list
            var rightPane = new VisualElement();
            rightPane.style.flexGrow  = 1;
            rightPane.style.minHeight = 0;
            rightPane.style.flexBasis = new StyleLength(0f);
            rightPane.style.flexDirection = FlexDirection.Column;

            _blendShapeShapeHeaderLabel = BuildColumnHeader(rightPane, "シェイプキー一覧");

            rightPane.Add(BuildSearchField(_composeSearchText, v =>
            {
                _composeSearchText = v;
                RefreshBlendShapeShapeList();
            }));

            _blendShapeShapeListView = new ListView();
            _blendShapeShapeListView.style.flexGrow  = 1;
            _blendShapeShapeListView.style.minHeight = 0;
            _blendShapeShapeListView.fixedItemHeight = 28;
            _blendShapeShapeListView.selectionType   = SelectionType.None;
            _blendShapeShapeListView.showBorder      = false;
            _blendShapeShapeListView.makeItem        = MakeComposeShapeRow;
            _blendShapeShapeListView.bindItem        = BindComposeShapeRow;
            rightPane.Add(_blendShapeShapeListView);

            _blendShapeShapeEmptyLabel = BuildEmptyPlaceholder();
            rightPane.Add(_blendShapeShapeEmptyLabel);

            body.Add(rightPane);
            container.Add(body);

            // Footer
            var footer = new VisualElement();
            footer.style.flexShrink = 0;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = PaneBorderColor;
            footer.style.paddingLeft = footer.style.paddingRight = 10;
            footer.style.paddingTop = footer.style.paddingBottom = 10;

            var overwriteRow = new VisualElement();
            overwriteRow.style.flexDirection = FlexDirection.Row;
            overwriteRow.style.alignItems = Align.Center;
            overwriteRow.style.minHeight = 32;
            overwriteRow.style.marginBottom = 8;

            var overwriteToggle = new Toggle("ベースを上書き");
            overwriteToggle.tooltip = "ONの場合、ベースのシェイプキー自体を合成結果で上書きします。OFFの場合は新しいシェイプキーとして追加します（右側で名前を指定）。";
            overwriteToggle.value = _overwriteShape;
            overwriteToggle.style.flexShrink = 0;
            overwriteToggle.style.fontSize = 13;
            overwriteToggle.RegisterValueChangedCallback(evt =>
            {
                _overwriteShape = evt.newValue;
                if (_overwriteShape && !string.IsNullOrEmpty(_baseShapeName))
                    _newShapeName = _baseShapeName;
                else if (!_overwriteShape)
                    _newShapeName = string.IsNullOrEmpty(_baseShapeName) ? "" : _baseShapeName + "_合成";
                RebuildComposeSubPane();
            });
            overwriteRow.Add(overwriteToggle);

            _blendShapeOverwriteTarget = new Label();
            _blendShapeOverwriteTarget.style.fontSize = 13;
            _blendShapeOverwriteTarget.style.color = DimColor;
            _blendShapeOverwriteTarget.style.marginLeft = 8;
            _blendShapeOverwriteTarget.style.flexGrow = 1;
            overwriteRow.Add(_blendShapeOverwriteTarget);

            _blendShapeNewNameField = new TextField();
            _blendShapeNewNameField.style.fontSize = 13;
            _blendShapeNewNameField.style.marginLeft = 8;
            FixFieldOverflow(_blendShapeNewNameField);
            _blendShapeNewNameField.RegisterValueChangedCallback(evt =>
            {
                _newShapeName = evt.newValue;
                UpdateBlendShapeExecuteButton();
            });
            overwriteRow.Add(_blendShapeNewNameField);
            footer.Add(overwriteRow);

            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.alignItems = Align.Center;

            var clearBtn = new Button(() =>
            {
                ResetComposeData();
                RebuildComposeSubPane();
            });
            clearBtn.text = "全クリア";
            clearBtn.style.width = 70;
            clearBtn.style.height = 36;
            clearBtn.style.fontSize = 12;
            actionRow.Add(clearBtn);

            _blendShapeExecuteWarning = new Label();
            _blendShapeExecuteWarning.style.flexGrow = 1;
            _blendShapeExecuteWarning.style.fontSize = 11;
            _blendShapeExecuteWarning.style.color = new Color(0.85f, 0.60f, 0.15f);
            _blendShapeExecuteWarning.style.unityTextAlign = TextAnchor.MiddleCenter;
            _blendShapeExecuteWarning.style.marginRight = 94; // 実行ボタン幅(80) + 右余白(10) + 間隔(4)
            actionRow.Add(_blendShapeExecuteWarning);

            footer.Add(actionRow);
            container.Add(footer);

            _blendShapeExecuteButton = new Button(() =>
            {
                ExecuteShapeCompose();
                RebuildComposeSubPane();
            });
            _blendShapeExecuteButton.text = "合成実行";
            _blendShapeExecuteButton.style.position = Position.Absolute;
            _blendShapeExecuteButton.style.right = 10;
            _blendShapeExecuteButton.style.bottom = 10;
            _blendShapeExecuteButton.style.width = 80;
            _blendShapeExecuteButton.style.height = 36;
            _blendShapeExecuteButton.style.fontSize = 12;
            container.Add(_blendShapeExecuteButton);

            RebuildComposeSubPane();
            return container;
        }

        private void RebuildComposeSubPane()
        {
            if (_blendShapeComposeScroll == null) return;

            _blendShapeTargetField?.SetValueWithoutNotify(_composeTarget);

            // Base band
            _blendShapeBaseBand.Clear();

            if (string.IsNullOrEmpty(_baseShapeName))
            {
                string baseBandHint = _shapeNames.Count == 0
                    ? "まず対象メッシュを選択してください"
                    : "右の一覧でベースを選択してください";
                var helpLbl = new Label(baseBandHint);
                helpLbl.style.fontSize = 11;
                helpLbl.style.color = DimColor;
                helpLbl.style.unityFontStyleAndWeight = FontStyle.Italic;
                _blendShapeBaseBand.Add(helpLbl);
            }
            else
            {
                var dot = new Label("●");
                dot.style.fontSize = 8;
                dot.style.color = BaseDotColor;
                dot.style.marginRight = 4;
                dot.style.flexShrink = 0;
                _blendShapeBaseBand.Add(dot);

                var basePfx = new Label("base");
                basePfx.style.fontSize = 10;
                basePfx.style.color = DimColor;
                basePfx.style.marginRight = 6;
                basePfx.style.flexShrink = 0;
                _blendShapeBaseBand.Add(basePfx);

                var baseName = new Label(_baseShapeName);
                baseName.style.fontSize = 12;
                baseName.style.unityFontStyleAndWeight = FontStyle.Bold;
                baseName.style.color = TextColor;
                baseName.style.flexGrow = 1;
                baseName.style.overflow = Overflow.Hidden;
                baseName.style.whiteSpace = WhiteSpace.NoWrap;
                _blendShapeBaseBand.Add(baseName);
            }

            // Compose scroll
            var composeScrollPos = _blendShapeComposeScroll.scrollOffset;
            _blendShapeComposeScroll.Clear();
            bool hasBase = !string.IsNullOrEmpty(_baseShapeName);
            for (int i = 0; i < _composeShapes.Count; i++)
            {
                if (i > 0 || hasBase)
                {
                    var plus = new Label("＋");
                    plus.style.fontSize = 12;
                    plus.style.color = TextColor;
                    plus.style.unityFontStyleAndWeight = FontStyle.Bold;
                    plus.style.unityTextAlign = TextAnchor.MiddleCenter;
                    plus.style.paddingTop = plus.style.paddingBottom = 2;
                    _blendShapeComposeScroll.Add(plus);
                }
                _blendShapeComposeScroll.Add(BuildBlendShapeComposeRow(i));
            }

            if (_composeShapes.Count == 0)
            {
                string composeHint = _shapeNames.Count == 0
                    ? "メッシュを選択するとシェイプキーが表示されます"
                    : "右の一覧から「＋追加」を押してください";
                var hint = new Label(composeHint);
                hint.style.fontSize = 11;
                hint.style.color = DimColor;
                hint.style.unityFontStyleAndWeight = FontStyle.Italic;
                hint.style.marginTop = 8;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                hint.style.whiteSpace = WhiteSpace.Normal;
                _blendShapeComposeScroll.Add(hint);
            }

            _blendShapeComposeScroll.schedule.Execute(
                () => _blendShapeComposeScroll.scrollOffset = composeScrollPos).ExecuteLater(0);

            // Shape scroll
            RefreshBlendShapeShapeList();

            // Footer state
            if (_blendShapeOverwriteTarget != null)
            {
                _blendShapeOverwriteTarget.text = string.IsNullOrEmpty(_baseShapeName) ? "（ベース未選択）" : _baseShapeName;
                _blendShapeOverwriteTarget.style.display = _overwriteShape ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_blendShapeNewNameField != null)
            {
                if (_blendShapeNewNameField.value != _newShapeName)
                    _blendShapeNewNameField.SetValueWithoutNotify(_newShapeName);
                _blendShapeNewNameField.style.display = _overwriteShape ? DisplayStyle.None : DisplayStyle.Flex;
            }

            UpdateBlendShapeExecuteButton();
        }

        private VisualElement BuildBlendShapeComposeRow(int index)
        {
            var item = _composeShapes[index];
            int capturedIndex = index;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = row.style.paddingRight = 8;
            row.style.paddingTop = row.style.paddingBottom = 5;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = PaneBorderColor;

            var removeBtn = new Button(() =>
            {
                _composeShapes.RemoveAt(capturedIndex);
                RebuildComposeSubPane();
            });
            removeBtn.text = "×";
            removeBtn.style.width = 20;
            removeBtn.style.height = 20;
            removeBtn.style.fontSize = 11;
            removeBtn.style.paddingLeft = removeBtn.style.paddingRight = 2;
            removeBtn.style.paddingTop = removeBtn.style.paddingBottom = 1;
            removeBtn.style.marginRight = 6;
            removeBtn.style.flexShrink = 0;
            row.Add(removeBtn);

            var nameLbl = new Label(item.name);
            nameLbl.style.fontSize = 11;
            nameLbl.style.color = TextColor;
            nameLbl.style.flexGrow = 1;
            nameLbl.style.flexShrink = 1;
            nameLbl.style.minWidth = new StyleLength(0f);
            nameLbl.style.overflow = Overflow.Hidden;
            nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(nameLbl);

            var slider = new Slider(-100f, 100f);
            slider.value = item.weight;
            slider.style.flexShrink = 0;
            slider.style.width = 90;
            slider.style.minWidth = new StyleLength(0f);
            slider.style.marginLeft = 4;
            slider.style.marginRight = 4;

            var valueField = new IntegerField();
            valueField.value = Mathf.RoundToInt(item.weight);
            valueField.style.flexShrink = 0;
            valueField.style.width = 40;
            valueField.style.minWidth = new StyleLength(0f);
            valueField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();

            slider.RegisterValueChangedCallback(evt =>
            {
                int rounded = Mathf.RoundToInt(evt.newValue);
                _composeShapes[capturedIndex] = (item.name, rounded);
                valueField.SetValueWithoutNotify(rounded);
                slider.SetValueWithoutNotify(rounded);
            });
            valueField.RegisterValueChangedCallback(evt =>
            {
                int clamped = Mathf.Clamp(evt.newValue, -100, 100);
                _composeShapes[capturedIndex] = (item.name, clamped);
                slider.SetValueWithoutNotify(clamped);
                if (evt.newValue != clamped) valueField.SetValueWithoutNotify(clamped);
            });

            row.Add(slider);
            row.Add(valueField);
            return row;
        }

        private void RefreshBlendShapeShapeList()
        {
            if (_blendShapeShapeListView == null) return;

            bool hasSearch = !string.IsNullOrEmpty(_composeSearchText);
            _blendShapeShapeFiltered.Clear();
            foreach (var shapeName in _shapeNames)
            {
                if (hasSearch && !shapeName.Contains(_composeSearchText, StringComparison.OrdinalIgnoreCase))
                    continue;
                _blendShapeShapeFiltered.Add(shapeName);
            }

            _blendShapeShapeListView.itemsSource = _blendShapeShapeFiltered;
            _blendShapeShapeListView.Rebuild();

            SetHeaderCount(_blendShapeShapeHeaderLabel, "シェイプキー一覧", _blendShapeShapeFiltered.Count);
            UpdateEmptyPlaceholder(_blendShapeShapeListView, _blendShapeShapeEmptyLabel, _blendShapeShapeFiltered.Count, _composeSearchText);
        }

        private class ComposeShapeRowView
        {
            public Label Dot;
            public Label NameLabel;
            public Button BaseBtn;
            public Button AddBtn;
            public string ShapeName;
        }

        private VisualElement MakeComposeShapeRow()
        {
            var row = BuildListRow(out var dot, out var nameLabel);
            var view = new ComposeShapeRowView { Dot = dot, NameLabel = nameLabel };

            view.BaseBtn = new Button(() =>
            {
                bool isBase = view.ShapeName == _baseShapeName;
                _baseShapeName = isBase ? "" : view.ShapeName;
                if (_overwriteShape) _newShapeName = _baseShapeName;
                RebuildComposeSubPane();
            });
            view.BaseBtn.style.fontSize    = 10;
            view.BaseBtn.style.height      = 20;
            view.BaseBtn.style.paddingLeft = view.BaseBtn.style.paddingRight = 5;
            view.BaseBtn.style.paddingTop  = view.BaseBtn.style.paddingBottom = 1;
            view.BaseBtn.style.flexShrink  = 0;
            row.Add(view.BaseBtn);

            view.AddBtn = new Button(() =>
            {
                _composeShapes.Add((view.ShapeName, 0f));
                RebuildComposeSubPane();
            });
            view.AddBtn.text = "＋追加";
            view.AddBtn.style.fontSize    = 10;
            view.AddBtn.style.height      = 20;
            view.AddBtn.style.paddingLeft = view.AddBtn.style.paddingRight = 5;
            view.AddBtn.style.paddingTop  = view.AddBtn.style.paddingBottom = 1;
            view.AddBtn.style.marginLeft  = 4;
            view.AddBtn.style.flexShrink  = 0;
            row.Add(view.AddBtn);

            row.userData = view;
            return row;
        }

        private void BindComposeShapeRow(VisualElement row, int index)
        {
            var view = (ComposeShapeRowView)row.userData;
            view.ShapeName = _blendShapeShapeFiltered[index];

            bool isBase  = view.ShapeName == _baseShapeName;
            bool isAdded = _composeShapes.Any(s => s.name == view.ShapeName);

            view.Dot.style.color = isBase  ? BaseDotColor
                                 : isAdded ? AccentColor
                                 : InactiveDotColor;
            view.NameLabel.text = view.ShapeName;

            view.BaseBtn.text = isBase ? "✓ベース" : "ベース";
            SetConfirmHighlight(view.BaseBtn, isBase);
        }

        private void UpdateBlendShapeExecuteButton()
        {
            if (_blendShapeExecuteButton == null) return;
            bool canCompose = CanExecuteCompose();
            _blendShapeExecuteButton.SetEnabled(canCompose);
            if (_blendShapeExecuteWarning == null) return;
            if (canCompose) { _blendShapeExecuteWarning.style.display = DisplayStyle.None; return; }
            _blendShapeExecuteWarning.text = _composeTarget?.sharedMesh == null ? "対象メッシュを選択してください。"
                : string.IsNullOrEmpty(_baseShapeName) ? "ベースシェイプキーを選択してください。"
                : "出力名を入力してください。";
            _blendShapeExecuteWarning.style.display = DisplayStyle.Flex;
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
            var mesh = UnityEngine.Object.Instantiate(originalMesh);
            mesh.name = $"{originalMesh.name}_Composed";

            int baseIndex = originalMesh.GetBlendShapeIndex(_baseShapeName);
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

        // 絶対座標ではなく差分（デルタ）だけを積算する。originalMesh.vertices/normals/tangents は
        // 足しても最後に引くだけで結果に影響しないため使わない（BlendShapeEditor等と同じ方式）。
        // メッシュによってはこのプロパティが実際の頂点数と食い違う配列を返すことがあり、
        // 不要なのに呼ぶとGetBlendShapeFrameVertices側でサイズ不一致エラーの原因になる。
        private (Vector3[] vertices, Vector3[] normals, Vector3[] tangents) ComputeComposedDeltas(Mesh originalMesh, int baseIndex)
        {
            int vertexCount = originalMesh.vertexCount;
            var composedVertices = new Vector3[vertexCount];
            var composedNormals = new Vector3[vertexCount];
            var composedTangents = new Vector3[vertexCount];

            ApplyBaseShapeDeltas(originalMesh, baseIndex, composedVertices, composedNormals, composedTangents);
            ApplyComposeShapeDeltas(originalMesh, composedVertices, composedNormals, composedTangents);

            return (composedVertices, composedNormals, composedTangents);
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

                int index = mesh.GetBlendShapeIndex(name);
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
                   _composeTarget.sharedMesh == null ||
                   _composeTarget.sharedMesh.blendShapeCount == 0;
        }

        private SkinnedMeshRenderer FindFirstValidSkinnedMeshRenderer()
        {
            foreach (var gameObject in _targets.Where(IsValidTarget))
            {
                var smr = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(_scanIncludeInactive)
                    .FirstOrDefault(s => s.sharedMesh?.blendShapeCount > 0);
                if (smr != null) return smr;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        // まばたき修正（Blink Correction）

        private VisualElement BuildBlinkCorrectionSubPane()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.flexGrow      = 1;
            container.style.minHeight     = 0;

            if (_composeTarget?.sharedMesh == null)
            {
                var help = new HelpBox("対象メッシュを選択してください。", HelpBoxMessageType.Info);
                help.style.marginLeft = help.style.marginRight = help.style.marginTop = help.style.marginBottom = 10;
                container.Add(help);
                return container;
            }

            var presetRow = new VisualElement();
            presetRow.style.flexDirection     = FlexDirection.Row;
            presetRow.style.alignItems        = Align.Center;
            presetRow.style.flexShrink        = 0;
            presetRow.style.paddingLeft       = presetRow.style.paddingRight  = 8;
            presetRow.style.paddingTop        = presetRow.style.paddingBottom = 6;
            presetRow.style.borderBottomWidth = 1;
            presetRow.style.borderBottomColor = PaneBorderColor;

            var presetLbl = new Label("Preset");
            presetLbl.style.fontSize   = 11;
            presetLbl.style.color      = DimColor;
            presetLbl.style.marginRight = 8;
            presetLbl.style.flexShrink = 0;
            presetRow.Add(presetLbl);

            LoadBuiltinBlinkPresets();

            var presetLabels = new List<string> { "(プリセットを選択)" };
            presetLabels.AddRange(_builtinBlinkPresets.Select(p => p.name));

            var presetDropdown = new DropdownField(presetLabels, Mathf.Clamp(_blinkPresetIndex + 1, 0, presetLabels.Count - 1));
            FixFieldOverflow(presetDropdown);
            presetDropdown.RegisterValueChangedCallback(evt =>
            {
                int newIndex = presetLabels.IndexOf(evt.newValue);
                Undo.RecordObject(this, "Select Blink Preset");
                _blinkPresetIndex = newIndex - 1;
                if (newIndex > 0)
                    ApplyBlinkPreset(_builtinBlinkPresets[newIndex - 1]);
                EditorUtility.SetDirty(this);
                // プリセットで改変済み目元シェイプキー・まばたきシェイプキー双方の選択状態が変わるため両方の一覧を更新する。
                // ただしListViewごとの作り直しは避け（数百件規模での再生成コスト・スクロール位置ロストを避けるため）中身だけを差し替える。
                RefreshBlinkTargetList();
                DetectBlinkSourceCandidates(); // 内部でRefreshBlinkSourceList()も呼ぶ
                UpdateBlinkApplyButton();
            });
            presetRow.Add(presetDropdown);
            container.Add(presetRow);

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow      = 1;
            columns.style.minHeight     = 0;

            // Left: 改変済み目元シェイプキー
            var leftPane = new VisualElement();
            leftPane.style.flexGrow         = 1;
            leftPane.style.flexBasis        = new StyleLength(0f);
            leftPane.style.minHeight        = 0;
            leftPane.style.flexDirection    = FlexDirection.Column;
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = PaneBorderColor;

            _blinkSourceHeaderLabel = BuildColumnHeader(leftPane, "改変済み目元シェイプキー");

            // 目元シェイプキー候補の検出はまばたきシェイプキーの選択が変わるたびに自動で走る（DetectBlinkSourceCandidates呼び出し箇所を参照）。
            // このトグルは検出結果（_blinkSourceCandidates）で一覧を絞り込んで表示するだけの表示フィルタ。
            var candidateFilterToggle = new Toggle("候補のみ表示");
            candidateFilterToggle.tooltip = "自動検出された目元シェイプキー候補だけに絞り込んで表示します";
            candidateFilterToggle.value = _blinkSourceCandidateFilterOnly;
            candidateFilterToggle.style.marginLeft = candidateFilterToggle.style.marginRight = 8;
            candidateFilterToggle.style.marginTop  = 4;
            candidateFilterToggle.style.fontSize   = 11;
            candidateFilterToggle.RegisterValueChangedCallback(evt =>
            {
                _blinkSourceCandidateFilterOnly = evt.newValue;
                RefreshBlinkSourceList();
            });
            leftPane.Add(candidateFilterToggle);

            leftPane.Add(BuildSearchField(_blinkSourceSearch, v =>
            {
                _blinkSourceSearch = v;
                RefreshBlinkSourceList();
            }));

            _blinkSourceListView = new ListView();
            _blinkSourceListView.style.flexGrow  = 1;
            _blinkSourceListView.style.minHeight = 0;
            _blinkSourceListView.fixedItemHeight = 28;
            _blinkSourceListView.selectionType   = SelectionType.None;
            _blinkSourceListView.showBorder      = false;
            _blinkSourceListView.makeItem        = MakeBlinkSourceRow;
            _blinkSourceListView.bindItem        = BindBlinkSourceRow;
            leftPane.Add(_blinkSourceListView);

            _blinkSourceEmptyLabel = BuildEmptyPlaceholder();
            leftPane.Add(_blinkSourceEmptyLabel);

            columns.Add(leftPane);

            // Right: まばたきシェイプキー
            var rightPane = new VisualElement();
            rightPane.style.flexGrow      = 1;
            rightPane.style.flexBasis     = new StyleLength(0f);
            rightPane.style.minHeight     = 0;
            rightPane.style.flexDirection = FlexDirection.Column;

            _blinkTargetHeaderLabel = BuildColumnHeader(rightPane, "まばたきシェイプキー");

            rightPane.Add(BuildSearchField(_blinkTargetSearch, v =>
            {
                _blinkTargetSearch = v;
                RefreshBlinkTargetList();
            }));

            _blinkTargetListView = new ListView();
            _blinkTargetListView.style.flexGrow  = 1;
            _blinkTargetListView.style.minHeight = 0;
            _blinkTargetListView.fixedItemHeight = 28;
            _blinkTargetListView.selectionType   = SelectionType.None;
            _blinkTargetListView.showBorder      = false;
            _blinkTargetListView.makeItem        = MakeBlinkTargetRow;
            _blinkTargetListView.bindItem        = BindBlinkTargetRow;
            rightPane.Add(_blinkTargetListView);

            _blinkTargetEmptyLabel = BuildEmptyPlaceholder();
            rightPane.Add(_blinkTargetEmptyLabel);

            columns.Add(rightPane);

            container.Add(columns);

            var footer = new VisualElement();
            footer.style.flexShrink        = 0;
            footer.style.flexDirection     = FlexDirection.Row;
            footer.style.alignItems        = Align.Center;
            footer.style.borderTopWidth    = 1;
            footer.style.borderTopColor    = PaneBorderColor;
            footer.style.paddingLeft       = footer.style.paddingRight  = 10;
            footer.style.paddingTop        = footer.style.paddingBottom = 10;

            var clearAllBtn = new Button(() =>
            {
                Undo.RecordObject(this, "Clear Blink Correction Selection");
                _blinkSourceNames.Clear();
                _blinkTargets.Clear();
                EditorUtility.SetDirty(this);
                RefreshBlinkTargetList();
                DetectBlinkSourceCandidates(); // ターゲットが空になるので候補もクリアされる（内部でRefreshBlinkSourceList()も呼ぶ）
                UpdateBlinkApplyButton();
            });
            clearAllBtn.text = "全解除";
            clearAllBtn.style.width       = 70;
            clearAllBtn.style.height      = 32;
            clearAllBtn.style.fontSize    = 12;
            clearAllBtn.style.marginRight = 8;
            footer.Add(clearAllBtn);

            _blinkRestoreOriginalButton = new Button(RestoreOriginalBlinkMesh) { text = "元メッシュに戻す" };
            _blinkRestoreOriginalButton.tooltip      = "まばたき修正を適用する前のメッシュに戻します";
            _blinkRestoreOriginalButton.style.width       = 100;
            _blinkRestoreOriginalButton.style.height      = 32;
            _blinkRestoreOriginalButton.style.fontSize    = 12;
            _blinkRestoreOriginalButton.style.marginRight = 8;
            footer.Add(_blinkRestoreOriginalButton);

            _blinkApplyButton = new Button(ApplyBlinkCorrection) { text = "補正を適用" };
            _blinkApplyButton.style.flexGrow = 1;
            _blinkApplyButton.style.height   = 32;
            _blinkApplyButton.style.fontSize = 13;
            footer.Add(_blinkApplyButton);
            container.Add(footer);

            RefreshBlinkTargetList();
            DetectBlinkSourceCandidates(); // 内部でRefreshBlinkSourceList()も呼ぶ
            UpdateBlinkApplyButton();
            UpdateRestoreOriginalButton();

            return container;
        }

        private void RebuildBlinkCorrectionSubPane()
        {
            if (_shapeKeySubPaneSlot == null || _shapeKeySubMode != ShapeKeySubMode.BlinkCorrection) return;
            _shapeKeySubPaneSlot.Clear();
            _shapeKeySubPaneSlot.Add(BuildBlinkCorrectionSubPane());
        }

        private void RefreshBlinkSourceList()
        {
            if (_blinkSourceListView == null) return;

            _blinkSourceFiltered.Clear();
            foreach (var shapeName in _shapeNames)
            {
                if (!BlinkMatchesSearch(shapeName, _blinkSourceSearch)) continue;
                // 「候補のみ表示」は検出候補に加えて、既に使用チェック済みのものも表示対象にする。
                // DetectBlinkSourceCandidatesは既にチェック済みの名前を候補として拾い直さない（再提案する意味がないため）
                // ので、ここで候補集合だけを条件にすると、ターゲット変更などで再検出が走るたびに
                // 使用中のシェイプキーが一覧から消えてしまう。
                if (_blinkSourceCandidateFilterOnly
                    && !_blinkSourceCandidates.Contains(shapeName)
                    && !_blinkSourceNames.Contains(shapeName))
                    continue;
                _blinkSourceFiltered.Add(shapeName);
            }

            _blinkSourceListView.itemsSource = _blinkSourceFiltered;
            _blinkSourceListView.Rebuild();

            SetHeaderCount(_blinkSourceHeaderLabel, "改変済み目元シェイプキー", _blinkSourceNames.Count);
            UpdateEmptyPlaceholder(_blinkSourceListView, _blinkSourceEmptyLabel, _blinkSourceFiltered.Count, _blinkSourceSearch);
            if (_blinkSourceFiltered.Count == 0 && _blinkSourceCandidateFilterOnly && _blinkSourceEmptyLabel != null)
                _blinkSourceEmptyLabel.text = "候補が見つかりませんでした。先にまばたきシェイプキーを選択してください。";
        }

        private VisualElement MakeBlinkSourceRow()
        {
            var row = BuildListRow(out var dot, out var nameLabel);
            var view = new BlinkSourceRowView { Dot = dot, NameLabel = nameLabel };

            view.CandidateBadge = new Label("候補");
            view.CandidateBadge.style.fontSize    = 9;
            view.CandidateBadge.style.color       = CandidateHighlightColor;
            view.CandidateBadge.style.borderTopWidth = view.CandidateBadge.style.borderBottomWidth
                = view.CandidateBadge.style.borderLeftWidth = view.CandidateBadge.style.borderRightWidth = 1;
            view.CandidateBadge.style.borderTopColor = view.CandidateBadge.style.borderBottomColor
                = view.CandidateBadge.style.borderLeftColor = view.CandidateBadge.style.borderRightColor = CandidateHighlightColor;
            view.CandidateBadge.style.paddingLeft = view.CandidateBadge.style.paddingRight = 3;
            view.CandidateBadge.style.marginRight = 4;
            view.CandidateBadge.style.flexShrink  = 0;
            view.CandidateBadge.style.display     = DisplayStyle.None;
            row.Add(view.CandidateBadge);

            view.ToggleBtn = new Button(() =>
            {
                var shapeName = view.ShapeName;
                Undo.RecordObject(this, "Toggle Blink Correction Source");
                if (_blinkSourceNames.Contains(shapeName)) _blinkSourceNames.Remove(shapeName);
                else                                       _blinkSourceNames.Add(shapeName);
                EditorUtility.SetDirty(this);
                RefreshBlinkSourceRowVisual(view);
                SetHeaderCount(_blinkSourceHeaderLabel, "改変済み目元シェイプキー", _blinkSourceNames.Count);
                UpdateBlinkApplyButton();
            });
            view.ToggleBtn.style.fontSize    = 10;
            view.ToggleBtn.style.height      = 20;
            view.ToggleBtn.style.paddingLeft = view.ToggleBtn.style.paddingRight  = 5;
            view.ToggleBtn.style.paddingTop  = view.ToggleBtn.style.paddingBottom = 1;
            view.ToggleBtn.style.flexShrink  = 0;
            row.Add(view.ToggleBtn);

            row.userData = view;
            return row;
        }

        private void BindBlinkSourceRow(VisualElement row, int index)
        {
            var view = (BlinkSourceRowView)row.userData;
            view.ShapeName = _blinkSourceFiltered[index];
            RefreshBlinkSourceRowVisual(view);
        }

        private void RefreshBlinkSourceRowVisual(BlinkSourceRowView view)
        {
            bool isEnabled = _blinkSourceNames.Contains(view.ShapeName);

            view.Dot.style.color = isEnabled ? AccentColor : InactiveDotColor;
            view.NameLabel.text  = view.ShapeName;

            // 候補バッジは使用チェックの有無にかかわらず、検出候補である間は表示し続ける。
            view.CandidateBadge.style.display =
                _blinkSourceCandidates.Contains(view.ShapeName) ? DisplayStyle.Flex : DisplayStyle.None;

            view.ToggleBtn.text = isEnabled ? "✓使用" : "使用";
            SetConfirmHighlight(view.ToggleBtn, isEnabled);
        }

        private void RefreshBlinkTargetList()
        {
            if (_blinkTargetListView == null) return;

            _blinkTargetFiltered.Clear();
            foreach (var shapeName in _shapeNames)
                if (BlinkMatchesSearch(shapeName, _blinkTargetSearch))
                    _blinkTargetFiltered.Add(shapeName);

            _blinkTargetListView.itemsSource = _blinkTargetFiltered;
            _blinkTargetListView.Rebuild();

            SetHeaderCount(_blinkTargetHeaderLabel, "まばたきシェイプキー", _blinkTargets.Count(t => t.enabled));
            UpdateEmptyPlaceholder(_blinkTargetListView, _blinkTargetEmptyLabel, _blinkTargetFiltered.Count, _blinkTargetSearch);
        }

        private VisualElement MakeBlinkTargetRow()
        {
            var row = BuildListRow(out var dot, out var nameLabel);
            var view = new BlinkTargetRowView { Dot = dot, NameLabel = nameLabel };

            // L/R/両は手動選択せず、補正適用時（ApplyBlinkCorrection）にDetectTargetSideでメッシュから自動判定する。
            // ここは判定結果を表示するだけの読み取り専用バッジ。
            view.SideBadge = new Label();
            view.SideBadge.style.fontSize    = 10;
            view.SideBadge.style.color       = DimColor;
            view.SideBadge.style.width       = 26;
            view.SideBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            view.SideBadge.style.marginRight = 4;
            view.SideBadge.style.flexShrink  = 0;
            row.Add(view.SideBadge);

            view.ToggleBtn = new Button(() =>
            {
                var info = FindBlinkTargetInfo(view.ShapeName);
                bool wasEnabled = info?.enabled ?? false;
                Undo.RecordObject(this, "Toggle Blink Correction Target");
                SetBlinkTargetInfo(view.ShapeName, !wasEnabled);
                EditorUtility.SetDirty(this);
                RefreshBlinkTargetRowVisual(view);
                SetHeaderCount(_blinkTargetHeaderLabel, "まばたきシェイプキー", _blinkTargets.Count(t => t.enabled));
                DetectBlinkSourceCandidates(); // ターゲット選択が変わるたびに候補を自動で再計算する
                UpdateBlinkApplyButton();
            });
            view.ToggleBtn.style.fontSize    = 10;
            view.ToggleBtn.style.height      = 20;
            view.ToggleBtn.style.paddingLeft = view.ToggleBtn.style.paddingRight  = 5;
            view.ToggleBtn.style.paddingTop  = view.ToggleBtn.style.paddingBottom = 1;
            view.ToggleBtn.style.flexShrink  = 0;
            row.Add(view.ToggleBtn);

            row.userData = view;
            return row;
        }

        private void BindBlinkTargetRow(VisualElement row, int index)
        {
            var view = (BlinkTargetRowView)row.userData;
            view.ShapeName = _blinkTargetFiltered[index];
            RefreshBlinkTargetRowVisual(view);
        }

        private void RefreshBlinkTargetRowVisual(BlinkTargetRowView view)
        {
            var info       = FindBlinkTargetInfo(view.ShapeName);
            bool isEnabled = info?.enabled ?? false;

            view.Dot.style.color = isEnabled ? AccentColor : InactiveDotColor;
            view.NameLabel.text  = view.ShapeName;

            var mesh = _composeTarget?.sharedMesh;
            if (isEnabled && mesh != null)
            {
                view.SideBadge.style.display = DisplayStyle.Flex;
                view.SideBadge.text = BlinkSideShortLabel(DetectTargetSide(mesh, view.ShapeName));
            }
            else
            {
                view.SideBadge.style.display = DisplayStyle.None;
            }

            view.ToggleBtn.text = isEnabled ? "✓対象" : "対象";
            SetConfirmHighlight(view.ToggleBtn, isEnabled);
        }

        private static string BlinkSideShortLabel(BlinkShapeSide side) =>
            side == BlinkShapeSide.Left ? "L" : side == BlinkShapeSide.Right ? "R" : "両";

        private static bool BlinkMatchesSearch(string name, string search) =>
            string.IsNullOrEmpty(search) || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void UpdateBlinkApplyButton()
        {
            if (_blinkApplyButton == null) return;
            bool canApply = _blinkSourceNames.Count > 0 && _blinkTargets.Any(t => t.enabled);
            _blinkApplyButton.SetEnabled(canApply);
        }

        // 「元メッシュに戻す」は、現在のメッシュがまばたき修正の出力パスそのもの（＝適用直後の状態）でなければ
        // そもそも押せないようにする（それ以外の状態では戻すべき対象が無い、または既に元メッシュのため）。
        private void UpdateRestoreOriginalButton()
        {
            if (_blinkRestoreOriginalButton == null) return;
            var renderer = _composeTarget;
            var mesh = renderer != null ? renderer.sharedMesh : null;
            bool canRestore = renderer != null && mesh != null
                && AssetDatabase.GetAssetPath(mesh) == GetBlinkFixOutputPath(renderer);
            _blinkRestoreOriginalButton.SetEnabled(canRestore);
        }

        // Editor/BuiltinPresets/配下のBlinkPresetAssetを毎回スキャンし直す（保存/削除後も追従させるため。ShadowSyncのLoadBuiltinPresetsと同じ方式）。
        private void LoadBuiltinBlinkPresets()
        {
            _builtinBlinkPresets.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:BlinkPresetAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<BlinkPresetAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) _builtinBlinkPresets.Add(asset);
            }
        }

        // プリセットは選択時点のチェック状態（改変済み目元シェイプキー・まばたきシェイプキー双方）を完全に置き換える（既存の手動チェックは上書きされる）
        private void ApplyBlinkPreset(BlinkPresetAsset preset)
        {
            _blinkSourceNames.Clear();
            _blinkSourceNames.AddRange(preset.sourceShapeKeyNames);

            _blinkTargets.Clear();
            foreach (var t in preset.targets)
                _blinkTargets.Add(new BlinkTargetInfo { shapeKeyName = t.shapeKeyName, enabled = true });
        }

        private BlinkTargetInfo FindBlinkTargetInfo(string name) =>
            _blinkTargets.FirstOrDefault(t => t.shapeKeyName == name);

        private void SetBlinkTargetInfo(string name, bool enabled)
        {
            var info = FindBlinkTargetInfo(name);
            if (info == null)
            {
                info = new BlinkTargetInfo { shapeKeyName = name };
                _blinkTargets.Add(info);
            }
            info.enabled = enabled;
        }

        // まばたき修正の焼き込み先パス（レンダラーのGameObject名から決定的に生成、固定）。
        // 二重適用防止は「現在のメッシュがこのパスかどうか」だけで判定する（ApplyBlinkCorrection参照）。
        private string GetBlinkFixOutputPath(SkinnedMeshRenderer renderer) =>
            $"{BLINK_FIX_OUTPUT_FOLDER}/{SanitizeAssetFileName(renderer.gameObject.name)}_BlinkFixed.asset";

        // 「元メッシュに戻す」専用のログファイルパス。ApplyBlinkCorrection自体はこれを一切参照しない。
        private string GetBlinkFixLogPath(SkinnedMeshRenderer renderer) =>
            $"{BLINK_FIX_OUTPUT_FOLDER}/{SanitizeAssetFileName(renderer.gameObject.name)}_OriginalMeshPath.txt";

        // ログに「適用直前のメッシュ」のパス+名前を記録する（Avatar Blink Fixの元メッシュログと同じ2行形式）。
        private static void WriteBlinkFixLog(string logPath, Mesh originalMesh)
        {
            string path = AssetDatabase.GetAssetPath(originalMesh);
            File.WriteAllText(logPath, path + "\n" + originalMesh.name + "\n");
        }

        // ログから元メッシュを読み込む。ファイルが無い/読み込めない場合はnullを返すだけで例外は投げない。
        private static Mesh ReadBlinkFixLogMesh(string logPath)
        {
            if (!File.Exists(logPath)) return null;
            string[] lines = File.ReadAllLines(logPath);
            if (lines.Length < 2 || string.IsNullOrEmpty(lines[0])) return null;

            var meshes = AssetDatabase.LoadAllAssetsAtPath(lines[0]).OfType<Mesh>();
            return meshes.FirstOrDefault(m => m.name == lines[1]) ?? meshes.FirstOrDefault();
        }

        // 固定パスへの上書き保存。既存アセットがあれば削除してから作り直す。
        private static void SaveOrReplaceMeshAsset(Mesh mesh, string path)
        {
            EnsureAssetFolder(BLINK_FIX_OUTPUT_FOLDER);
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // 有効な改変済み目元シェイプキー全ての「現在のウェイト」を使って、まばたきシェイプキーに焼き込む
        // 補正ベクトル場を1回だけ計算する（ターゲットに依存しない共通の場。Avatar Blink FixのflatV/flatN/flatTに相当）。
        // _blinkScratchVertices等は他の場所（DetectTargetSide等）でも使い回される単一バッファのため、
        // ここでの積算に使い回すと直後のGetBlendShapeFrameVerticesで上書きされてしまう。
        // そのため専用のアキュムレータ配列を別途確保する。
        private static (Vector3[] v, Vector3[] n, Vector3[] t) ComputeBlinkCorrectionField(Mesh mesh, Dictionary<string, int> sourceIndices, SkinnedMeshRenderer renderer)
        {
            int vertexCount = mesh.vertexCount;
            var accV = new Vector3[vertexCount];
            var accN = new Vector3[vertexCount];
            var accT = new Vector3[vertexCount];

            var readV = new Vector3[vertexCount];
            var readN = new Vector3[vertexCount];
            var readT = new Vector3[vertexCount];

            foreach (var kv in sourceIndices)
            {
                int frameCount = mesh.GetBlendShapeFrameCount(kv.Value);
                if (frameCount == 0) continue;
                mesh.GetBlendShapeFrameVertices(kv.Value, frameCount - 1, readV, readN, readT);

                float scale = -(renderer.GetBlendShapeWeight(kv.Value) / 100f);
                for (int i = 0; i < vertexCount; i++)
                {
                    accV[i] += readV[i] * scale;
                    accN[i] += readN[i] * scale;
                    accT[i] += readT[i] * scale;
                }
            }

            return (accV, accN, accT);
        }

        // 複数ターゲットを一括で置き換えるメッシュ作成（単一ターゲット版CreateMeshWithReplacedShapeの複数版。
        // シェイプキー合成機能は単一ターゲットのみなので既存のCreateMeshWithReplacedShapeは変更しない）。
        private static void CreateMeshWithReplacedShapes(Mesh mesh, Mesh originalMesh, Dictionary<string, (Vector3[] vertices, Vector3[] normals, Vector3[] tangents)> targetDeltas)
        {
            mesh.ClearBlendShapes();
            for (int i = 0; i < originalMesh.blendShapeCount; i++)
            {
                string shapeName = originalMesh.GetBlendShapeName(i);
                if (targetDeltas.TryGetValue(shapeName, out var deltas))
                    mesh.AddBlendShapeFrame(shapeName, 100f, deltas.vertices, deltas.normals, deltas.tangents);
                else
                    CopyExistingBlendShape(originalMesh, mesh, i, shapeName);
            }
        }

        // まばたき修正の「補正を適用」。まばたきシェイプキー自身のBlendShapeフレームに補正ベクトルを焼き込み、
        // 新しいメッシュアセットとしてSkinnedMeshRendererに差し替える（Avatar Blink Fixと同じ方式）。
        // ライブでSetBlendShapeWeightするだけの旧実装は、実際のまばたきはAnimator/VRCFTがウェイトを
        // 継続的に上書きするため実質的に効かなかった。焼き込み方式ならウェイトに比例して常に効く
        // （補正はベイク時点のソースウェイトのスナップショットである点に注意：ソース側のウェイトを
        // 変えたら再適用が必要）。
        private void ApplyBlinkCorrection()
        {
            var renderer = _composeTarget;
            var currentMesh = renderer != null ? renderer.sharedMesh : null;
            if (renderer == null || currentMesh == null)
            {
                EditorUtility.DisplayDialog("エラー", "対象メッシュが設定されていません。", "OK");
                return;
            }

            string outputPath = GetBlinkFixOutputPath(renderer);
            string currentPath = AssetDatabase.GetAssetPath(currentMesh);

            // 二重適用防止：現在のメッシュが既に自分自身の出力そのものなら、それを新たな元として計算し直さない。
            if (!string.IsNullOrEmpty(currentPath) && currentPath == outputPath)
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    "現在のメッシュは既にまばたき修正の適用結果です。\n二重適用を防ぐため中断しました。\n" +
                    "「元メッシュに戻す」ボタンで元に戻してから再度実行してください。",
                    "OK");
                return;
            }

            // 現在のメッシュをそのまま元メッシュとして扱う（ログファイルは「元メッシュに戻す」専用で、ここでは参照しない）。
            var originalMesh = currentMesh;
            EnsureBlinkMeshCache(originalMesh);

            var sourceIndices = new Dictionary<string, int>(_blinkSourceNames.Count);
            foreach (var sourceName in _blinkSourceNames)
            {
                int sourceIndex = originalMesh.GetBlendShapeIndex(sourceName);
                if (sourceIndex < 0)
                {
                    Debug.LogWarning($"[まばたき修正] 合算元 \"{sourceName}\" がメッシュ上に見つかりません。スキップします。");
                    continue;
                }
                sourceIndices[sourceName] = sourceIndex;
            }

            var enabledTargets = new List<(string name, int index)>();
            foreach (var t in _blinkTargets)
            {
                if (!t.enabled) continue;
                int targetIndex = originalMesh.GetBlendShapeIndex(t.shapeKeyName);
                if (targetIndex < 0)
                {
                    Debug.LogWarning($"[まばたき修正] ターゲット \"{t.shapeKeyName}\" がメッシュ上に見つかりません。スキップします。");
                    continue;
                }
                enabledTargets.Add((t.shapeKeyName, targetIndex));
            }

            if (enabledTargets.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "有効なまばたきシェイプキーが元メッシュ上に見つかりません。", "OK");
                return;
            }
            if (sourceIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "有効な改変済み目元シェイプキーが元メッシュ上に見つかりません。", "OK");
                return;
            }

            var (rawV, rawN, rawT) = ComputeBlinkCorrectionField(originalMesh, sourceIndices, renderer);

            var targetDeltas = new Dictionary<string, (Vector3[], Vector3[], Vector3[])>();
            foreach (var (name, index) in enabledTargets)
            {
                int frameCount = originalMesh.GetBlendShapeFrameCount(index);
                if (frameCount == 0)
                {
                    Debug.LogWarning($"[まばたき修正] ターゲット \"{name}\" にフレームがありません。スキップします。");
                    continue;
                }

                var side = DetectTargetSide(originalMesh, name);

                int vertexCount = originalMesh.vertexCount;
                var origV = new Vector3[vertexCount];
                var origN = new Vector3[vertexCount];
                var origT = new Vector3[vertexCount];
                originalMesh.GetBlendShapeFrameVertices(index, frameCount - 1, origV, origN, origT);

                var newV = new Vector3[vertexCount];
                var newN = new Vector3[vertexCount];
                var newT = new Vector3[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                {
                    float w = BlinkSideVertexWeight(side, _blinkCachedBaseVertices[v].x);
                    newV[v] = origV[v] + rawV[v] * w;
                    newN[v] = origN[v] + rawN[v] * w;
                    newT[v] = origT[v] + rawT[v] * w;
                }
                targetDeltas[name] = (newV, newN, newT);
            }

            if (targetDeltas.Count == 0)
            {
                EditorUtility.DisplayDialog("エラー", "焼き込み可能なまばたきシェイプキーがありませんでした。", "OK");
                return;
            }

            var dst = UnityEngine.Object.Instantiate(originalMesh);
            // 出力メッシュ名はレンダラーの識別子から決定的に付ける（元メッシュ名ベースだと、
            // シェイプキー合成とまばたき修正を何度も往復した際に "_BlinkFixed_Composed_BlinkFixed..." と
            // 際限なく伸びてしまうため）。
            dst.name = SanitizeAssetFileName(renderer.gameObject.name) + "_BlinkFixed";
            CreateMeshWithReplacedShapes(dst, originalMesh, targetDeltas);

            SaveOrReplaceMeshAsset(dst, outputPath);
            WriteBlinkFixLog(GetBlinkFixLogPath(renderer), originalMesh);

            Undo.RecordObject(renderer, "Apply Blink Correction");
            renderer.sharedMesh = dst;
            EditorUtility.SetDirty(renderer);

            EditorUtility.DisplayDialog("完了", $"まばたき修正を適用しました。\n{outputPath}", "OK");

            ScanForCompose();
            RefreshBlinkSourceList();
            RefreshBlinkTargetList();
            DetectBlinkSourceCandidates();
            UpdateBlinkApplyButton();
            UpdateRestoreOriginalButton();
        }

        // 「元メッシュに戻す」：ログに記録された適用前のメッシュにSkinnedMeshRendererを戻す。
        private void RestoreOriginalBlinkMesh()
        {
            var renderer = _composeTarget;
            if (renderer == null)
            {
                EditorUtility.DisplayDialog("エラー", "対象メッシュが設定されていません。", "OK");
                return;
            }

            var original = ReadBlinkFixLogMesh(GetBlinkFixLogPath(renderer));
            if (original == null)
            {
                EditorUtility.DisplayDialog("復元できません", "元メッシュの記録が見つかりませんでした。", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("元メッシュに戻す", "適用前の元メッシュに戻します。よろしいですか？", "戻す", "キャンセル"))
                return;

            Undo.RecordObject(renderer, "Restore Original Blink Mesh");
            renderer.sharedMesh = original;
            EditorUtility.SetDirty(renderer);

            ScanForCompose();
            RefreshBlinkSourceList();
            RefreshBlinkTargetList();
            DetectBlinkSourceCandidates();
            UpdateBlinkApplyButton();
            UpdateRestoreOriginalButton();
        }

        private void EnsureBlinkMeshCache(Mesh mesh)
        {
            if (_blinkCachedMesh == mesh) return;
            _blinkCachedMesh = mesh;
            _blinkCachedBaseVertices = mesh.vertices;
            _blinkTargetSideCache.Clear();

            // スクラッチ配列をメッシュの頂点数分だけ確保して使い回す
            // （毎回のnew Vector3[]割り当てを避ける。ソースの数だけ呼ばれるため800シェイプキー規模だと効いてくる）。
            int vertexCount = mesh.vertexCount;
            if (_blinkScratchVertices == null || _blinkScratchVertices.Length != vertexCount)
            {
                _blinkScratchVertices = new Vector3[vertexCount];
                _blinkScratchNormals  = new Vector3[vertexCount];
                _blinkScratchTangents = new Vector3[vertexCount];
            }
        }

        // 頂点1個分のL/R/両重み。側面の判定にも焼き込み計算にも同じ式を使う（BLINK_SIDE_SMOOTH_RANGEで滑らかにブレンド）。
        private static float BlinkSideVertexWeight(BlinkShapeSide side, float restX)
        {
            if (side == BlinkShapeSide.Both) return 1f;
            return side == BlinkShapeSide.Left
                ? Mathf.InverseLerp(BLINK_SIDE_SMOOTH_RANGE, -BLINK_SIDE_SMOOTH_RANGE, restX)
                : Mathf.InverseLerp(-BLINK_SIDE_SMOOTH_RANGE, BLINK_SIDE_SMOOTH_RANGE, restX);
        }

        // まばたきシェイプキー自身が動かしている頂点の重心が、左右どちらに寄っているかでLeft/Right/Bothを自動判定する。
        private BlinkShapeSide DetectTargetSide(Mesh mesh, string shapeKeyName)
        {
            if (_blinkTargetSideCache.TryGetValue(shapeKeyName, out var cached)) return cached;

            EnsureBlinkMeshCache(mesh);

            BlinkShapeSide result = BlinkShapeSide.Both;
            int index = mesh.GetBlendShapeIndex(shapeKeyName);
            if (index >= 0)
            {
                int frameCount = mesh.GetBlendShapeFrameCount(index);
                if (frameCount > 0)
                {
                    mesh.GetBlendShapeFrameVertices(index, frameCount - 1, _blinkScratchVertices, _blinkScratchNormals, _blinkScratchTangents);

                    double leftWeighted = 0, total = 0;
                    for (int i = 0; i < _blinkScratchVertices.Length; i++)
                    {
                        float mag = _blinkScratchVertices[i].magnitude;
                        if (mag <= 0f) continue;

                        float x = _blinkCachedBaseVertices[i].x;
                        leftWeighted += mag * BlinkSideVertexWeight(BlinkShapeSide.Left, x);
                        total += mag;
                    }

                    if (total > 0)
                    {
                        double leftRatio = leftWeighted / total;
                        result = leftRatio >= BLINK_TARGET_SIDE_DETECT_THRESHOLD ? BlinkShapeSide.Left
                               : leftRatio <= 1.0 - BLINK_TARGET_SIDE_DETECT_THRESHOLD ? BlinkShapeSide.Right
                               : BlinkShapeSide.Both;
                    }
                }
            }

            _blinkTargetSideCache[shapeKeyName] = result;
            return result;
        }

        // 有効な「まばたきシェイプキー」それぞれについて個別に頂点マスクを作り、
        // 候補シェイプキーごとに「各マスクへの一致率」を計算してから合計する（マスクを1つに統合しない）。
        // ターゲットが複数（例: EyeClosedLeft と EyeClosedRight）ある場合、統合マスクに対する一致率だと
        // 「どのターゲットとどれだけ一致しているか」の情報が潰れてしまうため、ターゲットごとに分けて計算し合計する方式にしている。
        // まばたきシェイプキーの選択・プリセット適用・全解除のたびに自動で呼ばれるため、対話ダイアログは出さずに静かに更新する。
        private void DetectBlinkSourceCandidates()
        {
            _blinkSourceCandidates.Clear();

            var mesh = _composeTarget?.sharedMesh;
            var enabledTargetNames = mesh != null
                ? _blinkTargets.Where(t => t.enabled).Select(t => t.shapeKeyName).ToList()
                : new List<string>();

            if (mesh != null && enabledTargetNames.Count > 0)
            {
                EnsureBlinkMeshCache(mesh);

                var perTargetMasks = new List<HashSet<int>>();
                foreach (var targetName in enabledTargetNames)
                {
                    var mask = BuildBlinkTargetVertexMask(mesh, targetName);
                    if (mask.Count > 0) perTargetMasks.Add(mask);
                }

                if (perTargetMasks.Count > 0)
                {
                    var maskedMags = new double[perTargetMasks.Count];
                    try
                    {
                        for (int i = 0; i < _shapeNames.Count; i++)
                        {
                            string shapeName = _shapeNames[i];
                            if ((i & 63) == 0)
                                EditorUtility.DisplayProgressBar("目元シェイプキー候補を検出中", shapeName, (float)i / _shapeNames.Count);

                            if (enabledTargetNames.Contains(shapeName)) continue; // ターゲット自身は候補から除外
                            // 使用チェック済みでも判定自体は毎回やり直す。現在のターゲット構成でまだ候補条件を
                            // 満たせば候補マークを表示し続け、満たさなくなれば（該当ターゲットが外れた等）消える。

                            int index = mesh.GetBlendShapeIndex(shapeName);
                            if (index < 0) continue;
                            int frameCount = mesh.GetBlendShapeFrameCount(index);
                            if (frameCount == 0) continue;
                            mesh.GetBlendShapeFrameVertices(index, frameCount - 1, _blinkScratchVertices, _blinkScratchNormals, _blinkScratchTangents);

                            Array.Clear(maskedMags, 0, maskedMags.Length);
                            double totalMag = 0;
                            for (int v = 0; v < _blinkScratchVertices.Length; v++)
                            {
                                float mag = _blinkScratchVertices[v].magnitude;
                                if (mag <= 0f) continue;
                                totalMag += mag;
                                for (int m = 0; m < perTargetMasks.Count; m++)
                                    if (perTargetMasks[m].Contains(v)) maskedMags[m] += mag;
                            }
                            if (totalMag <= 0) continue;

                            double summedRatio = 0;
                            for (int m = 0; m < maskedMags.Length; m++)
                                summedRatio += maskedMags[m] / totalMag;

                            if (summedRatio >= BLINK_SOURCE_DETECT_RATIO_THRESHOLD)
                                _blinkSourceCandidates.Add(shapeName);
                        }
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }
            }

            RefreshBlinkSourceList();
        }

        // 単一のまばたきシェイプキーが動かしている頂点インデックスの集合（マスク）を作る。
        private HashSet<int> BuildBlinkTargetVertexMask(Mesh mesh, string targetName)
        {
            var mask = new HashSet<int>();

            int index = mesh.GetBlendShapeIndex(targetName);
            if (index < 0) return mask;
            int frameCount = mesh.GetBlendShapeFrameCount(index);
            if (frameCount == 0) return mask;
            mesh.GetBlendShapeFrameVertices(index, frameCount - 1, _blinkScratchVertices, _blinkScratchNormals, _blinkScratchTangents);

            for (int i = 0; i < _blinkScratchVertices.Length; i++)
                if (_blinkScratchVertices[i].magnitude > BLINK_SOURCE_DETECT_VERTEX_EPS)
                    mask.Add(i);

            return mask;
        }
    }
}
#endif
