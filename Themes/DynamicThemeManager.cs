using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hacknet;
using Hacknet.Misc;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml;
using TAXCore.Config;
using TAXCore.Util;

namespace TAXCore.Themes
{
    public static class DynamicThemeManager
    {
        public enum DynamicType { None, Rainbow, Pulse, Matrix, Glitch }
        
        public static DynamicType CurrentDynamicType = DynamicType.None;
        public static string VideoPath = null;
        public static string CachePath = null;
        public static float HueShiftSpeed = 0.15f;
        
        private static float currentHue = 0f;
        private static float pulseTimer = 0f;
        private static Color lastMainColor = Color.Transparent;
        private static Color lastSecondaryColor = Color.Transparent;
        
        
        internal static Texture2D videoTexture = null;
        private static Process ffmpegProcess = null;
        public static bool IsVideoPlaying { get; private set; } = false;
        
        
        private static BinaryReader cacheReader = null;
        private static long frameDataStartPos = 0;
        private static int cacheFrameSize = 0;
        private static int cacheFrameCount = 0;
        private static int currentCacheFrame = 0;
        private static int cacheFPS = 30;
        private static SurfaceFormat cacheFormat = SurfaceFormat.Color;
        private static List<Texture2D> vramTextures = new List<Texture2D>();

        
        private static byte[] backBuffer = null;
        private static byte[] frontBuffer = null;
        private static bool hasNewFrame = false;
        private static bool hasFirstFrame = false;
        public static readonly object FrameLock = new object();
        private static CancellationTokenSource cts;

        public static void Init()
        {
            Pathfinder.Command.CommandManager.RegisterCommand("renderCache", OnRenderCacheCommand, true);
        }

        private static void OnRenderCacheCommand(OS os, string[] args)
        {
            string videoFullPath = null;
            int fps = TAXCoreConfig.WallpaperFPS;
            float scale = 0.5f;

            if (args.Length < 2)
            {
                if (!string.IsNullOrEmpty(VideoPath) && File.Exists(VideoPath))
                {
                    videoFullPath = VideoPath;
                    os.write("No video specified. Using current theme's video: " + Path.GetFileName(VideoPath));
                }
                else
                {
                    os.write("Usage: renderCache [VideoPath] [FPS] [Scale]");
                    os.write("Example: renderCache myvideo.mp4 24 0.5");
                    return;
                }
            }
            else
            {
                videoFullPath = ResolveFilePath(args[1]);
                if (args.Length > 2) int.TryParse(args[2], out fps);
                if (args.Length > 3) float.TryParse(args[3], out scale);
            }

            if (string.IsNullOrEmpty(videoFullPath) || !File.Exists(videoFullPath))
            {
                os.write("Error: Video file not found: " + videoFullPath);
                return;
            }

            string extensionDir = Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null 
                ? Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath : "Content";
            string wallpapersDir = Path.Combine(extensionDir, "Wallpapers");
            if (!Directory.Exists(wallpapersDir)) Directory.CreateDirectory(wallpapersDir);

            string fileName = Path.GetFileNameWithoutExtension(videoFullPath) + ".txc";
            string outputFullPath = Path.Combine(wallpapersDir, fileName);

            os.write($"Generating optimized cache: {fileName}...");
            os.write($"Settings: FPS={fps}, Scale={scale}");
            
            int w = os.ScreenManager.GraphicsDevice.Viewport.Width;
            int h = os.ScreenManager.GraphicsDevice.Viewport.Height;

            RenderCacheGenerator.GenerateCacheFromVideo(videoFullPath, outputFullPath, w, h, fps, scale);
            os.write("Generation started in background. Check terminal for progress.");
        }

