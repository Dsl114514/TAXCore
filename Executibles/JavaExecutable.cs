using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Hacknet;
using Hacknet.Gui;
using Pathfinder.Executable;
using TAXCore.Config;
namespace TAXCore.Executibles
{
    public class JavaExecutable : GameExecutable
    {
        private Process javaProcess;
        private bool isStarted = false;
        private List<string> outputLines = new List<string>();
        private object outputLock = new object();
        private static int[] javaEdgeA = {
            0, 1, 2, 3, 
            4, 5, 6,    
            8, 9,       
            11, 12,     
            14, 15      
        };
        private static int[] javaEdgeB = {
            1, 2, 3, 0, 
            5, 6, 7,    
            9, 10,      
            12, 13,     
            15, 16      
        };
        private static float[] javaVertX = {
            -0.3f, 0.3f, 0.25f, -0.25f,
            0.3f, 0.45f, 0.45f, 0.25f,
            -0.15f, -0.2f, -0.15f,
            0.05f, 0.1f, 0.05f,
            0.25f, 0.2f, 0.25f
        };
        private static float[] javaVertY = {
            -0.1f, -0.1f, 0.4f, 0.4f,
            0.0f, 0.05f, 0.25f, 0.3f,
            -0.2f, -0.4f, -0.6f,
            -0.2f, -0.4f, -0.6f,
            -0.2f, -0.4f, -0.6f
        };
        private static Random rnd = new Random();
        public JavaExecutable() : base() 
        {
            this.ramCost = 160;
            this.IdentifierName = "JavaRuntime";
        }
        public override void OnInitialize()
        {
            base.OnInitialize();
            if (Args.Length < 3 || Args[1] != "-jar")
            {
                os.write("Usage: java -jar <jar_name>");
                Result = CompletionResult.Error;
                return;
            }
            string jarName = Args[2];
            Computer targetComp = os.connectedComp ?? os.thisComputer;
            FileEntry jarFile = targetComp.files.root.searchForFile(jarName);
            if (jarFile == null)
            {
                Folder current = targetComp.files.root;
                foreach (int index in os.navigationPath)
                {
                    if (index >= 0 && index < current.folders.Count)
                        current = current.folders[index];
                }
                jarFile = current.searchForFile(jarName);
            }
            if (jarFile == null || (!jarFile.data.StartsWith("JFILE_PATH:") && !jarFile.data.StartsWith("#JAVAF#:")))
            {
                os.write("Error: Could not find java file " + jarName);
                Result = CompletionResult.Error;
                return;
            }
            string prefix = jarFile.data.StartsWith("JFILE_PATH:") ? "JFILE_PATH:" : "#JAVAF#:";
            string realPath = jarFile.data.Substring(prefix.Length);
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
            if (!File.Exists(realPath))
            {
                os.write("Error: Real JAR file not found at " + realPath);
                Result = CompletionResult.Error;
                return;
            }
            StartJavaProcess(realPath);
        }
        private void StartJavaProcess(string jarPath)
        {
            try
            {
                javaProcess = new Process();
                javaProcess.StartInfo.FileName = TAXCoreConfig.JavaPath;
                javaProcess.StartInfo.Arguments = $"-jar \"{jarPath}\"";
                javaProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(jarPath);
                javaProcess.StartInfo.UseShellExecute = false;
                javaProcess.StartInfo.RedirectStandardOutput = true;
                javaProcess.StartInfo.RedirectStandardError = true;
                javaProcess.StartInfo.CreateNoWindow = true;
                javaProcess.OutputDataReceived += (s, e) => {
                    if (e.Data != null) {
                        lock(outputLock) outputLines.Add(e.Data);
                    }
                };
                javaProcess.ErrorDataReceived += (s, e) => {
                    if (e.Data != null) {
                        lock(outputLock) outputLines.Add("[ERROR] " + e.Data);
                    }
                };
                javaProcess.Start();
                javaProcess.BeginOutputReadLine();
                javaProcess.BeginErrorReadLine();
                isStarted = true;
                os.write($"Java process started: {Path.GetFileName(jarPath)}");
            }
            catch (Exception ex)
            {
                os.write("Error starting Java: " + ex.Message);
                Result = CompletionResult.Error;
            }
        }
        public override void OnUpdate(float delta)
        {
            base.OnUpdate(delta);
            if (isStarted)
            {
                lock (outputLock)
                {
                    foreach (var line in outputLines)
                    {
                        os.write(line);
                    }
                    outputLines.Clear();
                }
                if (javaProcess.HasExited)
                {
                    os.write($"Java process exited with code {javaProcess.ExitCode}");
                    os.write("Program execution finished. Manual termination required to close terminal tool.");
                    isStarted = false;
                }
            }
        }
        public override void OnCompleteKilled()
        {
            base.OnCompleteKilled();
            KillProcess();
        }
        public override void Draw(float t)
        {
            base.Draw(t);
            drawTarget("app:");
            drawOutline();
            Rectangle contentRect = new Rectangle(bounds.X + 2, bounds.Y + 17, bounds.Width - 4, bounds.Height - 19);
            spriteBatch.Draw(Utils.white, contentRect, Color.Black * 0.8f);
            DrawAnimatedJavaIcon(contentRect, os.timer);
            string status = isStarted ? (javaProcess.HasExited ? "EXITED" : "ACTIVE") : "IDLE";
            Color statusColor = isStarted && !javaProcess.HasExited ? Color.Orange : Color.Gray;
            TextItem.doCenteredFontLabel(new Rectangle(contentRect.X, contentRect.Y + contentRect.Height - 30, contentRect.Width, 30), "JAVA RUNTIME [" + status + "]", GuiData.font, statusColor);
        }
        private void DrawAnimatedJavaIcon(Rectangle rect, float totalTime)
        {
            float centerX = rect.X + rect.Width / 2f;
            float centerY = rect.Y + rect.Height / 2f + 10f;
            float baseSize = Math.Min(rect.Width, rect.Height) * 0.4f;
            Color javaRed = new Color(243, 68, 54); 
            Color javaBlue = new Color(83, 130, 161); 
            float rotation = (float)Math.Sin(totalTime * 0.8f) * 0.05f;
            float pulse = (float)Math.Sin(totalTime * 1.5f) * 0.03f + 1f;
            float scale = baseSize * pulse;
            float steamOffset = (float)Math.Sin(totalTime * 3f) * 0.02f;
            for (int i = 0; i < javaEdgeA.Length; i++)
            {
                int indexA = javaEdgeA[i];
                int indexB = javaEdgeB[i];
                Color c = indexA >= 8 ? javaBlue * 0.6f : javaRed;
                float vxA = javaVertX[indexA];
                float vxB = javaVertX[indexB];
                if (indexA >= 8) vxA += steamOffset * (float)Math.Sin(totalTime * 2f + indexA);
                if (indexB >= 8) vxB += steamOffset * (float)Math.Sin(totalTime * 2f + indexB);
                Vector2 p1 = GetTransformedPos(centerX, centerY, vxA, javaVertY[indexA], scale, rotation);
                Vector2 p2 = GetTransformedPos(centerX, centerY, vxB, javaVertY[indexB], scale, rotation);
                p1 += new Vector2((float)(rnd.NextDouble() * 1 - 0.5f), (float)(rnd.NextDouble() * 1 - 0.5f)) * 0.2f;
                p2 += new Vector2((float)(rnd.NextDouble() * 1 - 0.5f), (float)(rnd.NextDouble() * 1 - 0.5f)) * 0.2f;
                DrawLine(p1, p2, c * 0.9f);
            }
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
        private void KillProcess()
        {
            if (isStarted && javaProcess != null && !javaProcess.HasExited)
            {
                try { javaProcess.Kill(); } catch { }
            }
        }
    }
}

