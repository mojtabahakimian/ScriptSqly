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
        private static void CostCloseScript(SqlConnection db)
        {
            // ترتیب مهم است: بلوک‌های پایه (۱۰ تا ۱۳) جدول‌ها و رویه‌هایی را
            // می‌سازند که بقیه بلوک‌ها به آن‌ها وابسته‌اند.
            string baseSchema = @"/* ═══════════════════════════════════════════════════════════════════
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
    Attempt       TINYINT       NOT NULL DEFAULT 1,
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
    TargetKind     TINYINT      NOT NULL,   -- 1=سود صفر 2=درصد مشخص 3=آزاد
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

            string seedData = @"/* ═══════════════════════════════════════════════════════════════════
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
 ('CHK-01', N'کاردکس منفی', 'S05', 1, 2, NULL,
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
  N'مقدار منفی در یک سطر فرمول قابل قبول نیست و باعث می‌شود مانده حساب کالای در جریان ساخت (۷۵۱) هرگز متوازن نشود. با دکمه اصلاح، آن سطر را صفر یا حذف کنید.', 75)
) AS s (RuleCode, RuleName, StepCode, ExType, DefaultSeverity, Threshold, RemedyText, SortOrder)
ON t.RuleCode = s.RuleCode
WHEN MATCHED THEN UPDATE SET
    t.RuleName = s.RuleName, t.StepCode = s.StepCode, t.ExType = s.ExType,
    t.DefaultSeverity = s.DefaultSeverity, t.Threshold = s.Threshold,
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

    DECLARE @try TINYINT =
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
    DECLARE @th9 FLOAT =
        ISNULL((SELECT Threshold FROM dbo.CC_CheckRule WHERE RuleCode='CHK-09'), 0.001);

    ;WITH Khod AS (
        SELECT CAST(hm.CODE AS BIGINT) AS Code,
               SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                      + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha
        FROM   dbo.HEAD_MANF hm JOIN dbo.DTL_MANF d ON d.FNUMB = hm.FNUMB
        WHERE  hm.GHEYMAT = @Month
        GROUP BY CAST(hm.CODE AS BIGINT), hm.FNUMB
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

            string s05Gate = @"/* ═══════════════════════════════════════════════════════════════════
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
       دو طرف رویه‌های مرجع نیامده‌اند (۳,۱۲,۱۳,۱۴,۱۵,۱۷,۱۸,۲۷) اصلاً
       در این محاسبه شرکت نمی‌کنند، دقیقاً چون منبع مرجع هم شرکتشان
       نمی‌دهد. TAG=6 «انتقالی-ورود» هم عمداً نیامده — طرف دیگرِ همان
       سند TAG=5 است و اگر هر دو حساب شوند، انتقال دوبار شمرده می‌شود.

       انبارگردانی (ANBGRD_LST/ANBGRD_HEAD) اینجا رویداد‌به‌رویداد
       اعمال می‌شود (نه با قاعدهٔ جمع‌کلِ عجیب رویه‌های مرجع که کل
       اختلاف یک کالا/انبار را یک‌جا یا کاملاً ورود یا کاملاً خروج
       حساب می‌کند) — چون این کنترل به ترتیب واقعی رویدادها نیاز
       دارد، نه فقط مانده نهایی.
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
       خودشان را دارند). ترتیب واقعی طبق قرارداد این سیستم از شرح
       تگ (TAGCOD.BARGAH) می‌آید، نه از NUMBER.

       فقط اولین نقطه منفی هر کالا/انبار در همین دوره گزارش می‌شود؛
       بقیه دنباله همان یک مشکل‌اند و فهرست را شلوغ می‌کنند.
       ───────────────────────────────────────────────────────────── */
    IF OBJECT_ID('tempdb..#PM') IS NOT NULL DROP TABLE #PM;

    -- ستون‌ها صریحاً تعریف می‌شوند (نه SELECT…INTO) چون شاخهٔ انبارگردانی
    -- برای TAG مقدار NULL دارد و نمی‌خواهیم NOT NULL این ستون از شاخهٔ
    -- اول به‌صورت ضمنی استنتاج شود.
    CREATE TABLE #PM (
        Anbar   INT          NULL,
        code    BIGINT       NULL,
        DATE_N  BIGINT       NULL,
        NUMBER  FLOAT        NULL,
        TAG     FLOAT        NULL,
        Meghdar FLOAT        NULL
    );

    INSERT #PM
    SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code,
            hl.DATE_N, hl.NUMBER, il.TAG, (il.MEGHk - il.MEGH_MAR) AS Meghdar
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

    INSERT #PM
    SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG,
            (il.MEGHk - il.MEGH_MAR)
    FROM    dbo.INVO_LST il
    JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
    WHERE   il.TAG = 5
      AND   il.ANBARF IS NOT NULL
      AND   hl.DATE_N BETWEEN @DT1 AND @DT2;

    INSERT #PM
    SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), hl.DATE_N, hl.NUMBER, il.TAG,
            -(il.MEGHk - il.MEGH_MAR)
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

    INSERT #PM
    SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT), ah.GRD_DATE, ah.GRD_NUM,
            CAST(NULL AS FLOAT), -(al.MOG - ISNULL(al.NUM3, 0))
    FROM    dbo.ANBGRD_LST al
    JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
    WHERE   ah.N_S IS NOT NULL
      AND   ah.GRD_ANBAR IS NOT NULL
      AND   ah.GRD_DATE BETWEEN @DT1 AND @DT2;

    ;WITH DistinctAnbars AS (
        SELECT DISTINCT Anbar FROM #PM WHERE Anbar IS NOT NULL
    ),
    Opening AS (
        -- مانده ابتدای دوره از تابع مرجع کارت کالا، فقط برای جفت‌های
        -- (انبار، کالا) که واقعاً در همین دوره حرکت دارند.
        SELECT  m.ANBAR AS Anbar, TRY_CAST(m.CODE AS BIGINT) AS code, m.MAND AS OpeningBalance
        FROM    DistinctAnbars da
        CROSS   APPLY dbo.MOGUDI(@DT1 - 1, CAST(da.Anbar AS NVARCHAR(50))) m
    ),
    AllMovement AS (
        SELECT  o.Anbar, o.code, CAST(0 AS BIGINT) AS DATE_N, CAST(0 AS FLOAT) AS NUMBER,
                CAST(NULL AS FLOAT) AS TAG, N'' AS Bargah, o.OpeningBalance AS Meghdar
        FROM    Opening o
        WHERE   EXISTS (SELECT 1 FROM #PM p WHERE p.Anbar = o.Anbar AND p.code = o.code)

        UNION ALL
        SELECT  p.Anbar, p.code, p.DATE_N, p.NUMBER, p.TAG,
                ISNULL(tc.BARGAH, N'') AS Bargah, p.Meghdar
        FROM    #PM p
        LEFT    JOIN dbo.TAGCOD tc ON tc.CODE = p.TAG
        WHERE   p.Anbar IS NOT NULL AND p.code IS NOT NULL
    ),
    Tajamoi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Bargah,
                SUM(Meghdar) OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Bargah, NUMBER
                    ROWS UNBOUNDED PRECEDING) AS Mande
        FROM    AllMovement
    ),
    AvvalinManfi AS (
        SELECT  Anbar, code, DATE_N, NUMBER, TAG, Bargah, Mande,
                ROW_NUMBER() OVER (
                    PARTITION BY Anbar, code
                    ORDER BY DATE_N, Bargah, NUMBER) AS rn
        FROM    Tajamoi
        WHERE   Mande < -0.0001
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
      AND   m.DATE_N BETWEEN @DT1 AND @DT2;

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
        ;WITH AnbarMovement AS (
            SELECT  il.ANBAR AS Anbar, TRY_CAST(il.CODE AS BIGINT) AS code, il.MABL_K AS Mablk
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (1, 7, 9, 24)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), (il.MABL * il.MEGH_MAR)
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 22
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  CAST(il.ANBARF AS INT), TRY_CAST(il.CODE AS BIGINT), il.MABL_K
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 5
              AND   il.ANBARF IS NOT NULL
              AND   hl.DATE_N <= @DT2
              AND   CAST(il.ANBARF AS INT) IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MABL_K
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG IN (2, 5, 8, 10, 11, 26)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  il.ANBAR, TRY_CAST(il.CODE AS BIGINT), -il.MABL_K
            FROM    dbo.INVO_LST il
            JOIN    dbo.HEAD_LST hl ON hl.TAG = il.TAG AND hl.NUMBER = il.NUMBER
            WHERE   il.TAG = 20
              AND   (hl.TAMIR = 1 OR hl.TAMIR = 4)
              AND   hl.DATE_N <= @DT2
              AND   il.ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)

            UNION ALL
            SELECT  ah.GRD_ANBAR, TRY_CAST(al.CODE AS BIGINT),
                    -(al.MOG - ISNULL(al.NUM3, 0)) * ISNULL(al.MABL, 0)
            FROM    dbo.ANBGRD_LST al
            JOIN    dbo.ANBGRD_HEAD ah ON ah.GRD_NUM = al.GRD_NUM
            WHERE   ah.N_S IS NOT NULL
              AND   ah.GRD_DATE <= @DT2
              AND   ah.GRD_ANBAR IN (SELECT Anbar FROM dbo.CC_AnbarHes)
        ),
        KartAnbar AS (
            SELECT  Anbar, code, SUM(Mablk) AS Mande
            FROM    AnbarMovement
            WHERE   Anbar IS NOT NULL AND code IS NOT NULL
            GROUP BY Anbar, code
        ),
        Hesabdari AS (
            SELECT  am.Anbar, TRY_CAST(d.HES_T AS BIGINT) AS code,
                    SUM(d.BED) - SUM(d.BES) AS Mande
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED  h  ON h.N_S = d.N_S
            JOIN    dbo.CC_AnbarHes am ON am.HesKol = d.HES_K AND am.HesMoin = d.HES_M
            WHERE   h.DATE_S <= @DT2
            GROUP BY am.Anbar, TRY_CAST(d.HES_T AS BIGINT)
        )
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Anbar, Code, Amount, Description)
        SELECT  @RunId, 'S05', 'CHK-02', 2, 2,
                ISNULL(k.Anbar, hh.Anbar), ISNULL(k.code, hh.code),
                ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0),
                CONCAT(N'انبار ', ISNULL(k.Anbar, hh.Anbar),
                       N' (', ISNULL(a.NAMES, N'نامشخص'), N'): کارت انبار ',
                       FORMAT(ISNULL(k.Mande, 0), 'N0'),
                       N' در برابر حسابداری ', FORMAT(ISNULL(hh.Mande, 0), 'N0'))
        FROM    KartAnbar k
        FULL    OUTER JOIN Hesabdari hh ON hh.Anbar = k.Anbar AND hh.code = k.code
        LEFT    JOIN dbo.TCOD_ANBAR a ON a.CODE = ISNULL(k.Anbar, hh.Anbar)
        WHERE   ABS(ISNULL(k.Mande, 0) - ISNULL(hh.Mande, 0)) > 1;
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
            TryExecuteCostCloseBatch(db, s05Gate, "CC_sp_S05_Gate",
                "اسکریپت‌های 10-schema.sql تا 13-chk04-and-autofix.sql را اول اجرا کنید.");

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

    DECLARE @TafDastmozd BIGINT = 99999999;
    DECLARE @UnitId INT, @Dep INT, @SplitMode TINYINT;

    -- Depatman = NULL يعني «همهٔ دپارتمان‌ها» — اگر بيش از يک واحد فعال اين
    -- حالت را داشته باشند، هر دو دقيقاً همان برگه‌هاي توليد را پردازش
    -- مي‌کنند. چون اين حلقه IMBIBE_MANF/IMBIBE_SAR را در HEAD_MANF مستقيماً
    -- ويرايش مي‌کند، واحد دومي که در همان اجرا پردازش مي‌شود ديگر مقدار
    -- اصلي فرمول را نمي‌بيند بلکه مقدارِ از قبل تعديل‌شدهٔ واحد اول را
    -- مي‌خواند و رويش دوباره ضريب مي‌زند — نتيجه فرمول را خراب مي‌کند، نه
    -- فقط عدد کنترلي را. مقدار پيش‌فرض داده اوليه (11-seed-data.sql) دقيقاً
    -- همين ترکيب را دارد؛ تا وقتي نصب‌کننده Depatman هر واحد را با دپارتمان
    -- واقعي‌اش عوض نکند، اجراي واقعي همين‌جا فرمول‌ها را خراب مي‌کرد.
    IF (SELECT COUNT(*) FROM dbo.CC_Unit WHERE IsActive = 1 AND Depatman IS NULL) > 1
    BEGIN
        RAISERROR(N'بيش از يک واحد توليدي فعال بدون دپارتمان مشخص (همه‌شمول) وجود دارد؛ اين باعث پردازش دوباره‌ي همان برگه‌ها و خراب شدن فرمول‌ها مي‌شود. دپارتمان هر واحد را در تنظیمات مشخص کنيد.', 16, 1);
        RETURN;
    END

    DELETE dbo.CC_ConversionCost WHERE RunId = @RunId;

    DECLARE cUnit CURSOR LOCAL FAST_FORWARD FOR
        SELECT UnitId, Depatman, SplitMode
        FROM   dbo.CC_Unit WHERE IsActive = 1 ORDER BY SeqNo;

    OPEN cUnit;
    FETCH NEXT FROM cUnit INTO @UnitId, @Dep, @SplitMode;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        ---- ۱) جذب‌شده از برگه‌هاي توليد اين واحد
        DECLARE @absWage FLOAT, @absOh FLOAT;

        SELECT  @absWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @absOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND  (@Dep IS NULL OR h.DEPATMAN = @Dep);

        DECLARE @absTotal FLOAT = @absWage + @absOh;

        ---- ۲) کنترل متقابل با حساب ۷۵۱ (فقط تفصيلي دستمزد)
        DECLARE @absWip FLOAT;

        SELECT  @absWip = ISNULL(SUM(d.BES) - SUM(d.BED), 0)
        FROM    dbo.DEED_DTL d
        JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
        WHERE   d.HES_K = 751 AND d.HES_T = @TafDastmozd
          AND   hd.DATE_S BETWEEN @DT1 AND @DT2;

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
            (@RunId, @UnitId, 1, @absWage, NULL, @actWage, @kWage, NULL),
            (@RunId, @UnitId, 2, @absOh,   NULL, @actOh,   @kOh,   NULL);

        ---- ۶) هشدار اختلاف کنترلي
        IF ABS(@absWip - @absTotal) > 10000000
            INSERT dbo.CC_Exception
                (RunId, StepCode, RuleCode, ExType, Severity, Amount, Description)
            VALUES (@RunId, 'S10', 'CHK-08', 10, 1, @absWip - @absTotal,
                    CONCAT(N'اختلاف جذب: برگه‌هاي توليد ', FORMAT(@absTotal, 'N0'),
                           N' در برابر حساب ۷۵۱ ', FORMAT(@absWip, 'N0')));

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
                          AND (@Dep IS NULL OR h.DEPATMAN = @Dep));

            INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
            VALUES (@RunId, 'S10', 1,
                    CONCAT(N'واحد ', @UnitId, N': ضريب تعديل ',
                           FORMAT(@kWage, 'N5'), N' روي ', @@ROWCOUNT, N' فرمول'));

            COMMIT;
        END

        FETCH NEXT FROM cUnit INTO @UnitId, @Dep, @SplitMode;
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
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S11_PropagateRates
    @RunId  INT,
    @Month  TINYINT,
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
        Code  BIGINT PRIMARY KEY,
        Llc   SMALLINT NOT NULL DEFAULT 0,
        FNUMB INT      NULL,
        Src   TINYINT  NOT NULL DEFAULT 1,
        Mat   FLOAT    NOT NULL DEFAULT 0,
        Wage  FLOAT    NOT NULL DEFAULT 0,
        Oh    FLOAT    NOT NULL DEFAULT 0
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

    ---- فرمول هر کالا
    UPDATE  c
       SET  c.FNUMB = f.FNUMB,
            c.Src   = 2
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 hm.FNUMB
                   FROM   dbo.HEAD_MANF hm
                   WHERE  CAST(hm.CODE AS BIGINT) = c.Code AND hm.GHEYMAT = @Month
                   ORDER BY hm.DATE_ACTIV DESC, hm.FNUMB DESC) f;

    /* ─── ۴) نرخ مواد خريدني: ميانگين وزني خروج از انبار ─── */
    UPDATE  c
       SET  c.Mat = z.fi, c.Src = 1
    FROM    #C c
    JOIN   (SELECT k.code, SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
            FROM   dbo.KALAS k
            WHERE  k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
            GROUP BY k.code) z ON z.code = c.Code
    WHERE   c.FNUMB IS NULL AND z.fi IS NOT NULL;

    ---- بدون گردش در ماه: آخرين نرخ ميانگين ثبت‌شده
    UPDATE  c
       SET  c.Mat = lp.AVRAGE
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 i.AVRAGE
                   FROM   dbo.INVO_LST i
                   JOIN   dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                   WHERE  CAST(i.CODE AS BIGINT) = c.Code AND i.AVRAGE > 0
                   ORDER BY h.DATE_N DESC, i.NUMBER DESC) lp
    WHERE   c.FNUMB IS NULL AND c.Mat = 0;

    UPDATE #C SET Src = 3 WHERE FNUMB IS NULL AND Mat = 0;

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

        ---- ۵-ب) بهاي والد = مجموع اجزا + جذب دستمزد + جذب سربار
        UPDATE  c
           SET  c.Mat  = ISNULL(a.MatCost, 0),
                c.Wage = ISNULL(hm.IMBIBE_MANF, 0),
                c.Oh   = ISNULL(hm.IMBIBE_SAR , 0)
        FROM    #C c
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = c.FNUMB
        CROSS   APPLY (SELECT SUM(d.MEGHk * (ch.Mat + ch.Wage + ch.Oh)) AS MatCost
                       FROM   dbo.DTL_MANF d
                       JOIN   #C ch ON ch.Code = CAST(d.CODE AS BIGINT)
                       WHERE  d.FNUMB = c.FNUMB) a
        WHERE   c.Llc = @lvl AND c.FNUMB IS NOT NULL;

        SET @lvl -= 1;
    END

    /* ─── ۶) ثبت نتيجه در CC_ItemCost ─── */
    DELETE dbo.CC_ItemCost WHERE RunId = @RunId;

    INSERT dbo.CC_ItemCost
        (RunId, PeriodMonth, Code, LowLevelCode, SourceKind, FNUMB,
         MaterialCost, WageCost, OverheadCost)
    SELECT  @RunId, @Month, Code, Llc, Src, FNUMB, Mat, Wage, Oh
    FROM    #C;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S11', 1,
            CONCAT(N'انتشار نرخ: ', @totalChanges, N' نرخ به‌روز شد'),
            (SELECT MAX(Llc) AS maxLevel, COUNT(*) AS items,
                    SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END) AS noSource
             FROM #C FOR JSON PATH));

    /* ─── ۷) آزمون سلامت: CHK-09 بايد صفر شود ─── */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-09';

    ;WITH Khod AS (
        SELECT CAST(hm.CODE AS BIGINT) AS Code,
               SUM(ISNULL(d.MABLK,0)) + MAX(ISNULL(hm.IMBIBE_MANF,0))
                                      + MAX(ISNULL(hm.IMBIBE_SAR,0)) AS Baha
        FROM   dbo.HEAD_MANF hm JOIN dbo.DTL_MANF d ON d.FNUMB = hm.FNUMB
        WHERE  hm.GHEYMAT = @Month
        GROUP BY CAST(hm.CODE AS BIGINT), hm.FNUMB
    ),
    DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code, AVG(d.SMABL) AS Nerkh
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S11', 'CHK-09', 14, 2, k.Code, k.Baha - v.Nerkh,
            N'نرخ پس از اجراي موتور هنوز منتشر نشده — نياز به بررسي'
    FROM    Khod k JOIN DarValed v ON v.Code = k.Code
    WHERE   ABS(k.Baha - v.Nerkh) / NULLIF(k.Baha, 0) > 0.001;

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
   EXEC dbo.CC_sp_S11_PropagateRates    @RunId=1, @Month=5, @WhatIf=1;
