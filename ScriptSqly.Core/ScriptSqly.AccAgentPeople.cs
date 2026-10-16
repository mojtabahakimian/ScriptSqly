using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ایجنت «رئیس حسابداری مجازی» در Safir — شناختنِ کاربران و یادگیری از بازخورد: AGT_UserProfile،
        /// AGT_Case.Accounts و AGT_Config.ProfilesForDate. بعد از AccAgentScript.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/47-acc-agent-people.sql در مخزن Safir یکی بماند
        /// (آزمون AccAgentPeopleTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AccAgentPeopleScript(SqlConnection db)
        {
            ExecuteBatches(db, AccAgentPeopleSql);
        }

        private const string AccAgentPeopleSql = @"/* ═══════════════════════════════════════════════════════════════════
   ایجنت «رئیس حسابداری مجازی» — شناختنِ کاربران و یادگیری از بازخورد
   (Server/Agent/AgentPeople.cs). بعد از اسکریپت ۴۶.

   AGT_UserProfile          — پرونده‌ی هر کسی که سند، فاکتور، برگه‌ی انبار یا
                              خزانه ثبت می‌کند: آمارِ کارش در سال (JSON)، سطحی
                              که از همین آمار درآمده، سطحی که رئیس حسابداری
                              دستی گذاشته، و شرحی که مدل درباره‌اش نوشته.
                              هر روز یک بار از روی داده‌ی واقعی ساخته می‌شود.
   AGT_Case.Accounts        — کدهای حسابِ سند هنگامِ ساختنِ پرونده، تا بازخوردِ
                              رئیس حسابداری روی این پرونده به بررسیِ سندهای
                              مشابهِ بعدی برسد.
   AGT_Config.ProfilesForDate — آخرین روزی (شمسی) که پرونده‌ها ساخته شد.

   idempotent است؛ چند بار اجرا شدنش ضرری ندارد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.AGT_UserProfile', N'U') IS NULL
CREATE TABLE dbo.AGT_UserProfile (
    UserKey       NVARCHAR(80)   NOT NULL CONSTRAINT PK_AGT_UserProfile PRIMARY KEY,  -- نامِ یکسان‌شده (AgentUserKey)
    DisplayName   NVARCHAR(80)   NOT NULL,
    Level         TINYINT        NOT NULL,     -- 0 = غیرفعال، 1 = تازه‌کار، 2 = نیازمند دقت، 3 = معمولی، 4 = دقیق
    LevelReasons  NVARCHAR(2000) NULL,         -- آرایه‌ی JSON از رشته‌ها
    LevelOverride TINYINT        NULL,         -- سطحی که رئیس حسابداری دستی گذاشته
    OverrideBy    NVARCHAR(50)   NULL,
    OverrideAtUtc DATETIME2      NULL,
    Stats         NVARCHAR(MAX)  NOT NULL,     -- AgentUserStats به JSON
    Summary       NVARCHAR(2000) NULL,         -- شرحی که مدل درباره‌ی این کاربر نوشته
    SummaryLevel  TINYINT        NULL,         -- سطحِ مؤثر وقتی Summary نوشته شد
    SummaryAtUtc  DATETIME2      NULL,
    FirstActive   BIGINT         NULL,
    LastActive    BIGINT         NULL,
    UpdatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_AGT_UserProfile_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF COL_LENGTH('dbo.AGT_Case', 'Accounts') IS NULL
    ALTER TABLE dbo.AGT_Case ADD Accounts NVARCHAR(1000) NULL;   -- کدهای حسابِ سند، جدا با ویرگول
GO

IF COL_LENGTH('dbo.AGT_Config', 'ProfilesForDate') IS NULL
    ALTER TABLE dbo.AGT_Config ADD ProfilesForDate BIGINT NULL;
GO

PRINT N'ایجنت رئیس حسابداری مجازی: پرونده‌ی کاربران آماده شد.';
GO
";
    }
}
