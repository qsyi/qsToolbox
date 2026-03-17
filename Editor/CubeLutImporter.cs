#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace qsyi
{
    internal sealed class CubeLutImporter : AssetPostprocessor
    {
        private const string LogPrefix = "[qsToolBox]";
        private sealed class CubeLutData
        {
            public string Title { get; }
            public int Size { get; }
            public IReadOnlyList<Color> Colors { get; }

            public CubeLutData(string title, int size, List<Color> colors)
            {
                Title = title ?? string.Empty;
                Size = size;
                Colors = colors ?? throw new ArgumentNullException(nameof(colors));
            }
        }

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false, true);
        private static readonly HashSet<string> PendingGeneratePaths = new HashSet<string>();
        private static readonly HashSet<string> PendingDeletePaths = new HashSet<string>();
        private static bool _isScheduled;

        // Asset entry point
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets.Where(IsCubeAssetPath))
                PendingGeneratePaths.Add(path);

            foreach (string path in deletedAssets.Where(IsCubeAssetPath))
                PendingDeletePaths.Add(path);

            for (int i = 0; i < movedAssets.Length; i++)
            {
                string newPath = movedAssets[i];
                string oldPath = movedFromAssetPaths[i];

                if (IsCubeAssetPath(oldPath))
                    PendingDeletePaths.Add(oldPath);

                if (IsCubeAssetPath(newPath))
                    PendingGeneratePaths.Add(newPath);
            }

            ScheduleProcessing();
        }

        // Import scheduling
        private static void ScheduleProcessing()
        {
            if (_isScheduled)
                return;

            _isScheduled = true;
            EditorApplication.delayCall += ProcessPendingAssets;
        }

        private static void ProcessPendingAssets()
        {
            _isScheduled = false;

            string[] deleteTargets = PendingDeletePaths.ToArray();
            string[] generateTargets = PendingGeneratePaths.ToArray();
            PendingDeletePaths.Clear();
            PendingGeneratePaths.Clear();

            foreach (string cubePath in deleteTargets)
            {
                try
                {
                    DeleteGeneratedTexture(cubePath);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{LogPrefix} Failed to delete generated LUT for '{cubePath}'.\n{exception.Message}");
                }
            }

            foreach (string cubePath in generateTargets)
            {
                if (!IsCubeAssetPath(cubePath))
                    continue;

                try
                {
                    GeneratePpsTexture(cubePath);
                    Debug.Log($"{LogPrefix} Generated PPS LUT texture: {GetGeneratedAssetPath(cubePath)}");
                }
                catch (Exception exception)
                {
                    DeleteGeneratedTexture(cubePath);
                    Debug.LogError($"{LogPrefix} Failed to convert '{cubePath}' to a PPS LUT texture.\n{exception.Message}");
                }
            }
        }

        // File paths
        private static bool IsCubeAssetPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   assetPath.EndsWith(".cube", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetGeneratedAssetPath(string cubeAssetPath)
        {
            string directory = Path.GetDirectoryName(cubeAssetPath)?.Replace('\\', '/') ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(cubeAssetPath);
            return $"{directory}/{fileName}_lut.png";
        }

        // Generation
        private static void GeneratePpsTexture(string cubeAssetPath)
        {
            string absoluteCubePath = GetAbsoluteAssetPath(cubeAssetPath);
            CubeLutData lutData = ParseFile(absoluteCubePath);
            Texture2D texture = null;

            try
            {
                texture = CreateTexture(lutData);
                byte[] pngBytes = texture.EncodeToPNG();
                if (pngBytes == null || pngBytes.Length == 0)
                    throw new IOException("Failed to encode LUT texture to PNG.");

                string generatedAssetPath = GetGeneratedAssetPath(cubeAssetPath);
                string absoluteTexturePath = GetAbsoluteAssetPath(generatedAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteTexturePath) ?? string.Empty);
                File.WriteAllBytes(absoluteTexturePath, pngBytes);

                AssetDatabase.ImportAsset(generatedAssetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter(generatedAssetPath);
            }
            finally
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void DeleteGeneratedTexture(string cubeAssetPath)
        {
            string generatedAssetPath = GetGeneratedAssetPath(cubeAssetPath);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(generatedAssetPath) != null ||
                File.Exists(Path.GetFullPath(generatedAssetPath)))
            {
                AssetDatabase.DeleteAsset(generatedAssetPath);
            }
        }

        private static Texture2D CreateTexture(CubeLutData lutData)
        {
            int size = lutData.Size;
            int width = size * size;
            int height = size;
            var pixels = new Color[width * height];

            for (int index = 0; index < lutData.Colors.Count; index++)
            {
                int red = index % size;
                int green = (index / size) % size;
                int blue = index / (size * size);

                int x = red + blue * size;
                int y = green;
                pixels[y * width + x] = Clamp01(lutData.Colors[index]);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = string.IsNullOrEmpty(lutData.Title) ? "CubeLut" : lutData.Title,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // Texture importer settings
        private static void ConfigureImporter(string generatedAssetPath)
        {
            var importer = AssetImporter.GetAtPath(generatedAssetPath) as TextureImporter;
            if (importer == null)
                throw new IOException($"Texture importer was not found for {generatedAssetPath}.");

            int requiredMaxTextureSize = ReadTextureDimension(generatedAssetPath);

            bool changed = false;
            changed |= SetIfDifferent(() => importer.textureType, value => importer.textureType = value, TextureImporterType.Default);
            changed |= SetIfDifferent(() => importer.textureShape, value => importer.textureShape = value, TextureImporterShape.Texture2D);
            changed |= SetIfDifferent(() => importer.sRGBTexture, value => importer.sRGBTexture = value, false);
            changed |= SetIfDifferent(() => importer.alphaSource, value => importer.alphaSource = value, TextureImporterAlphaSource.None);
            changed |= SetIfDifferent(() => importer.alphaIsTransparency, value => importer.alphaIsTransparency = value, false);
            changed |= SetIfDifferent(() => importer.mipmapEnabled, value => importer.mipmapEnabled = value, false);
            changed |= SetIfDifferent(() => importer.npotScale, value => importer.npotScale = value, TextureImporterNPOTScale.None);
            changed |= SetIfDifferent(() => importer.wrapMode, value => importer.wrapMode = value, TextureWrapMode.Clamp);
            changed |= SetIfDifferent(() => importer.filterMode, value => importer.filterMode = value, FilterMode.Bilinear);
            changed |= SetIfDifferent(() => importer.anisoLevel, value => importer.anisoLevel = value, 0);
            changed |= SetIfDifferent(() => importer.textureCompression, value => importer.textureCompression = value, TextureImporterCompression.Uncompressed);
            changed |= SetIfDifferent(() => importer.isReadable, value => importer.isReadable = value, false);
            changed |= SetIfDifferent(() => importer.maxTextureSize, value => importer.maxTextureSize = value, requiredMaxTextureSize);

            if (changed)
                importer.SaveAndReimport();

            ValidateImportedTexture(generatedAssetPath);
        }

        private static int ReadTextureDimension(string generatedAssetPath)
        {
            string absoluteTexturePath = GetAbsoluteAssetPath(generatedAssetPath);
            var probeTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                byte[] fileBytes = File.ReadAllBytes(absoluteTexturePath);
                if (!probeTexture.LoadImage(fileBytes, true))
                    throw new IOException($"Failed to read generated LUT texture: {generatedAssetPath}.");

                return GetRequiredMaxTextureSize(Mathf.Max(probeTexture.width, probeTexture.height));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probeTexture);
            }
        }

        private static void ValidateImportedTexture(string generatedAssetPath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(generatedAssetPath);
            if (texture == null)
                throw new IOException($"Generated LUT texture could not be loaded: {generatedAssetPath}.");

            if (texture.width != texture.height * texture.height)
                throw new IOException($"Generated LUT texture has invalid imported dimensions {texture.width}x{texture.height}. Expected width = height * height.");
        }

        // .cube parsing
        private static CubeLutData ParseFile(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                throw new ArgumentException("Path is null or empty.", nameof(absolutePath));

            string[] lines = File.ReadAllLines(absolutePath, Utf8NoBom);

            string title = string.Empty;
            int size = 0;
            bool hasDomainMin = false;
            bool hasDomainMax = false;
            var colors = new List<Color>();

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex]?.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
                {
                    title = ParseTitle(line);
                    continue;
                }

                if (line.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                    throw CreateFormatException(lineIndex, "LUT_1D_SIZE is not supported.");

                if (line.StartsWith("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
                {
                    hasDomainMin = true;
                    continue;
                }

                if (line.StartsWith("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
                {
                    hasDomainMax = true;
                    continue;
                }

                if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
                {
                    size = ParseLutSize(line, lineIndex);
                    continue;
                }

                colors.Add(ParseColor(line, lineIndex));
            }

            if (hasDomainMin || hasDomainMax)
                throw new FormatException("DOMAIN_MIN / DOMAIN_MAX is not supported.");

            if (size <= 0)
                throw new FormatException("LUT_3D_SIZE was not found.");

            if (size < 2)
                throw new FormatException($"LUT_3D_SIZE must be 2 or greater. Found: {size}.");

            int expectedCount = size * size * size;
            if (colors.Count != expectedCount)
                throw new FormatException($"Expected {expectedCount} color rows, but found {colors.Count}.");

            return new CubeLutData(title, size, colors);
        }

        private static string ParseTitle(string line)
        {
            int quoteStart = line.IndexOf('"');
            int quoteEnd = line.LastIndexOf('"');
            if (quoteStart >= 0 && quoteEnd > quoteStart)
                return line.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);

            string[] parts = SplitWhitespace(line);
            return parts.Length >= 2 ? parts[1] : string.Empty;
        }

        private static int ParseLutSize(string line, int lineIndex)
        {
            string[] parts = SplitWhitespace(line);
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
                throw CreateFormatException(lineIndex, "Invalid LUT_3D_SIZE declaration.");

            return size;
        }

        private static Color ParseColor(string line, int lineIndex)
        {
            string[] parts = SplitWhitespace(line);
            if (parts.Length != 3)
                throw CreateFormatException(lineIndex, "Color row must have exactly 3 numeric values.");

            if (!TryParseFloat(parts[0], out float r) ||
                !TryParseFloat(parts[1], out float g) ||
                !TryParseFloat(parts[2], out float b))
            {
                throw CreateFormatException(lineIndex, "Failed to parse color row.");
            }

            return new Color(r, g, b, 1f);
        }

        // Shared helpers
        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Asset path is null or empty.", nameof(assetPath));

            string normalizedAssetPath = assetPath.Replace('\\', '/');
            if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Asset path must start with 'Assets/': {assetPath}", nameof(assetPath));

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new IOException("Failed to resolve Unity project root.");

            string relativePath = normalizedAssetPath.Substring("Assets/".Length)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, "Assets", relativePath);
        }

        private static string[] SplitWhitespace(string line)
        {
            return line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        private static Color Clamp01(Color color)
        {
            return new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                1f);
        }

        private static int GetRequiredMaxTextureSize(int dimension)
        {
            int[] supportedSizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
            foreach (int size in supportedSizes)
            {
                if (dimension <= size)
                    return size;
            }

            return 16384;
        }

        private static bool SetIfDifferent<T>(Func<T> getter, Action<T> setter, T expected)
        {
            if (Equals(getter(), expected))
                return false;

            setter(expected);
            return true;
        }

        private static FormatException CreateFormatException(int lineIndex, string message)
        {
            return new FormatException($"Line {lineIndex + 1}: {message}");
        }
    }
}
#endif
