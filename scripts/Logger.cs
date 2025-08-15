using Godot;
using System.Collections.Generic;

public static class Logger
{
    public static bool EnableDebugLogs = true;
    public static bool EnableFileLogging = true;
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

        // Логируем в консоль
        GD.Print(message);
        
        // Логируем в файл если включено
        if (EnableFileLogging)
        {
            FileLogger.WriteToFile($"DEBUG: {message}");
        }
    }

    public static void Error(string message)
    {
        // Логируем в консоль
        GD.PushError(message);
        
        // Логируем в файл если включено
        if (EnableFileLogging)
        {
            FileLogger.WriteToFile($"ERROR: {message}");
        }
    }
    
    public static void Warning(string message)
    {
        // Логируем в консоль
        GD.Print($"⚠️ WARNING: {message}");
        
        // Логируем в файл если включено
        if (EnableFileLogging)
        {
            FileLogger.WriteToFile($"WARNING: {message}");
        }
    }
    
    public static void Info(string message)
    {
        // Логируем в консоль
        GD.Print($"ℹ️ INFO: {message}");
        
        // Логируем в файл если включено
        if (EnableFileLogging)
        {
            FileLogger.WriteToFile($"INFO: {message}");
        }
    }

    public static void ClearOnceOnlyLogs()
    {
        _loggedMessages.Clear();
    }
    
    public static void InitializeFileLogging()
    {
        if (EnableFileLogging)
        {
            FileLogger.Initialize();
            Info("📁 File logging system initialized!");
        }
    }
    
    public static string GetCurrentLogFile()
    {
        return FileLogger.GetCurrentLogFile();
    }
}