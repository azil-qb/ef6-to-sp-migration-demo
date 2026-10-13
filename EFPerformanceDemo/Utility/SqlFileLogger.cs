using System.Text;
using System.Text.RegularExpressions;
using EFPerformanceDemo.Utility;

namespace EFPerformanceDemo.Utility
{
    public class SqlFileLogger : ILogger
    {
        private readonly string _filePath;
        private static readonly object _lock = new();

        public SqlFileLogger(string filePath) => _filePath = filePath;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Event ID 20101 = CommandExecuted
            if (eventId.Id == 20101 && state is IReadOnlyList<KeyValuePair<string, object>> payload)
            {
                string commandText = string.Empty;
                var parameters = new List<string>();

                foreach (var kvp in payload)
                {
                    if (kvp.Key == "commandText")
                    {
                        commandText = kvp.Value?.ToString() ?? string.Empty;
                    }
                    else if (kvp.Key == "parameters" && kvp.Value is string paramsString && !string.IsNullOrWhiteSpace(paramsString))
                    {
                        // Extracts parameter string like "@__categoryId_0='1' (Nullable = false)"
                        var paramMatches = Regex.Matches(paramsString, @"(@\w+)=('([^']*)'|[^,\s]+)");
                        foreach (Match match in paramMatches)
                        {
                            var paramName = match.Groups[1].Value;
                            var paramValue = match.Groups[2].Value;

                            // Determines target SQL type (numeric vs string/bit)
                            if (paramValue.StartsWith("'") && paramValue.EndsWith("'"))
                            {
                                parameters.Add($"DECLARE {paramName} NVARCHAR(MAX) = {paramValue};");
                            }
                            else if (bool.TryParse(paramValue, out bool boolVal))
                            {
                                parameters.Add($"DECLARE {paramName} BIT = {(boolVal ? 1 : 0)};");
                            }
                            else
                            {
                                parameters.Add($"DECLARE {paramName} INT = {paramValue};");
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(commandText))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"-- Executed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                    foreach (var param in parameters)
                    {
                        sb.AppendLine(param);
                    }

                    sb.AppendLine(commandText);
                    sb.AppendLine("GO");
                    sb.AppendLine();

                    lock (_lock)
                    {
                        File.AppendAllText(_filePath, sb.ToString());
                    }
                }
            }
        }
    }

    public class SqlFileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        public SqlFileLoggerProvider(string filePath) => _filePath = filePath;
        public ILogger CreateLogger(string categoryName) => new SqlFileLogger(_filePath);
        public void Dispose() { }
    }
}
