using System;
using System.Collections.Generic;
using System.IO;
using Hacknet;
using Pathfinder.Executable;
using Pathfinder.Meta.Load;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Hacknet.Gui;
using TAXCore.Patches;
namespace TAXCore.Executibles
{
    [Pathfinder.Meta.Load.Executable("#VIM#")]
    public class VimExecutable : GameExecutable
    {
        private string targetFileName;
        private string targetDirectoryPath = ".";
        private string fileContent = "";
        private string editingContent = "";
        private FileEntry targetFile;
        private Folder targetFolder;
        private bool isSaving = false;
        private float saveTimer = 0f;
        private int cursorPosition = 0;
        private int myID = 987213;
        private Vector2 scrollOffset = Vector2.Zero;
        private bool isFocused = false;
        private enum VimMode { Normal, Insert, Command }
        private VimMode currentMode = VimMode.Normal;
        private string commandLine = "";
        private string statusMessage = "";
        private float statusTimer = 0f;
        public VimExecutable() : base()
        {
            this.ramCost = 100;
            this.IdentifierName = "VimTextEditor";
        }
        public override void OnInitialize()
        {
            base.OnInitialize();
            if (Args.Length > 1)
            {
                targetFileName = Args[1];
            }
            else
            {
                targetFileName = GetLastCattedFileName();
            }
            FindFile();
            if (targetFile != null)
            {
                fileContent = targetFile.data;
                if (fileContent.StartsWith("#JAVAF#:") || fileContent.StartsWith("#PYF#:") || fileContent.StartsWith("JFILE_PATH:"))
                {
                    editingContent = Computer.generateBinaryString(300);
                    SetStatus("Vim: [Link File] " + targetFileName + " (Read Only)");
                }
                else
                {
                    editingContent = fileContent;
                    SetStatus("Vim: Editing " + targetFileName + " (Type :q to exit)");
                }
                os.write("Vim: Opening " + targetFileName);
            }
            else if (targetFileName != null)
            {
                os.write("Vim: Creating new file " + targetFileName);
                fileContent = "";
                editingContent = "";
                SetStatus("Vim: New File (Type :q to exit)");
            }
            else
            {
                os.write("Usage: vim <filename> (or cat a file first)");
                this.isExiting = true;
                if (os.terminal != null) os.terminal.inputLocked = false;
                return;
            }
            editingContent = editingContent ?? "";
            cursorPosition = editingContent.Length;
        }
        private string GetLastCattedFileName()
        {
            if (os.terminal == null || os.terminal.runCommands == null) return null;
            for (int i = os.terminal.runCommands.Count - 1; i >= 0; i--)
            {
                string cmd = os.terminal.runCommands[i].Trim();
                if (cmd.StartsWith("cat ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = cmd.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        return parts[1];
                    }
                }
            }
            return null;
        }
        private void FindFile()
        {
            if (string.IsNullOrEmpty(targetFileName)) return;
            string filename = targetFileName;
            Folder folder = null;
            if (targetFileName.Contains("/") || targetFileName.Contains("\\"))
            {
                string[] parts = targetFileName.Split(Utils.directorySplitterDelim, StringSplitOptions.RemoveEmptyEntries);
                filename = parts[parts.Length - 1];
                targetDirectoryPath = targetFileName.Substring(0, Math.Max(0, targetFileName.Length - filename.Length - 1));
                Computer comp = os.connectedComp ?? os.thisComputer;
                folder = Programs.getFolderAtPath(targetDirectoryPath, os, comp.files.root, false);
            }
            if (folder == null)
            {
                folder = Programs.getCurrentFolder(os);
            }
            targetFolder = folder;
            targetFile = targetFolder.searchForFile(filename);
            targetFileName = filename; 
        }
        public override void Update(float t)
        {
            if (isExiting)
            {
                if (os.terminal != null) os.terminal.inputLocked = false;
                base.Update(t);
                return;
            }
            this.bounds = os.fullscreen;
            this.isFocused = true; 
            base.Update(t);
            if (isSaving)
            {
                saveTimer -= t;
                if (saveTimer <= 0) isSaving = false;
            }
            if (statusTimer > 0)
            {
                statusTimer -= t;
            }
            if (isFocused)
            {
                GuiData.active = myID;
                GuiData.willBlockTextInput = true;
                if (os.terminal != null) os.terminal.inputLocked = true;
                HandleInput();
                string[] lines = editingContent.Split('\n');
                int currentLine = 0;
                int tempPos = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (cursorPosition <= tempPos + lines[i].Length)
                    {
                        currentLine = i;
                        break;
                    }
                    tempPos += lines[i].Length + 1;
                }
                int currentColumn = cursorPosition - tempPos;
                float fontHeight = GuiData.smallfont.LineSpacing;
                Rectangle contentRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                Rectangle textRect = new Rectangle(contentRect.X + 10, contentRect.Y + 10, contentRect.Width - 20, contentRect.Height - 60);
                string textBeforeCursor = lines[currentLine].Substring(0, Math.Min(lines[currentLine].Length, Math.Max(0, currentColumn)));
                float cursorX = GuiData.smallfont.MeasureString(textBeforeCursor).X;
                float yPos = textRect.Y + ((currentLine - scrollOffset.Y) * fontHeight);
                IMEHandler.UpdateIMEWindowPosition((int)(textRect.X + cursorX), (int)yPos + (int)fontHeight);
            }
            else
            {
                IMEHandler.UpdateIMEWindowPosition(-1000, -1000);
            }
        }
        private void SetStatus(string msg, float time = 3f)
        {
            statusMessage = msg;
            statusTimer = time;
        }
        private void HandleInput()
        {
            var input = GuiData.getKeyboadState();
            var lastInput = GuiData.getLastKeyboadState();
            bool escPressed = input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape);
            if (currentMode == VimMode.Insert)
            {
                if (escPressed)
                {
                    currentMode = VimMode.Normal;
                    cursorPosition = Math.Max(0, cursorPosition - 1);
                    return;
                }
                foreach (char c in GuiData.getFilteredKeys())
                {
                    if (c >= ' ' || c == '\t')
                    {
                        editingContent = editingContent.Insert(cursorPosition, c.ToString());
                        cursorPosition++;
                    }
                }
                if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Back) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Back))
                {
                    if (cursorPosition > 0)
                    {
                        editingContent = editingContent.Remove(cursorPosition - 1, 1);
                        cursorPosition--;
                    }
                }
                if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Delete) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Delete))
                {
                    if (cursorPosition < editingContent.Length)
                    {
                        editingContent = editingContent.Remove(cursorPosition, 1);
                    }
                }
                if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter))
                {
                    editingContent = editingContent.Insert(cursorPosition, "\n");
                    cursorPosition++;
                }
            }
            else if (currentMode == VimMode.Normal)
            {
                foreach (char c in GuiData.getFilteredKeys())
                {
                    switch (c)
                    {
                        case 'i':
                            currentMode = VimMode.Insert;
                            break;
                        case ':':
                            currentMode = VimMode.Command;
                            commandLine = "";
                            break;
                        case 'h':
                            cursorPosition = Math.Max(0, cursorPosition - 1);
                            break;
                        case 'l':
                            cursorPosition = Math.Min(editingContent.Length, cursorPosition + 1);
                            break;
                        case 'j':
                            MoveCursorDown();
                            break;
                        case 'k':
                            MoveCursorUp();
                            break;
                        case 'x':
                            if (cursorPosition < editingContent.Length)
                                editingContent = editingContent.Remove(cursorPosition, 1);
                            break;
                        case 'a':
                            cursorPosition = Math.Min(editingContent.Length, cursorPosition + 1);
                            currentMode = VimMode.Insert;
                            break;
                    }
                }
            }
            else if (currentMode == VimMode.Command)
            {
                if (escPressed)
                {
                    currentMode = VimMode.Normal;
                    return;
                }
                foreach (char c in GuiData.getFilteredKeys())
                {
                    if (c >= ' ' || c == '\t')
                    {
                        commandLine += c;
                    }
                }
                if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Back) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Back))
                {
                    if (commandLine.Length > 0)
                        commandLine = commandLine.Substring(0, commandLine.Length - 1);
                }
                if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Enter))
                {
                    ExecuteVimCommand(commandLine);
                    if (currentMode == VimMode.Command) currentMode = VimMode.Normal;
                }
            }
            if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left))
                cursorPosition = Math.Max(0, cursorPosition - 1);
            if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right))
                cursorPosition = Math.Min(editingContent.Length, cursorPosition + 1);
            if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Up) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Up))
                MoveCursorUp();
            if (input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Down) && !lastInput.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Down))
                MoveCursorDown();
        }
        private void MoveCursorUp()
        {
            int lineStart = editingContent.LastIndexOf('\n', Math.Max(0, cursorPosition - 1));
            if (lineStart >= 0)
            {
                int prevLineStart = editingContent.LastIndexOf('\n', Math.Max(0, lineStart - 1));
                int column = cursorPosition - (lineStart + 1);
                cursorPosition = Math.Min(lineStart, (prevLineStart == -1 ? 0 : prevLineStart + 1) + column);
            }
            else
            {
                cursorPosition = 0;
            }
        }
        private void MoveCursorDown()
        {
            int lineStart = editingContent.LastIndexOf('\n', Math.Max(0, cursorPosition - 1));
            int column = cursorPosition - (lineStart + 1);
            int nextLineStart = editingContent.IndexOf('\n', cursorPosition);
            if (nextLineStart >= 0)
            {
                int nextLineEnd = editingContent.IndexOf('\n', nextLineStart + 1);
                if (nextLineEnd == -1) nextLineEnd = editingContent.Length;
                cursorPosition = Math.Min(nextLineEnd, nextLineStart + 1 + column);
            }
            else
            {
                cursorPosition = editingContent.Length;
            }
        }
        private void ExecuteVimCommand(string cmd)
        {
            cmd = cmd.Trim();
            if (cmd == "w")
            {
                SaveFile();
            }
            else if (cmd == "q")
            {
                ExitAndCd();
            }
            else if (cmd == "wq")
            {
                SaveFile();
                ExitAndCd();
            }
            else if (cmd == "q!")
            {
                ExitAndCd();
            }
            else
            {
                SetStatus("Unknown command: " + cmd);
            }
        }
        private void ExitAndCd()
        {
            this.isExiting = true;
            if (targetDirectoryPath != null && targetDirectoryPath != "." && targetDirectoryPath != "")
            {
                os.runCommand("cd " + targetDirectoryPath);
            }
        }
        public override void Draw(float t)
        {
            spriteBatch.Draw(Utils.white, bounds, Color.Black * 0.95f);
            Rectangle contentRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            Rectangle textRect = new Rectangle(contentRect.X + 10, contentRect.Y + 10, contentRect.Width - 20, contentRect.Height - 60);
            Rectangle statusBarRect = new Rectangle(contentRect.X, contentRect.Y + contentRect.Height - 40, contentRect.Width, 20);
            Rectangle commandBarRect = new Rectangle(contentRect.X, contentRect.Y + contentRect.Height - 20, contentRect.Width, 20);
            float fontHeight = GuiData.smallfont.LineSpacing;
            int maxVisibleLines = (int)(textRect.Height / fontHeight);
            float maxWidth = textRect.Width;
            List<string> wrappedLines = new List<string>();
            List<int> lineOriginalIndices = new List<int>(); 
            List<int> lineStartPositions = new List<int>(); 
            string[] originalLines = editingContent.Split('\n');
            int currentWrappedLine = 0;
            int currentWrappedColumn = 0;
            int globalPos = 0;
            for (int i = 0; i < originalLines.Length; i++)
            {
                string line = originalLines[i];
                if (line.Length == 0)
                {
                    if (cursorPosition == globalPos)
                    {
                        currentWrappedLine = wrappedLines.Count;
                        currentWrappedColumn = 0;
                    }
                    lineStartPositions.Add(globalPos);
                    wrappedLines.Add("");
                    lineOriginalIndices.Add(i);
                }
                else
                {
                    string remaining = line;
                    int subLineOffset = 0;
                    while (remaining.Length > 0)
                    {
                        int charCount = 0;
                        float currentWidth = 0;
                        while (charCount < remaining.Length)
                        {
                            float charWidth = GuiData.smallfont.MeasureString(remaining[charCount].ToString()).X;
                            if (currentWidth + charWidth > maxWidth && charCount > 0) break;
                            currentWidth += charWidth;
                            charCount++;
                        }
                        string subLine = remaining.Substring(0, charCount);
                        if (cursorPosition >= globalPos + subLineOffset && cursorPosition <= globalPos + subLineOffset + charCount)
                        {
                            bool isEndOfSubLine = cursorPosition == globalPos + subLineOffset + charCount;
                            bool hasMoreInOriginal = charCount < remaining.Length;
                            if (!(isEndOfSubLine && hasMoreInOriginal))
                            {
                                currentWrappedLine = wrappedLines.Count;
                                currentWrappedColumn = cursorPosition - (globalPos + subLineOffset);
                            }
                        }
                        lineStartPositions.Add(globalPos + subLineOffset);
                        wrappedLines.Add(subLine);
                        lineOriginalIndices.Add(i);
                        remaining = remaining.Substring(charCount);
                        subLineOffset += charCount;
                    }
                }
                globalPos += line.Length + 1; 
            }
            if (cursorPosition == editingContent.Length && (editingContent.Length == 0 || editingContent[editingContent.Length - 1] == '\n'))
            {
                currentWrappedLine = wrappedLines.Count;
                currentWrappedColumn = 0;
            }
            if (currentWrappedLine < scrollOffset.Y) scrollOffset.Y = currentWrappedLine;
            if (currentWrappedLine >= scrollOffset.Y + maxVisibleLines) scrollOffset.Y = currentWrappedLine - maxVisibleLines + 1;
            for (int i = (int)scrollOffset.Y; i < wrappedLines.Count; i++)
            {
                float yPos = textRect.Y + ((i - scrollOffset.Y) * fontHeight);
                if (yPos + fontHeight > textRect.Y && yPos < textRect.Y + textRect.Height)
                {
                    spriteBatch.DrawString(GuiData.smallfont, wrappedLines[i], new Vector2(textRect.X, yPos), Color.White);
                    if (i == currentWrappedLine && isFocused)
                    {
                        IMEHandler.OnDrawActiveField(spriteBatch, GuiData.smallfont, (int)(textRect.X), (int)yPos + (int)fontHeight, wrappedLines[i]);
                    }
                    if (i == currentWrappedLine && isFocused && (DateTime.Now.Millisecond % 1000 < 500))
                    {
                        string textBeforeCursor = wrappedLines[i].Substring(0, Math.Min(wrappedLines[i].Length, currentWrappedColumn));
                        float cursorX = GuiData.smallfont.MeasureString(textBeforeCursor).X;
                        Color cursorColor = currentMode == VimMode.Insert ? Color.White : Color.Gray * 0.8f;
                        spriteBatch.Draw(Utils.white, new Rectangle((int)(textRect.X + cursorX), (int)yPos, currentMode == VimMode.Insert ? 2 : 8, (int)fontHeight), cursorColor);
                    }
                }
            }
            spriteBatch.Draw(Utils.white, statusBarRect, Color.DarkBlue * 0.6f);
            string modeDisplay = $"-- {currentMode.ToString().ToUpper()} --";
            if (currentMode == VimMode.Normal) modeDisplay = "-- NORMAL -- (i:Insert, :q:Quit)";
            int realLine = 0;
            int realCol = 0;
            int tPos = 0;
            string[] oLines = editingContent.Split('\n');
            for (int i = 0; i < oLines.Length; i++)
            {
                if (cursorPosition <= tPos + oLines[i].Length)
                {
                    realLine = i;
                    realCol = cursorPosition - tPos;
                    break;
                }
                tPos += oLines[i].Length + 1;
            }
            string statusRight = $"Line {realLine + 1}, Col {realCol + 1} ";
            spriteBatch.DrawString(GuiData.smallfont, modeDisplay, new Vector2(statusBarRect.X + 5, statusBarRect.Y + 2), Color.White);
            float rightWidth = GuiData.smallfont.MeasureString(statusRight).X;
            spriteBatch.DrawString(GuiData.smallfont, statusRight, new Vector2(statusBarRect.X + statusBarRect.Width - rightWidth - 5, statusBarRect.Y + 2), Color.White);
            spriteBatch.Draw(Utils.white, commandBarRect, Color.Black * 0.8f);
            if (currentMode == VimMode.Command)
            {
                spriteBatch.DrawString(GuiData.smallfont, ":" + commandLine, new Vector2(commandBarRect.X + 5, commandBarRect.Y + 2), Color.White);
            }
            else if (statusTimer > 0)
            {
                spriteBatch.DrawString(GuiData.smallfont, statusMessage, new Vector2(commandBarRect.X + 5, commandBarRect.Y + 2), Color.Orange);
            }
            else if (isSaving)
            {
                spriteBatch.DrawString(GuiData.smallfont, "File Saved Successfully", new Vector2(commandBarRect.X + 5, commandBarRect.Y + 2), Color.Green);
            }
        }
        private void SaveFile()
        {
            if (fileContent.StartsWith("#JAVAF#:") || fileContent.StartsWith("#PYF#:") || fileContent.StartsWith("JFILE_PATH:"))
            {
                SetStatus("Cannot save to a link file! Use :q to exit.");
                return;
            }
            if (targetFile != null)
            {
                targetFile.data = editingContent;
            }
            else if (targetFileName != null && targetFolder != null)
            {
                targetFile = new FileEntry(editingContent, targetFileName);
                targetFolder.files.Add(targetFile);
            }
            isSaving = true;
            saveTimer = 1.5f;
            os.write("Vim: File saved.");
        }
    }
}

