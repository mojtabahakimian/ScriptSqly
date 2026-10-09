using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «رئیس حسابداری مجازی» در Safir (FORMNAME = ACC_AUDIT): فرم دسترسی، جدول قاعده‌ی حساب‌ها
        /// (AUD_AccountRule) و موردهای پذیرفته‌شده (AUD_Accepted).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/45-acc-audit.sql در مخزن Safir یکی بماند
        /// (آزمون AccAuditMigrationTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AccAuditScript(SqlConnection db)
        {
            ExecuteBatches(db, AccAuditSql);
        }

        private const string AccAuditSql = @"/* ═══════════════════════════════════════════════════════════════════
   رئیس حسابداری مجازی (FORMNAME = ACC_AUDIT)

   کنترل‌های دائمیِ حسابداری در Safir (Server/Audit). خودِ کنترل‌ها فقط
   می‌خوانند و چیزی در دیتابیس نمی‌سازند؛ این اسکریپت فقط سه چیز اضافه
   می‌کند:

   ۱) فرم ACC_AUDIT در TFORMS، تا در «تعیین سطح دسترسی» نرم‌افزار
      ویندوزی پیدا شود. RUN و SEE از تراز آزمایشی (TARAZ_4) کپی می‌شود؛
      UPD (پذیرفتن مورد و تغییر تنظیمات) برای همه خاموش می‌ماند.

   ۲) AUD_AccountRule — حساب‌هایی که قاعده‌ی خاص دارند:
         Kind 1 = باید ظرف MaxDays روز دست‌کم یک بار صفر شود (تنخواه‌گردان، مرکز هزینه)
         Kind 2 = همیشه بدهکار   Kind 3 = همیشه بستانکار
         Kind 4 = ماهیتش بررسی نشود (مثل سود و زیان انباشته)
      Hes پیشوند است: «111-2» همه‌ی زیرحساب‌های 111-2 را هم می‌گیرد.
      بار اول، معین‌هایی که نامشان «تنخواه» یا «هزینه ... تبدیل» دارد با
      ۷ روز ثبت می‌شوند؛ بعد از آن کاربر از صفحه‌ی تنظیمات تغییرشان می‌دهد.

   ۳) AUD_Accepted — موردی که کاربر بررسی کرده و درست دانسته، با دلیل؛
      دیگر در گزارش نمی‌آید. RefKey همان کلیدی است که کنترل برای آن مورد
      می‌سازد (شماره‌ی سند، کد حساب، سریالِ چک و ...).

   idempotent است؛ چند بار اجرا شدنش ضرری ندارد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.AUD_AccountRule', N'U') IS NULL
CREATE TABLE dbo.AUD_AccountRule (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AUD_AccountRule PRIMARY KEY,
    Hes          NVARCHAR(40)  NOT NULL,
    Kind         TINYINT       NOT NULL,
    MaxDays      INT           NULL,
    Note         NVARCHAR(200) NULL,
    CreatedBy    NVARCHAR(50)  NULL,
    CreatedAtUtc DATETIME2     NOT NULL CONSTRAINT DF_AUD_AccountRule_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_AUD_AccountRule UNIQUE (Hes, Kind)
);
GO

IF OBJECT_ID(N'dbo.AUD_Accepted', N'U') IS NULL
CREATE TABLE dbo.AUD_Accepted (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AUD_Accepted PRIMARY KEY,
    RuleCode      VARCHAR(12)   NOT NULL,
    RefKey        NVARCHAR(100) NOT NULL,
    Reason        NVARCHAR(400) NOT NULL,
    AcceptedBy    NVARCHAR(50)  NOT NULL,
    AcceptedAtUtc DATETIME2     NOT NULL CONSTRAINT DF_AUD_Accepted_At DEFAULT SYSUTCDATETIME(),
    IsActive      BIT           NOT NULL CONSTRAINT DF_AUD_Accepted_Active DEFAULT 1
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AUD_Accepted_Rule')
    CREATE INDEX IX_AUD_Accepted_Rule ON dbo.AUD_Accepted (RuleCode, RefKey) WHERE IsActive = 1;
GO

/* ── تنظیمات اولیه — فقط وقتی جدول هنوز خالی است ──
   «ي» و «ك» عربی هم در نام‌ها هست؛ پیش از LIKE یکسان می‌شوند. */
IF NOT EXISTS (SELECT 1 FROM dbo.AUD_AccountRule)
   AND OBJECT_ID(N'dbo.DETA_HES', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.AUD_AccountRule (Hes, Kind, MaxDays, Note, CreatedBy)
    SELECT  CONCAT(m.N_KOL, N'-', m.NUMBER), 1, 7, LEFT(LTRIM(RTRIM(m.NAME)), 200), N'seed'
    FROM    dbo.DETA_HES m
    WHERE   REPLACE(REPLACE(m.NAME, NCHAR(1610), NCHAR(1740)), NCHAR(1603), NCHAR(1705)) LIKE N'%تنخواه%'
       OR   REPLACE(REPLACE(m.NAME, NCHAR(1610), NCHAR(1740)), NCHAR(1603), NCHAR(1705)) LIKE N'%هزینه%تبدیل%';
END
GO

/* ── ۱) فرم در TFORMS ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'ACC_AUDIT')
BEGIN
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'ACC_AUDIT',
            N'رئیس حسابداری مجازی',
            3,
            ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'TARAZ_4'), 2),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
            GETDATE());
END
GO

/* ── ۲) ردیف دسترسی برای همه‌ی کاربران؛ RUN و SEE از تراز آزمایشی ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @AuditFormId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'ACC_AUDIT');
    DECLARE @TarazId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'TARAZ_4');

    IF @AuditFormId IS NOT NULL
    BEGIN
        INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
        SELECT D.IDD, @AuditFormId,
               ISNULL(scTaraz.RUN, 0),
               ISNULL(scTaraz.SEE, 0),
               0, 0, 0, GETDATE()
        FROM [dbo].[SALA_DTL] D
        LEFT JOIN [dbo].[SAL_CHEK] scTaraz ON scTaraz.USERCO = D.IDD AND scTaraz.[OBJECT] = @TarazId
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[SAL_CHEK] E
            WHERE E.USERCO = D.IDD AND E.[OBJECT] = @AuditFormId
        );
    END
END
GO

PRINT N'رئیس حسابداری مجازی: جدول‌ها و دسترسی آماده شد.';
GO
";
    }
}