        public static void OnThemeLoaded(string filepath)
        {
            try
            {
                if (!File.Exists(filepath)) return;
                XmlDocument doc = new XmlDocument();
                doc.Load(filepath);
                XmlNode root = doc.DocumentElement;

                if (root != null && root.Name == "CustomTheme")
                {
                    if (root.Attributes["renderCache"] != null)
                    {
                        string path = ResolveFilePath(root.Attributes["renderCache"].Value);
                        if (File.Exists(path)) 
                        { 
                            LoadRenderCache(path); 
                            
                            LoadThemeAttributes(root);
                            return; 
                        }
                    }

                    if (root.Attributes["videoBg"] != null)
                    {
                        string path = ResolveFilePath(root.Attributes["videoBg"].Value);
                        if (File.Exists(path)) StartVideoBackground(path);
                    }
                    else StopVideoBackground();

                    LoadThemeAttributes(root);
                }
            }
            catch (Exception ex) { Console.WriteLine("[TAXCore] Theme Parse Error: " + ex.Message); }
        }

        private static void LoadThemeAttributes(XmlNode root)
        {
            if (root.Attributes["dynamicType"] != null && Enum.TryParse<DynamicType>(root.Attributes["dynamicType"].Value, true, out var type))
                CurrentDynamicType = type;
            else
                CurrentDynamicType = DynamicType.None;

            if (root.Attributes["dynamicSpeed"] != null)
                float.TryParse(root.Attributes["dynamicSpeed"].Value, out HueShiftSpeed);
        }

        private static string ResolveFilePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            if (Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null)
                return Path.Combine(Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath, path);
            return Path.Combine("Content", path);
        }

        private static void LoadRenderCache(string path)
        {
            StopVideoBackground();
            CachePath = path;
            IsVideoPlaying = true;
            hasFirstFrame = false;
            hasNewFrame = false;

            try
            {
                cacheReader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                string header = new string(cacheReader.ReadChars(4));
                
                int w = cacheReader.ReadInt32();
                int h = cacheReader.ReadInt32();
                cacheFPS = cacheReader.ReadInt32();
                cacheFrameCount = cacheReader.ReadInt32();
                
                if (header == "TXC3")
                {
                    cacheFormat = SurfaceFormat.Bgr565;
                    cacheFrameSize = w * h * 2;
                }
                else if (header == "TXC1")
                {
                    cacheFormat = SurfaceFormat.Color;
                    cacheFrameSize = w * h * 4;
                }
                else throw new Exception("Unsupported cache format: " + header);

                frameDataStartPos = cacheReader.BaseStream.Position;
                currentCacheFrame = 0;

                
                if (TAXCoreConfig.UseVRAMCache)
                {
                    Console.WriteLine($"[TAXCore] VRAM Preload started for {cacheFrameCount} frames...");
                    byte[] tempBuffer = new byte[cacheFrameSize];
                    for (int i = 0; i < cacheFrameCount; i++)
                    {
                        cacheReader.Read(tempBuffer, 0, cacheFrameSize);
                        Texture2D tex = new Texture2D(OS.currentInstance.ScreenManager.GraphicsDevice, w, h, false, cacheFormat);
                        tex.SetData(tempBuffer);
                        vramTextures.Add(tex);
                        if (i % 50 == 0) Console.WriteLine($"[TAXCore] Preloaded {i}/{cacheFrameCount}...");
                    }
                    hasFirstFrame = true;
                    cacheReader.Close();
                    cacheReader = null;
                    Console.WriteLine("[TAXCore] VRAM Preload Complete. Switched to Zero-CPU mode.");
                }
                else
                {
                    lock (FrameLock)
                    {
                        if (videoTexture == null || videoTexture.Width != w || videoTexture.Height != h || videoTexture.Format != cacheFormat)
                        {
                            videoTexture?.Dispose();
                            videoTexture = new Texture2D(OS.currentInstance.ScreenManager.GraphicsDevice, w, h, false, cacheFormat);
                        }
                        backBuffer = new byte[cacheFrameSize];
                        frontBuffer = new byte[cacheFrameSize];
                    }
                    
                    cts = new CancellationTokenSource();
                    Task.Run(() => ReadCacheFrames(cts.Token));
                }
                
                Console.WriteLine($"[TAXCore] Cache streaming initialized: {path} ({cacheFrameCount} frames, {header})");
            }
            catch (Exception ex) 
            { 
                Console.WriteLine("[TAXCore] Cache Load Error: " + ex.Message); 
                StopVideoBackground();
            }
        }

