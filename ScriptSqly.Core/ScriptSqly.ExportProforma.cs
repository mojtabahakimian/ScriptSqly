using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «پیش‌فاکتور صادراتی» در سفیر: فرمِ HEAD_LST_PISHFROOSH_SADERATI با دسترسیِ جدا، اطلاعاتِ صادراتیِ هر پیش‌فاکتور
        /// (PROFORMA_EXPORT) و ترجمه‌های دائمی برای چاپِ انگلیسی و عربی (EXPORT_COMPANY، EXPORT_BANK، EXPORT_ITEM، EXPORT_PARTY،
        /// EXPORT_UNIT).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/87-export-proforma.sql در مخزن Safir یکی بماند
        /// (آزمونِ ExportProformaTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void ExportProformaScript(SqlConnection db)
        {
            ExecuteBatches(db, ExportProformaSql);
        }

        private const string ExportProformaSql = @"/* ═══════════════════════════════════════════════════════════════════
   پیش‌فاکتورِ صادراتی (فقط سفیر، صفحه‌ی /export-proforma)

   ── داده‌ی پیش‌فاکتور ──
   همان HEAD_LST / INVO_LST ِ TAG 20 با SADER = 1؛ نرخ و نوعِ ارز روی سربرگ (ARZD، ARZKIND2، ARZCODING) و فیِ ارزیِ هر
   سطر در N_TAF — همان ستون‌های فاکتورِ فروشِ صادراتی، پس جدولِ تازه‌ای برای خودِ پیش‌فاکتور لازم نیست. تبدیل به حواله‌ی
   صادراتی (SADER = 1) با همان N_TAF می‌رود و «فاکتور فروش صادراتی» قیمت را از آن برمی‌دارد.

   ── این اسکریپت ──
   ۱) TFORMS: فرمِ HEAD_LST_PISHFROOSH_SADERATI («پیش فاکتور صادراتی») با دسترسیِ جدا. ردیفِ هر کاربر صفر ساخته می‌شود،
      جز کسانی که «تعیین سطح دسترسی» (F_USER_PERMITION FORMS) را دارند — آن‌ها همه‌ی مجوزها را می‌گیرند تا بتوانند به
      دیگران بدهند. ردیفِ موجود دست نمی‌خورد.
   ۲) PROFORMA_EXPORT: اطلاعاتِ صادراتیِ هر پیش‌فاکتور (Incoterms، شرطِ پرداخت، اعتبار، بندرها، بسته‌بندی، بانکِ دریافت).
   ۳) ترجمه‌های دائمی برای چاپِ انگلیسی و عربی: EXPORT_COMPANY (شرکت، یک ردیف)، EXPORT_BANK (حساب‌های دریافتِ ارز)،
      EXPORT_ITEM (نام، شرح، کدِ HS، کشورِ سازنده و وزن‌های کالا)، EXPORT_PARTY (نام و نشانیِ مشتری) و EXPORT_UNIT (واحدها).
      همه بی کلیدِ خارجی به جدول‌های قدیمی — حذفِ یک کالا یا حساب در نرم‌افزارِ ویندوزی با این‌ها گیر نمی‌کند.
   ۴) TCOD_ARZ: ۲۲ ارزِ اصلیِ صادرات (دلار، یورو، درهم، یوان، لیر، دینار عراق، …) اگر نباشند اضافه می‌شوند.

   نرم‌افزارِ ویندوزی این‌ها را نمی‌شناسد و پیش‌فاکتورِ صادراتی را مثلِ پیش‌فاکتورِ عادی نشان می‌دهد.
   idempotent است. نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────── ۱) TFORMS و مجوزها ───────── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'HEAD_LST_PISHFROOSH_SADERATI')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'HEAD_LST_PISHFROOSH'), 5);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'HEAD_LST_PISHFROOSH_SADERATI', N'پیش فاکتور صادراتی', 3, @Grp,
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @Form INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'HEAD_LST_PISHFROOSH_SADERATI');
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

/* ───────── ۲) اطلاعاتِ صادراتیِ پیش‌فاکتور ───────── */
IF OBJECT_ID(N'[dbo].[PROFORMA_EXPORT]', N'U') IS NULL
    CREATE TABLE [dbo].[PROFORMA_EXPORT](
        [NUMBER]         FLOAT          NOT NULL CONSTRAINT [PK_PROFORMA_EXPORT] PRIMARY KEY,   -- HEAD_LST.NUMBER ِ TAG 20
        [INCOTERM]       NVARCHAR(10)   NULL,
        [INCOTERM_PLACE] NVARCHAR(200)  NULL,
        [PAYMENT_TERMS]  NVARCHAR(500)  NULL,
        [VALID_UNTIL]    BIGINT         NULL,      -- شمسیِ ۸ رقمی
        [PORT_LOADING]   NVARCHAR(200)  NULL,
        [PORT_DISCHARGE] NVARCHAR(200)  NULL,
        [DEST_COUNTRY]   NVARCHAR(100)  NULL,
        [SHIPMENT]       NVARCHAR(50)   NULL,
        [DELIVERY_TIME]  NVARCHAR(200)  NULL,
        [PACKING]        NVARCHAR(500)  NULL,
        [SHIPPING_MARKS] NVARCHAR(500)  NULL,
        [REMARKS]        NVARCHAR(2000) NULL,
        [BANK_ID]        INT            NULL,      -- EXPORT_BANK.ID
        [UP_USER]        NVARCHAR(50)   NULL,
        [UP_DATE]        DATETIME       NOT NULL CONSTRAINT [DF_PROFORMA_EXPORT_UP_DATE] DEFAULT (GETDATE())
    );
