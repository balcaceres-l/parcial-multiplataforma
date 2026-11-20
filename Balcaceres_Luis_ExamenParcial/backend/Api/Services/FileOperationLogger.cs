namespace Api.Services;

public interface IFileOperationLogger
{
    Task LogOperationAsync(string requestId, string category, string message);
    Task<string[]> ReadLogsAsync(int maxLines = 200);
    string LogFilePath { get; }
}

public class FileOperationLogger : IFileOperationLogger
{
    private readonly ILogger<FileOperationLogger> _logger;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    public string LogFilePath { get; }

    public FileOperationLogger(ILogger<FileOperationLogger> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        var logsDir = Path.Combine(env.ContentRootPath, "Logs");
        Directory.CreateDirectory(logsDir);
        LogFilePath = Path.Combine(logsDir, "operaciones.txt");
    }

    public async Task LogOperationAsync(string requestId, string category, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [X-Request-Id: {requestId}] [{category}] {message}";

        _logger.LogInformation("{LogLine}", line);

        await _semaphore.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(LogFilePath, line + Environment.NewLine);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string[]> ReadLogsAsync(int maxLines = 200)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(LogFilePath))
                return [];

            var lines = await File.ReadAllLinesAsync(LogFilePath);
            return lines.Reverse().Take(maxLines).ToArray();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
