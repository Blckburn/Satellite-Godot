using Godot;
using System;
using System.IO;
using System.Globalization;
using System.Linq;

/// <summary>
/// 📁 EPIC FILE LOGGING SYSTEM! 
/// Создает логи в папке logs/ с автоматической ротацией (максимум 50 файлов)
/// Формат файлов: YYYY-MM-DD_HH-mm-ss_run001.log
/// </summary>
public static class FileLogger
{
    private static string _logDirectory = "logs";
    private static string _currentLogFile = null;
    private static bool _isInitialized = false;
    private static readonly object _lock = new object();
    private static int _maxLogFiles = 50;
    
    /// <summary>
    /// Инициализация системы логирования при первом использовании
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;
        
        lock (_lock)
        {
            if (_isInitialized) return;
            
            try
            {
                // Создаем папку logs если её нет
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                    GD.Print($"📁 Created logs directory: {_logDirectory}");
                }
                
                // Создаем новый лог файл для этого запуска
                CreateNewLogFile();
                
                // Очищаем старые логи (оставляем только последние 50)
                CleanupOldLogs();
                
                _isInitialized = true;
                
                // Записываем заголовок лога
                WriteToFile("🚀 ========== SATELLITE GAME LOG SESSION STARTED ==========");
                WriteToFile($"📅 Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteToFile($"🎮 Godot Version: {Engine.GetVersionInfo()}");
                WriteToFile($"💻 Platform: {OS.GetName()}");
                WriteToFile("🚀 =========================================================");
                WriteToFile("");
                
                GD.Print($"✅ FileLogger initialized! Current log: {_currentLogFile}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"❌ Failed to initialize FileLogger: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Создает новый лог файл с уникальным именем
    /// </summary>
    private static void CreateNewLogFile()
    {
        DateTime now = DateTime.Now;
        string dateTime = now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        
        // Ищем следующий доступный номер рана
        int runNumber = 1;
        string fileName;
        do
        {
            fileName = $"{dateTime}_run{runNumber:D3}.log";
            runNumber++;
        }
        while (File.Exists(Path.Combine(_logDirectory, fileName)) && runNumber <= 999);
        
        _currentLogFile = Path.Combine(_logDirectory, fileName);
        
        // Создаем файл если его нет
        if (!File.Exists(_currentLogFile))
        {
            File.Create(_currentLogFile).Close();
        }
    }
    
    /// <summary>
    /// Удаляет старые лог файлы, оставляя только последние _maxLogFiles
    /// </summary>
    private static void CleanupOldLogs()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToArray();
            
            if (logFiles.Length > _maxLogFiles)
            {
                int filesToDelete = logFiles.Length - _maxLogFiles;
                var filesToRemove = logFiles.Skip(_maxLogFiles).Take(filesToDelete);
                
                foreach (var file in filesToRemove)
                {
                    try
                    {
                        file.Delete();
                        GD.Print($"🗑️ Deleted old log: {file.Name}");
                    }
                    catch (Exception e)
                    {
                        GD.PrintErr($"❌ Failed to delete log {file.Name}: {e.Message}");
                    }
                }
                
                GD.Print($"🧹 Cleanup complete! Kept {_maxLogFiles} latest logs, deleted {filesToDelete} old logs.");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"❌ Failed to cleanup old logs: {e.Message}");
        }
    }
    
    /// <summary>
    /// Записывает сообщение в текущий лог файл
    /// </summary>
    public static void WriteToFile(string message)
    {
        if (!_isInitialized)
        {
            Initialize();
        }
        
        if (string.IsNullOrEmpty(_currentLogFile)) return;
        
        lock (_lock)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                string logEntry = $"[{timestamp}] {message}";
                
                File.AppendAllText(_currentLogFile, logEntry + System.Environment.NewLine);
            }
            catch (Exception e)
            {
                GD.PrintErr($"❌ Failed to write to log file: {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Логирует сообщение как в консоль, так и в файл
    /// </summary>
    public static void Log(string message, bool showInConsole = true)
    {
        if (showInConsole)
        {
            GD.Print(message);
        }
        
        WriteToFile(message);
    }
    
    /// <summary>
    /// Логирует ошибку как в консоль, так и в файл
    /// </summary>
    public static void LogError(string message)
    {
        GD.PrintErr(message);
        WriteToFile($"❌ ERROR: {message}");
    }
    
    /// <summary>
    /// Логирует предупреждение как в консоль, так и в файл
    /// </summary>
    public static void LogWarning(string message)
    {
        GD.Print($"⚠️ WARNING: {message}");
        WriteToFile($"⚠️ WARNING: {message}");
    }
    
    /// <summary>
    /// Возвращает путь к текущему лог файлу
    /// </summary>
    public static string GetCurrentLogFile()
    {
        return _currentLogFile ?? "Not initialized";
    }
    
    /// <summary>
    /// Возвращает список всех лог файлов
    /// </summary>
    public static string[] GetAllLogFiles()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                return new string[0];
                
            return Directory.GetFiles(_logDirectory, "*.log")
                .Select(Path.GetFileName)
                .OrderByDescending(f => f)
                .ToArray();
        }
        catch (Exception e)
        {
            GD.PrintErr($"❌ Failed to get log files: {e.Message}");
            return new string[0];
        }
    }
}
