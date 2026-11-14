using System.Text;

namespace HsServer.Admin;

/// <summary>
/// Admin commands typed into the server's own window (like a Minecraft server console): the same commands as admin.bat,
/// with or without "admin" in front, plus "stop" to shut the server down. The typed command stays on its own "> " line
/// at the bottom: log lines are printed above it instead of through it. Up / Down recall earlier commands.
/// </summary>
internal static class AdminConsole
{
    private const string Prompt = "> ";
    private static readonly object Gate = new();
    private static readonly StringBuilder typed = new();
    private static readonly List<string> history = new();

    /// <summary>Reads the console in the background (nothing when input is redirected, e.g. tests).</summary>
    public static void Start(AdminCommands commands, CancellationTokenSource stop)
    {
        if (Console.IsInputRedirected) return;
        Log.ConsoleLine = AboveThePrompt;
        Log.QuietConsoleDebug = true; // the file still has them
        var thread = new Thread(() => Run(commands, stop)) { IsBackground = true, Name = "admin console" };
        thread.Start();
    }

    private static void Run(AdminCommands commands, CancellationTokenSource stop)
    {
        Log.Info("admin", "type commands in this window: help, players, give default EX1_116, dust default +1000, stop ...");
        Redraw();
        int recall = 0;
        while (!stop.IsCancellationRequested)
        {
            ConsoleKeyInfo key;
            try { key = Console.ReadKey(intercept: true); }
            catch (InvalidOperationException) { return; } // no real console after all
            string line = null;
            lock (Gate)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        line = typed.ToString();
                        typed.Clear();
                        if (line.Trim().Length > 0 && (history.Count == 0 || history[^1] != line)) history.Add(line);
                        recall = history.Count;
                        break;
                    case ConsoleKey.Backspace:
                        if (typed.Length > 0) typed.Length--;
                        break;
                    case ConsoleKey.Escape:
                        typed.Clear();
                        break;
                    case ConsoleKey.UpArrow when history.Count > 0:
                        recall = Math.Max(0, recall - 1);
                        typed.Clear().Append(history[recall]);
                        break;
                    case ConsoleKey.DownArrow when history.Count > 0:
                        recall = Math.Min(history.Count, recall + 1);
                        typed.Clear().Append(recall < history.Count ? history[recall] : "");
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
                        break;
                }
                Redraw();
            }
            if (line == null) continue;

            string[] args = Split(line);
            if (args.Length > 0 && args[0].Equals("admin", StringComparison.OrdinalIgnoreCase)) args = args[1..];
            if (args.Length == 0) continue;
            Log.Print(Prompt + line);
            if (args.Length == 1 && args[0].ToLowerInvariant() is "stop" or "exit" or "quit")
            {
                Log.Info("admin", "stopping the server (the game client stays open)");
                stop.Cancel();
                return;
            }
            AdminCommands.Result result = commands.RunAsync(args).GetAwaiter().GetResult();
            Log.Print(result.Text);
        }
    }

    // A log or output line: clear the prompt line, print the line, draw the prompt (with what is being typed) again.
    private static void AboveThePrompt(string line)
    {
        lock (Gate)
        {
            ClearLine();
            Console.WriteLine(line);
            DrawPrompt();
        }
    }

    private static void Redraw()
    {
        ClearLine();
        DrawPrompt();
    }

    private static void ClearLine()
    {
        int width = Math.Max(1, SafeWidth() - 1);
        Console.Write('\r' + new string(' ', width) + '\r');
    }

    private static void DrawPrompt()
    {
        string text = typed.ToString();
        int room = Math.Max(10, SafeWidth() - Prompt.Length - 2);
        Console.Write(Prompt + (text.Length > room ? "…" + text[^(room - 1)..] : text));
    }

    private static int SafeWidth()
    {
        try { return Console.WindowWidth; }
        catch (IOException) { return 120; }
    }

    /// <summary>Words of a typed line; "double quotes" keep spaces together.</summary>
    internal static string[] Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line ?? "")
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) words.Add(word.ToString());
                word.Clear();
                any = false;
            }
            else { word.Append(c); any = true; }
        }
        if (any) words.Add(word.ToString());
        return words.ToArray();
    }
}
