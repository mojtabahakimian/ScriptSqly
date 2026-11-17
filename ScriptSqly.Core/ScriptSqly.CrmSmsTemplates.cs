using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// پیامک‌های آماده CRM در سفیر: کلید CRMSMS (چه کسی پیام‌ها را تنظیم می‌کند) و جدول CRM_SMS_TEMPLATE که کاربر CRM فقط از
        /// آن انتخاب می‌کند؛ سرور متن دلخواه نمی‌پذیرد.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/90-crm-sms-templates.sql در مخزن Safir یکی بماند
        /// (آزمون CrmSmsTemplateTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void CrmSmsTemplatesScript(SqlConnection db)
        {
            ExecuteBatches(db, CrmSmsTemplatesSql);
        }

        private const string CrmSmsTemplatesSql = @"/* ═══════════════════════════════════════════════════════════════════
   پیامک‌های آماده CRM (فقط سفیر، دکمه «پیامک» در پرونده مشتری و صفحه /crm/sms-templates)

   ── چرا ──
   کاربر CRM نباید هر متنی که خواست برای مشتری بفرستد. مدیر متن‌ها را تعریف می‌کند و کاربر فقط یکی را انتخاب می‌کند؛
   سرور متن را از همین جدول می‌سازد و شماره موبایل را از پرونده مشتری (COPMANES) برمی‌دارد.

   ── این اسکریپت ──
   ۱) TFORMS: کلید CRMSMS («تنظیم پیامک‌های آماده CRM»)؛ فرم واقعی نیست، کلید روشن/خاموش هر کاربر است (مثل CRMALL) و
      فقط ستون RUN آن معنا دارد. ردیف هر کاربر بسته ساخته می‌شود، جز کسانی که «تعیین سطح دسترسی» (F_USER_PERMITION FORMS)
      را دارند تا بتوانند به دیگران بدهند. ردیف موجود دست نمی‌خورد.
   ۲) CRM_SMS_TEMPLATE: عنوان، متن (با {نام} به جای نام مشتری)، فعال/غیرفعال و ترتیب. وقتی تازه ساخته می‌شود شش پیام
      نمونه می‌گیرد؛ اجرای دوباره چیزی اضافه نمی‌کند.

   نرم‌افزار ویندوزی این‌ها را نمی‌شناسد. idempotent است. نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────── ۱) کلید CRMSMS و مجوزها ───────── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'CRMSMS')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'CRMALL'), 16);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'CRMSMS', N'تنظیم پیامک‌های آماده CRM', 3, @Grp,
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @Form INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'CRMSMS');
    DECLARE @Admin INT = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS');
    IF @Form IS NOT NULL
        INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
        SELECT D.IDD, @Form, ISNULL(a.Full_, 0), ISNULL(a.Full_, 0), ISNULL(a.Full_, 0), ISNULL(a.Full_, 0), ISNULL(a.Full_, 0), GETDATE()
        FROM [dbo].[SALA_DTL] D
        OUTER APPLY (SELECT TOP 1 CAST(1 AS bit) Full_ FROM [dbo].[SAL_CHEK] s
                     WHERE s.USERCO = D.IDD AND s.[OBJECT] = @Admin AND s.RUN = 1) a
        WHERE D.IDD IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM [dbo].[SAL_CHEK] E WHERE E.USERCO = D.IDD AND E.[OBJECT] = @Form);
END
GO

/* ───────── ۲) پیام‌های آماده ───────── */
IF OBJECT_ID(N'[dbo].[CRM_SMS_TEMPLATE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CRM_SMS_TEMPLATE](
        [ID]      INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CRM_SMS_TEMPLATE] PRIMARY KEY,
        [TITLE]   NVARCHAR(40)      NOT NULL,
        [BODY]    NVARCHAR(600)     NOT NULL,   -- {نام} = مدیر یا نام مشتری
        [ACTIVE]  BIT               NOT NULL CONSTRAINT [DF_CRM_SMS_TEMPLATE_ACTIVE] DEFAULT (1),
        [SORT]    INT               NOT NULL CONSTRAINT [DF_CRM_SMS_TEMPLATE_SORT] DEFAULT (0),
        [UID]     INT               NULL,
        [CRT]     DATETIME          NOT NULL CONSTRAINT [DF_CRM_SMS_TEMPLATE_CRT] DEFAULT (GETDATE()),
        [UPD_UID] INT               NULL,
        [UPD]     DATETIME          NULL
    );

    INSERT INTO [dbo].[CRM_SMS_TEMPLATE] (TITLE, BODY, SORT) VALUES
        (N'پیگیری',            N'سلام {نام} عزیز، جهت پیگیری درخواست شما تماس گرفته شد.', 1),
        (N'پاسخ نداد',         N'سلام {نام} عزیز، با شما تماس گرفتیم ولی موفق به صحبت نشدیم. لطفا در اولین فرصت با ما تماس بگیرید.', 2),
        (N'یادآوری جلسه',      N'سلام {نام} عزیز، یادآوری می‌کنیم جلسه ما با شما طبق هماهنگی برگزار می‌شود. منتظر دیدارتان هستیم.', 3),
        (N'ارسال نمونه',       N'سلام {نام} عزیز، نمونه‌های درخواستی برای شما ارسال شد. خوشحال می‌شویم نظرتان را بدانیم.', 4),
        (N'ارسال پیش‌فاکتور',  N'سلام {نام} عزیز، پیش‌فاکتور درخواستی برای شما ارسال شد. برای هر پرسشی در خدمت هستیم.', 5),
        (N'تشکر',              N'سلام {نام} عزیز، از وقتی که برای گفتگو با ما گذاشتید سپاسگزاریم.', 6);
END
GO

PRINT N'پیامک‌های آماده CRM: کلید CRMSMS و جدول CRM_SMS_TEMPLATE آماده شد.';
GO";
    }
}
