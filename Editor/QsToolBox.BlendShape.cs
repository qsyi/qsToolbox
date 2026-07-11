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
        private VisualElement _blendShapePane;
        private ObjectField _blendShapeTargetField;
        private ScrollView _blendShapeComposeScroll;
        private ScrollView _blendShapeShapeScroll;
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
            _blendShapeTargetField.value           = _composeTarget;
            _blendShapeTargetField.style.flexGrow  = 1;
            _blendShapeTargetField.style.flexShrink = 1;
            _blendShapeTargetField.style.minWidth  = 0;
            _blendShapeTargetField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            _blendShapeTargetField.RegisterCallback<AttachToPanelEvent>(_ =>
                _blendShapeTargetField.Query<VisualElement>().Build().ForEach(e => e.style.minWidth = 0));
            _blendShapeTargetField.RegisterValueChangedCallback(evt =>
            {
                _composeTarget = evt.newValue as SkinnedMeshRenderer;
                ResetComposeData();
                ScanForCompose();
                RebuildBlendShapePane();
            });
            hdr.Add(_blendShapeTargetField);
            pane.Add(hdr);

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

            var leftHd = new VisualElement();
            leftHd.style.flexDirection = FlexDirection.Row;
            leftHd.style.alignItems = Align.Center;
            leftHd.style.flexShrink = 0;
            leftHd.style.paddingLeft = leftHd.style.paddingRight = 8;
            leftHd.style.paddingTop = leftHd.style.paddingBottom = 5;
            leftHd.style.borderBottomWidth = 1;
            leftHd.style.borderBottomColor = PaneBorderColor;
            var leftHdLbl = new Label("合成リスト");
            leftHdLbl.style.fontSize = 11;
            leftHdLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            leftHd.Add(leftHdLbl);
            leftPane.Add(leftHd);

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

            var rightHd = new VisualElement();
            rightHd.style.flexDirection = FlexDirection.Row;
            rightHd.style.alignItems = Align.Center;
            rightHd.style.flexShrink = 0;
            rightHd.style.paddingLeft = rightHd.style.paddingRight = 8;
            rightHd.style.paddingTop = rightHd.style.paddingBottom = 5;
            rightHd.style.borderBottomWidth = 1;
            rightHd.style.borderBottomColor = PaneBorderColor;
            var rightHdLbl = new Label("シェイプキー一覧");
            rightHdLbl.style.fontSize = 11;
            rightHdLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            rightHd.Add(rightHdLbl);
            rightPane.Add(rightHd);

            var searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.alignItems = Align.Center;
            searchRow.style.flexShrink = 0;
            searchRow.style.paddingLeft = searchRow.style.paddingRight = 8;
            searchRow.style.paddingTop = searchRow.style.paddingBottom = 4;
            searchRow.style.borderBottomWidth = 1;
            searchRow.style.borderBottomColor = PaneBorderColor;

            // Wrap in overflow:Hidden so TextField's internal min-width can't push the clear button off-screen
            var searchWrap = new VisualElement();
            searchWrap.style.flexGrow = 1;
            searchWrap.style.flexShrink = 1;
            searchWrap.style.minWidth = new StyleLength(0f);
            searchWrap.style.overflow = Overflow.Hidden;

            var searchField = new TextField();
            searchField.value = _composeSearchText;
            searchField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            searchField.RegisterValueChangedCallback(evt =>
            {
                _composeSearchText = evt.newValue;
                RefreshBlendShapeShapeList();
            });
            searchWrap.Add(searchField);
            searchRow.Add(searchWrap);

            var clearSearchBtn = new Button(() =>
            {
                _composeSearchText = "";
                searchField.SetValueWithoutNotify("");
                RefreshBlendShapeShapeList();
            });
            clearSearchBtn.text = "✕";
            clearSearchBtn.style.width = 20;
            clearSearchBtn.style.height = 20;
            clearSearchBtn.style.fontSize = 10;
            clearSearchBtn.style.paddingLeft = clearSearchBtn.style.paddingRight = 2;
            clearSearchBtn.style.paddingTop = clearSearchBtn.style.paddingBottom = 2;
            clearSearchBtn.style.marginLeft = 4;
            clearSearchBtn.style.flexShrink = 0;
            searchRow.Add(clearSearchBtn);
            rightPane.Add(searchRow);

            _blendShapeShapeScroll = new ScrollView();
            _blendShapeShapeScroll.style.flexGrow  = 1;
            _blendShapeShapeScroll.style.minHeight = 0;
            rightPane.Add(_blendShapeShapeScroll);
            body.Add(rightPane);
            pane.Add(body);

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

            var overwriteToggle = new Toggle("上書き");
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
                RebuildBlendShapePane();
            });
            overwriteRow.Add(overwriteToggle);

            _blendShapeOverwriteTarget = new Label();
            _blendShapeOverwriteTarget.style.fontSize = 13;
            _blendShapeOverwriteTarget.style.color = DimColor;
            _blendShapeOverwriteTarget.style.marginLeft = 8;
            _blendShapeOverwriteTarget.style.flexGrow = 1;
            overwriteRow.Add(_blendShapeOverwriteTarget);

            _blendShapeNewNameField = new TextField();
            _blendShapeNewNameField.style.flexGrow = 1;
            _blendShapeNewNameField.style.fontSize = 13;
            _blendShapeNewNameField.style.marginLeft = 8;
            _blendShapeNewNameField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            _blendShapeNewNameField.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var inp = _blendShapeNewNameField.Q(className: "unity-base-field__input");
                if (inp != null) inp.style.minWidth = new StyleLength(0f);
            });
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
                RebuildBlendShapePane();
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
            pane.Add(footer);

            _blendShapeExecuteButton = new Button(() =>
            {
                ExecuteShapeCompose();
                RebuildBlendShapePane();
            });
            _blendShapeExecuteButton.text = "合成実行";
            _blendShapeExecuteButton.style.position = Position.Absolute;
            _blendShapeExecuteButton.style.right = 10;
            _blendShapeExecuteButton.style.bottom = 10;
            _blendShapeExecuteButton.style.width = 80;
            _blendShapeExecuteButton.style.height = 36;
            _blendShapeExecuteButton.style.fontSize = 12;
            pane.Add(_blendShapeExecuteButton);

            RebuildBlendShapePane();
            return pane;
        }

        private void RebuildBlendShapePane()
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
                dot.style.color = new Color(0.30f, 0.75f, 0.35f);
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
                RebuildBlendShapePane();
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

        private const int SHAPE_LIST_MAX = 150;

        private void RefreshBlendShapeShapeList()
        {
            if (_blendShapeShapeScroll == null) return;
            _blendShapeShapeScroll.Clear();

            if (_shapeNames.Count == 0)
            {
                var empty = new Label(_composeTarget == null ? "対象メッシュを選択してください" : "シェイプキーがありません");
                empty.style.fontSize = 11;
                empty.style.color = DimColor;
                empty.style.unityFontStyleAndWeight = FontStyle.Italic;
                empty.style.marginTop = 12;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                _blendShapeShapeScroll.Add(empty);
                return;
            }

            bool hasSearch = !string.IsNullOrEmpty(_composeSearchText);
            int shown = 0;
            int total = 0;
            foreach (var shapeName in _shapeNames)
            {
                if (hasSearch && !shapeName.Contains(_composeSearchText, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                total++;
                if (shown < SHAPE_LIST_MAX)
                {
                    _blendShapeShapeScroll.Add(BuildBlendShapeShapeRow(shapeName));
                    shown++;
                }
            }

            if (total == 0 && hasSearch)
            {
                var noMatch = new Label($"「{_composeSearchText}」に一致するシェイプキーがありません");
                noMatch.style.fontSize = 11;
                noMatch.style.color = DimColor;
                noMatch.style.unityFontStyleAndWeight = FontStyle.Italic;
                noMatch.style.marginTop = 12;
                noMatch.style.unityTextAlign = TextAnchor.MiddleCenter;
                noMatch.style.whiteSpace = WhiteSpace.Normal;
                _blendShapeShapeScroll.Add(noMatch);
            }
            else if (total > SHAPE_LIST_MAX)
            {
                var overflow = new Label($"... 他 {total - shown} 件 ／ 検索で絞り込んでください");
                overflow.style.fontSize = 10;
                overflow.style.color = DimColor;
                overflow.style.unityFontStyleAndWeight = FontStyle.Italic;
                overflow.style.marginTop = 6;
                overflow.style.marginBottom = 6;
                overflow.style.unityTextAlign = TextAnchor.MiddleCenter;
                overflow.style.whiteSpace = WhiteSpace.Normal;
                _blendShapeShapeScroll.Add(overflow);
            }
        }

        private VisualElement BuildBlendShapeShapeRow(string shapeName)
        {
            bool isBase  = shapeName == _baseShapeName;
            bool isAdded = _composeShapes.Any(s => s.name == shapeName);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = row.style.paddingRight = 8;
            row.style.paddingTop = row.style.paddingBottom = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = PaneBorderColor;

            var dot = new Label("●");
            dot.style.fontSize = 8;
            dot.style.color = isBase  ? new Color(0.30f, 0.75f, 0.35f)
                            : isAdded ? new Color(0.30f, 0.60f, 1.00f)
                            : new Color(0.35f, 0.35f, 0.35f);
            dot.style.marginRight = 5;
            dot.style.flexShrink = 0;
            row.Add(dot);

            var nameLbl = new Label(shapeName);
            nameLbl.style.fontSize = 11;
            nameLbl.style.color = TextColor;
            nameLbl.style.flexGrow = 1;
            nameLbl.style.flexShrink = 1;
            nameLbl.style.minWidth = new StyleLength(0f);
            nameLbl.style.overflow = Overflow.Hidden;
            nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(nameLbl);

            if (isBase)
            {
                var activeBaseBtn = new Button(() =>
                {
                    _baseShapeName = "";
                    if (_overwriteShape) _newShapeName = "";
                    RebuildBlendShapePane();
                });
                activeBaseBtn.text = "✓ベース";
                activeBaseBtn.style.fontSize = 10;
                activeBaseBtn.style.height = 20;
                activeBaseBtn.style.paddingLeft = activeBaseBtn.style.paddingRight = 5;
                activeBaseBtn.style.paddingTop = activeBaseBtn.style.paddingBottom = 1;
                activeBaseBtn.style.flexShrink = 0;
                activeBaseBtn.style.backgroundColor = new Color(0.25f, 0.65f, 0.30f, 0.20f);
                activeBaseBtn.style.color = new Color(0.25f, 0.75f, 0.30f);
                row.Add(activeBaseBtn);
            }
            else
            {
                var baseBtn = new Button(() =>
                {
                    _baseShapeName = shapeName;
                    if (_overwriteShape) _newShapeName = shapeName;
                    RebuildBlendShapePane();
                });
                baseBtn.text = "ベース";
                baseBtn.style.fontSize = 10;
                baseBtn.style.height = 20;
                baseBtn.style.paddingLeft = baseBtn.style.paddingRight = 5;
                baseBtn.style.paddingTop = baseBtn.style.paddingBottom = 1;
                baseBtn.style.flexShrink = 0;
                row.Add(baseBtn);
            }

            var addBtn = new Button(() =>
            {
                _composeShapes.Add((shapeName, 0f));
                RebuildBlendShapePane();
            });
            addBtn.text = "＋追加";
            addBtn.style.fontSize = 10;
            addBtn.style.height = 20;
            addBtn.style.paddingLeft = addBtn.style.paddingRight = 5;
            addBtn.style.paddingTop = addBtn.style.paddingBottom = 1;
            addBtn.style.marginLeft = 4;
            addBtn.style.flexShrink = 0;
            row.Add(addBtn);

            return row;
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
                var smr = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(s => s.sharedMesh?.blendShapeCount > 0);
                if (smr != null) return smr;
            }
            return null;
        }
    }
}
#endif
