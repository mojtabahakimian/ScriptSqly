using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        private static void ExecuteBatchesTransactional(SqlConnection db, string script)

        {
            using var transaction = db.BeginTransaction();
            try
            {
                ExecuteBatches(db, script, transaction);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); }
                catch (InvalidOperationException) { /* SQL Server may already have rolled back after XACT_ABORT. */ }
                throw;
            }
        }
        private static void ExecuteBatches(SqlConnection db, string script, SqlTransaction? transaction = null)
        {
            // Safely split the script ONLY when "GO" is on its own line
            var commands = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var cmdText in commands)
            {
                if (!string.IsNullOrWhiteSpace(cmdText))
                {
                    try
                    {
                        string test = cmdText;
                        db.Execute(cmdText, transaction: transaction);
                    }
                    catch (SqlException ex)
                    {
                        // Logging the exact error and query batch that failed so you can actually debug it
                        Console.WriteLine($"SQL Execution Error:\n{ex.Message}\nFailed Batch:\n{cmdText}\n");
                        // If a critical procedure fails to create, you might want to throw the error here
                        throw;
                    }
                }
            }
        }
        /// <summary>
        /// ماژول «بستن ماه بهای تمام‌شده» (پیشوند CC_).
        ///
        /// هر ده اسکریپت Server/Database/10-schema.sql تا
        /// 19-margin-fix-kalas.sql عیناً اینجا کپی شده‌اند، به همان ترتیب
        /// وابستگی: اول جدول‌های پایه، بعد داده اولیه، بعد رویه‌ها. پس
        /// اجرای این فایل روی یک پایگاه تازه هم کامل بالا می‌آید و
        /// پیش‌نیاز دستی ندارد.
        ///
        /// همه بلوک‌ها idempotent هستند (CREATE OR ALTER برای رویه‌ها،
        /// IF NOT EXISTS برای جدول‌ها و داده اولیه) چون این فایل هر بار
        /// دوباره روی همان پایگاه اجرا می‌شود.
        ///
        /// اگر متن یکی از بلوک‌ها را عوض کردید، همان تغییر را در فایل
        /// .sql متناظرش هم بگذارید؛ این دو باید مو‌به‌مو یکی بمانند.
        /// </summary>
        /// <summary>
        /// ماژول «بستن ماه بهای تمام‌شده» (پیشوند CC_).
        ///
        /// هر ده اسکریپت Server/Database/10-schema.sql تا
        /// 19-margin-fix-kalas.sql عیناً اینجا کپی شده‌اند، به همان ترتیب
        /// وابستگی: اول جدول‌های پایه، بعد داده اولیه، بعد رویه‌ها. پس
        /// اجرای این فایل روی یک پایگاه تازه هم کامل بالا می‌آید و
        /// پیش‌نیاز دستی ندارد.
        ///
        /// همه بلوک‌ها idempotent هستند (CREATE OR ALTER برای رویه‌ها،
        /// IF NOT EXISTS برای جدول‌ها و داده اولیه) چون این فایل هر بار
        /// دوباره روی همان پایگاه اجرا می‌شود.
        ///
        /// اگر متن یکی از بلوک‌ها را عوض کردید، همان تغییر را در فایل
        /// .sql متناظرش هم بگذارید؛ این دو باید مو‌به‌مو یکی بمانند.
        /// </summary>
        private static void LoadJobData(SqlConnection db)
        {
            const string JobFilePath = @"C:\CORRECT\joby.sql";

            if (!File.Exists(JobFilePath))
            {
                Console.WriteLine($"[LoadJobData] فایل پیدا نشد: {JobFilePath}");
                return;
            }

            // ── بررسی وجود داده قبلی ─────────────────────────────
            var existingCount = db.ExecuteScalar<int>("SELECT COUNT(*) FROM [dbo].[PAY2_JOB]");
            if (existingCount > 0)
            {
                Console.WriteLine($"[LoadJobData] PAY2_JOB از قبل {existingCount} رکورد دارد — رد شد.");
                return;
            }

            Console.WriteLine("[LoadJobData] در حال خواندن joby.sql ...");

            // فایل UTF-16LE است
            string[] lines = File.ReadAllLines(JobFilePath, System.Text.Encoding.Unicode);

            // ── Parse سطرهای INSERT با Regex ─────────────────────
            // نمونه: INSERT [dbo].[PAY2_JOB] ([JOB_ID],...) VALUES (1, N'1', N'2', N'3', 1)
            var insertRx = new Regex(
                @"VALUES\s*\(\s*(\d+)\s*,\s*N'((?:[^']|'')*)'\s*,\s*N'((?:[^']|'')*)'\s*,\s*(?:N'((?:[^']|'')*)'|NULL)\s*,\s*(\d+)\s*\)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var table = new DataTable();
            table.Columns.Add("JOB_ID", typeof(int));
            table.Columns.Add("JOB_CODE", typeof(string));
            table.Columns.Add("JOB_NAME", typeof(string));
            table.Columns.Add("JOB_GROUP", typeof(string));
            table.Columns.Add("IS_ACTIVE", typeof(bool));

            int parsed = 0;
            foreach (string line in lines)
            {
                var m = insertRx.Match(line);
                if (!m.Success) continue;

                table.Rows.Add(
                    int.Parse(m.Groups[1].Value),           // JOB_ID
                    m.Groups[2].Value.Replace("''", "'"),   // JOB_CODE
                    m.Groups[3].Value.Replace("''", "'"),   // JOB_NAME
                    m.Groups[4].Success && m.Groups[4].Value.Length > 0
                        ? (object)m.Groups[4].Value.Replace("''", "'")
                        : DBNull.Value,                     // JOB_GROUP (nullable)
                    m.Groups[5].Value == "1"                // IS_ACTIVE
                );
                parsed++;
            }

            if (parsed == 0)
            {
                Console.WriteLine("[LoadJobData] هیچ سطر INSERT ای parse نشد.");
                return;
            }

            Console.WriteLine($"[LoadJobData] {parsed} رکورد parse شد — در حال BulkCopy ...");

            // ── SqlBulkCopy ────────────────
            using var tx = db.BeginTransaction();
            try
            {
                using var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.KeepIdentity, tx)
                {
                    DestinationTableName = "[dbo].[PAY2_JOB]",
                    BatchSize = 1000,
                    BulkCopyTimeout = 600
                };
                bulk.ColumnMappings.Add("JOB_ID", "JOB_ID");
                bulk.ColumnMappings.Add("JOB_CODE", "JOB_CODE");
                bulk.ColumnMappings.Add("JOB_NAME", "JOB_NAME");
                bulk.ColumnMappings.Add("JOB_GROUP", "JOB_GROUP");
                bulk.ColumnMappings.Add("IS_ACTIVE", "IS_ACTIVE");

                bulk.WriteToServer(table);
                tx.Commit();
                Console.WriteLine($"[LoadJobData] {parsed} رکورد با موفقیت در PAY2_JOB درج شد.");
            }
            catch (Exception ex)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    Console.WriteLine($"[LoadJobData] خطا در Rollback: {rollbackEx.Message}");
                }

                Console.WriteLine($"[LoadJobData] خطا در BulkCopy: {ex.Message}");
                throw;
            }
        }
    }
}
