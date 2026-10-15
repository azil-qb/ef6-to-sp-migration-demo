using System.Data.Common;
using System.Data.Entity.Infrastructure.Interception;
using System.Text;

namespace EFPerformanceDemo.Utility
{
    /// <summary>
    /// EF6 command interceptor that logs executed SQL (with parameter DECLAREs)
    /// to a file, replaying the behavior of the original EF Core ILogger-based
    /// SqlFileLogger but using EF6's IDbCommandInterceptor pipeline instead.
    /// </summary>
    public class SqlFileLogger : IDbCommandInterceptor
    {
        private readonly string _filePath;
        private static readonly object _lock = new();

        public SqlFileLogger(string filePath) => _filePath = filePath;

        public void ReaderExecuting(DbCommand command, DbCommandInterceptionContext<DbDataReader> interceptionContext)
            => LogCommand(command);

        public void ReaderExecuted(DbCommand command, DbCommandInterceptionContext<DbDataReader> interceptionContext) { }

        public void NonQueryExecuting(DbCommand command, DbCommandInterceptionContext<int> interceptionContext)
            => LogCommand(command);

        public void NonQueryExecuted(DbCommand command, DbCommandInterceptionContext<int> interceptionContext) { }

        public void ScalarExecuting(DbCommand command, DbCommandInterceptionContext<object> interceptionContext)
            => LogCommand(command);

        public void ScalarExecuted(DbCommand command, DbCommandInterceptionContext<object> interceptionContext) { }

        private void LogCommand(DbCommand command)
        {
            if (string.IsNullOrEmpty(command.CommandText))
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"-- Executed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            foreach (DbParameter parameter in command.Parameters)
            {
                var declaredType = parameter.Value switch
                {
                    string => "NVARCHAR(MAX)",
                    bool => "BIT",
                    _ => "INT"
                };

                var declaredValue = parameter.Value switch
                {
                    string s => $"'{s}'",
                    bool b => b ? "1" : "0",
                    null or DBNull => "NULL",
                    _ => parameter.Value.ToString()
                };

                sb.AppendLine($"DECLARE {parameter.ParameterName} {declaredType} = {declaredValue};");
            }

            sb.AppendLine(command.CommandText);
            sb.AppendLine("GO");
            sb.AppendLine();

            lock (_lock)
            {
                File.AppendAllText(_filePath, sb.ToString());
            }
        }
    }
}
