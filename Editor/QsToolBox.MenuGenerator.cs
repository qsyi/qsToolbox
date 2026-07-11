#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
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
        private string _menuFolderName = "";

        [System.Serializable]
        private class MenuMeshEntry
        {
            public SkinnedMeshRenderer Renderer;
            public bool Include;
        }

        // 既存メニュー階層の「Childrenフォルダ」1件（MA Menu ItemのSub MenuがChildren方式のもの）
        private class MenuFolderNode
        {
            public string Label;
            public GameObject FolderObject;
            public List<MenuFolderNode> Children = new List<MenuFolderNode>();
        }

        // 既存メニュー階層で見つかったVRCExpressionsMenuアセット参照先（MA Menu ItemのMenuAsset方式、またはMA Menu InstallerのinstallTargetMenu経由）
        private class MenuAssetTarget
        {
            public VRCExpressionsMenu Asset;
            public string FoundVia;
        }

        // 設置先ピッカーの選択結果。両方nullならルート（既定の場所）を意味する。
        private class MenuGenerateDestination
        {
            public GameObject ChildrenFolderTarget;
            public VRCExpressionsMenu AssetTarget;
        }

        // メニュー生成の設置先を選ぶ別ウィンドウ。既存のChildrenフォルダ階層とアセット参照先を一覧表示する。
        private class MenuFolderPickerWindow : EditorWindow
        {
            private List<MenuFolderNode> _folders;
            private List<MenuAssetTarget> _assetTargets;
            private Action<MenuGenerateDestination> _onPick;

            public static void Open(List<MenuFolderNode> folders, List<MenuAssetTarget> assetTargets,
                Action<MenuGenerateDestination> onPick)
            {
                var win = CreateInstance<MenuFolderPickerWindow>();
                win._folders      = folders;
                win._assetTargets = assetTargets;
                win._onPick       = onPick;
                win.titleContent  = new GUIContent("設置先を選択");
                win.minSize = new Vector2(320, 360);
                win.maxSize = new Vector2(420, 700);
                win.ShowUtility();
            }

            public void CreateGUI()
            {
                var root = rootVisualElement;
                root.style.paddingLeft   = root.style.paddingRight  = 10;
                root.style.paddingTop    = root.style.paddingBottom = 10;
                root.style.flexGrow      = 1;

                var hintLbl = new Label("設置先を選んで下部の「生成」を押してください（選択中の項目をもう一度クリックしても生成されます）");
                hintLbl.style.fontSize     = 11;
                hintLbl.style.color        = DimColor;
                hintLbl.style.marginBottom = 8;
                hintLbl.style.whiteSpace   = WhiteSpace.Normal;
                root.Add(hintLbl);

                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.flexGrow       = 1;
                scroll.style.minHeight      = 0;
                scroll.style.borderTopWidth = scroll.style.borderBottomWidth = 1;
                scroll.style.borderTopColor = scroll.style.borderBottomColor = PaneBorderColor;

                // 未選択（＝ルート）を初期選択状態にしておく
                var selectedDest = new MenuGenerateDestination();
                VisualElement selectedRow = null;

                void Confirm(MenuGenerateDestination dest)
                {
                    _onPick?.Invoke(dest);
                    Close();
                }

                void SelectRow(VisualElement row, MenuGenerateDestination dest)
                {
                    if (selectedRow != null)
                        selectedRow.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                    selectedRow  = row;
                    selectedDest = dest;
                    row.style.backgroundColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.25f);
                }

                VisualElement MakeRow(string label, int depth, MenuGenerateDestination dest, Label chevron)
                {
                    var row = new VisualElement();
                    row.style.flexDirection     = FlexDirection.Row;
                    row.style.alignItems        = Align.Center;
                    row.style.paddingLeft       = 8 + depth * 16;
                    row.style.paddingRight      = 8;
                    row.style.paddingTop        = 6;
                    row.style.paddingBottom     = 6;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = PaneBorderColor;

                    if (chevron != null)
                        row.Add(chevron);

                    var lbl = new Label(label);
                    lbl.style.fontSize = 11;
                    lbl.style.color    = TextColor;
                    lbl.style.flexGrow = 1;
                    row.Add(lbl);

                    // 1回目のクリックで選択、選択中の項目をもう一度クリックすると確定して生成する
                    row.RegisterCallback<MouseDownEvent>(_ =>
                    {
                        if (selectedRow == row) Confirm(dest);
                        else                    SelectRow(row, dest);
                    });
                    row.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        if (selectedRow != row)
                            row.style.backgroundColor = EditorGUIUtility.isProSkin
                                ? new Color(0.26f, 0.26f, 0.28f, 1f)
                                : new Color(0.82f, 0.82f, 0.84f, 1f);
                    });
                    row.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        if (selectedRow != row)
                            row.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                    });
                    return row;
                }

                // 階層のわかりやすさのため▶/▼で開閉できるようにする。第一階層（トップレベルのフォルダ）は常に表示し、
                // それより深い階層は初期状態で閉じておく。
                void AddFolderRows(VisualElement container, List<MenuFolderNode> nodes, int depth)
                {
                    foreach (var node in nodes)
                    {
                        bool hasChildren = node.Children.Count > 0;
                        bool isOpen = false;

                        Label chevron = null;
                        VisualElement childContainer = null;
                        if (hasChildren)
                        {
                            chevron = new Label("▶");
                            chevron.style.fontSize    = 9;
                            chevron.style.color       = DimColor;
                            chevron.style.width       = 14;
                            chevron.style.flexShrink  = 0;
                            chevron.style.unityTextAlign = TextAnchor.MiddleCenter;
                        }
                        else
                        {
                            chevron = new Label("");
                            chevron.style.width      = 14;
                            chevron.style.flexShrink = 0;
                        }

                        var row = MakeRow(node.Label, depth,
                            new MenuGenerateDestination { ChildrenFolderTarget = node.FolderObject },
                            chevron);
                        container.Add(row);

                        if (hasChildren)
                        {
                            childContainer = new VisualElement();
                            childContainer.style.display = DisplayStyle.None;
                            container.Add(childContainer);
                            AddFolderRows(childContainer, node.Children, depth + 1);

                            chevron.RegisterCallback<MouseDownEvent>(evt =>
                            {
                                isOpen = !isOpen;
                                chevron.text = isOpen ? "▼" : "▶";
                                childContainer.style.display = isOpen ? DisplayStyle.Flex : DisplayStyle.None;
                                evt.StopPropagation();
                            });
                        }
                    }
                }

                var rootRow = MakeRow("ルート（指定しない）", 0, new MenuGenerateDestination(), null);
                scroll.Add(rootRow);
                SelectRow(rootRow, new MenuGenerateDestination());
                AddFolderRows(scroll, _folders, 1);

                if (_assetTargets.Count > 0)
                {
                    var assetHdr = new Label("メニューアセット");
                    assetHdr.style.fontSize                = 10;
                    assetHdr.style.color                   = DimColor;
                    assetHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
                    assetHdr.style.paddingLeft             = 8;
                    assetHdr.style.paddingTop              = 8;
                    assetHdr.style.paddingBottom           = 2;
                    scroll.Add(assetHdr);

                    foreach (var target in _assetTargets)
                    {
                        var asset = target.Asset;
                        scroll.Add(MakeRow($"{asset.name}（{target.FoundVia}）", 1,
                            new MenuGenerateDestination { AssetTarget = asset }, null));
                    }
                }

                root.Add(scroll);

                var btnRow = new VisualElement();
                btnRow.style.flexDirection  = FlexDirection.Row;
                btnRow.style.justifyContent = Justify.FlexEnd;
                btnRow.style.flexShrink     = 0;
                btnRow.style.marginTop      = 8;

                var cancelBtn = new Button(Close) { text = "キャンセル" };
                cancelBtn.style.height      = 24;
                cancelBtn.style.paddingLeft = cancelBtn.style.paddingRight = 10;
                btnRow.Add(cancelBtn);

                var generateBtn = new Button(() => Confirm(selectedDest)) { text = "生成" };
                generateBtn.style.height      = 24;
                generateBtn.style.paddingLeft = generateBtn.style.paddingRight = 14;
                generateBtn.style.marginLeft  = 6;
                btnRow.Add(generateBtn);

                root.Add(btnRow);
            }
        }

        private ScrollView _menuScrollView;
        private TextField _menuFolderField;
        private Label _menuWarningLabel;
        private Button _menuGenerateButton;
        private Button _menuAllSelBtn;
        private Button _menuPreviewButton;
        private Dictionary<SkinnedMeshRenderer, bool> _menuPreviewOriginalStates;

        // チェック済みメッシュを非表示にして、生成前に見た目を確認できるようにする。もう一度押すと元に戻る。
        private void ToggleMenuPreview()
        {
            if (_menuPreviewOriginalStates != null)
            {
                RestoreMenuPreview();
                return;
            }

            var entries = GetGeneratableMenuMeshEntries();
            if (entries.Count == 0) return;

            _menuPreviewOriginalStates = new Dictionary<SkinnedMeshRenderer, bool>();
            foreach (var entry in entries)
            {
                var go = entry.Renderer.gameObject;
                _menuPreviewOriginalStates[entry.Renderer] = go.activeSelf;
                go.SetActive(false);
            }
            UpdateMenuGenerateButton();
        }

        private void RestoreMenuPreview()
        {
            if (_menuPreviewOriginalStates == null) return;

            foreach (var kv in _menuPreviewOriginalStates)
            {
                if (kv.Key != null)
                    kv.Key.gameObject.SetActive(kv.Value);
            }
            _menuPreviewOriginalStates = null;
            UpdateMenuGenerateButton();
        }

        // プレビュー中にチェックを変更したら即座に表示へ反映する
        private void ApplyMenuPreviewForEntry(MenuMeshEntry entry)
        {
            if (_menuPreviewOriginalStates == null || entry?.Renderer == null) return;

            var go = entry.Renderer.gameObject;
            if (entry.Include)
            {
                if (!_menuPreviewOriginalStates.ContainsKey(entry.Renderer))
                    _menuPreviewOriginalStates[entry.Renderer] = go.activeSelf;
                go.SetActive(false);
            }
            else if (_menuPreviewOriginalStates.TryGetValue(entry.Renderer, out var original))
            {
                go.SetActive(original);
                _menuPreviewOriginalStates.Remove(entry.Renderer);
            }
        }

        private void GenerateMenu()
        {
            RestoreMenuPreview();

            var parentTarget = FindMenuGenerationRoot();
            var entriesToGenerate = GetGeneratableMenuMeshEntries();
            if (parentTarget == null || entriesToGenerate.Count == 0)
                return;

            var folders = new List<MenuFolderNode>();
            var assetTargets = new List<MenuAssetTarget>();
            CollectMenuFolders(parentTarget.transform, folders, assetTargets, new HashSet<VRCExpressionsMenu>());

            MenuFolderPickerWindow.Open(folders, assetTargets,
                destination => GenerateMenuInto(parentTarget, entriesToGenerate, destination));
        }

        // 既存メニュー階層を辿り、Childrenフォルダ（MA Menu Item, Children方式）とアセット参照先
        // （MA Menu ItemのMenuAsset方式 / MA Menu InstallerのinstallTargetMenu）を収集する。
        private static void CollectMenuFolders(Transform scanRoot, List<MenuFolderNode> intoNodes,
            List<MenuAssetTarget> intoAssets, HashSet<VRCExpressionsMenu> seenAssets)
        {
            for (int i = 0; i < scanRoot.childCount; i++)
            {
                var child = scanRoot.GetChild(i);
                bool recursedAsFolder = false;

                var menuItem = child.GetComponent<ModularAvatarMenuItem>();
                if (menuItem != null && menuItem.PortableControl.Type == PortableControlType.SubMenu)
                {
                    if (menuItem.MenuSource == SubmenuSource.Children)
                    {
                        var node = new MenuFolderNode
                        {
                            Label = string.IsNullOrEmpty(menuItem.label) ? child.name : menuItem.label,
                            FolderObject = child.gameObject,
                        };
                        intoNodes.Add(node);
                        var childrenRoot = menuItem.menuSource_otherObjectChildren != null
                            ? menuItem.menuSource_otherObjectChildren.transform
                            : child;
                        CollectMenuFolders(childrenRoot, node.Children, intoAssets, seenAssets);
                        recursedAsFolder = true;
                    }
                    else if (menuItem.MenuSource == SubmenuSource.MenuAsset && menuItem.Control?.subMenu != null)
                    {
                        RegisterAssetTarget(menuItem.Control.subMenu, child.name, intoAssets, seenAssets);
                    }
                }

                var installer = child.GetComponent<ModularAvatarMenuInstaller>();
                if (installer != null && installer.installTargetMenu != null)
                    RegisterAssetTarget(installer.installTargetMenu, child.name, intoAssets, seenAssets);

                if (!recursedAsFolder)
                    CollectMenuFolders(child, intoNodes, intoAssets, seenAssets);
            }
        }

        private static void RegisterAssetTarget(VRCExpressionsMenu asset, string foundVia,
            List<MenuAssetTarget> intoAssets, HashSet<VRCExpressionsMenu> seenAssets)
        {
            if (!seenAssets.Add(asset)) return;
            intoAssets.Add(new MenuAssetTarget { Asset = asset, FoundVia = foundVia });
        }

        private void GenerateMenuInto(GameObject avatarRoot, List<MenuMeshEntry> entriesToGenerate,
            MenuGenerateDestination destination)
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Menu");

            try
            {
                string folderName = GetGeneratedMenuFolderName();
                var folderObject = new GameObject(folderName);
                Undo.RegisterCreatedObjectUndo(folderObject, "Generate Menu");

                var parentTransform = destination.ChildrenFolderTarget != null
                    ? destination.ChildrenFolderTarget.transform
                    : avatarRoot.transform;
                folderObject.transform.SetParent(parentTransform, false);

                var generatedItemNames = new List<string>();

                foreach (var entry in entriesToGenerate)
                {
                    string itemName = entry.Renderer.name;
                    CreateToggleObject(
                        folderObject.transform,
                        itemName,
                        new[] { entry.Renderer.gameObject },
                        new[] { false });
                    generatedItemNames.Add(itemName);
                }

                var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(folderObject);
                ConfigureFolderMenuItem(menuItem);

                // Childrenフォルダ配下は「子である」こと自体でメニューに含まれるためInstallerは不要
                if (destination.ChildrenFolderTarget == null)
                {
                    var menuInstaller = Undo.AddComponent<ModularAvatarMenuInstaller>(folderObject);
                    ConfigureMenuInstaller(menuInstaller, destination.AssetTarget);
                }

                ForceRefreshGeneratedMenu(folderObject, menuItem);

                EditorUtility.SetDirty(folderObject);
                ScanData();
                foreach (var e in _menuMeshEntries.Where(e => e?.Renderer != null))
                    e.Include = false;
                RebuildMenuPane();

                Selection.activeObject = folderObject;
                EditorGUIUtility.PingObject(folderObject);

                EditorUtility.DisplayDialog(
                    "簡易メニュー生成",
                    BuildMenuGeneratedDialogMessage(folderName, generatedItemNames),
                    "OK");
                Debug.Log($"[qsToolBox] Generated menu '{folderObject.name}' with {generatedItemNames.Count} item(s).");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private GameObject FindMenuGenerationRoot()
        {
            foreach (var target in _targets.Where(IsValidTarget))
            {
                var current = target.transform;
                while (current != null)
                {
                    if (current.GetComponent<VRCAvatarDescriptor>() != null)
                        return current.gameObject;

                    current = current.parent;
                }
            }

            return _targets.FirstOrDefault(IsValidTarget);
        }

        private string GetGeneratedMenuFolderName()
        {
            return string.IsNullOrWhiteSpace(_menuFolderName)
                ? _targets.FirstOrDefault(IsValidTarget)?.name ?? "Menu"
                : _menuFolderName.Trim();
        }

        private static void ApplyMenuItemDefaults(ModularAvatarMenuItem menuItem, PortableControlType type)
        {
            menuItem.PortableControl.Type = type;
            menuItem.PortableControl.Value = 1f;
            menuItem.PortableControl.Parameter = string.Empty;
            menuItem.PortableControl.Icon = null;
            menuItem.MenuSource = SubmenuSource.Children;
            menuItem.isSynced = true;
            menuItem.isSaved = true;
            menuItem.isDefault = false;
            menuItem.automaticValue = true;
            menuItem.label = string.Empty;
        }

        private static VisualElement RightSpacer(float width = 10f)
        {
            var s = new VisualElement();
            s.style.width      = width;
            s.style.flexShrink = 0;
            return s;
        }

        private static void MarkComponentDirty(Component c)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c.gameObject);
            EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(c.gameObject);
        }

        private void ConfigureFolderMenuItem(ModularAvatarMenuItem menuItem)
        {
            ApplyMenuItemDefaults(menuItem, PortableControlType.SubMenu);
            MarkComponentDirty(menuItem);
        }

        private void ConfigureMenuInstaller(ModularAvatarMenuInstaller menuInstaller, VRCExpressionsMenu installTargetMenu = null)
        {
            menuInstaller.menuToAppend = null;
            menuInstaller.installTargetMenu = installTargetMenu;
            MarkComponentDirty(menuInstaller);
        }

        private void CreateToggleObject(Transform parent, string objectName, IEnumerable<GameObject> targetObjects, IEnumerable<bool> activeWhenOnValues)
        {
            var toggleObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(toggleObject, "Generate Menu");
            toggleObject.transform.SetParent(parent, false);

            var childMenuItem = Undo.AddComponent<ModularAvatarMenuItem>(toggleObject);
            ConfigureChildMenuItem(childMenuItem);

            var itemToggler = Undo.AddComponent<ItemToggler>(toggleObject);
            ConfigureItemToggler(
                itemToggler,
                childMenuItem,
                objectName,
                targetObjects.Zip(activeWhenOnValues, (target, isOn) => (target, isOn))
                    .Where(pair => pair.target != null)
                    .Distinct()
                    .ToList());
            MarkComponentDirty(childMenuItem);
            PrefabUtility.RecordPrefabInstancePropertyModifications(toggleObject);
            EditorUtility.SetDirty(toggleObject);
        }

        private static void ConfigureChildMenuItem(ModularAvatarMenuItem menuItem)
        {
            ApplyMenuItemDefaults(menuItem, PortableControlType.Toggle);
        }

        private void ConfigureItemToggler(
            ItemToggler itemToggler,
            ModularAvatarMenuItem parentMenuItem,
            string menuName,
            IReadOnlyList<(GameObject target, bool activeWhenOn)> targetObjects)
        {
            var serializedObject = new SerializedObject(itemToggler);
            serializedObject.Update();

            serializedObject.FindProperty("menuName").stringValue = menuName;
            serializedObject.FindProperty("parentOverride").objectReferenceValue = null;
            serializedObject.FindProperty("icon").objectReferenceValue = null;
            serializedObject.FindProperty("parentOverrideMA").objectReferenceValue = parentMenuItem;
            serializedObject.FindProperty("isSave").boolValue = true;
            serializedObject.FindProperty("isLocalOnly").boolValue = false;
            serializedObject.FindProperty("autoFixDuplicate").boolValue = true;
            serializedObject.FindProperty("defaultValue").boolValue = false;

            var parameterProperty = serializedObject.FindProperty("parameter");
            var objectsProperty = parameterProperty.FindPropertyRelative("objects");
            objectsProperty.arraySize = targetObjects.Count;
            for (int i = 0; i < targetObjects.Count; i++)
            {
                var objectElement = objectsProperty.GetArrayElementAtIndex(i);
                objectElement.FindPropertyRelative("obj").objectReferenceValue = targetObjects[i].target;
                objectElement.FindPropertyRelative("value").boolValue = targetObjects[i].activeWhenOn;
            }

            parameterProperty.FindPropertyRelative("blendShapeModifiers").arraySize = 0;
            parameterProperty.FindPropertyRelative("materialReplacers").arraySize = 0;
            parameterProperty.FindPropertyRelative("materialPropertyModifiers").arraySize = 0;
            parameterProperty.FindPropertyRelative("clips").arraySize = 0;

            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(itemToggler);
            PrefabUtility.RecordPrefabInstancePropertyModifications(itemToggler.gameObject);
            EditorUtility.SetDirty(itemToggler);
            EditorUtility.SetDirty(itemToggler.gameObject);
        }

        private void ForceRefreshGeneratedMenu(GameObject folderObject, ModularAvatarMenuItem menuItem)
        {
            if (folderObject == null)
                return;

            var childTogglers = folderObject.GetComponentsInChildren<ItemToggler>(true);
            var childMenuItems = folderObject.GetComponentsInChildren<ModularAvatarMenuItem>(true);
            MarkGeneratedMenuDirty(folderObject, menuItem, childMenuItems, childTogglers);

            EditorApplication.delayCall += () =>
            {
                if (folderObject == null)
                    return;

                var delayedMenuItem = menuItem != null
                    ? menuItem
                    : folderObject.GetComponent<ModularAvatarMenuItem>();
                var delayedChildMenuItems = folderObject.GetComponentsInChildren<ModularAvatarMenuItem>(true);
                var delayedChildTogglers = folderObject.GetComponentsInChildren<ItemToggler>(true);
                MarkGeneratedMenuDirty(folderObject, delayedMenuItem, delayedChildMenuItems, delayedChildTogglers);
            };
        }

        private void MarkGeneratedMenuDirty(
            GameObject folderObject,
            ModularAvatarMenuItem menuItem,
            IEnumerable<ModularAvatarMenuItem> childMenuItems,
            IEnumerable<ItemToggler> childTogglers)
        {
            if (folderObject == null)
                return;

            if (menuItem != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(menuItem);
                EditorUtility.SetDirty(menuItem);
            }

            foreach (var childMenuItem in childMenuItems.Where(child => child != null))
                MarkComponentDirty(childMenuItem);

            foreach (var toggler in childTogglers.Where(toggler => toggler != null))
                MarkComponentDirty(toggler);

            PrefabUtility.RecordPrefabInstancePropertyModifications(folderObject);
            EditorUtility.SetDirty(folderObject);
            EditorSceneManager.MarkSceneDirty(folderObject.scene);
        }

        private string BuildMenuGeneratedDialogMessage(string folderName, IReadOnlyList<string> generatedItemNames)
        {
            var lines = new List<string> { folderName };
            lines.AddRange(generatedItemNames.Select(name => $"・{name}"));
            lines.Add(string.Empty);
            lines.Add("メニューを生成しました");
            return string.Join("\n", lines);
        }

        private VisualElement BuildMenuPane()
        {
            var pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1;

            // ── Header ──────────────────────────────────────────────
            var hdr = new VisualElement();
            hdr.style.flexDirection   = FlexDirection.Row;
            hdr.style.alignItems      = Align.Center;
            hdr.style.paddingLeft     = 10;
            hdr.style.paddingRight    = 10;
            hdr.style.paddingTop      = 7;
            hdr.style.paddingBottom   = 7;
            hdr.style.borderBottomWidth = 1;
            hdr.style.borderBottomColor = PaneBorderColor;

            var folderLbl = new Label("フォルダ名");
            folderLbl.style.fontSize    = 11;
            folderLbl.style.color       = DimColor;
            folderLbl.style.marginRight = 6;
            folderLbl.style.flexShrink  = 0;
            hdr.Add(folderLbl);

            _menuFolderField = new TextField();
            _menuFolderField.value           = _menuFolderName;
            _menuFolderField.style.flexGrow  = 1;
            _menuFolderField.style.flexShrink = 1;
            _menuFolderField.style.minWidth  = 0;
            _menuFolderField.style.marginRight = 8;
            _menuFolderField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            _menuFolderField.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var inp = _menuFolderField.Q(className: "unity-base-field__input");
                if (inp != null) inp.style.minWidth = 0;
            });
            _menuFolderField.RegisterValueChangedCallback(evt =>
            {
                _menuFolderName = evt.newValue;
                EditorUtility.SetDirty(this);
                UpdateMenuGenerateButton();
            });
            hdr.Add(_menuFolderField);
            pane.Add(hdr);

            // ── 選択ツールバー ─────────────────────────────────────
            var toolbar = new VisualElement();
            toolbar.style.flexDirection   = FlexDirection.Row;
            toolbar.style.alignItems      = Align.Center;
            toolbar.style.paddingLeft     = 8;
            toolbar.style.paddingRight    = 8;
            toolbar.style.paddingTop      = 4;
            toolbar.style.paddingBottom   = 4;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = PaneBorderColor;
            toolbar.style.flexShrink        = 0;

            _menuAllSelBtn = new Button(() =>
            {
                var entries = _menuMeshEntries.Where(e => e?.Renderer != null).ToList();
                bool allSel = entries.Count > 0 && entries.All(e => e.Include);
                foreach (var e in entries) e.Include = !allSel;
                EditorUtility.SetDirty(this);
                RebuildMenuPane();
            });
            _menuAllSelBtn.style.fontSize    = 10;
            _menuAllSelBtn.style.height      = 22;
            _menuAllSelBtn.style.paddingLeft = _menuAllSelBtn.style.paddingRight = 8;
            RefreshMenuAllSelBtn();
            toolbar.Add(_menuAllSelBtn);

            pane.Add(toolbar);

            // ── Entry list ──────────────────────────────────────────
            _menuScrollView = new ScrollView();
            _menuScrollView.style.flexGrow  = 1;
            _menuScrollView.style.minHeight = 0;
            pane.Add(_menuScrollView);

            // ── Footer ──────────────────────────────────────────────
            var footer = new VisualElement();
            footer.style.paddingLeft    = 10;
            footer.style.paddingRight   = 10;
            footer.style.paddingTop     = 8;
            footer.style.paddingBottom  = 8;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = PaneBorderColor;
            footer.style.flexShrink     = 0;

            _menuWarningLabel = new Label();
            _menuWarningLabel.style.fontSize     = 11;
            _menuWarningLabel.style.color        = new Color(0.85f, 0.60f, 0.15f);
            _menuWarningLabel.style.marginBottom = 6;
            _menuWarningLabel.style.display      = DisplayStyle.None;
            footer.Add(_menuWarningLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.alignItems    = Align.Stretch;
            btnRow.style.minHeight     = 36;

            _menuPreviewButton = new Button(ToggleMenuPreview);
            _menuPreviewButton.style.flexGrow    = 0;
            _menuPreviewButton.style.width       = 100;
            _menuPreviewButton.style.marginRight = 6;
            _menuPreviewButton.style.fontSize    = 13;
            btnRow.Add(_menuPreviewButton);

            _menuGenerateButton = new Button(GenerateMenu);
            _menuGenerateButton.style.flexGrow = 1;
            _menuGenerateButton.style.fontSize = 13;
            btnRow.Add(_menuGenerateButton);

            footer.Add(btnRow);
            pane.Add(footer);

            RebuildMenuPane();
            return pane;
        }

        private void RebuildMenuPane()
        {
            if (_menuScrollView == null) return;

            if (_menuFolderField != null && _menuFolderField.value != _menuFolderName)
                _menuFolderField.SetValueWithoutNotify(_menuFolderName);

            _menuScrollView.Clear();

            bool hasEntries = _menuMeshEntries.Any(e => e?.Renderer != null);

            if (!hasEntries)
            {
                var empty = new Label("探索対象配下にメッシュが見つかりません。");
                empty.style.fontSize = 11;
                empty.style.color    = new Color(0.50f, 0.50f, 0.50f);
                empty.style.unityFontStyleAndWeight = FontStyle.Italic;
                empty.style.marginTop    = 16;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                _menuScrollView.Add(empty);
            }
            else
            {
                foreach (var entry in _menuMeshEntries)
                {
                    if (entry?.Renderer == null) continue;
                    _menuScrollView.Add(BuildMenuEntryCard(entry));
                }
            }

            UpdateMenuGenerateButton();
            RefreshMenuAllSelBtn();
        }

        private void RefreshMenuAllSelBtn()
        {
            if (_menuAllSelBtn == null) return;
            var entries = _menuMeshEntries.Where(e => e?.Renderer != null).ToList();
            bool allSel = entries.Count > 0 && entries.All(e => e.Include);
            _menuAllSelBtn.text = allSel ? "全解除" : "全選択";
        }

        private VisualElement BuildMenuEntryCard(MenuMeshEntry entry)
        {
            var card = new VisualElement();
            card.style.flexDirection    = FlexDirection.Row;
            card.style.alignItems       = Align.Center;
            card.style.paddingLeft      = 8;
            card.style.paddingRight     = 8;
            card.style.paddingTop       = 6;
            card.style.paddingBottom    = 6;
            card.style.borderBottomWidth = 1;
            card.style.borderBottomColor = PaneBorderColor;

            // Include toggle
            var toggle = new Toggle();
            toggle.value = entry.Include;
            toggle.style.marginRight = 8;
            toggle.style.flexShrink  = 0;
            card.Add(toggle);

            // Info column
            var info = new VisualElement();
            info.style.flexGrow   = 1;
            info.style.flexShrink = 1;

            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;

            var nameLbl = new Label(entry.Renderer.name);
            nameLbl.style.fontSize = 12;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.color    = entry.Include ? TextColor : DimColor;
            nameLbl.style.flexGrow = 1;
            nameRow.Add(nameLbl);
            info.Add(nameRow);

            // コールバックは nameLbl 宣言後に登録
            toggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(this, "Toggle Menu Entry");
                entry.Include = evt.newValue;
                EditorUtility.SetDirty(this);
                nameLbl.style.color = entry.Include ? TextColor : DimColor;
                ApplyMenuPreviewForEntry(entry);
                UpdateMenuGenerateButton();
                RefreshMenuAllSelBtn();
            });

            card.Add(info);

            // Renderer ObjectField (disabled, ping on click)
            var rendWrap = new VisualElement();
            rendWrap.style.width     = 140;
            rendWrap.style.flexShrink = 0;
            rendWrap.style.marginLeft = 8;
            var rendField = new ObjectField();
            rendField.objectType        = typeof(SkinnedMeshRenderer);
            rendField.allowSceneObjects = true;
            rendField.value             = entry.Renderer;
            rendField.label             = "";
            rendField.style.flexGrow    = 1;
            rendField.SetEnabled(false);
            rendField.Q<Label>(className: "unity-base-field__label")?.RemoveFromHierarchy();
            rendWrap.Add(rendField);

            // Overlay to capture clicks — disabled ObjectField doesn't reliably bubble events
            var selOverlay = new VisualElement();
            selOverlay.style.position = Position.Absolute;
            selOverlay.style.left = selOverlay.style.top = selOverlay.style.right = selOverlay.style.bottom = 0;
            selOverlay.RegisterCallback<MouseDownEvent>(evt =>
            {
                Selection.activeObject = entry.Renderer.gameObject;
                evt.StopPropagation();
            });
            rendWrap.Add(selOverlay);
            card.Add(rendWrap);

            // Card-level select+ping; toggle stops propagation to avoid double-fire
            card.RegisterCallback<MouseDownEvent>(_ =>
            {
                Selection.activeObject = entry.Renderer.gameObject;
                EditorGUIUtility.PingObject(entry.Renderer);
            });
            toggle.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());

            return card;
        }

        private void UpdateMenuGenerateButton()
        {
            if (_menuGenerateButton == null) return;
            var generatable = GetGeneratableMenuMeshEntries();
            bool canGenerate = !string.IsNullOrWhiteSpace(_menuFolderName) && generatable.Count > 0;
            _menuGenerateButton.SetEnabled(canGenerate);

            if (_menuWarningLabel != null)
            {
                _menuWarningLabel.text    = canGenerate ? "" : GetMenuGenerateWarningMessage();
                _menuWarningLabel.style.display = canGenerate ? DisplayStyle.None : DisplayStyle.Flex;
            }

            _menuGenerateButton.text = generatable.Count > 0 ? $"生成 ({generatable.Count}件)" : "生成";

            if (_menuPreviewButton != null)
            {
                bool previewActive = _menuPreviewOriginalStates != null;
                _menuPreviewButton.text = previewActive ? "プレビュー解除" : "プレビュー";
                _menuPreviewButton.SetEnabled(previewActive || generatable.Count > 0);
                _menuPreviewButton.style.backgroundColor = previewActive
                    ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f)
                    : new StyleColor(StyleKeyword.Null);
            }
        }

        private bool ScanMenuMeshEntries()
        {
            EnsureDefaultMenuFolderName();

            var previousSettings = _menuMeshEntries
                .Where(entry => entry != null && IsValidMenuRenderer(entry.Renderer))
                .GroupBy(entry => entry.Renderer)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Include);

            var scannedRenderers = _targets
                .Where(IsValidTarget)
                .SelectMany(target => target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                .Where(IsValidMenuRenderer)
                .Distinct()
                .ToList();

            var existingEntries = _menuMeshEntries
                .Where(entry => entry != null)
                .ToList();

            existingEntries.RemoveAll(e => e.Renderer == null || !scannedRenderers.Contains(e.Renderer));

            bool changed = false;
            foreach (var renderer in scannedRenderers)
            {
                bool alreadyExists = existingEntries.Any(entry => entry.Renderer == renderer);
                if (alreadyExists)
                    continue;

                previousSettings.TryGetValue(renderer, out bool include);

                existingEntries.Add(new MenuMeshEntry
                {
                    Renderer = renderer,
                    Include = include
                });
                changed = true;
            }

            // 削除があった場合も変化あり
            if (!changed && existingEntries.Count != _menuMeshEntries.Count)
                changed = true;

            _menuMeshEntries.Clear();
            _menuMeshEntries.AddRange(existingEntries);
            return changed;
        }

        private void EnsureDefaultMenuFolderName()
        {
            if (!string.IsNullOrWhiteSpace(_menuFolderName))
                return;

            var defaultTarget = _targets.FirstOrDefault(IsValidTarget);
            if (defaultTarget != null)
                _menuFolderName = defaultTarget.name;
        }

        private List<MenuMeshEntry> GetGeneratableMenuMeshEntries()
        {
            return _menuMeshEntries
                .Where(entry => entry != null && entry.Include && IsValidMenuRenderer(entry.Renderer))
                .GroupBy(entry => entry.Renderer)
                .Select(group => group.First())
                .ToList();
        }

        private bool IsValidMenuRenderer(SkinnedMeshRenderer renderer)
        {
            return renderer != null &&
                renderer.sharedMesh != null &&
                IsValidTarget(renderer.gameObject);
        }

        private string GetMenuGenerateWarningMessage()
        {
            if (string.IsNullOrWhiteSpace(_menuFolderName))
                return "フォルダ名を入力してください。";

            return "メニューを生成するには、含めるメッシュを 1 つ以上チェックしてください。";
        }
    }
}
#endif
