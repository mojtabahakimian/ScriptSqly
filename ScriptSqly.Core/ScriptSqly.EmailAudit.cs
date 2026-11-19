using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// سابقه‌ی محکمِ «ارسال با ایمیل» (ستون‌های تازه‌ی SAFIR_EMAIL_SENDS) و جدولِ SAFIR_MORINFO_LOG برای تغییرِ «اطلاعات بیشتر» ِ مشتری.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/93-email-audit.sql در مخزن Safir یکی بماند
        /// (آزمونِ EmailSendTests.Audit_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void EmailAuditScript(SqlConnection db)
        {
            ExecuteBatches(db, EmailAuditSql);
        }

        private const string EmailAuditSql = @"/* ═══════════════════════════════════════════════════════════════════
   سابقه‌ی محکمِ «ارسال با ایمیل» و تغییرِ ایمیلِ مشتری.

   • SAFIR_EMAIL_SENDS (مهاجرتِ ۹۲) چند ستون می‌گیرد: نشانیِ فرستنده، ایمیلِ ثبت‌شده‌ی مشتری در لحظه‌ی ارسال، اینکه نشانی از
     کارت مشتری آمد یا دستی نوشته شد، متنِ کاملِ ایمیل، اثرِ SHA-256 ِ فایلِ پیوست (برای اثباتِ اینکه چه فایلی رفت)، مرورگرِ
     کاربر و اینکه نشانی در کارت مشتری ثبت شد یا نه.
   • SAFIR_MORINFO_LOG: هر ذخیره‌ی «اطلاعات بیشتر» ِ مشتری (MORINFO) از سفیر — کدام کاربر، کی، از کجا، ایمیلِ قبل و بعد و
     فهرستِ تغییرها. ایمیلِ مشتری مقصدِ ارسال است و عوض کردنش باید ردِ کاربر را داشته باشد.
   • فرمِ EMAIL_SEND_DIRECT (فقط سفیر): «اجرا» = ارسالِ ایمیل بی پیش‌نمایش (بی رسیدِ پیش‌نمایش) پذیرفته می‌شود. به هیچ‌کس داده
     نمی‌شود: ردیفِ هر کاربر با همه‌ی مجوزها صفر ساخته می‌شود.
   هیچ جدولِ WPF دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_EMAIL_SENDS]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'FROM_EMAIL') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [FROM_EMAIL] NVARCHAR(110) NULL;
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'CUST_EMAIL') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [CUST_EMAIL] NVARCHAR(110) NULL;
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'TO_SOURCE') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [TO_SOURCE] TINYINT NULL;
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'BODY') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [BODY] NVARCHAR(4000) NULL;
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'FILE_SHA256') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [FILE_SHA256] CHAR(64) NULL;
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'USER_AGENT') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [USER_AGENT] NVARCHAR(300) NULL;
    IF COL_LENGTH(N'dbo.SAFIR_EMAIL_SENDS', N'SAVED_TO_CUST') IS NULL
        ALTER TABLE [dbo].[SAFIR_EMAIL_SENDS] ADD [SAVED_TO_CUST] BIT NULL;
END;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_MORINFO_LOG]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_MORINFO_LOG] (
        [ID]         BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT [PK_SAFIR_MORINFO_LOG] PRIMARY KEY,
        [CRT]        DATETIME       NOT NULL CONSTRAINT [DF_SAFIR_MORINFO_LOG_CRT] DEFAULT (GETDATE()),
        [USERCO]     INT            NOT NULL,
        [USER_NAME]  NVARCHAR(100)  NULL,
        [HES]        NVARCHAR(50)   NOT NULL,
        [CUST_NAME]  NVARCHAR(200)  NULL,
        [ACTION]     NVARCHAR(20)   NOT NULL,
        [SOURCE]     NVARCHAR(40)   NULL,
        [OLD_EMAIL]  NVARCHAR(110)  NULL,
        [NEW_EMAIL]  NVARCHAR(110)  NULL,
        [CHANGES]    NVARCHAR(4000) NULL,
        [IP]         VARCHAR(50)    NULL,
        [USER_AGENT] NVARCHAR(300)  NULL
    );
    CREATE INDEX [IX_SAFIR_MORINFO_LOG_HES] ON [dbo].[SAFIR_MORINFO_LOG] ([HES], [CRT] DESC);
    CREATE INDEX [IX_SAFIR_MORINFO_LOG_CRT] ON [dbo].[SAFIR_MORINFO_LOG] ([CRT] DESC);
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'EMAIL_SEND_DIRECT')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'SAZMAN'), 0);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'EMAIL_SEND_DIRECT', N'ارسال ایمیل بی پیش‌نمایش (فقط سفیر)', 3, @Grp,
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
    WHERE F.FORMNAME = N'EMAIL_SEND_DIRECT'
      AND D.IDD IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'سابقه ارسال ایمیل و تغییر ایمیل مشتری آماده شد.';
GO";
    }
}
