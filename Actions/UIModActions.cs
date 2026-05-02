using System;
using System.Collections.Generic;
using Hacknet;
using Hacknet.Gui;
using Pathfinder.Action;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder.Util;
using HarmonyLib;
namespace TAXCore.Actions
{
    public abstract class DelayableAction : PathfinderAction
    {
        [XMLStorage]
        public float Delay = 0f;
        public override void Trigger(object os_obj)
        {
            OS os = (OS)os_obj;
            if (Delay <= 0f)
            {
                Execute(os);
            }
            else
            {
                DateTime startTime = DateTime.Now;
                os.delayer.Post(d => (d.Time - startTime).TotalSeconds >= Delay, () => Execute(os));
            }
        }
        public abstract void Execute(OS os);
    }
    [HarmonyPatch]
    public static class UILayoutManager
    {
        public static Dictionary<Module, Rectangle> OriginalBounds = new Dictionary<Module, Rectangle>();
        public static Dictionary<Module, bool> OriginalVisibility = new Dictionary<Module, bool>();
        public static bool IsCustomLayoutActive = false;
        public static void SaveLayout(OS os)
        {
            OriginalBounds.Clear();
            OriginalVisibility.Clear();
            foreach (var mod in os.modules)
            {
                Rectangle logicBounds = mod.bounds;
                logicBounds.Y -= Module.PANEL_HEIGHT;
                logicBounds.Height += Module.PANEL_HEIGHT;
                OriginalBounds[mod] = logicBounds;
                OriginalVisibility[mod] = mod.visible;
            }
            IsCustomLayoutActive = true;
        }
        public static void RestoreLayout(OS os)
        {
            if (!IsCustomLayoutActive) return;
            foreach (var mod in os.modules)
            {
                if (OriginalBounds.ContainsKey(mod))
                    mod.Bounds = OriginalBounds[mod];
                if (OriginalVisibility.ContainsKey(mod))
                    mod.visible = OriginalVisibility[mod];
            }
            IsCustomLayoutActive = false;
        }
        [HarmonyPatch(typeof(Module), nameof(Module.drawFrame))]
        public static bool Prefix_Module_drawFrame(Module __instance)
        {
            if (IsCustomLayoutActive)
            {
                if (__instance is Terminal) return false;
                if (__instance is NetworkMap)
                {
                    Rectangle fullRect = __instance.bounds;
                    fullRect.Y -= Module.PANEL_HEIGHT;
                    fullRect.Height += Module.PANEL_HEIGHT;
                    __instance.spriteBatch.Draw(Utils.white, fullRect, __instance.os.moduleColorBacking * 0.3f);
                    
                    Rectangle headerRect = new Rectangle(fullRect.X, fullRect.Y, fullRect.Width, Module.PANEL_HEIGHT);
                    __instance.spriteBatch.Draw(Utils.white, headerRect, __instance.os.moduleColorStrong * 0.3f);
                    __instance.spriteBatch.DrawString(GuiData.detailfont, __instance.name, new Vector2((float)(headerRect.X + 2), (float)(headerRect.Y + 2)), __instance.os.semiTransText);
                    return false;
                }
            }
            return true;
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.Draw))]
        public static bool Prefix_Terminal_Draw(Terminal __instance, float t)
        {
            if (!IsCustomLayoutActive) return true;
            float tinyFontCharHeight = GuiData.tinyfont.MeasureString("W").Y;
            int num = (int)((float)(__instance.bounds.Height - 12) / (tinyFontCharHeight + 1f));
            num -= 3;
            num = Math.Min(num, __instance.history.Count);
            Vector2 input = new Vector2((float)(__instance.bounds.X + 4), (float)(__instance.bounds.Y + __instance.bounds.Height) - tinyFontCharHeight * 5f);
            if (num > 0)
            {
                for (int i = __instance.history.Count; i > __instance.history.Count - num; i--)
                {
                    try
                    {
                        __instance.spriteBatch.DrawString(GuiData.tinyfont, __instance.history[i - 1], Utils.ClipVec2ForTextRendering(input), __instance.os.terminalTextColor);
                        input.Y -= tinyFontCharHeight + 1f;
                    }
                    catch (Exception) { }
                }
            }
            __instance.doGui();
            return false;
        }
    }

    public class FullscreenNetmapAction : DelayableAction
    {
        public override void Execute(OS os)
        {
            UILayoutManager.SaveLayout(os);
            Viewport viewport = os.ScreenManager.GraphicsDevice.Viewport;
            os.ram.visible = false;
            int displayWidth = 400;
            int netMapWidth = viewport.Width - displayWidth;
            int mainHeight = viewport.Height - OS.TOP_BAR_HEIGHT;
            os.display.visible = true;
            os.display.Bounds = new Rectangle(netMapWidth, OS.TOP_BAR_HEIGHT, displayWidth, mainHeight);
            os.netMap.visible = true;
            os.netMap.Bounds = new Rectangle(0, OS.TOP_BAR_HEIGHT, netMapWidth, mainHeight);
            os.terminal.visible = true;
        }
    }

    public class RestoreUIAction : DelayableAction
    {
        public override void Execute(OS os)
        {
            UILayoutManager.RestoreLayout(os);
        }
    }
}
