#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditorInternal;

namespace qsyi
{
    [System.Serializable]
    public class BookmarkData
    {
        public string name;
        public string path;
        public string guid;

        public BookmarkData(string name, string path, string guid)
        {
            this.name = name;
            this.path = path;
            this.guid = guid;
        }
    }

    [System.Serializable]
    public class BookmarkManager
    {
        private const string PrefsKey = "qsyi.FolderBookmarks";

        // Fix 3: 変更通知イベント
        public static event Action OnBookmarksChanged;

        [SerializeField]
        private List<BookmarkData> bookmarks = new List<BookmarkData>();

        public List<BookmarkData> Bookmarks => bookmarks;

        private static BookmarkManager _instance;
        public static BookmarkManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = LoadBookmarks();
                return _instance;
            }
        }

        public void AddBookmark(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return;
            if (folderPath == "Assets")
                return;

            string guid = AssetDatabase.AssetPathToGUID(folderPath);
            if (bookmarks.Any(b => b.guid == guid))
                return;

            string folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(folderName))
                folderName = "Assets";

            bookmarks.Add(new BookmarkData(folderName, folderPath, guid));
            SaveBookmarks();
        }

        public void RemoveBookmark(string guid)
        {
            bookmarks.RemoveAll(b => b.guid == guid);
            SaveBookmarks();
        }

        public void RemoveBookmark(BookmarkData bookmark)
        {
            bookmarks.Remove(bookmark);
            SaveBookmarks();
        }

        public void ReorderBookmarks(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= bookmarks.Count || toIndex < 0 || toIndex >= bookmarks.Count)
                return;

            var item = bookmarks[fromIndex];
            bookmarks.RemoveAt(fromIndex);
            bookmarks.Insert(toIndex, item);
            SaveBookmarks();
        }

        public void NavigateToBookmark(BookmarkData bookmark)
        {
            if (bookmark == null) return;

            string actualPath = AssetDatabase.GUIDToAssetPath(bookmark.guid);
            if (string.IsNullOrEmpty(actualPath))
            {
                Debug.LogWarning($"ブックマーク '{bookmark.name}' のフォルダが見つかりません。削除された可能性があります。");
                return;
            }

            UnityEngine.Object folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(actualPath);
            if (folderAsset != null)
            {
                EditorApplication.ExecuteMenuItem("Window/General/Project");
                EditorUtility.FocusProjectWindow();
                EditorApplication.delayCall += () =>
                {
                    Selection.activeObject = folderAsset;
                    AssetDatabase.OpenAsset(folderAsset);
                };
            }
        }

        public void NavigateToHome()
        {
            UnityEngine.Object assetsFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets");
            if (assetsFolder == null) return;

            EditorUtility.FocusProjectWindow();

            EditorApplication.delayCall += () =>
            {
                Selection.activeObject = assetsFolder;
                EditorGUIUtility.PingObject(assetsFolder);
            };
        }

        public void SaveBookmarks()
        {
            string json = JsonUtility.ToJson(this, true);
            EditorPrefs.SetString(PrefsKey, json);
            OnBookmarksChanged?.Invoke(); // Fix 3
        }

        private static BookmarkManager LoadBookmarks()
        {
            string json = EditorPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
                return new BookmarkManager();

            try
            {
                return JsonUtility.FromJson<BookmarkManager>(json);
            }
            catch
            {
                return new BookmarkManager();
            }
        }

        public void CleanupInvalidBookmarks()
        {
            bookmarks.RemoveAll(b => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(b.guid)));
            SaveBookmarks();
        }
    }

    [Overlay(typeof(SceneView), "Folder Navigation", true, defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top, defaultLayout = Layout.HorizontalToolbar)]
    [Icon("d_FolderOpened Icon")]
    public class FolderNavigationOverlay : ToolbarOverlay
    {
        FolderNavigationOverlay() : base(HomeButton.ID, BookmarkButton.ID)
        {
            collapsedIcon = EditorGUIUtility.FindTexture("d_FolderOpened Icon");
            displayName = "フォルダナビゲーション";
        }

        public override void OnCreated()
        {
            base.OnCreated();
            collapsed = false;
        }
    }

    [EditorToolbarElement(ID, typeof(SceneView))]
    class HomeButton : EditorToolbarButton
    {
        public const string ID = "qsyi/HomeButton";

        public HomeButton()
        {
            text = "ホーム";
            tooltip = "Assetsフォルダに移動";
            icon = EditorGUIUtility.FindTexture("d_FolderOpened Icon");
            clicked += OnClicked;
        }

        void OnClicked()
        {
            BookmarkManager.Instance.NavigateToHome();
        }
    }

    [EditorToolbarElement(ID, typeof(SceneView))]
    class BookmarkButton : EditorToolbarDropdown
    {
        public const string ID = "qsyi/BookmarkButton";

        public BookmarkButton()
        {
            text = "ブックマーク";
            tooltip = "クリック: ブックマーク一覧 / D&D: フォルダをブックマーク追加";
            icon = EditorGUIUtility.FindTexture("d_Favorite Icon");

            clicked += ShowBookmarkMenu;

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerformed);
        }

        static string GetCurrentProjectFolder()
        {
            // 1. 選択中がフォルダ
            var activeObj = Selection.activeObject;
            if (activeObj != null)
            {
                string p = AssetDatabase.GetAssetPath(activeObj);
                if (AssetDatabase.IsValidFolder(p)) return p;
                // 2. 選択中がファイルなら親フォルダ
                string parent = Path.GetDirectoryName(p)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent)) return parent;
            }

            // 3. Project ウィンドウで現在開いているフォルダをリフレクションで取得
            try
            {
                var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                if (t != null)
                {
                    var browsers = Resources.FindObjectsOfTypeAll(t);
                    if (browsers.Length > 0)
                    {
                        var m = t.GetMethod("GetActiveFolderPath",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        var result = m?.Invoke(browsers[0], null) as string;
                        if (!string.IsNullOrEmpty(result) && AssetDatabase.IsValidFolder(result))
                            return result;
                    }
                }
            }
            catch { }

            return "Assets";
        }

        void OnDragUpdated(DragUpdatedEvent evt)
        {
            bool hasFolder = DragAndDrop.objectReferences.Any(
                o => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o)));
            DragAndDrop.visualMode = hasFolder
                ? DragAndDropVisualMode.Copy
                : DragAndDropVisualMode.Rejected;
        }

        void OnDragPerformed(DragPerformEvent evt)
        {
            int addedCount = 0;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!AssetDatabase.IsValidFolder(assetPath)) continue;
                BookmarkManager.Instance.AddBookmark(assetPath);
                addedCount++;
            }
            if (addedCount > 0)
            {
                DragAndDrop.AcceptDrag();
                // Fix 7: SceneView 通知
                SceneView.lastActiveSceneView?.ShowNotification(
                    new GUIContent($"ブックマークに追加しました ({addedCount}件)"), 1.5f);
            }
        }

        void ShowBookmarkMenu()
        {
            var menu = new GenericMenu();
            var bookmarkList = BookmarkManager.Instance.Bookmarks;

            if (bookmarkList.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("ブックマークがありません"));
            }
            else
            {
                bool anyValid = false;
                foreach (var bookmark in bookmarkList)
                {
                    var localBookmark = bookmark;
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(bookmark.guid)))
                        continue;
                    anyValid = true;
                    menu.AddItem(new GUIContent(bookmark.name), false, () =>
                        BookmarkManager.Instance.NavigateToBookmark(localBookmark));
                }
                if (!anyValid)
                    menu.AddDisabledItem(new GUIContent("有効なブックマークがありません (削除済み)"));
            }

            menu.AddSeparator("");
            string currentPath = GetCurrentProjectFolder();
            string currentName = currentPath == "Assets" ? "Assets" : Path.GetFileName(currentPath);
            if (currentPath == "Assets")
            {
                menu.AddDisabledItem(new GUIContent($"「{currentName}」はブックマークに追加できません"));
            }
            else
            {
                menu.AddItem(new GUIContent($"「{currentName}」をブックマークに追加"), false, () =>
                {
                    BookmarkManager.Instance.AddBookmark(currentPath);
                    SceneView.lastActiveSceneView?.ShowNotification(
                        new GUIContent($"追加: {currentName}"), 1.5f);
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("ブックマーク管理..."), false, OpenBookmarkManager);
            menu.ShowAsContext();
        }

        void OpenBookmarkManager()
        {
            BookmarkManagerWindow.ShowWindow();
        }
    }

    public static class ProjectContextMenu
    {
        [MenuItem("Assets/ブックマークに追加", true)]
        private static bool ValidateAddBookmark()
        {
            return Selection.activeObject != null &&
                   AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        [MenuItem("Assets/ブックマークに追加", false, 20)]
        private static void AddBookmark()
        {
            if (Selection.activeObject == null) return;

            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(assetPath))
                BookmarkManager.Instance.AddBookmark(assetPath);
        }
    }

    public class BookmarkManagerWindow : EditorWindow
    {
        private ReorderableList reorderableList;
        private bool isDraggingFromProject = false;
        private Vector2 _scrollPos;

        [MenuItem("Window/qsyi/Bookmark Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<BookmarkManagerWindow>("ブックマーク管理");
            window.minSize = new Vector2(450, 300);
            window.Show();
        }

        void OnEnable()
        {
            CreateReorderableList();
            BookmarkManager.OnBookmarksChanged += OnBookmarksChangedExternally; // Fix 3
        }

        void OnDisable()
        {
            BookmarkManager.OnBookmarksChanged -= OnBookmarksChangedExternally; // Fix 3
        }

        // Fix 3: 外部変更（右クリック追加・D&D など）で自動更新
        void OnBookmarksChangedExternally()
        {
            CreateReorderableList();
            Repaint();
        }

        void CreateReorderableList()
        {
            var bookmarkList = BookmarkManager.Instance.Bookmarks;

            reorderableList = new ReorderableList(bookmarkList, typeof(BookmarkData), true, true, false, true);

            reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "フォルダブックマーク");
            };

            reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= bookmarkList.Count) return;

                var bookmark = bookmarkList[index];
                string actualPath = AssetDatabase.GUIDToAssetPath(bookmark.guid);
                bool isValid = !string.IsNullOrEmpty(actualPath);
                Rect elementRect = new Rect(rect.x, rect.y, rect.width, reorderableList.elementHeight);

                Color originalColor = GUI.backgroundColor;
                if (isFocused)
                    GUI.backgroundColor = EditorGUIUtility.isProSkin
                        ? new Color(0.24f, 0.48f, 0.90f, 0.5f)
                        : new Color(0.24f, 0.48f, 0.90f, 0.25f);
                else if (isActive)
                    GUI.backgroundColor = Color.white * 0.8f;

                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                Rect iconRect = new Rect(rect.x, rect.y, 20, rect.height);
                GUIContent folderIcon = isValid
                    ? EditorGUIUtility.IconContent("d_FolderOpened Icon")
                    : EditorGUIUtility.IconContent("d_console.warnicon");
                GUI.Label(iconRect, folderIcon);

                // 名前欄: textField スタイルで枠表示し編集可能であることを明示
                Rect nameRect = new Rect(rect.x + 25, rect.y, rect.width - 25, rect.height);
                if (nameRect.Contains(Event.current.mousePosition))
                    GUI.tooltip = "クリックしてリネーム";
                string newName = EditorGUI.DelayedTextField(nameRect, bookmark.name, EditorStyles.textField);
                if (newName != bookmark.name && !string.IsNullOrWhiteSpace(newName))
                {
                    bookmark.name = newName;
                    BookmarkManager.Instance.SaveBookmarks();
                }

                rect.y += EditorGUIUtility.singleLineHeight + 2;
                Rect pathRect = new Rect(rect.x + 25, rect.y, rect.width - 25, rect.height);
                string displayPath = isValid ? actualPath : $"{bookmark.path} (削除済み)";
                EditorGUI.LabelField(pathRect, displayPath, EditorStyles.miniLabel);

                GUI.backgroundColor = originalColor;

                // カード右クリック → 削除コンテキストメニュー
                if (Event.current.type == EventType.ContextClick && elementRect.Contains(Event.current.mousePosition))
                {
                    var capturedBookmark = bookmark;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("削除"), false, () =>
                    {
                        BookmarkManager.Instance.RemoveBookmark(capturedBookmark);
                        CreateReorderableList();
                        Repaint();
                    });
                    menu.ShowAsContext();
                    Event.current.Use();
                }
            };

            reorderableList.elementHeight = EditorGUIUtility.singleLineHeight * 2 + 6;

            reorderableList.onReorderCallback = (ReorderableList list) =>
            {
                BookmarkManager.Instance.SaveBookmarks();
            };

            reorderableList.onRemoveCallback = (ReorderableList list) =>
            {
                if (list.index >= 0 && list.index < bookmarkList.Count)
                {
                    BookmarkManager.Instance.RemoveBookmark(bookmarkList[list.index]);
                    CreateReorderableList();
                }
            };
        }

        void OnGUI()
        {
            HandleDragAndDrop();
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("フォルダブックマーク管理", EditorStyles.boldLabel);

            if (GUILayout.Button("無効を削除", GUILayout.Width(80)))
            {
                BookmarkManager.Instance.CleanupInvalidBookmarks();
                CreateReorderableList();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("• D&D で順序変更・追加 / 名前欄クリックでリネーム\n• 右クリックまたは − ボタンで削除", MessageType.Info);
            EditorGUILayout.Space(5);

            if (isDraggingFromProject)
            {
                Rect dropAreaRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(dropAreaRect, new Color(0.3f, 0.6f, 1f, 0.3f));
                EditorGUI.LabelField(dropAreaRect, "ここにフォルダをドロップしてブックマーク追加", EditorStyles.centeredGreyMiniLabel);
            }

            var bookmarkList = BookmarkManager.Instance.Bookmarks;

            if (bookmarkList.Count == 0)
            {
                EditorGUILayout.HelpBox("ブックマークがありません。\n\nプロジェクトウィンドウでフォルダを右クリックして「ブックマークに追加」を選択するか、\nシーンビューのブックマークボタンやこのウィンドウにフォルダをドラッグ&ドロップしてください。", MessageType.Info);
                return;
            }

            if (reorderableList == null || reorderableList.list != bookmarkList)
                CreateReorderableList();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            reorderableList.DoLayoutList();
            EditorGUILayout.EndScrollView();
        }

        void HandleDragAndDrop()
        {
            Event evt = Event.current;
            Rect dropArea = new Rect(0, 0, position.width, position.height);

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (dropArea.Contains(evt.mousePosition))
                    {
                        bool hasValidFolder = DragAndDrop.objectReferences.Any(
                            o => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o)));
                        DragAndDrop.visualMode = hasValidFolder
                            ? DragAndDropVisualMode.Copy
                            : DragAndDropVisualMode.Rejected;
                        isDraggingFromProject = hasValidFolder;
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.DragPerform:
                    if (dropArea.Contains(evt.mousePosition))
                    {
                        int addedCount = 0;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            string assetPath = AssetDatabase.GetAssetPath(obj);
                            if (!AssetDatabase.IsValidFolder(assetPath)) continue;
                            BookmarkManager.Instance.AddBookmark(assetPath);
                            addedCount++;
                        }
                        if (addedCount > 0)
                        {
                            DragAndDrop.AcceptDrag();
                            CreateReorderableList();
                            // Fix 7: ウィンドウ内通知
                            ShowNotification(new GUIContent($"{addedCount}件をブックマークに追加しました"), 1.5f);
                        }
                        evt.Use();
                    }
                    isDraggingFromProject = false;
                    break;

                case EventType.DragExited:
                    isDraggingFromProject = false;
                    Repaint();
                    break;
            }
        }
    }

    [InitializeOnLoad]
    public static class BookmarkEditorInitializer
    {
        // Fix 4: セッション単位で1回だけ実行
        private const string SESSION_CLEANED_KEY = "qsyi.BookmarksCleaned";

        static BookmarkEditorInitializer()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (SessionState.GetBool(SESSION_CLEANED_KEY, false)) return;
            SessionState.SetBool(SESSION_CLEANED_KEY, true);
            BookmarkManager.Instance.CleanupInvalidBookmarks();
        }
    }
}
#endif
