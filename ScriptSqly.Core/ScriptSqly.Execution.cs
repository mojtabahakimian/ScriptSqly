using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations;

public sealed record MigrationStep(int Number, string Command, string State, long DurationMs);
public sealed record MigrationFailure(string Command, int ErrorNumber, string Message);
public sealed class MigrationExecutionResult
{
    public int Executed { get; internal set; }
    public int Skipped { get; internal set; }
    public List<MigrationFailure> Errors { get; } = new();
    public bool Success => Errors.Count == 0;
}

public static partial class ScriptSqly
{
    private sealed class ExecutionContext
    {
        public readonly MigrationExecutionResult Result = new();
        public Action<MigrationStep>? Progress;
        public int Number;
    }
    private static readonly AsyncLocal<ExecutionContext?> CurrentExecution = new();

    // The lock is owned by a SQL session, so separate web/runner processes cannot
    // update the same database simultaneously. It is released even on process death.
    public static MigrationExecutionResult RunTracked(string connectionString, bool includeBaseData,
        int type, Action<MigrationStep>? progress = null)
        => Track(connectionString, _ => LetsGo(connectionString, includeBaseData, type), progress);

    public static MigrationExecutionResult RunSqlTracked(string connectionString, string script,
        Action<MigrationStep>? progress = null)
        => Track(connectionString, db => ExecuteBatchesTransactional(db, script), progress);

    private static MigrationExecutionResult Track(string connectionString, Action<SqlConnection> action,
        Action<MigrationStep>? progress)
    {
        using var gateConnection = new SqlConnection(connectionString);
        gateConnection.Open();
        var acquired = gateConnection.ExecuteScalar<int>(@"
DECLARE @result int;
EXEC @result=sys.sp_getapplock @Resource=N'Safir.ScriptSqly.Upgrade',
 @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0;
SELECT @result;");
        if (acquired < 0)
            throw new InvalidOperationException("به‌روزرسانی دیگری روی همین دیتابیس در حال اجراست.");
        var previous = CurrentExecution.Value;
        var context = new ExecutionContext { Progress = progress };
        CurrentExecution.Value = context;
        try
        {
            action(gateConnection);
            return context.Result;
        }
        finally
        {
            CurrentExecution.Value = previous;
            if (gateConnection.State == ConnectionState.Open)
                gateConnection.Execute("EXEC sys.sp_releaseapplock @Resource=N'Safir.ScriptSqly.Upgrade', @LockOwner='Session';");
        }
    }

    private static readonly Regex Definition = new(@"\A\s*(?:(?:--[^\r\n]*(?:\r?\n|$))|(?:/\*.*?\*/\s*))*\s*(?:CREATE(?:\s+OR\s+ALTER)?|ALTER)\s+(PROC(?:EDURE)?|FUNCTION|VIEW)\s+((?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+))?)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static bool EquivalentDefinition(string existing, string desired)
    {
        string Normalize(string sql) => Definition.Replace(sql.Trim(), m =>
            "MODULE " + m.Groups[1].Value.ToUpperInvariant().Replace("PROCEDURE", "PROC") + " " +
            m.Groups[2].Value.Replace("[", "").Replace("]", "").ToUpperInvariant(), 1);
        // Do not collapse whitespace inside SQL string literals.
        return Normalize(existing).Equals(Normalize(desired), StringComparison.Ordinal);
    }

    private static string Label(string sql)
    {
        var definition = Definition.Match(sql);
        if (definition.Success) return definition.Groups[1].Value + " " + definition.Groups[2].Value;
        var first = sql.Trim().Split('\n').FirstOrDefault(x => !x.TrimStart().StartsWith("--") && x.Trim().Length > 0) ?? "SQL";
        return first.Trim()[..Math.Min(first.Trim().Length, 140)];
    }

    // Every Dapper Execute in the migration engine passes through this method.
    // Legacy callers keep their original behavior; tracked runs collect even the
    // exceptions that old catch blocks swallow, and report per-command progress.
    private static int ExecuteMigration(SqlConnection db, string sql, object? param = null,
        IDbTransaction? transaction = null, int? commandTimeout = null, CommandType? commandType = null)
    {
        var context = CurrentExecution.Value;
        if (context is null)
            return db.Execute(sql, param, transaction, commandTimeout, commandType);
        var number = ++context.Number;
        var label = Label(sql);
        var timer = Stopwatch.StartNew();
        context.Progress?.Invoke(new(number, label, "running", 0));
        try
        {
            var definition = Definition.Match(sql);
            if (definition.Success)
            {
                var objectName = definition.Groups[2].Value.Replace("[", "").Replace("]", "");
                var current = db.ExecuteScalar<string?>("SELECT OBJECT_DEFINITION(OBJECT_ID(@name))",
                    new { name = objectName }, transaction, commandTimeout ?? 30);
                if (current is not null && EquivalentDefinition(current, sql))
                {
                    context.Result.Skipped++;
                    context.Progress?.Invoke(new(number, label, "skipped", timer.ElapsedMilliseconds));
                    return 0;
                }
            }

            // A repeated single-column ADD needs no schema lock or expected exception.
            var add = Regex.Match(sql, @"\A\s*ALTER\s+TABLE\s+(?<table>[\w.\[\]]+)\s+ADD\s+(?<column>[\w\[\]]+)\s+", RegexOptions.IgnoreCase);
            if (add.Success && !add.Groups["column"].Value.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                var exists = db.ExecuteScalar<int>("SELECT CASE WHEN COL_LENGTH(@table,@column) IS NULL THEN 0 ELSE 1 END",
                    new { table = add.Groups["table"].Value.Replace("[", "").Replace("]", ""), column = add.Groups["column"].Value.Trim('[', ']') }, transaction);
                // Only skip a single ADD statement: a multi-statement batch may also add missing columns.
                if (exists == 1 && Regex.Matches(sql, @"\bALTER\s+TABLE\b", RegexOptions.IgnoreCase).Count == 1
                    && !sql.Trim().TrimEnd(';').Contains(';') && !Regex.IsMatch(sql, @"\b(?:UPDATE|INSERT|DELETE|EXEC)\b", RegexOptions.IgnoreCase))
                {
                    context.Result.Skipped++;
                    context.Progress?.Invoke(new(number, label, "skipped", timer.ElapsedMilliseconds));
                    return 0;
                }
            }
            var result = db.Execute(sql, param, transaction, commandTimeout, commandType);
            context.Result.Executed++;
            context.Progress?.Invoke(new(number, label, "done", timer.ElapsedMilliseconds));
            return result;
        }
        catch (Exception ex)
        {
            context.Result.Errors.Add(new(label, ex is SqlException se ? se.Number : 0, ex.Message));
            context.Progress?.Invoke(new(number, label, "failed", timer.ElapsedMilliseconds));
            throw;
        }
    }
}
