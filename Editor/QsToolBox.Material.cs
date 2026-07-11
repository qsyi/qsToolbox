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

        private VisualElement BuildMaterialPane()
        {
            bool dark = EditorGUIUtility.isProSkin;

            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;

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

            RebuildMaterialPane();
            return pane;
        }

        private void RebuildMaterialPane()
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
                    RebuildMaterialPane();
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
                        RebuildMaterialPane();
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
    }
}
#endif
