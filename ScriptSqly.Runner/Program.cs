using System;
using System.IO;
using ScriptSqly.Migrations;

namespace ScriptSqly.Runner
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("      ScriptSqly Database Migration Engine       ");
            Console.WriteLine("==================================================");

            string? connectionString = null;
            bool isCustomCall = false;
            int type = -1;
            bool previewOnly = false;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if ((arg == "--conn" || arg == "-c") && i + 1 < args.Length)
                {
                    connectionString = args[++i];
                }
                else if (arg.StartsWith("--conn="))
                {
                    connectionString = arg.Substring("--conn=".Length);
                }
                else if (arg == "--custom-call" || arg == "-custom")
                {
                    isCustomCall = true;
                }
                else if ((arg == "--type" || arg == "-t") && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out type);
                }
                else if (arg.StartsWith("--type="))
                {
                    int.TryParse(arg.Substring("--type=".Length), out type);
                }
                else if (arg == "--preview-only" || arg == "--preview")
                {
                    previewOnly = true;
                }
                else if (arg == "--help" || arg == "-h" || arg == "/?")
                {
                    ShowHelp();
                    return 0;
                }
            }

            // Fallback to environment variable if connectionString is missing
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = Environment.GetEnvironmentVariable("SCRIPTSQLY_CONN_STR")
                                   ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Connection string is required.");
                Console.ResetColor();
                Console.WriteLine("Provide via --conn \"...\" or env var SCRIPTSQLY_CONN_STR.");
                Console.WriteLine();
                ShowHelp();
                return 1;
            }

            if (previewOnly)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[PREVIEW MODE] Validating parameters only. No migrations executed.");
                Console.ResetColor();
                Console.WriteLine($"  Connection Target: {MaskConnectionString(connectionString)}");
                Console.WriteLine($"  isCustomCall: {isCustomCall}");
                Console.WriteLine($"  type: {type}");
                Console.WriteLine("Preview validation successful.");
                return 0;
            }

            try
            {
                Console.WriteLine($"Starting migration at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Target Database: {MaskConnectionString(connectionString)}");
                Console.WriteLine($"Parameters: isCustomCall={isCustomCall}, _type_={type}");
                Console.WriteLine("Executing ScriptSqly.RunTracked ...");

                var result = ScriptSqly.Migrations.ScriptSqly.RunTracked(connectionString, isCustomCall, type);
                return ReportMigrationResult(result);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ ERROR: Database migration failed!");
                Console.WriteLine($"Exception: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                Console.ResetColor();
                return 2;
            }
        }

        private static int ReportMigrationResult(MigrationExecutionResult result)
        {
            Console.ForegroundColor = result.Success ? ConsoleColor.Green : ConsoleColor.Red;
            try
            {
                Console.WriteLine(result.Success
                    ? "✅ Database migration completed successfully!"
                    : "❌ ERROR: Database migration failed!");
                Console.WriteLine($"Executed: {result.Executed}; skipped: {result.Skipped}; failed: {result.Errors.Count}");
                foreach (var failure in result.Errors)
                {
                    Console.WriteLine($"Step: {failure.Command}");
                    var error = failure.ErrorNumber > 0 ? $"SQL error {failure.ErrorNumber}" : "Error";
                    Console.WriteLine($"{error}: {failure.Message}");
                }
                return result.Success ? 0 : 2;
            }
            finally
            {
                Console.ResetColor();
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  ScriptSqly.Runner --conn \"<connection_string>\" [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --conn, -c <str>     SQL Server Connection String (Required or via SCRIPTSQLY_CONN_STR)");
            Console.WriteLine("  --custom-call        Set isCustomCall = true");
            Console.WriteLine("  --type, -t <num>     Set _type_ (e.g. 2 for Payroll / CostClose)");
            Console.WriteLine("  --preview-only       Validate parameters and test connection without running migrations");
            Console.WriteLine("  --help, -h           Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  ScriptSqly.Runner --conn \"Server=localhost;Database=YAZDSEPAR1405;Trusted_Connection=True;\" --custom-call");
            Console.WriteLine("  ScriptSqly.Runner -c \"Server=109.125.128.45;Database=SafirTest;User Id=sa;Password=...\" -t 2");
        }

        private static string MaskConnectionString(string connStr)
        {
            try
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
                if (!string.IsNullOrEmpty(builder.Password))
                {
                    builder.Password = "******";
                }
                return $"Server={builder.DataSource}; Database={builder.InitialCatalog}; IntegratedSecurity={builder.IntegratedSecurity}";
            }
            catch
            {
                return "Configured Connection String";
            }
        }
    }
}