        private static void ReadCacheFrames(CancellationToken token)
        {
            try
            {
                float frameDuration = 1f / cacheFPS;
                Stopwatch sw = new Stopwatch();

                while (!token.IsCancellationRequested && IsVideoPlaying && cacheReader != null)
                {
                    if (TAXCoreConfig.UseStaticWallpaper)
                    {
                        
                        if (hasNewFrame)
                        {
                            Thread.Sleep(1);
                            continue;
                        }
                    }
                    else
                    {
                        sw.Restart();
                    }

                    lock (FrameLock)
                    {
                        if (cacheReader != null)
                        {
                            long offset = (long)currentCacheFrame * cacheFrameSize;
                            cacheReader.BaseStream.Position = frameDataStartPos + offset;
                            cacheReader.Read(backBuffer, 0, cacheFrameSize);
                            
                            currentCacheFrame = (currentCacheFrame + 1) % cacheFrameCount;
                            hasNewFrame = true;
                        }
                    }

                    if (!TAXCoreConfig.UseStaticWallpaper)
                    {
                        float elapsed = (float)sw.Elapsed.TotalSeconds;
                        int sleepMs = (int)((frameDuration - elapsed) * 1000);
                        if (sleepMs > 0) Thread.Sleep(sleepMs);
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[TAXCore] Cache Thread Error: " + ex.Message); }
        }

        private static void StartVideoBackground(string path)
        {
            if (VideoPath == path && IsVideoPlaying) return;
            StopVideoBackground();
            VideoPath = path;
            IsVideoPlaying = true;
            hasFirstFrame = false;
            hasNewFrame = false;
            
            
            cacheFormat = SurfaceFormat.Bgr565; 
            
            cts = new CancellationTokenSource();
            Task.Run(() => ReadVideoFrames(cts.Token));
        }

        public static void StopVideoBackground()
        {
            IsVideoPlaying = false;
            hasFirstFrame = false;
            hasNewFrame = false;
            cts?.Cancel();
            try { if (ffmpegProcess != null && !ffmpegProcess.HasExited) ffmpegProcess.Kill(); } catch { }
            ffmpegProcess = null;
            VideoPath = null;
            CachePath = null;
            
            lock (FrameLock)
            {
                videoTexture?.Dispose();
                videoTexture = null;
                frontBuffer = null;
                backBuffer = null;
                
                cacheReader?.Close();
                cacheReader?.Dispose();
                cacheReader = null;

                foreach (var tex in vramTextures) tex.Dispose();
                vramTextures.Clear();
            }
        }

        private static void ReadVideoFrames(CancellationToken token)
        {
            try
            {
                int originalW = OS.currentInstance.ScreenManager.GraphicsDevice.Viewport.Width;
                int originalH = OS.currentInstance.ScreenManager.GraphicsDevice.Viewport.Height;
                
                
                int w = (int)(originalW * TAXCoreConfig.VideoScale);
                int h = (int)(originalH * TAXCoreConfig.VideoScale);
                w = w % 2 == 0 ? w : w + 1;
                h = h % 2 == 0 ? h : h + 1;

                int bufferSize = w * h * 2; 
                
                
                string args;
                if (TAXCoreConfig.UseStaticWallpaper)
                {
                    args = $"-hwaccel auto -stream_loop -1 -i \"{VideoPath}\" -threads 1 -vf \"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black\" -f image2pipe -vcodec rawvideo -pix_fmt rgb565le -";
                }
                else
                {
                    int fps = TAXCoreConfig.WallpaperFPS;
                    args = $"-hwaccel auto -re -stream_loop -1 -i \"{VideoPath}\" -threads 1 -vf \"fps={fps},scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black\" -f image2pipe -vcodec rawvideo -pix_fmt rgb565le -";
                }

                lock (FrameLock)
                {
                    if (videoTexture == null || videoTexture.Width != w || videoTexture.Height != h || videoTexture.Format != cacheFormat)
                    {
                        videoTexture?.Dispose();
                        videoTexture = new Texture2D(OS.currentInstance.ScreenManager.GraphicsDevice, w, h, false, cacheFormat);
                    }
                    backBuffer = new byte[bufferSize];
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = TAXCoreConfig.FFmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                ffmpegProcess = Process.Start(psi);
                if (ffmpegProcess == null) return;
                
                
                try { ffmpegProcess.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

                Stream stdout = ffmpegProcess.StandardOutput.BaseStream;
                byte[] tempBuffer = new byte[bufferSize];

                while (!token.IsCancellationRequested && IsVideoPlaying)
                {
                    if (TAXCoreConfig.UseStaticWallpaper && hasNewFrame)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    int totalRead = 0;
                    while (totalRead < bufferSize)
                    {
                        int read = stdout.Read(tempBuffer, totalRead, bufferSize - totalRead);
                        if (read <= 0) { IsVideoPlaying = false; break; }
                        totalRead += read;
                    }

                    if (!IsVideoPlaying || totalRead < bufferSize) break;

                    lock (FrameLock)
                    {
                        if (backBuffer == null || backBuffer.Length != bufferSize)
                            backBuffer = new byte[bufferSize];
                        Buffer.BlockCopy(tempBuffer, 0, backBuffer, 0, bufferSize);
                        hasNewFrame = true;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[TAXCore] Video Error: " + ex.Message); }
            finally { IsVideoPlaying = false; StopVideoBackground(); }
        }

        private static float colorUpdateTimer = 0f;
        private const float COLOR_UPDATE_INTERVAL = 0.033f; 

        public static void Update(float dt)
        {
            if (OS.currentInstance == null) return;
            OS os = OS.currentInstance;

            
            if (TAXCoreConfig.UseVRAMCache && vramTextures.Count > 0)
            {
                
                currentCacheFrame = (currentCacheFrame + 1) % vramTextures.Count;
            }
            
            else if (hasNewFrame)
            {
                lock (FrameLock)
                {
                    if (hasNewFrame && backBuffer != null)
                    {
                        int vw = videoTexture.Width;
                        int vh = videoTexture.Height;

                        if (frontBuffer == null || frontBuffer.Length != backBuffer.Length)
                            frontBuffer = new byte[backBuffer.Length];
                        
                        Buffer.BlockCopy(backBuffer, 0, frontBuffer, 0, backBuffer.Length);
                        videoTexture.SetData(frontBuffer);
                        hasFirstFrame = true;
                        hasNewFrame = false;
                    }
                }
            }

            if (CurrentDynamicType == DynamicType.None) return;

            
            colorUpdateTimer += dt;
            if (colorUpdateTimer < COLOR_UPDATE_INTERVAL) return;
            colorUpdateTimer = 0f;

            
            Color main = os.defaultHighlightColor;
            Color sec = os.defaultTopBarColor;

            switch (CurrentDynamicType)
            {
                case DynamicType.Rainbow:
                    currentHue = (currentHue + HueShiftSpeed * dt) % 6f;
                    main = new HSLColor(currentHue, 0.7f, 0.5f).ToRGB();
                    sec = new HSLColor((currentHue + 1f) % 6f, 0.7f, 0.5f).ToRGB();
                    break;
                case DynamicType.Pulse:
                    pulseTimer += dt * 2f;
                    float pulse = (float)(Math.Sin(pulseTimer) + 1.0) / 2.0f;
                    main = Color.Lerp(os.defaultHighlightColor, Color.White, pulse * 0.4f);
                    sec = Color.Lerp(os.defaultTopBarColor, Color.White, pulse * 0.2f);
                    break;
                case DynamicType.Matrix:
                    main = new Color(0, 255, 70); sec = new Color(0, 150, 40);
                    if (Utils.random.NextDouble() > 0.98) main = Color.White;
                    break;
                case DynamicType.Glitch:
                    if (Utils.random.NextDouble() > 0.9) {
                        main = new Color(Utils.random.Next(255), Utils.random.Next(255), Utils.random.Next(255));
                        sec = new Color(Utils.random.Next(255), Utils.random.Next(255), Utils.random.Next(255));
                    }
                    break;
            }
            ApplyThemeColors(os, main, sec);
        }

        private static void ApplyThemeColors(OS os, Color main, Color secondary)
        {
            
            if (main == lastMainColor && secondary == lastSecondaryColor) return;
            
            lastMainColor = main;
            lastSecondaryColor = secondary;

            os.highlightColor = main; os.topBarColor = secondary;
            os.terminalTextColor = main; os.exeModuleTopBar = secondary;
            os.shellColor = main; os.outlineColor = secondary;
            
            
            Color subtle = Color.Lerp(main, Color.Gray, 0.5f);
            Color solid = Color.Lerp(main, Color.Black, 0.7f);
            Color strong = Color.Lerp(main, Color.Black, 0.9f);
            Color backing = Color.Lerp(main, Color.Black, 0.95f);

            os.subtleTextColor = subtle;
            os.moduleColorSolid = solid;
            os.moduleColorStrong = strong;
            os.moduleColorBacking = backing;
            os.thisComputerNode = main; os.connectedNodeHighlight = secondary;
            os.scanlinesColor = new Color(main.R, main.G, main.B, (byte)20);
        }

        public static void DrawVideoBackground(SpriteBatch sb)
        {
            lock (FrameLock)
            {
                if (TAXCoreConfig.UseVRAMCache && vramTextures.Count > 0)
                {
                    Texture2D tex = vramTextures[currentCacheFrame];
                    sb.Draw(tex, new Rectangle(0, 0, sb.GraphicsDevice.Viewport.Width, sb.GraphicsDevice.Viewport.Height), Color.White);
                    return;
                }

                if (videoTexture != null && !videoTexture.IsDisposed && hasFirstFrame)
                {
                    sb.Draw(videoTexture, new Rectangle(0, 0, sb.GraphicsDevice.Viewport.Width, sb.GraphicsDevice.Viewport.Height), Color.White);
                }
            }
        }

        public static bool ShouldInterceptBackground()
        {
            return IsVideoPlaying && hasFirstFrame;
        }
    }

    [HarmonyPatch(typeof(OS), nameof(OS.quitGame))]
    public static class OSQuitPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            Console.WriteLine("[TAXCore] Game quitting, cleaning up resources...");
            DynamicThemeManager.StopVideoBackground();
        }
    }

    [HarmonyPatch(typeof(ThemeManager), nameof(ThemeManager.getThemeForDataString))]
    public static class ThemeDataStringPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string data)
        {
            if (data.Contains("___")) 
            {
                try
                {
                    string[] separated = data.Split(new string[] { "___" }, StringSplitOptions.RemoveEmptyEntries);
                    if (separated.Length > 1)
                    {
                        string path = FileEncrypter.DecryptString(separated[1], "")[2];
                        ThemeManager.LastLoadedCustomThemePath = path;
                    }
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(ThemeManager), nameof(ThemeManager.switchTheme), new Type[] { typeof(object), typeof(OSTheme) })]
    public static class ThemeSwitchThemePatch
    {
        [HarmonyPostfix]
        public static void Postfix(object osObject, OSTheme theme)
        {
            if (theme == OSTheme.Custom && !string.IsNullOrEmpty(ThemeManager.LastLoadedCustomThemePath))
            {
                string customThemePath = ThemeManager.LastLoadedCustomThemePath;
                string fullPath = customThemePath;
                if (!Path.IsPathRooted(fullPath))
                {
                    string baseDir = Settings.IsInExtensionMode && Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null 
                        ? Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath : "Content";
                    string checkPath = Path.Combine(baseDir, customThemePath);
                    if (File.Exists(checkPath)) fullPath = checkPath;
                    else {
                        checkPath = Path.Combine("Content/Themes", customThemePath);
                        if (File.Exists(checkPath)) fullPath = checkPath;
                    }
                }
                if (File.Exists(fullPath)) DynamicThemeManager.OnThemeLoaded(fullPath);
            }
        }
    }

    [HarmonyPatch(typeof(OS), nameof(OS.Update))]
    public static class OSUpdateThemePatch
    {
        [HarmonyPostfix]
        public static void Postfix(OS __instance, GameTime gameTime) => DynamicThemeManager.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    [HarmonyPatch(typeof(OS), nameof(OS.drawBackground))]
    public static class OSDrawBackgroundPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(OS __instance)
        {
            if (DynamicThemeManager.ShouldInterceptBackground())
            {
                DynamicThemeManager.DrawVideoBackground(GuiData.spriteBatch);
                return false; 
            }
            return true;
        }
    }
}
