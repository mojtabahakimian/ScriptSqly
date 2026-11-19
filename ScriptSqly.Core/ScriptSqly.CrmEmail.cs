using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ایمیلِ مشتریِ CRM: ستونِ EMAIL در COPMANES (سفیر: پرونده‌ی مشتری و «ارسال با ایمیل» ِ CRM).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/94-crm-email.sql در مخزن Safir یکی بماند
        /// (آزمونِ EmailSendTests.Crm_email_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void CrmEmailScript(SqlConnection db)
        {
            ExecuteBatches(db, CrmEmailSql);
        }

        private const string CrmEmailSql = @"/* ═══════════════════════════════════════════════════════════════════
   ایمیلِ مشتریِ CRM: ستونِ EMAIL در COPMANES تا نشانی یک بار در پرونده‌ی مشتری ثبت شود و «ارسال با ایمیل» ِ CRM خودش پُرش کند.
   WPF (CRMMAIN) با فهرستِ صریحِ ستون‌ها می‌نویسد، پس ستونِ تازه‌ی nullable اثری بر آن ندارد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[COPMANES]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.COPMANES', N'EMAIL') IS NULL
    ALTER TABLE [dbo].[COPMANES] ADD [EMAIL] NVARCHAR(110) NULL;
GO

PRINT N'ایمیل مشتری CRM آماده شد.';
GO";
    }
}
