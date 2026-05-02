using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hacknet;
using Hacknet.Gui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder.Executable;
using Pathfinder.Meta.Load;
using TAXCore.Config;
namespace TAXCore.Executibles
{
    public class VideoExecutable : GameExecutable
    {
        public static string FFmpegPath => TAXCoreConfig.FFmpegPath;
        public static string FFplayPath => TAXCoreConfig.FFplayPath;
        private Process ffmpegProcess;
        private Process audioProcess;
        private Texture2D videoTexture;
        private byte[] currentFrameData;
        private byte[] nextFrameData;
        private readonly object frameLock = new object();
        private bool hasNewFrame = false;
        private Rectangle windowBounds;
        private Rectangle videoBounds;
        private Rectangle controlBounds;
        private string videoPath;
        private bool isPaused = false;
        private float currentTime = 0f;
        private bool isFFmpegRunning = false;
        private CancellationTokenSource cts;
        private StreamWriter ffmpegStdin;
        private StreamWriter audioStdin;
        private float videoFPS = 30f;
        private float videoDuration = 0f;
        private Stopwatch playbackStopwatch = new Stopwatch();
        private long framesRead = 0;
        public VideoExecutable() : base()
        {
            this.ramCost = 150;
            this.IdentifierName = "Hacknet Player";
        }
        public override void OnInitialize()
        {
            base.OnInitialize();
            int screenW = os.ScreenManager.GraphicsDevice.Viewport.Width;
            int screenH = os.ScreenManager.GraphicsDevice.Viewport.Height;
            int targetW = screenW - 100;
            int targetH = screenH - 100;
            int targetX = (screenW - targetW) / 2;
            int targetY = (screenH - targetH) / 2;
            windowBounds = new Rectangle(targetX, targetY, targetW, targetH);
            videoBounds = new Rectangle(windowBounds.X + 2, windowBounds.Y + 25, windowBounds.Width - 4, windowBounds.Height - 25 - 40);
            controlBounds = new Rectangle(windowBounds.X + 2, videoBounds.Y + videoBounds.Height, windowBounds.Width - 4, 40);
            if (Args.Length < 2)
            {
                os.write("Usage: VideoPlayer [video_file]");
                this.isExiting = true;
                return;
            }
            string arg = Args[1];
            videoPath = ResolveVideoPath(arg);
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                os.write("Error: Could not find video file: " + arg);
                this.isExiting = true;
                return;
            }
            videoTexture = new Texture2D(os.ScreenManager.GraphicsDevice, videoBounds.Width, videoBounds.Height, false, SurfaceFormat.Color);
            currentFrameData = new byte[videoBounds.Width * videoBounds.Height * 4];
            nextFrameData = new byte[videoBounds.Width * videoBounds.Height * 4];
            StartFFmpeg();
        }
        private string ResolveVideoPath(string arg)
        {
            Computer comp = os.connectedComp ?? os.thisComputer;
            FileEntry file = comp.files.root.searchForFile(arg);
            if (file == null) file = FindFileRecursive(comp.files.root, arg);
            if (file != null && file.data.StartsWith("#VIDEOF#:"))
            {
                string path = file.data.Substring("#VIDEOF#:".Length);
                if (!Path.IsPathRooted(path) && Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null)
                {
                    path = Path.Combine(Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath, path);
                }
                return path;
            }
            if (File.Exists(arg)) return arg;
            return null;
        }
        private FileEntry FindFileRecursive(Folder folder, string name)
        {
            FileEntry f = folder.searchForFile(name);
            if (f != null) return f;
            foreach (var sub in folder.folders)
            {
                f = FindFileRecursive(sub, name);
                if (f != null) return f;
            }
            return null;
        }
        private void StartFFmpeg()
        {
            cts = new CancellationTokenSource();
            Task.Run(() => ReadFFmpegStream(cts.Token));
        }
        private void ReadFFmpegStream(CancellationToken token)
        {
            try
            {
                string videoArgs = $"-re -i \"{videoPath}\" -vf \"scale={videoBounds.Width}:{videoBounds.Height}:force_original_aspect_ratio=decrease,pad={videoBounds.Width}:{videoBounds.Height}:(ow-iw)/2:(oh-ih)/2:black\" -f image2pipe -vcodec rawvideo -pix_fmt rgba -";
                ProcessStartInfo psiVideo = new ProcessStartInfo
                {
                    FileName = FFmpegPath,
                    Arguments = videoArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true
                };
                ffmpegProcess = new Process { StartInfo = psiVideo };
                ffmpegProcess.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        var fpsMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"(\d+(\.\d+)?)\s+fps");
                        if (fpsMatch.Success) float.TryParse(fpsMatch.Groups[1].Value, out videoFPS);
                        var durMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"Duration:\s+(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                        if (durMatch.Success) {
                            float hours = float.Parse(durMatch.Groups[1].Value);
                            float mins = float.Parse(durMatch.Groups[2].Value);
                            float secs = float.Parse(durMatch.Groups[3].Value);
                            float ms = float.Parse(durMatch.Groups[4].Value) * 10f;
                            videoDuration = (hours * 3600) + (mins * 60) + secs + (ms / 1000f);
                        }
                        var timeMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                        if (timeMatch.Success) {
                            float hours = float.Parse(timeMatch.Groups[1].Value);
                            float mins = float.Parse(timeMatch.Groups[2].Value);
                            float secs = float.Parse(timeMatch.Groups[3].Value);
                            float ms = float.Parse(timeMatch.Groups[4].Value) * 10f;
                            currentTime = (hours * 3600) + (mins * 60) + secs + (ms / 1000f);
                        }
                    }
                };
                ffmpegProcess.Start();
                ffmpegProcess.BeginErrorReadLine();
                ffmpegStdin = ffmpegProcess.StandardInput;
                isFFmpegRunning = true;
                StartAudio();
                playbackStopwatch.Start();
                Stream stdout = ffmpegProcess.StandardOutput.BaseStream;
                int frameSize = videoBounds.Width * videoBounds.Height * 4;
                byte[] buffer = new byte[frameSize];
                while (!token.IsCancellationRequested && isFFmpegRunning)
                {
                    if (isPaused) {
                        Thread.Sleep(50);
                        continue;
                    }
                    int totalRead = 0;
                    while (totalRead < frameSize)
                    {
                        int read = stdout.Read(buffer, totalRead, frameSize - totalRead);
                        if (read <= 0) { isFFmpegRunning = false; break; }
                        totalRead += read;
                    }
                    if (totalRead == frameSize)
                    {
                        lock (frameLock)
                        {
                            Buffer.BlockCopy(buffer, 0, nextFrameData, 0, frameSize);
                            hasNewFrame = true;
                            framesRead++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("FFmpeg Error: " + e.Message);
            }
            finally
            {
                CleanupFFmpeg();
            }
        }
        private void StartAudio()
        {
            try
            {
                string audioArgs = $"-nodisp -autoexit \"{videoPath}\"";
                ProcessStartInfo psiAudio = new ProcessStartInfo
                {
                    FileName = FFplayPath,
                    Arguments = audioArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true
                };
                audioProcess = new Process { StartInfo = psiAudio };
                audioProcess.Start();
                audioStdin = audioProcess.StandardInput;
            }
            catch { }
        }
        private void CleanupFFmpeg()
        {
            isFFmpegRunning = false;
            if (ffmpegProcess != null && !ffmpegProcess.HasExited)
            {
                try { ffmpegProcess.Kill(); } catch { }
            }
            if (audioProcess != null && !audioProcess.HasExited)
            {
                try { audioProcess.Kill(); } catch { }
            }
        }
        public override void Update(float t)
        {
            base.Update(t);
            if (hasNewFrame)
            {
                lock (frameLock)
                {
                    videoTexture.SetData(nextFrameData);
                    hasNewFrame = false;
                }
            }
            if (!isFFmpegRunning && ffmpegProcess != null && ffmpegProcess.HasExited)
            {
            }
        }
        public override void Draw(float t)
        {
            base.Draw(t);
            Rectangle fullBounds = windowBounds;
            RenderedRectangle.doRectangleOutline(fullBounds.X, fullBounds.Y, fullBounds.Width, fullBounds.Height, 1, new Color?(os.outlineColor));
            GuiData.spriteBatch.Draw(Utils.white, fullBounds, os.darkBackgroundColor * 0.8f);
            Rectangle headerBounds = new Rectangle(windowBounds.X, windowBounds.Y, windowBounds.Width, 25);
            GuiData.spriteBatch.Draw(Utils.white, headerBounds, os.outlineColor);
            TextItem.doFontLabel(new Vector2(headerBounds.X + 5, headerBounds.Y + 2), "FFMPEG PLAYER - " + Path.GetFileName(videoPath), GuiData.detailfont, Color.White);
            if (Button.doButton(991001, headerBounds.X + headerBounds.Width - 22, headerBounds.Y + 2, 20, 20, "X", Color.Red))
            {
                this.isExiting = true;
            }
            if (videoTexture != null)
            {
                GuiData.spriteBatch.Draw(videoTexture, videoBounds, Color.White);
            }
            int btnW = 60;
            int btnH = 25;
            int spacing = 10;
            int curX = controlBounds.X + 10;
            int curY = controlBounds.Y + (controlBounds.Height - btnH) / 2;
            if (Button.doButton(991002, curX, curY, btnW, btnH, isPaused ? "RESUME" : "PAUSE", os.highlightColor))
            {
                isPaused = !isPaused;
                if (ffmpegStdin != null) ffmpegStdin.Write("p");
                if (audioStdin != null) audioStdin.Write("p");
                if (isPaused) playbackStopwatch.Stop();
                else playbackStopwatch.Start();
            }
            curX += btnW + spacing;
            string timeStr = FormatTime(currentTime) + " / " + FormatTime(videoDuration);
            Vector2 timeSize = GuiData.detailfont.MeasureString(timeStr);
            int barW = controlBounds.Width - curX - (int)timeSize.X - 40;
            int barH = 10;
            Rectangle barBounds = new Rectangle(curX, controlBounds.Y + (controlBounds.Height - barH) / 2, barW, barH);
            RenderedRectangle.doRectangleOutline(barBounds.X, barBounds.Y, barBounds.Width, barBounds.Height, 1, new Color?(Color.Gray));
            float progress = videoDuration > 0 ? Math.Min(1.0f, currentTime / videoDuration) : 0f;
            GuiData.spriteBatch.Draw(Utils.white, new Rectangle(barBounds.X, barBounds.Y, (int)(barBounds.Width * progress), barBounds.Height), os.highlightColor * 0.5f);
            TextItem.doFontLabel(new Vector2(barBounds.X + barBounds.Width + 15, controlBounds.Y + (controlBounds.Height - timeSize.Y) / 2), timeStr, GuiData.detailfont, Color.White);
        }
        private string FormatTime(float seconds)
        {
            if (float.IsInfinity(seconds) || float.IsNaN(seconds)) seconds = 0;
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds);
            return string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
        }
        public override void OnComplete()
        {
            base.OnComplete();
            Cleanup();
        }
        public override void OnCompleteKilled()
        {
            base.OnCompleteKilled();
            Cleanup();
        }
        private void Cleanup()
        {
            cts?.Cancel();
            CleanupFFmpeg();
            if (videoTexture != null)
            {
                videoTexture.Dispose();
                videoTexture = null;
            }
        }
    }
}

