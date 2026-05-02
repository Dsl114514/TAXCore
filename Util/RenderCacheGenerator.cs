using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TAXCore.Config;
namespace TAXCore.Util
{
    public static class RenderCacheGenerator
    {
        public static void GenerateCacheFromVideo(string videoPath, string outputPath, int width, int height, int fps, float scale = 0.5f)
        {
            if (!File.Exists(videoPath)) throw new FileNotFoundException("Video file not found", videoPath);

            int targetW = (int)(width * scale);
            int targetH = (int)(height * scale);
            targetW = targetW % 2 == 0 ? targetW : targetW + 1;
            targetH = targetH % 2 == 0 ? targetH : targetH + 1;

            Task.Run(() => {
                try
                {
                    Console.WriteLine($"[TAXCore] Starting optimized cache generation: {videoPath} -> {outputPath} ({targetW}x{targetH}, 16-bit)");
                    string args = $"-i \"{videoPath}\" -vf \"fps={fps},scale={targetW}:{targetH}:force_original_aspect_ratio=decrease,pad={targetW}:{targetH}:(ow-iw)/2:(oh-ih)/2:black\" -f image2pipe -vcodec rawvideo -pix_fmt rgb565le -";
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = TAXCoreConfig.FFmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    using (Process ffmpeg = Process.Start(psi))
                    using (BinaryWriter writer = new BinaryWriter(File.Create(outputPath)))
                    {
                        writer.Write(System.Text.Encoding.ASCII.GetBytes("TXC3"));
                        writer.Write(targetW);
                        writer.Write(targetH);
                        writer.Write(fps);
                        long frameCountPos = writer.BaseStream.Position;
                        writer.Write(0);
                        Stream stdout = ffmpeg.StandardOutput.BaseStream;
                        int frameSize = targetW * targetH * 2; 
                        byte[] buffer = new byte[frameSize];
                        int framesProcessed = 0;
                        while (true)
                        {
                            int totalRead = 0;
                            while (totalRead < frameSize)
                            {
                                int read = stdout.Read(buffer, totalRead, frameSize - totalRead);
                                if (read <= 0) break;
                                totalRead += read;
                            }
                            if (totalRead < frameSize) break;
                            writer.Write(buffer);
                            framesProcessed++;
                            if (framesProcessed % 100 == 0)
                                Console.WriteLine($"[TAXCore] Cached {framesProcessed} frames...");
                        }
                        writer.BaseStream.Position = frameCountPos;
                        writer.Write(framesProcessed);
                        Console.WriteLine($"[TAXCore] Cache generation complete. {framesProcessed} frames saved (TXC3).");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TAXCore] Cache Generation Failed: " + ex.Message);
                }
            });
        }
    }
}

