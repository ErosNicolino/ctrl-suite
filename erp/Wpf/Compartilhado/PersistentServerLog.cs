namespace GraficaCaixa;

public sealed class PersistentServerLog
{
    readonly object gate = new();
    readonly string path;
    readonly long maximumBytes;
    readonly int retainedFiles;

    public PersistentServerLog(string directory, long maximumBytes = 2 * 1024 * 1024, int retainedFiles = 5)
    {
        Directory.CreateDirectory(directory);
        path = System.IO.Path.Combine(directory, "servidor.log");
        this.maximumBytes = maximumBytes;
        this.retainedFiles = Math.Max(1, retainedFiles);
    }

    public string Path => path;

    public void Write(string eventName, string message)
    {
        var safeEvent = SingleLine(eventName);
        var safeMessage = SingleLine(message);
        lock (gate)
        {
            RotateIfNeeded();
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{safeEvent}] {safeMessage}{Environment.NewLine}");
        }
    }

    void RotateIfNeeded()
    {
        if (!File.Exists(path) || new FileInfo(path).Length < maximumBytes) return;
        var oldest = path + "." + retainedFiles;
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = retainedFiles - 1; index >= 1; index--)
        {
            var source = path + "." + index;
            if (File.Exists(source)) File.Move(source, path + "." + (index + 1), true);
        }
        File.Move(path, path + ".1", true);
    }

    static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
