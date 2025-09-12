#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                
                EditorApplication.delayCall += () => {
                    Selection.activeObject = folderAsset;
                    EditorApplication.delayCall += () => {
                        AssetDatabase.OpenAsset(folderAsset);
                    };
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
            tooltip = "ブックマークしたフォルダに移動/フォルダをドラッグ&ドロップでブックマーク追加";
            icon = EditorGUIUtility.FindTexture("d_Favorite Icon");
            
            clicked += ShowBookmarkMenu;
            
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerformed);
        }

        void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.objectReferences.Length == 1)
            {
                var draggedObject = DragAndDrop.objectReferences[0];
                string assetPath = AssetDatabase.GetAssetPath(draggedObject);
                
                DragAndDrop.visualMode = AssetDatabase.IsValidFolder(assetPath) 
                    ? DragAndDropVisualMode.Copy 
                    : DragAndDropVisualMode.Rejected;
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }
        }

        void OnDragPerformed(DragPerformEvent evt)
        {
            if (DragAndDrop.objectReferences.Length == 1)
            {
                var draggedObject = DragAndDrop.objectReferences[0];
                string assetPath = AssetDatabase.GetAssetPath(draggedObject);
                
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    BookmarkManager.Instance.AddBookmark(assetPath);
                    Debug.Log($"フォルダ '{Path.GetFileName(assetPath)}' をブックマークに追加しました。");
                    DragAndDrop.AcceptDrag();
                }
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
                BookmarkManager.Instance.CleanupInvalidBookmarks();
                
                foreach (var bookmark in bookmarkList)
                {
                    var localBookmark = bookmark;
                    string actualPath = AssetDatabase.GUIDToAssetPath(bookmark.guid);
                    
                    if (!string.IsNullOrEmpty(actualPath))
                    {
                        menu.AddItem(new GUIContent(bookmark.name), false, () => {
                            BookmarkManager.Instance.NavigateToBookmark(localBookmark);
                        });
                    }
                }
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
            {
                BookmarkManager.Instance.AddBookmark(assetPath);
                Debug.Log($"フォルダ '{Path.GetFileName(assetPath)}' をブックマークに追加しました。");
            }
        }
    }

    public class BookmarkManagerWindow : EditorWindow
    {
        private ReorderableList reorderableList;
        private bool isDraggingFromProject = false;

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

                Color originalColor = GUI.backgroundColor;
                if (isFocused)
                    GUI.backgroundColor = Color.cyan * 0.5f;
                else if (isActive)
                    GUI.backgroundColor = Color.white * 0.8f;

                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                Rect iconRect = new Rect(rect.x, rect.y, 20, rect.height);
                GUIContent icon = isValid ? 
                    EditorGUIUtility.IconContent("d_FolderOpened Icon") : 
                    EditorGUIUtility.IconContent("d_console.warnicon");
                GUI.Label(iconRect, icon);

                Rect nameRect = new Rect(rect.x + 25, rect.y, rect.width - 120, rect.height);
                EditorGUI.LabelField(nameRect, bookmark.name, EditorStyles.boldLabel);

                rect.y += EditorGUIUtility.singleLineHeight + 2;
                Rect pathRect = new Rect(rect.x + 25, rect.y, rect.width - 120, rect.height);
                string displayPath = isValid ? actualPath : $"{bookmark.path} (削除済み)";
                EditorGUI.LabelField(pathRect, displayPath, EditorStyles.miniLabel);

                Rect buttonRect = new Rect(rect.x + rect.width - 110, rect.y - EditorGUIUtility.singleLineHeight - 2, 50, rect.height);
                
                EditorGUI.BeginDisabledGroup(!isValid);
                if (GUI.Button(buttonRect, "移動"))
                {
                    BookmarkManager.Instance.NavigateToBookmark(bookmark);
                }
                EditorGUI.EndDisabledGroup();

                buttonRect.x += 55;
                if (GUI.Button(buttonRect, "削除"))
                {
                    if (EditorUtility.DisplayDialog("確認", 
                        $"ブックマーク '{bookmark.name}' を削除しますか？", 
                        "削除", "キャンセル"))
                    {
                        BookmarkManager.Instance.RemoveBookmark(bookmark);
                        CreateReorderableList();
                        Repaint();
                    }
                }

                GUI.backgroundColor = originalColor;
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
                    var bookmark = bookmarkList[list.index];
                    if (EditorUtility.DisplayDialog("確認", 
                        $"ブックマーク '{bookmark.name}' を削除しますか？", 
                        "削除", "キャンセル"))
                    {
                        BookmarkManager.Instance.RemoveBookmark(bookmark);
                        CreateReorderableList();
                    }
                }
            };
        }

        void OnGUI()
        {
            HandleDragAndDrop();
            EditorGUILayout.Space(10);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("フォルダブックマーク管理", EditorStyles.boldLabel);
            
            if (GUILayout.Button("無効なブックマークを削除", GUILayout.Width(150)))
            {
                BookmarkManager.Instance.CleanupInvalidBookmarks();
                CreateReorderableList();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("• ドラッグ&ドロップで順序を変更\n• プロジェクトウィンドウからフォルダをドラッグしてブックマーク追加", MessageType.Info);
            EditorGUILayout.Space(5);

            if (isDraggingFromProject)
            {
                Rect dropAreaRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(dropAreaRect, new Color(0.3f, 0.6f, 1f, 0.3f));
                EditorGUI.LabelField(dropAreaRect, "ここにフォルダをドロップしてブックマークに追加", EditorStyles.centeredGreyMiniLabel);
            }

            var bookmarkList = BookmarkManager.Instance.Bookmarks;
            
            if (bookmarkList.Count == 0)
            {
                EditorGUILayout.HelpBox("ブックマークがありません。\n\nプロジェクトウィンドウでフォルダを右クリックして「ブックマークに追加」を選択するか、\nシーンビューのブックマークボタンやこのウィンドウにフォルダをドラッグ&ドロップしてください。", MessageType.Info);
                return;
            }

            if (reorderableList == null || reorderableList.list != bookmarkList)
            {
                CreateReorderableList();
            }

            reorderableList.DoLayoutList();
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
                        bool hasValidFolder = false;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            string assetPath = AssetDatabase.GetAssetPath(obj);
                            if (AssetDatabase.IsValidFolder(assetPath))
                            {
                                hasValidFolder = true;
                                break;
                            }
                        }

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
                        bool addedAny = false;
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            string assetPath = AssetDatabase.GetAssetPath(obj);
                            if (AssetDatabase.IsValidFolder(assetPath))
                            {
                                BookmarkManager.Instance.AddBookmark(assetPath);
                                Debug.Log($"フォルダ '{Path.GetFileName(assetPath)}' をブックマークに追加しました。");
                                addedAny = true;
                            }
                        }
                        
                        if (addedAny)
                        {
                            DragAndDrop.AcceptDrag();
                            CreateReorderableList();
                            Repaint();
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
        static BookmarkEditorInitializer()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            BookmarkManager.Instance.CleanupInvalidBookmarks();
        }
    }
}
#endif