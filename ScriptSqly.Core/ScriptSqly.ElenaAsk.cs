using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «از النا بپرسید» در سفیر: جدولِ BugReportAiAsk (پرسش‌های مستقیمِ کاربران از النا و جوابِ او).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/83-elena-ask.sql در مخزن Safir یکی بماند
        /// (آزمونِ SupportAgentTests.Ask_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است و
        /// روی دیتابیسی که BugReports ندارد کاری نمی‌کند.
        /// </summary>
        private static void ElenaAskScript(SqlConnection db)
        {
            ExecuteBatches(db, ElenaAskSql);
        }

        private const string ElenaAskSql = @"/* ═══════════════════════════════════════════════════════════════════
   «از النا بپرسید» — پرسشِ مستقیمِ کاربر از النا (آیکونِ النا در نوارِ بالای همه‌ی صفحه‌ها)

   • BugReportAiAsk: هر پرسش و جوابِ النا: چه کسی، در کدام صفحه، چه پرسید، النا چه گفت و به کدام آموزش‌ها ارجاع داد،
     و تعدادِ تماس با مدل. سقفِ روزانه‌ی النا (BugReportAiConfig.DailyCallLimit) این‌ها را هم می‌شمارد و میزِ کارِ النا
     پرسش‌هایی را که آموزشی برایشان نبود نشان می‌دهد. جوابِ هر پرسش فقط برای خودِ پرسنده با صدا خوانده می‌شود.
   • مثلِ مهاجرتِ ۸۲ فقط روی دیتابیسی که BugReports دارد (همان‌جا که تنظیماتِ النا هست). idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[BugReportAiAsk]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BugReportAiAsk] (
        [Id]         INT IDENTITY(1,1) NOT NULL,
        [UserIdd]    INT            NOT NULL,
        [UserName]   NVARCHAR(100)  NULL,
        [Page]       NVARCHAR(200)  NULL,
        [Question]   NVARCHAR(1000) NOT NULL,
        [Answer]     NVARCHAR(MAX)  NULL,
        [Topics]     NVARCHAR(400)  NULL,
        [Report]     BIT            NOT NULL CONSTRAINT [DF_BugReportAiAsk_Report] DEFAULT (0),
        [ModelCalls] INT            NOT NULL CONSTRAINT [DF_BugReportAiAsk_ModelCalls] DEFAULT (0),
        [DurationMs] INT            NOT NULL CONSTRAINT [DF_BugReportAiAsk_DurationMs] DEFAULT (0),
        [Error]      NVARCHAR(1000) NULL,
        [CreatedAt]  DATETIME       NOT NULL CONSTRAINT [DF_BugReportAiAsk_CreatedAt] DEFAULT (GETDATE()),
        CONSTRAINT [PK_BugReportAiAsk] PRIMARY KEY CLUSTERED ([Id])
    );
    CREATE NONCLUSTERED INDEX [IX_BugReportAiAsk_CreatedAt] ON [dbo].[BugReportAiAsk] ([CreatedAt]) INCLUDE ([ModelCalls], [UserIdd]);
END;
GO

PRINT N'«از النا بپرسید»: جدولِ پرسش‌ها (BugReportAiAsk) آماده شد؛ آیکونِ النا در نوارِ بالا وقتی النا روشن است دیده می‌شود.';
GO";
    }
}
