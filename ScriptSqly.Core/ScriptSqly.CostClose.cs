using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        // GENERATED from Server/Database/*.sql in the Safir repo.
        // Do not edit by hand: change the .sql file and re-sync, otherwise
        // the customer database drifts from the development one.
        private static void CostCloseScript(SqlConnection db)
        {
            // Order matters: blocks 10-13 create the tables and procedures
            // the later blocks depend on.

            // --- 10-schema.sql ---
            string baseSchema = @"
/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۱ از ۳ : ساختار جداول

   هیچ جدول موجودی تغییر نمی‌کند. همه چیز با پیشوند CC_ اضافه می‌شود.
   قابل اجرای مکرر: اگر جدولی از قبل باشد، دست‌نخورده می‌ماند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، CC_ItemCost که ستون محاسباتی PERSISTED دارد (TotalCost)
-- در صورت خاموش بودن QUOTED_IDENTIFIER پیش‌فرض نشست/پایگاه، همان خطای
-- 1934 را که در رویه‌ها دیدیم، هنگام خودِ CREATE TABLE می‌دهد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────────────── اجرا و گام‌ها ───────────────────────── */

IF OBJECT_ID('dbo.CC_Run','U') IS NULL
CREATE TABLE dbo.CC_Run (
    RunId          INT IDENTITY(1,1) PRIMARY KEY,
    FiscalYear     SMALLINT      NOT NULL,
    PeriodMonth    TINYINT       NOT NULL,          -- ۱ تا ۱۲ = HEAD_MANF.GHEYMAT
    DateFrom       BIGINT        NOT NULL,          -- 14050401
    DateTo         BIGINT        NOT NULL,          -- 14050431
    RunNo          SMALLINT      NOT NULL,          -- شماره اجرا در همان ماه
    PrevRunId      INT           NULL,
    IsLatest       BIT           NOT NULL DEFAULT 1,
    RunKind        TINYINT       NOT NULL,          -- 1=آزمایشی 2=قطعی
    Status         TINYINT       NOT NULL,          -- 0=پیش‌نویس 1=درحال‌اجرا 2=متوقف
                                                    -- 3=تکمیل 4=خطا 5=بازگردانی‌شده
    FormulasDirty  BIT           NOT NULL DEFAULT 0,
    StartedAtUtc   DATETIME2     NULL,
    FinishedAtUtc  DATETIME2     NULL,
    StartedByUser  NVARCHAR(50)  NOT NULL,
    ApprovedByUser NVARCHAR(50)  NULL,
    ApprovedAtUtc  DATETIME2     NULL,
    Note           NVARCHAR(500) NULL,
    CreatedAtUtc   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_Run_Period')
    CREATE INDEX IX_CC_Run_Period ON dbo.CC_Run(FiscalYear, PeriodMonth, Status);
GO

IF OBJECT_ID('dbo.CC_RunStep','U') IS NULL
CREATE TABLE dbo.CC_RunStep (
    RunStepId     INT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT           NOT NULL REFERENCES dbo.CC_Run(RunId),
    StepCode      VARCHAR(10)   NOT NULL,
    StepTitle     NVARCHAR(120) NOT NULL,
    SeqNo         SMALLINT      NOT NULL,
    -- INT و نه TINYINT: حلقه‌ی همگرایی S07A↔S11 در هر اجرا تا ۴۰ دور می‌رود و
    -- این شمارنده بین اجراهای مکررِ همان Run انباشته می‌شود. روی یک ران واقعی
    -- (اردیبهشت ۱۴۰۵) S07A به ۲۵۵ رسید و دور بعد با
    -- «Arithmetic overflow error for data type tinyint, value = 256»
    -- کل بستن ماه را متوقف کرد. ۲۵۵ در استفاده‌ی عادی قابل‌دسترس است.
    Attempt       INT           NOT NULL DEFAULT 1,
    Status        TINYINT       NOT NULL,           -- 0=درانتظار 1=درحال‌اجرا 2=موفق
                                                    -- 3=هشدار 4=خطا 5=رد‌شده
    StartedAtUtc  DATETIME2     NULL,
    FinishedAtUtc DATETIME2     NULL,
    DurationMs    INT           NULL,
    RowsAffected  INT           NULL,
    ResultJson    NVARCHAR(MAX) NULL,
    ErrorMessage  NVARCHAR(MAX) NULL,
    CONSTRAINT UQ_CC_RunStep UNIQUE (RunId, StepCode, Attempt)
);
GO

IF OBJECT_ID('dbo.CC_RunLog','U') IS NULL
CREATE TABLE dbo.CC_RunLog (
    LogId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId       INT            NULL,
    StepCode    VARCHAR(10)    NULL,
    LoggedAtUtc DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    Severity    TINYINT        NOT NULL,            -- 0=ریز 1=اطلاع 2=هشدار 3=خطا
    Message     NVARCHAR(2000) NOT NULL,
    ContextJson NVARCHAR(MAX)  NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_RunLog_Run')
    CREATE INDEX IX_CC_RunLog_Run ON dbo.CC_RunLog(RunId, LogId);
GO

/* ───────────────────────── اسنپ‌شات و بازگردانی ───────────────────────── */

IF OBJECT_ID('dbo.CC_Snapshot','U') IS NULL
CREATE TABLE dbo.CC_Snapshot (
    SnapshotId    INT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT       NOT NULL,
    StepCode      VARCHAR(10) NOT NULL,
    TableName     SYSNAME   NOT NULL,
    BackupTable   SYSNAME   NOT NULL,
    RowsCopied    INT       NOT NULL,
    TakenAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RestoredAtUtc DATETIME2 NULL
);
GO

/* ───────────────────────── قواعد تشخیص و استثناها ───────────────────────── */

IF OBJECT_ID('dbo.CC_CheckRule','U') IS NULL
CREATE TABLE dbo.CC_CheckRule (
    RuleCode        VARCHAR(12)   NOT NULL PRIMARY KEY,
    RuleName        NVARCHAR(120) NOT NULL,
    StepCode        VARCHAR(10)   NOT NULL,
    ExType          TINYINT       NOT NULL,
    DefaultSeverity TINYINT       NOT NULL,        -- 1=هشدار 2=مسدودکننده
    Threshold       FLOAT         NULL,
    RemedyText      NVARCHAR(600) NOT NULL,
    IsActive        BIT           NOT NULL DEFAULT 1,
    SortOrder       SMALLINT      NOT NULL
);
GO

IF OBJECT_ID('dbo.CC_Exception','U') IS NULL
CREATE TABLE dbo.CC_Exception (
    ExceptionId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId          INT            NULL,
    StepCode       VARCHAR(10)    NOT NULL,
    RuleCode       VARCHAR(12)    NULL,
    ExType         TINYINT        NOT NULL,
    Severity       TINYINT        NOT NULL,
    Anbar          INT            NULL,
    Code           BIGINT         NULL,
    DocNumber      INT            NULL,
    DocTag         INT            NULL,
    DocDate        BIGINT         NULL,
    Amount         FLOAT          NULL,
    Description    NVARCHAR(500)  NOT NULL,
    IsResolved     BIT            NOT NULL DEFAULT 0,
    ResolvedBy     NVARCHAR(50)   NULL,
    ResolvedAtUtc  DATETIME2      NULL,
    ResolutionNote NVARCHAR(500)  NULL,
    CreatedAtUtc   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF COL_LENGTH('dbo.CC_Exception','RuleCode') IS NULL
    ALTER TABLE dbo.CC_Exception ADD RuleCode VARCHAR(12) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_Exception_Run')
    CREATE INDEX IX_CC_Exception_Run
        ON dbo.CC_Exception(RunId, StepCode, IsResolved, Severity);
GO

/* استثناهایی که کاربر یک‌بار پذیرفته و نباید هر ماه تکرار شوند */
IF OBJECT_ID('dbo.CC_AcceptedException','U') IS NULL
CREATE TABLE dbo.CC_AcceptedException (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    RuleCode     VARCHAR(12)   NOT NULL,
    Code         BIGINT        NULL,        -- کالا؛ NULL يعني همه
    FNUMB        INT           NULL,        -- فرمول؛ NULL يعني همه
    Reason       NVARCHAR(400) NOT NULL,
    AcceptedBy   NVARCHAR(50)  NOT NULL,
    AcceptedAtUtc DATETIME2    NOT NULL DEFAULT SYSUTCDATETIME(),
    IsActive     BIT           NOT NULL DEFAULT 1
);
GO
-- CHK-01/CHK-02 بر خلاف CHK-03/CHK-04 روی جفت (انبار، کالا) کار می‌کنند،
-- نه فقط کالا — بدون این ستون، پذیرفتن یک مغایرت برای یک انبار خاص،
-- همان کد را در همه‌ی انبارها هم بی‌صدا خاموش می‌کرد. NULL يعني همه‌ی
-- انبارها (عيناً همان قرارداد Code/FNUMB بالا).
IF COL_LENGTH('dbo.CC_AcceptedException','Anbar') IS NULL
    ALTER TABLE dbo.CC_AcceptedException ADD Anbar INT NULL;
GO

/* ───────────────────────── واحدهای تولیدی ───────────────────────── */

IF OBJECT_ID('dbo.CC_Unit','U') IS NULL
CREATE TABLE dbo.CC_Unit (
    UnitId     INT IDENTITY(1,1) PRIMARY KEY,
    UnitName   NVARCHAR(60) NOT NULL,
    Depatman   INT          NULL,
    SplitMode  TINYINT      NOT NULL DEFAULT 1,   -- 1=يک ضريب 2=دو ضريب
    IsActive   BIT          NOT NULL DEFAULT 1,
    SeqNo      SMALLINT     NOT NULL DEFAULT 1
);
GO

IF OBJECT_ID('dbo.CC_UnitAnbar','U') IS NULL
CREATE TABLE dbo.CC_UnitAnbar (
    UnitId       INT      NOT NULL REFERENCES dbo.CC_Unit(UnitId),
    Anbar        INT      NOT NULL,
    AnbarRole    TINYINT  NOT NULL,   -- 1=مواد مصرفي توليد 2=مواد اوليه
                                      -- 3=محصول 4=ساير
    DoStockCount BIT      NOT NULL DEFAULT 1,
    SeqNo        SMALLINT NOT NULL DEFAULT 1,
    PRIMARY KEY (UnitId, Anbar)
);
GO

IF OBJECT_ID('dbo.CC_UnitAcc','U') IS NULL
CREATE TABLE dbo.CC_UnitAcc (
    Id         INT           IDENTITY(1,1) PRIMARY KEY,
    UnitId     INT           NOT NULL REFERENCES dbo.CC_Unit(UnitId),
    HesKol     INT           NOT NULL,
    HesMoin    INT           NULL,   -- خالی = همه معین‌های این کل
    HesTafsili INT           NULL,   -- خالی = همه تفصیلی‌های همان معین
    CostKind   TINYINT       NOT NULL,          -- 1=دستمزد 2=سربار
    Ratio      DECIMAL(9,6)  NOT NULL DEFAULT 1,
    IsActive   BIT           NOT NULL DEFAULT 1,
    Note       NVARCHAR(200) NULL,
    CONSTRAINT UQ_CC_UnitAcc UNIQUE (UnitId, HesKol, HesMoin, HesTafsili)
);
GO

-- روی نصب‌های قدیمی‌تر که این جدول را بدون سطح معین/تفصیلی دارند
IF COL_LENGTH('dbo.CC_UnitAcc','HesMoin') IS NULL
    ALTER TABLE dbo.CC_UnitAcc ADD HesMoin INT NULL;
GO
IF COL_LENGTH('dbo.CC_UnitAcc','HesTafsili') IS NULL
    ALTER TABLE dbo.CC_UnitAcc ADD HesTafsili INT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UQ_CC_UnitAcc' AND parent_object_id = OBJECT_ID('dbo.CC_UnitAcc'))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.name = 'UQ_CC_UnitAcc' AND c.name = 'HesMoin')
BEGIN
    ALTER TABLE dbo.CC_UnitAcc DROP CONSTRAINT UQ_CC_UnitAcc;
    ALTER TABLE dbo.CC_UnitAcc ADD CONSTRAINT UQ_CC_UnitAcc
        UNIQUE (UnitId, HesKol, HesMoin, HesTafsili);
END
GO

-- نگاشت انبار به حساب موجودی جنسی (کل/معین)، برای CHK-02.
-- TCOD_ANBAR هیچ ستون حسابداری ندارد و این نگاشت شرکت‌به‌شرکت فرق
-- می‌کند (هر انبار زیر یک معین جداگانه در حسابداری ثبت می‌شود، نه یک
-- معین ثابت مشترک) — پس باید از تنظیمات وارد شود، نه هاردکد در کد.
IF OBJECT_ID('dbo.CC_AnbarHes','U') IS NULL
CREATE TABLE dbo.CC_AnbarHes (
    Anbar    INT           NOT NULL PRIMARY KEY,
    HesKol   INT           NOT NULL,
    HesMoin  INT           NOT NULL,
    Note     NVARCHAR(200) NULL
);
GO

/* ضریب جذب دستمزد به تفکیک (واحد تولیدی، کالا) — مثلاً بر مبنای وزن،
   حجم یا ارزش فروش، هرچه کاربر تعیین کند؛ یک کالا می‌تواند در واحدهای
   مختلف (یزد، تهران، ...) ضریب متفاوت داشته باشد. ممکن است برای کل
   سال یکسان بماند — بدون بُعد ماه/تاریخ عمداً. مبنای تقسیمِ دستمزد
   واقعیِ هر واحد بین کالاهای همان واحد در گام S07B (نگاه کنید
   CC_sp_S07B_SyncLaborRate). CODE هم‌نوع STUF_DEF.CODE/HEAD_MANF.CODE
   است (هر دو nvarchar(30)) تا JOIN بدون CAST انجام شود.

   Coefficient عمداً NULL می‌پذیرد: ردیف‌ها با
   POST labor-rates/sync-from-formulas از روی HEAD_MANF خودکار ساخته
   می‌شوند (Coefficient=NULL، یعنی «هنوز بررسی نشده»)؛ کاربر فقط عدد
   ضریب را پر می‌کند. S07B ردیف‌های NULL/صفر را از تقسیم کنار می‌گذارد.

   IsFixed: بعضی کالاها کارمزدی تولید می‌شوند و نرخشان (HEAD_MANF.
   IMBIBE_MANF) باید همیشه ثابت بماند — نه S07B (تقسیم بر اساس ضریب) و
   نه S10 (ضریب تعدیل یکنواخت) نباید دست‌شان بزنند. تأیید کاربر: این
   ویژگی هم به (واحد، کالا) وابسته است، نه فقط کالا — یک کالا ممکن است
   در یک واحد کارمزدی باشد و در واحد دیگر نه.

   OverheadCoefficient: ضریب جذبِ سربار (IMBIBE_SAR)، مستقل از ضریب
   دستمزد — چون معیارِ درستِ سربار می‌تواند با معیارِ دستمزد فرق کند.
   عمداً NULL می‌پذیرد و در محاسبه به ضریب دستمزد بازمی‌گردد (تأیید
   کاربر: «فعلاً از دستمزد براش مقدار بده») — یعنی تا وقتی کاربر
   مقدار مستقلی برای یک ردیف وارد نکند، همان ضریب دستمزد برای سربارش
   هم استفاده می‌شود. */
IF OBJECT_ID('dbo.CC_LaborAbsorptionRate','U') IS NULL
CREATE TABLE dbo.CC_LaborAbsorptionRate (
    UnitId              INT           NOT NULL,
    CODE                NVARCHAR(30)  NOT NULL,
    Coefficient         FLOAT         NULL,
    OverheadCoefficient FLOAT         NULL,
    IsFixed             BIT           NOT NULL DEFAULT 0,
    Note                NVARCHAR(200) NULL,
    CONSTRAINT PK_CC_LaborAbsorptionRate PRIMARY KEY (UnitId, CODE),
    CONSTRAINT FK_CC_LaborAbsorptionRate_Unit FOREIGN KEY (UnitId) REFERENCES dbo.CC_Unit(UnitId)
);
GO

/* ───────────────────────── نتایج محاسبه ───────────────────────── */

IF OBJECT_ID('dbo.CC_ItemCost','U') IS NULL
CREATE TABLE dbo.CC_ItemCost (
    Id           BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId        INT      NULL,
    PeriodMonth  TINYINT  NOT NULL,
    Code         BIGINT   NOT NULL,
    LowLevelCode SMALLINT NOT NULL,
    SourceKind   TINYINT  NOT NULL,      -- 1=ميانگين انبار 2=فرمول 3=بدون منبع
    FNUMB        INT      NULL,
    MaterialCost FLOAT    NOT NULL DEFAULT 0,
    WageCost     FLOAT    NOT NULL DEFAULT 0,
    OverheadCost FLOAT    NOT NULL DEFAULT 0,
    TotalCost    AS (MaterialCost + WageCost + OverheadCost) PERSISTED,
    CalculatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_ItemCost_Lookup')
    CREATE INDEX IX_CC_ItemCost_Lookup ON dbo.CC_ItemCost(PeriodMonth, Code, RunId);
GO

IF OBJECT_ID('dbo.CC_FormulaChange','U') IS NULL
CREATE TABLE dbo.CC_FormulaChange (
    ChangeId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId        INT           NOT NULL,
    StepCode     VARCHAR(10)   NOT NULL,
    FNUMB        INT           NOT NULL,
    ParentCode   BIGINT        NULL,
    ChildCode    BIGINT        NULL,
    FieldName    VARCHAR(20)   NOT NULL,   -- SMABL MABLK MEGHK PERT
                                           -- IMBIBE_MANF IMBIBE_SAR
    OldValue     FLOAT         NULL,
    NewValue     FLOAT         NULL,
    Reason       NVARCHAR(200) NULL,
    ChangedAtUtc DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_FormulaChange_Run')
    CREATE INDEX IX_CC_FormulaChange_Run ON dbo.CC_FormulaChange(RunId, StepCode, FNUMB);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_FormulaChange_Code')
    CREATE INDEX IX_CC_FormulaChange_Code ON dbo.CC_FormulaChange(ChildCode, RunId);
GO

/* ───────────────────────── انحراف و تصمیم‌ها ───────────────────────── */

IF OBJECT_ID('dbo.CC_Variance','U') IS NULL
CREATE TABLE dbo.CC_Variance (
    VarianceId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId          INT    NOT NULL,
    Anbar          INT    NOT NULL,
    Code           BIGINT NOT NULL,
    QtyVariance    FLOAT  NOT NULL,
    UnitRate       FLOAT  NULL,
    AmountVariance FLOAT  NULL,
    ConsumedQty    FLOAT  NULL,
    IsKeyItem      BIT    NOT NULL DEFAULT 0,
    CONSTRAINT UQ_CC_Variance UNIQUE (RunId, Anbar, Code)
);
GO

IF OBJECT_ID('dbo.CC_VarianceDecision','U') IS NULL
CREATE TABLE dbo.CC_VarianceDecision (
    DecisionId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId        INT           NOT NULL,
    Code         BIGINT        NOT NULL,
    Mode         TINYINT       NOT NULL,   -- 1=اختصاص 2=تسهيم 3=بدون تخصيص
    TargetCode   BIGINT        NULL,       -- کليد پايدار بين ماه‌ها
    TargetFNUMB  INT           NULL,       -- فرمول ماه جاري، مشتق از TargetCode
    AppliedQty   FLOAT         NULL,
    DecidedBy    NVARCHAR(50)  NOT NULL,
    DecidedAtUtc DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    Note         NVARCHAR(300) NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_CC_VarDecision_Code')
    CREATE INDEX IX_CC_VarDecision_Code ON dbo.CC_VarianceDecision(Code, RunId);
GO
-- محافظ ساختاری: یک تصمیم به ازای هر (اجرا،کالا). بدون این، اگر جایی
-- (کلاینت/SaveDecisions/S09a) به‌اشتباه دوباره INSERT کند بدون DELETE
-- قبلی، ردیف‌های تکراری بی‌صدا وارد می‌شوند و CC_sp_S09_ApplyDecisions
-- سهم انحراف را غیرقطعی/چندبار اعمال می‌کند — دقیقاً همان چیزی که در
-- اجرای ۱۶ باعث شد «باقیمانده» با هر بار «اعمال و محاسبه مجدد» بدتر شود.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UQ_CC_VarianceDecision')
    CREATE UNIQUE INDEX UQ_CC_VarianceDecision ON dbo.CC_VarianceDecision(RunId, Code);
GO

/* ───────────────────────── هزینه تبدیل و حاشیه سود ───────────────────────── */

IF OBJECT_ID('dbo.CC_ConversionCost','U') IS NULL
CREATE TABLE dbo.CC_ConversionCost (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    RunId            INT           NOT NULL,
    UnitId           INT           NOT NULL,
    CostKind         TINYINT       NOT NULL,   -- 0=کل 1=دستمزد 2=سربار
    AbsorbedAmount   DECIMAL(19,0) NOT NULL,
    AbsorbedFromWip  DECIMAL(19,0) NULL,
    ActualAmount     DECIMAL(19,0) NOT NULL,
    ActualDetailJson NVARCHAR(MAX) NULL,
    AdjustFactor     DECIMAL(18,8) NOT NULL,
    ApprovedBy       NVARCHAR(50)  NULL,
    CONSTRAINT UQ_CC_ConversionCost UNIQUE (RunId, UnitId, CostKind)
);
GO

IF OBJECT_ID('dbo.CC_MarginTarget','U') IS NULL
CREATE TABLE dbo.CC_MarginTarget (
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    Code           BIGINT       NOT NULL,
    TargetKind     TINYINT      NOT NULL,   -- 1=سود صفر 2=درصد مشخص 3=آزاد 4=سود صفر با پخش خودکار
    TargetPct      DECIMAL(9,4) NULL,
    BalancingCode  BIGINT       NULL,
    BalancingFNUMB INT          NULL,
    IsActive       BIT          NOT NULL DEFAULT 1,
    Note           NVARCHAR(300) NULL
);
GO

PRINT N'ساختار جداول ايجاد شد.';

SELECT  t.name AS جدول,
        (SELECT SUM(p.rows) FROM sys.partitions p
         WHERE p.object_id = t.object_id AND p.index_id IN (0,1)) AS تعداد_سطر
FROM    sys.tables t
WHERE   t.name LIKE 'CC[_]%'
ORDER BY t.name;
GO
";
            TryExecuteCostCloseBatch(db, baseSchema,
                "جدول‌های پایه CC_*",
                "اسکریپت 10-schema.sql را اجرا کنید.");

            // --- 11-seed-data.sql ---
            string seedData = @"
/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۲ از ۳ : داده اولیه

   قواعد تشخیص، واحدهای تولیدی، و استثناهای پذیرفته‌شده.
   قابل اجرای مکرر.

   ⚠ بخش واحدهای تولیدی را بر اساس واقعیت کارخانه ویرایش کنید.
     مقادیر فعلی نمونه‌اند و از گزارش موجودی خودتان استخراج شده‌اند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

/* ───────────────────────── قواعد تشخیص ───────────────────────── */

MERGE dbo.CC_CheckRule AS t
USING (VALUES
 ('CHK-01', N'کاردکس منفی', 'S05', 1, 2, -0.001,
  N'تاریخ رسید یا حواله را جابه‌جا کنید تا موجودی در هیچ لحظه‌ای منفی نشود.', 10),

 ('CHK-02', N'مغایرت کارت انبار و حسابداری', 'S05', 2, 2, NULL,
  N'معمولاً حواله‌ای است که فاکتورش صادر نشده، یا تاریخ فاکتور در ماه بعد افتاده. تاریخ‌ها را یکسان کنید.', 20),

 ('CHK-03', N'فرمول بدون نرخ جذب هزینه تبدیل', 'S00', 9, 1, NULL,
  N'در فرمول، «جذب هزینه دستمزد» را پر کنید. اگر عمداً صفر است (محصول فرعی مانند آب پنیر خالص)، آن را در فهرست استثناهای پذیرفته‌شده ثبت کنید تا دیگر هشدار ندهد.', 30),

 ('CHK-04', N'کالای تولیدشده بدون فرمول ماه', 'S00', 12, 2, NULL,
  N'نسخه ماه جاری فرمول ساخته نشده است. با «کپی فرمول» نسخه ماه را بسازید.', 40),

 ('CHK-05', N'ماده بدون منبع نرخ', 'S00', 4, 1, NULL,
  N'این ماده نه فرمول دارد و نه گردش خروج در ماه، بنابراین نرخش صفر می‌ماند و صفر را به همه کالاهای بالادست منتقل می‌کند. یک نرخ برایش تعیین کنید.', 50),

 ('CHK-06', N'حلقه در ساختار فرمول', 'S00', 5, 2, NULL,
  N'کالا مستقیم یا غیرمستقیم خودش را مصرف می‌کند. تا این حلقه شکسته نشود، محاسبه نرخ ممکن نیست.', 60),

 ('CHK-07', N'مانده نامتوازن مواد در حساب ۷۵۱', 'S00', 13, 1, 0.001,
  N'اگر یک طرف صفر باشد، حواله جا افتاده است. آستانه یک در هزار است؛ کمتر از آن گِردکردن طبیعی است و نیاز به اقدام ندارد.', 70),

 ('CHK-08', N'اختلاف جذب برگه تولید با سند', 'S10', 10, 1, NULL,
  N'سند حسابداری وقتی صادر شده که فرمول نرخ دیگری داشته است. برگه تولید را بازسازی کنید.', 80),

 ('CHK-09', N'نرخ منتشرنشده نیمه‌ساخته', 'S11', 14, 2, 0.001,
  N'بهای خودِ فرمول این کالا با نرخی که در فرمول کالاهای بالادست دارد نمی‌خواند؛ یعنی انتشار نرخ کامل نشده. پس از اجرای کامل محاسبه نرخ، این قاعده باید صفر شود.', 90),

 ('CHK-10', N'مانده حساب کالای در جریان ساخت', 'S10', 8, 1, 10000000,
  N'فرض «کالای در جریان ساخت صفر» نقض شده است. آستانه ده میلیون ریال تنظیم شده تا باقیمانده گِردکردن هشدار کاذب ندهد.', 100),

 ('CHK-11', N'انحراف روی ماده مصرف‌نشده', 'S09', 11, 1, NULL,
  N'این ماده در هیچ فرمولی مصرف نشده ولی انحراف دارد. برگه انتقال یا انبارِ انبارگردانی را بررسی کنید.', 110),

 ('CHK-12', N'فرمول مقصد ماه قبل موجود نیست', 'S09', 15, 1, NULL,
  N'تصمیم ماه قبل قابل ادامه نیست چون کالای مقصد امسال فرمول ندارد. پیش‌فرض روی تسهیم به نسبت مصرف قرار گرفت.', 120),

 ('CHK-13', N'حواله با مقدار صفر', 'S07', 16, 2, NULL,
  N'ماده در فرمول مقدار دارد ولی حواله‌اش با مقدار صفر صادر شده؛ یعنی فرمول پس از صدور حواله ویرایش شده است. خروج مواد باید بازسازی شود.', 130),

 ('CHK-15', N'فرمول با مقدار منفی', 'S00', 17, 2, NULL,
  N'مقدار منفی در یک سطر فرمول قابل قبول نیست و باعث می‌شود مانده حساب کالای در جریان ساخت (۷۵۱) هرگز متوازن نشود. با دکمه اصلاح، آن سطر را صفر یا حذف کنید.', 75),

 ('CHK-16', N'برگه تولید به انبار بدون واحد تعریف‌شده', 'S00', 18, 1, NULL,
  N'این انبار را در تنظیمات، به تعریف واحدهای تولیدی (نقش «محصول») اضافه کنید — وگرنه هزینه تبدیل این برگه‌ها در هیچ واحدی جذب نمی‌شود و مانده حساب ۷۵۱ کاذب می‌شود.', 45),

 ('CHK-17', N'شمارش دوم/سوم انبارگردانی بدون مغایرت شمارش اول', 'S00', 19, 2, NULL,
  N'شمارش اول این کالا با موجودی سیستم برابر بوده، پس نباید وارد شمارش دوم/سوم می‌شد. ستون NUM2/NUM3 را که اشتباه پر شده صفر کنید — این عدد مستقیم مقدار پایان‌دوره‌ی کالا را در موتور نرخ غلط می‌کند.', 46),

 ('CHK-18', N'فاصله بیش از یک ماه بین فاکتور و حواله/رسید یا برگشت', 'S00', 20, 2, NULL,
  N'مشخص نیست کدام تاریخ درست است — از دکمه‌ی «اصلاح تاریخ» کنار همین ردیف استفاده کنید و تاریخ درست را انتخاب کنید تا سند دیگر با آن یکی شود.', 47),

 ('CHK-19', N'تاریخ فاکتور با تاریخ سند حسابداری‌اش یکی نیست', 'S00', 21, 1, NULL,
  N'از دکمه‌ی «اصلاح تاریخ» کنار همین ردیف استفاده کنید و تاریخ درست را انتخاب کنید — معمولاً بعد از اصلاح تاریخ یک فاکتور (CHK-18) پیش می‌آید، چون آن اصلاح فقط فاکتور/حواله را عوض می‌کند، نه سند حسابداریِ از قبل صادرشده را.', 48),

 ('CHK-20', N'نرخ میانگین منفی', 'S00', 22, 1, NULL,
  N'این نرخ منفی معمولاً پیامد یک کاردکس منفی (CHK-01) در تاریخی نزدیک همین سند است. آن مغایرت را بررسی و در صورت لزوم فیِ این سند را دستی به نرخ واقعیِ همان لحظه اصلاح کنید.', 49),

 ('CHK-21', N'تاریخ برگشت فروش با تاریخ سند حسابداری‌اش یکی نیست', 'S00', 23, 1, NULL,
  N'از دکمه‌ی «اصلاح تاریخ» کنار همین ردیف استفاده کنید و تاریخ درست را انتخاب کنید — تا وقتی این دو یکی نشوند، کاردکس این حواله را در ماهِ خودش می‌بیند ولی حسابداری در ماهِ دیگر، و CHK-02 مغایرتِ کاذب نشان می‌دهد.', 50)
) AS s (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
ON t.RuleCode = s.RuleCode
-- ⚠️ Threshold عمداً از WHEN MATCHED بیرون است: کاربر می‌تواند از تنظیمات
-- برنامه آستانه‌ی هر قاعده را عوض کند (مثلاً CHK-01)؛ اگر این Seed دوباره
-- اجرا شود، نباید آن تنظیم دستی را با مقدار پیش‌فرض پاک کند. Threshold
-- فقط در INSERT اولیه (ردیف جدید) از مقدار پیش‌فرض بالا پر می‌شود.
WHEN MATCHED THEN UPDATE SET
    t.RuleName = s.RuleName, t.StepCode = s.StepCode, t.ExType = s.ExType,
    t.DefaultSeverity = s.DefaultSeverity,
    t.RemedyText = s.RemedyText, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT
    (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
    VALUES (s.RuleCode, s.RuleName, s.StepCode, s.ExType, s.DefaultSeverity,
            s.Threshold, s.RemedyText, s.SortOrder);
GO


/* ───────────────────────── استثنای پذیرفته‌شده ─────────────────────────
   آب پنیر خالص محصول فرعی است و عمداً هزینه تبدیل جذب نمی‌کند.
   تأیید شده توسط کاربر.
   ─────────────────────────────────────────────────────────────────────── */

IF NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException
               WHERE RuleCode = 'CHK-03' AND Code = 1787)
INSERT dbo.CC_AcceptedException (RuleCode, Code, FNUMB, Reason, AcceptedBy)
VALUES ('CHK-03', 1787, NULL,
        N'آب پنیر خالص محصول فرعی است و عمداً هزینه تبدیل جذب نمی‌کند.',
        N'مدیر مالی');
GO


/* ───────────────────────── واحدهای تولیدی ─────────────────────────
   ⚠ این بخش نمونه است. انبارها را با واقعیت کارخانه تطبیق دهید.
     نقش ۱ (مواد مصرفی تولید) مبنای محاسبه انحراف است و باید
     برای هر واحد دقیقاً یک انبار داشته باشد.
   ─────────────────────────────────────────────────────────────────── */

IF NOT EXISTS (SELECT 1 FROM dbo.CC_Unit)
BEGIN
    INSERT dbo.CC_Unit (UnitName, Depatman, SplitMode, IsActive, SeqNo)
    VALUES (N'واحد اصلی', NULL, 1, 1, 1),
           (N'واحد یزد',  NULL, 1, 1, 2);

    DECLARE @u1 INT = (SELECT UnitId FROM dbo.CC_Unit WHERE UnitName = N'واحد اصلی');
    DECLARE @u2 INT = (SELECT UnitId FROM dbo.CC_Unit WHERE UnitName = N'واحد یزد');

    INSERT dbo.CC_UnitAnbar (UnitId, Anbar, AnbarRole, DoStockCount, SeqNo)
    VALUES (@u1,   7, 1, 1, 1),      -- مواد مصرفي توليد ← مبناي انحراف
           (@u1,   1, 2, 1, 2),      -- مواد اوليه
           (@u1,   2, 3, 1, 3),      -- کالاي ساخته شده
           (@u1,   8, 4, 1, 4),
           (@u2, 810, 1, 1, 1),      -- مواد مصرفي توليد يزد
           (@u2, 811, 2, 1, 2),      -- مواد اوليه يزد
           (@u2, 807, 3, 1, 3);      -- محصول يزد

    -- نگاشت سرفصل‌هاي هزينه تبديل واقعي، بر اساس تراز خودتان
    INSERT dbo.CC_UnitAcc (UnitId, HesKol, CostKind, Ratio, Note)
    VALUES (@u1, 711, 1, 1.000, N'هزينه دستمزد توليد'),
           (@u1, 712, 1, 0.700, N'هزينه دستمزد خدمات — سهم توليدي'),
           (@u1, 713, 1, 0.600, N'هزينه دستمزد اداري — سهم توليدي'),
           (@u1, 721, 2, 1.000, N'ساير هزينه‌هاي توليد'),
           (@u1, 723, 2, 0.400, N'ساير هزينه‌هاي اداري — سهم توليدي'),
           (@u1, 745, 2, 0.250, N'مرکز هزينه ضايعات و ساير'),
           (@u2, 743, 2, 1.000, N'هزينه‌هاي واحد يزد');
END
GO

/* ───────────────────────── ثبت فرم‌ها در TFORMS ─────────────────────────
   نام‌ها دقیقاً باید با Shared/Constants/CostForms.cs یکی باشند — همان
   جدولی که Pay2AccessService/Pay2Authorize برای دسترسی می‌خواند. الگو
   عیناً از pay2_acl_migration.sql گرفته شده (GRP=10 برای این ماژول، تا
   با گروه ۹ که PAY2 استفاده می‌کند تداخل نکند).

   بدون این بخش، صفحهٔ مدیریت دسترسی هیچ ردیفی برای این ماژول نشان
   نمی‌دهد و وقتی AclEnforced روشن باشد هیچ‌کس نمی‌تواند به آن دسترسی
   بگیرد.
   ─────────────────────────────────────────────────────────────────────── */

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_DASHBOARD')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_DASHBOARD', N'داشبورد بستن ماه بهای تمام‌شده', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_RUN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_RUN', N'پیشرفت اجرای بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_EXCEPTIONS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_EXCEPTIONS', N'مغایرت‌های بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_VARIANCE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_VARIANCE', N'تصمیم انحراف', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_CONVERSION')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_CONVERSION', N'هزینه تبدیل', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_MARGIN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_MARGIN', N'سود و زیان کالا', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_HISTORY')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_HISTORY', N'سوابق اجراها', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_SETTINGS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_SETTINGS', N'تنظیمات بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_START')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_START', N'شروع اجرای بستن ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_AUTOFIX')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_AUTOFIX', N'اصلاح خودکار داده', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_RESOLVE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_RESOLVE', N'بستن استثنا', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_DECIDE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_DECIDE', N'ثبت تصمیم انحراف', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_APPLY_RATE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_APPLY_RATE', N'اعمال ضریب تعدیل', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_ROLLUP')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_ROLLUP', N'اجرای موتور نرخ', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_ROLLBACK')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_ROLLBACK', N'بازگردانی از اسنپ‌شات', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_APPROVE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_APPROVE', N'تأیید نهایی و قفل ماه', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_EXPORT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_EXPORT', N'خروجی اکسل', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_REBUILD_DOCS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_REBUILD_DOCS', N'بازسازی سند حواله خروج مواد', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_POST_CORRECTION')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_POST_CORRECTION', N'سند اصلاحی مغایرت کارت انبار/حسابداری', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

-- ⚠️ اصلاح (تأیید کاربر): این دو قبلاً زیرِ Pay2Perm.Upd روی ACT_RESOLVE
-- بودند و هرگز از صفحه‌ی «عملیات حساس» (که فقط یک تیکِ Run دارد) قابل‌اعطا
-- نبودند. حالا فرمِ مستقلِ خودشان را دارند — نگاه کنید CostForms.cs.
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_RESOLVE_PERMANENT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_RESOLVE_PERMANENT', N'پذیرش دائمی مغایرت', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'COST_ACT_FIX_DATE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'COST_ACT_FIX_DATE', N'اصلاح تاریخ مغایرِ سند', 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());

PRINT N'فرم‌های ماژول بستن ماه بهای تمام‌شده در TFORMS ثبت شدند.';
GO

PRINT N'داده اوليه ثبت شد.';

SELECT RuleCode AS کد, RuleName AS قاعده, StepCode AS گام,
       CASE DefaultSeverity WHEN 2 THEN N'مسدودکننده' ELSE N'هشدار' END AS شدت
FROM   dbo.CC_CheckRule ORDER BY SortOrder;

SELECT u.UnitName AS واحد, a.Anbar AS انبار,
       CASE a.AnbarRole WHEN 1 THEN N'مبناي انحراف' WHEN 2 THEN N'مواد اوليه'
                        WHEN 3 THEN N'محصول' ELSE N'ساير' END AS نقش,
       CASE a.DoStockCount WHEN 1 THEN N'بله' ELSE N'خير' END AS انبارگرداني
FROM   dbo.CC_Unit u JOIN dbo.CC_UnitAnbar a ON a.UnitId = u.UnitId
ORDER BY u.SeqNo, a.SeqNo;
GO
";
            TryExecuteCostCloseBatch(db, seedData,
                "قواعد تشخیص و واحدهای تولیدی",
                "اسکریپت 11-seed-data.sql را اجرا کنید (به CC_CheckRule و CC_Unit نیاز دارد).");

            // --- 12-procedures-phase1.sql ---
            string phase1Procs = @"
/* ═══════════════════════════════════════════════════════════════════
   فاز ۱ — فایل ۳ از ۳ : رویه‌ها

   مدیریت اجرا، اسنپ‌شات و بازگردانی، و گام‌های S00 تا S04.
   قابل اجرای مکرر (همه با CREATE OR ALTER).

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، رویه‌هایی که به CC_ItemCost/CC_ItemMargin می‌نویسند
-- (ستون‌های محاسباتی PERSISTED) با خطای 1934 شکست می‌خورند.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════ مدیریت اجرا ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_RunCreate
    @FiscalYear SMALLINT,
    @Month      TINYINT,
    @DateFrom   BIGINT,
    @DateTo     BIGINT,
    @RunKind    TINYINT,          -- 1=آزمايشي 2=قطعي
    @UserName   NVARCHAR(50),
    @Note       NVARCHAR(500) = NULL,
    @RunId      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
                 AND RunKind = 2 AND Status = 3 AND ApprovedAtUtc IS NOT NULL)
    BEGIN
        RAISERROR(N'براي اين ماه يک اجراي قطعي تأييدشده وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month AND Status = 1)
    BEGIN
        RAISERROR(N'يک اجرا براي اين ماه در حال انجام است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @no SMALLINT =
        ISNULL((SELECT MAX(RunNo) FROM dbo.CC_Run
                WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month), 0) + 1;

    DECLARE @prev INT =
        (SELECT TOP 1 RunId FROM dbo.CC_Run
         WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month
         ORDER BY RunNo DESC);

    UPDATE dbo.CC_Run SET IsLatest = 0
    WHERE FiscalYear = @FiscalYear AND PeriodMonth = @Month;

    INSERT dbo.CC_Run (FiscalYear, PeriodMonth, DateFrom, DateTo, RunNo,
                       PrevRunId, IsLatest, RunKind, Status, StartedByUser, Note)
    VALUES (@FiscalYear, @Month, @DateFrom, @DateTo, @no,
            @prev, 1, @RunKind, 0, @UserName, @Note);

    SET @RunId = SCOPE_IDENTITY();

    INSERT dbo.CC_RunLog (RunId, Severity, Message)
    VALUES (@RunId, 1, CONCAT(N'اجراي شماره ', @no, N' براي دوره ',
                              @FiscalYear, '/', @Month, N' ايجاد شد'));

    COMMIT;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepStart
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Title    NVARCHAR(120),
    @SeqNo    SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    -- INT و نه TINYINT — نگاه کنید توضیح ستون Attempt در 10-schema.sql.
    -- با TINYINT، رسیدن شمارنده به ۲۵۵ باعث می‌شد این عبارت سرریز کند و
    -- کل اجرا با خطای «Arithmetic overflow ... value = 256» متوقف شود.
    DECLARE @try INT =
        ISNULL((SELECT MAX(Attempt) FROM dbo.CC_RunStep
                WHERE RunId = @RunId AND StepCode = @StepCode), 0) + 1;

    INSERT dbo.CC_RunStep (RunId, StepCode, StepTitle, SeqNo, Attempt, Status, StartedAtUtc)
    VALUES (@RunId, @StepCode, @Title, @SeqNo, @try, 1, SYSUTCDATETIME());

    UPDATE dbo.CC_Run
       SET Status = 1, StartedAtUtc = ISNULL(StartedAtUtc, SYSUTCDATETIME())
     WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_StepFinish
    @RunId     INT,
    @StepCode  VARCHAR(10),
    @Status    TINYINT,                     -- 2=موفق 3=هشدار 4=خطا 5=رد‌شده
    @Rows      INT           = NULL,
    @Result    NVARCHAR(MAX) = NULL,
    @Error     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE  s
       SET  s.Status        = @Status,
            s.FinishedAtUtc = SYSUTCDATETIME(),
            s.DurationMs    = DATEDIFF(MILLISECOND, s.StartedAtUtc, SYSUTCDATETIME()),
            s.RowsAffected  = @Rows,
            s.ResultJson    = @Result,
            s.ErrorMessage  = @Error
    FROM    dbo.CC_RunStep s
    JOIN   (SELECT RunId, StepCode, MAX(Attempt) AS Attempt
            FROM   dbo.CC_RunStep
            WHERE  RunId = @RunId AND StepCode = @StepCode
            GROUP BY RunId, StepCode) x
           ON x.RunId = s.RunId AND x.StepCode = s.StepCode AND x.Attempt = s.Attempt;

    IF @Status = 4
        UPDATE dbo.CC_Run SET Status = 4 WHERE RunId = @RunId;
END
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_SetFormulasDirty
    @RunId INT, @Dirty BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.CC_Run SET FormulasDirty = @Dirty WHERE RunId = @RunId;
END
GO


/* ═══════════════ اسنپ‌شات و بازگردانی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_Snapshot
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Month    TINYINT,
    @DT1      BIGINT,
    @DT2      BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @bak SYSNAME, @sql NVARCHAR(MAX), @n INT;

    ---- DTL_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_DTL_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT d.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DTL_MANF d
                 JOIN dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DTL_MANF', @bak, @n);

    ---- HEAD_MANF : فقط فرمول‌هاي ماه
    SET @bak = CONCAT('CC_BAK_HEAD_MANF_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT h.* INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.HEAD_MANF h WHERE h.GHEYMAT = @m';
    EXEC sp_executesql @sql, N'@m TINYINT', @m = @Month;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'HEAD_MANF', @bak, @n);

    ---- DEED_HED : اسنپ‌شات کامل اسناد بازه، به‌همراه اسناد پس از @DT2 هم —
    -- چون شاخهٔ جابه‌جايي CC_sp_S04_SortDeeds مي‌تواند شمارهٔ اسناد بعد از
    -- پايان ماه را هم عوض کند تا با شمارهٔ تازهٔ اسناد اين ماه تلاقي نکند؛
    -- اگر آن اسناد اينجا اسنپ‌شات نشوند، Rollback راهي براي برگرداندن
    -- شماره‌شان ندارد. ستون‌ها هم کامل ذخيره مي‌شوند (نه فقط base/N_S/DATE_S)
    -- تا اگر CC_sp_S03_DeleteEmptyDeeds سندي را کامل حذف کرد، Rollback
    -- بتواند کل سطر را دوباره درج کند، نه فقط شماره‌اش را برگرداند.
    SET @bak = CONCAT('CC_BAK_DEED_HED_R', @RunId, '_', @StepCode);
    IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
        EXEC('DROP TABLE dbo.' + @bak);
    SET @sql = N'SELECT * INTO dbo.' + QUOTENAME(@bak) + N'
                 FROM dbo.DEED_HED WHERE DATE_S >= @a';
    EXEC sp_executesql @sql, N'@a BIGINT', @a = @DT1;
    SET @n = @@ROWCOUNT;
    INSERT dbo.CC_Snapshot (RunId, StepCode, TableName, BackupTable, RowsCopied)
    VALUES (@RunId, @StepCode, 'DEED_HED', @bak, @n);

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 1, N'اسنپ‌شات گرفته شد');

    SELECT TableName AS جدول, BackupTable AS جدول_پشتيبان, RowsCopied AS تعداد_سطر
    FROM   dbo.CC_Snapshot
    WHERE  RunId = @RunId AND StepCode = @StepCode;
END
GO


/* ═══════════════ S00 — بازبینی ابتدای ماه ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S00_Preflight
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  StepCode = 'S00' AND ISNULL(RunId, -1) = ISNULL(@RunId, -1);

    ---- CHK-03 : فرمول بدون نرخ جذب
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-03', 9, r.DefaultSeverity,
            MIN(CAST(pl.CODE AS BIGINT)), hm.FNUMB, MAX(h.DATE_N), SUM(pl.MEGHK),
            CONCAT(N'فرمول ', hm.FNUMB, N' نرخ جذب هزينه تبديل ندارد')
    FROM    dbo.HEAD_LST  h
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
    CROSS   JOIN dbo.CC_CheckRule r
    WHERE   r.RuleCode = 'CHK-03' AND r.IsActive = 1
      AND   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(hm.IMBIBE_MANF,0) + ISNULL(hm.IMBIBE_SAR,0) = 0
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-03' AND ae.IsActive = 1
                          AND (ae.Code IS NULL
                               OR ae.Code = CAST(pl.CODE AS BIGINT)))
    GROUP BY hm.FNUMB, r.DefaultSeverity;

    ---- CHK-04 : کالاي توليدشده بدون فرمول ماه
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-04', 12, 2, CAST(pl.CODE AS BIGINT),
            N'کالا در اين ماه توليد شده ولي فرمول ماه را ندارد'
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                          AND hm.GHEYMAT = @Month);

    ---- CHK-05 : ماده بدون منبع نرخ
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  DISTINCT @RunId, 'S00', 'CHK-05', 4, 1, CAST(d.CODE AS BIGINT),
            N'ماده بدون منبع نرخ — نرخ صفر به کالاهاي بالادست منتقل مي‌شود'
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF p
                        WHERE CAST(p.CODE AS BIGINT) = CAST(d.CODE AS BIGINT)
                          AND p.GHEYMAT = @Month)
      AND   NOT EXISTS (SELECT 1 FROM dbo.KALAS k
                        WHERE k.code = CAST(d.CODE AS BIGINT)
                          AND k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0);

    ---- CHK-15 : فرمول با مقدار منفی
    -- مقدار منفی در فرمول یعنی مانده حساب کالای در جریان ساخت (۷۵۱) هرگز
    -- متوازن نمی‌شود (CHK-07)؛ چون خروج مواد از روی همین عدد بازتولید
    -- می‌شود. کد سطر (DTL_MANF.id) در DocNumber ذخیره می‌شود تا اصلاح
    -- خودکار دقیقاً همان سطر را هدف بگیرد.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-15', 17, 2, CAST(d.CODE AS BIGINT),
            CAST(d.id AS INT), d.MEGH,
            CONCAT(N'فرمول ', h.FNUMB, N' مقدار منفی دارد: ', d.MEGH)
    FROM    dbo.DTL_MANF  d
    JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB AND h.GHEYMAT = @Month
    WHERE   d.MEGH < 0 OR d.MEGHk < 0;

    ---- CHK-16 : برگه تولید به انباري که به هيچ واحد توليدي (نقش «محصول»)
    -- وصل نيست — بدون اين تشخيص، S10 اين برگه‌ها را در محاسبه جذب هيچ
    -- واحدي نمي‌بيند و مانده حساب ۷۵۱ کاذب مي‌شود (دقيقاً همان چيزي که
    -- روي انبار ۱۵ رخ داد و کاربر تأييد کرد بايد به‌صورت خودکار
    -- روي هر پايگاه‌داده‌ي جديد هم چک شود).
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocNumber, DocDate, Description)
    SELECT DISTINCT @RunId, 'S00', 'CHK-16', 18, 1,
           TRY_CAST(pl.CODE AS BIGINT), h.NUMBER, h.DATE_N,
           CONCAT(N'برگه تولید شماره ', h.NUMBER, N' به انبار ', pl.ANBAR,
                  N' وارد شده که به هیچ واحد تولیدی (نقش «محصول») وصل نیست')
    FROM   dbo.HEAD_LST h
    JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND  pl.ANBAR IS NOT NULL
      AND  NOT EXISTS (SELECT 1 FROM dbo.CC_UnitAnbar ua
                        JOIN dbo.CC_Unit u ON u.UnitId = ua.UnitId
                        WHERE ua.Anbar = pl.ANBAR AND ua.AnbarRole = 3 AND u.IsActive = 1);

    ---- CHK-17 : شمارش دوم/سوم انبارگردانی بدون مغایرت شمارش اول
    -- طبق فرآیند واقعی انبارگردانی (تأیید کاربر): کالایی که شمارش اول
    -- (NUM1) آن با موجودی سیستم (MOG) برابر است، اصلاً نباید وارد دور
    -- دوم/سوم شمارش شود؛ NUM2/NUM3 فقط برای کالاهایی پر می‌شود که شمارش
    -- اول‌شان مغایرت داشته. اگر با این حال NUM2 یا NUM3 مقدار داشته باشد،
    -- یعنی عدد در ستون اشتباهی ثبت شده — دقیقاً همان چیزی که روی کد
    -- ۳۷۴ (شیر خام)، برگه انبارگردانی ۱۰۴ پیدا شد: MOG=0، NUM1=0 (بدون
    -- مغایرت)، ولی NUM3=29633 — این عدد از راه (MOG-NUM3) وارد موتور نرخ
    -- می‌شود و مقدار پایان‌دوره‌ی کالا را در همان انبار به‌کلی غلط می‌کند.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Anbar, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-17', 19, 2,
            TRY_CAST(al.CODE AS BIGINT), ah.GRD_ANBAR, ah.GRD_NUM, ah.GRD_DATE,
            CASE WHEN ISNULL(al.NUM3,0) <> 0 THEN al.NUM3 ELSE al.NUM2 END,
            CONCAT(N'برگه انبارگردانی ', ah.GRD_NUM, N' / انبار ', ah.GRD_ANBAR,
                   N': شمارش اول (', al.NUM1, N') با موجودی سیستم (', al.MOG,
                   N') برابر بوده ولی شمارش ', CASE WHEN ISNULL(al.NUM3,0) <> 0 THEN N'سوم' ELSE N'دوم' END,
                   N' مقدار دارد (', CASE WHEN ISNULL(al.NUM3,0) <> 0 THEN al.NUM3 ELSE al.NUM2 END,
                   N') — احتمالاً در ستون اشتباه ثبت شده.')
    FROM    dbo.ANBGRD_LST  al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.GRD_DATE BETWEEN @DT1 AND @DT2
      AND   ah.N_S IS NOT NULL
      AND   al.NUM1 IS NOT NULL AND al.NUM1 = al.MOG
      AND   (ISNULL(al.NUM2,0) <> 0 OR ISNULL(al.NUM3,0) <> 0)
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-17' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = ah.GRD_ANBAR)
                          AND (ae.Code  IS NULL OR ae.Code  = TRY_CAST(al.CODE AS BIGINT)));

    /* ─── CHK-18 : فاکتور فروش در ماهی متفاوت از حواله انبارش ───
       طبق دستور کاربر (پیدا شده از راه فاکتور فروش ۲۴۶۵/کد ۳۴۰۲: تاریخ
       فاکتور ۲۸/۲ ولی حواله‌ی انبار ۲۰/۳): وقتی فاکتور فروش و حواله‌ی
       انبارِ همان فاکتور در دو ماهِ شمسیِ متفاوت ثبت شده‌اند، معلوم
       نیست کدام درست است — باید اپراتور تصمیم بگیرد، نه بازسازی خودکار.

       ⚠️ معیار ابتدا «بیش از ۳۰ روز فاصله» بود، ولی نمونه‌ی محرکِ همین
       قاعده (فاکتور ۲۴۶۵) فقط ۲۳ روز واقعی فاصله دارد (۲۸ اردیبهشت تا
       ۲۰ خرداد) — چون این دو تاریخ درست کنارِ مرز ماه افتاده‌اند، نه
       چون فاصله‌ی زیادی دارند. معیار درست، طبق تأیید کاربر، «ماهِ شمسیِ
       متفاوت» است، نه شمارشِ روز — دقیقاً همان چیزی که برای بستنِ ماه
       اهمیت دارد (کدام ماهِ حسابداری صاحبِ این سند است). DATE_N به‌صورت
       عدد فشرده‌ی YYYYMMDD ذخیره می‌شود، پس DATE_N/100 دقیقاً YYYYMM
       (سال+ماه) را می‌دهد — تقسیم صحیح، بدون نیاز به تبدیل تقویم.

       ⚠️ دومین اصلاح (بعد از تأیید کاربر): برگشت فروش/خرید عمداً حذف
       شد. تصور اولیه این بود که تاریخ برگشت هم باید نزدیک تاریخ سند
       اصلی باشد — غلط بود. کاربر توضیح داد: «برگشت فروش‌های مستقیم که
       یعنی مستقیماً از حواله فروش استفاده می‌کنند تاریخشان ربطی به
       تاریخ حواله ندارد» — مشتری هر وقت جنس را برگرداند برمی‌گرداند،
       ماه‌ها بعد از خرید هم کاملاً طبیعی است؛ این قاعده برای سنجش‌شان
       غلط بود و روی نمونه‌ی واقعی (برگشت ۵: برگشت ۱۶/۲ برای فروش ۳۱/۱)
       مغایرت کاذب ساخت.

       RefList حاوی یک JSON کوچک است («سند الف»/«سند ب» و جدول/شماره/برچسبِ
       هرکدام) تا دکمه‌ی اصلاح بتواند دقیقاً بفهمد کدام ردیف از کدام جدول
       را باید به تاریخ دیگری تغییر دهد. خرید (TAG=۱) عمداً اینجا نیست:
       بر خلاف فروش، اینجا فاکتور خرید و رسید انبار یک سند واحدند (یک
       تاریخ)، نه دو سند جدا برای مقایسه. */
    ;WITH DateDrift AS (
        -- فاکتور فروش (TAG=13) در برابر حواله انبار فروش (TAG=2)
        -- ⚠️ NUMBER در HEAD_LST از نوع FLOAT است؛ بدون CAST به BIGINT، FOR
        -- JSON PATH پایین‌تر آن را به نماد علمی (مثلاً «۲.۴۶۵e+۳») می‌نویسد
        -- که در سمت C# به‌عنوان long قابل‌خواندن نیست.
        SELECT  N'sale' AS Kind,
                CAST(inv.NUMBER AS BIGINT) AS ANumber, 13 AS ATag, N'HEAD_LST' AS ATable, inv.DATE_N AS ADate,
                CAST(vch.NUMBER AS BIGINT) AS BNumber, 2  AS BTag, N'HEAD_LST' AS BTable, vch.DATE_N AS BDate,
                CONCAT(N'فاکتور فروش ', inv.NUMBER, N': تاریخ فاکتور ',
                       FORMAT(inv.DATE_N,'0000/00/00'), N' با تاریخ حواله انبار ',
                       FORMAT(vch.DATE_N,'0000/00/00'), N' در ماه متفاوتی ثبت شده‌اند')
        AS Description,
                CASE WHEN inv.DATE_N/100 <> vch.DATE_N/100 THEN 1 ELSE 0 END AS DifferentMonth
        FROM    dbo.HEAD_LST inv
        JOIN    dbo.HEAD_LST vch ON vch.NUMBER = inv.NUMBER AND vch.TAG = 2
        WHERE   inv.TAG = 13
          AND   (inv.DATE_N BETWEEN @DT1 AND @DT2 OR vch.DATE_N BETWEEN @DT1 AND @DT2)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, DocNumber, DocTag, DocDate, Amount, RefList, Description)
    SELECT  @RunId, 'S00', 'CHK-18', 20, 1,
            d.ANumber, d.ATag, d.ADate, d.BDate,
            (SELECT d.Kind AS kind,
                    d.ANumber AS aNumber, d.ATag AS aTag, d.ATable AS aTable, d.ADate AS aDate,
                    d.BNumber AS bNumber, d.BTag AS bTag, d.BTable AS bTable, d.BDate AS bDate
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            d.Description
    FROM    DateDrift d
    WHERE   d.DifferentMonth = 1
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-18' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL) AND (ae.Code IS NULL));

    /* ─── CHK-19 : تاریخ فاکتور فروش با تاریخ سند حسابداری‌اش دقیقاً یکی نیست ───
       پیدا شده وقتی CHK-18 (فاکتور ۲۴۶۵) با دکمه‌ی «اصلاح تاریخ» درست شد:
       تاریخ فاکتور (HEAD_LST/TAG=13) و حواله انبار (TAG=2) با هم یکی
       شدند (هر دو ۲۰/۳)، ولی خودِ سند حسابداریِ پست‌شده (DEED_HED،
       از راه DEED_DTL.NUMBER=فاکتور و TAG=13) هنوز تاریخ قدیم را داشت
       (۲۸/۲) — چون اصلاح CHK-18 فقط HEAD_LST/BACK_HEAD را می‌نویسد، نه
       سند حسابداری را. طبق دستور کاربر این دو باید «دقیقاً یکی» باشند،
       نه فقط هم‌ماه — همان آستانه‌ی یک‌ریالی/بدون‌اغماضِ CHK-02، اینجا
       روی روز.

       ⚠️ DISTINCT لازم است: یک فاکتور معمولاً چند ردیفِ DEED_DTL دارد
       (بستانکار مشتری، بستانکار فروش، بدهکار/بستانکار بهای تمام‌شده،
       …) که همه زیر همان یک N_S/TAG=13 هستند — بدون DISTINCT، همان یک
       فاکتور به تعداد ردیف‌هایش (مثلاً ۴ بار) تکراری درج می‌شد. */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, DocNumber, DocTag, DocDate, Amount, RefList, Description)
    SELECT  DISTINCT
            @RunId, 'S00', 'CHK-19', 21, 1,
            CAST(inv.NUMBER AS BIGINT), 13, inv.DATE_N, h.DATE_S,
            (SELECT N'invoiceVsAccounting' AS kind,
                    CAST(inv.NUMBER AS BIGINT) AS aNumber, 13 AS aTag, N'HEAD_LST' AS aTable, inv.DATE_N AS aDate,
                    CAST(d.N_S AS BIGINT) AS bNumber, 0 AS bTag, N'DEED_HED' AS bTable, h.DATE_S AS bDate
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            CONCAT(N'فاکتور فروش ', inv.NUMBER, N': تاریخ فاکتور ',
                   FORMAT(inv.DATE_N,'0000/00/00'), N' با تاریخ سند حسابداری ',
                   FORMAT(h.DATE_S,'0000/00/00'), N' (سند ', d.N_S, N') یکی نیست')
    FROM    dbo.HEAD_LST inv
    JOIN    dbo.DEED_DTL d ON d.NUMBER = inv.NUMBER AND d.TAG = 13
    JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
    WHERE   inv.TAG = 13
      AND   (inv.DATE_N BETWEEN @DT1 AND @DT2 OR h.DATE_S BETWEEN @DT1 AND @DT2)
      AND   inv.DATE_N <> h.DATE_S
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-19' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL) AND (ae.Code IS NULL));

    /* ─── CHK-21 : تاریخ برگشت فروش با تاریخ سند حسابداری‌اش یکی نیست ───
       پیدا شده روی کد ۳۵۱۰ / انبار ۸۱۳: حواله‌ی برگشت فروش شماره ۳۲۱
       (HEAD_LST.TAG=24) تاریخِ ۱۴۰۵/۰۱/۲۳ دارد، ولی سندِ حسابداریِ همان
       برگشت با تاریخِ ۱۴۰۵/۰۲/۲۳ پست شده — یک ماه دیرتر. نتیجه: کاردکس
       این حواله را جزوِ فروردین حساب کرد (چون تاریخِ خودِ حواله را
       می‌بیند) ولی حسابداری اصلاً در فروردین دیده نمی‌شد — CHK-02 یک
       مغایرتِ ۳۳,۸۱۰,۰۰۰ ریالی نشان داد.

       دقیقاً همان الگوی CHK-19 (فاکتور فروش TAG=13 در برابر سندش)، ولی
       CHK-19 برگشتِ فروش را پوشش نمی‌دهد. تفاوتِ مهم: برخلافِ فاکتورِ
       فروش که زیرِ همان TAG=13 در DEED_DTL هم پست می‌شود، سندِ
       حسابداریِ برگشتِ فروش زیرِ TAG=25 پست می‌شود، نه TAG=24 — تأییدشده
       با دادهٔ واقعی (SaleReturnRebuildService.RunPass2Async، همان
       تفکیکِ TAG=24/25 که در §2.3 مستندِ هم‌ترازیِ AUTO_BAZ آمده). */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, DocNumber, DocTag, DocDate, Amount, RefList, Description)
    SELECT  DISTINCT
            @RunId, 'S00', 'CHK-21', 23, 1,
            CAST(inv.NUMBER AS BIGINT), 24, inv.DATE_N, h.DATE_S,
            (SELECT N'saleReturnVsAccounting' AS kind,
                    CAST(inv.NUMBER AS BIGINT) AS aNumber, 24 AS aTag, N'HEAD_LST' AS aTable, inv.DATE_N AS aDate,
                    CAST(d.N_S AS BIGINT) AS bNumber, 0 AS bTag, N'DEED_HED' AS bTable, h.DATE_S AS bDate
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            CONCAT(N'برگشت فروش ', inv.NUMBER, N': تاریخ حواله ',
                   FORMAT(inv.DATE_N,'0000/00/00'), N' با تاریخ سند حسابداری ',
                   FORMAT(h.DATE_S,'0000/00/00'), N' (سند ', d.N_S, N') یکی نیست')
    FROM    dbo.HEAD_LST inv
    JOIN    dbo.DEED_DTL d ON d.NUMBER = inv.NUMBER AND d.TAG = 25
    JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
    WHERE   inv.TAG = 24
      AND   (inv.DATE_N BETWEEN @DT1 AND @DT2 OR h.DATE_S BETWEEN @DT1 AND @DT2)
      AND   inv.DATE_N <> h.DATE_S
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-21' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL) AND (ae.Code IS NULL));

    /* ─── CHK-21 (بخش دوم) : تاریخ حواله برگشت با تاریخ سربرگ خودش یکی نیست ───
       بخش اول بالا حواله (TAG=24) را با *سند حسابداری* مقایسه می‌کند و برای
       آن به DEED_DTL جوین می‌زند. ولی وقتی سند حسابداری اصلاً صادر نشده،
       آن جوین هیچ سطری نمی‌دهد و کنترل بی‌صدا رد می‌شود — دقیقاً همان
       حالتی که مغایرت را می‌سازد.

       نمونه‌ی واقعی (کد ۳۵۱۰ / انبار ۸۱۳ / فروردین ۱۴۰۵): سند ۳۲۱ در
       HEAD_LST دو تاریخ دارد — قلم کالا (TAG=24) به تاریخ ۱۴۰۵/۰۱/۲۳ و
       سربرگ (TAG=25) به تاریخ ۱۴۰۵/۰۲/۲۳. کاردکس تاریخِ TAG=24 را می‌بیند
       پس حرکت را در فروردین می‌شمارد، ولی SaleReturnRebuildService سند را
       از سربرگ TAG=25 می‌سازد که خارج از دوره است — پس هیچ سندی صادر
       نشد (DEED_DTL برای این شماره صفر ردیف دارد) و CHK-02 مغایرت
       ۳۳,۸۱۰,۰۰۰ ریالی نشان داد.

       این بخش ناسازگاری را یک مرحله زودتر می‌گیرد: مقایسه‌ی دو تاریخِ
       خودِ HEAD_LST، بدون هیچ وابستگی به اینکه سند صادر شده باشد یا نه. */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, DocNumber, DocTag, DocDate, Amount, RefList, Description)
    SELECT  DISTINCT
            @RunId, 'S00', 'CHK-21', 23, 1,
            CAST(h24.NUMBER AS BIGINT), 24, h24.DATE_N,
            (SELECT SUM(L.MABL_K) FROM dbo.INVO_LST L
             WHERE L.NUMBER = h24.NUMBER AND L.TAG = 24),
            (SELECT N'saleReturnHeaderDates' AS kind,
                    CAST(h24.NUMBER AS BIGINT) AS aNumber, 24 AS aTag, N'HEAD_LST' AS aTable, h24.DATE_N AS aDate,
                    CAST(h25.NUMBER AS BIGINT) AS bNumber, 25 AS bTag, N'HEAD_LST' AS bTable, h25.DATE_N AS bDate
             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            CONCAT(N'برگشت فروش ', h24.NUMBER, N': تاریخ قلم کالا ',
                   FORMAT(h24.DATE_N,'0000/00/00'), N' با تاریخ سربرگ ',
                   FORMAT(h25.DATE_N,'0000/00/00'),
                   N' یکی نیست — کاردکس از تاریخ قلم و سند حسابداری از تاریخ سربرگ ساخته می‌شود',
                   CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.DEED_DTL dd
                                         WHERE dd.NUMBER = h24.NUMBER AND dd.TAG = 25)
                        THEN N' (تا این لحظه هیچ سند حسابداری برای آن صادر نشده)'
                        ELSE N'' END)
    FROM    dbo.HEAD_LST h24
    JOIN    dbo.HEAD_LST h25 ON h25.NUMBER = h24.NUMBER AND h25.TAG = 25
    WHERE   h24.TAG = 24
      AND   (h24.DATE_N BETWEEN @DT1 AND @DT2 OR h25.DATE_N BETWEEN @DT1 AND @DT2)
      AND   h24.DATE_N <> h25.DATE_N
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-21' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL) AND (ae.Code IS NULL));

    /* ─── CHK-20 : نرخ میانگین منفی ───
       پیدا شده روی کد ۳۴۶۱/انبار۱: فروش ۱۴۰۵/۰۲/۰۹ کاردکس را وقتی فقط
       ۰٫۴ واحد موجودی بود منفی کرد (۹۹٫۶-، همان مغایرتی که CHK-01 با
       DocDate=۱۴۰۵۰۲۰۹ گزارش می‌کند)؛ سطرِ اولِ انبارگردانیِ بعدی
       (سند ۲۰۰، ۱۴۰۵/۰۳/۱۰) همان مانده‌ی منفی را تصحیح کرد ولی با
       نرخ ۳,۶۷۰,۰۱۶- ثبت شد — یک «قیمت منفی»، بدون معنای اقتصادی،
       که از کجا آمده روشن نبود تا این مسیر دنبال شد.

       CHK-01 فقط خودِ مانده‌ی منفیِ ریشه را گزارش می‌کند؛ این‌جا
       پیامدِ نرخیِ آن را نشان می‌دهیم — نرخ منفی در دو منبع ممکن
       است ثبت شود: INVO_LST.AVRAGE/AVRAGE2 (سطرهای عادی کاردکس) یا
       ANBGRD_LST.MABL (سند انبارگردانی/شمارش فیزیکی). تأیید کاربر:
       فقط هشدار (Severity=1)، نه دروازه‌ی مسدودکننده — فعلاً فقط
       دیده شود، اصلاح دستی جداگانه‌ای در کار نیست. */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, DocNumber, DocTag, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-20', 22, 1,
            CASE WHEN il.TAG = 5 THEN CAST(il.ANBARF AS INT) ELSE il.ANBAR END,
            TRY_CAST(il.CODE AS BIGINT), CAST(il.NUMBER AS BIGINT), il.TAG, hl.DATE_N,
            CASE WHEN il.TAG = 5 THEN il.AVRAGE2 ELSE il.AVRAGE END,
            CONCAT(N'نرخ میانگین منفی: کد ', il.CODE, N'/انبار ',
                   CASE WHEN il.TAG = 5 THEN il.ANBARF ELSE il.ANBAR END,
                   N' در سند شماره ', il.NUMBER, N' مورخ ', FORMAT(hl.DATE_N, '0000/00/00'),
                   N' نرخ ', FORMAT(CASE WHEN il.TAG = 5 THEN il.AVRAGE2 ELSE il.AVRAGE END, 'N0'),
                   N' ثبت شده — معمولاً پیامد یک کاردکس منفی (CHK-01) در تاریخی نزدیک همین سند است.')
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   hl.DATE_N BETWEEN @DT1 AND @DT2
      AND   ((il.TAG IN (1, 7, 9, 24) AND il.AVRAGE < 0)
          OR (il.TAG = 5 AND il.ANBARF IS NOT NULL AND il.AVRAGE2 < 0))
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-20' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = CASE WHEN il.TAG = 5 THEN CAST(il.ANBARF AS INT) ELSE il.ANBAR END)
                          AND (ae.Code  IS NULL OR ae.Code  = TRY_CAST(il.CODE AS BIGINT)));

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, DocNumber, DocDate, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-20', 22, 1,
            ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT), ah.GRD_NUM, ah.GRD_DATE, al.MABL,
            CONCAT(N'نرخ میانگین منفی: کد ', al.CODE, N'/انبار ', ah.GRD_ANBAR,
                   N' در سند انبارگردانی شماره ', ah.GRD_NUM, N' مورخ ', FORMAT(ah.GRD_DATE, '0000/00/00'),
                   N' نرخ ', FORMAT(al.MABL, 'N0'),
                   N' ثبت شده — معمولاً پیامد یک کاردکس منفی (CHK-01) در تاریخی نزدیک همین سند است.')
    FROM    dbo.ANBGRD_LST al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.N_S IS NOT NULL
      AND   ah.GRD_DATE BETWEEN @DT1 AND @DT2
      AND   al.MABL < 0
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-20' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = ah.GRD_ANBAR)
                          AND (ae.Code  IS NULL OR ae.Code  = TRY_CAST(al.CODE AS BIGINT)));

    ---- CHK-06 : حلقه در ساختار فرمول
    IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
    SELECT DISTINCT CAST(h.CODE AS BIGINT) AS P, CAST(d.CODE AS BIGINT) AS C
    INTO   #E
    FROM   dbo.HEAD_MANF h
    JOIN   dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE  h.GHEYMAT = @Month AND h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND  CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);
    CREATE CLUSTERED INDEX IX_E ON #E(P, C);

    ;WITH W AS (
        SELECT P AS Root, C, 1 AS L,
               CAST('/' + CAST(P AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM   #E
        UNION ALL
        SELECT w.Root, e.C, w.L + 1,
               CAST(w.Pt + CAST(e.P AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM   W w JOIN #E e ON e.P = w.C
        WHERE  w.L < 20
          AND  w.Pt NOT LIKE '%/' + CAST(e.C AS VARCHAR(20)) + '/%'
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT DISTINCT @RunId, 'S00', 'CHK-06', 5, 2, Root,
           N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
    FROM   W WHERE C = Root
    OPTION (MAXRECURSION 0);

    DROP TABLE #E;

    ---- CHK-07 : مانده نامتوازن مواد در ۷۵۱ (آستانه نسبي)
    DECLARE @th FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-07'), 0.001);

    -- DocNumber عمداً پر نمی‌شود: این قاعده مانده یک کالا را در کل بازه بررسی
    -- می‌کند، نه یک سند مشخص را؛ ستون HES_M (کد معین حسابداری) شمارهٔ برگهٔ
    -- تولید نیست و نمایشش به کاربر گمراه‌کننده بود.
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-07', 13,
            CASE WHEN SUM(d.BED) = 0 OR SUM(d.BES) = 0 THEN 2 ELSE 1 END,
            TRY_CAST(d.HES_T AS BIGINT),
            SUM(d.BED) - SUM(d.BES),
            CASE WHEN SUM(d.BED) = 0
                 THEN N'ماده با توليد خارج شده ولي با حواله وارد نشده'
                 WHEN SUM(d.BES) = 0
                 THEN N'ماده با حواله وارد شده ولي با توليد خارج نشده'
                 ELSE N'مانده نامتوازن مواد در حساب کالاي در جريان ساخت' END
    FROM    dbo.DEED_DTL d
    JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
    WHERE   d.HES_K = 751 AND d.HES_T <> 99999999
      AND   hd.DATE_S BETWEEN @DT1 AND @DT2
    GROUP BY d.HES_M, d.HES_T
    HAVING  (SUM(d.BED) = 0 AND SUM(d.BES) <> 0)
         OR (SUM(d.BES) = 0 AND SUM(d.BED) <> 0)
         OR (ABS(SUM(d.BED) - SUM(d.BES))
             / NULLIF((SUM(d.BED) + SUM(d.BES)) / 2.0, 0) > @th);

    ---- CHK-09 : نرخ منتشرنشده نيمه‌ساخته
    --
    -- ⚠️ يک کالا مي‌تواند در همان ماه بيش از يک فرمول فعال داشته باشد
    -- (مثلاً روزهاي مختلف با ترکيب مواد متفاوت توليد شده باشد) — طبق تأييد
    -- صاحب پروژه، اين حالت طبيعي است، نه خطاي داده. نسخه‌ي قبلي اين چک هر
    -- (Code,FNUMB) را جدا با نرخ منتشرشده مقايسه مي‌کرد، در حالي‌که موتور
    -- نرخ (S11) فقط يک بهاي واحد به بالادست منتشر مي‌کند — نتيجه: فرمول‌هاي
    -- «غيرمنتخب» هميشه به‌عنوان مغايرت کاذب باقي مي‌ماندند، حتي بعد از
    -- بازسازي نرخ. حالا بهاي «خودِ» کالا ميانگين موزونِ بهاي همه‌ي
    -- فرمول‌هاي فعالش است، وزن‌دهي‌شده با مقدار واقعيِ توليدشده زيرِ هرکدام
    -- (از TAG=9 در همين بازه) — دقيقاً همان معياري که S11 هم استفاده مي‌کند.
    DECLARE @th9 FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-09'), 0.001);

    ;WITH FormulaCost AS (
        SELECT  hm.FNUMB, CAST(hm.CODE AS BIGINT) AS Code,
                SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                       + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha,
                ISNULL(p.Qty, 0) AS Qty
        FROM    dbo.HEAD_MANF hm
        JOIN    dbo.DTL_MANF  d ON d.FNUMB = hm.FNUMB
        CROSS   APPLY (
                    SELECT SUM(pl.MEGHk) AS Qty
                    FROM   dbo.HEAD_LST h
                    JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                    WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                      AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
                ) p
        WHERE   hm.GHEYMAT = @Month
        GROUP BY hm.FNUMB, CAST(hm.CODE AS BIGINT), p.Qty
    ),
    -- ⚠️ اصلاح (کشف‌شده روی کدهای ۲۷۳۵/۲۸۸۹ و ۲۲ کد نیمه‌ساخته‌ی دیگر):
    -- برای کالای نیمه‌ساخته‌ای که هم فرمول دارد هم همین ماه به‌عنوان
    -- ماده‌ی اولیه‌ی کالای دیگری از انبار حواله خورده (TAG=10)، S11
    -- عمداً میانگینِ واقعیِ انبار را جایگزینِ جمعِ فرمول می‌کند (نگاه کنید
    -- CC_sp_S11_PropagateRates, بخشِ «نرخ مواد خریدنی» — تأیید کاربر،
    -- دقیقاً همان چیزی که مغایرت حساب ۷۷۱ را رفع کرد). این چک قبلاً این
    -- override را نمی‌دانست، پس «بهای خودِ کالا» را همیشه از جمعِ فرمول
    -- حساب می‌کرد — درحالی‌که S11 مقدارِ دیگری (میانگینِ انبار) را منتشر
    -- کرده بود؛ نتیجه یک مغایرتِ کاذبِ دائمی بود که هیچ تعداد اجرای S11
    -- رفعش نمی‌کرد، چون خودِ معیارِ مقایسه اشتباه بود، نه همگرایی.
    KalasAvg AS (
        SELECT  CAST(k.CODE AS BIGINT) AS Code,
                SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS Nerkh
        FROM    dbo.KALAS k
        WHERE   k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
        GROUP BY CAST(k.CODE AS BIGINT)
    ),
    Khod AS (
        -- اگر هيچ‌کدام از فرمول‌هاي اين کالا در بازه توليد واقعي نداشتند
        -- (تعريف شده ولي هنوز مصرف نشده)، ميانگين ساده جايگزين وزن مي‌شود.
        SELECT  f.Code,
                COALESCE(ka.Nerkh,
                         CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Baha * f.Qty) / SUM(f.Qty)
                              ELSE AVG(f.Baha) END) AS Baha
        FROM    FormulaCost f
        LEFT    JOIN KalasAvg ka ON ka.Code = f.Code
        GROUP BY f.Code, ka.Nerkh
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code,
               AVG(d.SMABL) AS Nerkh, COUNT(DISTINCT d.FNUMB) AS Valedha
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S00', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            CONCAT(N'نرخ منتشر نشده — بهاي فرمول ', CAST(ROUND(k.Baha,0) AS BIGINT),
                   N' ولي نرخ در ', v.Valedha, N' فرمول بالادست ',
                   CAST(ROUND(v.Nerkh,0) AS BIGINT))
    FROM    Khod k
    JOIN    DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > @th9;

    ---- خلاصه
    SELECT  e.RuleCode AS قاعده, r.RuleName AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده' ELSE N'هشدار' END AS شدت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.StepCode = 'S00' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO


/* ═══════════════ S03 — حذف اسناد حسابداری خالی ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S03_DeleteEmptyDeeds
    @RunId INT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 1                  -- ۱ = فقط گزارش، بدون حذف
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Empty') IS NOT NULL DROP TABLE #Empty;

    -- «خالی» یعنی نه فقط بدون ردیف DEED_DTL، بلکه هیچ جدول دیگری هم به آن
    -- ارجاع ندهد. طبق sys.foreign_keys، هشت جدول به DEED_HED.N_S کلید
    -- خارجی دارند (DEED_DTL, HEAD_LST, ANBGRD_HEAD, CHKREC_H, CHREC_HP,
    -- WORKHEAD, MO_DTL, PGET_HED, HEAD_LST_TMP_WPF). سندی که هنوز از
    -- کاردکس انبار یا هرکدام دیگر ارجاع می‌شود واقعاً خالی نیست، حتی اگر
    -- DEED_DTL نداشته باشد — نباید حذفش کرد، و مطلقاً نباید ارجاع آن
    -- جدول‌ها را NULL کرد تا حذف زور بشود؛ آن ارجاع همان چیزی است که
    -- ردگیری سند حسابداری را از رکورد انبار ممکن می‌کند.
    SELECT h.N_S, h.DATE_S
    INTO   #Empty
    FROM   dbo.DEED_HED h
    WHERE  h.DATE_S BETWEEN @DT1 AND @DT2
      AND  NOT EXISTS (SELECT 1 FROM dbo.DEED_DTL    d WHERE d.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.HEAD_LST    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.ANBGRD_HEAD x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.CHKREC_H    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.CHREC_HP    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.WORKHEAD    x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.MO_DTL      x WHERE x.N_S = h.N_S)
      AND  NOT EXISTS (SELECT 1 FROM dbo.PGET_HED    x WHERE x.N_S = h.N_S);

    -- HEAD_LST_TMP_WPF ممکن است روی همهٔ نصب‌ها نباشد؛ اگر هست همان قاعده.
    IF OBJECT_ID('dbo.HEAD_LST_TMP_WPF', 'U') IS NOT NULL
        DELETE e FROM #Empty e
        WHERE EXISTS (SELECT 1 FROM dbo.HEAD_LST_TMP_WPF t WHERE t.N_S = e.N_S);

    DECLARE @n INT = (SELECT COUNT(*) FROM #Empty);

    IF @WhatIf = 1
    BEGIN
        SELECT N_S AS شماره_سند, DATE_S AS تاريخ FROM #Empty ORDER BY DATE_S, N_S;
        SELECT @n AS تعداد_سند_قابل_حذف, N'حالت گزارش — چيزي حذف نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DELETE h FROM dbo.DEED_HED h JOIN #Empty e ON e.N_S = h.N_S;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S03', 1, CONCAT(N'حذف اسناد خالي: ', @n, N' سند'),
            (SELECT N_S, DATE_S FROM #Empty FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S03_DeleteEmptyDeeds).
    -- Dapper روی نام‌مستعار فارسی نگاشت نمی‌کند و بی‌صدا صفر برمی‌گرداند؛
    -- شرح فارسی در CC_RunLog بالا ثبت شده است.
    SELECT @n AS Value;
END
GO


/* ═══════════════ S04 — مرتب‌سازی اسناد ═══════════════ */

CREATE OR ALTER PROCEDURE dbo.CC_sp_S04_SortDeeds
    @RunId     INT,
    @DT1       BIGINT,
    @DT2       BIGINT,
    @WholeYear BIT = 0,
    @WhatIf    BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    DECLARE @seed FLOAT =
        CASE WHEN @WholeYear = 1 THEN 0
             ELSE ISNULL((SELECT MAX(N_S) FROM dbo.DEED_HED WHERE DATE_S < @DT1), 0) END;

    -- کل جدول را می‌آوریم (نه فقط بازهٔ ماه) چون برای جلوگیری از تلاقی با
    -- اسناد ماه‌های بعدی باید بدانیم شمارهٔ فعلی‌شان چیست؛ اسناد بیرون بازه
    -- در ستون NewNS همان شمارهٔ فعلی خودشان را می‌گیرند (دست‌نخورده).
    SELECT  base,
            DATE_S,
            N_S AS OldNS,
            CASE WHEN @WholeYear = 1 OR DATE_S BETWEEN @DT1 AND @DT2
                 THEN @seed + ROW_NUMBER() OVER (
                          PARTITION BY CASE WHEN @WholeYear = 1
                                             OR DATE_S BETWEEN @DT1 AND @DT2
                                        THEN 1 ELSE 0 END
                          ORDER BY DATE_S ASC, N_S ASC)
                 ELSE N_S END AS NewNS
    INTO    #Map
    FROM    dbo.DEED_HED;

    -- اگر بازهٔ شمارهٔ جدید ماه جاری با شمارهٔ فعلی اولین سند ماه‌های بعدی
    -- تلاقی کند، همهٔ اسناد بعد از @DT2 را به یک اندازه جلو می‌بریم؛ چون
    -- همه با هم جابه‌جا می‌شوند، ترتیب و فاصلهٔ نسبی‌شان دست‌نخورده می‌ماند
    -- و تلاقی تازه‌ای ایجاد نمی‌شود.
    IF @WholeYear = 0
    BEGIN
        DECLARE @maxNewInMonth FLOAT =
            ISNULL((SELECT MAX(NewNS) FROM #Map WHERE DATE_S BETWEEN @DT1 AND @DT2), @seed);
        DECLARE @minAfterMonth FLOAT =
            ISNULL((SELECT MIN(OldNS) FROM #Map WHERE DATE_S > @DT2), 0);

        IF @minAfterMonth > 0 AND @maxNewInMonth >= @minAfterMonth
        BEGIN
            DECLARE @shift FLOAT = (@maxNewInMonth - @minAfterMonth) + 1;
            UPDATE #Map SET NewNS = OldNS + @shift WHERE DATE_S > @DT2;
        END
    END

    CREATE UNIQUE CLUSTERED INDEX IX_Map ON #Map(base);

    DECLARE @total INT   = (SELECT COUNT(*) FROM #Map);
    DECLARE @changed INT = (SELECT COUNT(*) FROM #Map WHERE OldNS <> NewNS);

    IF @WhatIf = 1
    BEGIN
        SELECT TOP 100 base, OldNS AS شماره_فعلي, NewNS AS شماره_جديد
        FROM   #Map WHERE OldNS <> NewNS ORDER BY NewNS;
        SELECT @total AS کل_اسناد, @changed AS تعداد_تغيير,
               N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- تريگرهاي Audit را فقط براي همين نشست کنار مي‌گذاريم
    EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

    -- ۹ جدول فرزند با ON UPDATE CASCADE خودکار به‌روز مي‌شوند.
    -- دو مرحله‌اي: چون شمارهٔ جدید یک سند می‌تواند برابر شمارهٔ فعلیِ سند
    -- دیگری باشد که هنوز عوض نشده (Shift یا جابه‌جایی داخل ماه)، یک
    -- UPDATE مستقیم وسط کار به PRIMARY KEY تکراری می‌خورد. اول همه را به
    -- یک بازهٔ منفیِ ناهم‌پوشان می‌بریم، بعد به مقدار نهایی.
    UPDATE  h
       SET  h.N_S = -1000000.0 - m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S <> m.NewNS;

    UPDATE  h
       SET  h.N_S = m.NewNS
    FROM    dbo.DEED_HED h
    JOIN    #Map m ON m.base = h.base
    WHERE   h.N_S < 0;

    EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S04', 1, N'بازشماره‌گذاري اسناد انجام شد',
            (SELECT @total AS total, @changed AS changed FOR JSON PATH));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (CoreSteps.cs / S04_SortDeeds).
    -- Value = تعداد اسناد بازشماره‌شده؛ شرح فارسی در CC_RunLog بالا ثبت شد.
    SELECT @changed AS Value, @total AS Total;
END
GO


PRINT N'رويه‌هاي فاز ۱ ايجاد شدند.';

SELECT  name AS رويه, create_date AS تاريخ_ايجاد, modify_date AS آخرين_تغيير
FROM    sys.procedures
WHERE   name LIKE 'CC[_]sp[_]%'
ORDER BY name;
GO
";
            TryExecuteCostCloseBatch(db, phase1Procs,
                "CC_sp_RunCreate، CC_sp_StepStart/Finish، CC_sp_Snapshot، S00/S03/S04",
                "اسکریپت 12-procedures-phase1.sql را اجرا کنید.");

            // --- 13-chk04-and-autofix.sql ---
            string chk04AutoFix = @"
/* ═══════════════════════════════════════════════════════════════════
   دو تغییر بر اساس درخواست کاربر

   ۱) CHK-04 حالا شماره برگه‌های تولید را هم می‌دهد، نه فقط کد کالا
   ۲) رویه اصلاح خودکار: فرمول همان ماه را به برگه‌ها نسبت می‌دهد

   قابل اجرای مکرر.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ستون جدید برای نگهداری فهرست برگه‌ها و امکان اصلاح خودکار */
IF COL_LENGTH('dbo.CC_Exception','RefList') IS NULL
    ALTER TABLE dbo.CC_Exception ADD RefList NVARCHAR(2000) NULL;
GO
IF COL_LENGTH('dbo.CC_Exception','CanAutoFix') IS NULL
    ALTER TABLE dbo.CC_Exception ADD CanAutoFix BIT NOT NULL DEFAULT 0;
GO
IF COL_LENGTH('dbo.CC_CheckRule','FixProcName') IS NULL
    ALTER TABLE dbo.CC_CheckRule ADD FixProcName SYSNAME NULL;
GO
IF COL_LENGTH('dbo.CC_CheckRule','FixButtonText') IS NULL
    ALTER TABLE dbo.CC_CheckRule ADD FixButtonText NVARCHAR(60) NULL;
GO

UPDATE dbo.CC_CheckRule
   SET FixProcName   = 'CC_sp_Fix_MissingFormula',
       FixButtonText = N'اصلاح خودکار برگه'
 WHERE RuleCode = 'CHK-04';
GO


/* ═══════════════════════════════════════════════════════════════════
   CHK-04 — نسخه‌ای که شماره برگه می‌دهد

   یک سطر به ازای هر کالا، با فهرست برگه‌های متأثر.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Chk04_MissingFormula
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @RunId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  RuleCode = 'CHK-04' AND ISNULL(RunId,-1) = ISNULL(@RunId,-1);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, DocTag,
         DocNumber, DocDate, Amount, RefList, CanAutoFix, Description)
    SELECT  @RunId, 'S00', 'CHK-04', 12, 2,
            CAST(pl.CODE AS BIGINT),
            9,
            MIN(h.NUMBER),                       -- اولين برگه
            MIN(h.DATE_N),
            SUM(pl.MEGHK),                       -- جمع مقدار توليد متأثر
            STRING_AGG(CAST(h.NUMBER AS VARCHAR(12)), ', ')
                WITHIN GROUP (ORDER BY h.NUMBER),
            -- اصلاح خودکار فقط وقتي ممکن است که فرمول ماه واقعاً وجود داشته باشد
            CASE WHEN EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                              WHERE CAST(hm.CODE AS BIGINT) = CAST(pl.CODE AS BIGINT)
                                AND hm.GHEYMAT = @Month)
                 THEN 1 ELSE 0 END,
            CONCAT(N'کالا در ', COUNT(DISTINCT h.NUMBER),
                   N' برگه توليد شده ولي فرمول ماه ', @Month, N' به آن نسبت داده نشده')
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (
                SELECT 1 FROM dbo.HEAD_MANF hm
                WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                  AND hm.GHEYMAT = @Month)
    GROUP BY CAST(pl.CODE AS BIGINT);

    SELECT  e.Code       AS کد_کالا,
            s.NAME       AS نام_کالا,
            e.Amount     AS جمع_مقدار_توليد,
            e.RefList    AS برگه_ها,
            CASE e.CanAutoFix WHEN 1 THEN N'بله' ELSE N'خير — فرمول ماه وجود ندارد' END
                         AS اصلاح_خودکار
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = e.Code
    WHERE   e.RuleCode = 'CHK-04' AND ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
    ORDER BY e.Amount DESC;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   اصلاح خودکار — دکمه‌ای که کاربر می‌زند

   فرمول همان ماه را پیدا و به برگه‌های تولید نسبت می‌دهد.
   @ExceptionId داده شود  → فقط همان یک کالا
   @ExceptionId خالی      → همه کالاهای قابل اصلاح

   @WhatIf = 1 پیش‌فرض است: فقط نشان می‌دهد چه چیزی تغییر خواهد کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_MissingFormula
    @Month       TINYINT,
    @DT1         BIGINT,
    @DT2         BIGINT,
    @RunId       INT           = NULL,
    @ExceptionId BIGINT        = NULL,
    @UserName    NVARCHAR(50)  = N'system',
    @WhatIf      BIT           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- کالاهاي هدف
    IF OBJECT_ID('tempdb..#Target') IS NOT NULL DROP TABLE #Target;
    CREATE TABLE #Target (Code BIGINT PRIMARY KEY);

    INSERT #Target(Code)
    SELECT DISTINCT e.Code
    FROM   dbo.CC_Exception e
    WHERE  e.RuleCode = 'CHK-04'
      AND  e.IsResolved = 0
      AND  e.CanAutoFix = 1
      AND  ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
      AND  (@ExceptionId IS NULL OR e.ExceptionId = @ExceptionId);

    ---- نگاشت کالا به فرمول ماه
    ---- اگر يک کالا چند فرمول در همان ماه داشته باشد، تازه‌ترين انتخاب مي‌شود
    IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;

    SELECT  t.Code,
            f.FNUMB,
            f.Chand
    INTO    #Map
    FROM    #Target t
    CROSS   APPLY (
                SELECT TOP 1
                       hm.FNUMB,
                       COUNT(*) OVER () AS Chand
                FROM   dbo.HEAD_MANF hm
                WHERE  CAST(hm.CODE AS BIGINT) = t.Code
                  AND  hm.GHEYMAT = @Month
                ORDER BY hm.DATE_ACTIV DESC, hm.FNUMB DESC
            ) f;

    ---- سطرهايي که تغيير خواهند کرد
    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- کليد تطبيق id است نه (NUMBER, RADIF): ستون RADIF در INVO_LST
    -- nullable است و روي داده‌ي واقعي مي‌تواند خالي باشد؛ آن‌وقت شرط
    -- «r.Radif = pl.RADIF» در UPDATE هرگز برقرار نمي‌شود (NULL = NULL
    -- در SQL نادرست است) و اصلاح خودکار بي‌صدا هيچ سطري را عوض
    -- نمي‌کند، درحالي‌که تعداد را گزارش مي‌دهد و استثنا را هم مي‌بندد.
    -- id کليد اصلي جدول است و اين حالت را کاملاً حذف مي‌کند.
    SELECT  pl.id               AS InvoId,
            h.NUMBER            AS ProdNo,
            h.DATE_N            AS ProdDate,
            pl.RADIF            AS Radif,
            CAST(pl.CODE AS BIGINT) AS Code,
            pl.N_KOL            AS OldFnumb,
            m.FNUMB             AS NewFnumb,
            pl.MEGHK            AS Meghdar,
            m.Chand             AS ChandFormul
    INTO    #Rows
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    #Map m ON m.Code = CAST(pl.CODE AS BIGINT)
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                          AND hm.GHEYMAT = @Month);

    ---- هشدار: کالايي که در ماه بيش از يک فرمول دارد نياز به انتخاب کاربر دارد
    IF EXISTS (SELECT 1 FROM #Rows WHERE ChandFormul > 1)
        SELECT DISTINCT
               r.Code AS کد_کالا, s.NAME AS نام_کالا, r.ChandFormul AS تعداد_فرمول_ماه,
               N'اين کالا در اين ماه بيش از يک فرمول دارد؛ تازه‌ترين انتخاب شد' AS هشدار
        FROM   #Rows r LEFT JOIN dbo.STUF_DEF s ON CAST(s.CODE AS BIGINT) = r.Code
        WHERE  r.ChandFormul > 1;

    DECLARE @n INT = (SELECT COUNT(*) FROM #Rows);

    IF @WhatIf = 1
    BEGIN
        SELECT  ProdNo    AS شماره_برگه,
                ProdDate  AS تاريخ,
                Code      AS کد_کالا,
                OldFnumb  AS فرمول_فعلي,
                NewFnumb  AS فرمول_جديد,
                Meghdar   AS مقدار
        FROM    #Rows
        ORDER BY ProdDate, ProdNo;

        SELECT @n AS تعداد_سطر_قابل_اصلاح, N'حالت گزارش — چيزي تغيير نکرد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    -- کدهايي که واقعاً عوض شدند را نگه مي‌داريم تا فقط استثناي همان‌ها
    -- بسته شود. اگر UPDATE به هر دليلي سطري را نگيرد، نبايد استثنا را
    -- «رفع‌شده» علامت بزنيم و عدد قابل‌اصلاح را به‌عنوان عدد اصلاح‌شده
    -- گزارش کنيم — کاربر بايد ببيند که کاري انجام نشده.
    DECLARE @Applied TABLE (Code BIGINT);

    UPDATE  pl
       SET  pl.N_KOL = r.NewFnumb
    OUTPUT  CAST(inserted.CODE AS BIGINT) INTO @Applied(Code)
    FROM    dbo.INVO_LST pl
    JOIN    #Rows r ON r.InvoId = pl.id
    WHERE   pl.TAG = 9;

    DECLARE @appliedRows INT = @@ROWCOUNT;

    ---- ثبت در سابقه
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S00', CASE WHEN @appliedRows = 0 AND @n > 0 THEN 2 ELSE 1 END,
            CASE WHEN @appliedRows = 0 AND @n > 0
                 THEN CONCAT(N'اصلاح خودکار هيچ سطري را عوض نکرد (', @n,
                             N' سطر نامزد بود) — توسط ', @UserName)
                 ELSE CONCAT(N'اصلاح خودکار فرمول برگه‌هاي توليد: ', @appliedRows,
                             N' سطر توسط ', @UserName) END,
            (SELECT ProdNo, Code, OldFnumb, NewFnumb FROM #Rows FOR JSON PATH);

    ---- استثناها بسته مي‌شوند — فقط براي کدهايي که واقعاً اصلاح شدند
    UPDATE  e
       SET  e.IsResolved     = 1,
            e.ResolvedBy     = @UserName,
            e.ResolvedAtUtc  = SYSUTCDATETIME(),
            e.ResolutionNote = N'اصلاح خودکار — فرمول ماه به برگه‌ها نسبت داده شد'
    FROM    dbo.CC_Exception e
    WHERE   e.RuleCode = 'CHK-04'
      AND   ISNULL(e.RunId,-1) = ISNULL(@RunId,-1)
      AND   EXISTS (SELECT 1 FROM @Applied a WHERE a.Code = e.Code);

    ---- خروج مواد بايد بازسازي شود، چون فرمول برگه عوض شد
    IF @RunId IS NOT NULL AND @appliedRows > 0
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT @appliedRows AS تعداد_سطر_اصلاح_شده, @n AS تعداد_سطر_نامزد;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   CHK-15 — اصلاح فرمول با مقدار منفی

   @ExceptionId الزامی است: هر سطر فرمول منفی جدا اصلاح می‌شود، نه گروهی،
   چون هر سطر می‌تواند تصمیم متفاوتی بخواهد (صفر یا حذف). شناسه سطر
   (DTL_MANF.id) در CC_Exception.DocNumber ذخیره شده — نگاه کنید به
   CC_sp_S00_Preflight بخش CHK-15.

   @Action = 'zero'   → مقدار (MEGH/MEGHk/MABLK/SMABL) صفر می‌شود، سطر می‌ماند
   @Action = 'delete' → کل سطر فرمول حذف می‌شود

   @WhatIf = 1 پیش‌فرض است: فقط نشان می‌دهد چه چیزی تغییر خواهد کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_NegativeFormulaQty
    @ExceptionId BIGINT,
    @Action      VARCHAR(10),
    @RunId       INT           = NULL,
    @UserName    NVARCHAR(50)  = N'system',
    @WhatIf      BIT           = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Action NOT IN ('zero', 'delete')
    BEGIN
        RAISERROR(N'مقدار @Action باید zero یا delete باشد.', 16, 1);
        RETURN;
    END

    DECLARE @DtlId BIGINT, @Code BIGINT;

    SELECT  @DtlId = e.DocNumber, @Code = e.Code
    FROM    dbo.CC_Exception e
    WHERE   e.ExceptionId = @ExceptionId AND e.RuleCode = 'CHK-15';

    IF @DtlId IS NULL
    BEGIN
        RAISERROR(N'این استثنا یافت نشد یا مربوط به CHK-15 نیست.', 16, 1);
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.DTL_MANF WHERE id = @DtlId)
    BEGIN
        -- سطر قبلاً حذف يا اصلاح شده — فقط استثنا را ببند
        IF @WhatIf = 0
            UPDATE dbo.CC_Exception
               SET IsResolved = 1, ResolvedBy = @UserName, ResolvedAtUtc = SYSUTCDATETIME(),
                   ResolutionNote = N'سطر فرمول از قبل اصلاح شده بود'
             WHERE ExceptionId = @ExceptionId;

        SELECT 0 AS تغيير_يافت, N'سطر فرمول از قبل اصلاح يا حذف شده بود' AS وضعيت;
        RETURN;
    END

    IF @WhatIf = 1
    BEGIN
        SELECT  d.id AS شناسه_سطر, h.FNUMB AS شماره_فرمول, d.CODE AS کد_ماده,
                d.MEGH AS مقدار_فعلي, d.MEGHk AS مقدار_کوچک_فعلي,
                CASE @Action WHEN 'zero' THEN N'مقدار صفر مي‌شود'
                             ELSE N'کل سطر فرمول حذف مي‌شود' END AS عمليات
        FROM    dbo.DTL_MANF d
        JOIN    dbo.HEAD_MANF h ON h.FNUMB = d.FNUMB
        WHERE   d.id = @DtlId;
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @Fnumb INT;
    SELECT @Fnumb = FNUMB FROM dbo.DTL_MANF WHERE id = @DtlId;

    IF @Action = 'zero'
        UPDATE dbo.DTL_MANF
           SET MEGH = 0, MEGHk = 0, MABLK = 0, SMABL = 0
         WHERE id = @DtlId;
    ELSE
        DELETE dbo.DTL_MANF WHERE id = @DtlId;

    UPDATE dbo.CC_Exception
       SET IsResolved = 1, ResolvedBy = @UserName, ResolvedAtUtc = SYSUTCDATETIME(),
           ResolutionNote = CASE @Action WHEN 'zero' THEN N'مقدار سطر فرمول صفر شد'
                                          ELSE N'سطر فرمول حذف شد' END
     WHERE ExceptionId = @ExceptionId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S00', 1,
            CONCAT(N'اصلاح فرمول با مقدار منفي — فرمول ', @Fnumb, N', کالا ', @Code,
                   CASE @Action WHEN 'zero' THEN N' — مقدار صفر شد' ELSE N' — سطر حذف شد' END,
                   N' توسط ', @UserName));

    IF @RunId IS NOT NULL
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT 1 AS تغيير_يافت, N'انجام شد' AS وضعيت;
END
GO


PRINT N'CHK-04 و اصلاح خودکار آماده شد.';

/* نمونه:
   EXEC dbo.CC_sp_Chk04_MissingFormula  @Month=5, @DT1=14050501, @DT2=14050531;
   EXEC dbo.CC_sp_Fix_MissingFormula    @Month=5, @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_Fix_MissingFormula    @Month=5, @DT1=14050501, @DT2=14050531, @WhatIf=0,
                                        @UserName=N'مدير مالي';
*/
GO
";
            TryExecuteCostCloseBatch(db, chk04AutoFix,
                "CC_sp_Chk04_MissingFormula و CC_sp_Fix_MissingFormula",
                "اسکریپت 13-chk04-and-autofix.sql را اجرا کنید (به CC_Exception و CC_CheckRule نیاز دارد).");

            // --- 14-s05-gate.sql ---
            string s05Gate = @"
/* ═══════════════════════════════════════════════════════════════════
   S05 — دروازه اعتبارسنجی

   دو کنترلی که امروز با دابل‌کلیک روی گزارش موجودی می‌گیرید:
     CHK-01  کاردکس منفی
     CHK-02  مغایرت کارت انبار و حسابداری
     CHK-13  حواله با مقدار صفر

   نتیجه مستقیم در CC_Exception می‌نشیند و صفحه مغایرت‌ها نشانش می‌دهد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند (YAZDSEPAR{YEAR} در تولید، SafirTest* در محیط تست).
   اسکریپت را روی پایگاه هدف اجرا کنید. بقیه اسکریپت‌های
   Server/Database/ هم همین قرارداد را دارند.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_S05_Gate
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Exception
    WHERE  RunId = @RunId AND RuleCode IN ('CHK-01','CHK-02','CHK-13');

    /* ─────────────────────────────────────────────────────────────
       طبقه‌بندی جهت TAG (ورود/خروج انبار) — منبع مرجع، نه حدس

       نسخهٔ قبلی این دو کنترل با یک حدس چهارتایی (TAG IN (1,7,9,24)
       = ورود، هر چیز دیگری = خروج) کار می‌کرد. کاربر تأیید کرد که
       هم CHK-01 و هم CHK-02 هر دو با شمار خیلی بالا (به ترتیب ۱۹۹ و
       ۱۱۸۳ مورد) غلط بودند، و کارت کالای واقعی هرگز منفی نمی‌شود؛
       یعنی آن حدس اشتباه بود.

       این نسخه از سه تابع واقعیِ همین دیتابیس که «کارت کالا» و
       CC_sp_S07 (انبارگردانی) از قبل به آنها اعتماد دارند کپی شده:
       dbo.AK_MOGO_AVL_KOL/_SUB (ورودی‌ها)، dbo.AK_MOGO_FR/_SUB
       (خروجی‌ها)، و dbo.MOGUDI/dbo.AKMOGUDI_KOL_ANBAR (موجودی نقطه‌ای
       — پایه گزارش کارت کالا). طبقه‌بندی TAG دقیقاً از همان‌جا آمده:

         ورود:  ۱ رسید خرید، ۷ تولید-ورود، ۹ حواله ورود، ۲۴ برگشت فروش
                ۲۲ برگشت فروش (فقط مقدار مرجوعی)
                ۵ انتقالی — طرف انبار فرعی (ستون ANBARF) = ورود مقصد
         خروج:  ۲ حواله فروش، ۸ تولید-خروج، ۱۰ حواله خروج،
                ۱۱ حواله خروج سایر، ۲۶ برگشت خرید آزاد
                ۵ انتقالی — طرف انبار اصلی (ستون ANBAR) = خروج مبدأ
                ۲۰ پیش‌فاکتور تسویه‌شده (فقط TAMIR=1 یا ۴)

       این لیست خودِ ۴ TAG قدیمی را هم دربردارد؛ فقط دیگر «هرچیز غیر
       از این ۴تا خروج است» فرض نمی‌شود — TAG هایی که در هیچ‌کدام از
       دو طرف رویه‌های مرجع نیامده‌اند (۱۲,۱۳,۱۴,۱۵,۱۷,۱۸,۲۷) اصلاً
       در این محاسبه شرکت نمی‌کنند، دقیقاً چون منبع مرجع هم شرکتشان
       نمی‌دهد. TAG=6 «انتقالی-ورود» هم عمداً نیامده — طرف دیگرِ همان
       سند TAG=5 است و اگر هر دو حساب شوند، انتقال دوبار شمرده می‌شود.

       انبارگردانی (ANBGRD_LST/ANBGRD_HEAD) اینجا رویداد‌به‌رویداد
       اعمال می‌شود (نه با قاعدهٔ جمع‌کلِ عجیب رویه‌های مرجع که کل
       اختلاف یک کالا/انبار را یک‌جا یا کاملاً ورود یا کاملاً خروج
       حساب می‌کند) — چون این کنترل به ترتیب واقعی رویدادها نیاز
       دارد، نه فقط مانده نهایی.

       ⚠️ اصلاح دوم (بعد از تأیید کاربر که کاردکس منفی هنوز غلط بود):
       AK_MOGO_AVL_KOL_SUB/AK_MOGO_FR_SUB (منبع بالا) فقط «مانده‌ی
       نهایی تا یک تاریخ» را درست می‌دهند، نه ترتیب. TAG=3 (برگشت
       خرید) و TAG=4 (برگشت فروش) اصلاً به این شکل در HEAD_LST/INVO_LST
       ثبت نمی‌شوند؛ آن دو تابع به‌جایش MEGH_MAR را همان لحظه، مستقیم
       روی سند اصلی (خرید/فروش) کسر می‌کنند — یعنی مقدار برگشتی از
       همان تاریخِ سند اصلی از مانده کم می‌شود، نه از تاریخ واقعیِ خودِ
       برگشت. برای «مانده‌ی نهایی» (CHK-02، یا مانده ابتدای دوره) این
       فرقی نمی‌کند چون فقط جمع نهایی مهم است؛ اما برای این کنترل که
       به ترتیب واقعی نیاز دارد، دقیقاً همین فرق باعث منفیِ کاذب یا
       واقعیِ نادیده‌گرفته‌شده می‌شود — یک حواله همین ماه ممکن است روی
       مانده‌ای بنشیند که زودتر از موعد (در تاریخ خرید اصلی، نه تاریخ
       برگشت) کم شده.

       منبع واقعیِ ترتیب‌دار، تابع dbo.KA_KH است (پایه‌ی گزارش «کارت
       کالا» که طبق تأیید کاربر هرگز منفی نمی‌شود). آنجا TAG=3/4 از
       جدول جداگانه‌ی dbo.BACK_HEAD می‌آیند: BACK_HEAD.ta=1 یعنی برگشت
       خرید (⇒TAG=3، NUMBER1 به همان سند خرید در INVO_LST اشاره می‌کند)
       و BACK_HEAD.ta=2 یعنی برگشت فروش (⇒TAG=4). BACK_HEAD.DATE_N
       تاریخ واقعیِ خودِ برگشت است. به همین دلیل اینجا هم TAG(1,7,9,24)
       و TAG(2,5,8,10,11,26) دیگر MEGH_MAR را کم نمی‌کنند (تا دوبار
       حساب نشود) و به‌جایش دو شاخه‌ی جدید از BACK_HEAD اضافه شده که
       دقیقاً در تاریخ خودِ برگشت اثر می‌گذارند — عیناً مطابق KA_KH.

       همین جابه‌جاییِ تاریخ روی مانده‌ی ابتدای دوره (dbo.MOGUDI) هم اثر
       دارد: MOGUDI هم از همان دو تابع مرجع می‌آید، پس اگر سند اصلی قبل
       از @DT1 باشد ولی برگشتش در همین دوره (یا بعدش) ثبت شده باشد،
       MOGUDI آن را زودتر از موعد کم/زیاد کرده. OpeningBackHeadFix پایین
       دقیقاً همین موارد را پیدا و برمی‌گرداند تا در #PM (در تاریخ واقعی
       خودشان) دوباره اعمال شوند.
       ───────────────────────────────────────────────────────────── */

    /* ─────────────────────────────────────────────────────────────
       CHK-01 — کاردکس منفی

       موجودی تجمعی هر کالا در هر انبار به ترتیب تاریخ محاسبه و
       هر جا منفی شد علامت می‌خورد.

       موجودی ابتدای دوره: چون تراکنش‌های ماه‌های قبل هم روی موجودی
       اثر دارند (و اگر نادیده گرفته شوند، اولین حوالهٔ همین ماه
       کاذباً منفی به‌نظر می‌رسد)، مانده ابتدای دوره از dbo.MOGUDI —
       همان تابع مرجعِ کارت کالا — در تاریخ یک روز قبل از @DT1 خوانده
       می‌شود، نه از صفر. (DATE_N به‌صورت اعداد ۸رقمی YYYYMMDD ذخیره
       شده؛ DT1-1 حسابی همیشه زیر اولین تاریخ واقعی همان ماه و بالای
       آخرین تاریخ واقعی ماه قبل می‌افتد، چون هیچ تاریخ واقعی روز/ماه
       صفر وجود ندارد — نیازی به تقویم شمسی نیست.)

       ترتیب داخل یک روز: NUMBER به‌تنهایی بین انواع مختلف برگه
       دنباله‌ی واحد و قابل‌اتکایی نیست (هرکدام شماره‌گذاری مستقل
       خودشان را دارند).

       ⚠️ چهارمین اصلاح (بعد از تأیید کاربر): منبع درستِ ترتیب داخل یک
       روز، TAGCOD.BARGAH (متن) نیست — TAGCOD.tartib است، یک ستون عددی
       که دقیقاً برای همین منظور طراحی شده. مقایسه‌ی متنیِ BARGAH (چه
       خام، چه با LTRIM) چون به فاصله‌های ابتدایی ناهمسان و ترتیب
       الفبایی وابسته است، با ترتیب واقعی کسب‌وکار یکی نیست — مثلاً
       «انتقالی-ورود» (tartib=10) باید قبل از «برگشت خرید آزاد»
       (tartib=11) بیاید، ولی این دو به‌عنوان متن با هم می‌آمیزند. با
       گزارش واقعی کارت کالا تأیید شد که فقط tartib ترتیب درست را می‌دهد.

       فقط اولین نقطه منفی هر کالا/انبار در همین دوره گزارش می‌شود؛
       بقیه دنباله همان یک مشکل‌اند و فهرست را شلوغ می‌کنند.

       ⚠️ هفتمین اصلاح: آستانه دیگر عدد ثابت -0.0001 نیست — از
       CC_CheckRule.Threshold (قابل تنظیم در تنظیمات برنامه) خوانده
       می‌شود. چون MEGHk/MABL از نوع FLOAT هستند، بعضی کالاها به‌خاطر
       نسبت‌های تبدیل واحد یه باقیمانده‌ی واقعیِ خیلی کوچک دارند که هرگز
       دقیقاً صفر نمی‌شود؛ این آستانه برای فیلتر همین نویز است، نه برای
       نادیده گرفتن کمبود واقعی. پیش‌فرض -0.001.
       ───────────────────────────────────────────────────────────── */
    DECLARE @Chk01Threshold DECIMAL(18,6) =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode = 'CHK-01'), -0.001);

    IF OBJECT_ID('tempdb..#PM') IS NOT NULL DROP TABLE #PM;

    -- ستون‌ها صریحاً تعریف می‌شوند (نه SELECT…INTO) چون شاخهٔ انبارگردانی
    -- برای TAG مقدار NULL دارد و نمی‌خواهیم NOT NULL این ستون از شاخهٔ
    -- اول به‌صورت ضمنی استنتاج شود.
    --
    -- ⚠️ ششمین اصلاح: Meghdar عمداً DECIMAL است، نه FLOAT. MEGHk/MABL در
    -- خودِ INVO_LST از نوع FLOAT هستند، و جمع تجمعیِ FLOAT روی صدها ردیف
    -- یک ماه همیشه یک باقیمانده‌ی خیلی کوچک (مثلاً ۰٫۰۰۴) به‌جای صفر
    -- دقیق می‌گذارد — حتی وقتی ریاضی واقعی باید دقیقاً صفر شود. تبدیل به
    -- DECIMAL درست همینجا (قبل از SUM OVER)، نه بعدش، این خطای انباشتی
    -- را از ریشه حذف می‌کند.
    CREATE TABLE #PM (
        Anbar   INT             NULL,
        code    BIGINT          NULL,
        DATE_N  BIGINT          NULL,
        NUMBER  FLOAT           NULL,
        TAG     FLOAT           NULL,
        Meghdar DECIMAL(18,6)   NULL
    );

    INSERT #PM
    SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code,
            hl.DATE_N, hl.NUMBER, il.TAG, il.MEGHk AS Meghdar
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG IN (1, 7, 9, 24)
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG, il.MEGH_MAR
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 22
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    -- ⚠️ سومین اصلاح (بعد از تأیید کاربر با گزارش واقعی کارت کالا):
    -- طرف مقصدِ انتقالی (ANBARF) عمداً TAG=6 درج می‌شود، نه TAG=5 خودِ
    -- سند. دلیل: BARGAH طرف مقصد باید «انتقالی - ورود» باشد نه «انتقالی
    -- - خروج»، وگرنه ترتیب همان روز (پایین، ORDER BY ... LTRIM(BARGAH))
    -- غلط می‌شود. عیناً مطابق dbo.KA_KH که همین طرف را با «6 AS TA» برمی‌گرداند.
    INSERT #PM
    SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER,
            CAST(6 AS FLOAT), il.MEGHk
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 5
      AND   il.ANBARF IS NOT NULL
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG,
            -il.MEGHk
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG IN (2, 5, 8, 10, 11, 26)
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG, -il.MEGHk
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 20
      AND   (hl.TAMIR = 1 OR hl.TAMIR = 4)
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    -- ⚠️ پنجمین اصلاح: TAG این ردیف NULL نمی‌ماند — دقیقاً مثل UIIF در
    -- dbo.KA_KH باید TAG=18 (اضافه انبار) یا TAG=17 (کسری انبار) بگیرد،
    -- وگرنه در LEFT JOIN با TAGCOD هیچ tartib‌ای پیدا نمی‌کند، ISNULL آن
    -- را ۰ می‌گذارد، و همیشه اول همان روز پردازش می‌شود — حتی اگر واقعاً
    -- باید بعد از سند دیگری از همان روز بیاید (تأیید شد با گزارش واقعی
    -- کارت کالا: سند «اضافه انبار» زودتر از سند انتقالی-ورود همان روز
    -- پردازش می‌شد و مانده را کاذباً منفی نشان می‌داد).
    INSERT #PM
    SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT), ah.GRD_DATE, ah.GRD_NUM,
            CASE WHEN (al.MOG - ISNULL(al.NUM3, 0)) > 0 THEN CAST(18 AS FLOAT) ELSE CAST(17 AS FLOAT) END,
            -(al.MOG - ISNULL(al.NUM3, 0))
    FROM    dbo.ANBGRD_LST al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.N_S IS NOT NULL
      AND   ah.GRD_ANBAR IS NOT NULL
      AND   ((al.MOG - ISNULL(al.NUM3, 0)) * -1 <> 0)   -- مطابق dbo.KA_KH: ردیف بدون اختلاف واقعی حذف شود
      AND   ah.GRD_DATE BETWEEN @DT1 AND @DT2;

    -- TAG=3 برگشت خرید — از BACK_HEAD (ta=1)، در تاریخ واقعیِ خودِ
    -- برگشت، نه تاریخ سند خرید اصلی. مطابق dbo.KA_KH.
    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), bh.DATE_N, bh.NUMBER,
            CAST(3 AS FLOAT), -il.MEGH_MAR
    FROM    dbo.BACK_HEAD bh
    JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
    WHERE   bh.ta = 1
      AND   il.MEGH_MAR <> 0
      AND   bh.DATE_N BETWEEN @DT1 AND @DT2;

    -- TAG=4 برگشت فروش — از BACK_HEAD (ta=2)، همان منطق.
    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), bh.DATE_N, bh.NUMBER,
            CAST(4 AS FLOAT), il.MEGH_MAR
    FROM    dbo.BACK_HEAD bh
    JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
    WHERE   bh.ta = 2
      AND   il.MEGH_MAR <> 0
      AND   bh.DATE_N BETWEEN @DT1 AND @DT2;

    ;WITH DistinctAnbars AS (
        SELECT DISTINCT Anbar FROM #PM WHERE Anbar IS NOT NULL
    ),
    OpeningBackHeadFix AS (
        -- اصلاح مانده ابتدای دوره: MOGUDI مقدار برگشتی را در تاریخ سند
        -- اصلاح (نه تاریخ واقعی برگشت) کم/زیاد کرده. اگر سند اصلی قبل
        -- از @DT1 بوده ولی خودِ برگشت در @DT1 یا بعدش ثبت شده، آن اثر
        -- زودهنگام اینجا خنثی می‌شود تا در #PM (بالا) در تاریخ درست
        -- دوباره اعمال شود.
        SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code,
                SUM(CASE WHEN bh.ta = 1 THEN  il.MEGH_MAR
                         WHEN bh.ta = 2 THEN -il.MEGH_MAR
                    END) AS Fix
        FROM    dbo.BACK_HEAD bh
        JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
        JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
        WHERE   bh.ta IN (1, 2)
          AND   il.MEGH_MAR <> 0
          AND   hl.DATE_N < @DT1
          AND   bh.DATE_N >= @DT1
        GROUP BY il.ANBAR, TRY_CAST(il.CODE AS BIGINT)
    ),
    Opening AS (
        -- مانده ابتدای دوره از تابع مرجع کارت کالا، فقط برای جفت‌های
        -- (انبار، کالا) که واقعاً در همین دوره حرکت دارند.
        SELECT  m.ANBAR AS Anbar, TRY_CAST(m.CODE AS BIGINT) AS code,
                m.MAND + ISNULL(f.Fix, 0) AS OpeningBalance
        FROM    DistinctAnbars da
        CROSS   APPLY dbo.MOGUDI(@DT1 - 1, CAST(da.Anbar AS NVARCHAR(50))) m
        LEFT    JOIN OpeningBackHeadFix f
               ON f.Anbar = m.ANBAR AND f.code = TRY_CAST(m.CODE AS BIGINT)
    ),
    AllMovement AS (
        SELECT  o.Anbar, o.code, CAST(0 AS BIGINT) AS DATE_N, CAST(0 AS FLOAT) AS NUMBER,
                CAST(NULL AS FLOAT) AS TAG, CAST(0 AS INT) AS Tartib,
                CAST(o.OpeningBalance AS DECIMAL(18,6)) AS Meghdar
        FROM    Opening o
        WHERE   EXISTS (SELECT 1 FROM #PM p WHERE p.Anbar = o.Anbar AND p.code = o.code)

        UNION ALL
        SELECT  p.Anbar, p.code, p.DATE_N, p.NUMBER, p.TAG,
                ISNULL(tc.tartib, 0) AS Tartib, p.Meghdar
        FROM    #PM p
        LEFT    JOIN dbo.TAGCOD tc ON tc.CODE = p.TAG
        WHERE   p.Anbar IS NOT NULL AND p.code IS NOT NULL
    ),
    Tajamoi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Tartib,
                SUM(Meghdar) OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Tartib, NUMBER
                    ROWS UNBOUNDED PRECEDING) AS Mande
        FROM    AllMovement
    ),
    AvvalinManfi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Tartib, Mande,
                ROW_NUMBER() OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Tartib, NUMBER) AS rn
        FROM    Tajamoi
        WHERE   Mande < @Chk01Threshold
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocTag, DocDate, Amount, Description)
    SELECT  @RunId, 'S05', 'CHK-01', 1, 2,
            m.Anbar, m.code, m.NUMBER, m.TAG, m.DATE_N, m.Mande,
            CONCAT(N'انبار ', m.Anbar, N' (', ISNULL(a.NAMES, N'نامشخص'), N'): موجودی در تاریخ ',
                   m.DATE_N / 10000, '/',
                   FORMAT(m.DATE_N / 100 % 100, '00'), '/',
                   FORMAT(m.DATE_N % 100, '00'),
                   N' منفی می‌شود')
    FROM    AvvalinManfi m
    LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = m.Anbar
    WHERE   m.rn = 1
      AND   m.DATE_N BETWEEN @DT1 AND @DT2
      -- پذیرش دائمی (CC_AcceptedException) — عيناً همان مکانیزم
      -- CHK-03/CHK-04، به‌علاوه‌ی Anbar چون این کنترل روی جفت
      -- (انبار،کالا) کار می‌کند نه فقط کالا. چون این جدول مقید به
      -- RunId/ماه نیست، یک پذیرش هم دیگر در همین اجرا مسدود نمی‌کند
      -- هم در ماه‌های بعد دوباره ظاهر نمی‌شود.
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                        WHERE ae.RuleCode = 'CHK-01' AND ae.IsActive = 1
                          AND (ae.Anbar IS NULL OR ae.Anbar = m.Anbar)
                          AND (ae.Code  IS NULL OR ae.Code  = m.code));

    DROP TABLE #PM;

    /* ─────────────────────────────────────────────────────────────
       CHK-02 — مغایرت کارت انبار و حسابداری

       مانده ریالی کارت انبار با مانده حساب موجودی جنسی مقایسه
       می‌شود. کارت انبار اینجا مستقیماً از INVO_LST/HEAD_LST با
       همان طبقه‌بندی TAG بالا محاسبه می‌شود (نه KALAS، که فقط یک ویو
       گزارشی روی همین جدول‌هاست) — چون CHK-02 برخلاف CHK-01 فقط به
       مانده نهایی نیاز دارد، نه ترتیب تراکنش‌ها، آستانهٔ زمانی همان
       «<= @DT2» قبلی است (تجمعی از ابتدای تاریخچه، نه فقط این ماه).

       هر انبار زیر یک معین جداگانه در حسابداری ثبت می‌شود، نه یک
       معین ثابت مشترک برای همهٔ انبارها (تفصیلی طبق ساختار خودِ
       دیتابیس فقط زیر یک معین مشخص یکتاست: TDETA_HES.PK =
       (N_KOL,NUMBER,TNUMBER)). نگاشت واقعیِ انبار⇄معین از CC_AnbarHes
       (تنظیمات) خوانده می‌شود، نه هاردکد، چون شرکت‌به‌شرکت فرق می‌کند.

       آستانه یک ریال است چون این دو باید دقیقاً یکی باشند.

       ⚠️ اصلاح دوم: مثل CHK-01، برگشت خرید (TAG=3) و برگشت فروش
       (TAG=4) هیچ‌جای این محاسبه نبودند — چون اصلاً روی HEAD_LST ثبت
       نمی‌شوند، بلکه از dbo.BACK_HEAD می‌آیند (نگاه کنید توضیح CHK-01
       بالا). برخلاف CHK-01، اینجا نیازی به حذف چیزی از شاخه‌های TAG=1/2
       نبود چون آن‌ها همیشه MABL_K کامل را بدون کسر MEGH_MAR گرفته‌اند؛
       فقط دو شاخه‌ی جدید اضافه شده.

       ⚠️ سومین اصلاح (بعد از تأیید کاربر با گزارش واقعی کارت کالا و
       سند حسابداری): موجودی اول دوره (dbo.STUF_FSK.MABL_A) اصلاً در
       KartAnbar جمع نمی‌شد — یعنی «فقط گردش» با «افتتاحیه + گردش»ِ
       حسابداری مقایسه می‌شد. برای هر کالایی که موجودی اول دوره‌ی
       غیرصفر دارد (نه فقط یک مورد خاص)، این دقیقاً به‌اندازه‌ی همان
       موجودی اول دوره مغایرت کاذب می‌ساخت. حالا یک شاخه‌ی جدید همان
       مبلغ را از STUF_FSK اضافه می‌کند — دقیقاً مثل Opening در CHK-01.
       ───────────────────────────────────────────────────────────── */
    IF NOT EXISTS (SELECT 1 FROM dbo.CC_AnbarHes)
    BEGIN
        -- بدون نگاشت انبار⇄معین نمی‌توان درست مقایسه کرد؛ به‌جای هزاران
        -- مورد کاذب، یک هشدار واحد می‌گوید چه باید تنظیم شود.
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Description)
        VALUES
            (@RunId, 'S05', 'CHK-02', 2, 1,
             N'نگاشت انبار به حساب موجودی (کل/معین) در تنظیمات ثبت نشده؛ این کنترل غیرفعال است.');
    END
    ELSE
    BEGIN
        /* ─────────────────────────────────────────────────────────
           چهارمین اصلاح (بعد از تأیید کاربر با dbo.MOGHA_ANBAR — تابع
           مرجعِ همین گزارش «مانده کارت انبار» که سیستم اصلی استفاده
           می‌کند): ارزش کارت انبار جمعِ خامِ MABL_K هر تراکنش نیست؛
           «مانده مقدار × آخرین نرخ میانگین ثبت‌شده» است.

           چرا فرق دارد: بازسازی نرخ میانگین (S07A) برای TAG=۲/۲۲/۲۴/۲۶
           (INVO_LST) و TAG مجازی ۳/۴ (BACK_HEAD) فقط ستون AVRAGE/AVRAGE2
           را به‌روز می‌کند — نه MABL_K/MABL خودِ ردیف را؛ این عیناً رفتار
           C0_TASK اصلی است (فقط TAG=۵/۹/۱۰/۱۱ و انبارگردانی MABL_K را هم
           بازنویسی می‌کنند). نتیجه: MABL_K روی ردیف فروش، ارزش لحظه‌ی صدور
           فاکتور را نگه می‌دارد، نه نرخ میانگینِ اصلاح‌شده — و سند
           حسابداری (GENSANADFROOSH و مشابه) همیشه AVRAGE تازه را پست
           می‌کند، نه MABL_K را. جمع‌زدن MABL_K خام برای «کارت انبار» پس
           با گذر زمان (و هر بار که S07A نرخ را عوض می‌کند) از حسابداری
           واگرا می‌شود، حتی وقتی هیچ‌کدام واقعاً اشتباه نیستند.

           راه‌حل mirror شده از dbo.MOGHA_ANBAR: فقط «مقدار» هر تراکنش جمع
           زده می‌شود (نه مبلغ)؛ مبلغ نهایی = مانده‌مقدار × آخرین AVRAGE/
           AVRAGE2 ثبت‌شده روی این (کالا، انبار) تا @DT2 — با همان ترتیب
           tie-break کد اصلی (DATE_N، سپس TAGCOD.tartib، سپس NUMBER؛ عیناً
           BARGAH اصلی ولی با tartib عددی به‌جای متن، به همان دلیلی که
           برای CHK-01 قبلاً تصحیح شد) و بازگشت به STUF_FSK.FI_A وقتی هیچ
           تراکنشی ثبت نشده.

           روی کد ۳۳۰۱/انبار ۲ تست شد: مانده مقدار ۲۰۸۰ × آخرین نرخ
           ۳,۲۹۲,۹۸۶.۷۴ = ۶,۸۴۹,۴۱۲,۴۱۴ — دقیقاً برابر سمت حسابداری.
           ───────────────────────────────────────────────────────── */
        ;WITH AnbarQtyMovement AS (
            SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code, il.MEGHk AS Meg
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (1, 7, 9, 24)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), il.MEGH_MAR
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 22
              AND   hl.DATE_N <= @DT2
              AND   il.MEGH_MAR <> 0
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.MEGHk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 5
              AND   il.ANBARF IS NOT NULL
              AND   hl.DATE_N <= @DT2
              AND   CAST(il.ANBARF AS INT) IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGHk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (2, 5, 8, 10, 11, 26)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGHk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 20
              AND   (hl.TAMIR = 1 OR hl.TAMIR = 4)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT),
                    -(al.MOG - ISNULL(al.NUM3, 0))
            FROM    dbo.ANBGRD_LST al
            JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
            WHERE   ah.N_S IS NOT NULL
              AND   ah.GRD_DATE <= @DT2
              AND   ah.GRD_ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            -- TAG=3 برگشت خرید (از BACK_HEAD؛ کالا از انبار خارج می‌شود)
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGH_MAR
            FROM    dbo.BACK_HEAD bh
            JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
            WHERE   bh.ta = 1
              AND   il.MEGH_MAR <> 0
              AND   bh.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            -- TAG=4 برگشت فروش (از BACK_HEAD؛ کالا به انبار برمی‌گردد)
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), il.MEGH_MAR
            FROM    dbo.BACK_HEAD bh
            JOIN    dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
            WHERE   bh.ta = 2
              AND   il.MEGH_MAR <> 0
              AND   bh.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            -- موجودی اول دوره (فقط مقدار؛ ارزش از آخرین نرخ میانگین می‌آید)
            SELECT  f.ANBAR, TRY_CAST(f.CODE AS BIGINT), f.MOGODI_A
            FROM    dbo.STUF_FSK f
            WHERE   f.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)
        ),
        AnbarQty AS (
            SELECT  Anbar, code, SUM(Meg) AS Mand
            FROM    AnbarQtyMovement
            WHERE   Anbar IS NOT NULL AND code IS NOT NULL
            GROUP BY Anbar, code
        ),
        LastAvgSource AS (
            -- عیناً dbo.MOGHA_ANBAR.lastav_base: AVRAGE برای TAG۱/۷/۹/۲۴
            -- (ورود مستقیم)، AVRAGE2 برای TAG=۵ مقصد (ورود از انتقالی).
            SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code, il.AVRAGE AS Rate,
                    hl.DATE_N, t.tartib, il.NUMBER, il.ID
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON il.NUMBER = hl.NUMBER AND il.TAG = hl.TAG
            JOIN    dbo.TAGCOD t ON il.TAG = t.CODE
            WHERE   hl.DATE_N <= @DT2
              AND   il.TAG IN (1, 7, 9, 24)
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.AVRAGE2,
                    hl.DATE_N, t.tartib, il.NUMBER, il.ID
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON il.NUMBER = hl.NUMBER AND il.TAG = hl.TAG
            JOIN    dbo.TAGCOD t ON il.TAG = t.CODE
            WHERE   hl.DATE_N <= @DT2
              AND   il.TAG = 5
              AND   il.ANBARF IS NOT NULL
              AND   CAST(il.ANBARF AS INT) IN (SELECT Anbar FROM dbo.CC_AnbarHes)
        ),
        -- وقتی یک سند، یک کالا را در چند ردیف با نرخ‌های متفاوت ثبت کرده
        -- (مثلاً دو محموله‌ی هم‌روز با نرخ فرق)، این ردیف‌ها در
        -- (DATE_N،tartib،NUMBER) کاملاً هم‌تراز می‌شوند. قبلاً با
        -- ROW_NUMBER() بدون تای‌برک نهایی، یکی‌شان دلبخواهی انتخاب می‌شد —
        -- نتیجه‌ی CHK-02 بدون هیچ تغییری در داده، بین دو اجرای پشت‌سرهم
        -- عوض می‌شد (روی کد ۳۰۹۲/انبار۳: سند ۸۹۱ دو ردیف دارد — ۱۱ واحد
        -- با نرخ ۱,۵۸۰,۳۲۹ روی id کوچک‌تر، ۳۴۷ واحد با نرخ ۱,۷۰۰,۱۴۰ روی
        -- id بزرگ‌تر).
        --
        -- id DESC (ردیفی که آخر نوشته شده) درست است، نه ASC: نرخِ روی
        -- id بزرگ‌تر (۱,۷۰۰,۱۴۰) دقیقاً همان نرخی است که تمام اسناد
        -- انتقالِ *بعد* از سند ۸۹۱ (AVRAGE2شان) استفاده کرده‌اند —
        -- یعنی موتور نرخ میانگین، بعد از پردازش هر دو ردیفِ همین سند به
        -- ترتیب، روی همین عدد نهایی نشسته. با id DESC، مانده‌ی کارت
        -- انبار دقیقاً با حسابداری برابر شد (۱۰,۱۶۴,۳۸۹,۴۷۱ = ۱۰,۱۶۴,۳۸۹,۴۷۱،
        -- تا ریال). جالب این‌جاست که dbo.MOGHA_ANBAR (مرجع رسمی گزارش
        -- کارت انبار) هم همین باگ را دارد — بدون تای‌برک، این‌جا id
        -- کوچک‌تر را برمی‌گرداند و ۷۰۰+ میلیون مغایرت کاذب می‌سازد؛ فقط
        -- چون تا حالا این حالت (چند ردیف هم‌سند با نرخ فرق) به‌ندرت پیش
        -- اومده کسی متوجه نشده بود.
        LastAvgRanked AS (
            SELECT  Anbar, code, Rate,
                    ROW_NUMBER() OVER (PARTITION BY Anbar, code
                                        ORDER BY DATE_N DESC, tartib DESC, NUMBER DESC, ID DESC) AS rn
            FROM    LastAvgSource
        ),
        KartAnbar AS (
            SELECT  q.Anbar, q.code,
                    ROUND(q.Mand, 2) * ISNULL(la.Rate, f.FI_A) AS Mande
            FROM    AnbarQty q
            LEFT    JOIN LastAvgRanked la ON la.Anbar = q.Anbar AND la.code = q.code AND la.rn = 1
            LEFT    JOIN dbo.STUF_FSK  f  ON f.ANBAR = q.Anbar AND TRY_CAST(f.CODE AS BIGINT) = q.code
        ),
        Hesabdari AS (
            SELECT  am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code,
                    SUM(d.BED) - SUM(d.BES) AS Mande
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED  h  ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   h.DATE_S <= @DT2
            GROUP BY am.Anbar, TRY_CAST(d.HES_T AS BIGINT)
        ),
        -- تشخیص خودکارِ علتِ محتمل، تا اپراتور مجبور نباشد دستی SQL بزند:
        -- کدهایی که STUF_FSK موجودی اول دوره‌ی غیرصفر دارند ولی هیچ سند
        -- افتتاحیه‌ای زیر همان حساب (کل/معین/تفصیلی) در حسابداری ثبت
        -- نشده — دقیقاً همان الگویی که روی کد ۳۱۰۰/انبار۴ پیدا شد.
        -- این حالت را نمی‌شود خودکار اصلاح کرد (فقط تیم انبار/حسابداری
        -- می‌داند آن موجودی واقعی بوده یا نه)، پس فقط توضیح داده می‌شود.
        MissingOpening AS (
            SELECT DISTINCT am.Anbar, TRY_CAST(f.CODE AS BIGINT) AS code
            FROM    dbo.STUF_FSK f
            JOIN    dbo.CC_AnbarHes am ON am.Anbar = f.ANBAR
            WHERE   f.MOGODI_A <> 0
              AND   NOT EXISTS (
                        SELECT 1 FROM dbo.DEED_DTL d
                        JOIN   dbo.DEED_HED h ON h.N_S = d.N_S
                        WHERE  d.HES_K = am.HesKol AND d.HES_M = am.HesMoin
                          AND  TRY_CAST(d.HES_T AS BIGINT) = TRY_CAST(f.CODE AS BIGINT)
                          AND  d.SHARH LIKE N'%افتتاحيه%'
                    )
        ),
        -- جهتِ معکوسِ MissingOpening: سند افتتاحیه در حسابداری هست ولی
        -- کاردکس برای همان کالا/انبار موجودی اول دوره ندارد (MOGODI_A=0
        -- یا اصلاً ردیفی در STUF_FSK نیست). تا امروز فقط جهتِ اول تشخیص
        -- داده می‌شد و این حالت بدون هیچ توضیحی گزارش می‌شد.
        --
        -- روی داده‌ی واقعی (فروردین ۱۴۰۵، انبار ۸۰۷ «انبار محصول یزد»)
        -- سه کالا دقیقاً همین وضع را داشتند و مبلغ مغایرت مو‌به‌مو برابر
        -- سند افتتاحیه بود:
        --     ۲۸۸۲ → ۱۲,۶۰۷,۲۴۰   ۳۱۴۲ → ۲۵,۱۱۷,۰۶۸   ۳۳۴۲ → ۲۹,۲۷۹,۸۱۷
        -- مثل جهتِ اول، این هم خودکار قابل اصلاح نیست: فقط انبار/حسابداری
        -- می‌داند کدام سمت درست است.
        ExtraOpening AS (
            SELECT  DISTINCT am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   d.SHARH LIKE N'%افتتاحيه%'
              AND   NOT EXISTS (
                        SELECT 1 FROM dbo.STUF_FSK f
                        WHERE  f.ANBAR = am.Anbar
                          AND  TRY_CAST(f.CODE AS BIGINT) = TRY_CAST(d.HES_T AS BIGINT)
                          AND  f.MOGODI_A <> 0
                    )
        )
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
        SELECT  @RunId, 'S05', 'CHK-02', 2, 2,
                ISNULL(k.Anbar, hh.Anbar), ISNULL(k.code, hh.code),
                ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0),
                -- ⚠ علت، *اول* جمله می‌آید نه آخرش. قبلاً کلمه‌ی «افتتاحیه»
                -- ته یک جمله‌ی بلند بود و کاربر باید تا انتها می‌خواند تا
                -- بفهمد این مغایرت اصلاً از گردش ماه نیست. حالا اولین چیزی
                -- که بعد از نام انبار دیده می‌شود همین است.
                CONCAT(N'انبار ', ISNULL(k.Anbar, hh.Anbar),
                       N' (', ISNULL(a.NAMES, N'نامشخص'), N'): ',
                       CASE WHEN mo.code IS NOT NULL OR eo.code IS NOT NULL
                            THEN N'⚠ مغایرت مربوط به افتتاحیه است، نه گردش این ماه. '
                            ELSE N'' END,
                       N'کارت انبار ', FORMAT(ISNULL(k.Mande, 0), 'N0'),
                       N' در برابر حسابداری ', FORMAT(ISNULL(hh.Mande, 0), 'N0'),
                       CASE WHEN mo.code IS NOT NULL
                            THEN N' — موجودی اول دوره در کاردکس ثبت شده (STUF_FSK) ولی سند افتتاحیهٔ آن هرگز در حسابداری صادر نشده. تصمیم با انبار/حسابداری است؛ بازسازی خودکار درستش نمی‌کند.'
                            WHEN eo.code IS NOT NULL
                            THEN N' — سند افتتاحیه در حسابداری صادر شده ولی کاردکس موجودی اول دوره‌ای ندارد (STUF_FSK صفر است). مبلغ مغایرت معمولاً دقیقاً برابر همان سند افتتاحیه است. تصمیم با انبار/حسابداری است که کدام سمت درست است؛ بازسازی خودکار درستش نمی‌کند.'
                            ELSE N'' END)
        FROM    KartAnbar k
        FULL    OUTER JOIN Hesabdari hh ON hh.Anbar = k.Anbar AND hh.code = k.code
        LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = ISNULL(k.Anbar, hh.Anbar)
        LEFT    JOIN MissingOpening mo ON mo.Anbar = ISNULL(k.Anbar, hh.Anbar) AND mo.code = ISNULL(k.code, hh.code)
        LEFT    JOIN ExtraOpening   eo ON eo.Anbar = ISNULL(k.Anbar, hh.Anbar) AND eo.code = ISNULL(k.code, hh.code)
        WHERE   ABS(ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0)) > 1
          -- پذیرش دائمی — نگاه کنید توضیح بالای CHK-01. برای همین دلیل
          -- این‌جا هم اضافه شد: مورد شناخته‌شده‌ی «موجودی اول دوره سند
          -- افتتاحیه ندارد» (MissingOpening) دقیقاً همان چیزی است که
          -- معمولاً با این دکمه پذیرفته می‌شود.
          AND   NOT EXISTS (SELECT 1 FROM dbo.CC_AcceptedException ae
                            WHERE ae.RuleCode = 'CHK-02' AND ae.IsActive = 1
                              AND (ae.Anbar IS NULL OR ae.Anbar = ISNULL(k.Anbar, hh.Anbar))
                              AND (ae.Code  IS NULL OR ae.Code  = ISNULL(k.code, hh.code)));
    END

    /* ─────────────────────────────────────────────────────────────
       CHK-13 — حواله با مقدار صفر

       ماده‌ای که در فرمول مقدار دارد ولی حواله‌اش صفر است، یعنی
       فرمول پس از صدور حواله ویرایش شده و خروج مواد بازسازی نشده.
       این همان چیزی است که برای کالای ۲۸۴۱ در ماه تیر رخ داد.
       ───────────────────────────────────────────────────────────── */
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity,
         Anbar, Code, DocNumber, DocDate, Amount, Description)
    SELECT  DISTINCT @RunId, 'S05', 'CHK-13', 16, 2,
            i.ANBAR, CAST(i.CODE AS BIGINT), h.NUMBER, h.DATE_N, 0,
            N'حواله با مقدار صفر برای ماده‌ای که در فرمول ماه مقدار دارد'
    FROM    dbo.INVO_LST i
    JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
    WHERE   h.TAG = 10
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   ISNULL(i.MEGHK, 0) = 0
      AND   EXISTS (
                SELECT 1
                FROM   dbo.DTL_MANF d
                JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
                WHERE  CAST(d.CODE AS BIGINT) = CAST(i.CODE AS BIGINT)
                  AND  d.MEGHk > 0);

    /* ─────────────────────────── خلاصه ─────────────────────────── */
    SELECT  e.RuleCode                                            AS قاعده,
            r.RuleName                                            AS عنوان,
            CASE e.Severity WHEN 2 THEN N'مسدودکننده'
                            ELSE N'هشدار' END                     AS شدت,
            COUNT(*)                                              AS تعداد
    FROM    dbo.CC_Exception e
    LEFT    JOIN dbo.CC_CheckRule r ON r.RuleCode = e.RuleCode
    WHERE   e.RunId = @RunId AND e.StepCode = 'S05' AND e.IsResolved = 0
    GROUP BY e.RuleCode, r.RuleName, e.Severity
    ORDER BY e.Severity DESC, e.RuleCode;
END
GO

/* رویه آزمایشی قدیمی که جای خود را به CC_sp_S00_Preflight داده است. */
DROP PROCEDURE IF EXISTS dbo.CC_sp_Preflight;
GO

PRINT N'رويه دروازه اعتبارسنجي ايجاد شد.';
GO
";
            TryExecuteCostCloseBatch(db, s05Gate,
                "CC_sp_S05_Gate",
                "اسکریپت‌های 10-schema.sql تا 13-chk04-and-autofix.sql را اول اجرا کنید.");

            // --- 15-rate-engine-production.sql ---
            string rateEngine = @"
/* ═══════════════════════════════════════════════════════════════════
   مرحله ۴ — موتور نرخ، نسخه تولیدی

   تفاوت با نسخه آزمون بازگشتی (فایل 03):
     ۱) در DTL_MANF و HEAD_MANF می‌نویسد، نه فقط در CC_ItemCost
     ۲) هر تغییر در CC_FormulaChange ثبت می‌شود
     ۳) @RunId می‌گیرد و به سابقه اجرا وصل است
     ۴) S10 (تراز هزینه تبدیل) هم اینجاست

   ترتیب اجرا: S10 سپس S11
   چون ضریب تعدیل مستقل از نرخ مواد است، یک بار محاسبه کافی است
   و دیگر نیازی به قرار گرفتن داخل حلقه ندارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S11 که در CC_ItemCost (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S07B — تخصیص دستمزد و سربار به تفکیک کالا، بر اساس ضریب جذب

   کاربر برای هر کالا یک «ضریب جذب دستمزد» دستی وارد می‌کند
   (dbo.CC_LaborAbsorptionRate — مثلاً بر مبنای وزن: کالای ۱ کیلوگرمی
   ضریب ۱، کالای ۲ کیلوگرمی ضریب ۲ و...، ولی مبنا هرچه کاربر بخواهد
   می‌تواند باشد) و اختیاراً یک «ضریب جذب سربار» مستقل (چون معیارِ
   درستِ سربار می‌تواند با دستمزد فرق کند؛ تأیید کاربر: «فعلاً از
   دستمزد براش مقدار بده» — یعنی وقتی ضریب سربار خالی است، همان ضریب
   دستمزد جایگزینش می‌شود). دستمزد/سربارِ واقعیِ هر واحد تولیدی
   (مثلاً یزد) — مستقیم از حساب ۷۵۱، فیلترشده با HES_M (کدِ کالای
   تولیدشده) به کدهایی که همان واحد تولید کرده (نگاه کنید توضیح پایین‌تر).

   ⚠️ فرمولِ نرخِ واحدِ هر کالا (تأیید صریحِ کاربر، منطقِ حسابداریِ
   صنعتی): نرخِ دستمزدِ هر واحدِ کالا باید با افزایشِ حجمِ تولیدِ
   همان کالا کاهش یابد — یعنی هرچه یک کالا بیشتر تولید شود، هزینه‌ی
   دستمزدِ همان مقدارِ ثابت روی تعدادِ بیشتری واحد جذب می‌شود، پس نرخِ
   هر واحدش کمتر می‌شود؛ نه این‌که نرخِ واحد ثابت بماند و فقط جمعِ کل
   با حجم بالا برود. فرمول دقیق:

       نرخِ واحدِ کالا = ضریبِ کالا ÷ (مجموعِ سادهٔ ضرایبِ همه‌ی
                          کالاهای تولیدشده‌ی همین واحد این ماه ×
                          مقدارِ تولیدِ خودِ همین کالا این ماه)
                          × دستمزدِ واقعیِ واحد

   نکته‌ی مهم: «مجموعِ ضرایب» اینجا سادهٔ (بدون وزن‌دهی به مقدار) است —
   هر کد فقط یک‌بار با ضریبِ خودش جمع می‌شود، نه ضریب×مقدارش (که نسخه‌ی
   قبلی این فایل بود و باعث می‌شد نرخِ واحد اصلاً به مقدارِ تولیدِ خودِ
   همان کالا وابسته نباشد — با تستِ واقعی روی کد ۱۷۸۶/آب‌پنیر کشف و
   تصحیح شد).

   ⚠️ رفتار ضریب خالی/صفر (تأیید کاربر، کد ۳۷۳ خرداد ۱۴۰۵ کشف شد):
   خالی (NULL) یعنی «هنوز بررسی نشده» — این کالا از تقسیم و از مخرج
   کسر کنار می‌ماند و مقدار فعلیِ IMBIBE_MANF/IMBIBE_SAR دست‌نخورده
   می‌ماند. صفرِ صریح (Coefficient=0) اما یعنی «کاربر عمداً این کالا
   را از جذب دستمزد کنار گذاشته» — IMBIBE_MANF/IMBIBE_SAR همین کالا
   صراحتاً صفر می‌شود (نه این‌که دست‌نخورده بماند)، وگرنه یک نرخِ
   قدیمیِ باقی‌مانده از زمانی که ضریب هنوز صفر نشده بود، برای همیشه
   به‌جا می‌ماند و کسی متوجه نمی‌شود.

   عمداً قبل از S07A اجرا می‌شود (SeqNo=72، بین S07=70 و S07A=75) تا
   محاسبه‌ی نرخ میانگین/تولید همان ماه از همین مقدار استفاده کند.

   ⚠️ عمداً کنار پلاگ اصلاحی S10 (تأیید کاربر): چون همین دستمزد/سربارِ
   واقعیِ ۷۵۱ مبنای تقسیم است، انتظار می‌رود ضریب k در S10 نزدیک ۱ در
   بیاید — S10 همچنان به‌عنوان یک لایه‌ی تطبیق نهایی (گرد کردن/موارد
   خاص) دست‌نخورده باقی می‌ماند، نه این‌که حذف شود.

   ⚠️ اصلاح (کد ۳۶۸/۲۰/... واحد یزد، خرداد ۱۴۰۵ کشف شد): اگر یک واحد
   اصلاً نگاشت حساب دستمزد/سربار (CC_UnitAcc.CostKind) نداشته باشد،
   @actWage/@actOh همیشه صفر می‌ماند — بدون گارد، فرمول کالاهای
   ضریب‌دار همین واحد صفر می‌شدند (نابودیِ واقعیِ داده، نه خطای
   بی‌ضرر). حالا وقتی واقعی صفر است ولی کالایی با ضریب هست، هیچ‌کاری
   نمی‌کنیم و فقط هشدار می‌دهیم.

   ⚠️ کالای هم‌زمان چندواحدی (تأیید کاربر، کد ۳۷۳ خرداد ۱۴۰۵ کشف شد):
   HEAD_MANF فقط یک ردیف به‌ازای (CODE, GHEYMAT) دارد — نمی‌تواند
   هم‌زمان نرخ دو واحد را نگه دارد. اگر یک کد در چند واحد تولید شود
   (مثلاً کد ۳۷۳: عمدتاً انبار ۳/واحد اصلی، ولی کمی هم انبار ۸۰۸/یزد)
   و دو واحد برای همان فرمول نرخ‌های متفاوت پیشنهاد بدهند، دیگر
   به‌صورت کورسر (که هرکدام آخر اجرا شود بی‌سروصدا آن یکی را رونویسی
   می‌کرد) پیش نمی‌رویم؛ به‌جایش همه‌ی واحدها یک‌جا (Set-based) پردازش
   می‌شوند، پیشنهادِ هر واحد برای هر فرمول جمع‌آوری می‌شود، و فقط اگر
   پیشنهادها برابر باشند (یا فقط یک واحد پیشنهاد داده باشد) اعمال
   می‌شود؛ در صورت تعارض، هیچ‌کدام اعمال نمی‌شود و فقط هشدار ثبت
   می‌شود.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S07B_SyncLaborRate
    @RunId INT, @Month INT, @DT1 BIGINT, @DT2 BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Total INT = 0;

    -- ۱) دستمزد/سربارِ واقعیِ هر واحد.
    --
    -- ⚠️ اصلاح (تأیید کاربر: «تو داری دستمزد کارخونه را زیاد میزنی که
    -- در ۷۵۱ خودشو نشون میده»): قبلاً این عدد از خودِ حساب ۷۵۱ (تفصیلی
    -- ۹۹۹۹۹۹۹۹) می‌آمد — ولی ۷۵۱ خودش نتیجه‌ی همین فرمول‌هاست (هر برگه‌ی
    -- ورود کالا به انبار، با نرخِ IMBIBE_MANFِ همان لحظه، به ۷۵۱ می‌زند)؛
    -- یعنی اگر نرخِ قبلی زیاد بوده، ۷۵۱ هم زیاد شده و دوباره از رویِ آن
    -- نرخِ بعدی ساختن یعنی تکرارِ همان خطا (حلقه‌ی خودتغذیه). منبعِ
    -- مستقل و واقعی همان چیزی است که S10 هم زیرِ عنوانِ «واقعی» استفاده
    -- می‌کند: CC_UnitAcc (حساب‌های ۷۱۱-۷۴۵ و مشابه، طبقِ تنظیماتِ کاربر)،
    -- نه ۷۵۱.
    SELECT  m.UnitId,
            ISNULL(SUM(CASE WHEN m.CostKind = 1 THEN t.Amount * m.Ratio ELSE 0 END), 0) AS ActWage,
            ISNULL(SUM(CASE WHEN m.CostKind = 2 THEN t.Amount * m.Ratio ELSE 0 END), 0) AS ActOh
    INTO    #UnitActual
    FROM    dbo.CC_UnitAcc m
    CROSS   APPLY (
                SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                FROM   dbo.DEED_DTL d
                JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                  AND  d.HES_K = m.HesKol
                  AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                  AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
            ) t
    WHERE   m.IsActive = 1
    GROUP BY m.UnitId;

    -- ۲) مقدارِ کلِ تولیدِ همین ماه به‌تفکیکِ (واحد، کد)، به‌همراه ضریبِ
    --    وزنِ هر واحدِ MEGHk (کاربر: «مقدار ممکنه وزن نباشه مثلا ۱۸۰
    --    گرم باشه و تعداد زیاد» — یعنی MEGHk خام برای کالاهایی که با
    --    «عدد/بسته» شمرده می‌شوند با کالاهایی که با «کیلوگرم» شمرده
    --    می‌شوند قابلِ‌جمع نیست). WeightFactor از dbo.stuf_def_nfani.
    --    COLN6 می‌آید — همان ستونی که در گزارشِ KALAS خودِ کاربر با
    --    `SUM(CAST(COLN6 AS FLOAT)*MEGHk)` به‌عنوان «وزن» جمع می‌زند
    --    (مثلاً پنیر ۱۸۰گرمی → COLN6=۰.۱۸). وقتی این ستون خالی/غیرِعددی
    --    است یا صفر/منفی، ۱ فرض می‌شود (یعنی MEGHk خودش از قبل واحدِ
    --    وزنی است، مثلِ کالاهای کیلوگرمی که COLN6=۱ دارند).
    SELECT  cua.UnitId, hm.CODE, SUM(pl.MEGHK) AS Qty,
            MAX(ISNULL(NULLIF(TRY_CAST(nf.COLN6 AS FLOAT), 0), 1)) AS WeightFactor
    INTO    #CodeQty
    FROM    dbo.HEAD_LST  h
    JOIN    dbo.INVO_LST  pl  ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm  ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT) AND hm.GHEYMAT = @Month
    JOIN    dbo.CC_UnitAnbar cua ON cua.Anbar = pl.ANBAR AND cua.AnbarRole = 3
    JOIN    dbo.CC_Unit   u   ON u.UnitId  = cua.UnitId AND u.IsActive = 1
    LEFT    JOIN dbo.stuf_def_nfani nf ON nf.CODE = hm.CODE
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
    GROUP BY cua.UnitId, hm.CODE;

    -- مجموعِ وزنیِ ضرایب هر واحد در همین ماه — Σ(ضریب × مقدار ×
    -- ضریبِ‌وزن)، فقط کالاهایی که کاربر برایشان ضریب صریحِ غیرصفر ثبت
    -- کرده. این «مخرجِ مشترک» است: باعث می‌شود نرخِ هر کالا فقط تابعِ
    -- ضریبِ خودش باشد (نه مقدارِ تولیدش)، ولی جمعِ دستمزدِ همه‌ی
    -- کالاها با دستمزدِ واقعی برابر بماند (نگاه کنید توضیحِ فرمولِ
    -- پایین‌تر). ضریب سربارِ مؤثر = ISNULL(OverheadCoefficient, Coefficient).
    SELECT  cq.UnitId,
            ISNULL(SUM(r.Coefficient * cq.Qty * cq.WeightFactor), 0) AS TotalWeight,
            ISNULL(SUM(CASE WHEN ISNULL(r.OverheadCoefficient, r.Coefficient) <> 0
                             THEN ISNULL(r.OverheadCoefficient, r.Coefficient) * cq.Qty * cq.WeightFactor
                             ELSE 0 END), 0) AS TotalWeightOh
    INTO    #UnitWeight
    FROM    #CodeQty cq
    JOIN    dbo.CC_LaborAbsorptionRate r ON r.CODE = cq.CODE AND r.UnitId = cq.UnitId AND r.IsFixed = 0
    WHERE   r.Coefficient IS NOT NULL AND r.Coefficient <> 0
    GROUP BY cq.UnitId;

    -- ⚠️ کالاهای «کارمزدی» (IsFixed=1) از استخرِ تقسیم‌شونده کسر می‌شوند
    -- (تأیید صریحِ کاربر): نرخِ این کالاها ثابت می‌ماند و دست نمی‌خورد،
    -- ولی خودشان هم بخشی از دستمزدِ واقعیِ حسابِ ۷۵۱ را مصرف کرده‌اند —
    -- اگر همین سهم از دستمزدِ واقعی کسر نشود، بقیه‌ی کالاها (که ضریب
    -- دارند) کلِ دستمزدِ واقعی را بینِ خودشان تقسیم می‌کنند، درحالی‌که
    -- کالاهای کارمزدی هم جداگانه دستمزدِ ثابتِ خودشان را نگه داشته‌اند —
    -- یعنی جمعِ کلِ دستمزدِ همه‌ی کالاها بیشتر از دستمزدِ واقعی می‌شود.
    -- پس دستمزد/سربارِ باقی‌مانده برای تقسیم = دستمزدِ واقعی − Σ(مقدار
    -- تولیدِ هر کالای کارمزدی × نرخِ ثابتِ فعلی‌اش).
    SELECT  r.UnitId,
            ISNULL(SUM(q.Qty * hm.IMBIBE_MANF), 0) AS FixedWage,
            ISNULL(SUM(q.Qty * hm.IMBIBE_SAR),  0) AS FixedOh
    INTO    #FixedTotals
    FROM    dbo.CC_LaborAbsorptionRate r
    JOIN    #CodeQty q  ON q.UnitId = r.UnitId AND q.CODE = TRY_CAST(r.CODE AS BIGINT)
    JOIN    dbo.HEAD_MANF hm ON hm.CODE = r.CODE AND hm.GHEYMAT = @Month
    WHERE   r.IsFixed = 1
    GROUP BY r.UnitId;

    -- هشدار صفر بودن واقعی وقتی وزنی هست — ماجرای یزد/خرداد که باعث
    -- شد این گارد اضافه شود.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'واحد ', w.UnitId, N': دستمزد واقعی این واحد (طبق CC_UnitAcc) صفر آمد (احتمالاً نگاشتِ حساب‌ها برای این واحد ناقص است یا هیچ کدی توسط این واحد در این ماه تولید نشده) — تقسیم دستمزد رد شد تا IMBIBE_MANF صفر نشود.')
    FROM   #UnitWeight w
    LEFT   JOIN #UnitActual a ON a.UnitId = w.UnitId
    WHERE  w.TotalWeight <> 0 AND ISNULL(a.ActWage, 0) = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'واحد ', w.UnitId, N': سربار واقعی این واحد (طبق CC_UnitAcc، CostKind=2) صفر آمد (احتمالاً برای این واحد نگاشتِ سربار تعریف نشده یا هیچ کدی توسط این واحد در این ماه تولید نشده) — تقسیم سربار رد شد تا IMBIBE_SAR صفر نشود.')
    FROM   #UnitWeight w
    LEFT   JOIN #UnitActual a ON a.UnitId = w.UnitId
    WHERE  w.TotalWeightOh <> 0 AND ISNULL(a.ActOh, 0) = 0;

    -- هشدار وقتی سهمِ کالاهای کارمزدی از دستمزدِ واقعی بیشتر است —
    -- یعنی چیزی در نرخِ ثابتِ آن‌ها یا در خودِ دستمزدِ واقعی مشکوک
    -- است؛ باقی‌مانده منفی می‌شد، پس تقسیم برای بقیه‌ی کالاها هم رد
    -- شد (نه فقط صفر شدنِ کارمزدی‌ها، که اصلاً دست‌نخورده‌اند).
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'واحد ', a.UnitId, N': مجموعِ دستمزدِ کالاهای کارمزدی (', FORMAT(ft.FixedWage, 'N0'),
                  N') از دستمزدِ واقعیِ کلِ واحد (', FORMAT(a.ActWage, 'N0'),
                  N') بیشتر است — تقسیمِ دستمزد برای بقیه‌ی کالاها رد شد.')
    FROM   #UnitActual a
    JOIN   #FixedTotals ft ON ft.UnitId = a.UnitId
    WHERE  a.ActWage - ft.FixedWage <= 0 AND ft.FixedWage <> 0;

    -- هشدار: کالایی که این ماه تولید شده ولی هیچ سطری در
    -- CC_LaborAbsorptionRate ندارد.
    --
    -- ⚠️ چرا لازم است: JOIN به CC_LaborAbsorptionRate در ساخت #Proposals
    -- (پایین) از نوع INNER است، پس چنین کالایی اصلاً وارد محاسبه نمی‌شود و
    -- IMBIBE_MANF/IMBIBE_SAR فرمولش روی مقدار قبلی — معمولاً صفر — می‌ماند.
    -- تا امروز این حالت هیچ خطا و هیچ هشداری نمی‌داد، پس فقط وقتی کشف
    -- می‌شد که کسی دستی سراغ خودِ فرمول برود.
    --
    -- نمونه‌ی واقعی: کد ۳۱۷۰ «پنیر پیتزا موزارلا ۱ کیلویی نازلی»، فرمول
    -- ۸۲۶۰۳۱۶۸۸ مرداد ۱۴۰۵ — هر دو نرخ صفر، بدون هیچ نشانه‌ای.
    --
    -- عمداً فقط هشدار است نه خطا: نبودِ ضریب برای کالای تازه طبیعی است و
    -- نباید جلوی بستنِ ماه را بگیرد؛ فقط باید دیده شود.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'کالای ', cq.CODE, N' «', ISNULL(sd.NAME, N'؟'), N'» در واحد ', cq.UnitId,
                  N' این ماه تولید شده ولی ضریب جذب دستمزد/سربار برایش تعریف نشده — ',
                  N'دستمزد و سربار فرمولش دست‌نخورده ماند. ',
                  N'از بخش «نرخ جذب دستمزد» ضریبش را ثبت کنید.')
    FROM   #CodeQty cq
    LEFT   JOIN dbo.STUF_DEF sd ON sd.CODE = cq.CODE
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.CC_LaborAbsorptionRate r
                       WHERE r.CODE = cq.CODE AND r.UnitId = cq.UnitId);

    -- ۳) پیشنهادِ نرخ هر (واحد، فرمول) این ماه (تأیید کاربر: نرخِ پایه
    --    = دستمزدِ واقعی ÷ مجموعِ وزنی، بعد در ضریب و وزنِ خودِ کالا
    --    ضرب می‌شود — نه تقسیم بر مقدارِ تولیدِ خودِ کالا. نتیجه: دو
    --    کالای هم‌ضریب/هم‌وزن نرخِ واحدِ یکسان می‌گیرند، ولی چون نرخ در
    --    مقدار ضرب می‌شود، جمعِ دستمزدشان به‌نسبتِ تولید فرق می‌کند):
    --      IsFixed=1        → NULL (کارمزدی، هرگز دست نمی‌خورد)
    --      Coefficient=0    → 0    (کاربر عمداً کنار گذاشته)
    --      Coefficient=NULL → NULL (هنوز بررسی نشده، دست نمی‌خورد)
    --      باقی‌ماندهٔ واقعی (پس از کسرِ سهمِ کارمزدی‌ها) صفر/منفی، یا
    --      مقدارِ خودِ کالا صفر → NULL (نرخ ناقص می‌شد، دست نمی‌خورد —
    --                                  هشدار بالا)
    --      وگرنه            → باقی‌ماندهٔ واقعی × ضریب × ضریبِ‌وزنِ خودِ
    --                          کالا ÷ مجموعِ وزنی
    ;WITH ThisMonthFormula AS (
        SELECT DISTINCT hm.FNUMB, hm.CODE, cua.UnitId
        FROM   dbo.HEAD_LST h
        JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = TRY_CAST(pl.N_KOL AS INT) AND hm.GHEYMAT = @Month
        JOIN   dbo.CC_UnitAnbar cua ON cua.Anbar = pl.ANBAR AND cua.AnbarRole = 3
        JOIN   dbo.CC_Unit u ON u.UnitId = cua.UnitId AND u.IsActive = 1
        WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
    )
    SELECT  f.FNUMB, f.CODE, f.UnitId,
            CASE WHEN r.IsFixed = 1                                      THEN NULL
                 WHEN r.Coefficient = 0                                  THEN 0
                 WHEN r.Coefficient IS NULL                              THEN NULL
                 WHEN ISNULL(a.ActWage, 0) - ISNULL(ft.FixedWage, 0) <= 0
                      OR ISNULL(w.TotalWeight, 0) = 0 OR ISNULL(q.Qty, 0) = 0 THEN NULL
                 ELSE (a.ActWage - ISNULL(ft.FixedWage, 0)) * r.Coefficient * q.WeightFactor / w.TotalWeight
            END AS ProposedWage,
            CASE WHEN r.IsFixed = 1                                      THEN NULL
                 WHEN ISNULL(r.OverheadCoefficient, r.Coefficient) = 0    THEN 0
                 WHEN ISNULL(r.OverheadCoefficient, r.Coefficient) IS NULL THEN NULL
                 WHEN ISNULL(a.ActOh, 0) - ISNULL(ft.FixedOh, 0) <= 0
                      OR ISNULL(w.TotalWeightOh, 0) = 0 OR ISNULL(q.Qty, 0) = 0 THEN NULL
                 ELSE (a.ActOh - ISNULL(ft.FixedOh, 0)) * ISNULL(r.OverheadCoefficient, r.Coefficient) * q.WeightFactor / w.TotalWeightOh
            END AS ProposedOh
    INTO    #Proposals
    FROM    ThisMonthFormula f
    JOIN    dbo.CC_LaborAbsorptionRate r ON r.CODE = f.CODE AND r.UnitId = f.UnitId
    LEFT    JOIN #UnitActual a ON a.UnitId = f.UnitId
    LEFT    JOIN #UnitWeight w ON w.UnitId = f.UnitId
    LEFT    JOIN #CodeQty   q ON q.UnitId = f.UnitId AND q.CODE = f.CODE
    LEFT    JOIN #FixedTotals ft ON ft.UnitId = f.UnitId;

    DELETE FROM #Proposals WHERE ProposedWage IS NULL AND ProposedOh IS NULL;

    -- ۴) تعارض بین واحدها روی یک فرمولِ مشترک (کد چندواحدی): اگر
    --    پیشنهادهای واحدهای مختلف برای همین فرمول فرق کنند، هیچ‌کدام
    --    اعمال نمی‌شود و فقط هشدار ثبت می‌شود — دستمزد/سربار جداگانه
    --    بررسی می‌شوند چون ممکن است فقط یکی از این دو تعارض داشته باشد.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'کالای ', CODE, N' هم‌زمان در چند واحد تولید می‌شود و ضریب دستمزدشان به نرخ‌های متفاوت می‌رسد (',
                  FORMAT(MinW, 'N2'), N' در برابر ', FORMAT(MaxW, 'N2'),
                  N') — چون فرمول این کالا فقط یک نرخ می‌تواند داشته باشد، دستمزدش دست‌نخورده ماند.')
    FROM   (SELECT FNUMB, CODE, MIN(ProposedWage) AS MinW, MAX(ProposedWage) AS MaxW, COUNT(*) AS N
            FROM   #Proposals WHERE ProposedWage IS NOT NULL GROUP BY FNUMB, CODE) wa
    WHERE  wa.N > 1 AND ABS(wa.MaxW - wa.MinW) > 0.01;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'کالای ', CODE, N' هم‌زمان در چند واحد تولید می‌شود و ضریب سربارشان به نرخ‌های متفاوت می‌رسد (',
                  FORMAT(MinO, 'N2'), N' در برابر ', FORMAT(MaxO, 'N2'),
                  N') — چون فرمول این کالا فقط یک نرخ می‌تواند داشته باشد، سربارش دست‌نخورده ماند.')
    FROM   (SELECT FNUMB, CODE, MIN(ProposedOh) AS MinO, MAX(ProposedOh) AS MaxO, COUNT(*) AS N
            FROM   #Proposals WHERE ProposedOh IS NOT NULL GROUP BY FNUMB, CODE) oa
    WHERE  oa.N > 1 AND ABS(oa.MaxO - oa.MinO) > 0.01;

    -- ۵) اعمال — فقط جایی که تعارضی نیست (تک‌واحدی، یا همه‌ی واحدها
    --    یک نرخ پیشنهاد داده‌اند).
    DECLARE @WageRows INT = 0, @OhRows INT = 0;

    UPDATE hm
       SET hm.IMBIBE_MANF = wa.MinW
    FROM   dbo.HEAD_MANF hm
    JOIN   (SELECT FNUMB, CODE, MIN(ProposedWage) AS MinW, MAX(ProposedWage) AS MaxW
            FROM   #Proposals WHERE ProposedWage IS NOT NULL GROUP BY FNUMB, CODE) wa
           ON wa.FNUMB = hm.FNUMB AND wa.CODE = hm.CODE
    WHERE  hm.GHEYMAT = @Month
      AND  ABS(wa.MaxW - wa.MinW) <= 0.01;

    SET @WageRows = @@ROWCOUNT;
    SET @Total   += @WageRows;

    UPDATE hm
       SET hm.IMBIBE_SAR = oa.MinO
    FROM   dbo.HEAD_MANF hm
    JOIN   (SELECT FNUMB, CODE, MIN(ProposedOh) AS MinO, MAX(ProposedOh) AS MaxO
            FROM   #Proposals WHERE ProposedOh IS NOT NULL GROUP BY FNUMB, CODE) oa
           ON oa.FNUMB = hm.FNUMB AND oa.CODE = hm.CODE
    WHERE  hm.GHEYMAT = @Month
      AND  ABS(oa.MaxO - oa.MinO) <= 0.01;

    SET @OhRows = @@ROWCOUNT;

    -- ⚠️ قبلاً @Total = @WageRows + @OhRows بود — چون تقریباً هر فرمول
    -- هم‌زمان هم دستمزدش هم سربارش آپدیت می‌شود، این عدد هر فرمول را
    -- دوبار می‌شمرد (مثلاً ۹۴+۹۴=۱۸۸ برای ۹۴ فرمولِ واقعی) و در پیام
    -- «X فرمول به‌روزرسانی شد» (RateEngineSteps.cs) گمراه‌کننده بود.
    -- حالا @Total = تعدادِ فرمولِ یکتایی که حداقل یکی از دو مقدارش
    -- عوض شده، مطابق همان (FNUMB, CODE)ای که در دو UPDATE بالا هدف
    -- قرار گرفت.
    SELECT @Total = COUNT(*)
    FROM (
        SELECT FNUMB, CODE
        FROM   (SELECT FNUMB, CODE, MIN(ProposedWage) AS MinW, MAX(ProposedWage) AS MaxW
                FROM   #Proposals WHERE ProposedWage IS NOT NULL GROUP BY FNUMB, CODE) wa
        WHERE  ABS(wa.MaxW - wa.MinW) <= 0.01
        UNION
        SELECT FNUMB, CODE
        FROM   (SELECT FNUMB, CODE, MIN(ProposedOh) AS MinO, MAX(ProposedOh) AS MaxO
                FROM   #Proposals WHERE ProposedOh IS NOT NULL GROUP BY FNUMB, CODE) oa
        WHERE  ABS(oa.MaxO - oa.MinO) <= 0.01
    ) touched;

    -- ⚠️ قبلاً فقط در حالت هشدار/تعارض چیزی در CC_RunLog ثبت می‌شد —
    -- یک اجرای موفقِ بی‌مشکل هیچ ردی در لاگ اجرا نمی‌گذاشت (تأیید
    -- کاربر: «لاگ نمی‌زنه»). حالا همیشه یک خلاصه ثبت می‌شود، عیناً
    -- سبکِ لاگِ S10.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S07B', 1,
            CONCAT(N'دستمزد ', @WageRows, N' فرمول و سربار ', @OhRows, N' فرمول به‌روزرسانی شد (', @Total, N' فرمولِ یکتا).'));

    DROP TABLE #UnitActual;
    DROP TABLE #CodeQty;
    DROP TABLE #UnitWeight;
    DROP TABLE #FixedTotals;
    DROP TABLE #Proposals;

    SELECT @Total AS Value;
END
GO

/* ═══════════════════════════════════════════════════════════════════
   S10 — تراز هزینه تبدیل به تفکیک واحد تولیدی

   جذب‌شده = Σ (مقدار تولید × نرخ جذب فرمول)
   واقعی   = Σ (مانده سرفصل × ضریب سهم)   طبق CC_UnitAcc
   ضریب    = واقعی ÷ جذب‌شده

   کنترل متقابل: به شرط صفر بودن کار در جریان، جذب باید با
   گردش بستانکار حساب ۷۵۱ با تفصیلی 99999999 برابر باشد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S10_BalanceConversion
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TafDastmozd BIGINT = 99999999;   -- دستمزد (و روي اين پايگاه‌داده: سربار هم همين‌جا)
    DECLARE @TafSarbar   BIGINT = 99999998;   -- سربار، فقط وقتي نصب از دستمزد جدايش کرده باشد
    DECLARE @UnitId INT, @SplitMode TINYINT;

    -- تشخيص واحد از روي دپارتمان کنار گذاشته شد: دپارتمان را اپراتور دستي
    -- روي برگه مي‌زند و اشتباه تايپي رايج است. ملاک مطمئن، انباري است که
    -- محصول توليدشده وارد آن مي‌شود (CC_UnitAnbar.AnbarRole = 3، «محصول»)
    -- — همان چيزي که در تنظيمات واحدها از قبل تعريف شده و کاربر تأييد
    -- کرد بايد ملاک باشد (نه Depatman). CHK-16 (S00) از قبل هر انباري که
    -- برگه توليد دارد ولي به هيچ واحدي وصل نيست را هشدار مي‌دهد.
    --
    -- ريسک مشابهِ حالت قبلي (Depatman=NULL تکراري) اينجا اين است: اگر يک
    -- انبارِ «محصول» به بيش از يک واحد فعال وصل باشد، هر دو دقيقاً همان
    -- برگه‌ها را پردازش مي‌کنند و چون اين حلقه IMBIBE_MANF/IMBIBE_SAR را
    -- مستقيماً در HEAD_MANF ويرايش مي‌کند، واحد دوم رويِ مقدارِ از‌قبل‌
    -- تعديل‌شده‌ي واحد اول دوباره ضريب مي‌زند — فرمول‌ها خراب مي‌شوند.
    IF EXISTS (
        SELECT ua.Anbar
        FROM   dbo.CC_UnitAnbar ua
        JOIN   dbo.CC_Unit      u  ON u.UnitId = ua.UnitId AND u.IsActive = 1
        WHERE  ua.AnbarRole = 3
        GROUP  BY ua.Anbar
        HAVING COUNT(DISTINCT ua.UnitId) > 1
    )
    BEGIN
        RAISERROR(N'يک انبار محصول (نقش «محصول») به بيش از يک واحد توليدي فعال وصل است؛ اين باعث پردازش دوباره‌ي همان برگه‌ها و خراب شدن فرمول‌ها مي‌شود. نگاشت انبار⇄واحد را در تنظیمات اصلاح کنيد.', 16, 1);
        RETURN;
    END

    DELETE dbo.CC_ConversionCost WHERE RunId = @RunId;
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND StepCode = 'S10' AND RuleCode = 'CHK-08';

    DECLARE cUnit CURSOR LOCAL FAST_FORWARD FOR
        SELECT UnitId, SplitMode
        FROM   dbo.CC_Unit WHERE IsActive = 1 ORDER BY SeqNo;

    OPEN cUnit;
    FETCH NEXT FROM cUnit INTO @UnitId, @SplitMode;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        ---- ۱) جذب‌شده از برگه‌هاي توليد اين واحد (بر اساس انبار محصول)
        DECLARE @absWage FLOAT, @absOh FLOAT;

        -- کالاهای کارمزدی از این جمع کنار می‌مانند (تأیید کاربر): نرخِ
        -- S07B هم دقیقاً همین کار را می‌کند — دستمزدِ واقعیِ CC_UnitAcc
        -- را منهایِ سهمِ کارمزدی‌ها بین بقیه تقسیم می‌کند؛ پس «جذب‌شده»
        -- باید همان محدوده (فقط غیرکارمزدی) را داشته باشد تا با «واقعی»
        -- (که حالا S07B هم از همین CC_UnitAcc می‌گیرد، نه از ۷۵۱) در یک
        -- پاس دقیقاً جفت شود، بدون نیاز به همگراییِ چندمرحله‌ای.
        SELECT  @absWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @absOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND   pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                              WHERE UnitId = @UnitId AND AnbarRole = 3)
          AND   NOT EXISTS (
                    SELECT 1 FROM dbo.CC_LaborAbsorptionRate fx
                    WHERE fx.UnitId = @UnitId AND fx.CODE = hm.CODE AND fx.IsFixed = 1
                );

        DECLARE @absTotal FLOAT = @absWage + @absOh;

        ---- ۲) کنترل متقابل با حساب ۷۵۱، به تفکيک واحد و به تفکيک دستمزد/سربار
        -- HES_M روي اين رديف‌ها کدِ خودِ کالاي توليدشده است (نه يک معينِ
        -- عمومي) — کاربر تأييد کرد و مستقيماً تست شد: تمام HES_M هاي اين
        -- تفصيلي دقيقاً با STUF_DEF.CODE مطابقت دارند. پس مي‌شود دقيقاً
        -- همان مجموعه کدهايي را که اين واحد در همين بازه توليد کرده
        -- (زيرکوئري پايين، عيناً منطق جذب‌شده در بالا) فيلتر کرد و مانده
        -- ۷۵۱ را per-واحد گرفت، نه فقط جمع کل شرکت.
        --
        -- ⚠️ @TafSarbar (سربار، ۹۹۹۹۹۹۹۸) روي اين پايگاه‌داده استفاده
        -- نمي‌شود — همه‌ي دستمزد و سربار زير همان @TafDastmozd (۹۹۹۹۹۹۹۹)
        -- ثبت مي‌شوند (کاربر تأييد کرد). ولي روي نصب‌هاي ديگر ممکن است
        -- اين دو را جدا کنند؛ اگر اينجا فقط @TafDastmozd را چک مي‌کرديم،
        -- روي چنان پايگاه‌داده‌اي سهمِ سربار از مانده ۷۵۱ اصلاً ديده
        -- نمي‌شد و کنترل CHK-08 دقيقاً به‌اندازه‌ي سربار غلط مي‌شد. پس هر
        -- دو تفصيلي را جدا جمع مي‌زنيم؛ هر کدام که در اين پايگاه‌داده
        -- خالي باشد صفر مي‌ماند و به کنترل کل آسيبي نمي‌زند.
        --
        -- ⚠️ اصلاح (تأیید کاربر، کد ۳۷۳ خرداد ۱۴۰۵ کشف شد، عیناً همان
        -- تصحیح در S07B بالاتر): وقتی یک کد هم‌زمان در چند واحد تولید
        -- می‌شود، IN ساده مبلغِ کامل ۷۵۱ آن کد را به هر واحدی که کد را
        -- تولید کرده کامل می‌افزود (دوبارشماری در CHK-08). حالا مبلغ هر
        -- کد به نسبتِ سهمِ مقداریِ (MEGHK) این واحد از کل تولید همان کد
        -- در همین ماه تقسیم می‌شود.
        DECLARE @absWipWage FLOAT, @absWipOh FLOAT, @absWip FLOAT;

        ;WITH CodeAmt AS (
            SELECT  TRY_CAST(d.HES_M AS BIGINT) AS CODE,
                    SUM(CASE WHEN d.HES_T = @TafDastmozd THEN d.BES - d.BED ELSE 0 END) AS WageAmt,
                    SUM(CASE WHEN d.HES_T = @TafSarbar   THEN d.BES - d.BED ELSE 0 END) AS OhAmt
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
            WHERE   d.HES_K = 751 AND d.HES_T IN (@TafDastmozd, @TafSarbar)
              AND   hd.DATE_S BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(d.HES_M AS BIGINT)
        ),
        CodeQty AS (
            SELECT  TRY_CAST(pl.CODE AS BIGINT) AS CODE, cua.UnitId, SUM(pl.MEGHK) AS Qty
            FROM    dbo.HEAD_LST h
            JOIN    dbo.INVO_LST pl      ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
            JOIN    dbo.CC_UnitAnbar cua ON cua.Anbar  = pl.ANBAR AND cua.AnbarRole = 3
            JOIN    dbo.CC_Unit u        ON u.UnitId   = cua.UnitId AND u.IsActive = 1
            WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(pl.CODE AS BIGINT), cua.UnitId
        ),
        CodeTotalQty AS (
            SELECT CODE, SUM(Qty) AS TotalQty FROM CodeQty GROUP BY CODE
        )
        -- ⚠️ اصلاح (تأیید کاربر: «کنترلِ از ۷۵۱ باید کاملِ تفصیلی ۹۹۹۹۹۹۹۹
        -- را جمع بزند، کارمزدی‌ها هم توش باشد»): یک نسخه‌ی قبلی این‌جا
        -- سهمِ کالاهای کارمزدی را هم کنار می‌گذاشت تا با «جذب‌شده» (که
        -- عمداً کارمزدی‌ها را کنار می‌گذارد) هم‌محدوده شود — ولی این خودِ
        -- «کنترل از ۷۵۱» را اشتباه می‌کرد: این ستون قرار است یک کنترلِ
        -- مستقل و کاملِ گردشِ واقعیِ حساب باشد، نه مقیدشده به همان
        -- محدوده‌ای که روشِ جذب فعلاً استفاده می‌کند. پس هیچ کدی
        -- (کارمزدی یا نه) از این جمع کنار گذاشته نمی‌شود.
        SELECT  @absWipWage = ISNULL(SUM(ca.WageAmt * cq.Qty / ctq.TotalQty), 0),
                @absWipOh   = ISNULL(SUM(ca.OhAmt   * cq.Qty / ctq.TotalQty), 0)
        FROM    CodeAmt ca
        JOIN    CodeQty cq       ON cq.CODE  = ca.CODE AND cq.UnitId = @UnitId
        JOIN    CodeTotalQty ctq ON ctq.CODE = ca.CODE
        WHERE   ctq.TotalQty <> 0;

        SET @absWip = @absWipWage + @absWipOh;

        ---- ۳) واقعي از تراز، طبق نگاشت قابل ويرايش کاربر
        DECLARE @actWage FLOAT, @actOh FLOAT;

        -- CROSS APPLY نه JOIN روي جمعِ از‌قبل‌گروه‌بندی‌شده، چون هر سطر
        -- CC_UnitAcc ممکن است سطح معین/تفصیلی متفاوتی مشخص کرده باشد؛
        -- خالی‌بودن هرکدام یعنی «همهٔ آن سطح» (نگاشت گسترده‌تر، مثل قبل).
        SELECT  @actWage = ISNULL(SUM(CASE WHEN m.CostKind = 1
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0),
                @actOh   = ISNULL(SUM(CASE WHEN m.CostKind = 2
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0)
        FROM    dbo.CC_UnitAcc m
        CROSS   APPLY (
                    SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                    FROM   dbo.DEED_DTL d
                    JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                    WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                      AND  d.HES_K = m.HesKol
                      AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                      AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
                ) t
        WHERE   m.IsActive = 1 AND m.UnitId = @UnitId;

        -- ⚠️ اصلاح (تأیید کاربر: نرخِ S07B هم دستمزدِ واقعی را منهایِ
        -- سهمِ کارمزدی‌ها می‌کند قبل از تقسیم): «جذب‌شده» بالا عمداً
        -- کارمزدی‌ها را ندارد، پس «واقعی» هم باید همین سهم کم شود، وگرنه
        -- ضریب k یک تفاوتِ کاذب (دقیقاً به‌اندازه‌ی دستمزدِ کارمزدی‌ها)
        -- نشان می‌دهد و همه‌ی نرخ‌های غیرکارمزدی را غلط تعدیل می‌کند.
        DECLARE @fixedWage FLOAT, @fixedOh FLOAT;

        SELECT  @fixedWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @fixedOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        JOIN    dbo.CC_LaborAbsorptionRate fx ON fx.UnitId = @UnitId AND fx.CODE = hm.CODE AND fx.IsFixed = 1
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND   pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                              WHERE UnitId = @UnitId AND AnbarRole = 3);

        SET @actWage = @actWage - @fixedWage;
        SET @actOh   = @actOh   - @fixedOh;

        DECLARE @actTotal FLOAT = @actWage + @actOh;

        ---- ۴) ضريب تعديل
        DECLARE @kWage FLOAT = 1, @kOh FLOAT = 1;

        IF @absTotal <> 0
        BEGIN
            IF @SplitMode = 1                    -- يک ضريب براي کل هزينه تبديل
            BEGIN
                DECLARE @k FLOAT = @actTotal / @absTotal;
                SET @kWage = @k;
                SET @kOh   = @k;
            END
            ELSE                                 -- دو ضريب مجزا
            BEGIN
                SET @kWage = CASE WHEN @absWage <> 0 THEN @actWage / @absWage ELSE 1 END;
                SET @kOh   = CASE WHEN @absOh   <> 0 THEN @actOh   / @absOh   ELSE 1 END;
            END
        END

        ---- ۵) ثبت نتيجه
        INSERT dbo.CC_ConversionCost
            (RunId, UnitId, CostKind, AbsorbedAmount, AbsorbedFromWip,
             ActualAmount, AdjustFactor, ActualDetailJson)
        VALUES
            (@RunId, @UnitId, 0, @absTotal, @absWip, @actTotal,
             CASE WHEN @absTotal <> 0 THEN @actTotal / @absTotal ELSE 1 END,
             (SELECT m.HesKol, m.HesMoin, m.HesTafsili, m.CostKind, m.Ratio
              FROM   dbo.CC_UnitAcc m
              WHERE  m.UnitId = @UnitId AND m.IsActive = 1
              FOR JSON PATH)),
            (@RunId, @UnitId, 1, @absWage, @absWipWage, @actWage, @kWage, NULL),
            (@RunId, @UnitId, 2, @absOh,   @absWipOh,   @actOh,   @kOh,   NULL);

        ---- ۶) هشدار اختلاف کنترلي
        --
        -- ⚠️ اصلاح (همان باگِ CHK-09، اینجا هم پیدا شد): @absWip بالا عمداً
        -- کاملِ ۷۵۱ است (تأیید کاربر، برای ستونِ نمایشیِ «کنترل از ۷۵۱»)،
        -- ولی @absTotal («جذب‌شده») عمداً کارمزدی‌ها را ندارد — این دو
        -- همیشه به‌اندازه‌ی دستمزدِ کارمزدی‌ها فرق دارند و مقایسه‌ی مستقیم‌شان
        -- همیشه یک هشدارِ کاذب می‌سازد. اینجا برای خودِ این چک یک نسخه‌ی
        -- کارمزدی‌نتّشده از ۷۵۱ می‌سازیم — دقیقاً همان استثنایی که @absWage
        -- بالا هم دارد.
        DECLARE @absWipFixed FLOAT;

        ;WITH CodeAmt2 AS (
            SELECT  TRY_CAST(d.HES_M AS BIGINT) AS CODE,
                    SUM(CASE WHEN d.HES_T = @TafDastmozd THEN d.BES - d.BED ELSE 0 END) AS WageAmt,
                    SUM(CASE WHEN d.HES_T = @TafSarbar   THEN d.BES - d.BED ELSE 0 END) AS OhAmt
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
            WHERE   d.HES_K = 751 AND d.HES_T IN (@TafDastmozd, @TafSarbar)
              AND   hd.DATE_S BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(d.HES_M AS BIGINT)
        ),
        CodeQty2 AS (
            SELECT  TRY_CAST(pl.CODE AS BIGINT) AS CODE, cua.UnitId, SUM(pl.MEGHK) AS Qty
            FROM    dbo.HEAD_LST h
            JOIN    dbo.INVO_LST pl      ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
            JOIN    dbo.CC_UnitAnbar cua ON cua.Anbar  = pl.ANBAR AND cua.AnbarRole = 3
            JOIN    dbo.CC_Unit u        ON u.UnitId   = cua.UnitId AND u.IsActive = 1
            WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(pl.CODE AS BIGINT), cua.UnitId
        ),
        CodeTotalQty2 AS (
            SELECT CODE, SUM(Qty) AS TotalQty FROM CodeQty2 GROUP BY CODE
        )
        SELECT  @absWipFixed = ISNULL(SUM(ca.WageAmt * cq.Qty / ctq.TotalQty), 0)
                              + ISNULL(SUM(ca.OhAmt   * cq.Qty / ctq.TotalQty), 0)
        FROM    CodeAmt2 ca
        JOIN    CodeQty2 cq       ON cq.CODE  = ca.CODE AND cq.UnitId = @UnitId
        JOIN    CodeTotalQty2 ctq ON ctq.CODE = ca.CODE
        WHERE   ctq.TotalQty <> 0
          AND   NOT EXISTS (
                    SELECT 1 FROM dbo.CC_LaborAbsorptionRate fx
                    WHERE fx.UnitId = @UnitId AND TRY_CAST(fx.CODE AS BIGINT) = ca.CODE AND fx.IsFixed = 1
                );

        IF ABS(@absWipFixed - @absTotal) > 10000000
            INSERT dbo.CC_Exception
                (RunId, StepCode, RuleCode, ExType, Severity, Amount, Description)
            VALUES (@RunId, 'S10', 'CHK-08', 10, 1, @absWipFixed - @absTotal,
                    CONCAT(N'اختلاف جذب: برگه‌هاي توليد ', FORMAT(@absTotal, 'N0'),
                           N' در برابر حساب ۷۵۱ (بدونِ کارمزدی‌ها) ', FORMAT(@absWipFixed, 'N0')));

        ---- ۷) اعمال ضريب روي فرمول‌هاي کالاهاي توليدشده در اين واحد
        IF @WhatIf = 0 AND (@kWage <> 1 OR @kOh <> 1)
        BEGIN
            BEGIN TRAN;

            UPDATE  hm
               SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF * @kWage,
                    hm.IMBIBE_SAR  = hm.IMBIBE_SAR  * @kOh
            OUTPUT  @RunId, 'S10', inserted.FNUMB,
                    TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
                    deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
                    CONCAT(N'ضريب تعديل هزينه تبديل ', FORMAT(@kWage, 'N5'))
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.HEAD_MANF hm
            WHERE   hm.GHEYMAT = @Month
              AND   EXISTS (
                        SELECT 1
                        FROM   dbo.HEAD_LST h
                        JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                        WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                          AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
                          AND  pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                                            WHERE UnitId = @UnitId AND AnbarRole = 3))
              AND   NOT EXISTS (
                        SELECT 1 FROM dbo.CC_LaborAbsorptionRate fx
                        WHERE fx.UnitId = @UnitId AND fx.CODE = hm.CODE AND fx.IsFixed = 1
                   );

            INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
            VALUES (@RunId, 'S10', 1,
                    CONCAT(N'واحد ', @UnitId, N': ضريب تعديل ',
                           FORMAT(@kWage, 'N5'), N' روي ', @@ROWCOUNT, N' فرمول'));

            COMMIT;
        END

        FETCH NEXT FROM cUnit INTO @UnitId, @SplitMode;
    END

    CLOSE cUnit;
    DEALLOCATE cUnit;

    ---- خلاصه
    SELECT  u.UnitName                          AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل هزينه تبديل'
                            WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END   AS نوع,
            c.AbsorbedAmount                    AS جذب_شده,
            c.AbsorbedFromWip                   AS کنترل_از_751,
            c.ActualAmount                      AS واقعي,
            c.AdjustFactor                      AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S11 — انتشار نرخ، نسخه تولیدی

   سطح‌بندی درخت فرمول، سپس محاسبه از عمیق‌ترین سطح به سطح صفر.
   نتیجه در DTL_MANF نوشته و در CC_FormulaChange ثبت می‌شود.

   یک پاس، قطعی، بدون تکرار.
   ⚠️ يک کالا مي‌تواند در همان ماه بيش از يک فرمول فعال داشته باشد (مثلاً
   روزهاي مختلف با ترکيب مواد متفاوت توليد شده باشد) — طبق تأييد صاحب
   پروژه اين طبيعي است، نه خطاي داده. نسخه‌ي قبلي فقط يک فرمول را با
   TOP 1 (آخرين DATE_ACTIV/FNUMB) براي محاسبه و انتشار انتخاب مي‌کرد؛
   بهاي «خودِ» کالا حالا ميانگين موزونِ بهاي همه‌ي فرمول‌هاي فعالش است،
   وزن‌دهي‌شده با مقدار واقعيِ توليدشده زيرِ هرکدام در بازه‌ي @DT1..@DT2
   (از HEAD_LST/INVO_LST TAG=9، N_KOL=FNUMB). اگر هيچ‌کدام توليد واقعي
   نداشتند (فرمول تعريف شده ولي هنوز مصرف نشده)، ميانگين ساده جايگزين
   وزن مي‌شود — دقيقاً همان قاعده‌اي که CHK-09 در S00 هم استفاده مي‌کند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S11_PropagateRates
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── ۱) يال‌هاي درخت ─── */
    IF OBJECT_ID('tempdb..#Edge') IS NOT NULL DROP TABLE #Edge;

    SELECT  DISTINCT
            CAST(h.CODE AS BIGINT) AS Parent,
            CAST(d.CODE AS BIGINT) AS Child
    INTO    #Edge
    FROM    dbo.HEAD_MANF h
    JOIN    dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE   h.GHEYMAT = @Month
      AND   h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND   CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);

    CREATE CLUSTERED INDEX IX_Edge ON #Edge(Parent, Child);

    /* ─── ۲) تشخيص حلقه؛ بدون اين، محاسبه بي‌نهايت مي‌شود ─── */
    IF OBJECT_ID('tempdb..#Cycle') IS NOT NULL DROP TABLE #Cycle;
    CREATE TABLE #Cycle (Code BIGINT PRIMARY KEY);

    ;WITH Walk AS (
        SELECT  Parent AS Root, Child, 1 AS Lvl,
                CAST('/' + CAST(Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM    #Edge
        UNION ALL
        SELECT  w.Root, e.Child, w.Lvl + 1,
                CAST(w.Pt + CAST(e.Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM    Walk w JOIN #Edge e ON e.Parent = w.Child
        WHERE   w.Lvl < 20
          AND   w.Pt NOT LIKE '%/' + CAST(e.Child AS VARCHAR(20)) + '/%'
    )
    INSERT #Cycle(Code)
    SELECT DISTINCT Root FROM Walk WHERE Child = Root
    OPTION (MAXRECURSION 0);

    IF EXISTS (SELECT 1 FROM #Cycle)
    BEGIN
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
        SELECT @RunId, 'S11', 'CHK-06', 5, 2, Code,
               N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
        FROM   #Cycle;

        RAISERROR(N'حلقه در ساختار فرمول يافت شد؛ محاسبه متوقف شد.', 16, 1);
        RETURN;
    END

    /* ─── ۳) سطح‌بندي ─── */
    IF OBJECT_ID('tempdb..#C') IS NOT NULL DROP TABLE #C;

    CREATE TABLE #C (
        Code       BIGINT PRIMARY KEY,
        Llc        SMALLINT NOT NULL DEFAULT 0,
        HasFormula BIT      NOT NULL DEFAULT 0,
        Src        TINYINT  NOT NULL DEFAULT 1,
        Mat        FLOAT    NOT NULL DEFAULT 0,
        Wage       FLOAT    NOT NULL DEFAULT 0,
        Oh         FLOAT    NOT NULL DEFAULT 0
    );

    INSERT #C (Code)
    SELECT Parent FROM #Edge UNION SELECT Child FROM #Edge;

    DECLARE @changed INT = 1, @guard INT = 0;

    WHILE @changed > 0 AND @guard < 30
    BEGIN
        UPDATE  c
           SET  c.Llc = x.NewLlc
        FROM    #C c
        JOIN   (SELECT e.Child, MAX(p.Llc) + 1 AS NewLlc
                FROM   #Edge e JOIN #C p ON p.Code = e.Parent
                GROUP BY e.Child) x ON x.Child = c.Code
        WHERE   x.NewLlc > c.Llc;

        SET @changed = @@ROWCOUNT;
        SET @guard  += 1;
    END

    CREATE INDEX IX_C_Llc ON #C(Llc);

    /* ─── ۳ب) فرمول‌هاي هر کالا در اين ماه — ممکن است بيش از يکي باشد ───
       #F جايگزينِ ستون تکيِ #C.FNUMB قبلي است: هر رديف يک فرمول فعال است،
       با مقدار واقعيِ توليدشده زيرش (Qty) که وزنِ ميانگين‌گيري مي‌شود. */
    IF OBJECT_ID('tempdb..#F') IS NOT NULL DROP TABLE #F;

    CREATE TABLE #F (
        FNUMB INT    PRIMARY KEY,
        Code  BIGINT NOT NULL,
        Qty   FLOAT  NOT NULL DEFAULT 0,
        Wage  FLOAT  NOT NULL DEFAULT 0,
        Oh    FLOAT  NOT NULL DEFAULT 0,
        Mat   FLOAT  NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_F_Code ON #F(Code);

    INSERT #F (FNUMB, Code, Qty, Wage, Oh)
    SELECT  hm.FNUMB, CAST(hm.CODE AS BIGINT),
            ISNULL(p.Qty, 0), ISNULL(hm.IMBIBE_MANF, 0), ISNULL(hm.IMBIBE_SAR, 0)
    FROM    dbo.HEAD_MANF hm
    CROSS   APPLY (
                SELECT SUM(pl.MEGHk) AS Qty
                FROM   dbo.HEAD_LST h
                JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                  AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
            ) p
    WHERE   hm.GHEYMAT = @Month AND hm.CODE IS NOT NULL
      AND   EXISTS (SELECT 1 FROM #C c WHERE c.Code = CAST(hm.CODE AS BIGINT));

    UPDATE  c SET c.HasFormula = 1, c.Src = 2
    FROM    #C c
    WHERE   EXISTS (SELECT 1 FROM #F f WHERE f.Code = c.Code);

    /* ─── ۴) نرخ مواد خريدني: ميانگين وزني خروج از انبار ───
       عمداً روي کالاهاي بدون فرمول محدود نيست: نيمه‌ساخته‌اي که خودش هم
       اين ماه از انبار حواله خورده (مثل هر ماده‌ي اوليه‌ي ديگر) بايد
       دقيقاً همان ميانگين واقعيِ انبارش را به‌عنوان نرخِ «خودش وقتي در
       فرمولِ کالاي ديگري مصرف مي‌شود» بگيرد — نه نرخِ تازه‌محاسبه‌شده‌ي
       زنجيره‌ي BOM. کاربر تأييد کرد اين دقيقاً همان چيزي است که مغايرت
       حساب ۷۷۱ را ايجاد مي‌کرد: MaterialIssueRebuildService مبلغ واقعيِ
       حواله (بر مبناي AVRAGE واقعيِ انبار در لحظه‌ي هر تراکنش) را با
       SMABL مقايسه مي‌کند؛ اگر SMABL از ميانگين همان انبار بيايد، دو طرف
       از يک منبع مشتق مي‌شوند و طبيعتاً هم‌خوان مي‌مانند — برخلاف نرخِ
       لحظه‌ايِ بازسازي‌شده‌ي BOM که فقط آخرين قيمتِ اجزا را منعکس مي‌کند،
       نه ميانگينِ واقعيِ کل ماه. نتيجه در ۵-ج پايين‌تر override نمي‌شود
       (شرط Src<>1 آنجا).

       ⚠️ ميرايي (damping) — فيکسِ ناپايداريِ کدهاي چندسطحيِ خودمصرف:
       براي کالايي که هم فرمول دارد هم اين ماه به‌عنوان ماده‌ي اوليه‌ي
       کالاي ديگري حواله خروج مي‌شود (مثل ۳۷۳→۱۷۳۲→۳۳۶۵)، رسيدِ توليدِ
       همين کالا (Case ۹ در AverageRateRebuildService) از SMABL همين
       دورِ S11 قيمت مي‌گيرد؛ آن رسيد وارد ميانگين انبارش مي‌شود؛ همين جا
       آن ميانگين به‌عنوان نرخ رسمي‌اش برمي‌گردد. با S07A تنها (بدون S11
       ميانِ هر دور) اين خودش پايدار و سريع همگراست (تست عملي روي کد
       ۳۳۶۵: ۷۰۷ ریال → ۳۰ → ۳). اما وقتي S11 دوباره‌محاسبه‌شده را به
       DTL_MANF مي‌نويسد و S07A دوباره از همان مي‌خواند، هر سطح از زنجيره
       (۳۷۳، سپس ۱۷۳۲، سپس ۳۳۶۵) کمي تقويتش مي‌کند و رويِ‌هم زنجيره‌ي
       سه‌سطحي واگرا مي‌شود (روي ران ۱۷: ۶۸۶→۱۴۰۹→۲۶۳۴، تقريباً دو برابرِ
       هر دور — سقفِ ۵ دورِ حلقه‌ي همگراييِ CloseOrchestrator با هشدار
       متوقفش مي‌کرد، بدون رسيدن به جواب واقعي).
       راه‌حل: هر دور فقط کسري از تغيير را قبول مي‌کنيم (successive
       under-relaxation، تکنيک استاندارد براي رام‌کردن محاسبه‌ي تکراريِ
       خودارجاع)، نه کل آن را — مقدار قبلي از خودِ CC_ItemCost همين
       RunId مي‌آيد (هنوز پاک نشده؛ DELETE در پايين همين رويه است). دور
       اول (که هنوز رکورد قبلي نيست) کامل پذيرفته مي‌شود؛ از دور دوم به
       بعد فقط ۳۵٪ از تغيير اعمال مي‌شود. */
    DECLARE @Damping FLOAT = 0.35;

    UPDATE  c
       SET  c.Mat = CASE WHEN prev.MaterialCost IS NULL THEN z.fi
                          ELSE prev.MaterialCost + @Damping * (z.fi - prev.MaterialCost) END,
            c.Src = 1
    FROM    #C c
    JOIN   (SELECT k.code, SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
            FROM   dbo.KALAS k
            WHERE  k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
            GROUP BY k.code) z ON z.code = c.Code
    LEFT    JOIN dbo.CC_ItemCost prev
            ON  prev.RunId = @RunId AND prev.Code = c.Code
    WHERE   z.fi IS NOT NULL;

    ---- بدون گردش در ماه: آخرين نرخ ميانگين ثبت‌شده
    UPDATE  c
       SET  c.Mat = lp.AVRAGE
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 i.AVRAGE
                   FROM   dbo.INVO_LST i
                   JOIN   dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                   WHERE  CAST(i.CODE AS BIGINT) = c.Code AND i.AVRAGE > 0
                   ORDER BY h.DATE_N DESC, i.NUMBER DESC) lp
    WHERE   c.HasFormula = 0 AND c.Mat = 0;

    UPDATE #C SET Src = 3 WHERE HasFormula = 0 AND Mat = 0;

    /* ─── ۵) محاسبه از عميق‌ترين سطح به سطح صفر ───
       چون فرزندها هميشه سطح عميق‌تري از والد دارند، وقتي به والد
       مي‌رسيم بهاي همه اجزايش قبلاً محاسبه شده است. */

    DECLARE @lvl SMALLINT = (SELECT MAX(Llc) FROM #C);
    DECLARE @totalChanges INT = 0;

    WHILE @lvl >= 0
    BEGIN
        IF @WhatIf = 0
        BEGIN
            BEGIN TRAN;

            ---- ۵-الف) نرخ اجزا در فرمول والدهاي اين سطح
            UPDATE  d
               SET  d.SMABL = ch.Mat + ch.Wage + ch.Oh,
                    d.MABLK = ROUND((ch.Mat + ch.Wage + ch.Oh) * d.MEGHk, 0)
            OUTPUT  @RunId, 'S11', inserted.FNUMB,
                    NULL, TRY_CAST(inserted.CODE AS BIGINT), 'SMABL',
                    deleted.SMABL, inserted.SMABL,
                    N'انتشار نرخ — سطح‌بندي BOM'
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.DTL_MANF  d
            JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
            JOIN    #C p  ON p.Code  = CAST(hm.CODE AS BIGINT) AND p.Llc = @lvl
            JOIN    #C ch ON ch.Code = CAST(d.CODE  AS BIGINT)
            WHERE   ABS(ISNULL(d.SMABL, 0) - (ch.Mat + ch.Wage + ch.Oh)) > 0.5;

            SET @totalChanges += @@ROWCOUNT;

            COMMIT;
        END

        ---- ۵-ب) بهاي هر فرمولِ اين سطح = مجموع اجزاي همان فرمول
        UPDATE  f
           SET  f.Mat = ISNULL(a.MatCost, 0)
        FROM    #F f
        JOIN    #C p ON p.Code = f.Code AND p.Llc = @lvl
        CROSS   APPLY (SELECT SUM(d.MEGHk * (ch.Mat + ch.Wage + ch.Oh)) AS MatCost
                       FROM   dbo.DTL_MANF d
                       JOIN   #C ch ON ch.Code = CAST(d.CODE AS BIGINT)
                       WHERE  d.FNUMB = f.FNUMB) a;

        ---- ۵-ج) بهاي «خودِ» کالا = ميانگين موزونِ همه‌ي فرمول‌هايش با
        ---- مقدار واقعيِ توليدشده (Qty)؛ بدون هيچ توليدي، ميانگين ساده.
        ---- وقتي Mat از گام ۴ (ميانگين واقعيِ انبار) تعيين شده، Wage/Oh
        ---- را هم از BOM نمي‌گيرد و صفر مي‌ماند — نه فقط Mat را دست
        ---- نمي‌زند: نرخ انباري از MABL_K واقعيِ ثبت‌شده مي‌آيد که همان
        ---- لحظه‌ي توليد (TAG=9) از قبل دستمزد/سربار را داخلش دارد (نگاه
        ---- کنید AverageRateRebuildService, case 9: produced = IMBIBE_MANF
        ---- + IMBIBE_SAR + SumOfMABLK). اگر اينجا دوباره w.Wage/w.Oh را
        ---- روي همان کد جمع بزنيم، دستمزد/سربار دوبار حساب مي‌شود — دقيقاً
        ---- همان چيزي که مغايرت ۷۷۱ را نصفه رفع کرده بود (Mat درست شد ولي
        ---- Wage هنوز از BOM اضافه مي‌آمد).
        UPDATE  c
           SET  c.Wage = CASE WHEN c.Mat <> 0 THEN 0 ELSE w.Wage END,
                c.Oh   = CASE WHEN c.Mat <> 0 THEN 0 ELSE w.Oh   END,
                c.Mat  = CASE WHEN c.Mat <> 0 THEN c.Mat ELSE w.Mat END
        FROM    #C c
        CROSS   APPLY (
                    SELECT
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Mat  * f.Qty) / SUM(f.Qty) ELSE AVG(f.Mat)  END AS Mat,
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Wage * f.Qty) / SUM(f.Qty) ELSE AVG(f.Wage) END AS Wage,
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Oh   * f.Qty) / SUM(f.Qty) ELSE AVG(f.Oh)   END AS Oh
                    FROM #F f WHERE f.Code = c.Code
                ) w
        WHERE   c.Llc = @lvl AND c.HasFormula = 1;

        SET @lvl -= 1;
    END

    /* ─── ۶) ثبت نتيجه در CC_ItemCost ───
       FNUMB اينجا فقط براي نمايش در گزارش است؛ وقتي کالا چند فرمول همان
       ماه دارد، فرمولي که بيشترين مقدار واقعي زيرش توليد شده به‌عنوان
       نماينده انتخاب مي‌شود (بهاي واقعي همچنان ميانگين موزونِ همه است،
       نه فقط همين يکي). */
    DELETE dbo.CC_ItemCost WHERE RunId = @RunId;

    INSERT dbo.CC_ItemCost
        (RunId, PeriodMonth, Code, LowLevelCode, SourceKind, FNUMB,
         MaterialCost, WageCost, OverheadCost)
    SELECT  @RunId, @Month, c.Code, c.Llc, c.Src, rep.FNUMB, c.Mat, c.Wage, c.Oh
    FROM    #C c
    OUTER   APPLY (SELECT TOP 1 f.FNUMB FROM #F f WHERE f.Code = c.Code
                    ORDER BY f.Qty DESC, f.FNUMB DESC) rep;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S11', 1,
            CONCAT(N'انتشار نرخ: ', @totalChanges, N' نرخ به‌روز شد'),
            (SELECT MAX(Llc) AS maxLevel, COUNT(*) AS items,
                    SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END) AS noSource
             FROM #C FOR JSON PATH));

    /* ─── ۷) آزمون سلامت: CHK-09 بايد صفر شود ───
       Khod اينجا از خودِ #C خوانده مي‌شود (يعني همان بهاي موزوني که تازه
       محاسبه و منتشر شد)، نه دوباره از HEAD_MANF/DTL_MANF به تفکيک FNUMB —
       وگرنه هر فرمولِ «غيرمنتخب» يک کالاي چندفرمولي هميشه کاذب فلگ مي‌شد. */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-09';

    ;WITH DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code, AVG(d.SMABL) AS Nerkh
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S11', 'CHK-09', 14, 2, c.Code, (c.Mat + c.Wage + c.Oh) - v.Nerkh,
            N'نرخ پس از اجراي موتور هنوز منتشر نشده — نياز به بررسي'
    FROM    #C c
    JOIN    DarValed v ON v.Code = c.Code
    WHERE   c.HasFormula = 1
      AND   ABS((c.Mat + c.Wage + c.Oh) - v.Nerkh) / NULLIF((c.Mat + c.Wage + c.Oh), 0) > 0.001;

    /* ─── خلاصه ─── */
    SELECT  Llc                                          AS سطح,
            COUNT(*)                                     AS تعداد_کالا,
            SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END)     AS بدون_منبع_نرخ
    FROM    #C
    GROUP BY Llc ORDER BY Llc;

    SELECT  @totalChanges AS تعداد_نرخ_به‌روز_شده,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND RuleCode = 'CHK-09' AND IsResolved = 0)
                          AS نرخ_منتشر_نشده_باقيمانده;
END
GO


PRINT N'موتور نرخ توليدي (S10 و S11) ايجاد شد.';

/* نمونه:
   EXEC dbo.CC_sp_S10_BalanceConversion @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_S11_PropagateRates    @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
*/
GO
";
            TryExecuteCostCloseBatch(db, rateEngine,
                "CC_sp_S07B_SyncLaborRate، CC_sp_S10_BalanceConversion و CC_sp_S11_PropagateRates",
                "اسکریپت 15-rate-engine-production.sql را اجرا کنید (به CC_ConversionCost, CC_UnitAcc, CC_ItemCost نیاز دارد).");

            // --- 16-rollback.sql ---
            string rollback = @"
/* ═══════════════════════════════════════════════════════════════════
   بازگردانی از اسنپ‌شات

   هر گام نویسنده پیش از اجرا اسنپ‌شات می‌گیرد. این رویه آن را
   برمی‌گرداند و اجرا را به وضعیت «بازگردانی‌شده» می‌برد.

   بدون این، اجرای موتور روی داده واقعی ریسک دارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_Rollback
    @RunId    INT,
    @StepCode VARCHAR(10) = NULL,   -- خالي = بازگرداني کل اجرا (قديمي‌ترين اسنپ‌شات)
    @UserName NVARCHAR(50) = N'system',
    @WhatIf   BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- دورهٔ تأييدشده قفل است
    -- CC_sp_S14_ApproveClose به کاربر مي‌گويد «دوره تأييد و قفل شد»؛ اگر
    -- بازگرداني بتواند بعد از آن اجرا شود، آن قفل واقعي نيست و يک بستنِ
    -- رسميِ تأييدشده بي‌صدا باطل مي‌شود. تأييد بايد اول برداشته شود.
    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE RunId = @RunId AND ApprovedAtUtc IS NOT NULL)
    BEGIN
        RAISERROR(N'اين اجرا تأييد و قفل شده است؛ بازگرداني ممکن نيست.', 16, 1);
        RETURN;
    END

    ---- اسنپ‌شات‌هاي قابل استفاده
    -- MIN و نه MAX: در يک اجراي کامل چند گام اسنپ‌شات مي‌گيرند (S02, S09,
    -- S10, S11) و هرکدام هر سه جدول را نگه مي‌دارند. S03 و S04 که اسناد را
    -- حذف و بازشماره مي‌کنند بين اسنپ‌شات S02 و اسنپ‌شات S09 اجرا مي‌شوند،
    -- پس اسنپ‌شات‌هاي بعدي وضعيتِ «بعد از S03/S04» را در خود دارند. اگر
    -- بازگرداني کل اجرا از آخرين اسنپ‌شات انجام شود، حذف و بازشماره‌گذاري
    -- هرگز برنمي‌گردد — درحالي‌که هم دکمهٔ رابط کاربري و هم نام اين رويه به
    -- کاربر قول «بازگشت به وضعيت پيش از اجرا» را مي‌دهند. قديمي‌ترين
    -- اسنپ‌شات همان وضعيت پيش از اجراست. براي بازگرداني يک گام مشخص هم
    -- درست است، چون هر گام براي هر جدول فقط يک اسنپ‌شات دارد.
    IF OBJECT_ID('tempdb..#Snap') IS NOT NULL DROP TABLE #Snap;

    SELECT  s.SnapshotId, s.TableName, s.BackupTable, s.RowsCopied, s.StepCode
    INTO    #Snap
    FROM    dbo.CC_Snapshot s
    JOIN   (SELECT TableName, MIN(SnapshotId) AS Id
            FROM   dbo.CC_Snapshot
            WHERE  RunId = @RunId
              AND (@StepCode IS NULL OR StepCode = @StepCode)
              AND  RestoredAtUtc IS NULL
            GROUP BY TableName) x ON x.Id = s.SnapshotId;

    IF NOT EXISTS (SELECT 1 FROM #Snap)
    BEGIN
        SELECT N'اسنپ‌شات قابل بازگرداني يافت نشد' AS پيام;
        RETURN;
    END

    ---- بررسي وجود واقعي جداول پشتيبان
    DECLARE @missing NVARCHAR(MAX) = NULL;

    SELECT  @missing = STRING_AGG(BackupTable, ', ')
    FROM    #Snap
    WHERE   OBJECT_ID('dbo.' + BackupTable, 'U') IS NULL;

    IF @missing IS NOT NULL
    BEGIN
        RAISERROR(N'جدول پشتيبان يافت نشد: %s', 16, 1, @missing);
        RETURN;
    END

    IF @WhatIf = 1
    BEGIN
        SELECT  TableName   AS جدول,
                BackupTable AS جدول_پشتيبان,
                RowsCopied  AS تعداد_سطر,
                StepCode    AS گام
        FROM    #Snap ORDER BY TableName;

        SELECT N'حالت گزارش — چيزي بازگردانده نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @tbl SYSNAME, @bak SYSNAME, @sql NVARCHAR(MAX), @n INT = 0, @inserted INT;

    DECLARE cSnap CURSOR LOCAL FAST_FORWARD FOR
        SELECT TableName, BackupTable FROM #Snap;

    OPEN cSnap;
    FETCH NEXT FROM cSnap INTO @tbl, @bak;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @tbl = 'DTL_MANF'
        BEGIN
            SET @sql = N'
                UPDATE  d
                   SET  d.SMABL = b.SMABL,
                        d.MABLK = b.MABLK,
                        d.MEGHk = b.MEGHk,
                        d.PERT  = b.PERT
                FROM    dbo.DTL_MANF d
                JOIN    dbo.' + QUOTENAME(@bak) + N' b
                        ON b.FNUMB = d.FNUMB AND b.CODE = d.CODE';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;
        END
        ELSE IF @tbl = 'HEAD_MANF'
        BEGIN
            SET @sql = N'
                UPDATE  h
                   SET  h.IMBIBE_MANF = b.IMBIBE_MANF,
                        h.IMBIBE_SAR  = b.IMBIBE_SAR
                FROM    dbo.HEAD_MANF h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.FNUMB = h.FNUMB';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;
        END
        ELSE IF @tbl = 'DEED_HED'
        BEGIN
            -- بازگرداني شماره اسناد و اسناد حذف‌شده؛ ۹ جدول فرزند خودکار دنبال
            -- مي‌آيند. سه مرحله، به همان دليلي که CC_sp_S04_SortDeeds دو-مرحله‌اي
            -- است: اگر شمارهٔ اصليِ يک سند برابر شمارهٔ فعليِ سند ديگري باشد که
            -- هنوز به حالت اصلي‌اش برنگشته، UPDATE يا INSERT مستقيم به
            -- PRIMARY KEY تکراري مي‌خورد.
            EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

            -- ۱) هر سندي که شماره‌اش فرق کرده را به يک بازهٔ منفيِ ناهم‌پوشان
            --    مي‌بريم تا شمارهٔ اصلي‌اش براي درج سندهاي حذف‌شده (مرحلهٔ ۲) و
            --    بازگرداني خودش (مرحلهٔ ۳) آزاد و بدون برخورد باشد.
            SET @sql = N'
                UPDATE  h
                   SET  h.N_S = -3000000.0 - h.N_S
                FROM    dbo.DEED_HED h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.base = h.base
                WHERE   h.N_S <> b.N_S';
            EXEC sp_executesql @sql;

            -- ۲) سندهايي که CC_sp_S03_DeleteEmptyDeeds کامل حذف کرده بود را با
            --    همان base و همان مقادير همهٔ ستون‌ها دوباره درج مي‌کنيم. امن
            --    است چون مرحلهٔ ۱ هر شمارهٔ زندهٔ همپوشان را قبلاً کنار زده.
            -- @@ROWCOUNT را بلافاصله بعد از INSERT، داخل همان دستهٔ پویا، در
            -- @inserted می‌ریزیم — چون SET IDENTITY_INSERT OFF که بعدش لازم
            -- است خودش یک دستور SET است و @@ROWCOUNT را در نشستِ فراخوان صفر
            -- می‌کند (رفتار واقعی SQL Server، با آزمایش مستقیم تأیید شد). بدون
            -- این، بازگردانیِ سندی که فقط حذف شده بود (بدون تغییر شماره) به
            -- کاربر «۰ سطر بازگردانده شد» نشان می‌داد، با اینکه سند واقعاً
            -- برگشته بود.
            SET @sql = N'
                SET IDENTITY_INSERT dbo.DEED_HED ON;
                INSERT INTO dbo.DEED_HED
                    (N_S, DATE_S, SHARH_S, NO_S, ANBAR, N_FACTOR, GHATEI, USER_NAME,
                     base, SGN1, SGN2, SGN3, SGN4, OKF, sgn1usid, sgn2usid, sgn3usid,
                     CRT, UID, BAYEG)
                SELECT b.N_S, b.DATE_S, b.SHARH_S, b.NO_S, b.ANBAR, b.N_FACTOR, b.GHATEI,
                       b.USER_NAME, b.base, b.SGN1, b.SGN2, b.SGN3, b.SGN4, b.OKF,
                       b.sgn1usid, b.sgn2usid, b.sgn3usid, b.CRT, b.UID, b.BAYEG
                FROM   dbo.' + QUOTENAME(@bak) + N' b
                WHERE  NOT EXISTS (SELECT 1 FROM dbo.DEED_HED h WHERE h.base = b.base);
                SET @ins = @@ROWCOUNT;
                SET IDENTITY_INSERT dbo.DEED_HED OFF;';
            EXEC sp_executesql @sql, N'@ins INT OUTPUT', @ins = @inserted OUTPUT;
            SET @n += @inserted;

            -- ۳) سندهاي مرحلهٔ ۱ را از بازهٔ منفي به شمارهٔ اصلي‌شان برمي‌گردانيم.
            SET @sql = N'
                UPDATE  h
                   SET  h.N_S = b.N_S
                FROM    dbo.DEED_HED h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.base = h.base
                WHERE   h.N_S < 0';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;

            EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;
        END

        FETCH NEXT FROM cSnap INTO @tbl, @bak;
    END

    CLOSE cSnap;
    DEALLOCATE cSnap;

    ---- علامت‌گذاري اسنپ‌شات‌ها
    -- در بازگرداني کل اجرا، اسنپ‌شات‌هاي مياني (S09/S10/S11) هم مصرف‌شده
    -- حساب مي‌شوند؛ وگرنه بازگرداني دوباره، قديمي‌ترينِ باقيمانده يعني وضعيت
    -- «بعد از S03/S04» را روي داده‌اي که همين الان درست برگشته مي‌نويسد.
    UPDATE  s
       SET  s.RestoredAtUtc = SYSUTCDATETIME()
    FROM    dbo.CC_Snapshot s
    WHERE   s.RunId = @RunId
      AND   s.RestoredAtUtc IS NULL
      AND  (@StepCode IS NULL OR s.StepCode = @StepCode);

    ---- تغييرات ثبت‌شده باطل مي‌شوند
    DELETE dbo.CC_FormulaChange
    WHERE  RunId = @RunId
      AND (@StepCode IS NULL OR StepCode = @StepCode);

    ---- وضعيت اجرا
    UPDATE dbo.CC_Run
       SET Status = 5,                       -- بازگردانی‌شده
           FormulasDirty = 0,
           FinishedAtUtc = SYSUTCDATETIME()
     WHERE RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 2,
            CONCAT(N'بازگرداني توسط ', @UserName, N': ', @n, N' سطر'));

    COMMIT;

    SELECT @n AS تعداد_سطر_بازگردانده_شده;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   پاکسازی اسنپ‌شات‌های قدیمی
   جداول CC_BAK_* بعد از ۹۰ روز حذف می‌شوند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_PurgeSnapshots
    @OlderThanDays INT = 90,
    @WhatIf        BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#Old') IS NOT NULL DROP TABLE #Old;

    SELECT  SnapshotId, BackupTable, TakenAtUtc
    INTO    #Old
    FROM    dbo.CC_Snapshot
    WHERE   TakenAtUtc < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());

    IF @WhatIf = 1
    BEGIN
        SELECT BackupTable AS جدول, TakenAtUtc AS تاريخ FROM #Old;
        SELECT COUNT(*) AS تعداد_قابل_حذف FROM #Old;
        RETURN;
    END

    DECLARE @bak SYSNAME;
    DECLARE cOld CURSOR LOCAL FAST_FORWARD FOR SELECT BackupTable FROM #Old;

    OPEN cOld;
    FETCH NEXT FROM cOld INTO @bak;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
            EXEC('DROP TABLE dbo.' + @bak);

        FETCH NEXT FROM cOld INTO @bak;
    END

    CLOSE cOld;
    DEALLOCATE cOld;

    DELETE s FROM dbo.CC_Snapshot s JOIN #Old o ON o.SnapshotId = s.SnapshotId;

    SELECT COUNT(*) AS تعداد_حذف_شده FROM #Old;
END
GO

PRINT N'رويه‌هاي بازگرداني و پاکسازي ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, rollback,
                "CC_sp_Rollback و CC_sp_PurgeSnapshots",
                "اسکریپت 16-rollback.sql را اجرا کنید (به CC_Snapshot نیاز دارد).");

            // --- 17-variance-steps.sql ---
            string varianceSteps = @"
/* ═══════════════════════════════════════════════════════════════════
   S07 تا S09 — بازتولید، انحراف، و تخصیص

   S07  بازتولید خروج مواد + انبارگردانی  (بازنویسی مجموعه‌ای)
   S08  محاسبه انحراف مصرف
   S09  تخصیص انحراف با تصمیم کاربر
   S09a تولید پیشنهاد پیش‌فرض از ماه قبل

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S07 — بازتولید خروج مواد و انبارگردانی

   جایگزین اسکریپت فعلی با دو کرسر تودرتو و sp_executesql.
   خروج مواد یک INSERT مجموعه‌ای است؛ انبارگردانی کرسر روز دارد
   چون dbo.MOGUDI تابع جدولی پارامتری است.

   انبارها از CC_UnitAnbar خوانده می‌شوند، نه از کد. با این کار
   باگ تکرار انبار ۸ در اسکریپت فعلی موضوعیت خود را از دست می‌دهد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S07_RebuildIssue
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── بخش يک: خروج مواد ─── */
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  h.NUMBER  AS ProdNo,
            h.NUMBER1 AS IssueNo
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    WHERE   h.TAG = 9
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   EXISTS (SELECT 1 FROM dbo.HEAD_LST x
                    WHERE x.NUMBER = h.NUMBER1 AND x.TAG = 10);

    CREATE CLUSTERED INDEX IX_Prod ON #Prod(IssueNo);

    IF @WhatIf = 1
    BEGIN
        SELECT  COUNT(*) AS تعداد_برگه_توليد,
                (SELECT COUNT(*) FROM dbo.INVO_LST i
                 JOIN #Prod p ON p.IssueNo = i.NUMBER AND i.TAG = 10) AS سطر_فعلي_خروج
        FROM    #Prod;
        RETURN;
    END

    BEGIN TRAN;

    DELETE  i
    FROM    dbo.INVO_LST i
    JOIN    #Prod p ON p.IssueNo = i.NUMBER AND i.TAG = 10;

    DECLARE @deleted INT = @@ROWCOUNT;

    INSERT dbo.INVO_LST
        (NUMBER, TAG, ANBAR, CODE, VAHED_K, MEGH, MEGHK,
         N_RASID, MABL, AVRAGE, MABL_K)
    SELECT  p.IssueNo, 10, dm.ANBAR, dm.CODE, dm.VAHED_K,
            (dm.MEGH  + dm.PERT) * pl.MEGHK,
            (dm.MEGHK + dm.PERT) * pl.MEGHK,
            dm.FNUMB, 1, 1,
            (dm.MEGHK + dm.PERT) * pl.MEGHK
    FROM    #Prod p
    JOIN    dbo.INVO_LST  pl ON pl.NUMBER = p.ProdNo AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                            AND hm.GHEYMAT = @Month
    JOIN    dbo.DTL_MANF  dm ON dm.FNUMB  = hm.FNUMB;

    DECLARE @inserted INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S07', 1,
            CONCAT(N'بازتوليد خروج مواد: ', @deleted, N' حذف، ', @inserted, N' درج'),
            (SELECT @deleted AS deleted, @inserted AS inserted FOR JSON PATH));

    COMMIT;

    /* ─── بخش دو: انبارگرداني ─── */
    DECLARE @anb INT, @grdNum INT, @grdDate INT, @countRows INT = 0;

    -- NUM3 مقدارِ شمارشِ فیزیکیِ واقعی است (برای انبارهایی که واقعاً
    -- شمارش دستی دارند، نه اسنپ‌شاتِ خودکارِ روزانه) — این رویه نمی‌تواند
    -- آن را بازتولید کند. DELETE پایین آن را همراه کل ردیف پاک می‌کرد و
    -- INSERT بعدی هرگز NUM3 را دوباره نمی‌گذاشت، پس هر بار اجرای این گام
    -- بی‌صدا پاکش می‌کرد — دقیقاً همان چیزی که برای سند ۷۲ (انبار ۳)
    -- رخ داد و کاربر تأیید کرد باگ بوده. قبل از DELETE نگهش می‌داریم و
    -- بعد از INSERT دوباره رویش می‌گذاریم.
    IF OBJECT_ID('tempdb..#Num3') IS NOT NULL DROP TABLE #Num3;

    SELECT  l.GRD_NUM, l.CODE, l.NUM3
    INTO    #Num3
    FROM    dbo.ANBGRD_LST  l
    JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
    WHERE   h.GRD_DATE BETWEEN @DT1 AND @DT2
      AND   l.NUM3 IS NOT NULL AND l.NUM3 <> 0
      AND   h.GRD_ANBAR IN (SELECT ua.Anbar FROM dbo.CC_UnitAnbar ua
                             JOIN dbo.CC_Unit u ON u.UnitId = ua.UnitId AND u.IsActive = 1
                             WHERE ua.DoStockCount = 1);

    CREATE CLUSTERED INDEX IX_Num3 ON #Num3(GRD_NUM, CODE);

    DECLARE cAnb CURSOR LOCAL FAST_FORWARD FOR
        SELECT   ua.Anbar
        FROM     dbo.CC_UnitAnbar ua
        JOIN     dbo.CC_Unit u ON u.UnitId = ua.UnitId AND u.IsActive = 1
        WHERE    ua.DoStockCount = 1
        GROUP BY ua.Anbar, ua.SeqNo      -- يک انبار در دو واحد = يک بار پردازش
        ORDER BY ua.SeqNo;

    OPEN cAnb;
    FETCH NEXT FROM cAnb INTO @anb;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        DELETE  l
        FROM    dbo.ANBGRD_LST l
        JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
        WHERE   h.GRD_ANBAR = @anb AND h.GRD_DATE BETWEEN @DT1 AND @DT2;

        DECLARE cDay CURSOR LOCAL FAST_FORWARD FOR
            SELECT GRD_NUM, GRD_DATE
            FROM   dbo.ANBGRD_HEAD
            WHERE  GRD_ANBAR = @anb AND GRD_DATE BETWEEN @DT1 AND @DT2
            ORDER BY GRD_DATE;

        OPEN cDay;
        FETCH NEXT FROM cDay INTO @grdNum, @grdDate;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            INSERT dbo.ANBGRD_LST (CODE, MOG, GRD_NUM)
            SELECT CODE, MAND, @grdNum FROM dbo.MOGUDI(@grdDate, @anb);

            SET @countRows += @@ROWCOUNT;
            FETCH NEXT FROM cDay INTO @grdNum, @grdDate;
        END

        CLOSE cDay;
        DEALLOCATE cDay;

        FETCH NEXT FROM cAnb INTO @anb;
    END

    CLOSE cAnb;
    DEALLOCATE cAnb;

    UPDATE  l
       SET  l.NUM3 = n.NUM3
    FROM    dbo.ANBGRD_LST l
    JOIN    #Num3 n ON n.GRD_NUM = l.GRD_NUM AND n.CODE = l.CODE;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S07', 1, CONCAT(N'انبارگرداني: ', @countRows, N' سطر'));

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (CostCloseController/S07_RebuildIssue)؛
    -- برای بازبینی دستی در SSMS از دو خلاصه بالا (INSERT به CC_RunLog) استفاده کنید.
    SELECT @deleted AS Deleted, @inserted AS Inserted, @countRows AS StockCount;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S08 — محاسبه انحراف مصرف

   مانده انبار مواد مصرفی تولید = انحراف مصرف
   (به شرط صفر بودن کالای در جریان ساخت)

   همان zanbekht{MM}، ولی با مبلغ ریالی و درصد نسبت به مصرف،
   تا بتوان بر اساس اهمیت مرتب کرد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S08_CalcVariance
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_Variance WHERE RunId = @RunId;

    ---- انبارهاي «مبناي انحراف» هر واحد
    INSERT dbo.CC_Variance
        (RunId, Anbar, Code, QtyVariance, UnitRate, AmountVariance, ConsumedQty)
    SELECT  @RunId,
            h.GRD_ANBAR,
            l.CODE,
            SUM(l.MOG - ISNULL(l.NUM3, 0))                     AS QtyVar,
            MAX(ic.TotalCost)                                  AS Rate,
            SUM(l.MOG - ISNULL(l.NUM3, 0)) * MAX(ic.TotalCost) AS AmtVar,
            MAX(u.Consumed)                                    AS Consumed
    FROM    dbo.ANBGRD_LST  l
    JOIN    dbo.ANBGRD_HEAD h ON h.GRD_NUM = l.GRD_NUM
    JOIN    dbo.CC_UnitAnbar ua ON ua.Anbar = h.GRD_ANBAR AND ua.AnbarRole = 1
    LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = l.CODE AND ic.RunId = @RunId
    OUTER   APPLY (
                SELECT SUM(pl.MEGHK * d.MEGHk) AS Consumed
                FROM   dbo.HEAD_LST  hl
                JOIN   dbo.INVO_LST  pl ON pl.NUMBER = hl.NUMBER AND pl.TAG = 9
                JOIN   dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                       AND hm.GHEYMAT = @Month
                JOIN   dbo.DTL_MANF  d  ON d.FNUMB   = hm.FNUMB
                                       AND CAST(d.CODE AS BIGINT) = l.CODE
                WHERE  hl.TAG = 9 AND hl.DATE_N BETWEEN @DT1 AND @DT2
            ) u
    WHERE   h.GRD_DATE BETWEEN @DT1 AND @DT2
    GROUP BY h.GRD_ANBAR, l.CODE
    HAVING  ABS(SUM(l.MOG - ISNULL(l.NUM3, 0))) > 0.0001;

    ---- کالاي کليدي: بالاي يک درصد کل انحراف
    DECLARE @total FLOAT =
        (SELECT SUM(ABS(ISNULL(AmountVariance, 0)))
         FROM dbo.CC_Variance WHERE RunId = @RunId);

    IF @total > 0
        UPDATE dbo.CC_Variance
           SET IsKeyItem = 1
         WHERE RunId = @RunId
           AND ABS(ISNULL(AmountVariance, 0)) > @total * 0.01;

    ---- CHK-11: انحراف روي ماده‌اي که در هيچ فرمولي مصرف نشده
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-11';

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
    SELECT  @RunId, 'S08', 'CHK-11', 11, 1, v.Anbar, v.Code, v.AmountVariance,
            N'انحراف روي ماده‌اي که در هيچ فرمول اين ماه مصرف نشده'
    FROM    dbo.CC_Variance v
    WHERE   v.RunId = @RunId
      AND   ISNULL(v.ConsumedQty, 0) = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S08', 1,
            CONCAT(N'انحراف مصرف: ', COUNT(*), N' کالا، جمع ',
                   FORMAT(SUM(ISNULL(AmountVariance,0)), 'N0'), N' ريال'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN IsKeyItem = 1 THEN 1 ELSE 0 END) AS keyItems,
                    SUM(ISNULL(AmountVariance,0)) AS netAmount
             FROM dbo.CC_Variance WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_Variance WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S08_CalcVariance.VarianceSummary)
    SELECT  COUNT(*)                                        AS Items,
            SUM(CASE WHEN IsKeyItem = 1 THEN 1 ELSE 0 END)  AS KeyItems,
            SUM(ISNULL(AmountVariance, 0))                  AS NetAmount,
            SUM(ABS(ISNULL(AmountVariance, 0)))             AS GrossAmount
    FROM    dbo.CC_Variance WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S09a — تولید پیشنهاد پیش‌فرض

   زنجیره سه‌مرحله‌ای:
     ۱) فرمول مقصد ماه قبل امسال هم هست  → همان تصمیم (Manual)
     ۲) نیست ولی ماده مصرف شده           → تسهیم (Prorata)
     ۳) ماده اصلاً مصرف نشده             → بدون تخصیص (Ignore)

   کلید حمل تصمیم بین ماه‌ها TargetCode است نه TargetFNUMB، چون
   GHEYMAT شماره ماه است و فرمول هر ماه FNUMB جداگانه دارد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S09a_SeedDecisions
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_VarianceDecision WHERE RunId = @RunId;

    -- CC_Variance يک رديف به ازای هر (RunId,Anbar,Code) دارد؛ اگر مستقيم
    -- ازش INSERT کنيم، کالاهای چندانباره چند بار seed می‌شوند و همان
    -- مشکلِ تکرارِ CC_VarianceDecision که در Client/GetVariances/S09 رفع
    -- شد اينجا هم دوباره رخ می‌دهد. اول به ازای هر کد جمع می‌زنيم.
    ;WITH VarByCode AS (
        SELECT  Code, SUM(ConsumedQty) AS ConsumedQty
        FROM    dbo.CC_Variance
        WHERE   RunId = @RunId
        GROUP BY Code
    ),
    Prev AS (
        SELECT  d.Code, d.Mode, d.TargetCode,
                ROW_NUMBER() OVER (PARTITION BY d.Code
                                   ORDER BY d.DecisionId DESC) AS rn
        FROM    dbo.CC_VarianceDecision d
        JOIN    dbo.CC_Run r ON r.RunId = d.RunId
        WHERE   r.Status = 3            -- فقط از اجراهاي تکميل‌شده
          AND   d.RunId <> @RunId
    )
    INSERT dbo.CC_VarianceDecision
        (RunId, Code, Mode, TargetCode, TargetFNUMB, DecidedBy, Note)
    SELECT  @RunId,
            v.Code,
            CASE
              WHEN p.Mode = 1 AND hm.FNUMB IS NOT NULL THEN 1   -- ادامه تصميم قبلي
              WHEN ISNULL(v.ConsumedQty, 0) > 0         THEN 2   -- تسهيم
              ELSE 3                                             -- بدون تخصيص
            END,
            CASE WHEN hm.FNUMB IS NOT NULL THEN p.TargetCode END,
            hm.FNUMB,
            N'system',
            CASE
              WHEN p.Mode = 1 AND hm.FNUMB IS NOT NULL
                   THEN N'مثل ماه قبل'
              WHEN p.Mode = 1 AND hm.FNUMB IS NULL
                   THEN N'فرمول مقصد ماه قبل امسال نيست — تسهيم'
              WHEN ISNULL(v.ConsumedQty, 0) = 0
                   THEN N'ماده در هيچ فرمولي مصرف نشده — بررسي شود'
              ELSE N'تصميم جديد'
            END
    FROM    VarByCode v
    LEFT    JOIN Prev p ON p.Code = v.Code AND p.rn = 1
    OUTER   APPLY (SELECT TOP 1 h.FNUMB
                   FROM   dbo.HEAD_MANF h
                   WHERE  CAST(h.CODE AS BIGINT) = p.TargetCode
                     AND  h.GHEYMAT = @Month
                   ORDER BY h.FNUMB DESC) hm;

    ---- CHK-12: تصميم ماه قبل قابل ادامه نيست
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-12';

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
    SELECT  @RunId, 'S09', 'CHK-12', 15, 1, d.Code,
            N'فرمول مقصد ماه قبل در اين ماه وجود ندارد؛ پيش‌فرض روي تسهيم رفت'
    FROM    dbo.CC_VarianceDecision d
    WHERE   d.RunId = @RunId
      AND   d.Note LIKE N'%امسال نيست%';

    SELECT  CASE Mode WHEN 1 THEN N'اختصاص'
                      WHEN 2 THEN N'تسهيم'
                      ELSE N'بدون تخصيص' END AS حالت,
            COUNT(*) AS تعداد
    FROM    dbo.CC_VarianceDecision
    WHERE   RunId = @RunId
    GROUP BY Mode ORDER BY Mode;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S09 — اعمال تصمیم‌ها

   Manual   کل انحراف کالا به یک فرمول مشخص
   Prorata  تسهیم بین فرمول‌هایی که آن ماده را مصرف کرده‌اند
   Ignore   دست‌نخورده در حساب ۷۷۲ می‌ماند

   تغییر روی MEGHk انجام می‌شود، به ازای یک واحد محصول:
   مقدار افزوده = سهم انحراف ÷ مقدار تولید همان محصول
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S09_ApplyDecisions
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- مقدار توليد هر فرمول در ماه
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB,
            SUM(pl.MEGHK)             AS ProdQty
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
    GROUP BY TRY_CAST(pl.N_KOL AS INT)
    HAVING  SUM(pl.MEGHK) > 0;

    CREATE UNIQUE CLUSTERED INDEX IX_Prod ON #Prod(FNUMB);

    ---- سهم هر فرمول از انحراف هر ماده
    IF OBJECT_ID('tempdb..#Share') IS NOT NULL DROP TABLE #Share;

    -- CC_Variance يک رديف به ازای هر (RunId,Anbar,Code) دارد؛ تصميم‌ها
    -- در سطح کالا هستند، نه انبار. جوين مستقيم به CC_Variance برای
    -- کالاهای چندانباره چند رديف #Share توليد می‌کرد و UPDATE پايين
    -- فقط يکی را (به‌صورت غيرقطعی) اعمال می‌کرد — انحراف انبارهای
    -- ديگر آن کالا اصلاً به فرمول نمی‌رسيد و «باقيمانده» هرگز صفر
    -- نمی‌شد. اول به ازای هر کد جمع می‌زنيم.
    ;WITH VarByCode AS (
        SELECT  Code, SUM(QtyVariance) AS QtyVariance
        FROM    dbo.CC_Variance
        WHERE   RunId = @RunId
        GROUP BY Code
    ),
    Usage AS (
        SELECT  d.FNUMB,
                CAST(d.CODE AS BIGINT) AS Code,
                p.ProdQty * d.MEGHk    AS UsedQty
        FROM    dbo.DTL_MANF  d
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        JOIN    #Prod p ON p.FNUMB = d.FNUMB
        WHERE   d.MEGHk > 0
    )
    SELECT  u.FNUMB,
            u.Code,
            v.QtyVariance,
            dc.Mode,
            CASE
              -- اختصاص: کل انحراف به همان يک فرمول
              WHEN dc.Mode = 1 AND u.FNUMB = dc.TargetFNUMB THEN 1.0
              -- تسهيم: به نسبت مصرف
              WHEN dc.Mode = 2
                   THEN u.UsedQty / NULLIF(SUM(u.UsedQty) OVER (PARTITION BY u.Code), 0)
              ELSE 0
            END AS Ratio
    INTO    #Share
    FROM    Usage u
    JOIN    VarByCode                v  ON v.Code  = u.Code
    JOIN    dbo.CC_VarianceDecision  dc ON dc.Code = u.Code AND dc.RunId = @RunId
    WHERE   dc.Mode IN (1, 2);

    DELETE #Share WHERE Ratio IS NULL OR Ratio = 0;

    IF @WhatIf = 1
    BEGIN
        SELECT  s.FNUMB                              AS شماره_فرمول,
                s.Code                               AS کد_ماده,
                st.NAME                              AS نام_ماده,
                CASE s.Mode WHEN 1 THEN N'اختصاص'
                            ELSE N'تسهيم' END        AS حالت,
                s.QtyVariance                        AS کل_انحراف,
                s.Ratio                              AS سهم,
                s.QtyVariance * s.Ratio              AS مقدار_سهم,
                p.ProdQty                            AS مقدار_توليد,
                s.QtyVariance * s.Ratio / p.ProdQty  AS افزايش_در_فرمول
        FROM    #Share s
        JOIN    #Prod  p  ON p.FNUMB = s.FNUMB
        LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = s.Code
        ORDER BY ABS(s.QtyVariance * s.Ratio) DESC;
        RETURN;
    END

    BEGIN TRAN;

    -- ⚠ هر سه ستون با هم، طبق قراردادی که فرم فرمولِ نرم‌افزار قدیمی دارد:
    --     MEGHk = MEGH * VAHEDS.NESBAT
    --     MABLK = (PERT + MEGHk) * SMABL
    --
    -- نسخه‌ی قبلی فقط MEGHk را می‌نوشت (چون S11 برای بها همان را می‌خواند) و
    -- «مقدار» را دست‌نخورده می‌گذاشت. ولی MEGH مرده نیست: خودِ S07 در همین
    -- فایل، مقدارِ فیزیکیِ حواله‌ی خروج را از روی (dm.MEGH + dm.PERT)
    -- می‌سازد. نتیجه این بود که هر بار اجرای S09 دو ستون را از هم دورتر
    -- می‌کرد و انحراف بین اجراها انباشته می‌شد — پس از یک پاکسازیِ کامل،
    -- تنها چند بار اجرای دوباره‌ی گام‌ها ۳۱ ردیف تازه ناهماهنگ ساخت.
    -- MABLK هم PERT را جا انداخته بود.
    UPDATE  d
       SET  d.MEGH  = d.MEGH  + (s.QtyVariance * s.Ratio / p.ProdQty) / vv.NESBAT,
            d.MEGHk = d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty),
            d.MABLK = ROUND(ISNULL(d.SMABL, 0) *
                            (ISNULL(d.PERT, 0) + d.MEGHk
                             + (s.QtyVariance * s.Ratio / p.ProdQty)), 0)
    OUTPUT  @RunId, 'S09', inserted.FNUMB,
            NULL, TRY_CAST(inserted.CODE AS BIGINT), 'MEGHk',
            deleted.MEGHk, inserted.MEGHk,
            N'تخصيص انحراف مصرف'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.DTL_MANF d
    JOIN    #Share s ON s.FNUMB = d.FNUMB
                    AND s.Code  = CAST(d.CODE AS BIGINT)
    JOIN    #Prod  p ON p.FNUMB = d.FNUMB
    -- JOIN و نه LEFT JOIN: بدون نسبتِ واحد نمی‌شود «مقدار» را حساب کرد و
    -- نوشتنِ عدد حدسی بدتر از رد کردنِ آن ردیف است. همان کالاها در CHK
    -- به‌عنوان «واحد ناقص» دیده می‌شوند.
    JOIN    dbo.VAHEDS vv ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
                         AND vv.VAHED = d.VAHED_K
                         AND vv.NESBAT <> 0;

    DECLARE @n INT = @@ROWCOUNT;

    ---- ثبت مقدار اعمال‌شده در تصميم‌ها
    UPDATE  dc
       SET  dc.AppliedQty = x.Applied
    FROM    dbo.CC_VarianceDecision dc
    JOIN   (SELECT Code, SUM(QtyVariance * Ratio) AS Applied
            FROM   #Share GROUP BY Code) x ON x.Code = dc.Code
    WHERE   dc.RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S09', 1,
            CONCAT(N'تخصيص انحراف: ', @n, N' سطر فرمول به‌روز شد'));

    COMMIT;

    -- ستون انگلیسی برای مصرف برنامه‌ای (S09_ApplyDecisions.ApplyResult)
    SELECT @n AS Value;
END
GO


PRINT N'رويه‌هاي S07 تا S09 ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, varianceSteps,
                "CC_sp_S07_RebuildIssue، CC_sp_S08_CalcVariance، CC_sp_S09_ApplyDecisions، CC_sp_S09a_SeedDecisions",
                "اسکریپت 17-variance-steps.sql را اجرا کنید (به CC_Variance, CC_VarianceDecision, CC_UnitAnbar نیاز دارد).");

            // --- 18-margin-report-approve.sql ---
            string marginReportApprove = @"
/* ═══════════════════════════════════════════════════════════════════
   S12 تا S14 — سود کالا، گزارش هیئت‌مدیره، تأیید نهایی

   S12  سود و زیان به تفکیک کالا + اعمال هدف حاشیه
   S13  داده گزارش اکسل
   S14  تأیید و قفل دوره

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S12 که در CC_ItemMargin (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* جدول نتیجه سود کالا */
IF OBJECT_ID('dbo.CC_ItemMargin','U') IS NULL
CREATE TABLE dbo.CC_ItemMargin (
    Id            BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId         INT      NOT NULL,
    Code          BIGINT   NOT NULL,
    QtySold       FLOAT    NOT NULL DEFAULT 0,
    WeightKg      FLOAT    NULL,
    SalesAmount   FLOAT    NOT NULL DEFAULT 0,   -- مبلغ خالص فروش
    CostAmount    FLOAT    NOT NULL DEFAULT 0,   -- بهاي تمام‌شده کالاي فروش‌رفته
    Profit        AS (SalesAmount - CostAmount) PERSISTED,
    UnitCost      FLOAT    NULL,
    UnitPrice     FLOAT    NULL,
    CalculatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CC_ItemMargin UNIQUE (RunId, Code)
);
GO


/* ═══════════════════════════════════════════════════════════════════
   S12 — محاسبه سود و زیان کالا

   فروش    از فاکتورهای TAG=2
   بها     از حساب قیمت تمام‌شده (GHEYMAT) به تفکیک کالا
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S12_CalcMargin
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMargin WHERE RunId = @RunId;

    ;WITH Forush AS (
        SELECT  CAST(i.CODE AS BIGINT)                       AS Code,
                SUM(i.MEGHk)                                 AS Qty,
                SUM(i.MEGH)                                  AS Weight,
                SUM(i.MABL_K - ISNULL(i.N_MOIN, 0))          AS NetSales
        FROM    dbo.INVO_LST i
        JOIN    dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
        WHERE   i.TAG = 2 AND h.DATE_N BETWEEN @DT1 AND @DT2
        GROUP BY CAST(i.CODE AS BIGINT)
    ),
    Baha AS (
        -- بهاي تمام‌شده کالاي فروش‌رفته از سند حسابداري
        SELECT  TRY_CAST(d.HES_M AS BIGINT) AS Code,
                SUM(d.BED) - SUM(d.BES)     AS Cost
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.TAG = 13
          AND   h.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(d.HES_M AS BIGINT)
    )
    INSERT dbo.CC_ItemMargin
        (RunId, Code, QtySold, WeightKg, SalesAmount, CostAmount, UnitCost, UnitPrice)
    SELECT  @RunId,
            f.Code,
            f.Qty,
            f.Weight,
            f.NetSales,
            ISNULL(b.Cost, ISNULL(ic.TotalCost, 0) * f.Qty),
            CASE WHEN f.Qty <> 0
                 THEN ISNULL(b.Cost, ISNULL(ic.TotalCost,0) * f.Qty) / f.Qty END,
            CASE WHEN f.Qty <> 0 THEN f.NetSales / f.Qty END
    FROM    Forush f
    LEFT    JOIN Baha b ON b.Code = f.Code
    LEFT    JOIN dbo.CC_ItemCost ic ON ic.Code = f.Code AND ic.RunId = @RunId
    WHERE   f.Qty <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا: ', COUNT(*), N' کالا، ',
                   SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END), N' زيان‌ده'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(SalesAmount) AS totalSales,
                    SUM(CostAmount)  AS totalCost,
                    SUM(SalesAmount) - SUM(CostAmount) AS totalProfit
             FROM dbo.CC_ItemMargin WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S12_CalcMargin.MarginSummary)
    SELECT  COUNT(*)                                              AS Items,
            SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END)           AS LossItems,
            SUM(SalesAmount)                                      AS TotalSales,
            SUM(CostAmount)                                       AS TotalCost,
            SUM(SalesAmount) - SUM(CostAmount)                    AS TotalProfit
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S12b — اعمال هدف حاشیه سود

   وقتی زیان یک کالا صفر می‌شود، مبلغ آن از بهای تمام‌شده‌اش کم می‌شود.
   این مبلغ یا (الف) به یک کالاي متعادل‌کننده‌ي دستيِ واحد اضافه مي‌شود
   (TargetKind=1/2 با BalancingCode مشخص)، يا (ب) با «پخش خودکار»
   (TargetKind=4) متناسب با سود، بين همه‌ي کالاهاي سودده و بدون هدفِ
   موجود در آن اجرا پخش مي‌شود — چون يک کالاي زيان‌ده معمولاً از ظرفيتِ
   يک کالاي سودده‌ي تنها بيشتر است.

   تغییر روی IMBIBE_MANF فرمول انجام می‌گیرد، چون تنها جزئی است
   که مستقل از مواد قابل تنظیم است.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S12b_ApplyMarginTargets
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID('tempdb..#Adj') IS NOT NULL DROP TABLE #Adj;

    ---- مبلغ تعديل لازم براي هر کالاي هدف‌دار (دستي يا خودکار)
    SELECT  m.Code,
            t.TargetKind,
            t.TargetPct,
            t.BalancingCode,
            m.SalesAmount,
            m.CostAmount,
            m.QtySold,
            CASE t.TargetKind
                 WHEN 1 THEN m.CostAmount - m.SalesAmount                    -- سود صفر
                 WHEN 2 THEN m.CostAmount - m.SalesAmount * (1 - t.TargetPct/100.0)
                 WHEN 4 THEN m.CostAmount - m.SalesAmount                    -- سود صفر + پخش خودکار
                 ELSE 0 END AS AdjustAmount
    INTO    #Adj
    FROM    dbo.CC_ItemMargin m
    JOIN    dbo.CC_MarginTarget t ON t.Code = m.Code AND t.IsActive = 1
    WHERE   m.RunId = @RunId
      AND   t.TargetKind IN (1, 2, 4)
      AND   m.QtySold <> 0;

    DELETE #Adj WHERE ABS(AdjustAmount) < 1;

    ---- استخر پخش خودکار: کالاهاي سودده‌اي که خودشان هدف يا
    -- متعادل‌کننده‌ي دستيِ کسي نيستند (تا تعارض با تخصيص دستي پيش نيايد)
    IF OBJECT_ID('tempdb..#Pool') IS NOT NULL DROP TABLE #Pool;

    SELECT  m.Code, m.Profit, m.QtySold
    INTO    #Pool
    FROM    dbo.CC_ItemMargin m
    WHERE   m.RunId = @RunId
      AND   m.Profit > 0
      AND   m.QtySold <> 0
      AND   m.Code NOT IN (SELECT Code FROM #Adj)
      AND   m.Code NOT IN (SELECT BalancingCode FROM #Adj WHERE BalancingCode IS NOT NULL);

    DECLARE @TotalAutoAdjust FLOAT = (SELECT ISNULL(SUM(AdjustAmount), 0) FROM #Adj WHERE TargetKind = 4);
    DECLARE @TotalPoolProfit FLOAT = (SELECT ISNULL(SUM(Profit), 0) FROM #Pool);

    IF @TotalAutoAdjust > @TotalPoolProfit
    BEGIN
        SELECT  @TotalAutoAdjust AS مجموع_زيان_پخش_خودکار,
                @TotalPoolProfit AS مجموع_سود_استخر;

        RAISERROR(N'مجموع زيان کالاهاي «پخش خودکار» از مجموع سود کالاهاي سودده‌ي موجود (استخر) بيشتر است؛ بدون منفي‌شدن نرخ جذب امکان پخش کامل نيست — يک يا چند کالا را از حالت «پخش خودکار» خارج کنيد يا اهداف دستي را کاهش دهيد.', 16, 1);
        RETURN;
    END

    IF OBJECT_ID('tempdb..#AutoShare') IS NOT NULL DROP TABLE #AutoShare;

    SELECT  p.Code,
            p.QtySold AS Qty,
            (p.Profit / NULLIF(@TotalPoolProfit, 0)) * @TotalAutoAdjust AS Amount
    INTO    #AutoShare
    FROM    #Pool p
    WHERE   @TotalAutoAdjust <> 0;

    ---- تجميع مبلغ افزايشيِ هر متعادل‌کننده — دستي و سهم پخش خودکار با هم
    IF OBJECT_ID('tempdb..#BalancerAgg') IS NOT NULL DROP TABLE #BalancerAgg;

    SELECT  Code, SUM(Amount) AS Amount, MAX(Qty) AS Qty
    INTO    #BalancerAgg
    FROM (
        SELECT  a.BalancingCode AS Code, a.AdjustAmount AS Amount, bm.QtySold AS Qty
        FROM    #Adj a
        JOIN    dbo.CC_ItemMargin bm ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
        WHERE   a.BalancingCode IS NOT NULL AND bm.QtySold <> 0
        UNION ALL
        SELECT  Code, Amount, Qty FROM #AutoShare
    ) u
    GROUP BY Code;

    ---- هشدار: کالاي متعادل‌کننده زيان‌ده مي‌شود
    IF OBJECT_ID('tempdb..#Warn') IS NOT NULL DROP TABLE #Warn;

    SELECT  ba.Code                    AS BalancingCode,
            ba.Amount                  AS AdjustAmount,
            bm.Profit                  AS BalancerProfitBefore,
            bm.Profit - ba.Amount      AS BalancerProfitAfter
    INTO    #Warn
    FROM    #BalancerAgg ba
    JOIN    dbo.CC_ItemMargin bm ON bm.Code = ba.Code AND bm.RunId = @RunId
    WHERE   bm.Profit - ba.Amount < 0
      AND   bm.Profit >= 0;

    ---- نگهبان: نرخ جذب منفي
    -- هشدار #Warn بالا فقط سودِ کالاي متعادل‌کننده را مي‌سنجد، نه نرخي که
    -- واقعاً نوشته مي‌شود. اگر مبلغ تعديل از جذب فعلي بزرگ‌تر باشد،
    -- IMBIBE_MANF منفي مي‌شود — نرخ جذب دستمزدِ منفي در بهاي تمام‌شده
    -- بي‌معناست و S11 همان را به کل درخت محصول منتشر مي‌کند. اين حالت
    -- روي داده واقعي ديده شد: کالايي که نرخ کاردکسش صفر بود (CHK-14) با
    -- هدف «سود صفر»، جذب متعادل‌کننده را به عدد منفي برد.
    IF OBJECT_ID('tempdb..#Neg') IS NOT NULL DROP TABLE #Neg;

    SELECT q.Code, q.Naghsh, q.NerkhBefore, q.NerkhAfter
    INTO   #Neg
    FROM (
        SELECT  CAST(hm.CODE AS BIGINT) AS Code,
                N'کالاي هدف' AS Naghsh,
                hm.IMBIBE_MANF AS NerkhBefore,
                hm.IMBIBE_MANF - (a.AdjustAmount / NULLIF(a.QtySold, 0)) AS NerkhAfter
        FROM    dbo.HEAD_MANF hm
        JOIN    #Adj a ON CAST(hm.CODE AS BIGINT) = a.Code
        WHERE   hm.GHEYMAT = @Month
        UNION ALL
        SELECT  CAST(hm.CODE AS BIGINT),
                N'متعادل‌کننده',
                hm.IMBIBE_MANF,
                hm.IMBIBE_MANF + (ba.Amount / NULLIF(ba.Qty, 0))
        FROM    dbo.HEAD_MANF hm
        JOIN    #BalancerAgg ba ON CAST(hm.CODE AS BIGINT) = ba.Code
        WHERE   hm.GHEYMAT = @Month
    ) q
    WHERE  q.NerkhAfter < 0;

    IF @WhatIf = 1
    BEGIN
        SELECT  a.Code               AS کد_کالا,
                s.NAME               AS نام_کالا,
                a.SalesAmount        AS فروش,
                a.CostAmount         AS بها,
                a.SalesAmount - a.CostAmount AS سود_فعلي,
                a.AdjustAmount       AS مبلغ_تعديل,
                a.TargetKind         AS نوع_هدف,
                a.BalancingCode      AS کالاي_متعادل_کننده,
                sb.NAME              AS نام_متعادل_کننده
        FROM    #Adj a
        LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = a.Code
        LEFT    JOIN dbo.STUF_DEF sb ON TRY_CAST(sb.CODE AS BIGINT) = a.BalancingCode
        ORDER BY ABS(a.AdjustAmount) DESC;

        ---- سهم هر کالا از پخش خودکار — براي پيش‌نمايش
        SELECT  au.Code            AS کد_کالا,
                s.NAME             AS نام_کالا,
                au.Amount          AS سهم_از_پخش_خودکار,
                pm.Profit          AS سود_قبل,
                pm.Profit - au.Amount AS سود_بعد
        FROM    #AutoShare au
        JOIN    dbo.CC_ItemMargin pm ON pm.Code = au.Code AND pm.RunId = @RunId
        LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = au.Code
        ORDER BY au.Amount DESC;

        SELECT  w.BalancingCode           AS متعادل_کننده,
                w.BalancerProfitBefore    AS سود_قبل,
                w.BalancerProfitAfter     AS سود_بعد,
                N'کالاي متعادل‌کننده زيان‌ده مي‌شود' AS هشدار
        FROM    #Warn w;

        SELECT  n.Code        AS کد_کالا,
                n.Naghsh      AS نقش,
                n.NerkhBefore AS نرخ_جذب_فعلي,
                n.NerkhAfter  AS نرخ_جذب_پس_از_اعمال,
                N'نرخ جذب منفي مي‌شود — اعمال نخواهد شد' AS خطا
        FROM    #Neg n;

        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Neg)
    BEGIN
        SELECT  n.Code        AS کد_کالا,
                n.Naghsh      AS نقش,
                n.NerkhBefore AS نرخ_جذب_فعلي,
                n.NerkhAfter  AS نرخ_جذب_پس_از_اعمال
        FROM    #Neg n;

        RAISERROR(N'اعمال هدف حاشيه سود، نرخ جذب را منفي مي‌کند و بهاي تمام‌شده را خراب مي‌کند؛ کالاي متعادل‌کننده يا هدف را تغيير دهيد.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    ---- کاهش بهاي کالاي هدف: تعديل نرخ جذب دستمزد فرمول
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF - (a.AdjustAmount / NULLIF(a.QtySold, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN    #Adj a ON CAST(hm.CODE AS BIGINT) = a.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n1 INT = @@ROWCOUNT;

    ---- افزايش بهاي کالاي متعادل‌کننده (دستي يا خودکار) به همان مبلغ
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF + (ba.Amount / NULLIF(ba.Qty, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'جذب اثر معکوس هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN    #BalancerAgg ba ON CAST(hm.CODE AS BIGINT) = ba.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n2 INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S12', 1,
            CONCAT(N'هدف حاشيه سود: ', @n1, N' کالاي هدف، ', @n2, N' متعادل‌کننده (دستي+پخش خودکار)'));

    COMMIT;

    SELECT @n1 AS کالاي_هدف, @n2 AS متعادل_کننده;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S13 — داده گزارش هیئت‌مدیره

   شیت‌های موجود گزارش اکسل شما، به‌علاوه شیت جدید «خلاصه اجرا».
   خروجی چند مجموعه است که سمت سرور با ClosedXML به اکسل تبدیل می‌شود.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S13_ReportData
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Month TINYINT, @DT1 BIGINT, @DT2 BIGINT;
    SELECT @Month = PeriodMonth, @DT1 = DateFrom, @DT2 = DateTo
    FROM   dbo.CC_Run WHERE RunId = @RunId;

    ---- ۱) سود کالا به کالا
    SELECT  m.Code                        AS کد_کالا,
            s.NAME                        AS نام_کالا,
            @Month                        AS ماه,
            m.WeightKg                    AS وزن_به_کيلو,
            m.QtySold                     AS مقدار_کل,
            m.SalesAmount                 AS مبلغ_خالص,
            m.CostAmount                  AS مبلغ_ريالي,
            m.Profit                      AS سود,
            CASE WHEN m.SalesAmount <> 0
                 THEN ROUND(m.Profit / m.SalesAmount * 100, 0) END AS درصد
    FROM    dbo.CC_ItemMargin m
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = m.Code
    WHERE   m.RunId = @RunId
    ORDER BY m.Profit;

    ---- ۲) خلاصه اجرا — شيت جديدي که امروز وجود ندارد
    SELECT  r.RunId                       AS شماره_اجرا,
            r.FiscalYear                  AS سال,
            r.PeriodMonth                 AS ماه,
            r.RunNo                       AS نوبت,
            CASE r.RunKind WHEN 2 THEN N'قطعي' ELSE N'آزمايشي' END AS نوع,
            r.StartedByUser               AS کاربر,
            r.ApprovedByUser              AS تأييدکننده,
            (SELECT COUNT(*) FROM dbo.CC_FormulaChange WHERE RunId = @RunId)
                                          AS تعداد_تغيير_فرمول,
            (SELECT SUM(ISNULL(AmountVariance,0)) FROM dbo.CC_Variance WHERE RunId = @RunId)
                                          AS انحراف_مصرف,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND IsResolved = 0)
                                          AS استثناي_باز
    FROM    dbo.CC_Run r WHERE r.RunId = @RunId;

    ---- ۳) هزينه تبديل به تفکيک واحد
    SELECT  u.UnitName                    AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل' WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END AS نوع,
            c.AbsorbedAmount              AS جذب_شده,
            c.ActualAmount                AS واقعي,
            c.AdjustFactor                AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;

    ---- ۴) بيشترين تغيير نرخ — پاسخ به «چرا اين عدد عوض شد؟»
    SELECT  TOP 100
            f.FNUMB                       AS شماره_فرمول,
            sp.NAME                       AS کالاي_توليدي,
            sc.NAME                       AS ماده,
            f.FieldName                   AS فيلد,
            f.OldValue                    AS مقدار_قبل,
            f.NewValue                    AS مقدار_بعد,
            f.Reason                      AS علت
    FROM    dbo.CC_FormulaChange f
    LEFT    JOIN dbo.STUF_DEF sp ON TRY_CAST(sp.CODE AS BIGINT) = f.ParentCode
    LEFT    JOIN dbo.STUF_DEF sc ON TRY_CAST(sc.CODE AS BIGINT) = f.ChildCode
    WHERE   f.RunId = @RunId
    ORDER BY ABS(ISNULL(f.NewValue,0) - ISNULL(f.OldValue,0)) DESC;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S14 — تأیید نهایی و قفل دوره
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S14_Approve
    @RunId    INT,
    @UserName NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @kind TINYINT, @status TINYINT, @year SMALLINT, @month TINYINT;

    SELECT @kind = RunKind, @status = Status,
           @year = FiscalYear, @month = PeriodMonth
    FROM   dbo.CC_Run WHERE RunId = @RunId;

    IF @kind <> 2
    BEGIN
        RAISERROR(N'فقط اجراي قطعي قابل تأييد است.', 16, 1);
        RETURN;
    END

    IF @status <> 3
    BEGIN
        RAISERROR(N'اجرا هنوز تکميل نشده است.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Exception
               WHERE RunId = @RunId AND Severity = 2 AND IsResolved = 0)
    BEGIN
        RAISERROR(N'استثناي مسدودکننده باز وجود دارد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.CC_Run
               WHERE FiscalYear = @year AND PeriodMonth = @month
                 AND RunKind = 2 AND ApprovedAtUtc IS NOT NULL AND RunId <> @RunId)
    BEGIN
        RAISERROR(N'براي اين ماه قبلاً يک اجراي قطعي تأييد شده است.', 16, 1);
        RETURN;
    END

    BEGIN TRAN;

    UPDATE dbo.CC_Run
       SET ApprovedByUser = @UserName,
           ApprovedAtUtc  = SYSUTCDATETIME()
     WHERE RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S14', 1,
            CONCAT(N'تأييد نهايي دوره ', @year, '/', @month, N' توسط ', @UserName));

    COMMIT;

    SELECT N'دوره تأييد و قفل شد' AS وضعيت;
END
GO


PRINT N'رويه‌هاي S12 تا S14 ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, marginReportApprove,
                "CC_sp_S12_CalcMargin، CC_sp_S12b_ApplyMarginTargets، CC_sp_S13_ReportData، CC_sp_S14_Approve",
                "اسکریپت 18-margin-report-approve.sql را اجرا کنید (به CC_ItemMargin, CC_MarginTarget, CC_ConversionCost نیاز دارد).");

            // --- 19-margin-fix-kalas.sql ---
            string marginFixKalas = @"
/* ═══════════════════════════════════════════════════════════════════
   اصلاح S12 — محاسبه سود بر مبنای کاردکس

   ── چه چیزی غلط بود ──
   نسخه قبلی بهای تمام‌شده را از سند حسابداری (DEED_DTL با TAG=13)
   می‌گرفت. آن عدد، بهای لحظه صدور سند است.

   ── روش درست ──
   AVRAGE در KALAS میانگین متحرک واقعی کاردکس است و MABRIAL همان
   AVRAGE × MEGHk. برای سنجش سود این درست است، چون بهای واقعی
   موجودی را نشان می‌دهد نه بهای لحظه‌ای.

   سود = مبلغ خالص (KHFR) − مبلغ ریالی (MABRIAL)

   ستون‌های KALAS که استفاده می‌شوند:
     TAGCODE = 2   فاکتور فروش
     KHFR          MABL_K − N_MOIN  (مبلغ خالص پس از تخفیف)
     MABRIAL       AVRAGE × MEGHk   (بهای تمام‌شده از کاردکس)
     MEGHk         مقدار کل
     MEGH          وزن به کیلو

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.

   ⚠ حتماً پس از 18-margin-report-approve.sql اجرا شود — نسخه S12
   آن فایل را جایگزین می‌کند.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S12 که در CC_ItemMargin (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد — دقیقاً همان خطایی که تست واقعی گرفت.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ستون‌های جدید برای تفکیک تخفیف و برگشت */
IF COL_LENGTH('dbo.CC_ItemMargin','GrossSales') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD GrossSales FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','Discount') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD Discount FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','ReturnAmount') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD ReturnAmount FLOAT NULL;
GO
IF COL_LENGTH('dbo.CC_ItemMargin','ReturnQty') IS NULL
    ALTER TABLE dbo.CC_ItemMargin ADD ReturnQty FLOAT NULL;
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_S12_CalcMargin
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMargin WHERE RunId = @RunId;

    /* ─── فروش: TAGCODE = 2 ─── */
    ;WITH Forush AS (
        SELECT  k.CODE                       AS Code,
                SUM(k.MEGHk)                 AS Qty,
                SUM(k.MEGH)                  AS Weight,
                SUM(k.MABL_K)                AS Gross,      -- پيش از تخفيف
                SUM(ISNULL(k.N_MOIN, 0))     AS Discount,   -- تخفيف
                SUM(k.KHFR)                  AS NetSales,   -- مبلغ خالص
                SUM(k.MABRIAL)               AS CostRial    -- AVRAGE × MEGHk
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 2
          AND   k.MM = @Month
        GROUP BY k.CODE
    ),
    /* ─── برگشت از فروش: TAGCODE = 4 ─── */
    Bargasht AS (
        SELECT  k.CODE           AS Code,
                SUM(k.MEGHk)     AS Qty,
                SUM(k.KHFR)      AS NetAmount,
                SUM(k.MABRIAL)   AS CostRial
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 4
          AND   k.MM = @Month
        GROUP BY k.CODE
    )
    INSERT dbo.CC_ItemMargin
        (RunId, Code, QtySold, WeightKg, SalesAmount, CostAmount,
         UnitCost, UnitPrice, GrossSales, Discount, ReturnAmount, ReturnQty)
    SELECT  @RunId,
            f.Code,
            f.Qty      - ISNULL(b.Qty, 0),
            f.Weight,
            f.NetSales - ISNULL(b.NetAmount, 0),      -- فروش خالص پس از برگشت
            f.CostRial - ISNULL(b.CostRial, 0),       -- بهاي واقعي از کاردکس
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.CostRial - ISNULL(b.CostRial,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.NetSales - ISNULL(b.NetAmount,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            f.Gross,
            f.Discount,
            ISNULL(b.NetAmount, 0),
            ISNULL(b.Qty, 0)
    FROM    Forush f
    LEFT    JOIN Bargasht b ON b.Code = f.Code
    WHERE   f.Qty <> 0;

    /* ─── هشدار: کالاي فروش‌رفته بدون نرخ کاردکس ───
       اگر MABRIAL صفر باشد يعني AVRAGE در کاردکس صفر است و
       سود آن کالا کاملاً غلط محاسبه مي‌شود. */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-14';

    IF NOT EXISTS (SELECT 1 FROM dbo.CC_CheckRule WHERE RuleCode = 'CHK-14')
        INSERT dbo.CC_CheckRule
            (RuleCode, RuleName, StepCode, ExType, DefaultSeverity,
             RemedyText, SortOrder)
        VALUES ('CHK-14', N'فروش بدون نرخ کاردکس', 'S12', 17, 1,
                N'اين کالا فروخته شده ولي ميانگين نرخ در کاردکس صفر است، پس بهاي تمام‌شده و سودش صفر محاسبه مي‌شود. کاردکس کالا را بررسي کنيد؛ معمولاً يعني رسيد بدون مبلغ ثبت شده.',
                140);

    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S12', 'CHK-14', 17, 1, m.Code, m.SalesAmount,
            N'کالا فروخته شده ولي نرخ کاردکسش صفر است — سود غيرواقعي'
    FROM    dbo.CC_ItemMargin m
    WHERE   m.RunId = @RunId
      AND   m.CostAmount = 0
      AND   m.SalesAmount <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا: ', COUNT(*), N' کالا، سود کل ',
                   FORMAT(SUM(SalesAmount) - SUM(CostAmount), 'N0'), N' ريال'),
            (SELECT COUNT(*) AS items,
                    SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(SalesAmount) AS sales,
                    SUM(CostAmount)  AS cost
             FROM dbo.CC_ItemMargin WHERE RunId = @RunId FOR JSON PATH)
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;

    -- ستون‌های انگلیسی برای مصرف برنامه‌ای (S12_CalcMargin.MarginSummary)؛
    -- خلاصه‌ی خواناترِ فارسی (فروش ناخالص/تخفیف/برگشت) در CC_RunLog بالا ثبت شد.
    SELECT  COUNT(*)                                    AS Items,
            SUM(CASE WHEN Profit < 0 THEN 1 ELSE 0 END) AS LossItems,
            SUM(SalesAmount)                            AS TotalSales,
            SUM(CostAmount)                              AS TotalCost,
            SUM(SalesAmount) - SUM(CostAmount)          AS TotalProfit
    FROM    dbo.CC_ItemMargin WHERE RunId = @RunId;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   مقایسه: روش کاردکس در برابر روش سند حسابداری

   برای اطمینان از درستی تغییر. اگر اختلاف بزرگ بود، یعنی سند
   حسابداری با کاردکس نمی‌خواند و خودِ آن یک یافته است.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_CompareMarginMethods
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH AzKardex AS (
        SELECT  k.CODE AS Code,
                SUM(k.KHFR)    AS NetSales,
                SUM(k.MABRIAL) AS Cost
        FROM    dbo.KALAS k
        WHERE   k.TAGCODE = 2 AND k.MM = @Month
        GROUP BY k.CODE
    ),
    AzSanad AS (
        SELECT  TRY_CAST(d.HES_M AS BIGINT) AS Code,
                SUM(d.BED) - SUM(d.BES)     AS Cost
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE   d.TAG = 13 AND h.DATE_S BETWEEN @DT1 AND @DT2
          AND   TRY_CAST(d.HES_M AS BIGINT) IS NOT NULL
        GROUP BY TRY_CAST(d.HES_M AS BIGINT)
    )
    SELECT  TOP 50
            k.Code                            AS کد_کالا,
            s.NAME                            AS نام_کالا,
            ROUND(k.NetSales, 0)              AS فروش_خالص,
            ROUND(k.Cost, 0)                  AS بها_از_کاردکس,
            ROUND(ISNULL(sn.Cost, 0), 0)      AS بها_از_سند,
            ROUND(k.Cost - ISNULL(sn.Cost,0), 0) AS اختلاف,
            ROUND(k.NetSales - k.Cost, 0)     AS سود_روش_کاردکس,
            ROUND(k.NetSales - ISNULL(sn.Cost,0), 0) AS سود_روش_سند
    FROM    AzKardex k
    LEFT    JOIN AzSanad sn ON sn.Code = k.Code
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = k.Code
    WHERE   ABS(k.Cost - ISNULL(sn.Cost, 0)) > 1000
    ORDER BY ABS(k.Cost - ISNULL(sn.Cost, 0)) DESC;

    ;WITH AzKardex AS (
        SELECT SUM(k.MABRIAL) AS Cost FROM dbo.KALAS k
        WHERE k.TAGCODE = 2 AND k.MM = @Month
    ),
    AzSanad AS (
        SELECT SUM(d.BED) - SUM(d.BES) AS Cost
        FROM   dbo.DEED_DTL d JOIN dbo.DEED_HED h ON h.N_S = d.N_S
        WHERE  d.TAG = 13 AND h.DATE_S BETWEEN @DT1 AND @DT2
    )
    SELECT  ROUND((SELECT Cost FROM AzKardex), 0) AS جمع_بها_کاردکس,
            ROUND((SELECT Cost FROM AzSanad),  0) AS جمع_بها_سند,
            ROUND((SELECT Cost FROM AzKardex) -
                  (SELECT Cost FROM AzSanad), 0)  AS اختلاف_کل;
END
GO


PRINT N'S12 با منطق کاردکس بازنويسي شد.';

/* نمونه:
   EXEC dbo.CC_sp_CompareMarginMethods @Month=4, @DT1=14050401, @DT2=14050431;
*/
GO
";
            TryExecuteCostCloseBatch(db, marginFixKalas,
                "CC_sp_S12_CalcMargin (نسخه کاردکس)، CC_sp_CompareMarginMethods",
                "اسکریپت 19-margin-fix-kalas.sql را اجرا کنید (به دیدگاه KALAS و ستون‌های KHFR/MABRIAL/TAGCODE/MM نیاز دارد).");

            // --- 20-material-rebalance.sql ---
            string materialRebalance = @"
/* ═══════════════════════════════════════════════════════════════════
   جابه‌جایی مقدار مصرف ماده بین دو فرمول (اصلاح روی مواد، نه هزینه تبدیل)

   کاربرد: وقتی یک کالای فروش‌رفته (مثلاً پنیر اولیه) زیان‌ده است چون
   مصرف یک ماده‌ی کلیدی (مثلاً شیر اسکیم) در فرمولش بالاست، به‌جای
   دست‌کاری نرخ جذب دستمزد (IMBIBE_MANF در S12b که برای این حالت لور
   درستی نیست)، مقدار فیزیکی مصرف آن ماده از فرمول کالای فروش‌رفته کم
   و به فرمول کالای هم‌خانواده‌ای که در تولید مصرف می‌شود (نه فروخته
   می‌شود) اضافه می‌شود — جمع کل مصرف فیزیکی آن ماده در ماه ثابت
   می‌ماند، پس S08/S09 (انحراف مصرف) چیزی نمی‌بیند.

   دقیقاً همان الگوی محاسبه‌ی «مقدار تولید هر فرمول» را که
   CC_sp_S09_ApplyDecisions استفاده می‌کند به کار می‌بریم، تا مقدار
   فیزیکیِ ورودی کاربر (کیلو/لیتر ماده) به دلتای MEGHk هر فرمول تبدیل
   شود؛ چون MEGHk (نه MEGH) همان فیلدی است که S11 برای محاسبه‌ی بهای
   تمام‌شده واقعاً می‌خواند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_RebalanceMaterialQty
    @RunId          INT,
    @Month          TINYINT,
    @DT1            BIGINT,
    @DT2            BIGINT,
    @MaterialCode   BIGINT,
    @FromParentCode BIGINT,
    @ToParentCode   BIGINT,
    @Qty            FLOAT,      -- مقدار فیزیکی ماده که جابه‌جا می‌شود (واحد کاردکس ماده)
    @WhatIf         BIT = 1,
    -- فهرست FNUMB فرمول‌هایی که کاربر تیک زده (با کاما). NULL یعنی همه‌ی
    -- فرمول‌های هر دو کالا که این ماده را مصرف می‌کنند و سند تولید دارند.
    @SelectedFNUMBs NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Qty IS NULL OR @Qty <= 0
    BEGIN
        RAISERROR(N'مقدار جابه‌جایی باید عددی مثبت باشد.', 16, 1);
        RETURN;
    END

    IF @FromParentCode = @ToParentCode
    BEGIN
        RAISERROR(N'فرمول مبدأ و مقصد نمی‌توانند یکی باشند.', 16, 1);
        RETURN;
    END

    ---- مقدار توليد هر فرمول در اين ماه — عيناً منطق CC_sp_S09_ApplyDecisions
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB,
            SUM(pl.MEGHK)             AS ProdQty
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
    GROUP BY TRY_CAST(pl.N_KOL AS INT)
    HAVING  SUM(pl.MEGHK) > 0;

    ---- فرمول‌های دو طرف
    -- ⚠ یک کالا می‌تواند در یک ماه بیش از یک فرمول داشته باشد. نمونه‌ی واقعی:
    -- کد ۳۷۳ «شیر اسکیم» در اردیبهشت ۱۴۰۵ دو فرمول دارد (FNUMB ۲۳۷۳ و
    -- ۸۲۶۰۳۱۴۸۲). نسخه‌ی قبلی اینجا «SELECT TOP 1 ... » بدون ORDER BY داشت،
    -- یعنی خودسرانه و غیرقطعی یکی را برمی‌داشت و کسر می‌توانست از فرمول
    -- اشتباه برداشته شود — بدون اینکه کاربر بفهمد کدام انتخاب شده.
    --
    -- منطق درست: همه‌ی فرمول‌های آن کالا با هم و «به یک میزان» تغییر کنند،
    -- یعنی دلتای MEGHk یکسان روی هرکدام. چون
    --     جمع مقدار جابه‌جاشده = دلتا × Σ(مقدار تولید) = @Qty
    -- کل مصرف فیزیکی ماده در ماه ثابت می‌ماند و S08/S09 (انحراف مصرف)
    -- چیزی نمی‌بیند — همان تضمینی که این ابزار از ابتدا می‌داد، ولی حالا
    -- برای حالت چندفرمولی هم برقرار است.
    --
    -- فرمولی که در این بازه سند تولید ندارد کنار گذاشته می‌شود، نه اینکه
    -- کل عملیات را رد کند: بدون تولید، تغییر MEGHk آن هیچ مصرف فیزیکی‌ای
    -- را در این ماه جابه‌جا نمی‌کند.
    IF OBJECT_ID('tempdb..#Sel') IS NOT NULL DROP TABLE #Sel;

    SELECT  d.FNUMB,
            d.CODE,
            TRY_CAST(hm.CODE AS BIGINT) AS ParentCode,
            CASE WHEN TRY_CAST(hm.CODE AS BIGINT) = @FromParentCode
                 THEN -1 ELSE 1 END     AS Dir,
            d.MEGH,
            d.MEGHk,
            ISNULL(d.PERT, 0)           AS Pert,
            -- نسبتِ واحدِ ردیف به واحد اصلیِ کالا. مرجعش VAHEDS است — دقیقاً
            -- همان چیزی که فرم فرمولِ نرم‌افزار قدیمی می‌خواند:
            --     Me.MEGHk = Me.MEGH * VAHEDS.NESBAT
            -- (نه VAH_SUB؛ آن دو در ۱۵ ردیف با هم اختلاف دارند.)
            vv.NESBAT                   AS UnitRatio,
            ISNULL(d.SMABL, 0)          AS Rate,
            p.ProdQty
    INTO    #Sel
    FROM    dbo.DTL_MANF d
    JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
    JOIN    #Prod p ON p.FNUMB = d.FNUMB
    LEFT    JOIN dbo.VAHEDS vv
            ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
           AND vv.VAHED = d.VAHED_K
    WHERE   TRY_CAST(d.CODE AS BIGINT) = @MaterialCode
      AND   TRY_CAST(hm.CODE AS BIGINT) IN (@FromParentCode, @ToParentCode)
      AND   p.ProdQty > 0
      AND   (@SelectedFNUMBs IS NULL
             OR d.FNUMB IN (SELECT TRY_CAST(value AS INT)
                            FROM   STRING_SPLIT(@SelectedFNUMBs, ',')
                            WHERE  TRY_CAST(value AS INT) IS NOT NULL));

    IF NOT EXISTS (SELECT 1 FROM #Sel WHERE Dir = -1)
       OR NOT EXISTS (SELECT 1 FROM #Sel WHERE Dir = 1)
    BEGIN
        RAISERROR(N'برای یکی از دو کالا هیچ فرمولی پیدا نشد که هم این ماده را مصرف کند و هم در این بازه سند تولید داشته باشد.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM #Sel GROUP BY FNUMB HAVING COUNT(*) > 1)
    BEGIN
        RAISERROR(N'این ماده در یکی از فرمول‌ها بیش از یک ردیف (چند انبار) دارد؛ این حالت با این ابزار پشتیبانی نمی‌شود — دستی اصلاح کنید.', 16, 1);
        RETURN;
    END

    -- همان بررسی‌ای که فرم فرمولِ نرم‌افزار قدیمی هم دارد: بدون نسبتِ واحد
    -- نمی‌شود «مقدار» را از «مقدار کل» به دست آورد. سکوت کردن اینجا یعنی
    -- نوشتنِ یک عدد حدسی در فرمول.
    IF EXISTS (SELECT 1 FROM #Sel WHERE UnitRatio IS NULL OR UnitRatio = 0)
    BEGIN
        RAISERROR(N'واحد تعریف‌شده ناقص است و نسبت آن مشخص نگردیده — در بخش تعریف کالا آن را اصلاح کنید.', 16, 1);
        RETURN;
    END

    DECLARE @FromProdQty FLOAT, @ToProdQty FLOAT;

    -- جدا، نه با CASE داخل یک SUM: آن شکل برای هر سطرِ طرف مقابل یک NULL
    -- می‌سازد و SQL Server هشدار «Null value is eliminated by an aggregate»
    -- می‌دهد — بی‌ضرر ولی در لاگ‌ها گمراه‌کننده.
    SELECT @FromProdQty = SUM(ProdQty) FROM #Sel WHERE Dir = -1;
    SELECT @ToProdQty   = SUM(ProdQty) FROM #Sel WHERE Dir =  1;

    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- دلتای «مقدار» = دلتای «مقدار کل» ÷ نسبت واحد. @Qty در واحد کاردکس
    -- (واحد اصلی) است، پس مستقیماً روی MEGHk می‌نشیند و برای MEGH باید به
    -- واحد خودِ ردیف برگردانده شود — عکسِ همان MEGHk = MEGH × NESBAT.
    SELECT  s.FNUMB, s.CODE, s.MEGH, s.MEGHk, s.Pert, s.Rate,
            s.ParentCode, s.ProdQty, s.UnitRatio,
            d.Delta,
            d.Delta / s.UnitRatio AS MeghDelta
    INTO    #Rows
    FROM    #Sel s
    CROSS   APPLY (SELECT s.Dir * @Qty / CASE WHEN s.Dir = -1 THEN @FromProdQty
                                                              ELSE @ToProdQty END) AS d(Delta);

    IF EXISTS (SELECT 1 FROM #Rows WHERE MEGHk + Delta < 0 OR MEGH + MeghDelta < 0)
    BEGIN
        RAISERROR(N'این مقدار بیشتر از مصرف فعلیِ فرمول مبدأ است — عدد کوچک‌تری وارد کنید.', 16, 1);
        RETURN;
    END

    -- خروجی — چه پیش‌نمایش (WhatIf=1) چه بعد از اعمال (WhatIf=0)، از روی همین
    -- #Rows محاسبه می‌شود (مقادیر پیش از UPDATE در آن ثابت مانده)، تا کلاینت
    -- (Dapper → RebalancePreviewDto) یک شکل واحد ببیند. نام ستون‌ها انگلیسی‌اند
    -- چون قرار است روی یک DTO تایپ‌شده map شوند، نه فقط برای نمایش خام.
    IF @WhatIf = 0
    BEGIN
        BEGIN TRAN;

        -- ⚠ هر سه ستون با هم، طبق همان قراردادی که فرم فرمولِ نرم‌افزار
        -- قدیمی رعایت می‌کند:
        --     MEGHk = MEGH * VAHEDS.NESBAT
        --     MABLK = (PERT + MEGHk) * SMABL
        --
        -- نسخه‌ی قبلی فقط MEGHk را جابه‌جا می‌کرد (چون S11 برای بهای
        -- تمام‌شده همان را می‌خواند) و «مقدار» را دست‌نخورده می‌گذاشت، پس هر
        -- بار اجرا این دو ستون را از هم دورتر می‌کرد. MABLK هم PERT را جا
        -- انداخته بود؛ روی ردیف‌هایی با ضایعاتِ غیرصفر مبلغ را کم می‌داد.
        --
        -- سمت راستِ SET همیشه مقدارِ *پیش از* به‌روزرسانی را می‌خواند، پس
        -- هر سه از روی مقادیر قدیمی + دلتا حساب می‌شوند.
        UPDATE  d
           SET  d.MEGH  = d.MEGH  + r.MeghDelta,
                d.MEGHk = d.MEGHk + r.Delta,
                d.MABLK = ROUND((ISNULL(d.PERT, 0) + d.MEGHk + r.Delta) * r.Rate, 0)
        OUTPUT  @RunId, 'MANUAL', inserted.FNUMB,
                r.ParentCode, TRY_CAST(inserted.CODE AS BIGINT), 'MEGHk',
                deleted.MEGHk, inserted.MEGHk,
                N'جابه‌جایی مصرف ماده بین فرمول‌ها'
          INTO  dbo.CC_FormulaChange
                (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                 FieldName, OldValue, NewValue, Reason)
        FROM    dbo.DTL_MANF d
        JOIN    #Rows r ON r.FNUMB = d.FNUMB AND r.CODE = d.CODE;

        COMMIT;
    END

    -- FNUMB هم برمی‌گردد چون یک کالا می‌تواند چند فرمول داشته باشد و بدون آن
    -- دو سطرِ خروجی با نام یکسان تفکیک‌ناپذیر می‌شوند.
    SELECT  r.FNUMB                         AS FNUMB,
            r.ParentCode                    AS ParentCode,
            s.NAME                          AS ParentName,
            r.MEGH                          AS MEGHBefore,
            r.MEGH + r.MeghDelta            AS MEGHAfter,
            r.MEGHk                         AS MEGHkBefore,
            r.MEGHk + r.Delta               AS MEGHkAfter,
            r.Rate                          AS Rate,
            r.Rate * r.MEGHk                AS CostPerUnitBefore,
            r.Rate * (r.MEGHk + r.Delta)    AS CostPerUnitAfter,
            r.ProdQty                       AS ProdQty
    FROM    #Rows r
    LEFT    JOIN dbo.STUF_DEF s ON TRY_CAST(s.CODE AS BIGINT) = r.ParentCode
    ORDER BY r.ParentCode, r.FNUMB;
END
GO

PRINT N'رويه CC_sp_RebalanceMaterialQty ايجاد شد.';
GO
";
            TryExecuteCostCloseBatch(db, materialRebalance,
                "CC_sp_RebalanceMaterialQty (جابه‌جایی مقدار ماده بین فرمول‌ها)",
                "اسکریپت 20-material-rebalance.sql را اجرا کنید (به DTL_MANF/VAHEDS و CC_FormulaChange نیاز دارد).");

            // --- 21-mogha-anbar-tiebreak-fix.sql ---
            string moghaAnbarTiebreak = @"
/* ═══════════════════════════════════════════════════════════════════
   رفع مغایرت غیرقطعی dbo.MOGHA_ANBAR — تای‌برک آخرین نرخ

   dbo.MOGHA_ANBAR («کارت انبار»، مرجع رسمی این گزارش در کل سیستم و
   پایه‌ی CHK-02 در ماژول بستن ماه) آخرین نرخ هر (کالا،انبار) را با
   ROW_NUMBER() OVER (ORDER BY DATE_N DESC, BARGAH DESC, NUMBER DESC)
   پیدا می‌کند. وقتی یک سند، یک کالا را در چند ردیف با نرخ‌های متفاوت
   ثبت کرده باشد (مثلاً دو محموله‌ی هم‌روز با نرخ فرق)، این سه ستون
   کاملاً هم‌تراز می‌شوند — و بدون یک تای‌برک نهایی، SQL Server ترتیب
   بین ردیف‌های هم‌رتبه را تضمین نمی‌کند. نتیجه: MABLK همین تابع، بدون
   هیچ تغییری در داده، بین دو اجرای پشت‌سرهم می‌توانست عوض شود.

   کشف شد روی کد ۳۰۹۲/انبار۳ (سند تولید شماره ۸۹۱، ۱۱ واحد با نرخ
   ۱,۵۸۰,۳۲۹ روی id کوچک‌تر، ۳۴۷ واحد با نرخ ۱,۷۰۰,۱۴۰ روی id بزرگ‌تر) —
   بین اجراهای CHK-02 گاهی ۹۰۰+ میلیون ریال مغایرتِ کاذب می‌ساخت.

   رفع: id DESC (آخرین ردیفی که نوشته شده) به‌عنوان تای‌برک نهایی. تأیید
   شد که نرخِ روی id بزرگ‌تر دقیقاً همان نرخی است که تمام اسناد *بعدی*
   (AVRAGE2شان) واقعاً استفاده کرده‌اند — یعنی موتور نرخ میانگین، بعد از
   پردازش هر دو ردیفِ همین سند به ترتیب، روی همین عدد نهایی نشسته. با
   این تای‌برک، مانده‌ی MABLK دقیقاً با مانده‌ی حسابداری برابر شد
   (۱۰,۱۶۴,۳۸۹,۴۷۱ = ۱۰,۱۶۴,۳۸۹,۴۷۱، تا ریال) — نه فقط پایدار.

   همان تای‌برک در Server/Database/14-s05-gate.sql (LastAvgRanked، مبنای
   CHK-02) هم اضافه شده — این دو باید هم‌زمان دیپلوی شوند وگرنه CHK-02 و
   گزارش کارت انبار اصلی دوباره از هم فاصله می‌گیرند.

   طبق AGENTS.md، این فایل دقیقاً باید با نسخه‌ی
   External/ScriptSqly/ScriptSqly.Core/ScriptSqly.Main.cs (تابع
   MOGHA_ANBAR) همگام بماند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER FUNCTION [dbo].[MOGHA_ANBAR] (@dt2 INT, @ANBAR INT, @KOL INT)
RETURNS TABLE
AS
RETURN (
    WITH
    avl_sub AS (
        -- موجودی اولیه
        SELECT CODE, SUM(MOGODI_A) AS MEG, SUM(MABL_A) AS SumOfMABL_A, ANBAR
        FROM dbo.STUF_FSK
        GROUP BY CODE, ANBAR
        HAVING ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- خرید، برگشت فروش، تولید، سایر ورودی (TAG 1,7,9,24)
        SELECT i.CODE, SUM(i.MEGHk), SUM(i.MABL_K), i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG IN (1, 7, 9, 24) AND h.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- ایجاد موجودی (TAG 22)
        SELECT i.CODE, SUM(i.MEGH_MAR), SUM(i.MABL * i.MEGH_MAR), i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG = 22 AND h.DATE_N <= @dt2 AND i.MEGH_MAR <> 0
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- ورودی از انتقال (TAG 5 - انبار مقصد)
        SELECT i.CODE, SUM(i.MEGHk), SUM(i.MABL_K), i.ANBARF
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG = 5 AND h.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBARF
        HAVING i.ANBARF LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- انبارگردانی (ورودی)
        SELECT l.CODE, SUM((l.MOG - l.NUM3) * -1), SUM(ABS(l.MOG - l.NUM3) * l.MABL), a.GRD_ANBAR
        FROM dbo.ANBGRD_LST l INNER JOIN dbo.ANBGRD_HEAD a ON l.GRD_NUM = a.GRD_NUM
        WHERE a.GRD_DATE <= @dt2 AND a.N_S IS NOT NULL
              AND a.GRD_ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
        GROUP BY l.CODE, a.GRD_ANBAR
        HAVING SUM((l.MOG - l.NUM3) * -1) >= 0

        UNION ALL

        -- برگشت فروش (TAG مجازی 4): کالا از مشتری به انبار برمی‌گردد (ورودی)
        SELECT i.CODE, SUM(i.MEGH_MAR), SUM(i.MABL * i.MEGH_MAR), i.ANBAR
        FROM dbo.BACK_HEAD bh
             INNER JOIN dbo.INVO_LST i ON bh.ta = i.TAG AND bh.NUMBER1 = i.NUMBER
        WHERE bh.ta + 2 = 4 AND i.MEGH_MAR <> 0 AND bh.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
    ),
    avl AS (
        SELECT CODE, SUM(NULLIF(MEG, 0)) AS SMEGH, SUM(SumOfMABL_A) AS SMABLA, ANBAR
        FROM avl_sub
        GROUP BY CODE, ANBAR
    ),
    fr_sub AS (
        -- فروش، انتقال، برگشت خرید، سایر خروجی (TAG 2,5,8,10,11,26)
        SELECT i.CODE, SUM(i.MEGHk) AS MEG, i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG IN (2, 5, 8, 10, 11, 26) AND h.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- انبارگردانی (خروجی)
        SELECT l.CODE, SUM(l.MOG - l.NUM3), a.GRD_ANBAR
        FROM dbo.ANBGRD_LST l INNER JOIN dbo.ANBGRD_HEAD a ON l.GRD_NUM = a.GRD_NUM
        WHERE a.GRD_DATE <= @dt2 AND a.N_S IS NOT NULL
              AND a.GRD_ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
        GROUP BY l.CODE, a.GRD_ANBAR
        HAVING SUM(l.MOG - l.NUM3) > 0

        UNION ALL

        -- تعمیر (TAG 20)
        SELECT i.CODE, SUM(i.MEGHK), i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG = 20 AND h.DATE_N <= @dt2 AND (h.TAMIR = 1 OR h.TAMIR = 4)
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- برگشت خرید (TAG مجازی 3): کالا به تأمین‌کننده برمی‌گردد (خروجی)
        SELECT i.CODE, SUM(i.MEGH_MAR) AS MEG, i.ANBAR
        FROM dbo.BACK_HEAD bh
             INNER JOIN dbo.INVO_LST i ON bh.ta = i.TAG AND bh.NUMBER1 = i.NUMBER
        WHERE bh.ta + 2 = 3 AND i.MEGH_MAR <> 0 AND bh.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
    ),
    fr AS (
        SELECT CODE, SUM(MEG) AS MEG, ANBAR
        FROM fr_sub
        GROUP BY CODE, ANBAR
    ),
    -- مرتب‌سازی مطابق کارت انبار: DATE_N، BARGAH (از TAGCOD)، NUMBER
    lastav_base AS (
        SELECT i.CODE, i.ANBAR, i.AVRAGE AS AVRAGE, h.DATE_N, t.BARGAH, i.NUMBER, i.ID
        FROM dbo.INVO_LST i
             INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
             INNER JOIN dbo.TAGCOD t ON i.TAG = t.CODE
        WHERE h.DATE_N <= @dt2 AND i.TAG IN (1, 7, 9, 24)

        UNION ALL

        -- وارده از انتقال (ANBARF = انبار مقصد)
        SELECT i.CODE, i.ANBARF, i.AVRAGE2, h.DATE_N, t.BARGAH, i.NUMBER, i.ID
        FROM dbo.INVO_LST i
             INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
             INNER JOIN dbo.TAGCOD t ON i.TAG = t.CODE
        WHERE h.DATE_N <= @dt2 AND i.TAG = 5
    ),
    lastav AS (
        SELECT CODE, ANBAR, AVRAGE,
               ROW_NUMBER() OVER (PARTITION BY CODE, ANBAR ORDER BY DATE_N DESC, BARGAH DESC, NUMBER DESC, ID DESC) AS rn
        FROM lastav_base
    ),
    kart_anbar AS (
        SELECT
            sf.CODE,
            sf.ANBAR,
            ROUND(ISNULL(ISNULL(avl.SMEGH, 0) - ISNULL(fr.MEG, 0), 0), 2) AS MAND,
            ISNULL(
                COALESCE(la.AVRAGE, sf.FI_A, 0) *
                ROUND(ISNULL(ISNULL(avl.SMEGH, 0) - ISNULL(fr.MEG, 0), 0), 2),
                0
            ) AS MABLK
        FROM dbo.STUF_FSK sf
        INNER JOIN avl ON sf.CODE = avl.CODE AND sf.ANBAR = avl.ANBAR
        LEFT  JOIN fr  ON sf.CODE = fr.CODE  AND sf.ANBAR = fr.ANBAR
        LEFT  JOIN (SELECT CODE, ANBAR, AVRAGE FROM lastav WHERE rn = 1) la
               ON sf.CODE = la.CODE AND sf.ANBAR = la.ANBAR
        WHERE sf.ANBAR = @ANBAR
    ),
    hesab AS (
        SELECT d.HES_K, d.HES_M, SUM(d.BED - d.BES) AS mand, d.HES_T, d.HES
        FROM dbo.DEED_DTL d INNER JOIN dbo.DEED_HED h ON d.N_S = h.N_S
        WHERE h.DATE_S <= @dt2 AND d.HES_K = @KOL AND d.HES_M = @ANBAR
        GROUP BY d.HES_K, d.HES_M, d.HES_T, d.HES
    )
    SELECT
        ka.CODE,
        ROUND(ka.MABLK, 0)                                                             AS MABLK,
        ka.MAND,
        ISNULL(he.mand, 0)                                                             AS mab,
        CASE WHEN (ka.MABLK - ISNULL(he.mand, 0)) > 0
             THEN ROUND(ka.MABLK - ISNULL(he.mand, 0), 0)
             ELSE 0 END                                                                AS tafBED,
        CASE WHEN (ka.MABLK - ISNULL(he.mand, 0)) <= 0
             THEN ROUND(ka.MABLK - ISNULL(he.mand, 0), 0) * -1
             ELSE 0 END                                                                AS TAFBES,
        he.HES_T,
        he.HES_K,
        he.HES_M,
        he.HES
    FROM kart_anbar ka
    LEFT JOIN hesab he ON ka.CODE = he.HES_T
);
GO

PRINT N'تابع MOGHA_ANBAR با تای‌برک id DESC بازنویسی شد.';
GO
";
            TryExecuteCostCloseBatch(db, moghaAnbarTiebreak,
                "اصلاح tie-break در dbo.MOGHA_ANBAR",
                "اسکریپت 21-mogha-anbar-tiebreak-fix.sql را اجرا کنید.");

            // --- 22-runstep-attempt-int.sql ---
            string runStepAttemptInt = @"
/* ═══════════════════════════════════════════════════════════════════
   عریض کردن CC_RunStep.Attempt از TINYINT به INT

   ── چه چیزی خراب بود ──
   Attempt با TINYINT تعریف شده بود (سقف ۲۵۵). شماره‌ی تلاش در
   CC_sp_StepStart این‌طور حساب می‌شود:

       MAX(Attempt) برای همان RunId/StepCode  +  ۱

   یعنی شمارنده بین اجراهای مکررِ یک Run انباشته می‌شود و هرگز صفر
   نمی‌شود. حلقه‌ی همگرایی S07A↔S11 در هر اجرا تا ۴۰ دور می‌رود
   (MaxS11Cycles در CloseOrchestrator)، پس چند بار «اجرای مجدد گام‌ها»
   روی یک ماه کافی است تا از ۲۵۵ رد شود.

   روی ران واقعی اردیبهشت ۱۴۰۵ (RunId=6) دقیقاً همین شد: S07A به
   Attempt=255 رسید و دور بعد کل بستن ماه با این خطا متوقف شد:

       Arithmetic overflow error for data type tinyint, value = 256.
       Cannot insert the value NULL into column 'Attempt' ...

   (سرریزِ محاسبه، مقدار را NULL کرد و INSERT روی ستون NOT NULL شکست.)

   ── چرا INT و نه SMALLINT ──
   SMALLINT فقط سقف را به ۳۲٬۷۶۷ می‌برد؛ همان مسئله را دورتر می‌کند نه
   حل. INT با RunStepId هم‌نوع است و عملاً بی‌سقف.

   ⚠ محدودیت UQ_CC_RunStep روی (RunId, StepCode, Attempt) است، پس باید
   قبل از تغییر نوع ستون حذف و بعد دوباره ساخته شود.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* هر سه مرحله جدا و شرطی‌اند تا اسکریپت idempotent باشد و وضعیت
   نیمه‌مهاجرت را هم ترمیم کند (اگر اجرای قبلی وسط کار شکست خورده و
   محدودیت یکتا حذف شده ولی نوع ستون عوض نشده باشد). */

-- ۱) محدودیت یکتا شامل Attempt است، پس باید موقتاً برداشته شود
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UQ_CC_RunStep'
             AND parent_object_id = OBJECT_ID('dbo.CC_RunStep'))
BEGIN
    ALTER TABLE dbo.CC_RunStep DROP CONSTRAINT UQ_CC_RunStep;
    PRINT N'UQ_CC_RunStep موقتاً حذف شد.';
END
GO

-- ۲) تبدیل نوع ستون
--    ⚠ قید DEFAULT هم به ستون وابسته است و ALTER COLUMN را بلاک می‌کند
--    (خطای 5074). نامش خودکار ساخته شده (مثل DF__CC_RunSte__Attem__2CA81010)
--    و در هر نصب فرق می‌کند، پس باید از کاتالوگ خوانده شود نه هاردکد.
IF EXISTS (SELECT 1
           FROM   sys.columns
           WHERE  object_id = OBJECT_ID('dbo.CC_RunStep')
             AND  name      = 'Attempt'
             AND  system_type_id = TYPE_ID('tinyint'))
BEGIN
    PRINT N'در حال تبدیل CC_RunStep.Attempt از TINYINT به INT ...';

    DECLARE @df SYSNAME, @sql NVARCHAR(MAX);

    SELECT @df = dc.name
    FROM   sys.default_constraints dc
    JOIN   sys.columns c ON c.object_id = dc.parent_object_id
                        AND c.column_id = dc.parent_column_id
    WHERE  dc.parent_object_id = OBJECT_ID('dbo.CC_RunStep')
      AND  c.name = 'Attempt';

    IF @df IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.CC_RunStep DROP CONSTRAINT ' + QUOTENAME(@df);
        EXEC sp_executesql @sql;
    END

    ALTER TABLE dbo.CC_RunStep ALTER COLUMN Attempt INT NOT NULL;

    -- این بار با نام ثابت، تا دفعه‌ی بعد لازم نباشد از کاتالوگ پیدایش کنیم
    ALTER TABLE dbo.CC_RunStep
        ADD CONSTRAINT DF_CC_RunStep_Attempt DEFAULT 1 FOR Attempt;

    PRINT N'نوع ستون به INT تبدیل شد.';
END
ELSE
    PRINT N'CC_RunStep.Attempt از قبل TINYINT نیست — تبدیل لازم نبود.';
GO

-- ۳) بازگرداندن محدودیت یکتا
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE name = 'UQ_CC_RunStep'
                 AND parent_object_id = OBJECT_ID('dbo.CC_RunStep'))
BEGIN
    ALTER TABLE dbo.CC_RunStep
        ADD CONSTRAINT UQ_CC_RunStep UNIQUE (RunId, StepCode, Attempt);
    PRINT N'UQ_CC_RunStep بازگردانده شد.';
END
GO

/* CC_sp_StepStart هم متغیر داخلی‌اش TINYINT بود و مستقل از نوعِ ستون
   سرریز می‌کرد؛ نسخه‌ی اصلاح‌شده در 12-procedures-phase1.sql است و باید
   دوباره اجرا شود. برای اینکه این فایل به‌تنهایی هم کامل باشد، همان
   نسخه اینجا تکرار شده است. */
CREATE OR ALTER PROCEDURE dbo.CC_sp_StepStart
    @RunId    INT,
    @StepCode VARCHAR(10),
    @Title    NVARCHAR(120),
    @SeqNo    SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @try INT =
        ISNULL((SELECT MAX(Attempt) FROM dbo.CC_RunStep
                WHERE RunId = @RunId AND StepCode = @StepCode), 0) + 1;

    INSERT dbo.CC_RunStep (RunId, StepCode, StepTitle, SeqNo, Attempt, Status, StartedAtUtc)
    VALUES (@RunId, @StepCode, @Title, @SeqNo, @try, 1, SYSUTCDATETIME());

    UPDATE dbo.CC_Run
       SET Status = 1, StartedAtUtc = ISNULL(StartedAtUtc, SYSUTCDATETIME())
     WHERE RunId = @RunId;
END
GO

PRINT N'اسکریپت 22-runstep-attempt-int.sql اجرا شد.';
GO
";
            TryExecuteCostCloseBatch(db, runStepAttemptInt,
                "پهن کردن CC_RunStep.Attempt از TINYINT به INT",
                "اسکریپت 22-runstep-attempt-int.sql را اجرا کنید (به CC_RunStep نیاز دارد).");

            // --- 23-fix-dtl-manf-megh.sql ---
            string fixDtlManfMegh = @"
/* ═══════════════════════════════════════════════════════════════════
   همگام‌سازی DTL_MANF.MEGH («مقدار») با MEGHk («مقدار کل»)

   ── قرارداد ──
   فرم فرمولِ نرم‌افزار قدیمی این رابطه را نگه می‌دارد:

       MEGHk = MEGH * VAHEDS.NESBAT        (نسبت واحد ردیف به واحد اصلی)
       MABLK = (PERT + MEGHk) * SMABL

   ── چه چیزی خراب شده بود ──
   گام‌های بستن ماه و ابزار «جابه‌جایی مصرف ماده بین فرمول‌ها» فقط MEGHk را
   می‌نوشتند و MEGH را دست‌نخورده می‌گذاشتند، چون S11 برای بهای تمام‌شده
   همان MEGHk را می‌خواند. ولی MEGH مرده نیست: در S07
   (17-variance-steps.sql) حواله‌ی خروج مواد از روی هر دو ساخته می‌شود —

       INVO_LST.MEGH  = (dm.MEGH  + dm.PERT) * مقدار توليد
       INVO_LST.MEGHK = (dm.MEGHK + dm.PERT) * مقدار توليد

   پس مقدارِ فیزیکیِ ثبت‌شده در حواله‌ها با مبنای بها نمی‌خواند و کاربر در
   فرم فرمول ستون «مقدار» را کهنه می‌بیند. انحراف بین اجراها انباشته می‌شد.

   ── جهت اصلاح ──
   MEGHk مرجع است (تأیید صاحب پروژه): تنظیماتی که S09/S11 و بستن ماه روی
   MEGHk نوشته‌اند درست‌اند و نباید برگردند. پس

       MEGH := MEGHk / NESBAT

   نه برعکس. بازمحاسبه‌ی MEGHk از روی MEGH همه‌ی آن تنظیمات را پاک می‌کرد.

   ── دامنه ──
   ردیف‌هایی که نسبت واحدشان در VAHEDS تعریف نشده کنار گذاشته می‌شوند —
   بدون نسبت، «مقدار» قابل استخراج نیست و نوشتن عدد حدسی بدتر از نساختن
   آن است. تعدادشان در گزارش پایان اسکریپت می‌آید.

   ⚠ پیش از هر تغییر، ردیف‌های متأثر در CC_BAK_DTL_MANF_MEGHFIX نگهداری
   می‌شوند تا برگرداندن ممکن بماند.

   ⚠ پس از اجرا باید S07 دوباره اجرا شود تا حواله‌های خروج مواد با مقدار
   اصلاح‌شده بازتولید شوند؛ وگرنه DTL_MANF درست است ولی INVO_LST کهنه.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب فرق
   می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
SET XACT_ABORT ON
GO

BEGIN TRAN;

/* ── ۱) پشتیبان ────────────────────────────────────────────────── */
IF OBJECT_ID('dbo.CC_BAK_DTL_MANF_MEGHFIX','U') IS NOT NULL
    DROP TABLE dbo.CC_BAK_DTL_MANF_MEGHFIX;

SELECT  d.ID, d.FNUMB, d.CODE, d.ANBAR, d.VAHED_K,
        d.MEGH  AS MEGH_Old,
        d.MEGHk AS MEGHk_Old,
        d.PERT, d.SMABL, d.MABLK AS MABLK_Old,
        vv.NESBAT,
        SYSUTCDATETIME() AS BackedUpAtUtc
INTO    dbo.CC_BAK_DTL_MANF_MEGHFIX
FROM    dbo.DTL_MANF d
JOIN    dbo.VAHEDS vv
        ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
       AND vv.VAHED = d.VAHED_K
WHERE   vv.NESBAT <> 0
  AND   ABS(d.MEGHk - d.MEGH * vv.NESBAT) > 1e-9;

DECLARE @affected INT = @@ROWCOUNT;

/* ── ۲) اصلاح ──────────────────────────────────────────────────── */
UPDATE  d
   SET  d.MEGH = d.MEGHk / vv.NESBAT
FROM    dbo.DTL_MANF d
JOIN    dbo.VAHEDS vv
        ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
       AND vv.VAHED = d.VAHED_K
WHERE   vv.NESBAT <> 0
  AND   ABS(d.MEGHk - d.MEGH * vv.NESBAT) > 1e-9;

COMMIT;
GO

/* ── ۳) گزارش ──────────────────────────────────────────────────── */
SELECT  (SELECT COUNT(*) FROM dbo.CC_BAK_DTL_MANF_MEGHFIX) AS اصلاح_شد,

        (SELECT COUNT(*)
         FROM   dbo.DTL_MANF d
         JOIN   dbo.VAHEDS vv
                ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
               AND vv.VAHED = d.VAHED_K
         WHERE  vv.NESBAT <> 0
           AND  ABS(d.MEGHk - d.MEGH * vv.NESBAT) > 1e-9) AS باقيمانده_ناهماهنگ,

        (SELECT COUNT(*)
         FROM   dbo.DTL_MANF d
         LEFT   JOIN dbo.VAHEDS vv
                ON TRY_CAST(vv.CODE AS BIGINT) = TRY_CAST(d.CODE AS BIGINT)
               AND vv.VAHED = d.VAHED_K
         WHERE  vv.NESBAT IS NULL OR vv.NESBAT = 0) AS بدون_نسبت_واحد;
GO

PRINT N'اسکریپت 23-fix-dtl-manf-megh.sql اجرا شد. پشتیبان: CC_BAK_DTL_MANF_MEGHFIX';
PRINT N'⚠ اکنون S07 را دوباره اجرا کنید تا حواله‌های خروج مواد بازتولید شوند.';
GO
";
            TryExecuteCostCloseBatch(db, fixDtlManfMegh,
                "ترمیم DTL_MANF.MEGH بر پایه MEGHk و نسبت واحد",
                "اسکریپت 23-fix-dtl-manf-megh.sql را اجرا کنید (به DTL_MANF/VAHEDS نیاز دارد).");

            // --- 24-cost-forms-selfheal.sql ---
            string costFormsSelfHeal = @"
/* ═══════════════════════════════════════════════════════════════════
   خودترمیمیِ فرم‌ها و دسترسی‌های ماژول بهای تمام‌شده

   ── مسئله‌ای که این اسکریپت حل می‌کند ──
   ۱. هر بار فرم تازه‌ای به Shared/Constants/CostForms.cs اضافه می‌شود، باید
      ردیفش در TFORMS هم ساخته شود. تا امروز این کار با ۲۱ بلوک تکراریِ
      «IF NOT EXISTS … INSERT» در 11-seed-data.sql انجام می‌شد؛ جا افتادنِ
      یکی از آن‌ها هیچ خطایی نمی‌دهد، فقط آن قابلیت بی‌صدا برای همه قفل
      می‌شود.

   ۲. مهم‌تر: ساختنِ فرم در TFORMS به‌تنهایی کافی نیست. کاربری که از قبل به
      ماژول دسترسی داشته، روی فرمِ تازه هیچ ردیفی در SAL_CHEK ندارد و با
      ACL_ENFORCE=1 پاسخِ ۴۰۳ می‌گیرد — بدون اینکه بفهمد چرا. نمونه‌ی
      واقعی: کاربر ۱۱۴ روی YAZDSEPAR1405 به COST_ACT_FIX_DATE و
      COST_ACT_RESOLVE_PERMANENT دسترسی نداشت، چون آن دو فرم بعد از
      تنظیم دسترسی‌های او اضافه شده بودند.

   ── قاعده‌ی اعطای خودکار ──
   COST_DASHBOARD «فرمِ ورودیِ» ماژول است. هر کاربری که روی آن ردیف دارد،
   کاربرِ این ماژول شمرده می‌شود و هر فرمِ COST_ که ردیفش را ندارد با
   *همان* سطح دسترسیِ COST_DASHBOARD برایش ساخته می‌شود — نه بیشتر.
   کاربری که COST_DASHBOARD ندارد اصلاً دست نمی‌خورد، پس این اسکریپت به
   هیچ‌کس دسترسیِ تازه‌ای نمی‌دهد که از قبل نداشته باشد.

   اجرای دوباره بی‌خطر است: هرچه از قبل درست باشد دست‌نخورده می‌ماند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب فرق
   می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
SET XACT_ABORT ON
GO

BEGIN TRAN;

/* ── ۱) فهرست مرجع فرم‌ها — باید با CostForms.cs یکی بماند ────────── */
DECLARE @Forms TABLE (FormName NVARCHAR(100) PRIMARY KEY, Caption NVARCHAR(200));

INSERT INTO @Forms (FormName, Caption) VALUES
    (N'COST_DASHBOARD',             N'داشبورد بستن ماه بهای تمام‌شده'),
    (N'COST_RUN',                   N'پیشرفت اجرای بستن ماه'),
    (N'COST_EXCEPTIONS',            N'مغایرت‌های بستن ماه'),
    (N'COST_VARIANCE',              N'تصمیم انحراف'),
    (N'COST_CONVERSION',            N'هزینه تبدیل'),
    (N'COST_MARGIN',                N'سود و زیان کالا'),
    (N'COST_HISTORY',               N'سوابق اجراها'),
    (N'COST_SETTINGS',              N'تنظیمات بستن ماه'),
    (N'COST_ACT_START',             N'شروع اجرای بستن ماه'),
    (N'COST_ACT_AUTOFIX',           N'اصلاح خودکار داده'),
    (N'COST_ACT_RESOLVE',           N'بستن استثنا'),
    (N'COST_ACT_DECIDE',            N'ثبت تصمیم انحراف'),
    (N'COST_ACT_APPLY_RATE',        N'اعمال ضریب تعدیل'),
    (N'COST_ACT_ROLLUP',            N'اجرای موتور نرخ'),
    (N'COST_ACT_ROLLBACK',          N'بازگردانی از اسنپ‌شات'),
    (N'COST_ACT_APPROVE',           N'تأیید نهایی و قفل ماه'),
    (N'COST_ACT_EXPORT',            N'خروجی اکسل'),
    (N'COST_ACT_REBUILD_DOCS',      N'بازسازی سند حواله خروج مواد'),
    (N'COST_ACT_POST_CORRECTION',   N'سند اصلاحی مغایرت کارت انبار/حسابداری'),
    (N'COST_ACT_RESOLVE_PERMANENT', N'پذیرش دائمی مغایرت'),
    (N'COST_ACT_FIX_DATE',          N'اصلاح تاریخ مغایرِ سند');

/* ── ۲) فرم‌های جاافتاده را به TFORMS اضافه کن ───────────────────────
   IDH با ROW_NUMBER تخصیص می‌یابد، نه با MAX(IDH)+1 داخل یک INSERT
   چندسطری — آن شکل به همه‌ی سطرها یک شناسه‌ی یکسان می‌دهد. */
DECLARE @maxIdh INT = (SELECT ISNULL(MAX(IDH), 0) FROM dbo.TFORMS);

INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
SELECT  f.FormName, f.Caption, 3, 10,
        @maxIdh + ROW_NUMBER() OVER (ORDER BY f.FormName),
        GETDATE()
FROM    @Forms f
WHERE   NOT EXISTS (SELECT 1 FROM dbo.TFORMS t WHERE t.FORMNAME = f.FormName);

DECLARE @formsAdded INT = @@ROWCOUNT;

/* ── ۳) عنوانِ فرم‌های موجود را با فهرست مرجع هم‌راستا کن ──────────── */
UPDATE  t
   SET  t.CAPTION = f.Caption
FROM    dbo.TFORMS t
JOIN    @Forms f ON f.FormName = t.FORMNAME
WHERE   ISNULL(t.CAPTION, N'') <> f.Caption;

DECLARE @captionsFixed INT = @@ROWCOUNT;

/* ── ۴) دسترسی‌های جاافتاده را برای کاربرانِ همین ماژول بساز ────────
   سطح دسترسی از COST_DASHBOARD همان کاربر کپی می‌شود. */
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], CRT)
SELECT  d.USERCO, t.IDH, d.[RUN], d.[SEE], d.[INP], d.[UPD], d.[DEL], GETDATE()
FROM    dbo.SAL_CHEK d
JOIN    dbo.TFORMS dash ON dash.IDH = d.[OBJECT]
                       AND dash.FORMNAME = N'COST_DASHBOARD'
CROSS   JOIN dbo.TFORMS t
JOIN    @Forms f ON f.FormName = t.FORMNAME
WHERE   NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK sc
                    WHERE sc.USERCO = d.USERCO AND sc.[OBJECT] = t.IDH);

DECLARE @permsAdded INT = @@ROWCOUNT;

COMMIT;
GO

/* ── ۵) گزارش ──────────────────────────────────────────────────── */
SELECT  (SELECT COUNT(*) FROM dbo.TFORMS WHERE FORMNAME LIKE 'COST[_]%') AS فرم_موجود,

        (SELECT COUNT(*)
         FROM   dbo.SAL_CHEK sc
         JOIN   dbo.TFORMS t ON t.IDH = sc.[OBJECT]
         WHERE  t.FORMNAME = N'COST_DASHBOARD')                          AS کاربر_ماژول,

        (SELECT COUNT(*)
         FROM   dbo.SAL_CHEK d
         JOIN   dbo.TFORMS dash ON dash.IDH = d.[OBJECT]
                               AND dash.FORMNAME = N'COST_DASHBOARD'
         CROSS  JOIN dbo.TFORMS t
         WHERE  t.FORMNAME LIKE 'COST[_]%'
           AND  NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK sc
                            WHERE sc.USERCO = d.USERCO
                              AND sc.[OBJECT] = t.IDH))                  AS دسترسي_جاافتاده;
GO

PRINT N'اسکریپت 24-cost-forms-selfheal.sql اجرا شد.';
GO
";
            TryExecuteCostCloseBatch(db, costFormsSelfHeal,
                "افزودن فرم‌های COST_* و دسترسی‌هایشان",
                "اسکریپت 24-cost-forms-selfheal.sql را اجرا کنید (به TFORMS و SAL_CHEK نیاز دارد).");

            // --- 25-rebalance-suggest.sql ---
            string rebalanceSuggest = @"
/* ═══════════════════════════════════════════════════════════════════
   موتور پیشنهادِ جابه‌جایی مواد برای صفر کردن زیان کالا

   ── قاعده‌ی حاکم (تصمیم صاحب پروژه) ──
   دستمزد (IMBIBE_MANF) و سربار (IMBIBE_SAR) هرگز برای تنظیم سود کالا
   دست‌کاری نمی‌شوند. تنها اهرم مجاز «مقدار مواد» است، و هر مقداری که از
   فرمولی کم می‌شود باید به فرمول کالای دیگری اضافه شود که همان ماده را
   مصرف می‌کند — تا جمع مصرف فیزیکی ماه ثابت بماند و S08/S09 انحرافی
   نبینند. اجرای واقعیِ انتقال با CC_sp_RebalanceMaterialQty انجام
   می‌شود؛ این رویه فقط «چه چیزی را از کجا به کجا» پیشنهاد می‌دهد.

   ── عمق زنجیره: هر عددی، نه فقط ۲ ──
   @MaxDepth=1 یعنی فقط مواد مستقیمِ فرمولِ کالای زیان‌ده؛ هر واحد بیشتر
   یک سطح پایین‌تر در درختِ نیمه‌ساخته‌ها می‌رود. پیمایش بازگشتی است، پس
   ۳ و ۴ و ۵ هم واقعاً کار می‌کنند (نسخه‌ی قبلی سخت‌کدشده روی ۲ بود و
   گزینه‌ی «۳» در واسط هیچ اثری نداشت). سقف ۸ فقط برای مهار حلقه است.

   ⚠ اثرِ سطوح پایین‌تر رقیق است: کم کردن ماده از فرمولِ یک نیمه‌ساخته،
   بهای آن را برای *همه‌ی* مصرف‌کنندگانش کم می‌کند نه فقط کالای زیان‌ده.
   DilutionPct حاصل‌ضربِ سهم در تمام حلقه‌های زنجیره است.

   ── مقصدها: نیمه‌ساخته هم مجاز است ──
   نسخه‌ی قبلی مقصد را به کالاهایی محدود می‌کرد که در CC_ItemMargin سود
   مثبت داشتند، یعنی فقط کالاهای *فروش‌رفته*. روشِ متعارفِ کاربر دقیقاً
   بیرون از آن دایره بود: «از شیر خام کم کن و به شیر اسکیم بریز» — و شیر
   اسکیم (۳۷۳) نیمه‌ساخته است، هرگز فروخته نمی‌شود و در CC_ItemMargin
   سطر ندارد، پس هیچ‌وقت پیشنهاد نمی‌شد.

   حالا هر کالای دارای فرمول می‌تواند مقصد باشد. ظرفیتش از روی سودِ
   کالاهای فروش‌رفته‌ی *پایین‌دستش* حساب می‌شود: اگر ΔV ریال روی مقصد
   بنشیند، هر مصرف‌کننده‌ی نهایی c سهمی به‌اندازه‌ی absorb_c از آن را
   می‌گیرد، پس سقف = MIN(Profit_c / absorb_c). برای یک کالای فروش‌رفته‌ی
   ساده absorb خودش ۱ است و این دقیقاً همان «ظرفیت = سود» قبلی می‌شود.

   ── بازگشتِ بار به کالای زیان‌ده (BouncePct) ──
   قید سختِ قبلی «مقصد نباید در درختِ ورودی‌های کالای هدف باشد» هم برداشته
   شد، چون همان قید بود که شیر اسکیم را حذف می‌کرد (۳۶۸ خودش شیر اسکیم
   مصرف می‌کند). به‌جایش سهمی که از راه همان مصرف به کالای هدف *برمی‌گردد*
   محاسبه و گزارش می‌شود: BouncePct. تسکینِ خالص = ΔV × (۱ − Bounce)،
   و ستون Capacity همین عددِ خالص است. مقصدی که ≥۹۸٪ برگردد کنار می‌رود.

   ── هشدار زیان‌دهِ پایین‌دست (LoserCount) ──
   اگر کالای زیان‌دهِ دیگری هم پایین‌دستِ مقصد باشد، هر مبلغی زیانش را
   بیشتر می‌کند. چنین کالایی در MIN بالا نمی‌آید (سقف را صفر می‌کرد)، ولی
   شمارشش برمی‌گردد تا کاربر کورکورانه انتخاب نکند.

   ── گاف شناخته‌شده ──
   نسبتِ «فروش‌رفته به تولیدشده»ی خودِ کالای مبدأ در محاسبه نیست: اگر
   ۳۶۸ بیش از فروشش تولید شده باشد، برداشتنِ V ریال از فرمولش کمتر از V
   از بهای فروش‌رفته‌اش کم می‌کند. عمداً وارد نشد تا با محاسبه‌ی مقدارِ
   انتقال در rebalance-apply هم‌خوان بماند؛ هر دو با هم باید اصلاح شوند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────── حافظه‌ی انتخاب‌های کاربر ───────────────── */
IF OBJECT_ID('dbo.CC_RebalancePref','U') IS NULL
CREATE TABLE dbo.CC_RebalancePref (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    SourceCode    BIGINT   NOT NULL,   -- کالای زیان‌ده
    MaterialCode  BIGINT   NOT NULL,   -- ماده‌ای که جابه‌جا می‌شود
    TargetCode    BIGINT   NOT NULL,   -- کالای مقصد
    SharePct      FLOAT    NULL,       -- سهم این مقصد وقتی چند مقصد هست (NULL = خودکار)
    IsActive      BIT      NOT NULL DEFAULT 1,
    Note          NVARCHAR(200) NULL,
    CRT           DATETIME NOT NULL DEFAULT GETDATE(),
    UID           INT      NULL,
    CONSTRAINT UQ_CC_RebalancePref UNIQUE (SourceCode, MaterialCode, TargetCode)
);
GO

/* انتخاب‌ها معمولاً بین ماه‌ها معتبر می‌مانند (تصمیم صاحب پروژه)، پس
   عمداً به سال/ماه مقید نیستند: نشان داده می‌شوند و فقط با درخواست صریح
   کاربر دوباره محاسبه می‌شوند. */
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_RebalanceSuggest
    @RunId      INT,
    @Month      TINYINT,
    @DT1        BIGINT,
    @DT2        BIGINT,
    @SourceCode BIGINT,
    @MaxDepth   TINYINT = 2
AS
BEGIN
    SET NOCOUNT ON;

    -- سقف ۸: پیمایش بازگشتی است و فرمول‌ها می‌توانند حلقه بسازند
    -- (نیمه‌ساخته‌ای که برگشتی خودش را مصرف کند). عمقِ محدود تنها مهارِ
    -- مطمئنی است که CTE بازگشتیِ SQL Server بدون «مجموعه‌ی دیده‌شده‌ها»
    -- در اختیار می‌گذارد.
    IF @MaxDepth IS NULL OR @MaxDepth < 1 SET @MaxDepth = 1;
    IF @MaxDepth > 8 SET @MaxDepth = 8;

    ---- کسری: مبلغی که باید از بهای کالای هدف خارج شود تا سودش صفر شود
    DECLARE @Deficit FLOAT, @SourceProfit FLOAT;

    SELECT  @SourceProfit = Profit
    FROM    dbo.CC_ItemMargin
    WHERE   RunId = @RunId AND Code = @SourceCode;

    IF @SourceProfit IS NULL
    BEGIN
        RAISERROR(N'این کالا در سود و زیانِ این اجرا وجود ندارد (شاید فروشی نداشته).', 16, 1);
        RETURN;
    END

    SET @Deficit = CASE WHEN @SourceProfit < 0 THEN -@SourceProfit ELSE 0 END;

    ---- مقدار تولید هر فرمول در این ماه — عیناً منطق CC_sp_S09_ApplyDecisions
    IF OBJECT_ID('tempdb..#Prod') IS NOT NULL DROP TABLE #Prod;

    SELECT  TRY_CAST(pl.N_KOL AS INT) AS FNUMB,
            SUM(pl.MEGHK)             AS ProdQty
    INTO    #Prod
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.N_KOL AS INT) IS NOT NULL
    GROUP BY TRY_CAST(pl.N_KOL AS INT)
    HAVING  SUM(pl.MEGHK) > 0;

    ---- فرمول‌های فعالِ این ماه (فقط آن‌ها که سند تولید دارند)
    IF OBJECT_ID('tempdb..#F') IS NOT NULL DROP TABLE #F;

    SELECT  hm.FNUMB,
            TRY_CAST(hm.CODE AS BIGINT) AS ParentCode,
            p.ProdQty
    INTO    #F
    FROM    dbo.HEAD_MANF hm
    JOIN    #Prod p ON p.FNUMB = hm.FNUMB
    WHERE   hm.GHEYMAT = @Month;

    ---- ── گرافِ «چه چیزی چه چیزی را مصرف می‌کند» ──────────────────────
    -- یک بار ساخته می‌شود و هر دو پیمایش (بالادست برای نامزدها، پایین‌دست
    -- برای ظرفیتِ مقصدها) روی همین می‌نشینند. Qty در واحدِ اصلیِ خودِ ماده
    -- است، چون MEGHk همان واحد را دارد.
    IF OBJECT_ID('tempdb..#Use') IS NOT NULL DROP TABLE #Use;

    SELECT  f.ParentCode,
            TRY_CAST(d.CODE AS BIGINT) AS ChildCode,
            SUM(d.MEGHk * f.ProdQty)   AS Qty,
            MAX(ISNULL(d.SMABL, 0))    AS Rate
    INTO    #Use
    FROM    #F f
    JOIN    dbo.DTL_MANF d ON d.FNUMB = f.FNUMB
    WHERE   TRY_CAST(d.CODE AS BIGINT) IS NOT NULL
    GROUP BY f.ParentCode, TRY_CAST(d.CODE AS BIGINT);

    CREATE INDEX IX_Use_Parent ON #Use (ParentCode);
    CREATE INDEX IX_Use_Child  ON #Use (ChildCode);

    -- کل تولیدِ ماهِ هر کالای دارای فرمول (مخرجِ همه‌ی نسبت‌های رقت)
    IF OBJECT_ID('tempdb..#ProdByCode') IS NOT NULL DROP TABLE #ProdByCode;

    SELECT  ParentCode, SUM(ProdQty) AS ProdQty
    INTO    #ProdByCode
    FROM    #F
    GROUP BY ParentCode;

    CREATE INDEX IX_PBC ON #ProdByCode (ParentCode);

    ---- ── نامزدها: پیمایشِ بالادستِ درختِ کالای زیان‌ده ────────────────
    IF OBJECT_ID('tempdb..#Cand') IS NOT NULL DROP TABLE #Cand;

    WITH up AS (
        -- سطح ۱: مواد مستقیمِ فرمولِ کالای هدف، بدون رقت
        SELECT  u.ChildCode          AS MaterialCode,
                1                    AS Depth,
                u.Qty                AS Qty,
                u.Rate               AS Rate,
                CAST(1.0 AS FLOAT)   AS Dilution,
                CAST(NULL AS BIGINT) AS ViaCode
        FROM    #Use u
        WHERE   u.ParentCode = @SourceCode

        UNION ALL

        -- هر سطح پایین‌تر: رقتِ انباشته × سهمی از تولیدِ این نیمه‌ساخته که
        -- به مصرف‌کننده‌ی بالادستی‌اش می‌رسد.
        -- ⚠ سقفِ ۱: مصرف می‌تواند از تولیدِ همان ماه بیشتر باشد (برداشت از
        -- موجودی اول دوره) و کسر از ۱ رد کند؛ بیش از صد درصدِ اثر بی‌معناست.
        SELECT  u2.ChildCode,
                up.Depth + 1,
                u2.Qty,
                u2.Rate,
                up.Dilution * CASE WHEN up.Qty / p.ProdQty > 1 THEN 1
                                   ELSE up.Qty / p.ProdQty END,
                up.MaterialCode
        FROM    up
        JOIN    #ProdByCode p ON p.ParentCode = up.MaterialCode AND p.ProdQty > 0
        JOIN    #Use u2       ON u2.ParentCode = up.MaterialCode
        WHERE   up.Depth < @MaxDepth
          AND   up.Dilution > 0.0005          -- زیر این، اثر عملاً صفر است
          AND   u2.ChildCode <> @SourceCode   -- حلقه‌ی بدیهی به خودِ کالا
    )
    SELECT  MaterialCode,
            MIN(Depth)                    AS Depth,
            SUM(Qty)                      AS AvailableQty,
            MAX(Rate)                     AS Rate,
            SUM(Qty * Rate)               AS RemovableValue,
            SUM(Qty * Rate * Dilution)    AS EffectiveValue
    INTO    #Cand
    FROM    up
    WHERE   MaterialCode <> @SourceCode
    GROUP BY MaterialCode
    HAVING  SUM(Qty) > 0 AND MAX(Rate) > 0
    OPTION (MAXRECURSION 32);

    -- «از داخلِ …» برای نمایش: نیمه‌ساخته‌ی کم‌عمق‌ترین مسیر
    ALTER TABLE #Cand ADD ViaCode BIGINT NULL;

    WITH up2 AS (
        SELECT  u.ChildCode AS MaterialCode, 1 AS Depth, CAST(NULL AS BIGINT) AS ViaCode,
                CAST(1.0 AS FLOAT) AS Dilution, u.Qty AS Qty
        FROM    #Use u WHERE u.ParentCode = @SourceCode
        UNION ALL
        SELECT  u2.ChildCode, up2.Depth + 1, up2.MaterialCode,
                up2.Dilution * CASE WHEN up2.Qty / p.ProdQty > 1 THEN 1
                                    ELSE up2.Qty / p.ProdQty END,
                u2.Qty
        FROM    up2
        JOIN    #ProdByCode p ON p.ParentCode = up2.MaterialCode AND p.ProdQty > 0
        JOIN    #Use u2       ON u2.ParentCode = up2.MaterialCode
        WHERE   up2.Depth < @MaxDepth AND up2.Dilution > 0.0005
          AND   u2.ChildCode <> @SourceCode
    )
    UPDATE  c
       SET  c.ViaCode = v.ViaCode
    FROM    #Cand c
    CROSS   APPLY (SELECT TOP 1 ViaCode FROM up2
                   WHERE  up2.MaterialCode = c.MaterialCode
                   ORDER  BY Depth) v
    OPTION (MAXRECURSION 32);

    ---- ── مقصدهای بالقوه ───────────────────────────────────────────────
    -- هر کالایی که همین ماده را مصرف می‌کند و خودش کالای زیان‌ده نیست —
    -- چه فروش‌رفته باشد چه نیمه‌ساخته. کالاهایی که هدفِ فعالِ حاشیه سود
    -- دارند کنار می‌روند تا زنجیره‌ی تعدیل‌های تودرتو ساخته نشود.
    IF OBJECT_ID('tempdb..#DestRaw') IS NOT NULL DROP TABLE #DestRaw;

    SELECT  DISTINCT c.MaterialCode, u.ParentCode AS TargetCode
    INTO    #DestRaw
    FROM    #Cand c
    JOIN    #Use u ON u.ChildCode = c.MaterialCode
    WHERE   u.ParentCode <> @SourceCode
      AND   NOT EXISTS (SELECT 1 FROM dbo.CC_MarginTarget t
                        WHERE t.Code = u.ParentCode AND t.IsActive = 1);

    ---- ── جذبِ پایین‌دست: ΔV روی مقصد، چقدرش به هر کالای فروش‌رفته می‌رسد ──
    -- از هر مقصد رو به بالا در گراف حرکت می‌کنیم (مصرف‌کننده‌های مقصد،
    -- مصرف‌کننده‌های آن‌ها، …) و سهم را در هر گام ضرب می‌کنیم. خودِ مقصد با
    -- سهم ۱ در مجموعه هست، پس یک کالای فروش‌رفته‌ی ساده absorb=1 می‌گیرد و
    -- ظرفیتش دقیقاً «سودش» می‌شود — همان رفتار قبلی.
    IF OBJECT_ID('tempdb..#Node') IS NOT NULL DROP TABLE #Node;
    SELECT DISTINCT TargetCode AS Node INTO #Node FROM #DestRaw;

    IF OBJECT_ID('tempdb..#Down') IS NOT NULL DROP TABLE #Down;

    WITH dn AS (
        SELECT  n.Node, n.Node AS Descendant, CAST(1.0 AS FLOAT) AS Share, 0 AS Lvl
        FROM    #Node n
        UNION ALL
        SELECT  dn.Node, u.ParentCode,
                dn.Share * CASE WHEN u.Qty / p.ProdQty > 1 THEN 1
                                ELSE u.Qty / p.ProdQty END,
                dn.Lvl + 1
        FROM    dn
        JOIN    #ProdByCode p ON p.ParentCode = dn.Descendant AND p.ProdQty > 0
        JOIN    #Use u        ON u.ChildCode = dn.Descendant
        WHERE   dn.Lvl < 8 AND dn.Share > 0.0005
    )
    SELECT  Node, Descendant, SUM(Share) AS Share
    INTO    #Down
    FROM    dn
    GROUP BY Node, Descendant
    OPTION (MAXRECURSION 64);

    CREATE INDEX IX_Down ON #Down (Node);

    -- جذبِ نهایی به تفکیک کالای فروش‌رفته. عاملِ فروش/تولید: بهایی که وارد
    -- کالایی می‌شود فقط به‌نسبتِ مقدارِ فروش‌رفته‌اش در سود ماه اثر دارد؛
    -- بقیه در موجودی می‌نشیند. بدون سندِ تولید (تولید ماه‌های قبل) عامل ۱
    -- گرفته می‌شود تا مقصد بی‌دلیل حذف نشود.
    IF OBJECT_ID('tempdb..#Absorb') IS NOT NULL DROP TABLE #Absorb;

    SELECT  d.Node,
            d.Descendant                AS Code,
            m.Profit,
            d.Share * CASE WHEN p.ProdQty > 0 AND m.QtySold / p.ProdQty < 1
                           THEN m.QtySold / p.ProdQty ELSE 1 END AS Absorb
    INTO    #Absorb
    FROM    #Down d
    JOIN    dbo.CC_ItemMargin m ON m.RunId = @RunId AND m.Code = d.Descendant
    LEFT    JOIN #ProdByCode p  ON p.ParentCode = d.Descendant
    WHERE   m.QtySold <> 0;

    ---- ── ظرفیتِ خالصِ هر مقصد ───────────────────────────────────────────
    IF OBJECT_ID('tempdb..#Dest') IS NOT NULL DROP TABLE #Dest;

    SELECT  r.MaterialCode,
            r.TargetCode,
            agg.GrossCapacity,
            agg.BouncePct,
            agg.LoserCount,
            -- تسکینِ خالصی که این مقصد می‌تواند بدهد
            agg.GrossCapacity * (1.0 - agg.BouncePct / 100.0) AS Capacity,
            CASE WHEN sm.Code IS NULL THEN 1 ELSE 0 END AS IsSemi
    INTO    #Dest
    FROM    #DestRaw r
    LEFT    JOIN dbo.CC_ItemMargin sm
            ON sm.RunId = @RunId AND sm.Code = r.TargetCode AND sm.QtySold <> 0
    -- سه زیرپرس‌وجوی جدا و نه یک CROSS APPLY با چند تجمیع: شکل دوم برای
    -- هر سطرِ بی‌ربط یک NULL می‌سازد و «Null value is eliminated by an
    -- aggregate» در لاگ می‌نشیند — بی‌ضرر ولی گمراه‌کننده.
    CROSS   APPLY (
        SELECT
            -- سقف: تنگ‌ترین مصرف‌کننده‌ی سودده. کالای زیان‌ده‌ی هدف در این
            -- MIN نمی‌آید؛ اثرش جداگانه به‌صورت Bounce حساب می‌شود.
            (SELECT MIN(a.Profit / a.Absorb) FROM #Absorb a
             WHERE  a.Node = r.TargetCode AND a.Code <> @SourceCode
               AND  a.Profit > 0 AND a.Absorb > 0)                       AS GrossCapacity,
            ISNULL((SELECT MAX(a.Absorb) FROM #Absorb a
                    WHERE a.Node = r.TargetCode AND a.Code = @SourceCode), 0)
                                                             * 100.0     AS BouncePct,
            (SELECT COUNT(*) FROM #Absorb a
             WHERE  a.Node = r.TargetCode AND a.Code <> @SourceCode
               AND  a.Profit <= 0 AND a.Absorb > 0)                      AS LoserCount
    ) agg
    WHERE   agg.GrossCapacity > 0
      -- مقصدی که تقریباً همه‌ی بار را به خودِ کالای زیان‌ده برمی‌گرداند
      -- بی‌فایده است. نمونه‌ی واقعی: کالای ۳۳۶۵ و مقصدِ ۱۷۳۲.
      AND   agg.BouncePct < 98;

    ---- ── جمع‌بندی و رتبه‌بندی ───────────────────────────────────────
    SELECT  c.MaterialCode,
            s.NAME                                   AS MaterialName,
            c.Depth,
            c.ViaCode,
            sv.NAME                                  AS ViaName,
            c.AvailableQty,
            c.Rate,
            c.RemovableValue,
            CASE WHEN c.RemovableValue > 0
                 THEN c.EffectiveValue / c.RemovableValue * 100.0
                 ELSE 0 END                          AS DilutionPct,
            c.EffectiveValue,
            ISNULL(dd.DestCount, 0)                  AS DestCount,
            ISNULL(dd.DestCapacity, 0)               AS DestCapacity,
            @Deficit                                 AS Deficit,
            -- چقدر از کسری با این ماده واقعاً پوشش داده می‌شود
            CASE WHEN ISNULL(dd.DestCount, 0) = 0 THEN 0
                 ELSE (SELECT MIN(v) FROM (VALUES
                          (@Deficit),
                          (c.EffectiveValue),
                          (ISNULL(dd.DestCapacity, 0))) AS x(v))
            END                                      AS Coverage,
            CASE WHEN pref.TargetCode IS NOT NULL THEN 1 ELSE 0 END AS IsRemembered,
            pref.TargetCode                          AS RememberedTarget
    FROM    #Cand c
    LEFT    JOIN (SELECT MaterialCode, COUNT(*) AS DestCount,
                         SUM(Capacity) AS DestCapacity
                  FROM   #Dest GROUP BY MaterialCode) dd
            ON dd.MaterialCode = c.MaterialCode
    LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = c.MaterialCode
    LEFT    JOIN dbo.STUF_DEF sv ON TRY_CAST(sv.CODE AS BIGINT) = c.ViaCode
    LEFT    JOIN dbo.CC_RebalancePref pref
            ON pref.SourceCode = @SourceCode
           AND pref.MaterialCode = c.MaterialCode
           AND pref.IsActive = 1
    ORDER BY
            -- ۱) انتخابِ به‌خاطرسپرده‌ی کاربر همیشه اول
            CASE WHEN pref.TargetCode IS NOT NULL THEN 0 ELSE 1 END,
            -- ۲) موادی که کسری را کامل می‌پوشانند
            CASE WHEN ISNULL(dd.DestCount,0) > 0
                  AND c.EffectiveValue >= @Deficit
                  AND ISNULL(dd.DestCapacity,0) >= @Deficit THEN 0 ELSE 1 END,
            -- ۳) کمترین تعداد مقصد
            ISNULL(dd.DestCount, 0),
            -- ۴) بیشترین پوشش
            c.EffectiveValue DESC;

    ---- مقصدهای هر ماده — برای نمایش در دیالوگ انتخاب
    SELECT  d.MaterialCode,
            d.TargetCode,
            st.NAME      AS TargetName,
            d.Capacity,
            d.GrossCapacity,
            d.BouncePct,
            d.LoserCount,
            d.IsSemi,
            CASE WHEN pref.TargetCode IS NOT NULL THEN 1 ELSE 0 END AS IsRemembered
    FROM    #Dest d
    LEFT    JOIN dbo.STUF_DEF st ON TRY_CAST(st.CODE AS BIGINT) = d.TargetCode
    LEFT    JOIN dbo.CC_RebalancePref pref
            ON pref.SourceCode = @SourceCode
           AND pref.MaterialCode = d.MaterialCode
           AND pref.TargetCode = d.TargetCode
           AND pref.IsActive = 1
    -- بدونِ زیان‌دهِ پایین‌دست اول، بعد بیشترین ظرفیتِ خالص
    ORDER BY d.MaterialCode, d.LoserCount, d.Capacity DESC;
END
GO

PRINT N'رويه CC_sp_RebalanceSuggest (عمق آزاد + مقصد نيمه‌ساخته) به‌روز شد.';
GO
";
            TryExecuteCostCloseBatch(db, rebalanceSuggest,
                "CC_RebalancePref و CC_sp_RebalanceSuggest (عمق آزاد + مقصد نیمه‌ساخته)",
                "اسکریپت 25-rebalance-suggest.sql را اجرا کنید (به CC_ItemMargin و CC_MarginTarget نیاز دارد).");

            // --- 26-margin-by-unit.sql ---
            string marginByUnit = @"
/* ═══════════════════════════════════════════════════════════════════
   سود و زیان کالا به تفکیک واحد تولید

   ── چرا جدول جدا و نه تغییر CC_ItemMargin ──
   گرین (grain) جدول CC_ItemMargin «یک سطر به‌ازای هر کالا در هر اجرا»
   است و کلی کد به همین شکل تکیه دارد: S12b، تابلوی سود و زیان،
   CC_MarginTarget، CHK-14، و گزارش هیئت‌مدیره. اگر گرین را به
   (کالا × واحد) تغییر بدهیم همه‌ی آن‌ها بی‌صدا دو‌برابر می‌شمارند.
   پس گزارش تفکیکی در جدول خودش می‌نشیند و گزارش فعلی دست‌نخورده
   می‌ماند — «علاوه بر»، نه «به‌جای».

   ── دیمنشن واحد از کجا می‌آید ──
   KALAS.DEPATMAN وسوسه‌انگیز است ولی *کد بخش فروش* است نه واحد تولید
   (روی داده‌ی واقعی مقادیری مثل ۲۰، ۲۱، ۸۰۲۰۳۰۹ دارد، درحالی‌که
   CC_Unit.Depatman فقط ۱ و ۲ است). دیمنشن درست KALAS.ANBARCODE است که
   از CC_UnitAnbar به واحد نگاشت می‌شود:

       واحد ۱ (کارخانه یزدسپار) → انبارهای ۷,۸,۱,۲,۳,۱۰,۱۴,۱۵
       واحد ۲ (یزد)             → انبارهای ۸۱۰,۸۱۱,۸۰۷,۸۰۸

   فروشی که از انباری بیاید که به هیچ واحدی نگاشت ندارد با UnitId=NULL
   ثبت می‌شود تا بی‌صدا گم نشود — جمعِ تفکیکی باید با جمع کل بخواند.

   ── هم‌خوانی با گزارش کل ──
   منطق محاسبه عیناً همان CC_sp_S12_CalcMargin است (فروش TAGCODE=2،
   برگشت TAGCODE=4، بها از MABRIAL کاردکس)، فقط با یک کلید گروه‌بندی
   بیشتر. پس جمعِ سطرهای هر کالا روی همه‌ی واحدها باید با سطر همان کالا
   در CC_ItemMargin برابر باشد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر نصب
   فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

IF OBJECT_ID('dbo.CC_ItemMarginUnit','U') IS NULL
CREATE TABLE dbo.CC_ItemMarginUnit (
    RunId        INT    NOT NULL,
    UnitId       INT    NULL,          -- NULL = انبارِ بدون نگاشت واحد
    Code         BIGINT NOT NULL,
    QtySold      FLOAT  NULL,
    WeightKg     FLOAT  NULL,
    SalesAmount  FLOAT  NULL,
    CostAmount   FLOAT  NULL,
    UnitCost     FLOAT  NULL,
    UnitPrice    FLOAT  NULL,
    GrossSales   FLOAT  NULL,
    Discount     FLOAT  NULL,
    ReturnAmount FLOAT  NULL,
    ReturnQty    FLOAT  NULL,
    Profit AS (ISNULL(SalesAmount,0) - ISNULL(CostAmount,0)) PERSISTED
);
GO

-- ⚠ کلید یکتا، نه PRIMARY KEY: ستون UnitId عمداً NULL می‌پذیرد (فروش از
-- انباری که به هیچ واحدی نگاشت ندارد) و SQL Server ستون NULLable را در
-- PRIMARY KEY قبول نمی‌کند. در UNIQUE INDEX، مقادیر NULL با هم برابر
-- شمرده می‌شوند — که دقیقاً همان چیزی است که می‌خواهیم: به‌ازای هر
-- (اجرا، کالا) حداکثر یک سطرِ «بدون واحد».
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_CC_ItemMarginUnit'
                 AND object_id = OBJECT_ID('dbo.CC_ItemMarginUnit'))
    CREATE UNIQUE INDEX UX_CC_ItemMarginUnit
        ON dbo.CC_ItemMarginUnit (RunId, Code, UnitId);
GO


CREATE OR ALTER PROCEDURE dbo.CC_sp_S12u_MarginByUnit
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE dbo.CC_ItemMarginUnit WHERE RunId = @RunId;

    /* نگاشت انبار → واحد. یک انبار نباید به دو واحد بخورد؛ اگر خورد،
       کوچک‌ترین UnitId برداشته می‌شود تا سطر دوباره‌شماری نشود. */
    ;WITH AnbarUnit AS (
        SELECT Anbar, MIN(UnitId) AS UnitId
        FROM   dbo.CC_UnitAnbar
        GROUP BY Anbar
    ),
    /* ─── فروش: TAGCODE = 2 ─── */
    Forush AS (
        SELECT  k.CODE                       AS Code,
                au.UnitId                    AS UnitId,
                SUM(k.MEGHk)                 AS Qty,
                SUM(k.MEGH)                  AS Weight,
                SUM(k.MABL_K)                AS Gross,
                SUM(ISNULL(k.N_MOIN, 0))     AS Discount,
                SUM(k.KHFR)                  AS NetSales,
                SUM(k.MABRIAL)               AS CostRial
        FROM    dbo.KALAS k
        LEFT    JOIN AnbarUnit au ON au.Anbar = k.ANBARCODE
        WHERE   k.TAGCODE = 2
          AND   k.MM = @Month
        GROUP BY k.CODE, au.UnitId
    ),
    /* ─── برگشت از فروش: TAGCODE = 4 ─── */
    Bargasht AS (
        SELECT  k.CODE           AS Code,
                au.UnitId        AS UnitId,
                SUM(k.MEGHk)     AS Qty,
                SUM(k.KHFR)      AS NetAmount,
                SUM(k.MABRIAL)   AS CostRial
        FROM    dbo.KALAS k
        LEFT    JOIN AnbarUnit au ON au.Anbar = k.ANBARCODE
        WHERE   k.TAGCODE = 4
          AND   k.MM = @Month
        GROUP BY k.CODE, au.UnitId
    )
    INSERT dbo.CC_ItemMarginUnit
        (RunId, UnitId, Code, QtySold, WeightKg, SalesAmount, CostAmount,
         UnitCost, UnitPrice, GrossSales, Discount, ReturnAmount, ReturnQty)
    SELECT  @RunId,
            f.UnitId,
            f.Code,
            f.Qty      - ISNULL(b.Qty, 0),
            f.Weight,
            f.NetSales - ISNULL(b.NetAmount, 0),
            f.CostRial - ISNULL(b.CostRial, 0),
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.CostRial - ISNULL(b.CostRial,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            CASE WHEN f.Qty - ISNULL(b.Qty,0) <> 0
                 THEN (f.NetSales - ISNULL(b.NetAmount,0))
                      / (f.Qty - ISNULL(b.Qty,0)) END,
            f.Gross,
            f.Discount,
            ISNULL(b.NetAmount, 0),
            ISNULL(b.Qty, 0)
    FROM    Forush f
    LEFT    JOIN Bargasht b ON b.Code = f.Code
                           AND ISNULL(b.UnitId, -1) = ISNULL(f.UnitId, -1)
    WHERE   f.Qty <> 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    SELECT  @RunId, 'S12', 1,
            CONCAT(N'سود کالا به تفکیک واحد: ', COUNT(DISTINCT ISNULL(UnitId,-1)),
                   N' واحد، ', COUNT(*), N' سطر'),
            (SELECT ISNULL(u.UnitId,-1) AS unitId,
                    MAX(ISNULL(cu.UnitName, N'بدون واحد')) AS unitName,
                    COUNT(*) AS items,
                    SUM(CASE WHEN u.Profit < 0 THEN 1 ELSE 0 END) AS lossItems,
                    SUM(u.SalesAmount) AS sales,
                    SUM(u.CostAmount)  AS cost,
                    SUM(u.Profit)      AS profit
             FROM   dbo.CC_ItemMarginUnit u
             LEFT   JOIN dbo.CC_Unit cu ON cu.UnitId = u.UnitId
             WHERE  u.RunId = @RunId
             GROUP  BY ISNULL(u.UnitId,-1)
             FOR JSON PATH)
    FROM    dbo.CC_ItemMarginUnit WHERE RunId = @RunId;
END
GO

PRINT N'جدول CC_ItemMarginUnit و رويه CC_sp_S12u_MarginByUnit ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, marginByUnit,
                "CC_ItemMarginUnit و CC_sp_S12u_MarginByUnit",
                "اسکریپت 26-margin-by-unit.sql را اجرا کنید (به KALAS.ANBARCODE و CC_UnitAnbar نیاز دارد).");

            // --- 27-formula-copy.sql ---
            string formulaCopy = @"
/* ═══════════════════════════════════════════════════════════════════
   CHK-04 — وقتی کالا برای ماهِ جاری هیچ فرمولی ندارد

   ── مسئله ──
   اصلاح خودکارِ موجود (CC_sp_Fix_MissingFormula) فقط وقتی کار می‌کند که
   فرمولی با GHEYMAT برابرِ ماهِ جاری از قبل وجود داشته باشد؛ کارش صرفاً
   نسبت‌دادنِ آن به برگه‌های تولید است. اگر چنین فرمولی نباشد،
   CanAutoFix=0 می‌شود و کاربر هیچ راهی ندارد.

   نمونه‌ی واقعی (تیر ۱۴۰۵): کد ۲۸۱۲ «پنیر پیتزا پامپارو ۱۸۰ گرمی
   شادنوش» یازده فرمول دارد — ماه‌های ۰، ۲، ۳، ۵، ۷ تا ۱۲ — ولی برای
   ماه ۴ هیچ‌کدام.

   ── چرا «نسبت دادنِ فرمولِ ماه دیگر» جواب نمی‌دهد ──
   خودِ شرطِ CHK-04 این است که برگه به فرمولی با GHEYMAT = ماهِ جاری
   اشاره کند. اگر برگه را به فرمولِ ماه ۳ وصل کنیم، کنترل همچنان مغایرت
   نشان می‌دهد و S11 هم نرخِ ماه را روی آن منتشر نمی‌کند. پس فرمول باید
   به ماهِ جاری **کپی** شود، نه فقط اشاره داده شود.

   ── قاعده‌ی صاحب پروژه ──
   «فرمول را از ماه قبل بگیرد؛ اگر ماه قبل نداشت، لیست فرمول‌های آن کالا
   بدون توجه به ماه را بیاورد و کاربر خودش انتخاب کند.»

   پس CC_sp_FormulaOptions همه‌ی فرمول‌های کالا را با رتبه‌ی پیشنهاد
   برمی‌گرداند (ماه قبل اول)، و انتخاب نهایی با کاربر است.

   ── نکته‌ی مهم درباره‌ی GHEYMAT ──
   GHEYMAT فقط شماره‌ی ماه است (۱ تا ۱۲)، بدون سال — فرمول‌ها قالبِ
   ماهانه‌اند و بین سال‌ها دوباره استفاده می‌شوند. روی داده‌ی واقعی،
   فرمولِ «ماه ۳»ِ کد ۲۸۱۲ تاریخ فعال‌سازی ۱۴۰۴/۰۳/۲۰ دارد و برای خرداد
   ۱۴۰۵ هم همان به کار می‌رود. پس «ماه قبل» یعنی GHEYMAT = @Month - 1،
   و برای فروردین یعنی ۱۲ (اسفند).

   ── دستمزد و سربار ──
   IMBIBE_MANF/IMBIBE_SAR عیناً از فرمولِ مبدأ کپی می‌شوند و دست‌کاری
   نمی‌شوند؛ S07B بعداً خودش نرخِ واقعیِ ماه را رویشان می‌نشاند.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────────────────────────────────────────────────────────────────
   فهرست فرمول‌های یک کالا، برای انتخاب کاربر
   ─────────────────────────────────────────────────────────────────── */
CREATE OR ALTER PROCEDURE dbo.CC_sp_FormulaOptions
    @Code  BIGINT,
    @Month TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    -- ماهِ قبل، با چرخشِ سال: قبلِ فروردین، اسفند است.
    DECLARE @Prev TINYINT = CASE WHEN @Month <= 1 THEN 12 ELSE @Month - 1 END;

    SELECT  hm.FNUMB                                   AS Fnumb,
            CAST(hm.GHEYMAT AS INT)                    AS Mah,
            hm.DATE_ACTIV                              AS DateActiv,
            hm.IMBIBE_MANF                             AS Wage,
            hm.IMBIBE_SAR                              AS Overhead,
            (SELECT COUNT(*) FROM dbo.DTL_MANF d WHERE d.FNUMB = hm.FNUMB) AS LineCount,
            CASE WHEN CAST(hm.GHEYMAT AS INT) = @Prev THEN 1 ELSE 0 END    AS IsPrevMonth,
            -- آیا این فرمول در ماهِ خودش واقعاً استفاده شده؟ فرمولی که
            -- هیچ‌وقت تولیدی نداشته احتمالاً متروک است و نباید اول
            -- پیشنهاد شود.
            CASE WHEN EXISTS (SELECT 1 FROM dbo.INVO_LST pl
                              WHERE pl.TAG = 9 AND TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB)
                 THEN 1 ELSE 0 END                     AS EverUsed
    FROM    dbo.HEAD_MANF hm
    WHERE   TRY_CAST(hm.CODE AS BIGINT) = @Code
      -- فرمولی که از قبل مالِ همین ماه است اینجا بی‌معناست: در آن حالت
      -- اصلاً کپی لازم نیست و CC_sp_Fix_MissingFormula کار می‌کند.
      AND   CAST(hm.GHEYMAT AS INT) <> @Month
    ORDER BY
            -- ۱) ماه قبل، همان چیزی که صاحب پروژه پیش‌فرض خواست
            CASE WHEN CAST(hm.GHEYMAT AS INT) = @Prev THEN 0 ELSE 1 END,
            -- ۲) فرمولی که واقعاً استفاده شده
            CASE WHEN EXISTS (SELECT 1 FROM dbo.INVO_LST pl
                              WHERE pl.TAG = 9 AND TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB)
                 THEN 0 ELSE 1 END,
            -- ۳) تازه‌ترین
            hm.DATE_ACTIV DESC, hm.FNUMB DESC;
END
GO


/* ───────────────────────────────────────────────────────────────────
   کپیِ یک فرمول به ماهِ جاری و نسبت‌دادنش به برگه‌های تولید
   ─────────────────────────────────────────────────────────────────── */
CREATE OR ALTER PROCEDURE dbo.CC_sp_Fix_CopyFormulaToMonth
    @Code         BIGINT,
    @Month        TINYINT,
    @SourceFnumb  INT,
    @DT1          BIGINT,
    @DT2          BIGINT,
    @RunId        INT          = NULL,
    @ExceptionId  BIGINT       = NULL,
    @UserName     NVARCHAR(50) = N'system',
    @WhatIf       BIT          = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- اعتبارسنجی
    IF NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF
                   WHERE FNUMB = @SourceFnumb AND TRY_CAST(CODE AS BIGINT) = @Code)
    BEGIN
        RAISERROR(N'فرمول انتخاب‌شده متعلق به این کالا نیست.', 16, 1);
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.HEAD_MANF
               WHERE TRY_CAST(CODE AS BIGINT) = @Code AND CAST(GHEYMAT AS INT) = @Month)
    BEGIN
        RAISERROR(N'این کالا برای این ماه از قبل فرمول دارد؛ از «اصلاح خودکار» استفاده کنید، نه کپی.', 16, 1);
        RETURN;
    END

    ---- برگه‌های تولیدی که باید به فرمول تازه وصل شوند
    IF OBJECT_ID('tempdb..#Rows') IS NOT NULL DROP TABLE #Rows;

    -- کلید تطبیق id است نه (NUMBER, RADIF) — به همان دلیلی که در
    -- CC_sp_Fix_MissingFormula مستند شده: RADIF می‌تواند NULL باشد.
    SELECT  pl.id      AS InvoId,
            h.NUMBER   AS ProdNo,
            h.DATE_N   AS ProdDate,
            pl.N_KOL   AS OldFnumb,
            pl.MEGHK   AS Meghdar
    INTO    #Rows
    FROM    dbo.HEAD_LST h
    JOIN    dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    WHERE   h.TAG = 9
      AND   h.DATE_N BETWEEN @DT1 AND @DT2
      AND   TRY_CAST(pl.CODE AS BIGINT) = @Code
      AND   NOT EXISTS (SELECT 1 FROM dbo.HEAD_MANF hm
                        WHERE hm.FNUMB = TRY_CAST(pl.N_KOL AS INT)
                          AND CAST(hm.GHEYMAT AS INT) = @Month);

    DECLARE @n INT = (SELECT COUNT(*) FROM #Rows);

    IF @WhatIf = 1
    BEGIN
        SELECT  r.ProdNo   AS شماره_برگه,
                r.ProdDate AS تاریخ,
                @Code      AS کد_کالا,
                r.OldFnumb AS فرمول_فعلی,
                r.Meghdar  AS مقدار
        FROM    #Rows r
        ORDER BY r.ProdNo;

        -- نامِ ستون‌ها عمداً بدون «أ»: کاراکترهای همزه‌دار در identifierهای
        -- SQL وقتی فایل بدون codepage درست خوانده شود خطای نحوی می‌سازند.
        SELECT  @n           AS تعداد_سطر_قابل_اصلاح,
                @SourceFnumb AS فرمول_مبدا,
                (SELECT CAST(GHEYMAT AS INT) FROM dbo.HEAD_MANF WHERE FNUMB = @SourceFnumb)
                             AS ماه_مبدا,
                (SELECT COUNT(*) FROM dbo.DTL_MANF WHERE FNUMB = @SourceFnumb)
                             AS تعداد_ردیف_فرمول,
                N'حالت گزارش — چیزی تغییر نکرد' AS وضعیت;
        RETURN;
    END

    BEGIN TRAN;

    ---- شماره‌ی فرمول تازه.
    -- FNUMB کلید اصلیِ غیرهویتی است و نرم‌افزار قدیمی هم با MAX+1 جلو
    -- می‌رود. UPDLOCK/HOLDLOCK جلوی گرفتنِ شماره‌ی تکراری توسط دو کاربر
    -- هم‌زمان را می‌گیرد.
    DECLARE @NewFnumb INT;
    SELECT  @NewFnumb = ISNULL(MAX(FNUMB), 0) + 1
    FROM    dbo.HEAD_MANF WITH (UPDLOCK, HOLDLOCK);

    ---- سربرگ فرمول
    -- DATE_ACTIV روی اولین روزِ همین دوره می‌نشیند تا فرمول از ابتدای ماه
    -- معتبر باشد؛ اگر تاریخِ مبدأ کپی شود، فرمول «از آینده» یا «از سالِ
    -- قبل» به نظر می‌رسد و گزارش‌های تاریخی را گمراه می‌کند.
    INSERT dbo.HEAD_MANF
        (FNUMB, CODE, DATE_ACTIV, IMBIBE_MANF, IMBIBE_SAR, GHEYMAT,
         NAMES, N_KOL, NUMBER, TNUMBER, SA_HOUR, SA_NHOU, TOZIH, CRT, UID)
    SELECT  @NewFnumb, hm.CODE, @DT1, hm.IMBIBE_MANF, hm.IMBIBE_SAR, @Month,
            hm.NAMES, hm.N_KOL, hm.NUMBER, hm.TNUMBER, hm.SA_HOUR, hm.SA_NHOU,
            LEFT(ISNULL(hm.TOZIH, N'') +
                 N' [کپی از فرمول ' + CAST(@SourceFnumb AS NVARCHAR(20)) +
                 N' ماه ' + CAST(CAST(hm.GHEYMAT AS INT) AS NVARCHAR(2)) + N']', 500),
            GETDATE(), NULL
    FROM    dbo.HEAD_MANF hm
    WHERE   hm.FNUMB = @SourceFnumb;

    ---- ردیف‌های فرمول
    INSERT dbo.DTL_MANF
        (FNUMB, CODE, ANBAR, VAHED_K, MEGH, MEGHk, PERT, SMABL, MABLK, TOZIH, CRT, UID)
    SELECT  @NewFnumb, d.CODE, d.ANBAR, d.VAHED_K, d.MEGH, d.MEGHk, d.PERT,
            d.SMABL, d.MABLK, d.TOZIH, GETDATE(), NULL
    FROM    dbo.DTL_MANF d
    WHERE   d.FNUMB = @SourceFnumb;

    ---- وصل کردن برگه‌های تولید به فرمول تازه
    DECLARE @applied INT = 0;

    UPDATE  pl
       SET  pl.N_KOL = @NewFnumb
    FROM    dbo.INVO_LST pl
    JOIN    #Rows r ON r.InvoId = pl.id;

    SET @applied = @@ROWCOUNT;

    ---- بستن استثنا
    UPDATE  e
       SET  e.IsResolved = 1, e.ResolvedBy = @UserName, e.ResolvedAtUtc = SYSUTCDATETIME(),
            e.ResolutionNote = CONCAT(N'فرمول ', @SourceFnumb, N' به ماه ', @Month,
                                      N' کپی شد (فرمول تازه ', @NewFnumb, N') و ',
                                      @applied, N' برگه به آن وصل شد.')
    FROM    dbo.CC_Exception e
    WHERE   e.RuleCode = 'CHK-04'
      AND   e.Code = @Code
      AND   ISNULL(e.RunId, -1) = ISNULL(@RunId, -1)
      AND   (@ExceptionId IS NULL OR e.ExceptionId = @ExceptionId);

    ---- خروج مواد باید بازسازی شود، چون فرمولِ برگه عوض شد
    IF @RunId IS NOT NULL AND @applied > 0
        UPDATE dbo.CC_Run SET FormulasDirty = 1 WHERE RunId = @RunId;

    COMMIT;

    SELECT  @applied  AS تعداد_سطر_اصلاح_شده,
            @NewFnumb AS فرمول_جدید,
            @n        AS تعداد_سطر_نامزد;
END
GO

PRINT N'رويه‌هاي CC_sp_FormulaOptions و CC_sp_Fix_CopyFormulaToMonth ايجاد شدند.';
GO
";
            TryExecuteCostCloseBatch(db, formulaCopy,
                "CC_sp_FormulaOptions و CC_sp_Fix_CopyFormulaToMonth",
                "اسکریپت 27-formula-copy.sql را اجرا کنید (به HEAD_MANF/DTL_MANF و CC_Exception نیاز دارد).");

            // --- 28-financial-statements.sql ---
            string financialStatements = @"
/* ═══════════════════════════════════════════════════════════════════
   صورت‌های مالیِ بهای تمام‌شده

   سه صورتِ استاندارد، همه از داده‌ی همین اجرا:
     ۱) صورت بهای تمام‌شده کالای ساخته‌شده  (COGM)
     ۲) صورت بهای تمام‌شده کالای فروش‌رفته   (COGS)
     ۳) صورت سود و زیان ناخالص

   ── منبع هر رقم ──
   موجودی اول/پایان دوره : کاردکس، «مانده‌مقدار × آخرین نرخ میانگین» —
                           همان روشی که dbo.MOGHA_ANBAR و دروازه‌ی S05
                           استفاده می‌کنند، نه جمعِ خامِ MABL_K (توضیحش
                           در 14-s05-gate.sql آمده: S07A فقط AVRAGE را
                           به‌روز می‌کند نه MABL_K).
   خرید دوره             : INVO_LST TAG=1 به انبارهای مواد
   دستمزد و سربار        : CC_ConversionCost.ActualAmount — یعنی رقمِ
                           واقعیِ حسابداری، نه جذب‌شده
   فروش و بهای فروش‌رفته  : CC_ItemMargin (خروجی S12)

   ── تله‌ای که این صورت عمداً پنهانش نمی‌کند ──
   انبارها نقش دارند (CC_UnitAnbar.AnbarRole): ۱=مواد مصرفی تولید،
   ۲=مواد اولیه، ۳=محصول، ۴=سایر. روی داده‌ی واقعی (خرداد ۱۴۰۵) دیده شد
   که ۱٬۰۸۱ میلیارد ریال از انبارِ *محصول* به انبارِ مصرفی برمی‌گردد —
   یعنی نیمه‌ساخته‌ای که دوباره وارد تولید می‌شود.

   اگر آن مبلغ ساده در «مواد مستقیم» بنشیند، دستمزد و سرباری که در
   مرحله‌ی قبل داخلش رفته دوباره شمرده می‌شود و صورت متورم می‌شود. پس
   به‌عنوان یک سطرِ اطلاعیِ جدا گزارش می‌گردد، نه قاطیِ مواد.

   ── خط تطبیق ──
   آخرین سطرِ صورت دوم، COGS محاسبه‌شده از گردشِ انبار را با جمعِ
   CC_ItemMargin.CostAmount می‌سنجد. این دو باید بخوانند؛ اختلافشان
   یعنی جایی از زنجیره‌ی انبار→بها ناسازگار است. عمداً «تطبیق» است نه
   «تصحیح»: عدد را دستکاری نمی‌کنیم، اختلاف را نشان می‌دهیم.

   نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────── سرفصل‌های هزینه‌های دوره ─────────
   دستمزد و سربار در CC_UnitAcc تعریف می‌شوند چون به «واحد تولیدی»
   می‌چسبند. هزینه‌های فروش و اداری این‌طور نیستند — هزینه‌ی دوره‌اند و
   به کل شرکت تعلق دارند، پس جدول خودشان را دارند.

   الگوی سطح حساب عیناً همان CC_UnitAcc است: معین/تفصیلی خالی یعنی
   «همه‌ی زیرمجموعه‌های سطح بالاتر»، و Ratio برای وقتی است که فقط سهمی
   از یک حساب به این طبقه تعلق دارد. */
IF OBJECT_ID('dbo.CC_ExpenseAcc','U') IS NULL
CREATE TABLE dbo.CC_ExpenseAcc (
    Id          INT           IDENTITY(1,1) PRIMARY KEY,
    ExpenseKind TINYINT       NOT NULL,   -- 1=فروش 2=اداری 3=مالی 4=ساير
    HesKol      INT           NOT NULL,
    HesMoin     INT           NULL,       -- خالی = همه معین‌های این کل
    HesTafsili  INT           NULL,       -- خالی = همه تفصیلی‌های همان معین
    Ratio       DECIMAL(9,6)  NOT NULL DEFAULT 1,
    IsActive    BIT           NOT NULL DEFAULT 1,
    Note        NVARCHAR(200) NULL,
    CONSTRAINT UQ_CC_ExpenseAcc UNIQUE (ExpenseKind, HesKol, HesMoin, HesTafsili)
);
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_FinancialStatements
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Month TINYINT, @DT1 BIGINT, @DT2 BIGINT, @Year SMALLINT;
    SELECT  @Month = PeriodMonth, @DT1 = DateFrom, @DT2 = DateTo, @Year = FiscalYear
    FROM    dbo.CC_Run WHERE RunId = @RunId;

    IF @DT1 IS NULL
    BEGIN
        RAISERROR(N'اجرا پیدا نشد.', 16, 1);
        RETURN;
    END

    ---- ارزش موجودی هر (انبار،کالا) در یک تاریخ ─────────────────────
    -- دو بار لازم است (ابتدا و پایان دوره)، پس یک جدول موقت با ستون
    -- Cut نگه می‌داریم به‌جای دو بلوک تکراری.
    IF OBJECT_ID('tempdb..#Inv') IS NOT NULL DROP TABLE #Inv;
    CREATE TABLE #Inv (Cut BIGINT, Anbar INT, Code BIGINT, Qty FLOAT, Rate FLOAT);

    DECLARE @Open BIGINT = @DT1 - 1;   -- تاریخ‌ها عدد YYYYMMDD اند؛ روزِ صفر
                                       -- وجود ندارد پس این همیشه بین دو ماه می‌افتد
    DECLARE @cut BIGINT, @i INT = 0;

    WHILE @i < 2
    BEGIN
        SET @cut = CASE WHEN @i = 0 THEN @Open ELSE @DT2 END;

        ;WITH Mv AS (
            -- ورودها
            SELECT il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS Code, il.MEGHk AS M
            FROM   dbo.INVO_LST il JOIN dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
            WHERE  il.TAG IN (1,7,9,24) AND h.DATE_N <= @cut
            UNION ALL
            SELECT CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.MEGHk
            FROM   dbo.INVO_LST il JOIN dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
            WHERE  il.TAG=5 AND il.ANBARF IS NOT NULL AND h.DATE_N <= @cut
            UNION ALL
            -- خروج‌ها
            SELECT il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MEGHk
            FROM   dbo.INVO_LST il JOIN dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
            WHERE  il.TAG IN (2,5,8,10,11,26) AND h.DATE_N <= @cut
            UNION ALL
            -- موجودی اول سال
            SELECT f.ANBAR, TRY_CAST(f.CODE AS BIGINT), f.MOGODI_A FROM dbo.STUF_FSK f
        ),
        Q AS (
            SELECT Anbar, Code, SUM(M) AS Qty FROM Mv
            WHERE Anbar IS NOT NULL AND Code IS NOT NULL
            GROUP BY Anbar, Code
        ),
        R AS (
            -- آخرین نرخ میانگینِ ثبت‌شده تا این تاریخ؛ تای‌برک عیناً
            -- همان چیزی که در دروازه‌ی S05 تصحیح شد (id نزولی).
            SELECT Anbar, Code, Rate,
                   ROW_NUMBER() OVER (PARTITION BY Anbar, Code
                                      ORDER BY DATE_N DESC, tartib DESC, NUMBER DESC, ID DESC) rn
            FROM (
                SELECT il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS Code,
                       il.AVRAGE AS Rate, h.DATE_N, t.tartib, il.NUMBER, il.ID
                FROM   dbo.INVO_LST il
                JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
                JOIN   dbo.TAGCOD t ON t.CODE=il.TAG
                WHERE  il.TAG IN (1,7,9,24) AND h.DATE_N <= @cut
                UNION ALL
                SELECT CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT),
                       il.AVRAGE2, h.DATE_N, t.tartib, il.NUMBER, il.ID
                FROM   dbo.INVO_LST il
                JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
                JOIN   dbo.TAGCOD t ON t.CODE=il.TAG
                WHERE  il.TAG=5 AND il.ANBARF IS NOT NULL AND h.DATE_N <= @cut
            ) x
        )
        INSERT #Inv (Cut, Anbar, Code, Qty, Rate)
        SELECT @cut, q.Anbar, q.Code, q.Qty,
               ISNULL(r.Rate, f.FI_A)
        FROM   Q q
        LEFT   JOIN R r ON r.Anbar=q.Anbar AND r.Code=q.Code AND r.rn=1
        LEFT   JOIN dbo.STUF_FSK f ON f.ANBAR=q.Anbar AND TRY_CAST(f.CODE AS BIGINT)=q.Code
        WHERE  ABS(q.Qty) > 0.0001;

        SET @i += 1;
    END

    ---- ارزش موجودی به تفکیک نقش انبار ─────────────────────────────
    IF OBJECT_ID('tempdb..#ByRole') IS NOT NULL DROP TABLE #ByRole;

    SELECT  i.Cut, ua.AnbarRole AS Role,
            SUM(ROUND(i.Qty, 2) * ISNULL(i.Rate, 0)) AS Val
    INTO    #ByRole
    FROM    #Inv i
    JOIN    (SELECT Anbar, MIN(AnbarRole) AS AnbarRole
             FROM dbo.CC_UnitAnbar GROUP BY Anbar) ua ON ua.Anbar = i.Anbar
    GROUP BY i.Cut, ua.AnbarRole;

    DECLARE @MatOpen  FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@Open AND Role IN (1,2)), 0),
            @MatClose FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@DT2  AND Role IN (1,2)), 0),
            @FgOpen   FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@Open AND Role = 3),     0),
            @FgClose  FLOAT = ISNULL((SELECT SUM(Val) FROM #ByRole WHERE Cut=@DT2  AND Role = 3),     0);

    ---- خرید دوره: رسید خرید به انبارهای مواد
    DECLARE @Purchase FLOAT = ISNULL((
        SELECT SUM(il.MABL_K)
        FROM   dbo.INVO_LST il
        JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
        JOIN   (SELECT Anbar, MIN(AnbarRole) AS AnbarRole
                FROM dbo.CC_UnitAnbar GROUP BY Anbar) ua ON ua.Anbar = il.ANBAR
        WHERE  il.TAG = 1 AND ua.AnbarRole IN (1,2)
          AND  h.DATE_N BETWEEN @DT1 AND @DT2), 0);

    ---- نیمه‌ساخته‌ی بازگشتی: انتقال از انبار محصول به انبار مصرفی.
    -- سطر اطلاعی است، نه جزئی از مواد — دلیلش بالای همین فایل.
    DECLARE @Recirc FLOAT = ISNULL((
        SELECT SUM(il.MABL_K)
        FROM   dbo.INVO_LST il
        JOIN   dbo.HEAD_LST h ON h.NUMBER=il.NUMBER AND h.TAG=il.TAG
        JOIN   (SELECT Anbar, MIN(AnbarRole) AS AnbarRole FROM dbo.CC_UnitAnbar GROUP BY Anbar) us
               ON us.Anbar = il.ANBAR
        JOIN   (SELECT Anbar, MIN(AnbarRole) AS AnbarRole FROM dbo.CC_UnitAnbar GROUP BY Anbar) ud
               ON ud.Anbar = CAST(il.ANBARF AS INT)
        WHERE  il.TAG = 5 AND us.AnbarRole = 3 AND ud.AnbarRole = 1
          AND  h.DATE_N BETWEEN @DT1 AND @DT2), 0);

    ---- دستمزد و سربارِ واقعی
    DECLARE @Wage FLOAT = ISNULL((SELECT SUM(ActualAmount) FROM dbo.CC_ConversionCost
                                  WHERE RunId=@RunId AND CostKind=1), 0),
            @Oh   FLOAT = ISNULL((SELECT SUM(ActualAmount) FROM dbo.CC_ConversionCost
                                  WHERE RunId=@RunId AND CostKind=2), 0);

    DECLARE @MatAvail FLOAT = @MatOpen + @Purchase,
            @MatUsed  FLOAT = @MatOpen + @Purchase - @MatClose;
    DECLARE @MfgCost  FLOAT = @MatUsed + @Wage + @Oh;
    DECLARE @COGM     FLOAT = @MfgCost;          -- WIP جدا نگه‌داری نمی‌شود
    DECLARE @COGS     FLOAT = @FgOpen + @COGM - @FgClose;

    ---- ارقام سود و زیان از S12
    DECLARE @Sales FLOAT = ISNULL((SELECT SUM(SalesAmount) FROM dbo.CC_ItemMargin WHERE RunId=@RunId), 0),
            @S12Cost FLOAT = ISNULL((SELECT SUM(CostAmount) FROM dbo.CC_ItemMargin WHERE RunId=@RunId), 0);

    /* ═══ ۱) صورت بهای تمام‌شده کالای ساخته‌شده ═══ */
    SELECT * FROM (VALUES
        (10, N'موجودی اول دوره مواد',                @MatOpen,  0),
        (20, N'خرید مواد طی دوره',                   @Purchase, 0),
        (30, N'مواد آماده مصرف',                     @MatAvail, 1),
        (40, N'کسر: موجودی پایان دوره مواد',         -@MatClose, 0),
        (50, N'مواد مصرف‌شده',                        @MatUsed,  1),
        (60, N'دستمزد',                              @Wage,     0),
        (70, N'سربار ساخت',                          @Oh,       0),
        (80, N'بهای تمام‌شده کالای ساخته‌شده',        @COGM,     2),
        (90, N'ــ اطلاعی: نیمه‌ساخته بازگشتی به تولید', @Recirc, 3)
    ) v(ردیف, شرح, مبلغ, نوع)
    ORDER BY ردیف;

    /* ═══ ۲) صورت بهای تمام‌شده کالای فروش‌رفته ═══ */
    SELECT * FROM (VALUES
        (10, N'موجودی اول دوره کالای ساخته‌شده',      @FgOpen,  0),
        (20, N'بهای تمام‌شده کالای ساخته‌شده',        @COGM,    0),
        (30, N'کالای آماده فروش',                    @FgOpen + @COGM, 1),
        (40, N'کسر: موجودی پایان دوره کالای ساخته‌شده', -@FgClose, 0),
        (50, N'بهای تمام‌شده کالای فروش‌رفته',        @COGS,    2),
        (60, N'ــ تطبیق: بهای فروش‌رفته طبق سود و زیان', @S12Cost, 3),
        (70, N'ــ اختلاف',                            @COGS - @S12Cost, 3)
    ) v(ردیف, شرح, مبلغ, نوع)
    ORDER BY ردیف;

    /* ═══ ۳) صورت سود و زیان ═══
       هزینه‌های دوره از CC_ExpenseAcc می‌آیند. مانده‌ی هر سرفصل عیناً
       مثل CC_UnitAcc حساب می‌شود: بدهکار منهای بستانکار در بازه‌ی همین
       اجرا، ضربدر Ratio. سرفصلی که تعریف نشده باشد صفر می‌ماند و سطرش
       هم نمایش داده می‌شود تا معلوم باشد جایش خالی است، نه اینکه بی‌صدا
       از صورت حذف شود. */
    IF OBJECT_ID('tempdb..#Exp') IS NOT NULL DROP TABLE #Exp;

    SELECT  m.ExpenseKind AS Kind,
            ISNULL(SUM(t.Amount * m.Ratio), 0) AS Amount
    INTO    #Exp
    FROM    dbo.CC_ExpenseAcc m
    CROSS   APPLY (
                SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                FROM   dbo.DEED_DTL d
                JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                  AND  d.HES_K = m.HesKol
                  AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                  AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
            ) t
    WHERE   m.IsActive = 1
    GROUP BY m.ExpenseKind;

    DECLARE @ExpSell  FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=1), 0),
            @ExpAdmin FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=2), 0),
            @ExpFin   FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=3), 0),
            @ExpOther FLOAT = ISNULL((SELECT Amount FROM #Exp WHERE Kind=4), 0);

    DECLARE @Gross FLOAT = @Sales - @S12Cost;
    DECLARE @ExpAll FLOAT = @ExpSell + @ExpAdmin + @ExpFin + @ExpOther;

    SELECT * FROM (VALUES
        (10, N'فروش خالص',                           @Sales,   0),
        (20, N'کسر: بهای تمام‌شده کالای فروش‌رفته',   -@S12Cost, 0),
        (30, N'سود ناخالص',                          @Gross,   1),
        (40, N'کسر: هزینه‌های فروش',                 -@ExpSell,  0),
        (50, N'کسر: هزینه‌های اداری',                -@ExpAdmin, 0),
        (60, N'کسر: هزینه‌های مالی',                 -@ExpFin,   0),
        (70, N'کسر: سایر هزینه‌ها',                  -@ExpOther, 0),
        (80, N'جمع هزینه‌های دوره',                  -@ExpAll,   1),
        (90, N'سود عملیاتی',                         @Gross - @ExpAll, 2),
        (100, N'ــ درصد سود ناخالص',
             CASE WHEN @Sales <> 0 THEN ROUND(@Gross / @Sales * 100, 1) END, 3),
        (110, N'ــ درصد سود عملیاتی',
             CASE WHEN @Sales <> 0 THEN ROUND((@Gross - @ExpAll) / @Sales * 100, 1) END, 3)
    ) v(ردیف, شرح, مبلغ, نوع)
    ORDER BY ردیف;

    /* ═══ ۴) تفکیک سرفصل‌های هزینه — تا معلوم باشد هر رقم از کجا آمده ═══ */
    SELECT  CASE m.ExpenseKind WHEN 1 THEN N'فروش' WHEN 2 THEN N'اداری'
                               WHEN 3 THEN N'مالی' ELSE N'ساير' END AS طبقه,
            m.HesKol      AS کل,
            m.HesMoin     AS معین,
            m.HesTafsili  AS تفصیلی,
            m.Ratio       AS ضریب,
            ISNULL(t.Amount, 0)            AS مانده_حساب,
            ISNULL(t.Amount, 0) * m.Ratio  AS سهم_این_طبقه,
            m.Note        AS یادداشت
    FROM    dbo.CC_ExpenseAcc m
    CROSS   APPLY (
                SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                FROM   dbo.DEED_DTL d
                JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                  AND  d.HES_K = m.HesKol
                  AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                  AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
            ) t
    WHERE   m.IsActive = 1
    ORDER BY m.ExpenseKind, m.HesKol, m.HesMoin, m.HesTafsili;
END
GO

PRINT N'رويه CC_sp_FinancialStatements ايجاد شد.';
GO
";
            TryExecuteCostCloseBatch(db, financialStatements,
                "CC_ExpenseAcc و CC_sp_FinancialStatements",
                "اسکریپت 28-financial-statements.sql را اجرا کنید (به CC_ItemMargin, CC_ConversionCost و CC_UnitAnbar نیاز دارد).");
        }

        private static void TryExecuteCostCloseBatch(SqlConnection db, string script, string what, string hint)
        {
            try
            {
                ExecuteBatches(db, script);
                Console.WriteLine($"[CostCloseScript] {what} OK.");
            }
            catch (SqlException ex) when (ex.Message.Contains("Invalid object name 'dbo.CC_"))
            {
                Console.WriteLine($"[CostCloseScript] base CC_* tables missing for {what} - {hint}");
            }
        }
    }
}
