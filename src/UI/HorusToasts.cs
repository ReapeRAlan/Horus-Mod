using System.Collections.Generic;
using UnityEngine;

namespace HorusMod.UI
{
    public static class HorusToasts
    {
        private struct Toast
        {
            public string Text;
            public float Time;
        }

        private const int Capacity = 5;
        private const float VisibleSeconds = 4f;
        private static readonly Queue<Toast> ring = new Queue<Toast>(Capacity);
        private static string latestText;
        private static float latestTime;

        public static string Current
        {
            get
            {
                return !string.IsNullOrEmpty(latestText) && Time.unscaledTime - latestTime <= VisibleSeconds
                    ? latestText
                    : null;
            }
        }

        public static void Show(string message, bool gameMessage = true)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            while (ring.Count >= Capacity) ring.Dequeue();
            ring.Enqueue(new Toast { Text = message, Time = Time.unscaledTime });
            latestText = message;
            latestTime = Time.unscaledTime;
            if (gameMessage && SceneSingleton<GameplayUI>.i != null)
                SceneSingleton<GameplayUI>.i.GameMessage(message);
        }

        public static void Clear()
        {
            ring.Clear();
            latestText = null;
            latestTime = 0f;
        }
    }
}
