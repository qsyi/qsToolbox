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
        private readonly Dictionary<Material, Image> _materialThumbImages = new Dictionary<Material, Image>();
        private readonly HashSet<Material> _pendingThumbMats = new HashSet<Material>();

        // ─── マテリアル置換 / poiyomi変換 の切り替え ───────────────────
        private enum MaterialSubMode { Replace, PoiyomiConvert }
        private MaterialSubMode _materialSubMode = MaterialSubMode.Replace;
        private VisualElement _materialReplaceModeBtn;
        private VisualElement _materialPoiyomiModeBtn;
        private VisualElement _materialReplaceModeAccent;
        private VisualElement _materialPoiyomiModeAccent;
        private Label _materialReplaceModeLabel;
        private Label _materialPoiyomiModeLabel;
        private VisualElement _materialSubPaneSlot;

        // ─── poiyomi変換モード ───────────────────────────────────────
        private enum PoiyomiConvertVariant { CopyToPro, ToPro, CopyToToon, ToToon }
        [SerializeField] private PoiyomiConvertVariant _poiyomiConvertVariant = PoiyomiConvertVariant.CopyToPro;
        private readonly HashSet<Material> _poiyomiSelectedMaterials = new HashSet<Material>();
        private VisualElement _poiyomiListEl;
        private Label _poiyomiCountLabel;
        private Button _poiyomiAllSelBtn;
        private Button _poiyomiExecuteButton;
        private DropdownField _poiyomiVariantDropdown;

        private static readonly (PoiyomiConvertVariant variant, string label)[] POIYOMI_VARIANTS =
        {
            (PoiyomiConvertVariant.CopyToPro,  "Translate Copy to Poiyomi Pro"),
            (PoiyomiConvertVariant.ToPro,      "Translate to Poiyomi Pro"),
            (PoiyomiConvertVariant.CopyToToon, "Translate Copy to Poiyomi Toon"),
            (PoiyomiConvertVariant.ToToon,     "Translate to Poiyomi Toon"),
        };

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

            if (_materialUsage.TryGetValue(newMaterial, out var existing))
            {
                // 置換先が既に一覧に存在する場合は使用箇所だけ統合し、旧スロットを削除（重複カード防止）
                existing.AddRange(usageList);
                _materials.Remove(oldMaterial);
            }
            else
            {
                _materialUsage[newMaterial] = usageList;
                for (int i = 0; i < _materials.Count; i++)
                {
                    if (_materials[i] == oldMaterial)
                    {
                        _materials[i] = newMaterial;
                        break;
                    }
                }
            }
        }

        // ─── モードの外枠（サブタブ + 中身のスロット） ────────────────

        private VisualElement BuildMaterialPane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1;

            var modeRow = new VisualElement();
            modeRow.style.flexDirection     = FlexDirection.Row;
            modeRow.style.flexShrink        = 0;
            modeRow.style.borderBottomWidth = 1;
            modeRow.style.borderBottomColor = PaneBorderColor;

            _materialReplaceModeBtn = BuildFlatSubTab("マテリアル置換", () => SetMaterialSubMode(MaterialSubMode.Replace),
                out _materialReplaceModeAccent, out _materialReplaceModeLabel);
            modeRow.Add(_materialReplaceModeBtn);

            _materialPoiyomiModeBtn = BuildFlatSubTab("poiyomi変換", () => SetMaterialSubMode(MaterialSubMode.PoiyomiConvert),
                out _materialPoiyomiModeAccent, out _materialPoiyomiModeLabel);
            modeRow.Add(_materialPoiyomiModeBtn);

            pane.Add(modeRow);

            _materialSubPaneSlot = new VisualElement();
            _materialSubPaneSlot.style.flexGrow  = 1;
            _materialSubPaneSlot.style.minHeight = 0;
            pane.Add(_materialSubPaneSlot);

            RebuildMaterialSubMode();
            return pane;
        }

        private void SetMaterialSubMode(MaterialSubMode mode)
        {
            if (_materialSubMode == mode) return;
            _materialSubMode = mode;
            RebuildMaterialSubMode();
        }

        // 外部（ScanDataからの再スキャン等）から呼ばれる窓口。
        // 静的なUI構造は壊さず、現在アクティブなサブモードの中身だけを更新する。
        private void RebuildMaterialPane()
        {
            if (_materialSubPaneSlot == null) return;
            if (_materialSubMode == MaterialSubMode.Replace)
                RebuildMaterialReplaceSubPane();
            else
                RebuildPoiyomiConvertSubPane();
        }

        private void RebuildMaterialSubMode()
        {
            if (_materialSubPaneSlot == null) return;

            bool replaceActive = _materialSubMode == MaterialSubMode.Replace;
            SetFlatSubTabActive(_materialReplaceModeAccent, _materialReplaceModeLabel, replaceActive);
            SetFlatSubTabActive(_materialPoiyomiModeAccent, _materialPoiyomiModeLabel, !replaceActive);

            _materialSubPaneSlot.Clear();
            if (replaceActive)
            {
                _materialScrollView = null;
                _materialSubPaneSlot.Add(BuildMaterialReplaceSubPane());
            }
            else
            {
                _poiyomiListEl          = null;
                _poiyomiCountLabel      = null;
                _poiyomiAllSelBtn       = null;
                _poiyomiExecuteButton   = null;
                _poiyomiVariantDropdown = null;
                _materialSubPaneSlot.Add(BuildPoiyomiConvertSubPane());
            }
        }

        // ─── マテリアル置換サブモード（既存機能、無変更） ─────────────

        private VisualElement BuildMaterialReplaceSubPane()
        {
            bool dark = EditorGUIUtility.isProSkin;

            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1;
            pane.style.minHeight = 0;

            // Section header
            var hdr = new VisualElement();
            hdr.style.flexDirection   = FlexDirection.Row;
            hdr.style.alignItems      = Align.Center;
            hdr.style.paddingLeft     = 10;
            hdr.style.paddingRight    = 10;
            hdr.style.paddingTop      = 7;
            hdr.style.paddingBottom   = 7;
            hdr.style.borderBottomWidth = 1;
            hdr.style.borderBottomColor = ChromeBorderColor;

            var hdrTitle = new Label("マテリアル置換");
            hdrTitle.style.fontSize = 12;
            hdrTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            hdrTitle.style.flexGrow = 1;
            hdr.Add(hdrTitle);

            var hint = new Label("マテリアルをドラッグして置換");
            hint.style.fontSize = 12;
            hint.style.color       = dark
                ? new Color(0.55f, 0.55f, 0.55f, 1f)
                : new Color(0.45f, 0.45f, 0.45f, 1f);
            hdr.Add(hint);

            pane.Add(hdr);

            _materialScrollView = new ScrollView(ScrollViewMode.Vertical);
            _materialScrollView.style.flexGrow  = 1;
            _materialScrollView.style.minHeight = 0;
            pane.Add(_materialScrollView);

            RebuildMaterialReplaceSubPane();
            return pane;
        }

        private void RebuildMaterialReplaceSubPane()
        {
            if (_materialScrollView == null) return;
            _materialScrollView.Clear();

            bool dark = EditorGUIUtility.isProSkin;

            if (_materials.Count == 0)
            {
                var empty = new Label("マテリアルが見つかりません。");
                empty.style.fontSize   = 11;
                empty.style.color      = dark
                    ? new Color(0.55f, 0.55f, 0.55f, 1f)
                    : new Color(0.45f, 0.45f, 0.45f, 1f);
                empty.style.unityFontStyleAndWeight = FontStyle.Italic;
                empty.style.marginTop    = 16;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                _materialScrollView.Add(empty);
                return;
            }

            _materialThumbImages.Clear();
            _pendingThumbMats.Clear();

            foreach (var mat in _materials)
            {
                _materialUsage.TryGetValue(mat, out var usages);
                _materialScrollView.Add(BuildMaterialCard(mat, usages));
            }

            if (_pendingThumbMats.Count > 0)
            {
                _materialScrollView.schedule.Execute(() =>
                {
                    _pendingThumbMats.RemoveWhere(m =>
                    {
                        var p = AssetPreview.GetAssetPreview(m);
                        if (p == null) return false;
                        if (_materialThumbImages.TryGetValue(m, out var img)) img.image = p;
                        return true;
                    });
                }).Every(200).Until(() => _pendingThumbMats.Count == 0);
            }
        }

        private VisualElement BuildMaterialCard(Material mat,
            List<(Renderer renderer, int slot)> usages)
        {
            bool dark = EditorGUIUtility.isProSkin;

            // ── Card shell ────────────────────────────────────────────
            var card = new VisualElement();
            card.style.flexDirection   = FlexDirection.Row;
            card.style.alignItems      = Align.FlexStart;   // 子要素を上揃え固定
            card.style.paddingLeft     = 8;
            card.style.paddingRight    = 8;
            card.style.paddingTop      = 6;
            card.style.paddingBottom   = 6;
            card.style.borderBottomWidth = 1;
            card.style.borderBottomColor = PaneBorderColor;

            if (mat != null)
                card.RegisterCallback<MouseDownEvent>(_ => EditorGUIUtility.PingObject(mat));

            // ── D&D オーバーレイ ──────────────────────────────────────
            var overlay = new VisualElement();
            overlay.style.position        = Position.Absolute;
            overlay.style.top             = 0;
            overlay.style.left            = 0;
            overlay.style.right           = 0;
            overlay.style.bottom          = 0;
            overlay.style.backgroundColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.30f);
            overlay.style.alignItems      = Align.Center;
            overlay.style.justifyContent  = Justify.Center;
            overlay.style.display         = DisplayStyle.None;
            overlay.pickingMode           = PickingMode.Ignore;

            var overlayText = new Label("⇄");
            overlayText.style.color                    = Color.white;
            overlayText.style.fontSize                 = 28;
            overlayText.style.unityFontStyleAndWeight  = FontStyle.Bold;
            overlayText.pickingMode                    = PickingMode.Ignore;
            overlay.Add(overlayText);
            // card.Add(overlay) は rightCol より後で追加（描画順を最後にして前面表示）

            card.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var dragged = DragAndDrop.objectReferences.OfType<Material>()
                    .FirstOrDefault(m => m != mat);
                if (dragged != null)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    overlay.style.display  = DisplayStyle.Flex;
                }
                else
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    overlay.style.display  = DisplayStyle.None;
                }
                evt.StopPropagation();
            });

            card.RegisterCallback<DragLeaveEvent>(_ => overlay.style.display = DisplayStyle.None);
            // DragExited はドラッグがキャンセル（Escape等）された場合に発火する
            card.RegisterCallback<DragExitedEvent>(_ => overlay.style.display = DisplayStyle.None);

            card.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                overlay.style.display = DisplayStyle.None;
                var newMat = DragAndDrop.objectReferences.OfType<Material>()
                    .FirstOrDefault(m => m != mat);
                if (newMat != null)
                {
                    ReplaceMaterial(mat, newMat);
                    RebuildMaterialReplaceSubPane();
                }
                evt.StopPropagation();
            });

            // ── Thumbnail ─────────────────────────────────────────────
            var thumb = new Image();
            thumb.style.width      = 40;
            thumb.style.height     = 40;
            thumb.style.flexShrink = 0;
            thumb.style.marginRight = 8;
            thumb.style.marginTop   = 2;
            thumb.style.borderTopLeftRadius = thumb.style.borderTopRightRadius =
                thumb.style.borderBottomLeftRadius = thumb.style.borderBottomRightRadius = 4;
            thumb.style.backgroundColor = dark
                ? new Color(0.18f, 0.18f, 0.20f, 1f)
                : new Color(0.78f, 0.78f, 0.80f, 1f);

            var initial = AssetPreview.GetAssetPreview(mat);
            if (initial != null)
                thumb.image = initial;
            else
            {
                thumb.image = AssetPreview.GetMiniThumbnail(mat);
                if (mat != null)
                {
                    _materialThumbImages[mat] = thumb;
                    _pendingThumbMats.Add(mat);
                }
            }
            card.Add(thumb);

            // ── Right column ─────────────────────────────────────────
            var rightCol = new VisualElement();
            rightCol.style.flexGrow   = 1;
            rightCol.style.flexShrink = 1;

            // 上段: マテリアル名 + 置換 ObjectField（常に上端固定）
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;
            nameRow.style.marginBottom  = 3;

            var nameLbl = new Label(mat != null ? mat.name : "(null)");
            nameLbl.style.fontSize = 12;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.color   = TextColor;
            nameLbl.style.flexGrow = 1;
            nameRow.Add(nameLbl);

            if (mat != null)
            {
                var matField = new ObjectField();
                matField.objectType        = typeof(Material);
                matField.allowSceneObjects = false;
                matField.value             = mat;
                matField.label             = "";
                matField.style.width       = 160;
                matField.style.flexShrink  = 0;
                matField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
                matField.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
                matField.RegisterValueChangedCallback(evt =>
                {
                    // ドラッグ中はObjectFieldのChangeEventを無視する（DragPerformEventで処理する）
                    if (DragAndDrop.objectReferences.Length > 0)
                    {
                        matField.SetValueWithoutNotify(mat);
                        return;
                    }
                    var newMat = evt.newValue as Material;
                    if (newMat != null && newMat != mat)
                    {
                        ReplaceMaterial(mat, newMat);
                        RebuildMaterialReplaceSubPane();
                    }
                    else
                    {
                        matField.SetValueWithoutNotify(mat);
                    }
                });
                nameRow.Add(matField);
            }
            rightCol.Add(nameRow);

            // 下段: レンダラー一覧（展開しても上段は動かない）
            if (usages != null && usages.Count > 0)
            {
                var rendRow = new VisualElement();
                rendRow.style.flexDirection = FlexDirection.Row;
                rendRow.style.alignItems    = Align.Center;

                var r0Wrap = MakeRendererField(usages[0].renderer);
                r0Wrap.style.flexGrow = 1;
                rendRow.Add(r0Wrap);

                if (usages.Count > 1)
                {
                    bool expanded = false;
                    var extraList = new VisualElement();
                    extraList.style.display = DisplayStyle.None;
                    for (int i = 1; i < usages.Count; i++)
                        extraList.Add(MakeRendererField(usages[i].renderer));

                    var expandBtn = new Label($"+{usages.Count - 1}件 ▼");
                    expandBtn.style.fontSize    = 10;
                    expandBtn.style.color       = AccentColor;
                    expandBtn.style.marginLeft  = 6;
                    expandBtn.style.flexShrink  = 0;
                    expandBtn.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        expanded = !expanded;
                        extraList.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                        expandBtn.text = expanded
                            ? $"−{usages.Count - 1}件 ▲"
                            : $"+{usages.Count - 1}件 ▼";
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

            card.Add(rightCol);
            card.Add(overlay); // 最後に追加して前面に描画
            return card;
        }

        // ─── poiyomi変換サブモード ─────────────────────────────────────

        private VisualElement BuildPoiyomiConvertSubPane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1;
            pane.style.minHeight = 0;

            if (!IsPoiyomiTranslatorAvailable())
            {
                var missing = new HelpBox(
                    "Poiyomiシェーダーが見つかりません（このプロジェクトにはインストールされていないようです）。",
                    HelpBoxMessageType.Warning);
                missing.style.marginLeft = missing.style.marginRight =
                    missing.style.marginTop = missing.style.marginBottom = 10;
                pane.Add(missing);
                return pane;
            }

            // ── Header: 件数 + 全選択/全解除 ──
            var hdr = new VisualElement();
            hdr.style.flexDirection     = FlexDirection.Row;
            hdr.style.alignItems        = Align.Center;
            hdr.style.paddingLeft       = 8;
            hdr.style.paddingRight      = 8;
            hdr.style.paddingTop        = 5;
            hdr.style.paddingBottom     = 5;
            hdr.style.borderBottomWidth = 1;
            hdr.style.borderBottomColor = PaneBorderColor;

            _poiyomiCountLabel = new Label("選択中 (0/0)");
            _poiyomiCountLabel.style.fontSize                = 12;
            _poiyomiCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _poiyomiCountLabel.style.color                   = TextColor;
            _poiyomiCountLabel.style.flexGrow = 1;
            hdr.Add(_poiyomiCountLabel);

            _poiyomiAllSelBtn = new Button(() =>
            {
                var eligible = _materials.Where(IsLiltoonMaterial).ToList();
                bool allSel = eligible.Count > 0 && eligible.All(m => _poiyomiSelectedMaterials.Contains(m));
                if (allSel) _poiyomiSelectedMaterials.Clear();
                else        foreach (var m in eligible) _poiyomiSelectedMaterials.Add(m);
                RebuildPoiyomiConvertSubPane();
            });
            _poiyomiAllSelBtn.text             = "全選択";
            _poiyomiAllSelBtn.style.marginLeft = 4;
            _poiyomiAllSelBtn.style.fontSize   = 10;
            _poiyomiAllSelBtn.style.height     = 20;
            hdr.Add(_poiyomiAllSelBtn);

            pane.Add(hdr);

            // ── マテリアル一覧（チェックボックス選択） ──
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow  = 1;
            scroll.style.minHeight = 0;
            _poiyomiListEl = new VisualElement();
            scroll.Add(_poiyomiListEl);
            pane.Add(scroll);

            // ── フッター: 変換先ドロップダウン + 実行ボタン ──
            var footer = new VisualElement();
            footer.style.flexDirection   = FlexDirection.Row;
            footer.style.alignItems      = Align.Center;
            footer.style.flexShrink      = 0;
            footer.style.paddingLeft     = 8;
            footer.style.paddingRight    = 8;
            footer.style.paddingTop      = 6;
            footer.style.paddingBottom   = 6;
            footer.style.borderTopWidth  = 1;
            footer.style.borderTopColor  = PaneBorderColor;

            bool hasPro = PoiyomiProjectHasPro();
            var variantLabels = POIYOMI_VARIANTS
                .Where(v => hasPro || v.variant == PoiyomiConvertVariant.CopyToToon || v.variant == PoiyomiConvertVariant.ToToon)
                .Select(v => v.label)
                .ToList();
            if (!hasPro && (_poiyomiConvertVariant == PoiyomiConvertVariant.CopyToPro || _poiyomiConvertVariant == PoiyomiConvertVariant.ToPro))
                _poiyomiConvertVariant = PoiyomiConvertVariant.CopyToToon;

            int currentLabelIndex = variantLabels.IndexOf(POIYOMI_VARIANTS.First(v => v.variant == _poiyomiConvertVariant).label);
            _poiyomiVariantDropdown = new DropdownField(variantLabels, Mathf.Max(0, currentLabelIndex));
            _poiyomiVariantDropdown.style.flexGrow = 1;
            FixFieldOverflow(_poiyomiVariantDropdown);
            _poiyomiVariantDropdown.RegisterValueChangedCallback(evt =>
            {
                var match = POIYOMI_VARIANTS.FirstOrDefault(v => v.label == evt.newValue);
                _poiyomiConvertVariant = match.label != null ? match.variant : PoiyomiConvertVariant.CopyToPro;
            });
            footer.Add(_poiyomiVariantDropdown);

            _poiyomiExecuteButton = new Button(ExecutePoiyomiConvert) { text = "変換を実行" };
            _poiyomiExecuteButton.style.marginLeft = 6;
            _poiyomiExecuteButton.style.height     = 22;
            _poiyomiExecuteButton.style.flexShrink = 0;
            footer.Add(_poiyomiExecuteButton);

            pane.Add(footer);

            if (!hasPro)
            {
                var proNote = new Label("このプロジェクトにはPoiyomi Proが見つからないため、Pro系の変換先は選べません。");
                proNote.style.fontSize   = 10;
                proNote.style.color      = DimColor;
                proNote.style.marginTop  = 4;
                pane.Add(proNote);
            }

            RebuildPoiyomiConvertSubPane();
            return pane;
        }

        private void RebuildPoiyomiConvertSubPane()
        {
            if (_poiyomiListEl == null) return;

            _poiyomiSelectedMaterials.IntersectWith(_materials);
            _poiyomiSelectedMaterials.RemoveWhere(m => !IsLiltoonMaterial(m));
            _poiyomiListEl.Clear();

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
                _poiyomiListEl.Add(empty);
            }

            foreach (var mat in _materials)
            {
                if (mat == null) continue;
                var matRef = mat;
                bool eligible = IsLiltoonMaterial(mat);

                var row = new VisualElement();
                row.style.flexDirection     = FlexDirection.Row;
                row.style.alignItems        = Align.Center;
                row.style.paddingLeft       = 6;
                row.style.paddingRight      = 8;
                row.style.paddingTop        = 5;
                row.style.paddingBottom     = 5;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = PaneBorderColor;
                if (!eligible)
                {
                    row.style.opacity = 0.45f;
                    row.tooltip = "liltoonシェーダーのマテリアルではないため変換できません。";
                }
                row.RegisterCallback<MouseDownEvent>(_ => EditorGUIUtility.PingObject(matRef));

                var chk = new Toggle();
                chk.value             = eligible && _poiyomiSelectedMaterials.Contains(mat);
                chk.style.marginRight = 4;
                chk.style.flexShrink  = 0;
                chk.SetEnabled(eligible);
                chk.Q<Label>(className: "unity-toggle__label")?.RemoveFromHierarchy();
                chk.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                chk.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _poiyomiSelectedMaterials.Add(matRef);
                    else              _poiyomiSelectedMaterials.Remove(matRef);
                    UpdatePoiyomiCountAndButton();
                });
                row.Add(chk);

                var thumb = new Image();
                thumb.style.width       = 32;
                thumb.style.height      = 32;
                thumb.style.flexShrink  = 0;
                thumb.style.marginRight = 6;
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

                var nameLbl = new Label(mat.name);
                nameLbl.style.fontSize                = 12;
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.color                   = TextColor;
                nameLbl.style.flexGrow                = 1;
                nameLbl.style.overflow                = Overflow.Hidden;
                row.Add(nameLbl);

                var kind = GetMaterialShaderKind(mat);
                var kindColor = ShaderKindColor(kind);
                var kindBadge = new Label(ShaderKindLabel(kind));
                kindBadge.style.fontSize    = 9;
                kindBadge.style.color       = kindColor;
                kindBadge.style.borderTopWidth = kindBadge.style.borderBottomWidth
                    = kindBadge.style.borderLeftWidth = kindBadge.style.borderRightWidth = 1;
                kindBadge.style.borderTopColor = kindBadge.style.borderBottomColor
                    = kindBadge.style.borderLeftColor = kindBadge.style.borderRightColor = kindColor;
                kindBadge.style.paddingLeft = kindBadge.style.paddingRight = 3;
                kindBadge.style.marginLeft  = 4;
                kindBadge.style.flexShrink  = 0;
                row.Add(kindBadge);

                _poiyomiListEl.Add(row);
            }

            if (pending.Count > 0)
            {
                _poiyomiListEl.schedule.Execute(() =>
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

            UpdatePoiyomiCountAndButton();
        }

        private void UpdatePoiyomiCountAndButton()
        {
            if (_poiyomiCountLabel != null)
                _poiyomiCountLabel.text = $"選択中 ({_poiyomiSelectedMaterials.Count}/{_materials.Count})";
            if (_poiyomiExecuteButton != null)
                _poiyomiExecuteButton.SetEnabled(IsPoiyomiTranslatorAvailable() && _poiyomiSelectedMaterials.Count > 0);
        }

        private void ExecutePoiyomiConvert()
        {
            if (!IsPoiyomiTranslatorAvailable()) return;
            var materials = _poiyomiSelectedMaterials.Where(IsLiltoonMaterial).ToList();
            if (materials.Count == 0) return;

            bool isPro  = _poiyomiConvertVariant == PoiyomiConvertVariant.CopyToPro || _poiyomiConvertVariant == PoiyomiConvertVariant.ToPro;
            bool isCopy = _poiyomiConvertVariant == PoiyomiConvertVariant.CopyToPro || _poiyomiConvertVariant == PoiyomiConvertVariant.CopyToToon;

            Undo.SetCurrentGroupName("Poiyomi変換");
            int group = Undo.GetCurrentGroup();
            try
            {
                if (isCopy)
                {
                    string suffix = isPro ? "PoiPro" : "PoiToon";
                    foreach (var mat in materials)
                    {
                        var newMat = DuplicateMaterialAsset(mat, $"{mat.name}_{suffix}");
                        if (newMat == null) continue;
                        _poiTranslateMaterialMethod.Invoke(null, new object[] { newMat, isPro });
                        // 変換後、コピー元を使っているレンダラーを自動でコピー先に置き換える
                        ReplaceMaterial(mat, newMat);
                        EditorGUIUtility.PingObject(newMat);
                    }
                }
                else
                {
                    foreach (var mat in materials)
                        _poiTranslateMaterialMethod.Invoke(null, new object[] { mat, isPro });
                }
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }

            _poiyomiSelectedMaterials.Clear();
            RebuildPoiyomiConvertSubPane();
        }

        // Poiyomi本体のPoiHelpers.TryDuplicateMaterialAssetと同等の複製（同フォルダに新規アセットを作成）。
        // 戻り値のMaterialは呼び出し側でReplaceMaterial()に渡し、レンダラーへの反映に使う。
        private static Material DuplicateMaterialAsset(Material source, string newName)
        {
            if (source == null) return null;
            string srcPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(srcPath) || !(srcPath.StartsWith("Assets/") || srcPath.StartsWith("Packages/")))
                return null;

            string dir = Path.GetDirectoryName(srcPath)?.Replace('\\', '/') ?? "Assets";
            string newPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{newName}.mat");
            if (!AssetDatabase.CopyAsset(srcPath, newPath)) return null;

            AssetDatabase.ImportAsset(newPath);
            return AssetDatabase.LoadAssetAtPath<Material>(newPath);
        }

        // ─── Poiyomi（Thry ShaderTranslator）へのリフレクション ─────────
        // qsToolBoxはPoiyomiをVPM依存に持たないため、型を一切コンパイル時参照せず、
        // 見つからない場合は機能を静かに無効化する（Modular Avatar連携と同じ方針）。

        private static bool _poiyomiResolved;
        private static MethodInfo _poiTranslateMaterialMethod;       // private static void _TranslateMaterial(Material, bool)
        private static MethodInfo _poiProjectHasProMethod;           // private static bool _ProjectHasPro()

        private static void ResolvePoiyomiTranslator()
        {
            if (_poiyomiResolved) return;
            _poiyomiResolved = true;

            const string typeName = "Poi.Tools.ShaderTranslator.Translations.Menu.LiltoonToPoiyomiTranslation_Menus";
            var type = Type.GetType($"{typeName}, Assembly-CSharp-Editor")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => { try { return a.GetType(typeName); } catch { return null; } })
                    .FirstOrDefault(t => t != null);
            if (type == null) return;

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            _poiTranslateMaterialMethod = type.GetMethod("_TranslateMaterial", flags, null, new[] { typeof(Material), typeof(bool) }, null);
            _poiProjectHasProMethod     = type.GetMethod("_ProjectHasPro", flags, null, Type.EmptyTypes, null);

            if (_poiTranslateMaterialMethod == null)
                _poiProjectHasProMethod = null;
        }

        private static bool IsPoiyomiTranslatorAvailable()
        {
            ResolvePoiyomiTranslator();
            return _poiTranslateMaterialMethod != null;
        }

        private static bool PoiyomiProjectHasPro()
        {
            ResolvePoiyomiTranslator();
            if (_poiProjectHasProMethod == null) return false;
            try { return (bool)_poiProjectHasProMethod.Invoke(null, null); }
            catch (TargetInvocationException) { return false; }
        }

        // マテリアル欄に表示する「liltoon / Poiyomi Pro / Poiyomi Toon / その他」の判定。
        private enum MaterialShaderKind { Liltoon, PoiyomiPro, PoiyomiToon, Other }

        private static MaterialShaderKind GetMaterialShaderKind(Material mat)
        {
            if (mat == null || mat.shader == null) return MaterialShaderKind.Other;
            string name = mat.shader.name;
            if (name.IndexOf("liltoon", StringComparison.OrdinalIgnoreCase) >= 0)
                return MaterialShaderKind.Liltoon;
            if (name.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (name.IndexOf("pro", StringComparison.OrdinalIgnoreCase) >= 0)
                    return MaterialShaderKind.PoiyomiPro;
                if (name.IndexOf("toon", StringComparison.OrdinalIgnoreCase) >= 0)
                    return MaterialShaderKind.PoiyomiToon;
            }
            return MaterialShaderKind.Other;
        }

        private static string ShaderKindLabel(MaterialShaderKind kind) => kind switch
        {
            MaterialShaderKind.Liltoon    => "liltoon",
            MaterialShaderKind.PoiyomiPro => "Poiyomi Pro",
            MaterialShaderKind.PoiyomiToon => "Poiyomi Toon",
            _ => "その他",
        };

        private static Color ShaderKindColor(MaterialShaderKind kind) => kind switch
        {
            MaterialShaderKind.Liltoon     => AccentColor,
            MaterialShaderKind.PoiyomiPro  => new Color(0.85f, 0.65f, 0.15f),
            MaterialShaderKind.PoiyomiToon => new Color(0.25f, 0.65f, 0.60f),
            _ => DimColor,
        };

        // Poiyomi本体のCanTranslateMaterial（LiltoonToPoiyomiToonTranslation.cs）と同じ判定をそのまま踏襲。
        // liltoon系以外のマテリアルは変換対象外としてグレーアウトする。
        private static bool IsLiltoonMaterial(Material mat) => GetMaterialShaderKind(mat) == MaterialShaderKind.Liltoon;
    }
}
#endif
