﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace Nova
{
    // object can be string or string[]
    //
    // For the i-th (zero-based) string in the string[],
    // it will be used as the translation if the first argument provided to __ is i
    //
    // Example:
    // "ivebeenthere": ["I've never been there", "I've been there once", "I've been there twice", "I've been there {0} times"]
    // __("ivebeenthere", 2) == "I've been there twice"
    // __("ivebeenthere", 4) == "I've been there 4 times"
    using TranslationBundle = Dictionary<string, object>;

    [ExportCustomType]
    public static class I18n
    {
        public const string LocalePath = "Locales/";

        public static readonly SystemLanguage[] SupportedLocales =
            {SystemLanguage.ChineseSimplified, SystemLanguage.English};

        public static SystemLanguage DefaultLocale => SupportedLocales[0];

        private static SystemLanguage _currentLocale = Application.systemLanguage;

        public static SystemLanguage CurrentLocale
        {
            get => _currentLocale;
            set
            {
                _currentLocale = value;
                Init();
                FallbackLocale();
                LocaleChanged.Invoke();
            }
        }

        private static void FallbackLocale()
        {
            if (_currentLocale == SystemLanguage.Chinese || _currentLocale == SystemLanguage.ChineseSimplified ||
                _currentLocale == SystemLanguage.ChineseTraditional)
            {
                _currentLocale = SystemLanguage.ChineseSimplified;
            }
            else
            {
                _currentLocale = SystemLanguage.English;
            }
        }

        public static readonly UnityEvent LocaleChanged = new UnityEvent();

        private static bool Inited = false;

        private static void Init()
        {
            if (Inited) return;
            LoadTranslationBundles();
            Inited = true;
        }

        private static readonly Dictionary<SystemLanguage, TranslationBundle> TranslationBundles =
            new Dictionary<SystemLanguage, TranslationBundle>();

        private static void LoadTranslationBundles()
        {
            foreach (var locale in SupportedLocales)
            {
#if UNITY_EDITOR
                var text = File.ReadAllText(EditorPathRoot + locale + ".json");
                TranslationBundles[locale] = JsonConvert.DeserializeObject<TranslationBundle>(text);
#else
                var textAsset = Resources.Load(LocalePath + locale) as TextAsset;
                TranslationBundles[locale] = JsonConvert.DeserializeObject<TranslationBundle>(textAsset.text);
#endif
            }
        }

        /// <summary>
        /// Get the translation specified by key and optionally deal with the plurals and format arguments. (Shorthand)<para />
        /// Translation will be automatically reloaded if the JSON file is changed.
        /// </summary>
        /// <param name="locale"></param>
        /// <param name="key">Key to specify the translation</param>
        /// <param name="args">Arguments to provide to the translation as a format string.<para />
        /// The first argument will be used to determine the quantity if needed.</param>
        /// <returns>The translated string.</returns>
        public static string __(SystemLanguage locale, string key, params object[] args)
        {
#if UNITY_EDITOR
            EditorOnly_GetLatestTranslation();
#endif

            Init();

            string translation = key;

            if (TranslationBundles[locale].TryGetValue(key, out var raw))
            {
                if (raw is string value)
                {
                    translation = value;
                }
                else if (raw is string[] formats)
                {
                    if (formats.Length == 0)
                    {
                        Debug.LogWarningFormat("Nova: Empty translation string list for: {0}", key);
                    }
                    else if (args.Length == 0)
                    {
                        translation = formats[0];
                    }
                    else
                    {
                        // The first argument will determine the quantity
                        object arg1 = args[0];
                        if (arg1 is int i)
                        {
                            translation = formats[Math.Min(i, formats.Length - 1)];
                        }
                    }
                }
                else
                {
                    Debug.LogWarningFormat("Nova: Invalid translation format for: {0}", key);
                }

                if (args.Length > 0)
                {
                    translation = string.Format(translation, args);
                }
            }
            else
            {
                Debug.LogWarningFormat("Nova: Missing translation for: {0}", key);
            }

            return translation;
        }

        public static string __(string key, params object[] args)
        {
            return __(CurrentLocale, key, args);
        }

        // Get localized string with fallback to DefaultLocale
        public static string __(Dictionary<SystemLanguage, string> dict)
        {
            if (dict.ContainsKey(CurrentLocale))
            {
                return dict[CurrentLocale];
            }
            else
            {
                return dict[DefaultLocale];
            }
        }

#if UNITY_EDITOR
        private static string EditorPathRoot => "Assets/Nova/Resources/" + LocalePath;

        private static string EditorTranslationPath => EditorPathRoot + CurrentLocale + ".json";

        private static DateTime LastWriteTime;

        private static void EditorOnly_GetLatestTranslation()
        {
            var writeTime = File.GetLastWriteTime(EditorTranslationPath);
            if (writeTime != LastWriteTime)
            {
                LastWriteTime = writeTime;
                Inited = false;
                Init();
            }
        }
#endif
    }
}