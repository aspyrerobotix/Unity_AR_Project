using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ARProject.EditorTools
{
    /// <summary>
    /// Editor window that batch-compresses all textures inside a chosen folder (and its
    /// subfolders) using settings tuned to balance visual quality against file size /
    /// runtime memory, for mobile AR performance.
    /// </summary>
    public class TextureCompressorWindow : EditorWindow
    {
        private const int DefaultMaxSize = 2048;

        private string _targetFolder = "Assets";
        private int _maxSize = DefaultMaxSize;
        private int _compressionQuality = 50; // 0 (fast/low quality) - 100 (slow/high quality), used by crunch compression
        private bool _useCrunchCompression = true;
        private bool _generateMipmaps = true;
        private bool _overwriteNonDefaultPlatformOverrides = false;

        private Vector2 _scroll;
        private List<string> _previewTexturePaths = new List<string>();
        private long _estimatedBeforeBytes;

        [MenuItem("Tools/Texture Compressor")]
        public static void ShowWindow()
        {
            var window = GetWindow<TextureCompressorWindow>("Texture Compressor");
            window.minSize = new Vector2(420, 360);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Batch Texture Compression", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select a folder to compress every texture inside it (including subfolders). " +
                "Settings below are balanced to reduce app size and improve performance while " +
                "keeping visual quality acceptable.",
                MessageType.Info);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Folder", GUILayout.Width(100));
            EditorGUILayout.LabelField(_targetFolder, EditorStyles.textField);
            if (GUILayout.Button("Select...", GUILayout.Width(80)))
            {
                SelectFolder();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compression Settings", EditorStyles.boldLabel);

            _maxSize = EditorGUILayout.IntPopup(
                "Max Texture Size",
                _maxSize,
                new[] { "512", "1024", "2048", "4096" },
                new[] { 512, 1024, 2048, 4096 });

            _useCrunchCompression = EditorGUILayout.Toggle(
                new GUIContent("Use Crunch Compression", "Crunch reduces file size further with a small, usually imperceptible, quality cost."),
                _useCrunchCompression);

            using (new EditorGUI.DisabledScope(!_useCrunchCompression))
            {
                _compressionQuality = EditorGUILayout.IntSlider(
                    new GUIContent("Compression Quality", "Higher = better quality / larger size. 50 is a good balance."),
                    _compressionQuality, 0, 100);
            }

            _generateMipmaps = EditorGUILayout.Toggle(
                new GUIContent("Generate Mipmaps", "Recommended for 3D/AR scene textures viewed at varying distances."),
                _generateMipmaps);

            _overwriteNonDefaultPlatformOverrides = EditorGUILayout.Toggle(
                new GUIContent("Overwrite Platform Overrides", "If a texture already has custom Android/iOS overrides, overwrite them too. Otherwise those overrides are left as-is."),
                _overwriteNonDefaultPlatformOverrides);

            EditorGUILayout.Space();

            if (GUILayout.Button("Scan Folder", GUILayout.Height(24)))
            {
                ScanFolder();
            }

            if (_previewTexturePaths.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Found {_previewTexturePaths.Count} texture(s) — current size on disk: {EditorUtility.FormatBytes(_estimatedBeforeBytes)}", EditorStyles.boldLabel);

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(120));
                foreach (var path in _previewTexturePaths)
                {
                    EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Compress Textures", GUILayout.Height(32)))
                {
                    CompressTextures();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        private void SelectFolder()
        {
            string absoluteStart = string.IsNullOrEmpty(_targetFolder) ? Application.dataPath : Path.GetFullPath(_targetFolder);
            string selected = EditorUtility.OpenFolderPanel("Select Texture Folder", absoluteStart, "");
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            string dataPath = Path.GetFullPath(Application.dataPath);
            string selectedFull = Path.GetFullPath(selected);

            if (!selectedFull.StartsWith(dataPath))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside this project's Assets directory.", "OK");
                return;
            }

            string relative = "Assets" + selectedFull.Substring(dataPath.Length).Replace('\\', '/');
            _targetFolder = relative;
            _previewTexturePaths.Clear();
        }

        private void ScanFolder()
        {
            _previewTexturePaths = FindTexturePaths(_targetFolder);
            _estimatedBeforeBytes = 0;
            foreach (var path in _previewTexturePaths)
            {
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
                if (File.Exists(fullPath))
                {
                    _estimatedBeforeBytes += new FileInfo(fullPath).Length;
                }
            }
        }

        private List<string> FindTexturePaths(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return new List<string>();
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
        }

        private void CompressTextures()
        {
            if (_previewTexturePaths.Count == 0)
            {
                ScanFolder();
            }

            if (_previewTexturePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("No Textures Found", $"No textures were found under '{_targetFolder}'.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Confirm Compression",
                    $"This will modify import settings for {_previewTexturePaths.Count} texture(s) under '{_targetFolder}'.\n\nThis cannot be automatically undone. Continue?",
                    "Compress", "Cancel"))
            {
                return;
            }

            int processed = 0;
            int skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < _previewTexturePaths.Count; i++)
                {
                    string path = _previewTexturePaths[i];
                    EditorUtility.DisplayProgressBar("Compressing Textures", path, (float)i / _previewTexturePaths.Count);

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        skipped++;
                        continue;
                    }

                    ApplyBalancedSettings(importer);
                    processed++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog(
                "Compression Complete",
                $"Processed {processed} texture(s).\nSkipped {skipped} (not texture importers).",
                "OK");

            ScanFolder();
        }

        private void ApplyBalancedSettings(TextureImporter importer)
        {
            // Keep sprites/UI and normal maps/masks tagged correctly; only touch compression-related fields.
            importer.mipmapEnabled = _generateMipmaps && importer.textureType != TextureImporterType.Sprite;
            importer.maxTextureSize = _maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = _useCrunchCompression;
            importer.compressionQuality = _compressionQuality;

            // Use high-quality compressed format as the default, letting Unity pick the best
            // compressed format for each platform (ASTC on mobile, BC7/DXT on desktop) rather
            // than forcing a single format that may be unsupported or wasteful.
            ApplyPlatformSettings(importer, "Android", TextureImporterFormat.ASTC_6x6);
            ApplyPlatformSettings(importer, "iPhone", TextureImporterFormat.ASTC_6x6);
            ApplyPlatformSettings(importer, "Standalone", TextureImporterFormat.DXT5);

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        private void ApplyPlatformSettings(TextureImporter importer, string platform, TextureImporterFormat format)
        {
            var settings = importer.GetPlatformTextureSettings(platform);

            if (settings.overridden && !_overwriteNonDefaultPlatformOverrides)
            {
                // Respect existing per-platform overrides unless the user asked to overwrite them.
                return;
            }

            settings.overridden = true;
            settings.maxTextureSize = _maxSize;
            settings.format = format;
            settings.compressionQuality = _compressionQuality;
            settings.crunchedCompression = _useCrunchCompression;
            settings.textureCompression = TextureImporterCompression.Compressed;

            importer.SetPlatformTextureSettings(settings);
        }
    }
}
