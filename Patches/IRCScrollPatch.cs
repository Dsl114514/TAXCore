using System;
using System.Collections.Generic;
using Hacknet;
using Hacknet.Daemons.Helpers;
using Hacknet.Gui;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TAXCore.Patches
{
    
    
    
    
    [HarmonyPatch]
    public static class IRCScrollPatch
    {
        
        private static Dictionary<IRCSystem, int> scrollOffsets = new Dictionary<IRCSystem, int>();

        
        private static Dictionary<IRCSystem, bool> userScrolled = new Dictionary<IRCSystem, bool>();

        
        private static int GetScrollOffset(IRCSystem system)
        {
            if (!scrollOffsets.ContainsKey(system))
            {
                scrollOffsets[system] = 0;
                userScrolled[system] = false;
            }
            return scrollOffsets[system];
        }

        
        private static void SetScrollOffset(IRCSystem system, int offset)
        {
            scrollOffsets[system] = Math.Max(0, offset);
            
            if (offset > 0)
            {
                userScrolled[system] = true;
            }
        }

        
        
        
        [HarmonyPatch(typeof(IRCSystem), nameof(IRCSystem.GetLogsFromFile))]
        [HarmonyPostfix]
        public static void GetLogsFromFilePostfix(IRCSystem __instance, ref List<IRCSystem.IRCLogEntry> __result)
        {
            
            

            
            var activeLogFile = __instance.ActiveLogFile;
            if (activeLogFile == null || string.IsNullOrEmpty(activeLogFile.data))
            {
                return;
            }

            
            string text = activeLogFile.data;

            
            if (text.StartsWith("#"))
            {
                text = text.Substring(1);
            }

            
            string[] array = text.Split(IRCSystem.EntryLineDelimiter, StringSplitOptions.None);
            List<IRCSystem.IRCLogEntry> list = new List<IRCSystem.IRCLogEntry>();

            for (int i = 0; i < array.Length; i++)
            {
                
                if (!string.IsNullOrEmpty(array[i]))
                {
                    list.Add(IRCSystem.IRCLogEntry.DeserializeSafe(array[i]));
                }
            }

            
            __result = list;
        }

        
        
        
        [HarmonyPatch(typeof(IRCSystem), nameof(IRCSystem.DrawLog))]
        [HarmonyPrefix]
        public static bool DrawLogPrefix(
            IRCSystem __instance,
            Rectangle dest,
            SpriteBatch sb,
            Dictionary<string, Color> HighlightKeywords)
        {
            
            List<IRCSystem.IRCLogEntry> logsFromFile = __instance.GetLogsFromFile();

            if (logsFromFile.Count == 0)
            {
                return true; 
            }

            
            System.Diagnostics.Debug.WriteLine($"[IRCScrollPatch] DrawLog called. Messages: {logsFromFile.Count}");

            int lineHeight = (int)(GuiData.ActiveFontConfig.tinyFontCharHeight + 4f);
            int lineSpacing = 0;

            
            List<int> messageLineCounts = new List<int>();
            int totalLines = 0;

            foreach (var log in logsFromFile)
            {
                string authorText = "<" + log.Author + ">";
                int authorWidth = (int)GuiData.tinyfont.MeasureString(authorText).X;

                int timestampAreaWidth = 55;
                int spacing = 4;
                int nameAreaWidth = Math.Max(76, authorWidth + spacing);
                int scrollBarWidth = 10;
                int messageWidth = dest.Width - (timestampAreaWidth + spacing + nameAreaWidth + scrollBarWidth);

                string[] messageLines;
                if (!log.Message.StartsWith("!ATTACHMENT:"))
                {
                    string wrappedText = Utils.SuperSmartTwimForWidth(log.Message, messageWidth, GuiData.tinyfont);
                    messageLines = wrappedText.Split(Utils.newlineDelim, StringSplitOptions.None);
                }
                else
                {
                    messageLines = new string[] { log.Message };
                }

                int linesForThisMessage = messageLines.Length;
                messageLineCounts.Add(linesForThisMessage);
                totalLines += linesForThisMessage;
            }

            
            int maxLinesOnScreen = (int)((float)dest.Height / (float)lineHeight);

            
            int maxScroll = Math.Max(0, totalLines - maxLinesOnScreen);

            
            float wheelScroll = GuiData.getMouseWheelScroll();
            if (wheelScroll != 0)
            {
                int currentOffset = GetScrollOffset(__instance);
                
                int newOffset = currentOffset - (int)wheelScroll;

                
                newOffset = Math.Max(0, Math.Min(newOffset, maxScroll));

                SetScrollOffset(__instance, newOffset);
            }
            else if (!userScrolled.ContainsKey(__instance) || !userScrolled[__instance])
            {
                
                SetScrollOffset(__instance, 0);
            }

            
            int scrollOffset = GetScrollOffset(__instance);

            
            

            
            int bottomMessageIndex = logsFromFile.Count - 1;
            int linesToSkip = scrollOffset;

            
            for (int i = logsFromFile.Count - 1; i >= 0 && linesToSkip > 0; i--)
            {
                if (messageLineCounts[i] <= linesToSkip)
                {
                    linesToSkip -= messageLineCounts[i];
                    bottomMessageIndex = i - 1; 
                }
                else
                {
                    
                    bottomMessageIndex = i;
                    break;
                }
            }

            if (bottomMessageIndex < 0) bottomMessageIndex = 0;

            
            int topMessageIndex = bottomMessageIndex;
            int displayedLines = 0;

            for (int i = bottomMessageIndex; i >= 0; i--)
            {
                displayedLines += messageLineCounts[i];
                topMessageIndex = i;

                if (displayedLines >= maxLinesOnScreen)
                {
                    break;
                }
            }

            
            Rectangle? originalScissor = sb.GraphicsDevice.ScissorRectangle;

            
            int currentY = dest.Y + dest.Height; 
            int linesDrawn = 0;
            bool reachedTop = false;

            
            
            for (int i = bottomMessageIndex; i >= topMessageIndex && !reachedTop; i--)
            {
                if (i < 0 || i >= logsFromFile.Count) continue;

                IRCSystem.IRCLogEntry log = logsFromFile[i];

                
                bool needsNewMessagesLineDraw = __instance.messagesAddedSinceLastView > 0 &&
                    __instance.messagesAddedSinceLastView < logsFromFile.Count &&
                    (logsFromFile.Count - i) == __instance.messagesAddedSinceLastView;

                
                string authorText = "<" + log.Author + ">";
                int authorWidth = (int)GuiData.tinyfont.MeasureString(authorText).X;

                
                
                
                
                int timestampAreaWidth = 55;  
                int spacing = 4;              
                int nameAreaWidth = Math.Max(76, authorWidth + spacing); 

                
                int scrollBarWidth = 10;  
                int messageWidth = dest.Width - (timestampAreaWidth + spacing + nameAreaWidth + scrollBarWidth);

                string messageText = log.Message;
                string[] messageLines;

                if (!log.Message.StartsWith("!ATTACHMENT:"))
                {
                    messageText = Utils.SuperSmartTwimForWidth(messageText, messageWidth, GuiData.tinyfont);
                    messageLines = messageText.Split(Utils.newlineDelim, StringSplitOptions.None);
                }
                else
                {
                    messageLines = new string[] { messageText };
                }

                
                for (int lineIdx = messageLines.Length - 1; lineIdx >= 0; lineIdx--)
                {
                    currentY -= lineHeight;

                    if (currentY < dest.Y)
                    {
                        reachedTop = true;
                        break;
                    }

                    Rectangle lineRect = new Rectangle(dest.X, currentY, dest.Width, lineHeight);

                    
                    if (lineIdx == 0)
                    {
                        
                        Rectangle timestampRect = new Rectangle(dest.X, currentY, timestampAreaWidth, lineHeight);
                        __instance.DrawLine("[" + log.Timestamp + "] ", timestampRect, sb, Color.White);

                        
                        Color authorColor = Color.LightBlue;
                        if (HighlightKeywords.ContainsKey(log.Author))
                        {
                            authorColor = HighlightKeywords[log.Author];
                        }

                        
                        Rectangle authorRect = new Rectangle(dest.X + timestampAreaWidth + spacing, currentY, nameAreaWidth, lineHeight);
                        __instance.DrawLine(authorText, authorRect, sb, authorColor);
                    }

                    
                    Color messageColor = Color.Lerp(Color.White,
                        HighlightKeywords.ContainsKey(log.Author) ? HighlightKeywords[log.Author] : Color.LightBlue,
                        0.22f);

                    
                    Rectangle messageRect = new Rectangle(
                        dest.X + timestampAreaWidth + spacing + nameAreaWidth,
                        currentY,
                        messageWidth,
                        lineHeight);

                    string lineContent = messageLines[lineIdx];
                    if (lineContent.StartsWith("!ATTACHMENT:"))
                    {
                        
                        __instance.DrawLine(lineContent, messageRect, sb, messageColor);
                    }
                    else if (lineContent.StartsWith("!ANNOUNCEMENT!"))
                    {
                        
                        __instance.DrawLine(lineContent, messageRect, sb, messageColor);
                    }
                    else
                    {
                        
                        __instance.DrawLine(lineContent, messageRect, sb, messageColor);
                    }

                    linesDrawn++;
                }

                if (reachedTop) break;

                
                Rectangle separatorLine = new Rectangle(dest.X + timestampAreaWidth + spacing + nameAreaWidth - 5,
                    currentY, 1, lineHeight * messageLines.Length + 4);
                sb.Draw(Utils.white, separatorLine, Color.White * 0.12f);

                
                if (needsNewMessagesLineDraw)
                {
                    Rectangle newMsgLine = new Rectangle(dest.X, currentY + lineHeight + 1, dest.Width, 1);
                    sb.Draw(Utils.white, newMsgLine, Color.White * 0.5f);
                }

                currentY -= lineSpacing;
            }

            
            
            if (originalScissor.HasValue)
            {
                sb.GraphicsDevice.ScissorRectangle = originalScissor.Value;
            }
            else
            {
                sb.GraphicsDevice.ScissorRectangle = new Rectangle(0, 0,
                    sb.GraphicsDevice.Viewport.Width, sb.GraphicsDevice.Viewport.Height);
            }

            if (maxScroll > 0)
            {
                DrawScrollBar(dest, sb, scrollOffset, maxScroll, maxLinesOnScreen);
            }
            return false; 
        }

        
        
        
        private static void DrawScrollBar(Rectangle dest, SpriteBatch sb, int scrollOffset, int maxScroll, int visibleLines)
        {
            int scrollBarWidth = 6;
            int scrollBarX = dest.X + dest.Width - scrollBarWidth - 2;

            
            Rectangle scrollBarBg = new Rectangle(scrollBarX, dest.Y, scrollBarWidth, dest.Height);
            sb.Draw(Utils.white, scrollBarBg, Color.Black * 0.3f);

            
            int totalLines = maxScroll + visibleLines;
            float scrollRatio = totalLines > 0 ? (float)visibleLines / totalLines : 1f;
            int thumbHeight = Math.Max(20, (int)(dest.Height * scrollRatio));

            
            
            float positionRatio = maxScroll > 0 ? (float)scrollOffset / maxScroll : 0f;

            
            int thumbY = dest.Y + dest.Height - thumbHeight - (int)((dest.Height - thumbHeight) * positionRatio);

            
            Rectangle thumbRect = new Rectangle(scrollBarX + 1, thumbY, scrollBarWidth - 2, thumbHeight);
            sb.Draw(Utils.white, thumbRect, Color.White * 0.6f);
        }

        
        
        
        [HarmonyPatch(typeof(IRCSystem), nameof(IRCSystem.LeftView))]
        [HarmonyPostfix]
        public static void LeftViewPostfix(IRCSystem __instance)
        {
            
            SetScrollOffset(__instance, 0);
            if (userScrolled.ContainsKey(__instance))
            {
                userScrolled[__instance] = false;
            }
        }
    }
}
