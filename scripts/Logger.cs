using Godot;
using System.Collections.Generic;

public static class Logger
{
    public static bool EnableDebugLogs = true;
    private static readonly Dictionary<string, bool> _loggedMessages = new Dictionary<string, bool>();

    public static void Debug(string message, bool onceOnly = false)
    {
        if (!EnableDebugLogs) return;

        if (onceOnly)
        {
            string key = message;
            if (_loggedMessages.ContainsKey(key))
                return;

            _loggedMessages[key] = true;
        }

        GD.Print(message);
    }

    public static void Error(string message)
    {
        GD.PushError(message);
    }

    public static void ClearOnceOnlyLogs()
    {
        _loggedMessages.Clear();
    }
}