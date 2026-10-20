using System.Data;
using System.Data.Common;
using System.Data.Entity.Infrastructure.Interception;
using System.Globalization;
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
                string sqlType = GetSqlType(parameter);
                string literal = GetSqlLiteral(parameter.Value, sqlType);
                // Ensure parameter name starts with @ for DECLARE
                var paramName = parameter.ParameterName;
                if (!string.IsNullOrEmpty(paramName) && !paramName.StartsWith("@"))
                    paramName = "@" + paramName;

                sb.AppendLine($"DECLARE {paramName} {sqlType} = {literal};");
            }

            sb.AppendLine(command.CommandText);
            sb.AppendLine("GO");
            sb.AppendLine();

            lock (_lock)
            {
                File.AppendAllText(_filePath, sb.ToString());
            }
        }

        private static string GetSqlType(DbParameter parameter)
        {
            // Prefer DbType if available
            var dbType = parameter.DbType;
            switch (dbType)
            {
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                    // If Size provided and reasonable, use it; otherwise VARCHAR(MAX)
                    if (parameter.Size > 0 && parameter.Size <= 8000)
                        return $"VARCHAR({parameter.Size})";
                    return "VARCHAR(MAX)";

                case DbType.String:
                case DbType.StringFixedLength:
                    if (parameter.Size > 0 && parameter.Size <= 4000)
                        return $"NVARCHAR({parameter.Size})";
                    return "NVARCHAR(MAX)";

                case DbType.Guid:
                    return "UNIQUEIDENTIFIER";

                case DbType.Binary:
                    return "VARBINARY(MAX)";

                case DbType.Boolean:
                    return "BIT";

                case DbType.Byte:
                    return "TINYINT";

                case DbType.Int16:
                    return "SMALLINT";

                case DbType.Int32:
                    return "INT";

                case DbType.Int64:
                    return "BIGINT";

                case DbType.Currency:
                    return "MONEY";

                case DbType.Decimal:
                    // Try to use Precision/Scale if provided; fall back to DECIMAL(18,2)
                    byte precision = 0;
                    byte scale = 0;
                    try
                    {
                        precision = parameter.Precision;
                        scale = parameter.Scale;
                    }
                    catch { /* ignore if not supported */ }

                    if (precision > 0)
                    {
                        if (scale == 0)
                            scale = 2;
                        return $"DECIMAL({precision},{scale})";
                    }
                    return "DECIMAL(18,2)";

                case DbType.Double:
                    return "FLOAT";

                case DbType.Single:
                    return "REAL";

                case DbType.Date:
                case DbType.DateTime:
                case DbType.DateTime2:
                    return "DATETIME2";

                case DbType.DateTimeOffset:
                    return "DATETIMEOFFSET";

                case DbType.Time:
                    return "TIME";

                case DbType.Xml:
                    return "XML";

                case DbType.Object:
                default:
                    // Fallback to NVARCHAR(MAX)
                    return "NVARCHAR(MAX)";
            }
        }

        private static string GetSqlLiteral(object value, string sqlType)
        {
            if (value == null || value == DBNull.Value)
                return "NULL";

            switch (value)
            {
                case string s:
                    // Escape single quotes by doubling
                    var escaped = s.Replace("'", "''");
                    // Use N prefix for unicode types (NVARCHAR)
                    var prefix = sqlType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase) ? "N" : "";
                    return $"{prefix}'{escaped}'";

                case Guid g:
                    return $"'{g}'";

                case bool b:
                    return b ? "1" : "0";

                case byte[] bytes:
                    // Convert to 0xABCDEF hex literal
                    var hex = BitConverter.ToString(bytes).Replace("-", "");
                    return $"0x{hex}";

                case DateTime dt:
                    // Use ISO-like format compatible with SQL Server
                    return $"'{dt.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture)}'";

                case DateTimeOffset dto:
                    return $"'{dto.ToString("yyyy-MM-dd HH:mm:ss.ffff zzz", CultureInfo.InvariantCulture)}'";

                case decimal dec:
                    return dec.ToString(CultureInfo.InvariantCulture);

                case float f:
                    return f.ToString(CultureInfo.InvariantCulture);

                case double d:
                    return d.ToString(CultureInfo.InvariantCulture);

                case IFormattable formattable:
                    // covers integers and other numerics
                    return formattable.ToString(null, CultureInfo.InvariantCulture);

                default:
                    // Fallback to quoted string representation
                    var str = value.ToString()?.Replace("'", "''");
                    return $"'{str}'";
            }
        }
    }
}