using HarmonyLib;
using Hacknet;
using TAXCore.Executibles;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Pathfinder.Executable;
namespace TAXCore.Patches
{
    [HarmonyPatch(typeof(OS), "drawModules")]
    public static class OSDrawModulesPatch
    {
        private static readonly List<ExeModule> hiddenExes = new List<ExeModule>();
        [HarmonyPrefix]
        public static void Prefix(OS __instance)
        {
            hiddenExes.Clear();
            for (int i = 0; i < __instance.exes.Count; i++)
            {
                if (__instance.exes[i] is VimExecutable)
                {
                    hiddenExes.Add(__instance.exes[i]);
                    __instance.exes.RemoveAt(i);
                }
            }
        }
        [HarmonyPostfix]
        public static void Postfix(OS __instance)
        {
            for (int i = 0; i < hiddenExes.Count; i++)
            {
                var exe = hiddenExes[i];
                __instance.exes.Add(exe);
                exe.bounds = __instance.fullscreen;
                exe.Draw(0.016f);
            }
            hiddenExes.Clear();
        }
    }
    [HarmonyPatch(typeof(OS), "addExe")]
    public static class OSAddExePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(OS __instance, GameExecutable exe)
        {
            if (exe is VimExecutable vim)
            {
                vim.os = __instance;
            }
            return true;
        }
    }
}

