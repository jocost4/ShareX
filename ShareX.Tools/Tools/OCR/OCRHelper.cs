using ShareX.HelpersLib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bitmap = SkiaSharp.SKBitmap;
using ImageFormat = SkiaSharp.SKEncodedImageFormat;

namespace ShareX.Tools;

public static class OCRHelper
{
    private const string SupportedVersion = "10.0.18362.0";
    private static bool? _isTesseractInstalled;
    private static OCRLanguageOption[]? _availableLanguages;

    private static readonly Dictionary<string, string> LanguageCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "en", "eng" },
        { "pt", "por" },
        { "es", "spa" },
        { "fr", "fra" },
        { "de", "deu" },
        { "it", "ita" },
        { "ja", "jpn" },
        { "ru", "rus" },
        { "zh", "chi_sim" },
        { "zh-Hans", "chi_sim" },
        { "zh-Hant", "chi_tra" },
        { "nl", "nld" },
        { "pl", "pol" },
        { "tr", "tur" },
        { "ar", "ara" },
        { "ko", "kor" }
    };

    public static bool IsTesseractInstalled()
    {
        if (_isTesseractInstalled.HasValue)
        {
            return _isTesseractInstalled.Value;
        }

        try
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            string exeName = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
            foreach (string dir in paths)
            {
                if (File.Exists(Path.Combine(dir, exeName)))
                {
                    _isTesseractInstalled = true;
                    return true;
                }
            }
        }
        catch
        {
        }

        _isTesseractInstalled = false;
        return false;
    }

    public static bool IsSupported => IsTesseractInstalled() || OperatingSystem.IsWindows();

    public static OCRLanguageOption[] AvailableLanguages
    {
        get
        {
            if (_availableLanguages != null && _availableLanguages.Length > 0)
            {
                return _availableLanguages;
            }

            _availableLanguages = LoadAvailableLanguages();
            return _availableLanguages;
        }
    }

    private static OCRLanguageOption[] LoadAvailableLanguages()
    {
        if (!IsTesseractInstalled())
        {
            return [new OCRLanguageOption("English (eng)", "eng")];
        }

        try
        {
            using Process proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            proc.StartInfo.ArgumentList.Add("--list-langs");
            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            List<OCRLanguageOption> list = new();
            string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("List of", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string displayName = $"{GetLanguageDisplayName(line)} ({line})";
                list.Add(new OCRLanguageOption(displayName, line));
            }

            if (list.Count > 0)
            {
                return list.ToArray();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to list tesseract languages: {ex}");
        }

        return [new OCRLanguageOption("English (eng)", "eng")];
    }

    private static string GetLanguageDisplayName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "eng" => "English",
            "osd" => "Orientation and Script Detection",
            "por" => "Portuguese",
            "spa" => "Spanish",
            "fra" => "French",
            "deu" => "German",
            "ita" => "Italian",
            "jpn" => "Japanese",
            "chi_sim" => "Chinese (Simplified)",
            "chi_tra" => "Chinese (Traditional)",
            "rus" => "Russian",
            "nld" => "Dutch",
            "pol" => "Polish",
            "tur" => "Turkish",
            "ara" => "Arabic",
            "kor" => "Korean",
            _ => code
        };
    }

    public static void ThrowIfNotSupported()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("OCR requires Tesseract OCR on Linux (install via 'sudo apt install tesseract-ocr') or Windows UWP OCR engine.");
        }
    }

    public static async Task<string> OCR(Bitmap bitmap, string languageTag = "en", float scaleFactor = 1f, bool singleLine = false)
    {
        ThrowIfNotSupported();

        if (bitmap == null)
        {
            return string.Empty;
        }

        string tempImg = Path.Combine(Path.GetTempPath(), $"sharex_ocr_{Guid.NewGuid():N}.png");
        string tempOutBase = Path.Combine(Path.GetTempPath(), $"sharex_ocr_{Guid.NewGuid():N}");
        string tempOutTxt = tempOutBase + ".txt";

        try
        {
            Bitmap toSave = bitmap;
            Bitmap? scaled = null;
            if (scaleFactor > 0 && Math.Abs(scaleFactor - 1f) > 0.01f)
            {
                int targetWidth = Math.Max(1, (int)(bitmap.Width * scaleFactor));
                int targetHeight = Math.Max(1, (int)(bitmap.Height * scaleFactor));
                scaled = new Bitmap(targetWidth, targetHeight);
                using (SKCanvas canvas = new SKCanvas(scaled))
                {
                    canvas.DrawBitmap(bitmap, new SKRect(0, 0, targetWidth, targetHeight));
                }
                toSave = scaled;
            }

            using (scaled)
            {
                using (FileStream fs = File.Create(tempImg))
                {
                    toSave.Encode(fs, ImageFormat.Png, 100);
                }
            }

            string lang = languageTag;
            if (LanguageCodeMap.TryGetValue(languageTag, out string? mapped))
            {
                lang = mapped;
            }

            using Process process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            process.StartInfo.ArgumentList.Add(tempImg);
            process.StartInfo.ArgumentList.Add(tempOutBase);
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add(lang);
            if (singleLine)
            {
                process.StartInfo.ArgumentList.Add("--psm");
                process.StartInfo.ArgumentList.Add("7");
            }
            process.StartInfo.ArgumentList.Add("quiet");

            process.Start();
            await process.WaitForExitAsync();

            if (File.Exists(tempOutTxt))
            {
                string text = await File.ReadAllTextAsync(tempOutTxt);
                if (singleLine)
                {
                    return text.Replace("\r", "").Replace("\n", " ").Trim();
                }
                return text.Trim();
            }

            return string.Empty;
        }
        finally
        {
            try
            {
                if (File.Exists(tempImg))
                {
                    File.Delete(tempImg);
                }
            }
            catch { }

            try
            {
                if (File.Exists(tempOutTxt))
                {
                    File.Delete(tempOutTxt);
                }
            }
            catch { }
        }
    }
}