GO

/* ───────── ۳) ترجمه‌ها و مشخصاتِ صادراتی ───────── */
IF OBJECT_ID(N'[dbo].[EXPORT_COMPANY]', N'U') IS NULL
    CREATE TABLE [dbo].[EXPORT_COMPANY](
        [ID]         INT            NOT NULL CONSTRAINT [PK_EXPORT_COMPANY] PRIMARY KEY CONSTRAINT [CK_EXPORT_COMPANY_ONE] CHECK ([ID] = 1),
        [NAME_EN]    NVARCHAR(300)  NULL,
        [NAME_AR]    NVARCHAR(300)  NULL,
        [ADDRESS_EN] NVARCHAR(500)  NULL,
        [ADDRESS_AR] NVARCHAR(500)  NULL,
        [COUNTRY_EN] NVARCHAR(100)  NULL,
        [COUNTRY_AR] NVARCHAR(100)  NULL,
        [TEL]        NVARCHAR(100)  NULL,
        [EMAIL]      NVARCHAR(200)  NULL,
        [WEBSITE]    NVARCHAR(200)  NULL,
        [REG_NO]     NVARCHAR(100)  NULL,
        [UP_USER]    NVARCHAR(50)   NULL,
        [UP_DATE]    DATETIME       NOT NULL CONSTRAINT [DF_EXPORT_COMPANY_UP_DATE] DEFAULT (GETDATE())
    );
GO

IF OBJECT_ID(N'[dbo].[EXPORT_BANK]', N'U') IS NULL
    CREATE TABLE [dbo].[EXPORT_BANK](
        [ID]           INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EXPORT_BANK] PRIMARY KEY,
        [TITLE]        NVARCHAR(100)  NOT NULL,
        [BANK_NAME]    NVARCHAR(200)  NULL,
        [BANK_ADDRESS] NVARCHAR(500)  NULL,
        [SWIFT]        NVARCHAR(20)   NULL,
        [IBAN]         NVARCHAR(50)   NULL,
        [ACCOUNT_NO]   NVARCHAR(50)   NULL,
        [BENEFICIARY]  NVARCHAR(300)  NULL,
        [CURRENCY_ISO] NVARCHAR(10)   NULL,
        [ACTIVE]       BIT            NOT NULL CONSTRAINT [DF_EXPORT_BANK_ACTIVE] DEFAULT (1),
        [UP_USER]      NVARCHAR(50)   NULL,
        [UP_DATE]      DATETIME       NOT NULL CONSTRAINT [DF_EXPORT_BANK_UP_DATE] DEFAULT (GETDATE())
    );
GO

IF OBJECT_ID(N'[dbo].[EXPORT_ITEM]', N'U') IS NULL
    CREATE TABLE [dbo].[EXPORT_ITEM](
        [CODE]         NVARCHAR(30)   NOT NULL CONSTRAINT [PK_EXPORT_ITEM] PRIMARY KEY,   -- STUF_DEF.CODE
        [NAME_EN]      NVARCHAR(300)  NULL,
        [NAME_AR]      NVARCHAR(300)  NULL,
        [DESC_EN]      NVARCHAR(500)  NULL,
        [DESC_AR]      NVARCHAR(500)  NULL,
        [HS_CODE]      NVARCHAR(20)   NULL,
        [ORIGIN_EN]    NVARCHAR(100)  NULL,
        [ORIGIN_AR]    NVARCHAR(100)  NULL,
        [NET_WEIGHT]   FLOAT          NULL,      -- کیلوگرم، برای یک واحدِ پایه
        [GROSS_WEIGHT] FLOAT          NULL,
        [UP_USER]      NVARCHAR(50)   NULL,
        [UP_DATE]      DATETIME       NOT NULL CONSTRAINT [DF_EXPORT_ITEM_UP_DATE] DEFAULT (GETDATE())
    );
GO

IF OBJECT_ID(N'[dbo].[EXPORT_PARTY]', N'U') IS NULL
    CREATE TABLE [dbo].[EXPORT_PARTY](
        [HES]        NVARCHAR(50)   NOT NULL CONSTRAINT [PK_EXPORT_PARTY] PRIMARY KEY,   -- CUST_HESAB.hes
        [NAME_EN]    NVARCHAR(300)  NULL,
        [NAME_AR]    NVARCHAR(300)  NULL,
        [ADDRESS_EN] NVARCHAR(500)  NULL,
        [ADDRESS_AR] NVARCHAR(500)  NULL,
        [COUNTRY_EN] NVARCHAR(100)  NULL,
        [COUNTRY_AR] NVARCHAR(100)  NULL,
        [TEL]        NVARCHAR(100)  NULL,
        [EMAIL]      NVARCHAR(200)  NULL,
        [TAX_ID]     NVARCHAR(100)  NULL,
        [UP_USER]    NVARCHAR(50)   NULL,
        [UP_DATE]    DATETIME       NOT NULL CONSTRAINT [DF_EXPORT_PARTY_UP_DATE] DEFAULT (GETDATE())
    );
