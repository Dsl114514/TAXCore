using BepInEx;
using BepInEx.Hacknet;
using Hacknet;
using System.Text;
using System;
using System.IO;
using System.Threading;
using System.Xml;
using Hacknet.Effects;
using HarmonyLib;
using Pathfinder.Daemon;
using Hacknet.Gui;
using Hacknet.Misc;
using Hacknet.PlatformAPI.Storage;
using Hacknet.Screens;
using Hacknet.UIUtils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.InteropServices;
using Pathfinder.Options;
using System.Linq;
using Pathfinder.Action;
using Pathfinder.Event;
using Pathfinder.Event.Saving;
using Pathfinder.Executable;
using Pathfinder.Port;
using Pathfinder.Replacements;
using Pathfinder.Util.XML;
using System.Xml.Linq;
using TAXCore.Actions;
using TAXCore.Executibles;
using TAXCore.Patches;
using TAXCore.Config;
using TAXCore.Themes;


namespace TAXCore
{
    [BepInPlugin(ModGUID, ModName, ModVer)]
    public class TAXCore : HacknetPlugin
    {
        public const string ModGUID = "com.TAXCore.Dsl";
        public const string ModName = "TAXCore";
        public const string ModVer = "1.5.6";

        string taxCoreArt = $@"
    /-------------------------------------------------------------------------------------/\    
   / \      _________  ________     ___    ___ ________  ________  ________  _______     /  \
  /   \    |\___   ___\\   __  \   |\  \  /  /|\   ____\|\   __  \|\   __  \|\  ___ \   /    \
  \    \   \|___ \  \_\ \  \|\  \  \ \  \/  / | \  \___|\ \  \|\  \ \  \|\  \ \   __/|   \    \
   \    \       \ \  \ \ \   __  \  \ \    / / \ \  \    \ \  \\\  \ \   _  _\ \  \_|/__  \    \
    \    \       \ \  \ \ \  \ \  \  /     \/   \ \  \____\ \  \\\  \ \  \\  \\ \  \_|\ \  \    \
     \    \       \ \__\ \ \__\ \__\/  /\   \    \ \_______\ \_______\ \__\\ _\\ \_______\  \    \
      \    \       \|__|  \|__|\|__/__/ /\ __\    \|_______|\|_______|\|__|\|__|\|_______|   \    \
       \    \                      |__|/ \|__|                                                \    \ 
        \    \                                  Version-1.5.6                                  \    \     
         \    \                                                                                 \    \     
          \    /---------------------------------------------------------------------------------\----/
           \  /                                                                                   \  /
            \/-------------------------------------------------------------------------------------\/  
";
        public override bool Load()
        {
            
            string configFileName = "TAXCore_Config.json";
            string configPath = null;

            
            if (Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null)
            {
                string extPath = Path.Combine(Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath, configFileName);
                if (File.Exists(extPath)) configPath = extPath;
            }

            
            if (configPath == null)
            {
                string assemblyLocation = typeof(TAXCore).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    string pluginDir = Path.GetDirectoryName(assemblyLocation);
                    string testPath = Path.Combine(pluginDir, configFileName);
                    if (File.Exists(testPath)) configPath = testPath;
                }
            }

            
            if (configPath == null)
            {
                string gameRoot = BepInEx.Paths.GameRootPath;
                string testPath = Path.Combine(gameRoot, configFileName);
                if (File.Exists(testPath)) configPath = testPath;
            }

            
            if (configPath == null)
            {
                configPath = Path.Combine(Paths.PluginPath, configFileName);
            }

            TAXCoreConfig.Load(configPath);

            Console.WriteLine($"[TAXCore] Config loaded from: {configPath ?? "None (using defaults)"}");
            Console.WriteLine($"[TAXCore] JavaPath: {TAXCoreConfig.JavaPath}");
            Console.WriteLine($"[TAXCore] QemuPath: {TAXCoreConfig.QemuPath}");
            Console.WriteLine($"[TAXCore] FFmpegPath: {TAXCoreConfig.FFmpegPath}");

            
            var harmony = new Harmony("com.TAXCore.MainMenuText");
            harmony.PatchAll();

            
            global::TAXCore.Patches.IMEHandler.Init();
            global::TAXCore.Themes.DynamicThemeManager.Init();
            
