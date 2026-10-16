using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ایجنت «رئیس حسابداری مجازی» در Safir — تحلیلِ علتِ یافته‌های کنترل‌های ثابت: AGT_Investigation و
        /// AGT_Config.InvestigatedForDate. بعد از AccAgentPeopleScript.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/48-acc-agent-investigate.sql در مخزن Safir یکی بماند
        /// (آزمون AccAgentInvestigateTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AccAgentInvestigateScript(SqlConnection db)
        {
            ExecuteBatches(db, AccAgentInvestigateSql);
        }

        private const string AccAgentInvestigateSql = @"/* ═══════════════════════════════════════════════════════════════════
   ایجنت «رئیس حسابداری مجازی» — تحلیلِ علتِ یافته‌های کنترل‌های ثابت
   (Server/Agent/AgentInvestigator.cs). بعد از اسکریپت‌های ۴۶ و ۴۷.

   AGT_Investigation — برای یافته‌های کنترل‌های تطبیقی (حسابی که باید صفر
                       شود، مانده‌ی خلافِ ماهیت، صندوق و بانکِ منفی‌شده،
                       دفتر چک با حساب، کارت انبار با حساب موجودی)، علتی که
                       ایجنت با شواهد پیدا کرده: خطای ثبت، کارِ معوق یا
                       توضیح‌پذیر. یک ردیف برای هر یافته (RuleCode + RefKey)؛
                       اگر یافته عوض شود (Fingerprint) دوباره بررسی می‌شود.
   AGT_Config.InvestigatedForDate — آخرین روزی (شمسی) که نوبتِ روزانه اجرا شد.

   idempotent است؛ چند بار اجرا شدنش ضرری ندارد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.AGT_Investigation', N'U') IS NULL
CREATE TABLE dbo.AGT_Investigation (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AGT_Investigation PRIMARY KEY,
    RuleCode      NVARCHAR(20)   NOT NULL,
    RefKey        NVARCHAR(200)  NOT NULL,
    Fingerprint   NVARCHAR(100)  NOT NULL,     -- مبلغ و تاریخِ یافته هنگامِ بررسی
    Finding       NVARCHAR(600)  NOT NULL,     -- متنِ یافته هنگامِ بررسی
    Amount        FLOAT          NULL,
    Kind          TINYINT        NOT NULL,     -- 1 = توضیح‌پذیر، 2 = کارِ معوق، 3 = خطای ثبت
    Severity      TINYINT        NOT NULL,     -- 1 = اطلاع، 2 = هشدار، 3 = بحرانی
    Confidence    FLOAT          NOT NULL,
    Cause         NVARCHAR(600)  NOT NULL,
    Evidence      NVARCHAR(MAX)  NULL,         -- آرایه‌ی JSON از رشته‌ها
    Vouchers      NVARCHAR(400)  NULL,         -- شماره‌ی سندها، جدا با ویرگول
    Fix           NVARCHAR(2000) NULL,
    CaseId        INT            NULL,         -- پرونده‌ای که برای خطای ثبت باز شد
    IsCurrent     BIT            NOT NULL CONSTRAINT DF_AGT_Investigation_Current DEFAULT 1,   -- 0 = یافته دیگر نیست
    RunId         BIGINT         NULL,
    CreatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_AGT_Investigation_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_AGT_Investigation_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AGT_Investigation_Ref')
    CREATE UNIQUE INDEX UX_AGT_Investigation_Ref ON dbo.AGT_Investigation (RuleCode, RefKey);
GO

IF COL_LENGTH('dbo.AGT_Config', 'InvestigatedForDate') IS NULL
    ALTER TABLE dbo.AGT_Config ADD InvestigatedForDate BIGINT NULL;
GO

PRINT N'ایجنت رئیس حسابداری مجازی: تحلیلِ علتِ یافته‌ها آماده شد.';
GO
";
    }
}
