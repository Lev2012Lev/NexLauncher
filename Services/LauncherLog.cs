using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NexLauncher.Services;

public sealed class LauncherLog
{
    private readonly object _gate = new();
    public string FilePath { get; }

    public LauncherLog(string dataDirectory)
    {
        FilePath = Path.Combine(dataDirectory, "logs", "launcher.log");
    }

    public static string Sanitize(string text)
    {
        text = Regex.Replace(text, @"(?i)(Bearer\s+)[^\s""']+", "$1[hidden]");
        text = Regex.Replace(text, @"(?i)((?:access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization)\s*[""']?\s*[:=]\s*[""']?)[^\s,""'}]+", "$1[hidden]");
        return Regex.Replace(text, @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", "[hidden]");
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Keep one previous log so a long game session does not grow the file forever.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 4 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {Sanitize(message)}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
