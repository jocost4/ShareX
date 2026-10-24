#region License Information (GPL v3)
/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/
#endregion License Information (GPL v3)

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using SkiaSharp;

namespace ShareX.ScreenCaptureLib;

public static class LinuxCaptureHelper
{
    private static readonly object _syncLock = new();

    public static SKBitmap? CaptureFullscreen()
    {
        lock (_syncLock)
        {
            string? filePath = CaptureViaPortal();
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    using FileStream stream = File.OpenRead(filePath);
                    return SKBitmap.Decode(stream);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load portal screenshot: {ex}");
                }
            }
            return null;
        }
    }

    public static SKBitmap? CaptureRectangle(Rectangle rect)
    {
        SKBitmap? full = CaptureFullscreen();
        if (full == null) return null;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return full;

        Rectangle fullBounds = new Rectangle(0, 0, full.Width, full.Height);
        Rectangle intersect = Rectangle.Intersect(fullBounds, rect);
        if (intersect.Width <= 0 || intersect.Height <= 0) return full;

        try
        {
            SKBitmap cropped = new SKBitmap(intersect.Width, intersect.Height);
            using (SKCanvas canvas = new SKCanvas(cropped))
            {
                canvas.DrawBitmap(full, -intersect.X, -intersect.Y);
            }
            full.Dispose();
            return cropped;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to crop rectangle: {ex}");
        }

        return full;
    }

    private static string? CaptureViaPortal()
    {
        Process? monitor = null;
        try
        {
            DateTime startTime = DateTime.UtcNow;
            string? foundPath = null;
            using AutoResetEvent waitHandle = new AutoResetEvent(false);
            Regex uriRegex = new Regex("file://([^\\s\"']+)");

            monitor = new Process();
            monitor.StartInfo = new ProcessStartInfo("stdbuf", "-oL busctl --user monitor org.freedesktop.portal.Desktop")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            monitor.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Match m = uriRegex.Match(e.Data);
                    if (m.Success)
                    {
                        foundPath = Uri.UnescapeDataString(m.Groups[1].Value);
                        waitHandle.Set();
                    }
                }
            };
            monitor.Start();
            monitor.BeginOutputReadLine();

            using (Process caller = new Process())
            {
                caller.StartInfo = new ProcessStartInfo("gdbus", "call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop --method org.freedesktop.portal.Screenshot.Screenshot \"\" \"{'interactive': <false>}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                caller.Start();
                caller.WaitForExit(3000);
            }

            waitHandle.WaitOne(2000);

            try { monitor.Kill(); } catch { }
            monitor.Dispose();
            monitor = null;

            if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
            {
                return foundPath;
            }

            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrEmpty(picturesDir) && Directory.Exists(picturesDir))
            {
                var latestFile = Directory.GetFiles(picturesDir, "*.png", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.LastWriteTimeUtc >= startTime.AddSeconds(-2))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestFile != null && latestFile.Length > 0)
                {
                    return latestFile.FullName;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Portal capture exception: {ex}");
        }
        finally
        {
            if (monitor != null)
            {
                try { monitor.Kill(); } catch { }
                monitor.Dispose();
            }
        }

        return null;
    }
}
