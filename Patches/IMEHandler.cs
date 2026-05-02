using System;
using SDL2;
using HarmonyLib;
using Hacknet;
using Hacknet.Gui;
using Hacknet.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
namespace TAXCore.Patches
{
    public static class IMEHandler
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct COMPOSITIONFORM
        {
            public uint dwStyle;
            public POINT ptCurrentPos;
            public RECT rcArea;
        }
        private const uint CFS_POINT = 0x0002;
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")]
        private static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM lpCompForm);
        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_Rect { public int x, y, w, h; }
        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SDL_SetTextInputRect")]
        private static extern void SDL_SetTextInputRect(ref SDL_Rect rect);
        public static void UpdateIMEWindowPosition(int x, int y)
        {
            try
            {
                SDL_Rect rect = new SDL_Rect { x = x, y = y, w = 20, h = 20 };
                SDL_SetTextInputRect(ref rect);
                IntPtr hWnd = Game1.getSingleton().Window.Handle;
                IntPtr hIMC = ImmGetContext(hWnd);
                if (hIMC != IntPtr.Zero)
                {
                    COMPOSITIONFORM form = new COMPOSITIONFORM();
                    form.dwStyle = CFS_POINT;
                    form.ptCurrentPos = new POINT { x = x, y = y };
                    ImmSetCompositionWindow(hIMC, ref form);
                    ImmReleaseContext(hWnd, hIMC);
                }
            }
            catch {  }
        }
        private static bool initialized = false;
        public static bool IsChineseMode = true; 
        public static void Init()
        {
            if (initialized) return;
            initialized = true;
        }
        public static void ToggleMode()
        {
            IsChineseMode = !IsChineseMode;
            if (GuiData.lastInput != null)
            {
                Console.WriteLine("[IME] Mode: " + (IsChineseMode ? "System IME Enabled" : "System IME Disabled"));
            }
        }
        public static void OnDrawActiveField(SpriteBatch sb, SpriteFont font, int x, int y, string currentText)
        {
            SpriteFont drawFont = GuiData.font ?? font ?? GuiData.smallfont;
            string modeText = IsChineseMode ? "" : "";
            Color modeColor = IsChineseMode ? Color.Orange : Color.White * 0.5f;
            sb.DrawString(drawFont, modeText, new Vector2(x, y - 25), modeColor);
            if (!IsChineseMode) return;
            float cursorOffset = 0;
            if (drawFont != null && !string.IsNullOrEmpty(currentText))
            {
                int cursorArea = Math.Min(currentText.Length, TextBox.cursorPosition);
                int offset = Math.Max(0, TextBox.textDrawOffsetPosition);
                int relativeCursor = Math.Max(0, cursorArea - offset);
                if (relativeCursor >= 0 && offset + relativeCursor <= currentText.Length)
                {
                    string visibleTextBeforeCursor = currentText.Substring(offset, relativeCursor);
                    cursorOffset = drawFont.MeasureString(visibleTextBeforeCursor).X;
                }
            }
            UpdateIMEWindowPosition(x + (int)cursorOffset, y);
        }
    }
    [HarmonyPatch(typeof(GuiData), nameof(GuiData.doInput), new Type[] { typeof(InputState) })]
    public static class GuiDataInputPatch
    {
        private static bool shiftWasDown = false;
        [HarmonyPostfix]
        public static void Postfix(InputState input)
        {
            KeyboardState current = input.CurrentKeyboardStates[0];
            bool shiftIsDown = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
            if (shiftWasDown && !shiftIsDown)
            {
                IMEHandler.ToggleMode();
                TAXCore.PrintGradientAscii("[TAXCore] Chinese input method repair");
            }
            shiftWasDown = shiftIsDown;
        }
    }
    [HarmonyPatch(typeof(GuiData), nameof(GuiData.getFilteredKeys))]
    public static class GuiDataFilteredKeysPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref char[] __result)
        {
            var hookField = typeof(GuiData).GetField("TextInputHook", BindingFlags.NonPublic | BindingFlags.Static);
            if (hookField == null) return true;
            var hook = hookField.GetValue(null);
            if (hook == null) return true;
            var bufferProp = hook.GetType().GetProperty("Buffer");
            string text = (string)bufferProp.GetValue(hook);
            List<char> output = new List<char>();
            foreach (char c in text)
            {
                if (IMEHandler.IsChineseMode)
                {
                    if (c >= ' ' || c > 255) output.Add(c);
                }
                else
                {
                    if (c >= ' ' && c <= '~') output.Add(c);
                }
            }
            var clearMethod = hook.GetType().GetMethod("clearBuffer");
            clearMethod?.Invoke(hook, null);
            __result = output.ToArray();
            return false;
        }
    }
    [HarmonyPatch(typeof(TextBox), nameof(TextBox.doTerminalTextField))]
    public static class TerminalTextFieldIMEPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int myID, int x, int y, int width, int selectionHeight, int lines, string str, SpriteFont font)
        {
            if (GuiData.active == myID)
            {
                IMEHandler.OnDrawActiveField(GuiData.spriteBatch, font, x, y, str);
            }
        }
    }
    [HarmonyPatch(typeof(TextBox), nameof(TextBox.doTextBox))]
    public static class TextBoxIMEPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int myID, int x, int y, int width, int lines, string str, SpriteFont font)
        {
            if (GuiData.active == myID)
            {
                IMEHandler.OnDrawActiveField(GuiData.spriteBatch, font, x, y, str);
            }
        }
    }
}

