using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace ApexDrive
{
    /// <summary>Keyboard wrapper that works whether the project uses the old Input Manager, the new Input System, or both.</summary>
    public static class Inp
    {
        public static bool Held(KeyCode k)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(k);
#elif ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; Key key = Map(k);
            return kb != null && key != Key.None && kb[key].isPressed;
#else
            return false;
#endif
        }

        public static bool Pressed(KeyCode k)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(k);
#elif ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; Key key = Map(k);
            return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static Key Map(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.W: return Key.W;
                case KeyCode.S: return Key.S;
                case KeyCode.A: return Key.A;
                case KeyCode.D: return Key.D;
                case KeyCode.Q: return Key.Q;
                case KeyCode.E: return Key.E;
                case KeyCode.V: return Key.V;
                case KeyCode.R: return Key.R;
                case KeyCode.P: return Key.P;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2;
                case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4;
                case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.T: return Key.T;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Y: return Key.Y;
                case KeyCode.N: return Key.N;
                case KeyCode.B: return Key.B;
                case KeyCode.Z: return Key.Z;
                case KeyCode.X: return Key.X;
                case KeyCode.C: return Key.C;
                case KeyCode.F: return Key.F;
                case KeyCode.M: return Key.M;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.L: return Key.L;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                default: return Key.None;
            }
        }
#endif
    }
}
