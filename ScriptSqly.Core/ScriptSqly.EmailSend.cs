using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ارسال با ایمیل از داخلِ سفیر: فرمِ EMAIL_SEND (به هیچ‌کس داده نمی‌شود) و جدولِ سوابقِ SAFIR_EMAIL_SENDS.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/92-email-send.sql در مخزن Safir یکی بماند
        /// (آزمونِ EmailSendTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void EmailSendScript(SqlConnection db)
        {
            ExecuteBatches(db, EmailSendSql);
        }

        private const string EmailSendSql = @"/* ═══════════════════════════════════════════════════════════════════
   ارسال با ایمیل از داخلِ سفیر: فاکتور، پیش‌فاکتور و هر PDF ِ دیگر از پنجره‌ی پیش‌نمایش، و صورت‌حساب مشتری؛ از ایمیلِ
   شرکت که در «مشخصات سیستم ← ایمیل» ثبت می‌شود (GENERAL_OPTIONS ِ EMAIL_GATEWAY، رمزشده).

   • فرمِ EMAIL_SEND (فقط سفیر): «اجرا» یا «ورود» = اجازه‌ی ارسال. به هیچ‌کس داده نمی‌شود: ردیفِ هر کاربر با همه‌ی
     مجوزها صفر ساخته می‌شود.
   • SAFIR_EMAIL_SENDS: هر ارسال (موفق یا نه) — کدام کاربر، کی، به کدام نشانی و کدام حساب، چه سندی، نامِ فایل و نتیجه.
     خودِ فایل نگه داشته نمی‌شود.
   هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_EMAIL_SENDS]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_EMAIL_SENDS] (
        [ID]         BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT [PK_SAFIR_EMAIL_SENDS] PRIMARY KEY,
        [CRT]        DATETIME       NOT NULL CONSTRAINT [DF_SAFIR_EMAIL_SENDS_CRT] DEFAULT (GETDATE()),
        [USERCO]     INT            NOT NULL,
        [USER_NAME]  NVARCHAR(100)  NULL,
        [TO_EMAIL]   NVARCHAR(110)  NOT NULL,
        [CUST_NO]    NVARCHAR(30)   NULL,
        [CUST_NAME]  NVARCHAR(200)  NULL,
        [KIND]       NVARCHAR(80)   NULL,
        [DOC_REF]    NVARCHAR(60)   NULL,
        [SUBJECT]    NVARCHAR(200)  NULL,
        [FILE_NAME]  NVARCHAR(200)  NULL,
        [FILE_SIZE]  INT            NULL,
        [OK]         BIT            NOT NULL,
        [ERROR]      NVARCHAR(400)  NULL,
        [IP]         VARCHAR(50)    NULL
    );
    CREATE INDEX [IX_SAFIR_EMAIL_SENDS_CRT] ON [dbo].[SAFIR_EMAIL_SENDS] ([CRT] DESC);
    CREATE INDEX [IX_SAFIR_EMAIL_SENDS_CUST] ON [dbo].[SAFIR_EMAIL_SENDS] ([CUST_NO], [CRT] DESC);
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'EMAIL_SEND')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'SAZMAN'), 0);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'EMAIL_SEND', N'ارسال با ایمیل (فقط سفیر)', 3, @Grp,
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH, 0, 0, 0, 0, 0, GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    WHERE F.FORMNAME = N'EMAIL_SEND'
      AND D.IDD IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'ارسال با ایمیل: فرم EMAIL_SEND و جدول SAFIR_EMAIL_SENDS آماده شد.';
GO";
    }
}