            ActionManager.RegisterAction<FullscreenNetmapAction>("FullscreenNetmap");
            ActionManager.RegisterAction<RestoreUIAction>("RestoreUI");
            ActionManager.RegisterAction<SetTopMostAction>("SetTopMost");
            ActionManager.RegisterAction<PlayImageSequenceAction>("PlayImageSequence");
            ActionManager.RegisterAction<StopImageSequenceAction>("StopImageSequence");
            PortManager.RegisterPort("smb", "Server Message Block", 445);
            PortManager.RegisterPort("msg", "MSG Authentication", 31);

            
            ExecutableManager.RegisterExecutable<PythonExe>("#PYTHON#");
            
            ExecutableManager.RegisterExecutable<JavaExecutable>("#JAVA#");

            
            ExecutableManager.RegisterExecutable<VimExecutable>("#VIM#");

            
            ExecutableManager.RegisterExecutable<QemuExecutable>("#QEMU#");
            ExecutableManager.RegisterExecutable<VideoExecutable>("#VIDEOU#");
            
            EventManager<Pathfinder.Event.Loading.ExtensionLoadEvent>.AddHandler(OnExtensionLoad);

            PrintGradientAscii(taxCoreArt);
            return true;
        }
        

        private void OnExtensionLoad(Pathfinder.Event.Loading.ExtensionLoadEvent e)
        {
            if (e.Unload) return; 

            string configFileName = "TAXCore_Config.json";
            string extConfigPath = Path.Combine(e.Info.FolderPath, configFileName);
            if (File.Exists(extConfigPath))
            {
                TAXCoreConfig.Load(extConfigPath);
                Console.WriteLine("[TAXCore] Extension config loaded: " + extConfigPath);
            }
        }

        private static void PrintSolidColorAscii(string art, int r, int g, int b)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool ansi = EnableAnsiColors();
            string[] lines = art.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                if (ansi)
                {
                    Console.Write("\x1b[38;2;" + r + ";" + g + ";" + b + "m" + line);
                    Console.Write("\x1b[0m");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(line);
                }
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        public static void PrintVerticalGradientAscii(string art, int r1, int g1, int b1, int r2, int g2, int b2)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool ansi = EnableAnsiColors();
            string[] lines = art.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int r = 0; r < lines.Length; r++)
            {
                string line = lines[r];
                float t = lines.Length <= 1 ? 0f : (float)r / (float)(lines.Length - 1);
                int currR = (int)(r1 * (1 - t) + r2 * t);
                int currG = (int)(g1 * (1 - t) + g2 * t);
                int currB = (int)(b1 * (1 - t) + b2 * t);
                for (int c = 0; c < line.Length; c++)
                {
                    if (ansi)
                    {
                        Console.Write("\x1b[38;2;" + currR + ";" + currG + ";" + currB + "m" + line[c]);
                    }
                    else
                    {
                        Console.ForegroundColor = t < 0.5f ? ConsoleColor.Red : ConsoleColor.Blue;
                        Console.Write(line[c]);
                    }
                }
                if (ansi) Console.Write("\x1b[0m");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        private static bool EnableAnsiColors()
        {
            try
            {
                IntPtr handle = GetStdHandle(-11);
                uint mode;
                if (!GetConsoleMode(handle, out mode)) return false;
                const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
                uint newMode = mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                return SetConsoleMode(handle, newMode);
            }
            catch { return false; }
        }
        public static void PrintGradientAscii(string art)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool ansi = EnableAnsiColors();
            string[] lines = art.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int maxLen = 0;
            for (int i = 0; i < lines.Length; i++) if (lines[i].Length > maxLen) maxLen = lines[i].Length;
            for (int r = 0; r < lines.Length; r++)
            {
                string line = lines[r];
                for (int c = 0; c < line.Length; c++)
                {
                    float t = maxLen <= 1 ? 0f : (float)c / (float)(maxLen - 1);
                    int r_val = (int)(255f * (1 - t)); 
                    int g_val = (int)(255f * (1 - t));
                    int b_val = (int)(255f * t);
                    if (ansi)
                    {
                        Console.Write("\x1b[38;2;" + r_val + ";" + g_val + ";" + b_val + "m" + line[c]);
                    }
                    else
                    {
                        
                        ConsoleColor col = t < 0.5f ? ConsoleColor.Yellow : ConsoleColor.Blue;
                        Console.ForegroundColor = col;
                        Console.Write(line[c]);
                    }
                }
                if (ansi) Console.Write("\x1b[0m");
                Console.ResetColor();
                Console.WriteLine();
            }
        }
    }
}
