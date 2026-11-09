using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// گزارش خطا در سفیر: BugReports.Software («کدام نرم‌افزار»: Safir / Wpf / Access).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/79-bug-report-software.sql در مخزن Safir یکی بماند
        /// (آزمونِ BugReportHintsTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است و
        /// روی دیتابیسی که BugReports ندارد کاری نمی‌کند.
        /// </summary>
        private static void BugReportSoftwareScript(SqlConnection db)
        {
            ExecuteBatches(db, BugReportSoftwareSql);
        }

        private const string BugReportSoftwareSql = @"/* ═══════════════════════════════════════════════════════════════════
   گزارش خطا (/bug-report): «مربوط به کدام نرم‌افزار؟»

   • BugReports.Software: «Safir» (سفیرِ تحتِ وب)، «Wpf» (مسترکارکتِ ویندوزی) یا «Access» (نسخه‌ی تحتِ Access)؛
     گزارش‌های قبلی خالی می‌مانند. گزارش‌های این سه نرم‌افزار قاطی بودند و کاربرها با «سفیر :» در متن جدایشان می‌کردند.
   • جدولِ BugReports فقط روی دیتابیسِ سرورِ گزارش‌ها هست؛ جایی که نیست، این اسکریپت کاری نمی‌کند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.BugReports', N'Software') IS NULL
    ALTER TABLE [dbo].[BugReports] ADD [Software] NVARCHAR(20) NULL;
GO

PRINT N'گزارش خطا: ستونِ «کدام نرم‌افزار» آماده شد.';
GO";
    }
}
