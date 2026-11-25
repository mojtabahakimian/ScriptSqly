using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «از النا بپرسید» در سفیر: IP، مرورگر و شناسه‌ی دستگاهِ کسی که از النا پرسید (BugReportAiAsk.ClientIp، UserAgent و DeviceId).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/91-elena-ask-client.sql در مخزن Safir یکی بماند
        /// (آزمونِ ElenaAskHistoryTests.Client_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد).
        /// idempotent است و روی دیتابیسی که BugReportAiAsk ندارد کاری نمی‌کند.
        /// </summary>
        private static void ElenaAskClientScript(SqlConnection db)
        {
            ExecuteBatches(db, ElenaAskClientSql);
        }

        private const string ElenaAskClientSql = @"/* ═══════════════════════════════════════════════════════════════════
   «از النا بپرسید» — از کجا پرسیده شد (۱۴۰۵/۰۸/۲۸)

   • BugReportAiAsk.ClientIp: نشانیِ IP ِ کسی که پرسید، همان‌طور که سرورِ وب دید (برای اینترنت، IP ِ بیرونیِ او).
   • BugReportAiAsk.UserAgent: مرورگر و سیستم‌عاملِ او (سرآیندِ User-Agent، حداکثر ۳۰۰ نویسه).
   • BugReportAiAsk.DeviceId: شناسه‌ی ثابتِ همان مرورگر (GUID ای که مرورگر یک بار می‌سازد و نگه می‌دارد). کاربرانِ داخلِ
     شرکت همه با IP ِ روتر دیده می‌شوند؛ این شناسه دستگاه‌ها را از هم جدا می‌کند.
   • تا امروز فقط کاربر و صفحه ثبت می‌شد؛ پیدا کردنِ این‌که یک پرسش از کدام دستگاه بود به لاگِ IIS ِ سرور نیاز داشت.
     میزِ کارِ النا («پرسش‌های کاربران» و «گفتگوی کامل») این دو را نشان می‌دهد. پرسش‌های قبلی خالی می‌مانند.
   • فقط روی دیتابیسی که BugReportAiAsk دارد (مهاجرتِ ۸۳). idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[BugReportAiAsk]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.BugReportAiAsk', N'ClientIp') IS NULL
    ALTER TABLE [dbo].[BugReportAiAsk] ADD [ClientIp] NVARCHAR(64) NULL;
GO

IF OBJECT_ID(N'[dbo].[BugReportAiAsk]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.BugReportAiAsk', N'UserAgent') IS NULL
    ALTER TABLE [dbo].[BugReportAiAsk] ADD [UserAgent] NVARCHAR(300) NULL;
GO

IF OBJECT_ID(N'[dbo].[BugReportAiAsk]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.BugReportAiAsk', N'DeviceId') IS NULL
    ALTER TABLE [dbo].[BugReportAiAsk] ADD [DeviceId] NVARCHAR(64) NULL;
GO

PRINT N'«از النا بپرسید»: IP، مرورگر و شناسه‌ی دستگاهِ پرسنده (BugReportAiAsk.ClientIp، UserAgent و DeviceId) از این به بعد ثبت می‌شود.';
GO";
    }
}
