using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Nova.Editor
{
    [CustomEditor(typeof(BackgroundGroup))]
    public class BackgroundGroupEditor : UnityEditor.Editor
    {
        public const int SnapshotWidth = 320;
        public const int SnapshotHeight = 180;
        public const float SnapshotAspectRatio = (float) SnapshotHeight / SnapshotWidth;

        private static string GetAssetFullPath(UnityEngine.Object asset)
        {
            return Path.Combine(Application.dataPath, AssetDatabase.GetAssetPath(asset).Substring(7));
        }

        private const string ResourceFolderName = "/Resources/";

        private static string GetResourcesFolder(string path)
        {
            // Convert OS-dependent path separator to "/"
            path = path.Replace("\\", "/");

            var index = path.IndexOf(ResourceFolderName, StringComparison.Ordinal);
            if (index == -1)
            {
                throw new ArgumentException();
            }

            return path.Substring(0, index + ResourceFolderName.Length);
        }

        private static string GetResourcesPath(string path)
        {
            // Convert OS-dependent path separator to "/"
            path = path.Replace("\\", "/");

            var index = path.IndexOf(ResourceFolderName, StringComparison.Ordinal);
            if (index == -1)
            {
                throw new ArgumentException();
            }

            var fullResPath = path.Substring(index + ResourceFolderName.Length);
            var dir = Path.GetDirectoryName(fullResPath);
            var fileName = Path.GetFileNameWithoutExtension(fullResPath);
            return Path.Combine(dir, fileName);
        }

        private static string GetCommonPrefix(IEnumerable<string> paths)
        {
            var fileNames = paths.Select(Path.GetFileNameWithoutExtension);
            var prefix = new string(
                fileNames.First()
                    .Substring(0, fileNames.Min(s => s.Length))
                    .TakeWhile((c, i) => fileNames.All(s => s[i] == c))
                    .ToArray()
            );

            prefix = prefix.TrimEnd('_');

            if (prefix.Length == 0)
            {
                prefix = fileNames.OrderBy(s => s).First();
            }

            return prefix;
        }

        private static void CreateBackgroundGroup(string path, IEnumerable<string> backgroundSprites)
        {
            var groupPath = Path.Combine(path, GetCommonPrefix(backgroundSprites) + "_group.asset");
            var group = AssetDatabase.LoadAssetAtPath<BackgroundGroup>(groupPath);
            if (group == null)
            {
                group = CreateInstance<BackgroundGroup>();
                AssetDatabase.CreateAsset(group, groupPath);
            }

            group.entries = backgroundSprites.Select(bgPath =>
            {
                var fileName = Path.GetFileNameWithoutExtension(bgPath);
                return new BackgroundEntry
                {
                    id = fileName,
                    displayNames = new[] {new LocaleStringPair {locale = I18n.DefaultLocale, value = fileName}},
                    resourcePath = GetResourcesPath(bgPath),
                };
            }).ToList();

            EditorUtility.SetDirty(group);
        }

        [MenuItem("Assets/Create/Nova/Background Group", false)]
        public static void CreateBackgroundGroup()
        {
            // split path name and file name
            var path = EditorUtils.GetSelectedDirectory();
            CreateBackgroundGroup(path, EditorUtils.PathOfSelectedSprites());
        }

        [MenuItem("Assets/Create/Nova/Background Group", true)]
        public static bool CreateBackgroundGroupValidation()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(Texture2D);
        }

        private ReorderableList reorderableList;
        private SerializedProperty entries;

        private void OnEnable()
        {
            entries = serializedObject.FindProperty("entries");
            reorderableList = new ReorderableList(serializedObject, entries,
                true, true, true, true);

            reorderableList.drawHeaderCallback = rect => { EditorGUI.LabelField(rect, new GUIContent("Backgrounds")); };

            reorderableList.onAddCallback = list =>
            {
                var index = list.index == -1 ? entries.arraySize : list.index;
                entries.InsertArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
            };

            reorderableList.onRemoveCallback = list =>
            {
                if (list.index == -1) return;
                entries.DeleteArrayElementAtIndex(list.index);
                serializedObject.ApplyModifiedProperties();
            };
        }

        private bool previewDrawSnapshotFrame = true;
        private Color previewSnapshotFrameColor = Color.red;
        private float previewSnapshotFrameLineWidth = 2.0f;

        private static void CorrectSnapshotScaleY(BackgroundEntry entry, Texture tex, SerializedProperty entryProperty)
        {
            var size = entry.snapshotScale * new Vector2(tex.width, tex.height);
            var fix = Mathf.Abs(SnapshotAspectRatio / (size.y / size.x));
            entryProperty.FindPropertyRelative("snapshotScale.y").floatValue *= fix;
        }

        private static void CorrectSnapshotScaleX(BackgroundEntry entry, Texture tex, SerializedProperty entryProperty)
        {
            var size = entry.snapshotScale * new Vector2(tex.width, tex.height);
            var fix = Mathf.Abs((1.0f / SnapshotAspectRatio) / (size.x / size.y));
            entryProperty.FindPropertyRelative("snapshotScale.x").floatValue *= fix;
        }

        private static void ResetSnapshotScaleOffset(SerializedProperty entryProperty)
        {
            // use serialized property for proper save & undo
            entryProperty.FindPropertyRelative("snapshotOffset.x").floatValue = 0.0f;
            entryProperty.FindPropertyRelative("snapshotOffset.y").floatValue = 0.0f;
            entryProperty.FindPropertyRelative("snapshotScale.x").floatValue = 1.0f;
            entryProperty.FindPropertyRelative("snapshotScale.y").floatValue = 1.0f;
        }

        private void DrawPreview(string path, BackgroundEntry entry, SerializedProperty entryProperty)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                EditorGUILayout.HelpBox("Invalid background resource path!", MessageType.Error);
                return;
            }

            if (GUILayout.Button("Correct Snapshot Scale Y for Aspect Ratio"))
            {
                CorrectSnapshotScaleY(entry, sprite.texture, entryProperty);
            }

            if (GUILayout.Button("Correct Snapshot Scale X for Aspect Ratio"))
            {
                CorrectSnapshotScaleX(entry, sprite.texture, entryProperty);
            }

            if (GUILayout.Button("Reset Snapshot Scale Offset"))
            {
                ResetSnapshotScaleOffset(entryProperty);
            }

            var height = EditorGUIUtility.currentViewWidth / sprite.texture.width * sprite.texture.height;
            var rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.DrawPreviewTexture(rect, sprite.texture);
            if (previewDrawSnapshotFrame)
            {
                DrawPreviewSnapshotFrame(entry, rect);
            }
        }

        private void DrawPreviewSnapshotFrame(BackgroundEntry entry, Rect rect)
        {
            EditorUtils.DrawPreviewCropFrame(rect, new Rect(entry.snapshotOffset, entry.snapshotScale),
                previewSnapshotFrameColor, previewSnapshotFrameLineWidth);
        }

        private void DrawEntry(int index)
        {
            var entry = entries.GetArrayElementAtIndex(index);
            EditorGUILayout.PropertyField(entry, true);
            var path = entry.FindPropertyRelative("resourcePath").stringValue;
            EditorGUILayout.Space();

            // preview snapshot frame options
            previewDrawSnapshotFrame = GUILayout.Toggle(previewDrawSnapshotFrame, "Preview Draw Snapshot Frame");
            previewSnapshotFrameColor = EditorGUILayout.ColorField("Snapshot Frame Color", previewSnapshotFrameColor);
            previewSnapshotFrameLineWidth = EditorGUILayout.Slider("Snapshot Frame Line Width",
                previewSnapshotFrameLineWidth, 0.5f, 4.0f);

            DrawPreview(path, Target.entries[index], entry);
        }

        private static RenderTexture _snapshotRenderTexture;

        public static RenderTexture SnapshotRenderTexture
        {
            get
            {
                if (_snapshotRenderTexture == null)
                {
                    _snapshotRenderTexture =
                        new RenderTexture(SnapshotWidth, SnapshotHeight, 0);
                }

                return _snapshotRenderTexture;
            }
        }

        private static Texture2D _snapshotTexture;

        public static Texture2D SnapshotTexture
        {
            get
            {
                if (_snapshotTexture == null)
                {
                    _snapshotTexture = new Texture2D(SnapshotWidth, SnapshotHeight);
                }

                return _snapshotTexture;
            }
        }

        private static byte[] GetSnapshotPNGData()
        {
            var oldRt = RenderTexture.active;
            RenderTexture.active = SnapshotRenderTexture;
            SnapshotTexture.ReadPixels(new Rect(0, 0, SnapshotWidth, SnapshotHeight), 0, 0);
            SnapshotTexture.Apply();
            RenderTexture.active = oldRt;
            return SnapshotTexture.EncodeToPNG();
        }

        private static void GenerateSnapshot(BackgroundEntry entry)
        {
            if (entry == null) return;
            var sprite = Resources.Load<Sprite>(entry.resourcePath);
            if (sprite == null) return;
            var tex = sprite.texture;
            Graphics.Blit(tex, SnapshotRenderTexture, entry.snapshotScale, entry.snapshotOffset);
            var data = GetSnapshotPNGData();

            var assetFullPath = GetAssetFullPath(sprite);
            var snapshotFullPath =
                Path.Combine(GetResourcesFolder(assetFullPath), entry.snapshotResourcePath + ".png");
            File.WriteAllBytes(snapshotFullPath, data);
        }

        private BackgroundGroup Target => target as BackgroundGroup;

        public static void GenerateSnapshot(BackgroundGroup group)
        {
            if (group == null || group.entries.Count <= 0) return;
            GenerateSnapshot(group.entries[0]);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (GUILayout.Button("Generate Snapshot"))
            {
                GenerateSnapshot(Target);
                AssetDatabase.Refresh();
            }

            EditorGUILayout.HelpBox("The first background entry will be selected as the snapshot", MessageType.Info);

            reorderableList.DoLayoutList();
            if (reorderableList.index == -1 || reorderableList.index >= Target.entries.Count)
            {
                EditorGUILayout.LabelField(new GUIContent("Nothing Selected"));
            }
            else
            {
                DrawEntry(reorderableList.index);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}