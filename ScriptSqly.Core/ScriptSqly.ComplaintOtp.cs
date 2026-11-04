using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// تأییدِ موبایلِ فرمِ عمومیِ شکایت در سفیر: جدولِ کدهای پیامکیِ SAFIR_COMPLAINT_OTP.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/72-complaint-otp.sql در مخزن Safir یکی بماند
        /// (آزمونِ ComplaintOtpTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void ComplaintOtpScript(SqlConnection db)
        {
            ExecuteBatches(db, ComplaintOtpSql);
        }

        private const string ComplaintOtpSql = @"/* ═══════════════════════════════════════════════════════════════════
   تأییدِ موبایلِ فرمِ عمومیِ شکایت: پیش از ثبت، کدِ پیامکی به همان موبایل فرستاده و در انتهای فرم پرسیده می‌شود.

   • SAFIR_COMPLAINT_OTP مثلِ SAFIR_PORTAL_OTP است: کد فقط به‌صورتِ HMAC، زمان‌ها UTC، هر کد ۳ دقیقه و ۵ بار امتحان.
   • COMPLAINT_ID شکایتی است که با این کد ثبت شد؛ شکایتی که ردیفی این‌جا ندارد «موبایلِ تأییدنشده» است.
   • فقط Safir می‌خواند و می‌نویسد؛ CustomerComplaints دست نمی‌خورد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_COMPLAINT_OTP]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_COMPLAINT_OTP] (
        [ID]           BIGINT IDENTITY(1,1) NOT NULL,
        [MOBILE]       NVARCHAR(20)  NOT NULL,
        [CODE_HASH]    NVARCHAR(200) NOT NULL,
        [EXPIRES]      DATETIME      NOT NULL,
        [ATTEMPTS]     INT           NOT NULL CONSTRAINT [DF_SAFIR_COMPLAINT_OTP_ATTEMPTS] DEFAULT (0),
        [USED]         BIT           NOT NULL CONSTRAINT [DF_SAFIR_COMPLAINT_OTP_USED] DEFAULT (0),
        [COMPLAINT_ID] INT           NULL,
        [IP]           NVARCHAR(64)  NULL,
        [CRT]          DATETIME      NOT NULL CONSTRAINT [DF_SAFIR_COMPLAINT_OTP_CRT] DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_SAFIR_COMPLAINT_OTP] PRIMARY KEY CLUSTERED ([ID])
    );
    CREATE NONCLUSTERED INDEX [IX_SAFIR_COMPLAINT_OTP_MOBILE] ON [dbo].[SAFIR_COMPLAINT_OTP] ([MOBILE], [CRT]);
    CREATE NONCLUSTERED INDEX [IX_SAFIR_COMPLAINT_OTP_COMPLAINT] ON [dbo].[SAFIR_COMPLAINT_OTP] ([COMPLAINT_ID]) WHERE [COMPLAINT_ID] IS NOT NULL;
END;
GO

PRINT N'تأییدِ موبایلِ شکایت: جدولِ SAFIR_COMPLAINT_OTP آماده شد.';
GO";
    }
}
