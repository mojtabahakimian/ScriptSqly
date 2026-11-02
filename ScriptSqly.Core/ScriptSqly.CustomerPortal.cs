using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// پرتال مشتریِ سفیر: جدول‌های SAFIR_PORTAL_CUST / _OTP / _LOG / _CONFIG و فرمِ دسترسیِ CUSTOMER_PORTAL.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/67-customer-portal.sql در مخزن Safir یکی بماند
        /// (آزمونِ CustomerPortalAdminTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void CustomerPortalScript(SqlConnection db)
        {
            ExecuteBatches(db, CustomerPortalSql);
        }

        private const string CustomerPortalSql = @"/* ═══════════════════════════════════════════════════════════════════
   پرتال مشتری: مشتری با کدِ یک‌بارمصرفِ پیامکی وارد می‌شود و فقط صورت‌حساب، پیش‌فاکتورها و چک‌های خودش را
   می‌بیند و سفارش می‌دهد (سفارش = پیش‌فاکتور که به کارتابلِ کارشناسِ فروشِ همان مشتری ارجاع می‌شود).

   • SAFIR_PORTAL_CUST  — مشتریانی که مدیر برایشان پرتال را باز کرده؛ نبودِ ردیف یعنی ورود ممکن نیست.
                          SEC_VER با هر بستن یا عوض شدنِ موبایل عوض می‌شود تا توکن‌های قبلی همان لحظه باطل شوند.
   • SAFIR_PORTAL_OTP   — کدهای یک‌بارمصرف؛ فقط هش (HMAC با کلیدِ سرور)، هرگز خودِ کد.
   • SAFIR_PORTAL_LOG   — ورود، درخواستِ کد، سفارش و تغییراتِ مدیر؛ برای پیگیری و جلوگیری از سوءاستفاده.
   • SAFIR_PORTAL_CONFIG — تنظیماتِ پرتالِ همین شرکت (کلید/مقدار): نامِ لینک، عنوان، کارشناسِ پیش‌فرض، …
   • فرمِ CUSTOMER_PORTAL در TFORMS (صفحه‌ی «پرتال مشتری» در سفیر): SEE دیدن، UPD فعال/غیرفعال و تنظیمات،
     INP ارسالِ پیامکِ دعوت. ردیفِ هر کاربر یک‌بار با سطحِ «مدیریت دسترسی‌های حقوق و دستمزد» ساخته می‌شود.
   هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_PORTAL_CUST]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_PORTAL_CUST] (
        [HES]         NVARCHAR(100) NOT NULL,
        [MOBILE]      NVARCHAR(20)  NOT NULL,
        [ENABLED]     BIT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_CUST_ENABLED] DEFAULT (1),
        [CAN_ORDER]   BIT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_CUST_CAN_ORDER] DEFAULT (1),
        [EXPERT_IDD]  INT           NULL,
        [NOTE]        NVARCHAR(300) NULL,
        [SEC_VER]     INT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_CUST_SEC_VER] DEFAULT (1),
        [FAILS]       INT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_CUST_FAILS] DEFAULT (0),
        [LOCK_UNTIL]  DATETIME      NULL,
        [LAST_LOGIN]  DATETIME      NULL,
        [LOGINS]      INT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_CUST_LOGINS] DEFAULT (0),
        [INVITED]     DATETIME      NULL,
        [CRT]         DATETIME      NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_CUST_CRT] DEFAULT (GETDATE()),
        [UID]         INT           NULL,
        [UPD_AT]      DATETIME      NULL,
        [UPD_BY]      INT           NULL,
        CONSTRAINT [PK_SAFIR_PORTAL_CUST] PRIMARY KEY CLUSTERED ([HES])
    );
END;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_PORTAL_CUST]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAFIR_PORTAL_CUST_MOBILE' AND object_id = OBJECT_ID(N'[dbo].[SAFIR_PORTAL_CUST]'))
    CREATE NONCLUSTERED INDEX [IX_SAFIR_PORTAL_CUST_MOBILE] ON [dbo].[SAFIR_PORTAL_CUST] ([MOBILE]);
GO

IF OBJECT_ID(N'[dbo].[SAFIR_PORTAL_OTP]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_PORTAL_OTP] (
        [ID]        BIGINT        IDENTITY(1,1) NOT NULL,
        [MOBILE]    NVARCHAR(20)  NOT NULL,
        [CODE_HASH] NVARCHAR(200) NOT NULL,
        [EXPIRES]   DATETIME      NOT NULL,
        [ATTEMPTS]  INT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_OTP_ATTEMPTS] DEFAULT (0),
        [USED]      BIT           NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_OTP_USED] DEFAULT (0),
        [IP]        NVARCHAR(64)  NULL,
        [CRT]       DATETIME      NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_OTP_CRT] DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_SAFIR_PORTAL_OTP] PRIMARY KEY CLUSTERED ([ID])
    );
    CREATE NONCLUSTERED INDEX [IX_SAFIR_PORTAL_OTP_MOBILE] ON [dbo].[SAFIR_PORTAL_OTP] ([MOBILE], [CRT]);
END;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_PORTAL_LOG]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_PORTAL_LOG] (
        [ID]     BIGINT        IDENTITY(1,1) NOT NULL,
        [HES]    NVARCHAR(100) NULL,
        [MOBILE] NVARCHAR(20)  NULL,
        [EVENT]  NVARCHAR(40)  NOT NULL,
        [DETAIL] NVARCHAR(400) NULL,
        [IP]     NVARCHAR(64)  NULL,
        [USERCO] INT           NULL,
        [CRT]    DATETIME      NOT NULL CONSTRAINT [DF_SAFIR_PORTAL_LOG_CRT] DEFAULT (GETDATE()),
        CONSTRAINT [PK_SAFIR_PORTAL_LOG] PRIMARY KEY CLUSTERED ([ID])
    );
    CREATE NONCLUSTERED INDEX [IX_SAFIR_PORTAL_LOG_HES] ON [dbo].[SAFIR_PORTAL_LOG] ([HES], [CRT]);
END;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_PORTAL_CONFIG]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_PORTAL_CONFIG] (
        [K]      NVARCHAR(50)   NOT NULL,
        [V]      NVARCHAR(MAX)  NULL,
        [UPD_AT] DATETIME       NULL CONSTRAINT [DF_SAFIR_PORTAL_CONFIG_UPD_AT] DEFAULT (GETDATE()),
        [UPD_BY] INT            NULL,
        CONSTRAINT [PK_SAFIR_PORTAL_CONFIG] PRIMARY KEY CLUSTERED ([K])
    );
END;
GO

/* ── فرمِ دسترسیِ صفحه‌ی «پرتال مشتری»، در همان گروهِ «تعیین سطح دسترسی» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'CUSTOMER_PORTAL')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS'), 0);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'CUSTOMER_PORTAL', N'پرتال مشتری', 3, @Grp,
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminId INT = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'PAY2_ADMIN_ACL');

    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH,
           ISNULL(P.RUN, 0), ISNULL(P.SEE, 0), ISNULL(P.INP, 0), ISNULL(P.UPD, 0), ISNULL(P.DEL, 0),
           GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    OUTER APPLY (SELECT TOP 1 S.RUN, S.SEE, S.INP, S.UPD, S.DEL
                 FROM [dbo].[SAL_CHEK] S
                 WHERE S.USERCO = D.IDD AND S.[OBJECT] = @AdminId) P
    WHERE F.FORMNAME = N'CUSTOMER_PORTAL'
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'پرتال مشتری: جدول‌های SAFIR_PORTAL_* و فرمِ CUSTOMER_PORTAL آماده شد.';
GO";
    }
}
