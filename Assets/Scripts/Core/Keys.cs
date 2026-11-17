using UnityEngine;

namespace RH
{
    /// <summary>Rebindable controls, stored in PlayerPrefs.</summary>
    public static class Keys
    {
        public enum Action { Forward, Back, Left, Right, Sprint, Interact, Tablet, Drop, Camera }

        public static readonly string[] Names = { "Move forward", "Move back", "Strafe left", "Strafe right", "Sprint", "Interact", "Ticket tablet", "Drop carried item", "Camera" };
        static readonly KeyCode[] Defaults = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.LeftShift, KeyCode.E, KeyCode.Tab, KeyCode.G, KeyCode.F };
        static KeyCode[] map;

        static KeyCode[] Map
        {
            get
            {
                if (map != null) return map;
                map = (KeyCode[])Defaults.Clone();
                for (int i = 0; i < map.Length; i++) map[i] = (KeyCode)PlayerPrefs.GetInt("key_" + (Action)i, (int)Defaults[i]);
                return map;
            }
        }

        public static KeyCode Get(Action a) => Map[(int)a];

        public static void Set(Action a, KeyCode k)
        {
            Map[(int)a] = k;
            PlayerPrefs.SetInt("key_" + a, (int)k);
        }

        public static void Reset()
        {
            for (int i = 0; i < Defaults.Length; i++) Set((Action)i, Defaults[i]);
        }

        public static bool Down(Action a) => Input.GetKeyDown(Get(a));
        public static bool Held(Action a) => Input.GetKey(Get(a));

        /// <summary>Short label for prompts, e.g. "E", "Tab", "LShift".</summary>
        public static string Label(Action a) => Label(Get(a));

        public static string Label(KeyCode k) => k switch
        {
            KeyCode.LeftShift => "LShift",
            KeyCode.RightShift => "RShift",
            KeyCode.LeftControl => "LCtrl",
            KeyCode.RightControl => "RCtrl",
            KeyCode.LeftAlt => "LAlt",
            KeyCode.Mouse0 => "LMB",
            KeyCode.Mouse1 => "RMB",
            KeyCode.Mouse2 => "MMB",
            _ when k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9 => ((int)(k - KeyCode.Alpha0)).ToString(),
            _ => k.ToString(),
        };
    }
}
