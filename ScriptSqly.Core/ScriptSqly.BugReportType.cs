using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// گزارش خطا و درخواست امکان در سفیر: BugReports.ReportType و Tags و جدولِ عکس‌های پیوستِ BugReportAttachments.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/75-bug-report-type.sql در مخزن Safir یکی بماند
        /// (آزمونِ BugReportTypeTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است و
        /// روی دیتابیسی که BugReports ندارد کاری نمی‌کند.
        /// </summary>
        private static void BugReportTypeScript(SqlConnection db)
        {
            ExecuteBatches(db, BugReportTypeSql);
        }

        private const string BugReportTypeSql = @"/* ═══════════════════════════════════════════════════════════════════
   گزارش خطا و درخواست امکان در سفیر (/bug-report): نوعِ گزارش، برچسب‌های مدیر و عکسِ پیوست.

   • BugReports.ReportType: «Bug» (اشکالِ نرم‌افزاری) یا «Feature» (ساخت، طراحی یا تغییرِ بخشی از نرم‌افزار)؛ گزارش‌های قبلی Bug می‌مانند.
   • BugReports.Tags: برچسب‌های مدیر با «|» از هم جدا (مثلاً «فوری|آموزش کاربر»)؛ فقط مدیر می‌نویسد.
   • BugReportAttachments: تا ۳ عکسِ JPEG/PNG برای هر گزارش (سرور اندازه و نوع را می‌سنجد)؛ با حذفِ گزارش پاک می‌شود.
   • جدولِ BugReports فقط روی دیتابیسِ سرورِ گزارش‌ها هست؛ جایی که نیست، این اسکریپت کاری نمی‌کند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.BugReports', N'ReportType') IS NULL
    ALTER TABLE [dbo].[BugReports] ADD [ReportType] NVARCHAR(30) NOT NULL CONSTRAINT [DF_BugReports_ReportType] DEFAULT (N'Bug') WITH VALUES;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.BugReports', N'Tags') IS NULL
    ALTER TABLE [dbo].[BugReports] ADD [Tags] NVARCHAR(500) NULL;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[BugReportAttachments]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BugReportAttachments] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [BugReportId] INT            NOT NULL,
        [FileName]    NVARCHAR(200)  NULL,
        [ContentType] NVARCHAR(50)   NOT NULL,
        [Data]        VARBINARY(MAX) NOT NULL,
        [CreatedAt]   DATETIME       NOT NULL CONSTRAINT [DF_BugReportAttachments_CreatedAt] DEFAULT (GETDATE()),
        CONSTRAINT [PK_BugReportAttachments] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_BugReportAttachments_BugReports] FOREIGN KEY ([BugReportId]) REFERENCES [dbo].[BugReports] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_BugReportAttachments_BugReportId] ON [dbo].[BugReportAttachments] ([BugReportId]);
END;
GO

PRINT N'گزارش خطا: نوعِ گزارش، برچسب‌ها و جدولِ عکس‌های پیوست آماده شد.';
GO";
    }
}
