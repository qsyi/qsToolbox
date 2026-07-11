#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;

namespace qsyi
{
    [InitializeOnLoad]
    internal static class QsVersionChecker
    {
        private const string PACKAGE_NAME       = "jp.qsyi.qs-toolbox";
        private const string GITHUB_API_URL     = "https://api.github.com/repos/qsyi/qsToolbox/releases/latest";
        private const string LATEST_VER_KEY     = "qsToolBox_latestVersion";
        private const string SESSION_FETCHED_KEY = "qsToolBox_fetchedThisSession";

        public static bool   HasUpdate      { get; private set; }
        public static bool   CheckComplete  { get; private set; }
        public static bool   IsFetching     { get; private set; }
        public static string LatestVersion  { get; private set; } = "";
        public static string CurrentVersion { get; private set; } = "";

        private static UnityWebRequest _request;

        static QsVersionChecker()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            string current = GetCurrentVersion();
            CurrentVersion = current;
            if (string.IsNullOrEmpty(current)) return;

            // キャッシュ済みの結果で即時表示
            string cached = EditorPrefs.GetString(LATEST_VER_KEY, "");
            if (!string.IsNullOrEmpty(cached))
                TrySetUpdate(current, cached);

            // 起動後初回のみフェッチ（リコンパイルではスキップ）
            if (!SessionState.GetBool(SESSION_FETCHED_KEY, false))
            {
                SessionState.SetBool(SESSION_FETCHED_KEY, true);
                FetchLatestVersion();
            }
        }

        private static void FetchLatestVersion()
        {
            IsFetching = true;
            _request = new UnityWebRequest(GITHUB_API_URL, "GET");
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.SetRequestHeader("User-Agent", "qsToolBox-VersionChecker");
            _request.timeout = 10;
            _request.SendWebRequest();
            EditorApplication.update += PollRequest;
        }

        private static void PollRequest()
        {
            if (_request == null || !_request.isDone) return;
            EditorApplication.update -= PollRequest;

            if (_request.result == UnityWebRequest.Result.Success)
            {
                string tagName = ExtractTagName(_request.downloadHandler.text);
                if (!string.IsNullOrEmpty(tagName))
                {
                    EditorPrefs.SetString(LATEST_VER_KEY, tagName);
                    string current = GetCurrentVersion();
                    if (!string.IsNullOrEmpty(current))
                        TrySetUpdate(current, tagName);
                }
            }

            _request.Dispose();
            _request = null;
            IsFetching = false;

            // フェッチ完了後にウィンドウを再描画
            EditorApplication.delayCall += () =>
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<QsToolBox>())
                    w.Repaint();
            };
        }

        private static void TrySetUpdate(string current, string latest)
        {
            try
            {
                var currentVer = new Version(current.TrimStart('v'));
                var latestVer  = new Version(latest.TrimStart('v'));
                HasUpdate     = latestVer > currentVer;
                LatestVersion = latest.TrimStart('v');
            }
            catch { /* バージョン文字列が不正な場合は無視 */ }
            CheckComplete = true;
        }

        private static string GetCurrentVersion()
        {
            try
            {
                string path = $"Packages/{PACKAGE_NAME}/package.json";
                if (!File.Exists(path)) return "";
                var pkg = JsonUtility.FromJson<PackageJson>(File.ReadAllText(path));
                return pkg?.version ?? "";
            }
            catch { return ""; }
        }

        [Serializable] private class GitHubRelease { public string tag_name; }
        [Serializable] private class PackageJson    { public string version; }

        private static string ExtractTagName(string json)
        {
            try
            {
                var release = JsonUtility.FromJson<GitHubRelease>(json);
                return release?.tag_name ?? "";
            }
            catch { return ""; }
        }
    }
}
#endif
