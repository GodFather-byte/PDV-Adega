using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PDV_Adega;

public static class SupportDiagnostics
{
    private const long MaximumLogBytes = 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PDV-Adega",
        "support-diagnostics.log");

    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                LogException("AppDomain", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("Unobserved task", args.Exception);
        };

        if (Application.Current is not null)
            Application.Current.DispatcherUnhandledException += (_, args) =>
            {
                LogException("WPF dispatcher", args.Exception);
                args.Handled = false;
            };
    }

    public static void LogException(string source, Exception exception)
    {
        var details = $"{source}: {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";
        Append(details);
    }

    public static byte[] CreatePackage(string deviceId)
    {
        var appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var log = File.Exists(LogPath) ? File.ReadAllText(LogPath) : "No application errors have been recorded.";
        if (Encoding.UTF8.GetByteCount(log) > MaximumLogBytes)
            log = log[^((int)MaximumLogBytes / 2)..];

        var manifest = string.Join(
            Environment.NewLine,
            "PDV-Adega diagnostic information",
            $"Application version: {appVersion}",
            $"Windows version: {Environment.OSVersion.Version}",
            $"Computer name: {Environment.MachineName}",
            $"Installation ID: {deviceId}",
            $"Generated at (UTC): {DateTimeOffset.UtcNow:O}",
            "Included data: application error log and technical environment information.",
            "Excluded data: products, stock, sales, customers, cash sessions, printer configuration and license key.");

        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "diagnostics.txt", manifest);
            WriteEntry(archive, "application-errors.log", log);
        }

        return memory.ToArray();
    }

    private static void Append(string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaximumLogBytes)
                {
                    var rotatedPath = $"{LogPath}.1";
                    if (File.Exists(rotatedPath)) File.Delete(rotatedPath);
                    File.Move(LogPath, rotatedPath);
                }

                var line = $"{DateTimeOffset.UtcNow:O} {message.ReplaceLineEndings(" | ")}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                Trace.WriteLine("PDV-Adega could not write the support diagnostics log because of an I/O error.");
            }
            catch (UnauthorizedAccessException)
            {
                Trace.WriteLine("PDV-Adega could not write the support diagnostics log due to file permissions.");
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }
}
