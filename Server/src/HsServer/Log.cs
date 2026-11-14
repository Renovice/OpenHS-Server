namespace HsServer;

/// <summary>Timestamped console log (optionally mirrored to a file); one line per event so runs can be grepped.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter file;
    public static bool Verbose { get; set; }

    /// <summary>Where console lines go instead of Console.WriteLine (the admin console's prompt keeps the typed command
    /// below the log); null = the console directly.</summary>
    public static Action<string> ConsoleLine { get; set; }

    /// <summary>Debug lines only in the file, not in the window (the admin console keeps the window readable).</summary>
    public static bool QuietConsoleDebug { get; set; }

    /// <summary>Also write every line to <paramref name="path"/> (flushed per line, readable while running).</summary>
    public static void ToFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (Gate) file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    }

    public static void Info(string area, string message) => Write("INF", area, message);
    public static void Warn(string area, string message) => Write("WRN", area, message);
    public static void Error(string area, string message) => Write("ERR", area, message);
    public static void Debug(string area, string message) { if (Verbose) Write("DBG", area, message); }

    private static void Write(string level, string area, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level} [{area}] {message}";
        lock (Gate)
        {
            if (!(QuietConsoleDebug && level == "DBG"))
            {
                if (ConsoleLine is { } toConsole) toConsole(line);
                else Console.WriteLine(line);
            }
            file?.WriteLine(line);
        }
    }

    /// <summary>A line for the window only (command output), through the same path as the log.</summary>
    public static void Print(string text)
    {
        lock (Gate)
        {
            foreach (string line in text.Replace("\r", "").Split('\n'))
                if (ConsoleLine is { } toConsole) toConsole(line);
                else Console.WriteLine(line);
        }
    }
}
