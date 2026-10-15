using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ایجنت «رئیس حسابداری مجازی» در Safir: تنظیمات، اثر انگشت سندها، پرونده‌ها، اجراها و حافظه
        /// (AGT_Config، AGT_VoucherState، AGT_Case، AGT_Run، AGT_Memory). دسترسی همان فرم ACC_AUDIT است.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/46-acc-agent.sql در مخزن Safir یکی بماند
        /// (آزمون AccAgentTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AccAgentScript(SqlConnection db)
        {
            ExecuteBatches(db, AccAgentSql);
        }

        private const string AccAgentSql = @"/* ═══════════════════════════════════════════════════════════════════
   ایجنت «رئیس حسابداری مجازی» (Server/Agent)

   سرویس پس‌زمینه‌ی Safir که هر چند دقیقه سندهای دستی و خزانه‌ی تازه یا
   اصلاح‌شده را پیدا می‌کند، با مدل هوش مصنوعی بررسی‌شان می‌کند و برای هر
   مشکل یک پرونده می‌سازد. فقط می‌خواند؛ در اسناد چیزی نمی‌نویسد. دسترسی
   همان فرم ACC_AUDIT است (اسکریپت ۴۵): SEE برای دیدن، UPD برای تنظیمات و
   بستن پرونده.

   AGT_Config       — تنظیمات (یک ردیف). پیش‌فرض خاموش است.
   AGT_VoucherState — اثر انگشتِ هر سند دستی و خزانه؛ سندی که اثرش عوض شود
                      و چند دقیقه ثابت بماند، دوباره بررسی می‌شود.
   AGT_Case         — پرونده‌ها.
   AGT_Run          — هر بار که ایجنت مدل را صدا زد (بررسی سند یا گزارش
                      روزانه)، با مصرف و خطا. گزارش روزانه در Output است.
   AGT_Memory       — توضیح‌هایی که آدم‌ها هنگام بستن پرونده دادند و ایجنت
                      باید دفعه‌ی بعد به خاطر بیاورد.

   idempotent است؛ چند بار اجرا شدنش ضرری ندارد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.AGT_Config', N'U') IS NULL
CREATE TABLE dbo.AGT_Config (
    Id                 TINYINT        NOT NULL CONSTRAINT PK_AGT_Config PRIMARY KEY CONSTRAINT CK_AGT_Config_One CHECK (Id = 1),
    IsEnabled          BIT            NOT NULL CONSTRAINT DF_AGT_Config_Enabled  DEFAULT 0,
    IntervalMinutes    INT            NOT NULL CONSTRAINT DF_AGT_Config_Interval DEFAULT 5,
    StableMinutes      INT            NOT NULL CONSTRAINT DF_AGT_Config_Stable   DEFAULT 10,
    MaxReviewsPerCycle INT            NOT NULL CONSTRAINT DF_AGT_Config_PerCycle DEFAULT 10,
    DailyCallLimit     INT            NOT NULL CONSTRAINT DF_AGT_Config_Calls    DEFAULT 300,
    InitialReviewDays  INT            NOT NULL CONSTRAINT DF_AGT_Config_Initial  DEFAULT 3,
    BriefHour          TINYINT        NOT NULL CONSTRAINT DF_AGT_Config_Brief    DEFAULT 7,
    NotifyUserCo       INT            NULL,
    NotifyMobile       NVARCHAR(20)   NULL,
    NotifyEmail        NVARCHAR(200)  NULL,
    LastCycleUtc       DATETIME2      NULL,
    LastError          NVARCHAR(400)  NULL,
    UpdatedBy          NVARCHAR(50)   NULL,
    UpdatedAtUtc       DATETIME2      NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AGT_Config)
    INSERT INTO dbo.AGT_Config (Id) VALUES (1);
GO

IF OBJECT_ID(N'dbo.AGT_VoucherState', N'U') IS NULL
CREATE TABLE dbo.AGT_VoucherState (
    N_S           FLOAT      NOT NULL CONSTRAINT PK_AGT_VoucherState PRIMARY KEY,
    Hash          INT        NOT NULL,
    HashSinceUtc  DATETIME2  NOT NULL,
    ReviewedHash  INT        NULL,
    ReviewedAtUtc DATETIME2  NULL
);
GO

