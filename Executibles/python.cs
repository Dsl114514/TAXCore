using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Hacknet;
using Hacknet.Gui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder.Executable;
using HarmonyLib;
using System.Threading;
using System.Threading.Tasks;
namespace TAXCore.Executibles
{
    [Pathfinder.Meta.Load.Executable("#PYTHON#")]
    public class PythonExe : GameExecutable
    {
        public static PythonExe ActivePythonInstance = null;
        private Process pythonProcess;
        private StringBuilder outputBuffer = new StringBuilder();
        private readonly object lockObj = new object();
        private bool processStarted = false;
        private string resolvedPythonPath = null;
        private static int[] pyEdgeA = {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 
            16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 
        };
        private static int[] pyEdgeB = {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 0, 
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 16 
        };
        private static float[] pyVertX = {
            -0.15f, 0.15f, 0.35f, 0.45f, 0.45f, 0.35f, 0.15f, 0.15f, 0.15f, -0.05f, -0.15f, -0.15f, -0.35f, -0.45f, -0.45f, -0.35f,
            0.15f, -0.15f, -0.35f, -0.45f, -0.45f, -0.35f, -0.15f, -0.15f, -0.15f, 0.05f, 0.15f, 0.15f, 0.35f, 0.45f, 0.45f, 0.35f
        };
        private static float[] pyVertY = {
            -0.45f, -0.45f, -0.45f, -0.35f, -0.1f, 0.05f, 0.05f, 0.2f, 0.45f, 0.45f, 0.45f, 0.3f, 0.1f, 0.1f, -0.35f, -0.45f,
            0.45f, 0.45f, 0.45f, 0.35f, 0.1f, -0.05f, -0.05f, -0.2f, -0.45f, -0.45f, -0.45f, -0.3f, -0.1f, -0.1f, 0.35f, 0.45f
        };
        private static Random rnd = new Random();
        public PythonExe() : base()
        {
            this.ramCost = 150;
            this.IdentifierName = "PythonInterpreter";
        }
        public override void OnInitialize()
        {
            base.OnInitialize();
            ActivePythonInstance = this;
            os.write("--- Python VEnv Initialization ---");
            if (TryResolvePythonPath())
            {
                StartPythonProcess();
                if (Args != null && Args.Length > 1)
                {
                    RunGameScript(Args[1]);
                }
                else
                {
                    os.write("Python Interpreter Ready. (Type 'exit()' to quit)");
                }
            }
            else
            {
                os.write("CRITICAL ERROR: Could not find python.exe in virtual environment!");
                os.write("Checked: python\\Scripts\\python.exe and python\\python.exe");
                this.isExiting = true;
            }
        }
        private bool TryResolvePythonPath()
        {
            string root = BepInEx.Paths.GameRootPath;
            string[] possiblePaths = new[]
            {
                Path.Combine(root, "python", "Scripts", "python.exe"),
                Path.Combine(root, "python", "python.exe"),
                "f:\\Project\\C#\\TAXCore\\python\\Scripts\\python.exe"
            };
            foreach (var p in possiblePaths)
            {
                if (File.Exists(p))
                {
                    resolvedPythonPath = p;
                    os.write("Found Python: " + p);
                    return true;
                }
            }
            return false;
        }
        private void StartPythonProcess()
        {
            try
            {
                pythonProcess = new Process();
                pythonProcess.StartInfo.FileName = resolvedPythonPath;
                pythonProcess.StartInfo.UseShellExecute = false;
                pythonProcess.StartInfo.RedirectStandardInput = true;
                pythonProcess.StartInfo.RedirectStandardOutput = true;
                pythonProcess.StartInfo.RedirectStandardError = true;
                pythonProcess.StartInfo.CreateNoWindow = true;
                pythonProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                pythonProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;
                pythonProcess.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                pythonProcess.StartInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
                processStarted = true; 
                pythonProcess.Start();
                var outThread = new Thread(() => ReadStreamBlocking(pythonProcess.StandardOutput));
                outThread.IsBackground = true;
                outThread.Start();
                var errThread = new Thread(() => ReadStreamBlocking(pythonProcess.StandardError, "! "));
                errThread.IsBackground = true;
                errThread.Start();
                SendInput("import sys; sys.ps1 = ''; sys.ps2 = ''");
                Task.Run(async () =>
                {
                    while (processStarted && !pythonProcess.HasExited)
                    {
                        await Task.Delay(500);
                    }
                    if (processStarted) AppendOutput("Python process exited unexpectedly.");
                });
            }
            catch (Exception ex)
            {
                os.write("Process Start Failed: " + ex.Message);
                this.isExiting = true;
            }
        }
        private void AppendOutput(string text)
        {
            lock (lockObj)
            {
                outputBuffer.Append(text); 
            }
        }
        private void ReadStreamBlocking(StreamReader reader, string prefix = "")
        {
            char[] buffer = new char[1024];
            try
            {
                while (processStarted)
                {
                    int count = reader.Read(buffer, 0, buffer.Length);
                    if (count > 0)
                    {
                        string text = new string(buffer, 0, count);
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            text = text.Replace("\n", "\n" + prefix);
                        }
                        AppendOutput(text);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (processStarted)
                {
                    lock (lockObj)
                    {
                        outputBuffer.Append("\n! Read Error: " + ex.Message + "\n");
                    }
                }
            }
        }
        private void RunGameScript(string filename)
        {
            Computer comp = os.connectedComp ?? os.thisComputer;
            FileEntry file = FindFileRecursive(comp.files.root, filename);
            if (file == null)
            {
                os.write("File Error: '" + filename + "' not found on " + comp.ip);
                return;
            }
            string content = file.data;
            if (content.StartsWith("#PYF#:"))
            {
                string realPath = content.Substring("#PYF#:".Length);
                if (!Path.IsPathRooted(realPath))
                {
                    if (Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo != null)
                    {
                        realPath = Path.Combine(Hacknet.Extensions.ExtensionLoader.ActiveExtensionInfo.FolderPath, realPath);
                    }
                    else
                    {
                        realPath = Path.GetFullPath(realPath);
                    }
                }
                if (File.Exists(realPath))
                {
                    content = File.ReadAllText(realPath, Encoding.UTF8);
                }
                else
                {
                    os.write("Error: Real Python script not found at " + realPath);
                    return;
                }
            }
            string tempFile = Path.Combine(Path.GetTempPath(), "hn_py_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".py");
            File.WriteAllText(tempFile, content, new UTF8Encoding(false)); 
            os.write("Executing script file: " + filename);
            string escapedPath = tempFile.Replace("\\", "\\\\");
            string cmd = $"exec(open(r'{escapedPath}', encoding='utf-8').read(), {{'__name__': '__main__'}})";
            SendInput(cmd);
            Task.Run(async () =>
            {
                await Task.Delay(5000); 
            });
        }
        private FileEntry FindFileRecursive(Folder folder, string filename)
        {
            FileEntry file = folder.searchForFile(filename);
            if (file != null) return file;
            Folder bin = folder.searchForFolder("bin");
            if (bin != null)
            {
                file = bin.searchForFile(filename);
                if (file != null) return file;
            }
            {
                foreach (var sub in folder.folders)
                {
                    if (sub.name == "bin") continue;
                    file = FindFileRecursive(sub, filename);
                    if (file != null) return file;
                }
            }
            return null;
        }
        public void SendInput(string input)
        {
            if (processStarted && !pythonProcess.HasExited)
            {
                try
                {
                    pythonProcess.StandardInput.WriteLine(input);
                    pythonProcess.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    os.write("Input Error: " + ex.Message);
                }
            }
        }
        public override void Update(float t)
        {
            base.Update(t);
            lock (lockObj)
            {
                if (outputBuffer.Length > 0)
                {
                    string content = outputBuffer.ToString();
                    outputBuffer.Clear();
                    content = content.Replace("\r\n", "\n");
                    content = content.Replace(">>> ", "").Replace("... ", "");
                    if (content.Contains("\n"))
                    {
                        string[] lines = content.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (i < lines.Length - 1)
                            {
                                os.write(lines[i]);
                            }
                            else if (!string.IsNullOrEmpty(lines[i]))
                            {
                                os.writeSingle(lines[i]);
                            }
                        }
                    }
                    else
                    {
                        os.writeSingle(content);
                    }
                }
            }
            if (processStarted && pythonProcess.HasExited)
            {
                this.isExiting = true;
            }
        }
        public override void Draw(float t)
        {
            base.Draw(t);
            drawTarget("app:");
            drawOutline();
            Rectangle contentRect = new Rectangle(bounds.X + 2, bounds.Y + 17, bounds.Width - 4, bounds.Height - 19);
            spriteBatch.Draw(Utils.white, contentRect, Color.Black * 0.8f);
            DrawAnimatedPythonIcon(contentRect, os.timer);
            string status = processStarted ? (pythonProcess.HasExited ? "EXITED" : "ACTIVE") : "STARTING";
            Color statusColor = processStarted && !pythonProcess.HasExited ? Color.Lime : Color.Red;
            TextItem.doCenteredFontLabel(new Rectangle(contentRect.X, contentRect.Y + contentRect.Height - 30, contentRect.Width, 30), "PYTHON INTERPRETER [" + status + "]", GuiData.font, statusColor);
        }
        private void DrawAnimatedPythonIcon(Rectangle rect, float totalTime)
        {
            float centerX = rect.X + rect.Width / 2f;
            float centerY = rect.Y + rect.Height / 2f - 10f;
            float baseSize = Math.Min(rect.Width, rect.Height) * 0.4f;
            Color pyBlue = new Color(55, 118, 171);
            Color pyYellow = new Color(255, 211, 67);
            float rotation = (float)Math.Sin(totalTime * 0.5f) * 0.1f;
            float pulse = (float)Math.Sin(totalTime * 2f) * 0.02f + 1f;
            float scale = baseSize * pulse;
            for (int i = 0; i < pyEdgeA.Length; i++)
            {
                int indexA = pyEdgeA[i];
                int indexB = pyEdgeB[i];
                Color c = indexA < 16 ? pyBlue : pyYellow;
                Vector2 p1 = GetTransformedPos(centerX, centerY, pyVertX[indexA], pyVertY[indexA], scale, rotation);
                Vector2 p2 = GetTransformedPos(centerX, centerY, pyVertX[indexB], pyVertY[indexB], scale, rotation);
                p2 += new Vector2((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1)) * 0.3f;
                DrawLine(p1, p2, c * 0.9f);
            }
            Vector2 eye1 = GetTransformedPos(centerX, centerY, -0.3f, -0.3f, scale, rotation);
            Vector2 eye2 = GetTransformedPos(centerX, centerY, 0.3f, 0.3f, scale, rotation);
            spriteBatch.Draw(Utils.white, new Rectangle((int)eye1.X - 1, (int)eye1.Y - 1, 3, 3), Color.White);
            spriteBatch.Draw(Utils.white, new Rectangle((int)eye2.X - 1, (int)eye2.Y - 1, 3, 3), Color.White);
        }
        private Vector2 GetTransformedPos(float cx, float cy, float vx, float vy, float scale, float rotation)
        {
            float rx = (float)(vx * Math.Cos(rotation) - vy * Math.Sin(rotation));
            float ry = (float)(vx * Math.Sin(rotation) + vy * Math.Cos(rotation));
            return new Vector2(cx + rx * scale, cy + ry * scale);
        }
        private void DrawLine(Vector2 start, Vector2 end, Color color)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            spriteBatch.Draw(Utils.white,
                new Rectangle((int)start.X, (int)start.Y, (int)edge.Length(), 1),
                null, color, angle, Vector2.Zero, SpriteEffects.None, 0);
        }
        public override void OnCompleteKilled()
        {
            base.OnCompleteKilled();
            Cleanup();
        }
        public override void OnComplete()
        {
            base.OnComplete();
            Cleanup();
        }
        private void Cleanup()
        {
            processStarted = false;
            if (pythonProcess != null && !pythonProcess.HasExited)
            {
                try { pythonProcess.Kill(); } catch { }
            }
            if (ActivePythonInstance == this) ActivePythonInstance = null;
        }
    }
    [HarmonyPatch(typeof(OS), nameof(OS.execute))]
    public static class OS_Execute_Patch
    {
        public static bool Prefix(string text)
        {
            if (PythonExe.ActivePythonInstance != null && !PythonExe.ActivePythonInstance.isExiting)
            {
                string trimmed = text.Trim();
                if (trimmed == "exit" || trimmed == "exit()")
                {
                    PythonExe.ActivePythonInstance.isExiting = true;
                    return true;
                }
                PythonExe.ActivePythonInstance.os.write("> " + text);
                PythonExe.ActivePythonInstance.SendInput(text);
                return false;
            }
            return true;
        }
    }
}
