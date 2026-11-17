using System.Collections.Generic;
using UnityEngine;

namespace RH
{
    /// <summary>Procedurally synthesized sound: the data hall's fan roar plus UI and work sound effects.</summary>
    public class Audio : MonoBehaviour
    {
        static Audio inst;
        const int Rate = 44100;
        AudioSource hum, sfx;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        float lastClick;

        public static float Volume = 0.8f;
        /// <summary>How loud the fan roar should be where the player stands (0..1), set by the game each frame.</summary>
        public static float HumLevel = 0.5f;

        void Awake()
        {
            inst = this;
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = MakeHum();
            hum.loop = true;
            hum.volume = 0.25f;
            hum.Play();
            sfx = gameObject.AddComponent<AudioSource>();
            clips["click"] = Tone(0.03f, t => Sq(t, 1800) * 0.25f * Env(t, 0.03f));
            clips["beep"] = Tone(0.12f, t => Sine(t, 1320) * 0.35f * Env(t, 0.12f));
            clips["ticket"] = Tone(0.42f, t => (t < 0.14f ? Sine(t, 880) : t < 0.28f ? Sine(t, 1175) : Sine(t, 1568)) * 0.35f * Env(t, 0.42f));
            clips["good"] = Tone(0.35f, t => (Sine(t, t < 0.15f ? 660 : 990) + 0.4f * Sine(t, t < 0.15f ? 1320 : 1980)) * 0.3f * Env(t, 0.35f));
            clips["bad"] = Tone(0.38f, t => Sq(t, t < 0.18f ? 180 : 140) * 0.22f * Env(t, 0.38f));
            clips["clunk"] = Tone(0.18f, t => (Noise() * 0.6f + Sine(t, 70)) * 0.5f * Mathf.Exp(-t * 28f));
            clips["screw"] = Tone(0.22f, t => (Noise() * 0.3f + Sq(t, 340 + t * 900)) * 0.18f * Env(t, 0.22f));
            clips["plug"] = Tone(0.08f, t => (Noise() * 0.5f + Sine(t, 2400)) * 0.4f * Mathf.Exp(-t * 60f));
            clips["pickup"] = Tone(0.15f, t => (Noise() * 0.4f + Sine(t, 220)) * 0.35f * Mathf.Exp(-t * 20f));
            clips["shutter"] = Tone(0.16f, t => Noise() * 0.5f * (t < 0.03f ? 1f : t > 0.09f && t < 0.12f ? 0.8f : 0.05f));
            clips["alarm"] = Tone(0.6f, t => Sine(t, (int)(t / 0.15f) % 2 == 0 ? 990 : 740) * 0.3f * Env(t, 0.6f));
        }

        void Update()
        {
            AudioListener.volume = Volume;
            hum.volume = Mathf.MoveTowards(hum.volume, 0.08f + HumLevel * 0.32f, Time.unscaledDeltaTime * 0.4f);
        }

        public static void Play(string name, float vol = 1f)
        {
            if (inst == null || !inst.clips.TryGetValue(name, out var c)) return;
            if (name == "click") { if (Time.unscaledTime - inst.lastClick < 0.05f) return; inst.lastClick = Time.unscaledTime; }
            inst.sfx.PlayOneShot(c, vol);
        }

        static readonly System.Random rnd = new System.Random(7);
        static float Noise() => (float)rnd.NextDouble() * 2f - 1f;
        static float Sine(float t, float f) => Mathf.Sin(2 * Mathf.PI * f * t);
        static float Sq(float t, float f) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t));
        static float Env(float t, float len) => Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((len - t) / (len * 0.6f));

        static AudioClip Tone(float len, System.Func<float, float> f)
        {
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)Rate);
            var c = AudioClip.Create("sfx", n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip MakeHum()
        {
            int n = Rate * 4;
            var data = new float[n];
            float brown = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                brown = Mathf.Clamp(brown + Noise() * 0.04f, -1f, 1f) * 0.995f;
                data[i] = brown * 0.5f + Sine(t, 120) * 0.04f + Sine(t, 240) * 0.02f + Noise() * 0.03f;
            }
            // Crossfade the ends so the loop is seamless.
            int fade = Rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float a = i / (float)fade;
                data[i] = data[i] * a + data[n - fade + i] * (1 - a);
            }
            var c = AudioClip.Create("hum", n - fade, 1, Rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            c.SetData(trimmed, 0);
            return c;
        }
    }
}
