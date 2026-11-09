using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ارسال به واتس‌اپ از داخلِ سفیر: فرمِ WHATSAPP_SEND (به هیچ‌کس داده نمی‌شود) و جدولِ سوابقِ SAFIR_WA_SENDS.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/80-whatsapp-send.sql در مخزن Safir یکی بماند
        /// (آزمونِ WhatsAppSendTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void WhatsAppSendScript(SqlConnection db)
        {
            ExecuteBatches(db, WhatsAppSendSql);
        }

        private const string WhatsAppSendSql = @"/* ═══════════════════════════════════════════════════════════════════
   ارسال به واتس‌اپ از داخلِ سفیر: فاکتور، پیش‌فاکتور و هر PDF ِ دیگر از پنجره‌ی پیش‌نمایش، و پیامِ آزاد از صفحه‌ی /whatsapp.

   • فرمِ WHATSAPP_SEND (فقط سفیر): «اجرا» یا «ورود» = اجازه‌ی ارسال؛ «دیدن» = دیدنِ سوابقِ همه‌ی کاربران (بی آن هر کس فقط
     ارسال‌های خودش را می‌بیند). به هیچ‌کس داده نمی‌شود: ردیفِ هر کاربر با همه‌ی مجوزها صفر ساخته می‌شود.
   • SAFIR_WA_SENDS: هر ارسال (موفق یا نه) — کدام کاربر، کی، به کدام شماره و کدام حساب، چه سندی، نامِ فایل و نتیجه.
     خودِ فایل نگه داشته نمی‌شود.
   هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_WA_SENDS]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_WA_SENDS] (
        [ID]         BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT [PK_SAFIR_WA_SENDS] PRIMARY KEY,
        [CRT]        DATETIME       NOT NULL CONSTRAINT [DF_SAFIR_WA_SENDS_CRT] DEFAULT (GETDATE()),
        [USERCO]     INT            NOT NULL,
        [USER_NAME]  NVARCHAR(100)  NULL,
        [MOBILE]     VARCHAR(20)    NOT NULL,
        [CUST_NO]    NVARCHAR(30)   NULL,
        [CUST_NAME]  NVARCHAR(200)  NULL,
        [KIND]       NVARCHAR(80)   NULL,
        [DOC_REF]    NVARCHAR(60)   NULL,
        [CAPTION]    NVARCHAR(1000) NULL,
        [FILE_NAME]  NVARCHAR(200)  NULL,
        [FILE_SIZE]  INT            NULL,
        [OK]         BIT            NOT NULL,
        [MESSAGE_ID] NVARCHAR(200)  NULL,
        [ERROR]      NVARCHAR(400)  NULL,
        [IP]         VARCHAR(50)    NULL
    );
    CREATE INDEX [IX_SAFIR_WA_SENDS_CRT] ON [dbo].[SAFIR_WA_SENDS] ([CRT] DESC);
    CREATE INDEX [IX_SAFIR_WA_SENDS_CUST] ON [dbo].[SAFIR_WA_SENDS] ([CUST_NO], [CRT] DESC);
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'WHATSAPP_SEND')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'SAZMAN'), 0);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'WHATSAPP_SEND', N'ارسال به واتس‌اپ (فقط سفیر)', 3, @Grp,
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
    WHERE F.FORMNAME = N'WHATSAPP_SEND'
      AND D.IDD IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'ارسال به واتس‌اپ: فرمِ WHATSAPP_SEND و جدولِ SAFIR_WA_SENDS آماده شد.';
GO";
    }
}
