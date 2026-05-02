using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using Hacknet;
using Hacknet.Gui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder.Executable;
using Pathfinder.Meta.Load;
using TAXCore.Config;

namespace TAXCore.Executibles
{
    public class QemuExecutable : GameExecutable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
            return GetWindowLongPtr32(hWnd, nIndex);
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_STYLE = -16;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_BORDER = 0x00800000;
        private const uint SWP_NOZORDER = 0x0004;

        public static string QemuPath => TAXCoreConfig.QemuPath;
        private Process qemuProcess;
        private IntPtr qemuWindowHandle = IntPtr.Zero;
        private bool isEmbedded = false;

        private Microsoft.Xna.Framework.Rectangle customBounds;
        private Microsoft.Xna.Framework.Rectangle windowBounds; 
        private Microsoft.Xna.Framework.Rectangle contentBounds; 

        public QemuExecutable() : base()
        {
            this.ramCost = 250;
            this.IdentifierName = "QEMU Virtual Machine";
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetMenu(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetMenu(IntPtr hWnd, IntPtr hMenu);

        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_MDICHILD = 0x00000040;
        private const uint WS_EX_TOPMOST = 0x00000008;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        private const int SW_SHOW = 5;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hGDIOBJ);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int SRCCOPY = 0x00CC0020;

        private Texture2D qemuTexture;
        private Microsoft.Xna.Framework.Color[] textureData;
        private byte[] pixelBuffer;
        private const float CAPTURE_INTERVAL = 1f / 30f; 

        public override void OnInitialize()
        {
            base.OnInitialize();
            
            
            int targetW = Math.Max(400, os.ScreenManager.GraphicsDevice.Viewport.Width - 200);
            int targetH = Math.Max(300, os.ScreenManager.GraphicsDevice.Viewport.Height - 200);
            int targetX = (os.ScreenManager.GraphicsDevice.Viewport.Width - targetW) / 2;
            int targetY = (os.ScreenManager.GraphicsDevice.Viewport.Height - targetH) / 2;
            
            windowBounds = new Microsoft.Xna.Framework.Rectangle(targetX, targetY, targetW, targetH);
            
            
            
            contentBounds = new Microsoft.Xna.Framework.Rectangle(windowBounds.X + 2, windowBounds.Y + 25, windowBounds.Width - 4, windowBounds.Height - 27);
            customBounds = contentBounds; 

            
            List<string> qemuArgsList = new List<string>();
            if (Args.Length > 1)
            {
                
                for (int i = 1; i < Args.Length; i++)
                {
                    string arg = Args[i];
                    Computer comp = os.connectedComp ?? os.thisComputer;
                    FileEntry gameFile = FindFileRecursive(comp.files.root, arg);
                    
                    if (gameFile != null && gameFile.data.StartsWith("#QU_ISO#:"))
                    {
                        string realPath = gameFile.data.Substring("#QU_ISO#:".Length);
                        if (!Path.IsPathRooted(realPath) && Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null)
                        {
                            realPath = Path.Combine(Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath, realPath);
                        }
                        qemuArgsList.Add("\"" + realPath + "\"");
                    }
                    else
                    {
                        qemuArgsList.Add(arg);
                    }
                }
            }
            else
            {
                qemuArgsList.Add("-m");
                qemuArgsList.Add("512");
                qemuArgsList.Add("-vga");
                qemuArgsList.Add("std");
            }

            
            
            string qemuArgs = string.Join(" ", qemuArgsList);
            if (!qemuArgs.Contains("-display"))
            {
                qemuArgs += " -display sdl,window-close=off"; 
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = QemuPath,
                    Arguments = qemuArgs,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(QemuPath)
                };

                qemuProcess = Process.Start(psi);
                os.write("QEMU: Initializing display surface...");
            }
            catch (Exception e)
            {
                os.write("QEMU Error: " + e.Message);
                this.isExiting = true;
            }
        }

