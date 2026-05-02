using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Hacknet;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder.Action;
using Pathfinder.Util;
using HarmonyLib;

namespace TAXCore.Actions
{
    [HarmonyPatch]
    public static class ImageSequenceManager
    {
        public class ActiveSequence
        {
            public List<Texture2D> Frames = new List<Texture2D>();
            public Rectangle Bounds;
            public float FPS;
            public bool Loop;
            public float ElapsedTime;
            public bool IsFinished;
            public string Id;
        }
        public static List<ActiveSequence> Sequences = new List<ActiveSequence>();
        [HarmonyPostfix]
        [HarmonyPatch(typeof(OS), nameof(OS.Draw))]
        public static void Postfix_OS_Draw(OS __instance, GameTime gameTime)
        {
            if (Sequences.Count == 0 || GuiData.spriteBatch == null) return;
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            GuiData.startDraw();
            try
            {
                for (int i = Sequences.Count - 1; i >= 0; i--)
                {
                    var seq = Sequences[i];
                    if (seq == null) continue;
                    seq.ElapsedTime += dt;
                    int frameIndex = (int)(seq.ElapsedTime * seq.FPS);
                    if (frameIndex >= seq.Frames.Count)
                    {
                        if (seq.Loop && seq.Frames.Count > 0)
                        {
                            seq.ElapsedTime %= (seq.Frames.Count / seq.FPS);
                            frameIndex = (int)(seq.ElapsedTime * seq.FPS);
                        }
                        else
                        {
                            seq.IsFinished = true;
                            foreach (var frame in seq.Frames) frame?.Dispose();
                            Sequences.RemoveAt(i);
                            continue;
                        }
                    }
                    if (seq.Frames[frameIndex] != null)
                    {
                        GuiData.spriteBatch.Draw(seq.Frames[frameIndex], seq.Bounds, Color.White);
                    }
                }
            }
            finally
            {
                GuiData.endDraw();
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(OS), nameof(OS.quitGame))]
        public static void Prefix_OS_QuitGame()
        {
            ClearSequences();
        }
        public static void ClearSequences()
        {
            foreach (var seq in Sequences)
            {
                foreach (var frame in seq.Frames) frame.Dispose();
            }
            Sequences.Clear();
        }
    }
    public class SetTopMostAction : DelayableAction
    {
        [XMLStorage]
        public bool Enabled = true;
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        public override void Execute(OS os)
        {
            try
            {
                IntPtr hWnd = os.ScreenManager.Game.Window.Handle;
                SetWindowPos(hWnd, Enabled ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }
            catch (Exception ex)
            {
                os.write("Error setting TopMost: " + ex.Message);
            }
        }
    }
    public class PlayImageSequenceAction : DelayableAction
    {
        [XMLStorage]
        public string Folder;
        [XMLStorage]
        public string Id;
        [XMLStorage]
        public int X = 0;
        [XMLStorage]
        public int Y = 0;
        [XMLStorage]
        public int Width = -1;
        [XMLStorage]
        public int Height = -1;
        [XMLStorage]
        public float FPS = 24f;
        [XMLStorage]
        public bool Loop = false;
        [XMLStorage]
        public bool Fullscreen = false;
        [XMLStorage]
        public bool Adaptive = false;
        public override void Execute(OS os)
        {
            if (string.IsNullOrEmpty(Folder)) return;
            Viewport viewport = os.ScreenManager.GraphicsDevice.Viewport;
            int finalX = X;
            int finalY = Y;
            int finalWidth = Width;
            int finalHeight = Height;
            if (Fullscreen)
            {
                finalX = 0;
                finalY = 0;
                finalWidth = viewport.Width;
                finalHeight = viewport.Height;
            }
            if (!string.IsNullOrEmpty(Id))
            {
                var existing = ImageSequenceManager.Sequences.FirstOrDefault(s => s.Id == Id);
                if (existing != null)
                {
                    foreach (var f in existing.Frames) f.Dispose();
                    ImageSequenceManager.Sequences.Remove(existing);
                }
            }
            string fullPath = Folder;
            if (!Path.IsPathRooted(fullPath))
            {
                if (Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null)
                {
                    fullPath = Path.Combine(Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath, Folder);
                }
            }
            if (!Directory.Exists(fullPath))
            {
                os.write("Error: Image sequence folder not found: " + fullPath);
                return;
            }
            var seq = new ImageSequenceManager.ActiveSequence
            {
                Id = Id,
                FPS = FPS,
                Loop = Loop,
                Bounds = new Rectangle(finalX, finalY, finalWidth, finalHeight)
            };
            string[] files = Directory.GetFiles(fullPath)
                .Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s)
                .ToArray();
            foreach (var file in files)
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        seq.Frames.Add(Texture2D.FromStream(os.ScreenManager.GraphicsDevice, stream));
                    }
                }
                catch (Exception ex)
                {
                    os.write("Error loading image " + file + ": " + ex.Message);
                }
            }
            if (seq.Frames.Count == 0)
            {
                os.write("Error: No valid images found in folder: " + fullPath);
                return;
            }
            if (Adaptive)
            {
                float imgW = seq.Frames[0].Width;
                float imgH = seq.Frames[0].Height;
                float screenW = viewport.Width;
                float screenH = viewport.Height;
                float scale = Math.Min(screenW / imgW, screenH / imgH);
                seq.Bounds.Width = (int)(imgW * scale);
                seq.Bounds.Height = (int)(imgH * scale);
                seq.Bounds.X = (int)((screenW - seq.Bounds.Width) / 2);
                seq.Bounds.Y = (int)((screenH - seq.Bounds.Height) / 2);
            }
            else if (!Fullscreen)
            {
                if (finalWidth == -1) seq.Bounds.Width = seq.Frames[0].Width;
                if (finalHeight == -1) seq.Bounds.Height = seq.Frames[0].Height;
            }
            ImageSequenceManager.Sequences.Add(seq);
        }
    }
    public class StopImageSequenceAction : DelayableAction
    {
        [XMLStorage]
        public string Id;
        public override void Execute(OS os)
        {
            if (string.IsNullOrEmpty(Id))
            {
                ImageSequenceManager.ClearSequences();
            }
            else
            {
                var seq = ImageSequenceManager.Sequences.FirstOrDefault(s => s.Id == Id);
                if (seq != null)
                {
                    foreach (var f in seq.Frames) f.Dispose();
                    ImageSequenceManager.Sequences.Remove(seq);
                }
            }
        }
    }
}

