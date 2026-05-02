using HarmonyLib;
using Hacknet;
using System;
using System.Text;
namespace TAXCore.Patches
{
    [HarmonyPatch(typeof(Programs), nameof(Programs.cat))]
    public static class ProgramCatPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string[] args, OS os)
        {
            try
            {
                if (args == null || args.Length < 2 || os == null) return true;
                string target = args[1];
                Folder folder = null;
                string filename = target;
                if (target.Contains("/") || target.Contains("\\"))
                {
                    string[] parts = target.Split(Utils.directorySplitterDelim, StringSplitOptions.RemoveEmptyEntries);
                    filename = parts[parts.Length - 1];
                    string path = target.Substring(0, Math.Max(0, target.Length - filename.Length - 1));
                    Computer comp = os.connectedComp ?? os.thisComputer;
                    if (comp != null && comp.files != null)
                    {
                        folder = Programs.getFolderAtPath(path, os, comp.files.root, false);
                    }
                }
                if (folder == null) folder = Programs.getCurrentFolder(os);
                if (folder == null) return true;
                FileEntry file = folder.searchForFile(filename);
                if (file != null && file.data != null)
                {
                    if (file.data.StartsWith("#JAVAF#:") || file.data.StartsWith("#PYF#:") || file.data.StartsWith("#QU_ISO#:") || file.data.StartsWith("JFILE_PATH:") || file.data.StartsWith("#VIDEOF#:"))
                    {
                        string binaryData = Computer.generateBinaryString(300);
                        os.write(string.Concat(new object[]
                        {
                            file.name,
                            " : ",
                            (double)file.size / 1000.0,
                            "kb\n",
                            binaryData,
                            "\n"
                        }));
                        StringBuilder wrappedData = new StringBuilder();
                        for (int j = 0; j < binaryData.Length; j++)
                        {
                            wrappedData.Append(binaryData[j]);
                            if ((j + 1) % 50 == 0 && (j + 1) < binaryData.Length) wrappedData.Append('\n');
                        }
                        os.displayCache = wrappedData.ToString();
                        if (os.display != null)
                        {
                            os.display.command = "cat";
                            os.display.commandArgs = args;
                            os.display.LastDisplayedFileFolder = folder;
                            os.display.LastDisplayedFileSourceIP = (os.connectedComp != null) ? os.connectedComp.ip : os.thisComputer.ip;
                        }
                        return false; 
                    }
                }
            }
            catch (Exception)
            {
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(DisplayModule), "doCatDisplay")]
    public static class DisplayModuleWrapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(DisplayModule __instance)
        {
            OS os = __instance.os;
            if (os != null && !string.IsNullOrEmpty(os.displayCache))
            {
                if (os.displayCache.Length > 60 && !os.displayCache.Contains(" ") && !os.displayCache.Contains("\n"))
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < os.displayCache.Length; i++)
                    {
                        sb.Append(os.displayCache[i]);
                        if ((i + 1) % 50 == 0 && (i + 1) < os.displayCache.Length) sb.Append('\n');
                    }
                    os.displayCache = sb.ToString();
                }
            }
        }
    }
}