IF OBJECT_ID(N'dbo.AGT_Run', N'U') IS NULL
CREATE TABLE dbo.AGT_Run (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AGT_Run PRIMARY KEY,
    Kind         TINYINT        NOT NULL,      -- 1 = بررسی سند، 2 = گزارش روزانه
    N_S          FLOAT          NULL,
    ForDate      BIGINT         NULL,
    StartedAtUtc DATETIME2      NOT NULL CONSTRAINT DF_AGT_Run_Started DEFAULT SYSUTCDATETIME(),
    DurationMs   INT            NULL,
    ModelCalls   INT            NOT NULL CONSTRAINT DF_AGT_Run_Calls DEFAULT 0,
    TokensIn     INT            NULL,
    TokensOut    INT            NULL,
    Ok           BIT            NOT NULL CONSTRAINT DF_AGT_Run_Ok DEFAULT 0,
    Error        NVARCHAR(400)  NULL,
    Output       NVARCHAR(MAX)  NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AGT_Run_Started')
    CREATE INDEX IX_AGT_Run_Started ON dbo.AGT_Run (StartedAtUtc, Kind);
GO

IF OBJECT_ID(N'dbo.AGT_Case', N'U') IS NULL
CREATE TABLE dbo.AGT_Case (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AGT_Case PRIMARY KEY,
    N_S           FLOAT          NULL,
    Verdict       TINYINT        NOT NULL,     -- 2 = مشکوک، 3 = غلط
    Severity      TINYINT        NOT NULL,     -- 1 = اطلاع، 2 = هشدار، 3 = بحرانی
    Confidence    FLOAT          NOT NULL,
    Title         NVARCHAR(300)  NOT NULL,
    Reasons       NVARCHAR(MAX)  NULL,         -- آرایه‌ی JSON از رشته‌ها
    SuggestedFix  NVARCHAR(2000) NULL,
    Author        NVARCHAR(80)   NULL,
    VoucherDate   BIGINT         NULL,
    Amount        FLOAT          NULL,
    Status        TINYINT        NOT NULL CONSTRAINT DF_AGT_Case_Status DEFAULT 1,
                                               -- 1 = باز، 2 = رفع شد، 3 = درست است، 4 = هشدار نادرست، 5 = سند حذف شد
    RunId         BIGINT         NULL,
    CreatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_AGT_Case_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_AGT_Case_Updated DEFAULT SYSUTCDATETIME(),
    NotifiedAtUtc DATETIME2      NULL,
    ClosedBy      NVARCHAR(50)   NULL,
    CloseNote     NVARCHAR(400)  NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AGT_Case_Status')
    CREATE INDEX IX_AGT_Case_Status ON dbo.AGT_Case (Status, CreatedAtUtc);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AGT_Case_Ns')
    CREATE INDEX IX_AGT_Case_Ns ON dbo.AGT_Case (N_S, Status);
GO

IF OBJECT_ID(N'dbo.AGT_Memory', N'U') IS NULL
CREATE TABLE dbo.AGT_Memory (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AGT_Memory PRIMARY KEY,
    Scope        TINYINT        NOT NULL,      -- 1 = عمومی، 2 = یک حساب (پیشوند)، 3 = یک ثبت‌کننده
    ScopeKey     NVARCHAR(80)   NULL,
    Note         NVARCHAR(1000) NOT NULL,
    CaseId       INT            NULL,
    CreatedBy    NVARCHAR(50)   NOT NULL,
    CreatedAtUtc DATETIME2      NOT NULL CONSTRAINT DF_AGT_Memory_Created DEFAULT SYSUTCDATETIME(),
    IsActive     BIT            NOT NULL CONSTRAINT DF_AGT_Memory_Active DEFAULT 1
);
GO

PRINT N'ایجنت رئیس حسابداری مجازی: جدول‌ها آماده شد (خاموش).';
GO
";
    }
}