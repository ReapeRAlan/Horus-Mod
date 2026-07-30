using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace HorusMod.Data
{
    public static class HorusPrefs
    {
        public static readonly Rect DefaultWindowRect = new Rect(20f, 20f, 430f, 660f);

        private static ConfigEntry<float> x;
        private static ConfigEntry<float> y;
        private static ConfigEntry<float> width;
        private static ConfigEntry<float> height;
        private static ConfigEntry<string> favoriteKeys;
        private static ConfigEntry<string> recentKeys;
        private static readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> recents = new List<string>();

        public static IReadOnlyList<string> Recents => recents;

        public static void Bind(ConfigFile config)
        {
            x = config.Bind("Window", "X", DefaultWindowRect.x, "Horus window X position.");
            y = config.Bind("Window", "Y", DefaultWindowRect.y, "Horus window Y position.");
            width = config.Bind("Window", "Width", DefaultWindowRect.width, "Horus window width.");
            height = config.Bind("Window", "Height", DefaultWindowRect.height, "Horus window height.");
            favoriteKeys = config.Bind("Browser", "Favorites", "", "Semicolon-delimited UnitDefinition jsonKeys.");
            recentKeys = config.Bind("Browser", "Recent", "", "Most recently spawned UnitDefinition jsonKeys.");
            LoadSet(favoriteKeys.Value, favorites);
            recents.Clear();
            foreach (string key in Split(recentKeys.Value))
                if (!recents.Contains(key)) recents.Add(key);
        }

        public static Rect LoadWindow()
        {
            if (x == null) return DefaultWindowRect;
            return new Rect(x.Value, y.Value, Mathf.Max(360f, width.Value), Mathf.Max(320f, height.Value));
        }

        public static void SaveWindow(Rect rect)
        {
            if (x == null) return;
            x.Value = rect.x;
            y.Value = rect.y;
            width.Value = rect.width;
            height.Value = rect.height;
        }

        public static Rect ResetWindow()
        {
            SaveWindow(DefaultWindowRect);
            return DefaultWindowRect;
        }

        public static bool IsFavorite(string key) => !string.IsNullOrEmpty(key) && favorites.Contains(key);

        public static bool ToggleFavorite(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool nowFavorite = !favorites.Remove(key);
            if (nowFavorite) favorites.Add(key);
            if (favoriteKeys != null) favoriteKeys.Value = string.Join(";", favorites);
            UnitCatalog.InvalidateQueries();
            return nowFavorite;
        }

        public static void AddRecent(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            recents.RemoveAll(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
            recents.Insert(0, key);
            if (recents.Count > 8) recents.RemoveRange(8, recents.Count - 8);
            if (recentKeys != null) recentKeys.Value = string.Join(";", recents);
        }

        private static void LoadSet(string value, HashSet<string> destination)
        {
            destination.Clear();
            foreach (string key in Split(value)) destination.Add(key);
        }

        private static IEnumerable<string> Split(string value)
        {
            return (value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
