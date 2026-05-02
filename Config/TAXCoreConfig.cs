using System.IO;
using System.Text.RegularExpressions;
namespace TAXCore.Config
{
    public static class TAXCoreConfig
    {
        public static string JavaPath { get; private set; } = "java"; 
        public static string QemuPath { get; private set; } = @"D:\Program Files\qemu\qemu-system-x86_64.exe";
        public static string FFmpegPath { get; private set; } = "ffmpeg";
        public static string FFplayPath { get; private set; } = "ffplay";
        public static int WallpaperFPS { get; private set; } = 30;
        public static bool UseStaticWallpaper { get; private set; } = false;
        public static bool UseVRAMCache { get; private set; } = true; 
        public static float VideoScale { get; private set; } = 0.5f; 
        public static void Load(string configPath)
        {
            if (string.IsNullOrEmpty(configPath)) return;
            if (File.Exists(configPath))
            {
                string content = File.ReadAllText(configPath);
                string configDir = Path.GetDirectoryName(configPath);
                string gameRoot = BepInEx.Paths.GameRootPath;
                var javaMatch = Regex.Match(content, "\"JavaPath\"\\s*:\\s*\"(.*?)\"");
                if (javaMatch.Success)
                {
                    string path = javaMatch.Groups[1].Value.Replace("\\\\", "\\");
                    JavaPath = ResolvePath(path, configDir, gameRoot);
                }
                var qemuMatch = Regex.Match(content, "\"QemuPath\"\\s*:\\s*\"(.*?)\"");
                if (qemuMatch.Success)
                {
                    string path = qemuMatch.Groups[1].Value.Replace("\\\\", "\\");
                    string resolved = ResolvePath(path, configDir, gameRoot);
                    if (Directory.Exists(resolved))
                    {
                        QemuPath = Path.Combine(resolved, "qemu-system-x86_64.exe");
                    }
                    else
                    {
                        QemuPath = resolved;
                    }
                }
                var ffmpegMatch = Regex.Match(content, "\"FFmpegPath\"\\s*:\\s*\"(.*?)\"");
                if (ffmpegMatch.Success)
                {
                    string path = ffmpegMatch.Groups[1].Value.Replace("\\\\", "\\");
                    string resolved = ResolvePath(path, configDir, gameRoot);
                    if (Directory.Exists(resolved))
                    {
                        FFmpegPath = Path.Combine(resolved, "ffmpeg.exe");
                        FFplayPath = Path.Combine(resolved, "ffplay.exe");
                    }
                    else
                    {
                        FFmpegPath = resolved;
                        FFplayPath = Path.Combine(Path.GetDirectoryName(resolved) ?? "", "ffplay.exe");
                    }
                }
                var fpsMatch = Regex.Match(content, "\"WallpaperFPS\"\\s*:\\s*(\\d+)");
                if (fpsMatch.Success)
                {
                    if (int.TryParse(fpsMatch.Groups[1].Value, out int fps))
                    {
                        WallpaperFPS = fps;
                    }
                }
                var staticMatch = Regex.Match(content, "\"UseStaticWallpaper\"\\s*:\\s*(true|false)");
                if (staticMatch.Success)
                {
                    if (bool.TryParse(staticMatch.Groups[1].Value, out bool useStatic))
                    {
                        UseStaticWallpaper = useStatic;
                    }
                }
                var vramMatch = Regex.Match(content, "\"UseVRAMCache\"\\s*:\\s*(true|false)");
                if (vramMatch.Success)
                {
                    if (bool.TryParse(vramMatch.Groups[1].Value, out bool useVram))
                    {
                        UseVRAMCache = useVram;
                    }
                }
                var scaleMatch = Regex.Match(content, "\"VideoScale\"\\s*:\\s*([0-9.]+)");
                if (scaleMatch.Success)
                {
                    if (float.TryParse(scaleMatch.Groups[1].Value, out float scale))
                    {
                        VideoScale = scale;
                    }
                }
            }
        }
        private static string ResolvePath(string path, string configDir, string gameRoot)
        {
            if (Path.IsPathRooted(path)) return path;
            string relativeToConfig = Path.GetFullPath(Path.Combine(configDir, path));
            if (File.Exists(relativeToConfig) || Directory.Exists(relativeToConfig)) return relativeToConfig;
            string relativeToGame = Path.GetFullPath(Path.Combine(gameRoot, path));
            if (File.Exists(relativeToGame) || Directory.Exists(relativeToGame)) return relativeToGame;
            return path;
        }
    }
}