*/
GO
";
            TryExecuteCostCloseBatch(db, rateEngine, "CC_sp_S10_BalanceConversion و CC_sp_S11_PropagateRates",
                "اسکریپت 15-rate-engine-production.sql را اجرا کنید (به CC_ConversionCost, CC_UnitAcc, CC_ItemCost نیاز دارد).");

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
            TryExecuteCostCloseBatch(db, rollback, "CC_sp_Rollback و CC_sp_PurgeSnapshots",
                "اسکریپت 16-rollback.sql را اجرا کنید (به CC_Snapshot نیاز دارد).");

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

    ;WITH Prev AS (
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
    FROM    dbo.CC_Variance v
    LEFT    JOIN Prev p ON p.Code = v.Code AND p.rn = 1
    OUTER   APPLY (SELECT TOP 1 h.FNUMB
                   FROM   dbo.HEAD_MANF h
                   WHERE  CAST(h.CODE AS BIGINT) = p.TargetCode
                     AND  h.GHEYMAT = @Month
                   ORDER BY h.FNUMB DESC) hm
    WHERE   v.RunId = @RunId;

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

    ;WITH Usage AS (
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
    JOIN    dbo.CC_Variance          v  ON v.Code  = u.Code AND v.RunId  = @RunId
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

    UPDATE  d
       SET  d.MEGHk = d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty),
            d.MABLK = ROUND(ISNULL(d.SMABL, 0) *
                            (d.MEGHk + (s.QtyVariance * s.Ratio / p.ProdQty)), 0)
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
    JOIN    #Prod  p ON p.FNUMB = d.FNUMB;

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

   وقتی زیان یک کالا صفر می‌شود، مبلغ آن از بهای تمام‌شده‌اش کم و
   به کالای متعادل‌کننده اضافه می‌شود، تا جمع کل دست‌نخورده بماند.

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

    ---- مبلغ تعديل لازم براي هر کالاي هدف‌دار
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
                 ELSE 0 END AS AdjustAmount
    INTO    #Adj
    FROM    dbo.CC_ItemMargin m
    JOIN    dbo.CC_MarginTarget t ON t.Code = m.Code AND t.IsActive = 1
    WHERE   m.RunId = @RunId
      AND   t.TargetKind IN (1, 2)
      AND   m.QtySold <> 0;

    DELETE #Adj WHERE ABS(AdjustAmount) < 1;

    ---- هشدار: کالاي متعادل‌کننده زيان‌ده مي‌شود
    IF OBJECT_ID('tempdb..#Warn') IS NOT NULL DROP TABLE #Warn;

    SELECT  a.Code                    AS SourceCode,
            a.BalancingCode,
            a.AdjustAmount,
            bm.Profit                 AS BalancerProfitBefore,
            bm.Profit - a.AdjustAmount AS BalancerProfitAfter
    INTO    #Warn
    FROM    #Adj a
    JOIN    dbo.CC_ItemMargin bm ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
    WHERE   a.BalancingCode IS NOT NULL
      AND   bm.Profit - a.AdjustAmount < 0
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
                hm.IMBIBE_MANF + (x.Amount / NULLIF(x.Qty, 0))
        FROM    dbo.HEAD_MANF hm
        JOIN   (SELECT a.BalancingCode AS Code,
                       SUM(a.AdjustAmount) AS Amount,
                       MAX(bm.QtySold) AS Qty
                FROM   #Adj a
                JOIN   dbo.CC_ItemMargin bm
                       ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
                WHERE  a.BalancingCode IS NOT NULL AND bm.QtySold <> 0
                GROUP BY a.BalancingCode) x ON CAST(hm.CODE AS BIGINT) = x.Code
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
                a.BalancingCode      AS کالاي_متعادل_کننده,
                sb.NAME              AS نام_متعادل_کننده
        FROM    #Adj a
        LEFT    JOIN dbo.STUF_DEF s  ON TRY_CAST(s.CODE  AS BIGINT) = a.Code
        LEFT    JOIN dbo.STUF_DEF sb ON TRY_CAST(sb.CODE AS BIGINT) = a.BalancingCode
        ORDER BY ABS(a.AdjustAmount) DESC;

        SELECT  w.SourceCode              AS کالاي_مبدا,
                w.BalancingCode           AS متعادل_کننده,
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

    ---- افزايش بهاي کالاي متعادل‌کننده به همان مبلغ
    UPDATE  hm
       SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF + (x.Amount / NULLIF(x.Qty, 0))
    OUTPUT  @RunId, 'S12', inserted.FNUMB,
            TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
            deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
            N'جذب اثر معکوس هدف حاشيه سود'
      INTO  dbo.CC_FormulaChange
            (RunId, StepCode, FNUMB, ParentCode, ChildCode,
             FieldName, OldValue, NewValue, Reason)
    FROM    dbo.HEAD_MANF hm
    JOIN   (SELECT a.BalancingCode AS Code,
                   SUM(a.AdjustAmount) AS Amount,
                   MAX(bm.QtySold) AS Qty
            FROM   #Adj a
            JOIN   dbo.CC_ItemMargin bm
                   ON bm.Code = a.BalancingCode AND bm.RunId = @RunId
            WHERE  a.BalancingCode IS NOT NULL AND bm.QtySold <> 0
            GROUP BY a.BalancingCode) x ON CAST(hm.CODE AS BIGINT) = x.Code
    WHERE   hm.GHEYMAT = @Month;

    DECLARE @n2 INT = @@ROWCOUNT;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S12', 1,
            CONCAT(N'هدف حاشيه سود: ', @n1, N' کالاي هدف، ', @n2, N' متعادل‌کننده'));

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

            // ⚠ حتماً بعد از marginReportApprove اجرا شود — نسخه CC_sp_S12_CalcMargin
            // را با محاسبه بر مبنای کاردکس (KALAS) جایگزین می‌کند.
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
        }
        private static void TryExecuteCostCloseBatch(SqlConnection db, string script, string what, string hint)
        {
            try
            {
                ExecuteBatches(db, script);
                Console.WriteLine($"[CostCloseScript] {what} ایجاد/به‌روزرسانی شد.");
            }
            catch (SqlException ex) when (ex.Message.Contains("Invalid object name 'dbo.CC_"))
            {
                Console.WriteLine($"[CostCloseScript] جدول‌های پایه CC_* پیدا نشدند برای {what} — {hint}");
            }
        }

    }
}