        private FileEntry FindFileRecursive(Folder f, string name)
        {
            FileEntry file = f.searchForFile(name);
            if (file != null) return file;
            foreach (var sub in f.folders)
            {
                file = FindFileRecursive(sub, name);
                if (file != null) return file;
            }
            return null;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

        private delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

        private IntPtr FindMainWindowHandle(Process process)
        {
            IntPtr handle = IntPtr.Zero;
            foreach (ProcessThread thread in process.Threads)
            {
                EnumThreadWindows(thread.Id, (hWnd, lParam) =>
                {
                    handle = hWnd;
                    return false; 
                }, IntPtr.Zero);
                if (handle != IntPtr.Zero) break;
            }
            return handle;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const uint PW_CLIENTONLY = 0x00000001;

        public override void Update(float t)
        {
            if (isExiting) return;

            base.Update(t);

            if (qemuProcess != null && !qemuProcess.HasExited)
            {
                if (!isEmbedded)
                {
                    qemuProcess.Refresh();
                    IntPtr handle = qemuProcess.MainWindowHandle;
                    if (handle == IntPtr.Zero) handle = FindMainWindowHandle(qemuProcess);

                    if (handle != IntPtr.Zero)
                    {
                        qemuWindowHandle = handle;
                        
                        
                        
                        long exStyle = (long)GetWindowLongPtr(qemuWindowHandle, GWL_EXSTYLE);
                        exStyle &= ~0x00040000L; 
                        exStyle |= 0x00000080L;  
                        SetWindowLongPtr(qemuWindowHandle, GWL_EXSTYLE, (IntPtr)exStyle);

                        
                        long style = (long)GetWindowLongPtr(qemuWindowHandle, GWL_STYLE);
                        style &= ~0x80000000L; 
                        style &= ~0x00C00000L; 
                        style &= ~0x00040000L; 
                        style |= WS_CHILD;
                        SetWindowLongPtr(qemuWindowHandle, GWL_STYLE, (IntPtr)style);

                        
                        SetParent(qemuWindowHandle, os.ScreenManager.GraphicsDevice.PresentationParameters.DeviceWindowHandle);
                        
                        isEmbedded = true;
                        os.write("QEMU: Hardware accelerated surface synchronized.");
                    }
                }
                else
                {
                    UpdateWindowPosition();
                    
                    
                    if (GuiData.mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                        windowBounds.Contains(GuiData.mouse.X, GuiData.mouse.Y))
                    {
                        SetFocus(qemuWindowHandle);
                    }
                }
            }
            else if (qemuProcess != null && qemuProcess.HasExited)
            {
                this.isExiting = true;
            }
        }

        private void CaptureQemuFrame()
        {
            if (qemuWindowHandle == IntPtr.Zero) return;

            int width = contentBounds.Width;
            int height = contentBounds.Height;

            if (qemuTexture == null || qemuTexture.Width != width || qemuTexture.Height != height)
            {
                qemuTexture = new Texture2D(os.ScreenManager.GraphicsDevice, width, height);
                textureData = new Microsoft.Xna.Framework.Color[width * height];
            }

            IntPtr hdcSrc = GetDC(qemuWindowHandle);
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            IntPtr hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);

            
            using (System.Drawing.Bitmap bmp = System.Drawing.Bitmap.FromHbitmap(hBitmap))
            {
                System.Drawing.Imaging.BitmapData data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                
                int size = width * height * 4;
                if (pixelBuffer == null || pixelBuffer.Length != size) pixelBuffer = new byte[size];
                Marshal.Copy(data.Scan0, pixelBuffer, 0, size);
                bmp.UnlockBits(data);

                for (int i = 0; i < textureData.Length; i++)
                {
                    int b = pixelBuffer[i * 4];
                    int g = pixelBuffer[i * 4 + 1];
                    int r = pixelBuffer[i * 4 + 2];
                    int a = pixelBuffer[i * 4 + 3];
                    textureData[i] = new Microsoft.Xna.Framework.Color(r, g, b, a);
                }

                qemuTexture.SetData(textureData);
            }

            
            SelectObject(hdcDest, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcDest);
            ReleaseDC(qemuWindowHandle, hdcSrc);
        }

        private void EmbedWindow()
        {
            IntPtr gameHandle = os.ScreenManager.GraphicsDevice.PresentationParameters.DeviceWindowHandle;
            
            
            IntPtr hMenu = GetMenu(qemuWindowHandle);
            if (hMenu != IntPtr.Zero) SetMenu(qemuWindowHandle, IntPtr.Zero);

            
            
            long style = (long)GetWindowLongPtr(qemuWindowHandle, GWL_STYLE);
            
            
            style &= ~0x80000000L; 
            style &= ~0x00C00000L; 
            style &= ~0x00040000L; 
            style &= ~0x00800000L; 
            style |= WS_CHILD;     
            style |= 0x10000000L;  
            
            SetWindowLongPtr(qemuWindowHandle, GWL_STYLE, (IntPtr)style);

            
            long exStyle = (long)GetWindowLongPtr(qemuWindowHandle, GWL_EXSTYLE);
            exStyle &= ~0x00040000L; 
            exStyle |= 0x00000080L;  
            SetWindowLongPtr(qemuWindowHandle, GWL_EXSTYLE, (IntPtr)exStyle);

            
            SetParent(qemuWindowHandle, gameHandle);
            UpdateWindowPosition();
            
            
            ShowWindow(qemuWindowHandle, SW_SHOW);
            UpdateWindow(qemuWindowHandle);
            
            
            SetWindowPos(qemuWindowHandle, IntPtr.Zero, contentBounds.X, contentBounds.Y, contentBounds.Width, contentBounds.Height, SWP_NOZORDER | 0x0020);
        }

        private void UpdateWindowPosition()
        {
            if (qemuWindowHandle != IntPtr.Zero)
            {
                
                
                SetWindowPos(qemuWindowHandle, IntPtr.Zero, contentBounds.X, contentBounds.Y, contentBounds.Width, contentBounds.Height, SWP_NOZORDER);
            }
        }

        public override void Draw(float t)
        {
            
            
            spriteBatch.Draw(Utils.white, windowBounds, Microsoft.Xna.Framework.Color.Black * 0.8f);
            
            
            Microsoft.Xna.Framework.Rectangle headerRect = new Microsoft.Xna.Framework.Rectangle(windowBounds.X, windowBounds.Y, windowBounds.Width, 25);
            spriteBatch.Draw(Utils.white, headerRect, os.exeModuleTopBar);
            
            
            spriteBatch.Draw(Utils.white, new Microsoft.Xna.Framework.Rectangle(windowBounds.X, windowBounds.Y, windowBounds.Width, 1), os.outlineColor); 
            spriteBatch.Draw(Utils.white, new Microsoft.Xna.Framework.Rectangle(windowBounds.X, windowBounds.Y + windowBounds.Height - 1, windowBounds.Width, 1), os.outlineColor); 
            spriteBatch.Draw(Utils.white, new Microsoft.Xna.Framework.Rectangle(windowBounds.X, windowBounds.Y, 1, windowBounds.Height), os.outlineColor); 
            spriteBatch.Draw(Utils.white, new Microsoft.Xna.Framework.Rectangle(windowBounds.X + windowBounds.Width - 1, windowBounds.Y, 1, windowBounds.Height), os.outlineColor); 
            
            
            string title = "QEMU VIRTUAL MACHINE - " + (qemuProcess != null ? "RUNNING" : "STOPPED");
            spriteBatch.DrawString(GuiData.font, title, new Vector2(headerRect.X + 5, headerRect.Y + 2), Microsoft.Xna.Framework.Color.White, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);

            
            spriteBatch.Draw(Utils.white, contentBounds, Microsoft.Xna.Framework.Color.Black);
            
            if (!isEmbedded)
            {
                string msg = "Initialising Virtual Machine...";
                Vector2 size = GuiData.font.MeasureString(msg);
                spriteBatch.DrawString(GuiData.font, msg, new Vector2(contentBounds.X + contentBounds.Width/2 - size.X/2, contentBounds.Y + contentBounds.Height/2 - size.Y/2), Microsoft.Xna.Framework.Color.White);
            }
        }

        public override void OnComplete()
        {
            base.OnComplete();
            if (qemuProcess != null && !qemuProcess.HasExited)
            {
                try { qemuProcess.Kill(); } catch { }
            }
        }
    }
}
