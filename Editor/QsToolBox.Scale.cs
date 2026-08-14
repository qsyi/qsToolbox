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
        private VisualElement _scalePane;
        private ObjectField   _scaleAvatarField;
        private VisualElement _scaleOutfitRows;
        private VisualElement _scaleWarnings;
        private ScrollView    _scaleBoneScroll;
        private VisualElement _scaleBoneDetail;
        private string        _selectedBoneName = "";
        private Label         _scaleSyncLabel;
        private Button        _scaleSyncButton;
        private Toggle        _scaleAdjusterToggle;
        private Toggle        _scaleScaleToggle;
        private Toggle        _scaleRotToggle;
        private bool          _scaleOutfitFoldoutExpanded = false;
        private readonly Dictionary<string, VisualElement> _scaleBoneRows = new Dictionary<string, VisualElement>();
        [System.Serializable]
        private class OutfitArmatureEntry
        {
            public GameObject Outfit;
            public List<Transform> Armatures = new List<Transform>();
            [HideInInspector] public bool AutoAssigned;
        }

        private VisualElement BuildScalePane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1;

            // ── Header: armature settings ─────────────────────────────
            var hdr = new VisualElement();
            hdr.style.paddingLeft  = 10;
            hdr.style.paddingRight = 10;
            hdr.style.paddingTop   = hdr.style.paddingBottom = 7;
            hdr.style.borderBottomWidth = 1;
            hdr.style.borderBottomColor = PaneBorderColor;

            var avatarRow = new VisualElement();
            avatarRow.style.flexDirection = FlexDirection.Row;
            avatarRow.style.alignItems = Align.Center;
            avatarRow.style.marginBottom = 4;

            var avatarLbl = new Label("素体 Armature");
            avatarLbl.style.fontSize = 11;
            avatarLbl.style.color = DimColor;
            avatarLbl.style.width = 100;
            avatarLbl.style.flexShrink = 0;
            avatarRow.Add(avatarLbl);

            _scaleAvatarField = new ObjectField();
            _scaleAvatarField.objectType = typeof(Transform);
            _scaleAvatarField.allowSceneObjects = true;
            _scaleAvatarField.value          = _avatarArmature;
            _scaleAvatarField.style.flexGrow  = 1;
            _scaleAvatarField.style.flexShrink = 1;
            _scaleAvatarField.style.minWidth  = 0;
            _scaleAvatarField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            _scaleAvatarField.RegisterCallback<AttachToPanelEvent>(_ =>
                _scaleAvatarField.Query<VisualElement>().Build().ForEach(e => e.style.minWidth = 0));
            _scaleAvatarField.RegisterValueChangedCallback(evt =>
            {
                _serializedObject.Update();
                _armatureProperty.objectReferenceValue = evt.newValue as Transform;
                _serializedObject.ApplyModifiedProperties();
                _avatarArmature = evt.newValue as Transform;
                ScanBones();
                RebuildScalePane();
            });
            avatarRow.Add(_scaleAvatarField);
            hdr.Add(avatarRow);

            _scaleOutfitRows = new VisualElement();
            hdr.Add(_scaleOutfitRows);
            pane.Add(hdr);

            // ── Warnings ──────────────────────────────────────────────
            _scaleWarnings = new VisualElement();
            _scaleWarnings.style.display = DisplayStyle.None;
            pane.Add(_scaleWarnings);

            // ── Body: 2-column ────────────────────────────────────────
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow  = 1;
            body.style.minHeight = 0;

            var leftPane = new VisualElement();
            leftPane.style.flexGrow = 0;
            leftPane.style.flexShrink = 0;
            leftPane.style.width = 140;
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = PaneBorderColor;

            var leftHd = new VisualElement();
            leftHd.style.flexDirection = FlexDirection.Row;
            leftHd.style.alignItems = Align.Center;
            leftHd.style.paddingLeft = leftHd.style.paddingRight = 8;
            leftHd.style.paddingTop = leftHd.style.paddingBottom = 5;
            leftHd.style.borderBottomWidth = 1;
            leftHd.style.borderBottomColor = PaneBorderColor;
            var leftHdLbl = new Label("ボーン一覧");
            leftHdLbl.style.fontSize = 11;
            leftHdLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            leftHd.Add(leftHdLbl);
            leftPane.Add(leftHd);

            _scaleBoneScroll = new ScrollView();
            _scaleBoneScroll.style.flexGrow  = 1;
            _scaleBoneScroll.style.minHeight = 0;
            leftPane.Add(_scaleBoneScroll);
            body.Add(leftPane);

            var rightPane = new VisualElement();
            rightPane.style.flexGrow      = 1;
            rightPane.style.minHeight     = 0;
            rightPane.style.flexDirection = FlexDirection.Column;

            var rightHd = new VisualElement();
            rightHd.style.flexDirection = FlexDirection.Row;
            rightHd.style.alignItems = Align.Center;
            rightHd.style.paddingLeft = rightHd.style.paddingRight = 8;
            rightHd.style.paddingTop = rightHd.style.paddingBottom = 5;
            rightHd.style.borderBottomWidth = 1;
            rightHd.style.borderBottomColor = PaneBorderColor;
            var rightHdLbl = new Label("スケール編集");
            rightHdLbl.style.fontSize = 11;
            rightHdLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            rightHd.Add(rightHdLbl);
            rightPane.Add(rightHd);

            var detailScroll = new ScrollView();
            detailScroll.style.flexGrow  = 1;
            detailScroll.style.minHeight = 0;

            _scaleBoneDetail = new VisualElement();
            _scaleBoneDetail.style.paddingLeft  = _scaleBoneDetail.style.paddingRight  = 10;
            _scaleBoneDetail.style.paddingTop   = _scaleBoneDetail.style.paddingBottom = 8;
            detailScroll.Add(_scaleBoneDetail);
            rightPane.Add(detailScroll);
            body.Add(rightPane);
            pane.Add(body);

            // ── Footer: sync controls ─────────────────────────────────
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems    = Align.Center;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = PaneBorderColor;
            footer.style.paddingLeft    = footer.style.paddingRight  = 10;
            footer.style.paddingTop     = footer.style.paddingBottom = 8;

            var toggleColumn = new VisualElement();
            toggleColumn.style.flexDirection = FlexDirection.Column;
            toggleColumn.style.flexGrow      = 1;

            _scaleAdjusterToggle = new Toggle("ScaleAdjusterを同期");
            _scaleAdjusterToggle.tooltip = "MAの「ScaleAdjusterを一致させる」と同じ処理で、衣装のScaleAdjusterを素体に合わせます";
            _scaleAdjusterToggle.value            = _autoSyncScaleAdjuster;
            _scaleAdjusterToggle.style.fontSize   = 12;
            _scaleAdjusterToggle.style.marginBottom = 2;
            _scaleAdjusterToggle.RegisterValueChangedCallback(evt => _autoSyncScaleAdjuster = evt.newValue);
            toggleColumn.Add(_scaleAdjusterToggle);

            _scaleScaleToggle = new Toggle("スケールを同期");
            _scaleScaleToggle.tooltip = "MAの「位置をもとアバターに合わせてリセット」のスケールを合わせるオプションと同じ処理です";
            _scaleScaleToggle.value            = _autoSyncScale;
            _scaleScaleToggle.style.fontSize   = 12;
            _scaleScaleToggle.style.marginBottom = 2;
            _scaleScaleToggle.RegisterValueChangedCallback(evt => _autoSyncScale = evt.newValue);
            toggleColumn.Add(_scaleScaleToggle);

            _scaleRotToggle = new Toggle("回転も同期");
            _scaleRotToggle.tooltip = "MAの「位置をもとアバターに合わせてリセット」の回転を合わせるオプションと同じ処理です";
            _scaleRotToggle.value          = _autoSyncRotate;
            _scaleRotToggle.style.fontSize = 12;
            _scaleRotToggle.style.marginBottom = 2;
            _scaleRotToggle.RegisterValueChangedCallback(evt => _autoSyncRotate = evt.newValue);
            toggleColumn.Add(_scaleRotToggle);

            var positionResetCaption = new Label("スケールまたは回転を同期すると、衣装ボーンの位置も基準アバターに合わせてリセットされます");
            positionResetCaption.style.fontSize = 10;
            positionResetCaption.style.color = DimColor;
            positionResetCaption.style.whiteSpace = WhiteSpace.Normal;
            toggleColumn.Add(positionResetCaption);

            footer.Add(toggleColumn);

            _scaleSyncLabel = new Label("✓ 同期しました");
            _scaleSyncLabel.style.fontSize    = 12;
            _scaleSyncLabel.style.color       = new Color(0.18f, 0.58f, 0.28f);
            _scaleSyncLabel.style.alignSelf   = Align.Center;
            _scaleSyncLabel.style.marginRight = 8;
            _scaleSyncLabel.style.display     = DisplayStyle.None;
            footer.Add(_scaleSyncLabel);

            _scaleSyncButton = new Button(() =>
            {
                // 直前にボーン構成が変わっている可能性があるので、同期直前に一度だけ最新化する。
                // RebuildScalePaneの差分検出はTransform参照から値を都度ライブに読むため、同期後の再スキャンは不要。
                ScanBones();
                ApplyAvatarScalesToOutfits();
                RebuildScalePane();
                _scaleSyncLabel.style.display = DisplayStyle.Flex;
                _scaleSyncLabel.schedule.Execute(() =>
                    _scaleSyncLabel.style.display = DisplayStyle.None).StartingIn(3000);
            });
            _scaleSyncButton.text                 = "一括同期";
            _scaleSyncButton.style.width          = 100;
            _scaleSyncButton.style.height         = 32;
            _scaleSyncButton.style.alignSelf      = Align.Center;
            _scaleSyncButton.style.fontSize       = 13;
            _scaleSyncButton.style.paddingLeft    = 16;
            _scaleSyncButton.style.paddingRight   = 16;
            footer.Add(_scaleSyncButton);
            pane.Add(footer);

            RebuildScalePane();
            return pane;
        }

        private void RebuildScalePane()
        {
            if (_scaleBoneScroll == null) return;

            // ボーン未選択またはボーンリストに存在しなければ Hips をデフォルト選択
            if ((string.IsNullOrEmpty(_selectedBoneName) || !_avatarBones.ContainsKey(_selectedBoneName))
                && _avatarBones.ContainsKey("Hips"))
                _selectedBoneName = "Hips";

            _scaleAvatarField?.SetValueWithoutNotify(_avatarArmature);

            // Outfit armature rows
            _scaleOutfitRows.Clear();
            var outfitTargets = _targets.Where(t => t != null && !IsSourceAvatarSelf(t)).ToList();

            VisualElement rowsParent = _scaleOutfitRows;
            if (outfitTargets.Count > 1)
            {
                var foldout = new Foldout();
                foldout.text  = $"衣装 Armature ({outfitTargets.Count}件)";
                foldout.value = _scaleOutfitFoldoutExpanded;
                foldout.style.marginTop = 2;
                foldout.RegisterValueChangedCallback(evt => _scaleOutfitFoldoutExpanded = evt.newValue);
                _scaleOutfitRows.Add(foldout);
                rowsParent = foldout;
            }

            foreach (var outfit in outfitTargets)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var lbl = new Label(outfit.name);
                lbl.style.fontSize = 11;
                lbl.style.color = DimColor;
                lbl.style.width = 100;
                lbl.style.flexShrink = 0;
                lbl.style.overflow = Overflow.Hidden;
                lbl.style.whiteSpace = WhiteSpace.NoWrap;
                row.Add(lbl);

                var entry = GetOrCreateOutfitArmatureEntry(outfit);
                var currentArmature = entry.Armatures?.FirstOrDefault(a => a != null);

                var outfitField = new ObjectField();
                outfitField.objectType = typeof(Transform);
                outfitField.allowSceneObjects = true;
                outfitField.value          = currentArmature;
                outfitField.style.flexGrow  = 1;
                outfitField.style.flexShrink = 1;
                outfitField.style.minWidth  = 0;
                outfitField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
                outfitField.RegisterCallback<AttachToPanelEvent>(_ =>
                    outfitField.Query<VisualElement>().Build().ForEach(e => e.style.minWidth = 0));
                outfitField.RegisterValueChangedCallback(evt =>
                {
                    var e = GetOrCreateOutfitArmatureEntry(outfit);
                    if (e.Armatures == null) e.Armatures = new List<Transform>();
                    if (e.Armatures.Count == 0) e.Armatures.Add(null);
                    e.Armatures[0] = evt.newValue as Transform;
                    ScanBones();
                    RebuildScalePane();
                });
                row.Add(outfitField);
                rowsParent.Add(row);
            }

            // Warnings
            _scaleWarnings.Clear();
            bool hasOutfitBones = _outfitBones.Count > 0;
            bool hasAvatarBones = _avatarBones.Count > 0;

            bool isValidTarget = _targets.Count > 0 &&
                _targets.All(t => t?.GetComponent<ModularAvatarMeshSettings>() != null || IsAvatarTarget(t));
            if (!isValidTarget && _targets.Count > 0)
            {
                var w = new HelpBox("SetupOutfitした衣装、またはアバターを入れてください。", HelpBoxMessageType.Error);
                w.style.marginLeft = w.style.marginRight = 10;
                w.style.marginTop = 4;
                _scaleWarnings.Add(w);
            }
            if (!hasOutfitBones && outfitTargets.Count > 0)
            {
                var w = new HelpBox("衣装のボーンが見つかりません。", HelpBoxMessageType.Warning);
                w.style.marginLeft = w.style.marginRight = 10;
                w.style.marginTop = 4;
                _scaleWarnings.Add(w);
            }
            if (!hasAvatarBones && _avatarArmature != null)
            {
                var w = new HelpBox("素体のボーンが見つかりません。", HelpBoxMessageType.Warning);
                w.style.marginLeft = w.style.marginRight = 10;
                w.style.marginTop = 4;
                _scaleWarnings.Add(w);
            }

            // 「一括同期」の対象になれるか＝MAの厳密なマッピング基準（HasValidMergeArmature）で判定する
            // （アバターターゲットはMergeArmature不要のため、アーマチュアが解決できていれば無条件でOK）。
            // _avatarBones/_outfitBonesはBONE_ORDER（主要ボーンのみ）に絞った表示用の対応付けなので、
            // それらが空でも実際にはMergeArmatureが有効な衣装があり得る（例：単一の非定番ボーンにのみ
            // マージするアクセサリ等）。同期可否の判定に流用しない。
            var outfitsWithoutMergeArmature = new List<string>();
            bool hasSyncableOutfit = false;
            bool hasAvatarTargetWithoutBase = false;
            foreach (var outfit in outfitTargets)
            {
                var armatures = ResolveOutfitArmatures(outfit);
                if (armatures.Count == 0) continue; // 未設定は上の「衣装のボーンが見つかりません」でカバー済み

                if (IsAvatarTarget(outfit))
                {
                    if (_avatarArmature == null) hasAvatarTargetWithoutBase = true;
                    else hasSyncableOutfit = true;
                }
                else if (armatures.Any(HasValidMergeArmature))
                {
                    hasSyncableOutfit = true;
                }
                else
                {
                    outfitsWithoutMergeArmature.Add(outfit.name);
                }
            }
            if (outfitsWithoutMergeArmature.Count > 0)
            {
                var w = new HelpBox(
                    $"MA Merge Armatureが設定されていない衣装があります（{string.Join(", ", outfitsWithoutMergeArmature)}）。一括同期が効きません。",
                    HelpBoxMessageType.Warning);
                w.style.marginLeft = w.style.marginRight = 10;
                w.style.marginTop = 4;
                _scaleWarnings.Add(w);
            }
            if (hasAvatarTargetWithoutBase)
            {
                var w = new HelpBox(
                    "アバター同士のコピーには「素体 Armature」の指定が必要です。",
                    HelpBoxMessageType.Warning);
                w.style.marginLeft = w.style.marginRight = 10;
                w.style.marginTop = 4;
                _scaleWarnings.Add(w);
            }

            bool maToolsAvailable = IsMergeArmatureToolsAvailable();
            if (!maToolsAvailable)
            {
                var w = new HelpBox("Modular Avatarのバージョンが古いか非対応のため、一括同期を利用できません。", HelpBoxMessageType.Error);
                w.style.marginLeft = w.style.marginRight = 10;
                w.style.marginTop = 4;
                _scaleWarnings.Add(w);
            }

            _scaleWarnings.style.display = _scaleWarnings.childCount > 0
                ? DisplayStyle.Flex : DisplayStyle.None;

            // Bone list
            _scaleBoneScroll.Clear();
            _scaleBoneRows.Clear();
            foreach (var boneName in BONE_ORDER)
            {
                bool detected = _avatarBones.TryGetValue(boneName, out var bone) && bone != null;
                bool isSelected = boneName == _selectedBoneName;

                bool hasDiff = false;
                if (detected)
                {
                    var avatarAdjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
                    foreach (var outfitBoneMap in _outfitBones.Values)
                    {
                        if (!outfitBoneMap.TryGetValue(boneName, out var outfitBone) || outfitBone == null)
                            continue;
                        if (!Approximately(outfitBone.localScale, bone.localScale))
                            { hasDiff = true; break; }
                        if (avatarAdjuster != null)
                        {
                            var outfitAdj = outfitBone.GetComponent<ModularAvatarScaleAdjuster>();
                            if (outfitAdj == null || !Approximately(outfitAdj.Scale, avatarAdjuster.Scale))
                                { hasDiff = true; break; }
                        }
                    }
                }

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = row.style.paddingRight = 8;
                row.style.paddingTop = row.style.paddingBottom = 4;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = PaneBorderColor;
                if (isSelected)
                    row.style.backgroundColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f);

                var dot = new Label("●");
                dot.style.fontSize = 7;
                dot.style.color = detected ? new Color(0.30f, 0.75f, 0.35f) : new Color(0.35f, 0.35f, 0.35f);
                dot.style.marginRight = 5;
                dot.style.flexShrink = 0;
                row.Add(dot);

                var nameLbl = new Label(boneName);
                nameLbl.style.fontSize = 11;
                nameLbl.style.color = detected ? TextColor : DimColor;
                nameLbl.style.flexGrow = 1;
                nameLbl.style.overflow = Overflow.Hidden;
                nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
                row.Add(nameLbl);

                if (hasDiff)
                {
                    var diffDot = new Label("●");
                    diffDot.tooltip = "素体と衣装でスケールが異なります";
                    diffDot.style.fontSize    = 7;
                    diffDot.style.color       = new Color(0.95f, 0.65f, 0.10f);
                    diffDot.style.flexShrink  = 0;
                    row.Add(diffDot);
                }

                var capturedBone = boneName;
                row.RegisterCallback<MouseDownEvent>(_ =>
                {
                    if (_selectedBoneName == capturedBone) return;
                    if (_scaleBoneRows.TryGetValue(_selectedBoneName, out var oldRow))
                        oldRow.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                    _selectedBoneName = capturedBone;
                    row.style.backgroundColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f);
                    RebuildScaleBoneDetail();
                });
                row.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    if (capturedBone != _selectedBoneName)
                        row.style.backgroundColor = EditorGUIUtility.isProSkin
                            ? new Color(0.26f, 0.26f, 0.28f, 0.5f)
                            : new Color(0.78f, 0.78f, 0.80f, 0.5f);
                });
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    if (capturedBone != _selectedBoneName)
                        row.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                });

                _scaleBoneRows[boneName] = row;
                _scaleBoneScroll.Add(row);
            }

            RebuildScaleBoneDetail();

            bool canSync = hasSyncableOutfit && maToolsAvailable && !EditorApplication.isPlaying;
            _scaleSyncButton?.SetEnabled(canSync);
        }

        private void RebuildScaleBoneDetail()
        {
            if (_scaleBoneDetail == null) return;
            _scaleBoneDetail.Clear();

            if (string.IsNullOrEmpty(_selectedBoneName))
            {
                var hint = new Label("左の一覧からボーンを選択してください");
                hint.style.fontSize = 11;
                hint.style.color = DimColor;
                hint.style.unityFontStyleAndWeight = FontStyle.Italic;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.marginTop = 20;
                _scaleBoneDetail.Add(hint);
                return;
            }

            if (!_avatarBones.TryGetValue(_selectedBoneName, out var bone) || bone == null)
            {
                var hint = new Label($"「{_selectedBoneName}」は未検出です");
                hint.style.fontSize = 11;
                hint.style.color = DimColor;
                hint.style.unityFontStyleAndWeight = FontStyle.Italic;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                hint.style.marginTop = 20;
                _scaleBoneDetail.Add(hint);
                return;
            }

            // Transform (read-only)
            var transformRow = new VisualElement();
            transformRow.style.flexDirection = FlexDirection.Row;
            transformRow.style.alignItems = Align.Center;
            transformRow.style.marginBottom = 10;

            var transformLbl = new Label("Transform");
            transformLbl.style.fontSize = 11;
            transformLbl.style.color = DimColor;
            transformLbl.style.width = 90;
            transformLbl.style.flexShrink = 0;
            transformRow.Add(transformLbl);

            var transformField = new ObjectField();
            transformField.objectType = typeof(Transform);
            transformField.allowSceneObjects = true;
            transformField.value = bone;
            transformField.SetEnabled(false);
            transformField.style.flexGrow = 1;
            transformField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            transformRow.Add(transformField);
            _scaleBoneDetail.Add(transformRow);

            // Scale
            var scaleSectionLbl = new Label("Scale");
            scaleSectionLbl.style.fontSize = 11;
            scaleSectionLbl.style.color = DimColor;
            scaleSectionLbl.style.marginBottom = 3;
            _scaleBoneDetail.Add(scaleSectionLbl);

            var scaleField = new Vector3Field();
            scaleField.value = bone.localScale;
            scaleField.style.marginBottom = 10;
            scaleField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            scaleField.RegisterValueChangedCallback(evt =>
            {
                if (!Approximately(evt.newValue, bone.localScale))
                {
                    Undo.RecordObject(bone, "Change Bone Transform Scale");
                    bone.localScale = evt.newValue;
                    EditorUtility.SetDirty(bone);
                }
            });
            _scaleBoneDetail.Add(scaleField);

            // ScaleAdjuster (if exists)
            var adjuster = bone.GetComponent<ModularAvatarScaleAdjuster>();
            if (adjuster != null)
            {
                var adjSectionLbl = new Label("ScaleAdjuster");
                adjSectionLbl.style.fontSize = 11;
                adjSectionLbl.style.color = DimColor;
                adjSectionLbl.style.marginBottom = 3;
                _scaleBoneDetail.Add(adjSectionLbl);

                var adjField = new Vector3Field();
                adjField.value = adjuster.Scale;
                adjField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
                adjField.RegisterValueChangedCallback(evt =>
                    ApplyScaleAdjusterScale(adjuster, evt.newValue, true, "Change Bone ScaleAdjuster Scale"));
                _scaleBoneDetail.Add(adjField);
            }
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                   Mathf.Approximately(a.y, b.y) &&
                   Mathf.Approximately(a.z, b.z);
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

        // ── MA MergeArmatureInspectorTools（internal）へのリフレクション ──
        // 「一括同期」はMA本体の「ScaleAdjusterを一致させる」「位置をもとアバターに合わせてリセット」と
        // 同じ処理を呼び出す。両メソッドともinternalなため直接参照はできず、一度だけ解決してキャッシュする。
        private static bool _maToolsResolved;
        private static MethodInfo _maMatchScaleAdjustersMethod;
        private static MethodInfo _maForcePositionMethod;
        private static Type _maOptionsType;
        private static FieldInfo _maOptAdjustScaleField;
        private static FieldInfo _maOptAdjustRotationField;

        private static void ResolveMergeArmatureTools()
        {
            if (_maToolsResolved) return;
            _maToolsResolved = true;

            var toolsType = global::System.Type.GetType(
                "nadena.dev.modular_avatar.core.editor.MergeArmatureInspectorTools, nadena.dev.modular-avatar.core.editor");
            var optionsType = global::System.Type.GetType(
                "nadena.dev.modular_avatar.core.editor.MergeArmaturePositionResetOptions, nadena.dev.modular-avatar.core.editor");

            if (toolsType == null || optionsType == null)
            {
                foreach (var assembly in global::System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (toolsType == null)
                        toolsType = assembly.GetType("nadena.dev.modular_avatar.core.editor.MergeArmatureInspectorTools");
                    if (optionsType == null)
                        optionsType = assembly.GetType("nadena.dev.modular_avatar.core.editor.MergeArmaturePositionResetOptions");
                    if (toolsType != null && optionsType != null) break;
                }
            }

            if (toolsType == null || optionsType == null)
            {
                Debug.LogWarning("[qsToolBox] Modular Avatarの MergeArmatureInspectorTools が見つかりませんでした。一括同期は利用できません。");
                return;
            }

            _maOptionsType = optionsType;
            _maMatchScaleAdjustersMethod = toolsType.GetMethod(
                "MatchScaleAdjusters", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Transform) }, null);
            _maForcePositionMethod = toolsType.GetMethod(
                "ForcePositionToBaseAvatar", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Transform), optionsType }, null);
            _maOptAdjustScaleField = optionsType.GetField("AdjustScale", BindingFlags.Public | BindingFlags.Instance);
            _maOptAdjustRotationField = optionsType.GetField("AdjustRotation", BindingFlags.Public | BindingFlags.Instance);

            if (_maMatchScaleAdjustersMethod == null || _maForcePositionMethod == null ||
                _maOptAdjustScaleField == null || _maOptAdjustRotationField == null)
            {
                Debug.LogWarning("[qsToolBox] Modular Avatarの MergeArmatureInspectorTools のAPI形状が想定と異なります。一括同期は利用できません。");
                _maMatchScaleAdjustersMethod = null;
                _maForcePositionMethod = null;
            }
        }

        private static bool IsMergeArmatureToolsAvailable()
        {
            ResolveMergeArmatureTools();
            return _maMatchScaleAdjustersMethod != null && _maForcePositionMethod != null;
        }

        // armatureRoot配下に、有効なマージ対象を持つModularAvatarMergeArmatureが1つでもあるか（public API、リフレクション不要）。
        private static bool HasValidMergeArmature(Transform armatureRoot)
        {
            if (armatureRoot == null) return false;
            return armatureRoot.GetComponentsInChildren<ModularAvatarMergeArmature>(true)
                .Any(m => m.mergeTarget != null && m.mergeTarget.Get(m) != null);
        }

        // MAの「ScaleAdjusterを一致させる」ボタンと同じ処理。ScaleAdjusterコンポーネントの値のみ合わせる。
        private static void MatchOutfitScaleAdjusters(Transform armatureRoot)
        {
            ResolveMergeArmatureTools();
            if (_maMatchScaleAdjustersMethod == null || armatureRoot == null) return;

            try
            {
                _maMatchScaleAdjustersMethod.Invoke(null, new object[] { armatureRoot });
            }
            catch (TargetInvocationException e)
            {
                Debug.LogError($"[qsToolBox] MatchScaleAdjusters呼び出しでエラーが発生しました: {e.InnerException ?? e}");
            }
        }

        // MAの「位置をもとアバターに合わせてリセット」実行ボタンと同じ処理。位置は常にリセットされ、
        // スケール・回転は引数で指定した場合のみ合わせる。ConvertATPose/HeuristicRootScaleはMAの
        // デフォルト値（true）のまま変更しない。
        private static void ForceOutfitPositionToAvatar(Transform armatureRoot, bool adjustScale, bool adjustRotation)
        {
            ResolveMergeArmatureTools();
            if (_maForcePositionMethod == null || _maOptionsType == null || armatureRoot == null) return;

            try
            {
                var options = global::System.Activator.CreateInstance(_maOptionsType);
                _maOptAdjustScaleField.SetValue(options, adjustScale);
                _maOptAdjustRotationField.SetValue(options, adjustRotation);
                _maForcePositionMethod.Invoke(null, new object[] { armatureRoot, options });
            }
            catch (TargetInvocationException e)
            {
                Debug.LogError($"[qsToolBox] ForcePositionToBaseAvatar呼び出しでエラーが発生しました: {e.InnerException ?? e}");
            }
        }

        // ── アバター間の直接コピー（MergeArmatureコンポーネント不要） ──
        // アバター同士は通常MergeArmature関係を持たない上、AvatarObjectReference.Get()は参照を持つ
        // コンポーネント自身が属するアバターを起点に再解決する設計のため、一時的にMergeArmatureを
        // 付けても独立した2アバター間では正しく参照できない（Runtime/AvatarObjectReference.cs）。
        // そのため、MAのGetBonesMapping()/MatchScaleAdjusters/ForcePositionToBaseAvatarの中身の
        // アルゴリズムだけをコンポーネント無しで再現する。AT-poseへの変換・アーマチュア全体の
        // 腕の長さ比スケールは含めない（対象はスコープ外）。

        // MAのHeuristicBoneMapper.boneNamePatternsをそのまま移植した別名テーブル（コンポーネント不要版）。
        // 出典: https://github.com/HhotateA/AvatarModifyTools （Copyright (c) 2021 @HhotateA_xR, MIT License）
        //       https://github.com/Azukimochi/BoneRenamer （Copyright (c) 2023 Azukimochi, MIT License）
        private static readonly string[][] BONE_ALIAS_GROUPS = new[]
        {
            new[] {"Hips", "Hip", "pelvis"},
            new[]
            {
                "LeftUpperLeg", "UpperLeg_Left", "UpperLeg_L", "Leg_Left", "Leg_L", "ULeg_L", "Left leg", "LeftUpLeg",
                "UpLeg.L", "Thigh_L"
            },
            new[]
            {
                "RightUpperLeg", "UpperLeg_Right", "UpperLeg_R", "Leg_Right", "Leg_R", "ULeg_R", "Right leg",
                "RightUpLeg", "UpLeg.R", "Thigh_R"
            },
            new[]
            {
                "LeftLowerLeg", "LowerLeg_Left", "LowerLeg_L", "Knee_Left", "Knee_L", "LLeg_L", "Left knee", "LeftLeg", "leg_L", "shin.L"
            },
            new[]
            {
                "RightLowerLeg", "LowerLeg_Right", "LowerLeg_R", "Knee_Right", "Knee_R", "LLeg_R", "Right knee",
                "RightLeg", "leg_R", "shin.R"
            },
            new[] {"LeftFoot", "Foot_Left", "Foot_L", "Ankle_L", "Foot.L.001", "Left ankle", "heel.L", "heel"},
            new[] {"RightFoot", "Foot_Right", "Foot_R", "Ankle_R", "Foot.R.001", "Right ankle", "heel.R", "heel"},
            new[] {"Spine", "spine01"},
            new[] {"Chest", "Bust", "spine02", "upper_chest"},
            new[] {"Neck"},
            new[] {"Head"},
            new[] {"LeftShoulder", "Shoulder_Left", "Shoulder_L"},
            new[] {"RightShoulder", "Shoulder_Right", "Shoulder_R"},
            new[]
            {
                "LeftUpperArm", "UpperArm_Left", "UpperArm_L", "Arm_Left", "Arm_L", "UArm_L", "Left arm", "UpperLeftArm"
            },
            new[]
            {
                "RightUpperArm", "UpperArm_Right", "UpperArm_R", "Arm_Right", "Arm_R", "UArm_R", "Right arm",
                "UpperRightArm"
            },
            new[] {"LeftLowerArm", "LowerArm_Left", "LowerArm_L", "LArm_L", "Left elbow", "LeftForeArm", "Elbow_L", "forearm_L", "ForArm_L"},
            new[] {"RightLowerArm", "LowerArm_Right", "LowerArm_R", "LArm_R", "Right elbow", "RightForeArm", "Elbow_R", "forearm_R", "ForArm_R"},
            new[] {"LeftHand", "Hand_Left", "Hand_L", "Left wrist", "Wrist_L"},
            new[] {"RightHand", "Hand_Right", "Hand_R", "Right wrist", "Wrist_R"},
            new[]
            {
                "LeftToes", "Toes_Left", "Toe_Left", "ToeIK_L", "Toes_L", "Toe_L", "Foot.L.002", "Left Toe",
                "LeftToeBase"
            },
            new[]
            {
                "RightToes", "Toes_Right", "Toe_Right", "ToeIK_R", "Toes_R", "Toe_R", "Foot.R.002", "Right Toe",
                "RightToeBase"
            },
            new[] {"LeftEye", "Eye_Left", "Eye_L"},
            new[] {"RightEye", "Eye_Right", "Eye_R"},
            new[] {"Jaw"},
            new[]
            {
                "LeftThumbProximal", "ProximalThumb_Left", "ProximalThumb_L", "Thumb1_L", "ThumbFinger1_L",
                "LeftHandThumb1", "Thumb Proximal.L", "Thunb1_L", "finger01_01_L"
            },
            new[]
            {
                "LeftThumbIntermediate", "IntermediateThumb_Left", "IntermediateThumb_L", "Thumb2_L", "ThumbFinger2_L",
                "LeftHandThumb2", "Thumb Intermediate.L", "Thunb2_L", "finger01_02_L"
            },
            new[]
            {
                "LeftThumbDistal", "DistalThumb_Left", "DistalThumb_L", "Thumb3_L", "ThumbFinger3_L", "LeftHandThumb3",
                "Thumb Distal.L", "Thunb3_L", "finger01_03_L"
            },
            new[]
            {
                "LeftIndexProximal", "ProximalIndex_Left", "ProximalIndex_L", "Index1_L", "IndexFinger1_L",
                "LeftHandIndex1", "Index Proximal.L", "finger02_01_L", "f_index.01.L"
            },
            new[]
            {
                "LeftIndexIntermediate", "IntermediateIndex_Left", "IntermediateIndex_L", "Index2_L", "IndexFinger2_L",
                "LeftHandIndex2", "Index Intermediate.L", "finger02_02_L", "f_index.02.L"
            },
            new[]
            {
                "LeftIndexDistal", "DistalIndex_Left", "DistalIndex_L", "Index3_L", "IndexFinger3_L", "LeftHandIndex3",
                "Index Distal.L", "finger02_03_L", "f_index.03.L"
            },
            new[]
            {
                "LeftMiddleProximal", "ProximalMiddle_Left", "ProximalMiddle_L", "Middle1_L", "MiddleFinger1_L",
                "LeftHandMiddle1", "Middle Proximal.L", "finger03_01_L", "f_middle.01.L"
            },
            new[]
            {
                "LeftMiddleIntermediate", "IntermediateMiddle_Left", "IntermediateMiddle_L", "Middle2_L",
                "MiddleFinger2_L", "LeftHandMiddle2", "Middle Intermediate.L", "finger03_02_L", "f_middle.02.L"
            },
            new[]
            {
                "LeftMiddleDistal", "DistalMiddle_Left", "DistalMiddle_L", "Middle3_L", "MiddleFinger3_L",
                "LeftHandMiddle3", "Middle Distal.L", "finger03_03_L", "f_middle.03.L"
            },
            new[]
            {
                "LeftRingProximal", "ProximalRing_Left", "ProximalRing_L", "Ring1_L", "RingFinger1_L", "LeftHandRing1",
                "Ring Proximal.L", "finger04_01_L", "f_ring.01.L"
            },
            new[]
            {
                "LeftRingIntermediate", "IntermediateRing_Left", "IntermediateRing_L", "Ring2_L", "RingFinger2_L",
                "LeftHandRing2", "Ring Intermediate.L", "finger04_02_L", "f_ring.02.L"
            },
            new[]
            {
                "LeftRingDistal", "DistalRing_Left", "DistalRing_L", "Ring3_L", "RingFinger3_L", "LeftHandRing3",
                "Ring Distal.L", "finger04_03_L", "f_ring.03.L"
            },
            new[]
            {
                "LeftLittleProximal", "ProximalLittle_Left", "ProximalLittle_L", "Little1_L", "LittleFinger1_L",
                "LeftHandPinky1", "Little Proximal.L", "finger05_01_L", "f_pinky.01.L", "Pinky1.L"
            },
            new[]
            {
                "LeftLittleIntermediate", "IntermediateLittle_Left", "IntermediateLittle_L", "Little2_L",
                "LittleFinger2_L", "LeftHandPinky2", "Little Intermediate.L", "finger05_02_L", "f_pinky.02.L", "Pinky2.L"
            },
            new[]
            {
                "LeftLittleDistal", "DistalLittle_Left", "DistalLittle_L", "Little3_L", "LittleFinger3_L",
                "LeftHandPinky3", "Little Distal.L", "finger05_03_L", "f_pinky.03.L", "Pinky3.L"
            },
            new[]
            {
                "RightThumbProximal", "ProximalThumb_Right", "ProximalThumb_R", "Thumb1_R", "ThumbFinger1_R",
                "RightHandThumb1", "Thumb Proximal.R", "Thunb1_R", "finger01_01_R"
            },
            new[]
            {
                "RightThumbIntermediate", "IntermediateThumb_Right", "IntermediateThumb_R", "Thumb2_R",
                "ThumbFinger2_R", "RightHandThumb2", "Thumb Intermediate.R", "Thunb2_R", "finger01_02_R"
            },
            new[]
            {
                "RightThumbDistal", "DistalThumb_Right", "DistalThumb_R", "Thumb3_R", "ThumbFinger3_R",
                "RightHandThumb3", "Thumb Distal.R", "Thunb3_R", "finger01_03_R"
            },
            new[]
            {
                "RightIndexProximal", "ProximalIndex_Right", "ProximalIndex_R", "Index1_R", "IndexFinger1_R",
                "RightHandIndex1", "Index Proximal.R", "finger02_01_R", "f_index.01.R"
            },
            new[]
            {
                "RightIndexIntermediate", "IntermediateIndex_Right", "IntermediateIndex_R", "Index2_R",
                "IndexFinger2_R", "RightHandIndex2", "Index Intermediate.R", "finger02_02_R", "f_index.02.R"
            },
            new[]
            {
                "RightIndexDistal", "DistalIndex_Right", "DistalIndex_R", "Index3_R", "IndexFinger3_R",
                "RightHandIndex3", "Index Distal.R", "finger02_03_R", "f_index.03.R"
            },
            new[]
            {
                "RightMiddleProximal", "ProximalMiddle_Right", "ProximalMiddle_R", "Middle1_R", "MiddleFinger1_R",
                "RightHandMiddle1", "Middle Proximal.R", "finger03_01_R", "f_middle.01.R"
            },
            new[]
            {
                "RightMiddleIntermediate", "IntermediateMiddle_Right", "IntermediateMiddle_R", "Middle2_R",
                "MiddleFinger2_R", "RightHandMiddle2", "Middle Intermediate.R", "finger03_02_R", "f_middle.02.R"
            },
            new[]
            {
                "RightMiddleDistal", "DistalMiddle_Right", "DistalMiddle_R", "Middle3_R", "MiddleFinger3_R",
                "RightHandMiddle3", "Middle Distal.R", "finger03_03_R", "f_middle.03.R"
            },
            new[]
            {
                "RightRingProximal", "ProximalRing_Right", "ProximalRing_R", "Ring1_R", "RingFinger1_R",
                "RightHandRing1", "Ring Proximal.R", "finger04_01_R", "f_ring.01.R"
            },
            new[]
            {
                "RightRingIntermediate", "IntermediateRing_Right", "IntermediateRing_R", "Ring2_R", "RingFinger2_R",
                "RightHandRing2", "Ring Intermediate.R", "finger04_02_R", "f_ring.02.R"
            },
            new[]
            {
                "RightRingDistal", "DistalRing_Right", "DistalRing_R", "Ring3_R", "RingFinger3_R", "RightHandRing3",
                "Ring Distal.R", "finger04_03_R", "f_ring.03.R"
            },
            new[]
            {
                "RightLittleProximal", "ProximalLittle_Right", "ProximalLittle_R", "Little1_R", "LittleFinger1_R",
                "RightHandPinky1", "Little Proximal.R", "finger05_01_R", "f_pinky.01.R", "Pinky1.R"
            },
            new[]
            {
                "RightLittleIntermediate", "IntermediateLittle_Right", "IntermediateLittle_R", "Little2_R",
                "LittleFinger2_R", "RightHandPinky2", "Little Intermediate.R", "finger05_02_R", "f_pinky.02.R", "Pinky2.R"
            },
            new[]
            {
                "RightLittleDistal", "DistalLittle_Right", "DistalLittle_R", "Little3_R", "LittleFinger3_R",
                "RightHandPinky3", "Little Distal.R", "finger05_03_R", "f_pinky.03.R", "Pinky3.R"
            },
            new[] {"UpperChest", "UChest"},
        };

        private static string NormalizeAliasName(string name)
        {
            name = name.ToLowerInvariant();
            name = System.Text.RegularExpressions.Regex.Replace(name, "^bone_|[0-9 ._]", "");
            return name;
        }

        private static Dictionary<string, List<int>> _aliasNameToGroup;
        private static Dictionary<int, List<string>> _aliasGroupToNames;

        // 一度だけ構築してキャッシュ（HeuristicBoneMapperの静的コンストラクタと同じ考え方）。
        private static void EnsureAliasTablesBuilt()
        {
            if (_aliasNameToGroup != null) return;
            _aliasNameToGroup = new Dictionary<string, List<int>>();
            _aliasGroupToNames = new Dictionary<int, List<string>>();

            for (int i = 0; i < BONE_ALIAS_GROUPS.Length; i++)
            foreach (var rawName in BONE_ALIAS_GROUPS[i])
            {
                var norm = NormalizeAliasName(rawName);
                if (!_aliasNameToGroup.TryGetValue(norm, out var groups))
                    _aliasNameToGroup[norm] = groups = new List<int>();
                if (!groups.Contains(i)) groups.Add(i);

                if (!_aliasGroupToNames.TryGetValue(i, out var names))
                    _aliasGroupToNames[i] = names = new List<string>();
                if (!names.Contains(norm)) names.Add(norm);
            }
        }

        // destRootの名前が、prefix/suffixに合致していれば剥がした中身の名前を返す。合致しなければnull。
        private static string StripAffixes(string childName, string prefix, string suffix)
        {
            if (!childName.StartsWith(prefix) || !childName.EndsWith(suffix) ||
                childName.Length == prefix.Length + suffix.Length)
                return null;
            return childName.Substring(prefix.Length, childName.Length - prefix.Length - suffix.Length);
        }

        // MAのGetBonesMapping()と同じ考え方（2パス構成）でボーンを対応付ける（コンポーネント不要版）。
        // Pass1で名前の完全一致を全て確定させてから、Pass2で残りを別名テーブルでフォールバックする
        // （先に別名マッチさせると、後で見つかるはずの完全一致の相手を横取りしてしまうため、この順序が重要）。
        // prefix/suffixは当面デフォルト空文字（完全一致）のみ対応。
        private static List<(Transform source, Transform dest)> BuildDirectBoneMapping(
            Transform sourceRoot, Transform destRoot, string prefix = "", string suffix = "")
        {
            EnsureAliasTablesBuilt();
            var result = new List<(Transform, Transform)>();
            ScanHierarchy(destRoot, sourceRoot);
            return result;

            void ScanHierarchy(Transform dest, Transform source)
            {
                // このレベルのsource子を名前で索引化（既に別のMergeArmatureが乗っているものは除外）。
                var sourceByName = new Dictionary<string, Transform>();
                foreach (Transform t in source)
                {
                    if (t.GetComponent<ModularAvatarMergeArmature>() != null) continue;
                    if (!sourceByName.ContainsKey(t.name)) sourceByName[t.name] = t;
                }

                var levelPairs = new List<(Transform source, Transform dest)>();
                var fallbackCandidates = new List<Transform>();

                // Pass 1: 完全一致
                foreach (Transform t in dest)
                {
                    if (t.GetComponent<ModularAvatarMergeArmature>() != null) continue;

                    var targetName = StripAffixes(t.gameObject.name, prefix, suffix);
                    if (targetName != null && sourceByName.TryGetValue(targetName, out var exact))
                    {
                        levelPairs.Add((exact, t));
                        sourceByName.Remove(targetName);
                    }
                    else
                    {
                        fallbackCandidates.Add(t);
                    }
                }

                // Pass 2: 別名テーブルによるフォールバック（Pass1で残ったsource側からのみ選ぶ）
                var sourceByNormalized = new Dictionary<string, Transform>();
                foreach (var kv in sourceByName)
                {
                    var norm = NormalizeAliasName(kv.Key);
                    if (!sourceByNormalized.ContainsKey(norm)) sourceByNormalized[norm] = kv.Value;
                }

                foreach (var t in fallbackCandidates)
                {
                    var targetName = StripAffixes(t.gameObject.name, prefix, suffix);
                    if (targetName == null) continue;
                    if (!_aliasNameToGroup.TryGetValue(NormalizeAliasName(targetName), out var groups)) continue;

                    Transform found = null;
                    foreach (var g in groups)
                    {
                        foreach (var alias in _aliasGroupToNames[g])
                        {
                            if (sourceByNormalized.TryGetValue(alias, out found)) break;
                        }
                        if (found != null) break;
                    }
                    if (found == null) continue;

                    levelPairs.Add((found, t));
                    sourceByName.Remove(found.name);
                    sourceByNormalized.Remove(NormalizeAliasName(found.name));
                }

                foreach (var (s, d) in levelPairs)
                {
                    result.Add((s, d));
                    ScanHierarchy(d, s);
                }
            }
        }

        // MAの「ScaleAdjusterを一致させる」相当（MergeArmature不要版）。ルートペア＋対応付けの各ペアで合わせる。
        private static void MatchAvatarScaleAdjustersDirect(Transform sourceRoot, Transform destRoot)
        {
            MatchOneScaleAdjuster(sourceRoot, destRoot);
            foreach (var (source, dest) in BuildDirectBoneMapping(sourceRoot, destRoot))
                MatchOneScaleAdjuster(source, dest);
        }

        // MAのMatchScaleAdjusterローカル関数と同じ処理。ScaleAdjusterの値をコピー/追加/削除するだけで、
        // 子オブジェクトの位置補正は行わない（ApplyScaleAdjusterScaleとは違い、あえて再利用しない。
        // ScaleAdjusterの実効果はNDMFのビルド時にSkinnedMeshRendererのボーン参照を差し替える方式のため、
        // 子の位置は元々関係無く、MA本体もこの一括同期処理では子位置に触れていない）。
        private static void MatchOneScaleAdjuster(Transform sourceBone, Transform destBone)
        {
            var sourceAdj = sourceBone.GetComponent<ModularAvatarScaleAdjuster>();
            var destAdj = destBone.GetComponent<ModularAvatarScaleAdjuster>();

            if (sourceAdj == null)
            {
                if (destAdj != null)
                {
                    Undo.DestroyObjectImmediate(destAdj);
                    EditorUtility.SetDirty(destBone.gameObject);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(destBone.gameObject);
                }
                return;
            }

            if (destAdj == null)
                destAdj = Undo.AddComponent<ModularAvatarScaleAdjuster>(destBone.gameObject);
            else
                Undo.RecordObject(destAdj, "Match Avatar ScaleAdjuster");

            destAdj.Scale = sourceAdj.Scale;
            EditorUtility.SetDirty(destAdj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(destAdj);
        }

        // MAの「位置をもとアバターに合わせてリセット」相当（MergeArmature不要版、AT-pose変換・
        // ルートスケール調整は含まない）。位置は常にコピーし、スケール・回転は引数で指定した場合のみコピーする。
        private static void CopyAvatarBoneTransformsDirect(Transform sourceRoot, Transform destRoot, bool adjustScale, bool adjustRotation)
        {
            CopyOneBoneTransform(sourceRoot, destRoot, adjustScale, adjustRotation);
            foreach (var (source, dest) in BuildDirectBoneMapping(sourceRoot, destRoot))
                CopyOneBoneTransform(source, dest, adjustScale, adjustRotation);
        }

        private static void CopyOneBoneTransform(Transform source, Transform dest, bool adjustScale, bool adjustRotation)
        {
            Undo.RecordObject(dest, "Copy Avatar Bone Transform");
            dest.position = source.position;
            if (adjustScale) dest.localScale = source.localScale;
            if (adjustRotation) dest.localRotation = source.localRotation;
            EditorUtility.SetDirty(dest);
        }

        // アバタールート自身（VRCAvatarDescriptorが付いたGameObject）のlocalScaleをコピーする。
        // ボーンのScaleAdjuster/Transformとは別に、アバター全体の大きさ（ルートスケール）の差を揃えるため。
        private static void CopyAvatarRootScale(GameObject sourceAvatarRoot, GameObject destAvatarRoot)
        {
            if (sourceAvatarRoot == null || destAvatarRoot == null) return;
            Undo.RecordObject(destAvatarRoot.transform, "Copy Avatar Root Scale");
            destAvatarRoot.transform.localScale = sourceAvatarRoot.transform.localScale;
            EditorUtility.SetDirty(destAvatarRoot.transform);
        }

        // 対象自身がVRCAvatarDescriptorを持つか＝「衣装」ではなく「アバター」として扱うかの判定。
        private static bool IsAvatarTarget(GameObject target) =>
            target != null && target.GetComponent<VRCAvatarDescriptor>() != null;

        // armatureが属するアバタールート（VRCAvatarDescriptorを持つGameObject）を親方向に辿って求める。
        private static GameObject GetOwningAvatarRoot(Transform armature)
        {
            var current = armature;
            while (current != null)
            {
                var descriptor = current.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null) return descriptor.gameObject;
                current = current.parent;
            }
            return null;
        }

        // targetが素体アバター自身（_avatarArmatureの所属アバター）かどうか。
        // スキャン・一括同期の対象から除外するために使う（自分自身へのコピーを避ける）。
        private bool IsSourceAvatarSelf(GameObject target)
        {
            if (target == null || _avatarArmature == null) return false;
            return target == GetOwningAvatarRoot(_avatarArmature);
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

        // MA本体の「ScaleAdjusterを一致させる」「位置をもとアバターに合わせてリセット」を
        // 各衣装のアーマチュアRootに対して呼び出す（自前でのボーン走査・値コピーは行わない）。
        private void ApplyAvatarScalesToOutfits()
        {
            if (_targets.Count == 0)
                return;

            Undo.SetCurrentGroupName("Sync Bones");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                foreach (var target in _targets.Where(t => t != null && !IsSourceAvatarSelf(t)))
                foreach (var armature in ResolveOutfitArmatures(target))
                {
                    if (armature == null) continue;

                    if (IsAvatarTarget(target))
                    {
                        if (_avatarArmature == null) continue;

                        if (_autoSyncScaleAdjuster)
                            MatchAvatarScaleAdjustersDirect(_avatarArmature, armature);

                        if (_autoSyncScale)
                            CopyAvatarRootScale(GetOwningAvatarRoot(_avatarArmature), target);

                        if (_autoSyncScale || _autoSyncRotate)
                            CopyAvatarBoneTransformsDirect(_avatarArmature, armature, _autoSyncScale, _autoSyncRotate);
                    }
                    else
                    {
                        if (_autoSyncScaleAdjuster)
                            MatchOutfitScaleAdjusters(armature);

                        if (_autoSyncScale || _autoSyncRotate)
                            ForceOutfitPositionToAvatar(armature, _autoSyncScale, _autoSyncRotate);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[qsToolBox] Sync error: {e}");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private bool ScanBones()
        {
            _outfitBones.Clear();
            _avatarBones.Clear();
            CleanupOutfitArmatureEntries();

            FindAndSetAvatarArmature();

            if (_avatarArmature != null)
                BuildBoneMap(_avatarArmature, _avatarBones);

            foreach (var outfit in _targets.Where(t => t != null && !IsSourceAvatarSelf(t)))
            {
                var entry = GetOrCreateOutfitArmatureEntry(outfit);
                TryAutoAssignOutfitArmatureOnce(entry, outfit);
                var armatures = ResolveOutfitArmatures(outfit);
                if (armatures.Count > 0)
                {
                    var boneMap = IsAvatarTarget(outfit) && _avatarArmature != null
                        ? BuildOutfitBoneMapFromDirectMapping(armatures)
                        : BuildOutfitBoneMapFromMergeArmature(armatures);
                    if (boneMap.Count > 0)
                        _outfitBones[outfit] = boneMap;
                }
            }

            int h = unchecked(17 * 31 + (_avatarArmature?.GetInstanceID() ?? 0));
            h = unchecked(h * 31 + _avatarBones.Count);
            foreach (var kv in _outfitBones)
            {
                h = unchecked(h * 31 + kv.Key.GetInstanceID());
                h = unchecked(h * 31 + kv.Value.Count);
            }

            return HashChanged(h, ref _lastBonesHash);
        }

        // 衣装ボーンの検出はMA公式のModularAvatarMergeArmature.GetBonesMapping()（public API）のみを使う。
        // フォールバックは行わない：マッピングに無いボーンはそのまま未検出として扱う
        // （「一括同期」が実際に触るボーンと一覧・差分表示を常に一致させるため）。
        private Dictionary<string, Transform> BuildOutfitBoneMapFromMergeArmature(List<Transform> armatures)
        {
            var avatarToOutfit = new Dictionary<Transform, Transform>();
            foreach (var armature in armatures)
            {
                if (armature == null) continue;

                foreach (var mergeArmature in armature.GetComponentsInChildren<ModularAvatarMergeArmature>(true))
                {
                    if (mergeArmature.mergeTarget == null || mergeArmature.mergeTarget.Get(mergeArmature) == null)
                        continue;

                    var mapping = mergeArmature.GetBonesMapping();
                    if (mapping == null) continue;

                    foreach (var (avatarBone, outfitBone) in mapping)
                    {
                        if (avatarBone != null && outfitBone != null && !avatarToOutfit.ContainsKey(avatarBone))
                            avatarToOutfit[avatarBone] = outfitBone;
                    }
                }
            }

            var boneMap = new Dictionary<string, Transform>();
            foreach (var boneName in BONE_ORDER)
            {
                if (_avatarBones.TryGetValue(boneName, out var avatarBone) &&
                    avatarBone != null &&
                    avatarToOutfit.TryGetValue(avatarBone, out var outfitBone))
                {
                    boneMap[boneName] = outfitBone;
                }
            }
            return boneMap;
        }

        // アバターターゲット用：MergeArmatureの代わりにBuildDirectBoneMapping（名前完全一致→別名テーブル）で対応付ける。
        // 構造はBuildOutfitBoneMapFromMergeArmatureと同じで、ペア収集元だけが異なる。
        private Dictionary<string, Transform> BuildOutfitBoneMapFromDirectMapping(List<Transform> armatures)
        {
            var avatarToOutfit = new Dictionary<Transform, Transform>();
            foreach (var armature in armatures)
            {
                if (armature == null) continue;

                foreach (var (avatarBone, outfitBone) in BuildDirectBoneMapping(_avatarArmature, armature))
                {
                    if (avatarBone != null && outfitBone != null && !avatarToOutfit.ContainsKey(avatarBone))
                        avatarToOutfit[avatarBone] = outfitBone;
                }
            }

            var boneMap = new Dictionary<string, Transform>();
            foreach (var boneName in BONE_ORDER)
            {
                if (_avatarBones.TryGetValue(boneName, out var avatarBone) &&
                    avatarBone != null &&
                    avatarToOutfit.TryGetValue(avatarBone, out var outfitBone))
                {
                    boneMap[boneName] = outfitBone;
                }
            }
            return boneMap;
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

        // _targets内に複数の異なるアバターの候補がある場合、Hierarchyパネルで一番上に表示される
        // （＝ヒエラルキー順が最も早い）ものを素体として優先する。
        private Transform FindAvatarArmature()
        {
            VRCAvatarDescriptor best = null;

            foreach (var target in _targets.Where(t => t != null))
            {
                var current = target.transform;
                while (current != null)
                {
                    var descriptor = current.GetComponent<VRCAvatarDescriptor>();
                    if (descriptor != null)
                    {
                        if (best == null || CompareHierarchyOrder(descriptor.transform, best.transform) < 0)
                            best = descriptor;
                        break;
                    }
                    current = current.parent;
                }
            }

            return best != null ? FindChildByKeyword(best.transform, "armature") : null;
        }

        // シーンルートからのSiblingIndexの並びを求める（Hierarchyパネルでの表示順を再現するため）。
        private static int[] GetHierarchyPath(Transform t)
        {
            var path = new List<int>();
            while (t != null)
            {
                path.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            path.Reverse();
            return path.ToArray();
        }

        // Hierarchyパネルでの表示順を比較する（aがbより上なら負の値）。
        private static int CompareHierarchyOrder(Transform a, Transform b)
        {
            var pa = GetHierarchyPath(a);
            var pb = GetHierarchyPath(b);
            int len = Mathf.Min(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                if (pa[i] != pb[i]) return pa[i] - pb[i];
            }
            return pa.Length - pb.Length;
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
                var searchRoot = BONE_PARENT.TryGetValue(boneName, out var parentName) &&
                                 boneMap.TryGetValue(parentName, out var parent)
                                 ? parent : armature;

                var foundBone = FindChildByKeyword(searchRoot, boneName);

                if (foundBone == null && BONE_ALIASES.TryGetValue(boneName, out var aliases))
                {
                    foreach (var alias in aliases)
                    {
                        foundBone = FindChildByKeyword(searchRoot, alias);
                        if (foundBone != null) break;
                    }
                }

                if (foundBone != null)
                    boneMap[boneName] = foundBone;
            }
        }
    }
}
#endif