GO

IF OBJECT_ID(N'[dbo].[EXPORT_UNIT]', N'U') IS NULL
    CREATE TABLE [dbo].[EXPORT_UNIT](
        [CODE]    INT            NOT NULL CONSTRAINT [PK_EXPORT_UNIT] PRIMARY KEY,   -- TCOD_VAHEDS.CODE
        [NAME_EN] NVARCHAR(100)  NULL,
        [NAME_AR] NVARCHAR(100)  NULL,
        [UP_USER] NVARCHAR(50)   NULL,
        [UP_DATE] DATETIME       NOT NULL CONSTRAINT [DF_EXPORT_UNIT_UP_DATE] DEFAULT (GETDATE())
    );
GO

/* ───────── ۴) ارزهای اصلی در TCOD_ARZ ───────── */
-- پیش‌فاکتور و فاکتورِ صادراتی بی فهرستِ ارز کار نمی‌کنند. ارزهایی که بیشترِ صادرات با آن‌هاست اینجا اضافه می‌شوند — با
-- همان کد و عنوانِ ISO 4217 ِ فهرستِ کاملِ ScriptSqly، پس اجرای بعدیِ آن ردیفِ تکراری نمی‌سازد. ارزی که کد یا ISO اش از
-- پیش هست دست نمی‌خورد؛ ارزی که کاربر حذف کرده دوباره برمی‌گردد (فقط همین ۲۲ تا).
IF OBJECT_ID(N'[dbo].[TCOD_ARZ]', N'U') IS NULL
    CREATE TABLE [dbo].[TCOD_ARZ](
        [Code]        INT           NOT NULL PRIMARY KEY,
        [Title]       NVARCHAR(100) NULL,
        [ISOCode]     NVARCHAR(6)   NULL,
        [CountryName] NVARCHAR(100) NULL,
        [CRT]         DATETIME      NULL,
        [UID]         INT           NULL,
        [ID]          BIGINT        IDENTITY(1,1) NOT NULL
    );
GO

INSERT INTO [dbo].[TCOD_ARZ] ([Code], [Title], [ISOCode], [CountryName])
SELECT seed.[Code], seed.[Title], seed.[ISOCode], seed.[CountryName] FROM (VALUES
(840, N'US Dollar', N'USD', N'UNITED STATES OF AMERICA (THE)'),
(978, N'Euro', N'EUR', N'EUROPEAN UNION'),
(784, N'UAE Dirham', N'AED', N'UNITED ARAB EMIRATES (THE)'),
(156, N'Yuan Renminbi', N'CNY', N'CHINA'),
(949, N'Turkish Lira', N'TRY', N'TÜRKİYE'),
(368, N'Iraqi Dinar', N'IQD', N'IRAQ'),
(682, N'Saudi Riyal', N'SAR', N'SAUDI ARABIA'),
(634, N'Qatari Rial', N'QAR', N'QATAR'),
(414, N'Kuwaiti Dinar', N'KWD', N'KUWAIT'),
(512, N'Rial Omani', N'OMR', N'OMAN'),
(48,  N'Bahraini Dinar', N'BHD', N'BAHRAIN'),
(643, N'Russian Ruble', N'RUB', N'RUSSIAN FEDERATION (THE)'),
(826, N'Pound Sterling', N'GBP', N'UNITED KINGDOM OF GREAT BRITAIN AND N. IRELAND'),
(756, N'Swiss Franc', N'CHF', N'SWITZERLAND'),
(392, N'Yen', N'JPY', N'JAPAN'),
(356, N'Indian Rupee', N'INR', N'INDIA'),
(586, N'Pakistan Rupee', N'PKR', N'PAKISTAN'),
(971, N'Afghani', N'AFN', N'AFGHANISTAN'),
(944, N'Azerbaijan Manat', N'AZN', N'AZERBAIJAN'),
(51,  N'Armenian Dram', N'AMD', N'ARMENIA'),
(398, N'Tenge', N'KZT', N'KAZAKHSTAN'),
(934, N'Turkmenistan New Manat', N'TMT', N'TURKMENISTAN')
) AS seed ([Code], [Title], [ISOCode], [CountryName])
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[TCOD_ARZ] t WHERE t.[Code] = seed.[Code] OR t.[ISOCode] = seed.[ISOCode]);
GO

PRINT N'پیش‌فاکتور صادراتی: فرم HEAD_LST_PISHFROOSH_SADERATI و جدول‌های PROFORMA_EXPORT و EXPORT_* و ارزهای اصلی آماده‌اند.';
GO
";
    }
}
