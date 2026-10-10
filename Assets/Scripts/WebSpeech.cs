using System.Runtime.InteropServices;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>The browser's own speech synthesis, used for the radio and commentators in the WebGL build (Piper cannot run in a browser).</summary>
    public static class WebSpeech
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ApexSpeak(string text, int kind);
        [DllImport("__Internal")] static extern void ApexSpeakStop();
        public static void Speak(string text, int kind) { ApexSpeak(text, kind); }
        public static void Stop() { ApexSpeakStop(); }
#else
        public static void Speak(string text, int kind) { }
        public static void Stop() { }
#endif
    }

    /// <summary>Fonts: the OS fonts of a desktop machine do not exist in a browser, so there the built-in font is used.</summary>
    public static class FontKit
    {
        public static Font Make(string[] names, int size)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            return Font.CreateDynamicFontFromOSFont(names, size);
#endif
        }
    }
}
