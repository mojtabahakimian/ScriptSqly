using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// دستیار هوشمند — گزارش‌های ذخیره‌شده (AI_SavedReport): گزارشی که دستیار با پرسش از کاربر ساخت و کاربر گفت ذخیره شود.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/81-ai-saved-report.sql در مخزن Safir یکی بماند
        /// (آزمونِ AiSavedReportTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AiSavedReportScript(SqlConnection db)
        {
            ExecuteBatches(db, AiSavedReportSql);
        }

        private const string AiSavedReportSql = @"/* ═══════════════════════════════════════════════════════════════════
   دستیار هوشمند — «گزارشِ ذخیره‌شده»: گزارشی که ابزارِ ثابت نداشت، دستیار با پرسش از کاربر ساخت
   و کاربر صریحاً گفت ذخیره شود. دفعه‌ی بعد دستیار همین را پیدا و اجرا می‌کند، نه اینکه از صفر بگردد.

   • فقط کاربرانِ دارای «کوئری آزاد» (AI_UserAccess.AllowRawSql) آن را می‌سازند و اجرا می‌کنند.
   • SqlText فقط یک SELECT است با پارامترهای @From، @To، @AsOf (تاریخِ yyyymmdd) و @Code؛
     اجرا با همان کاربرِ فقط‌خواندنیِ run_sql.
   • حذف = IsActive صفر؛ ردیف پاک نمی‌شود.
   فقط سفیر از این جدول استفاده می‌کند؛ هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[AI_SavedReport]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AI_SavedReport] (
        [Id]            INT IDENTITY(1, 1) NOT NULL CONSTRAINT [PK_AI_SavedReport] PRIMARY KEY,
        [Name]          NVARCHAR(150)  NOT NULL,
        [Definition]    NVARCHAR(2000) NOT NULL,
        [Keywords]      NVARCHAR(400)  NULL,
        [SqlText]       NVARCHAR(MAX)  NOT NULL,
        [Params]        NVARCHAR(100)  NULL,
        [CreatedByCo]   INT            NOT NULL,
        [CreatedByName] NVARCHAR(100)  NULL,
        [CreatedAtUtc]  DATETIME2      NOT NULL CONSTRAINT [DF_AI_SavedReport_Created] DEFAULT (SYSUTCDATETIME()),
        [UpdatedAtUtc]  DATETIME2      NULL,
        [RunCount]      INT            NOT NULL CONSTRAINT [DF_AI_SavedReport_RunCount] DEFAULT (0),
        [LastRunUtc]    DATETIME2      NULL,
        [IsActive]      BIT            NOT NULL CONSTRAINT [DF_AI_SavedReport_Active] DEFAULT (1)
    );
    CREATE UNIQUE INDEX [UX_AI_SavedReport_Name] ON [dbo].[AI_SavedReport] ([Name]) WHERE [IsActive] = 1;
END;
GO

PRINT N'دستیار هوشمند: جدولِ AI_SavedReport (گزارش‌های ذخیره‌شده) آماده شد.';
GO";
    }
}
