using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// نگهداری و تعمیرات (CMMS) در سفیر: جدول‌های PM_* ِ Access اگر نباشند، جدول‌های تازه‌ی CMMS_* و فرم‌های PMHYGIENE و PMCONFIG.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/73-cmms.sql در مخزن Safir یکی بماند
        /// (آزمونِ CmmsTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void CmmsScript(SqlConnection db)
        {
            ExecuteBatches(db, CmmsSql);
        }

        private const string CmmsSql = @"/* ═══════════════════════════════════════════════════════════════════
   نگهداری و تعمیرات (CMMS) در Safir — راهنما: docs/guides/cmms.md

   جدول‌های Access (PM_*) همان‌اند که بودند؛ WPF آن‌ها را در «سال مالی جدید» با همان شناسه‌ها کپی می‌کند و سفیر هم همان‌ها را
   می‌خواند و می‌نویسد. فقط اگر در دیتابیسی نباشند با همان ستون‌ها ساخته می‌شوند (کلیدهای خارجیِ تازه بی CASCADE؛ کلیدهای
   موجود دست نمی‌خورند). هر چه تازه است در جدول‌های CMMS_* کنارشان می‌آید:
   • CMMS_CONFIG     — تنظیمات (کلید/مقدار).
   • CMMS_CLASS      — کلاسِ تجهیز (PM_ASSETS.AS_GRP)؛ اگر خالی باشد ۱۰ ردیفِ پیش‌فرض (۱ تولید، ۳ آزمایشگاه، مثلِ داده‌ی Access).
   • CMMS_ASSET      — ۱:۱ با PM_ASSETS: بحرانیت (ISO 55001)، سازنده، نصب، در تماس با غذا، حساسِ ایمنی، بازرسیِ قانونی،
                       ابزارِ اندازه‌گیری، کنتور، هزینه‌ی توقف، مشخصات و نقاطِ جداسازیِ انرژی.
   • CMMS_METER      — قرائت‌های کنتورِ هر تجهیز.
   • CMMS_PLAN       — ۱:۱ با PM_RUMAINTE: نوع، فعال، ثابت/شناور، پیش‌آگهی، اولویت، ترخیصِ بهداشتی، LOTO، مجوزِ کار، روان‌کارِ
                       خوراکی، مرجعِ بازرسی و قانونی، دستورالعمل، مبدأِ آخرین انجام.
   • CMMS_PLAN_STEP  — چک‌لیستِ هر برنامه؛ CMMS_WO_STEP نتیجه‌اش در هر دستور کار.
   • CMMS_WO         — ۱:۱ با PM_JOB: مرحله، نوع، اولویت، مهلت، زمانِ واقعی، توقف، کدهای ISO 14224، تعمیرِ موقت و پیگیری،
                       LOTO و مجوز، ترخیصِ بهداشتی، پیمانکار، سرمایه‌ای، کنتور، کالیبراسیون و گواهی.
   • CMMS_WO_LABOR   — کارکردِ هر نفر؛ CMMS_WO_DOC پیوندِ برگه‌های انبار (TAG 11 و 23).
   • CMMS_REQ        — ۱:۱ با PM_EM_FAILURE_EVENTS: وضعیت، اولویت، ایمنی و ایمنیِ غذا، تصمیم.
   • CMMS_LOG        — سابقه‌ی کارها.
   • فرم‌ها: ۹ فرمِ Access اگر نباشند، به‌علاوه‌ی PMHYGIENE «ترخیص بهداشتی تجهیزات پس از تعمیر» و PMCONFIG «تنظیمات نگهداری و
     تعمیرات»؛ ردیفِ SAL_CHEK ِ هر کاربر بسته ساخته می‌شود، جز PMCONFIG که از UPD ِ «مدیریت دستور کارها» می‌آید.
   هیچ ستون، کلید یا داده‌ی موجودی عوض نمی‌شود؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ── جدول‌های Access، فقط اگر نباشند ── */
IF OBJECT_ID(N'dbo.PM_LOCATION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_LOCATION (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_LOCATION PRIMARY KEY,
        LO_NAME nvarchar(250) NOT NULL,
        LO_IDD int NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_LOCATION_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_JOBTITLE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_JOBTITLE (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_JOBTITLE PRIMARY KEY,
        JOB_TITLE nvarchar(250) NOT NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_JOBTITLE_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_EM_FAILURE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_EM_FAILURE (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_EM_FAILURE PRIMARY KEY,
        FI_NAME nvarchar(150) NOT NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_EM_FAILURE_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_ASSETS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_ASSETS (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_ASSETS PRIMARY KEY,
        AS_NAME nvarchar(200) NOT NULL,
        AS_SERIAL nvarchar(50) NULL,
        AS_MODEL nvarchar(50) NULL,
        AS_GRP int NULL,
        AS_Longitude decimal(10, 7) NULL,
        AS_Latitude decimal(10, 7) NULL,
        AS_IDDMAIN int NULL,
        AS_IDAMVAL int NULL,
        AS_IMAGE image NULL,
        AS_HESAB nvarchar(50) NULL,
        AS_GARANTI int NULL,
        AS_GARANTICOMP nvarchar(200) NULL,
        AS_LOCATION int NULL CONSTRAINT FK_PM_ASSETS_PM_LOCATION FOREIGN KEY REFERENCES dbo.PM_LOCATION (IDD),
        AS_STATUS int NULL CONSTRAINT DF_PM_ASSETS_AS_STATUS DEFAULT 1,
        AS_CODING nvarchar(50) NULL,
        SGN1 bit NULL CONSTRAINT DF_PM_ASSETS_SGN1 DEFAULT 0,
        SGN2 bit NULL CONSTRAINT DF_PM_ASSETS_SGN2 DEFAULT 0,
        SGN3 bit NULL CONSTRAINT DF_PM_ASSETS_SGN3 DEFAULT 0,
        SGN4 bit NULL CONSTRAINT DF_PM_ASSETS_SGN4 DEFAULT 0,
        sgn1usid int NULL,
        sgn2usid int NULL,
        sgn3usid int NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_ASSETS_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_RUMAINTE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_RUMAINTE (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_RUMAINTE PRIMARY KEY,
        AS_IDD int NOT NULL CONSTRAINT FK_PM_RUMAINTE_PM_ASSETS FOREIGN KEY REFERENCES dbo.PM_ASSETS (IDD),
        RM_RSANGESH int NOT NULL,
        RM_PERIOD int NOT NULL,
        RM_MEGHDAR int NOT NULL,
        RM_PERSON nvarchar(50) NOT NULL,
        RM_JOB int NOT NULL,
        RM_STDATE int NOT NULL,
        RM_STTIME int NOT NULL,
        RM_TAKHIRMO int NOT NULL CONSTRAINT DF_PM_RUMAINTE_RM_TAKHIRMO DEFAULT 0,
        RM_TAKHVAHED int NOT NULL,
        RM_ELAMBE nvarchar(50) NULL,
        TM_TIMENEED int NULL,
        TM_HESAB_HAZINE nvarchar(50) NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_RUMAINTE_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_JOB', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_JOB (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_JOB PRIMARY KEY,
        RM_IDD int NULL CONSTRAINT FK_PM_JOB_PM_RUMAINTE FOREIGN KEY REFERENCES dbo.PM_RUMAINTE (IDD),
        AS_IDD int NOT NULL,
        JO_JOB int NOT NULL,
        JO_RSANGESH int NULL,
        JO_PERIOD int NULL,
        JO_MEGHDAR int NULL,
        JO_PERSON nvarchar(50) NOT NULL,
        JO_STDATE int NOT NULL,
        JO_STTIME int NOT NULL,
        JO_DONE int NOT NULL CONSTRAINT DF_PM_JOB_JO_DONE DEFAULT 0,
        JO_ENDATE int NULL,
        JO_ENTIME int NULL,
        JO_HESAB_HAZINE nvarchar(50) NULL,
        JO_LOCATION int NULL,
        JO_TAKHIRMO int NULL CONSTRAINT DF_PM_JOB_JO_TAKHIRMO DEFAULT 0,
        JO_TAKHVAHED int NULL,
        JO_ELAMBE nvarchar(50) NULL,
        JO_TIMENEED int NULL,
        JO_KIND int NULL CONSTRAINT DF_PM_JOB_JO_KIND DEFAULT 0,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_JOB_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_MVAD', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_MVAD (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_MVAD PRIMARY KEY,
        JOB_IDD int NOT NULL CONSTRAINT FK_PM_MVAD_PM_MVAD FOREIGN KEY REFERENCES dbo.PM_JOB (IDD),
        MV_ANBAR int NOT NULL,
        MV_CODE nvarchar(15) NOT NULL,
        MV_MEGH float NOT NULL CONSTRAINT DF_PM_MVAD_MV_MEGH DEFAULT 0,
        MV_MEGHk float NOT NULL CONSTRAINT DF_PM_MVAD_MV_MEGHk DEFAULT 0,
        MV_VAHED_K int NOT NULL,
        MV_DESCRIPTION nvarchar(100) NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_MVAD_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_MVT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_MVT (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MVT PRIMARY KEY,
        RM_IDD int NOT NULL,
        MV_CODE nvarchar(15) NOT NULL,
        MV_MEGH float NOT NULL CONSTRAINT DF_PM_MVT_MV_MEGH DEFAULT 0,
        MV_MEGHk float NOT NULL CONSTRAINT DF_PM_MVT_MV_MEGHk DEFAULT 0,
        MV_VAHED_K int NOT NULL,
        MV_DESCRIPTION nvarchar(100) NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_MVT_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.PM_EM_FAILURE_EVENTS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PM_EM_FAILURE_EVENTS (
        IDD int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PM_EM_FAILURE_EVENTS PRIMARY KEY,
        FI_IDD int NOT NULL CONSTRAINT FK_PM_EM_FAILURE_EVENTS_PM_EM_FAILURE FOREIGN KEY REFERENCES dbo.PM_EM_FAILURE (IDD),
        FIO_JOB int NULL CONSTRAINT FK_PM_EM_FAILURE_EVENTS_PM_JOBTITLE FOREIGN KEY REFERENCES dbo.PM_JOBTITLE (IDD),
        AS_IDD int NOT NULL CONSTRAINT FK_PM_EM_FAILURE_EVENTS_PM_ASSETS FOREIGN KEY REFERENCES dbo.PM_ASSETS (IDD),
        FIO_DATE int NOT NULL,
        FIO_TIME int NOT NULL,
        FIO_PERIO int NOT NULL CONSTRAINT DF_PM_EM_FAILURE_EVENTS_FIO_PERIO DEFAULT 0,
        FIO_AS_VAS int NOT NULL,
        FIO_DESCRIPTION nvarchar(500) NULL,
        FIO_ZAMEM image NULL,
        FIO_EVKIND int NOT NULL,
        FIO_JOBIDD int NULL,
        SGN1 bit NULL CONSTRAINT DF_PM_EM_FAILURE_EVENTS_SGN1 DEFAULT 0,
        SGN2 bit NULL CONSTRAINT DF_PM_EM_FAILURE_EVENTS_SGN2 DEFAULT 0,
        SGN3 bit NULL CONSTRAINT DF_PM_EM_FAILURE_EVENTS_SGN3 DEFAULT 0,
        SGN4 bit NULL CONSTRAINT DF_PM_EM_FAILURE_EVENTS_SGN4 DEFAULT 0,
        sgn1usid int NULL,
        sgn2usid int NULL,
        sgn3usid int NULL,
        CR datetime NULL,
        US int NULL,
        CRT datetime NULL CONSTRAINT DF_PM_EM_FAILURE_EVENTS_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

/* ── جدول‌های سفیر ── */
IF OBJECT_ID(N'dbo.CMMS_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_CONFIG (
        [KEY] nvarchar(40) NOT NULL CONSTRAINT PK_CMMS_CONFIG PRIMARY KEY,
        [VALUE] nvarchar(200) NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.CMMS_CLASS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_CLASS (
        ID int NOT NULL CONSTRAINT PK_CMMS_CLASS PRIMARY KEY,
        NAME nvarchar(100) NOT NULL,
        SORT int NOT NULL CONSTRAINT DF_CMMS_CLASS_SORT DEFAULT 0,
        CRT datetime NOT NULL CONSTRAINT DF_CMMS_CLASS_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.CMMS_ASSET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_ASSET (
        AS_IDD int NOT NULL CONSTRAINT PK_CMMS_ASSET PRIMARY KEY
            CONSTRAINT FK_CMMS_ASSET_PM_ASSETS FOREIGN KEY REFERENCES dbo.PM_ASSETS (IDD) ON DELETE CASCADE,
        CRITICALITY tinyint NOT NULL CONSTRAINT DF_CMMS_ASSET_CRITICALITY DEFAULT 2,
        MAKER nvarchar(100) NULL,
        MAKE_YEAR int NULL,
        INSTALL_DATE int NULL,
        FOOD_CONTACT bit NOT NULL CONSTRAINT DF_CMMS_ASSET_FOOD_CONTACT DEFAULT 0,
        SAFETY_CRITICAL bit NOT NULL CONSTRAINT DF_CMMS_ASSET_SAFETY_CRITICAL DEFAULT 0,
        STATUTORY bit NOT NULL CONSTRAINT DF_CMMS_ASSET_STATUTORY DEFAULT 0,
        MEASURING bit NOT NULL CONSTRAINT DF_CMMS_ASSET_MEASURING DEFAULT 0,
        METER_UNIT nvarchar(30) NULL,
        DOWN_COST float NULL,
        SPECS nvarchar(2000) NULL,
        ENERGY nvarchar(500) NULL,
        NOTE nvarchar(1000) NULL,
        UPD datetime NULL,
        UPD_UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.CMMS_METER', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_METER (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CMMS_METER PRIMARY KEY,
        AS_IDD int NOT NULL CONSTRAINT FK_CMMS_METER_PM_ASSETS FOREIGN KEY REFERENCES dbo.PM_ASSETS (IDD) ON DELETE CASCADE,
        RDATE int NOT NULL,
        RTIME int NULL,
        [VALUE] float NOT NULL,
        NOTE nvarchar(200) NULL,
        UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_CMMS_METER_CRT DEFAULT GETDATE()
    );
    CREATE INDEX IX_CMMS_METER_ASSET ON dbo.CMMS_METER (AS_IDD, RDATE, ID);
END;
GO

IF OBJECT_ID(N'dbo.CMMS_PLAN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_PLAN (
        RM_IDD int NOT NULL CONSTRAINT PK_CMMS_PLAN PRIMARY KEY
            CONSTRAINT FK_CMMS_PLAN_PM_RUMAINTE FOREIGN KEY REFERENCES dbo.PM_RUMAINTE (IDD) ON DELETE CASCADE,
        KIND tinyint NOT NULL CONSTRAINT DF_CMMS_PLAN_KIND DEFAULT 2,
        TITLE nvarchar(200) NULL,
        ACTIVE bit NOT NULL CONSTRAINT DF_CMMS_PLAN_ACTIVE DEFAULT 1,
        FIXED bit NOT NULL CONSTRAINT DF_CMMS_PLAN_FIXED DEFAULT 0,
        LEAD_DAYS int NULL,
        PRIORITY tinyint NOT NULL CONSTRAINT DF_CMMS_PLAN_PRIORITY DEFAULT 3,
        HYGIENE bit NOT NULL CONSTRAINT DF_CMMS_PLAN_HYGIENE DEFAULT 0,
        LOTO bit NOT NULL CONSTRAINT DF_CMMS_PLAN_LOTO DEFAULT 0,
        PERMIT bit NOT NULL CONSTRAINT DF_CMMS_PLAN_PERMIT DEFAULT 0,
        FOOD_GRADE bit NOT NULL CONSTRAINT DF_CMMS_PLAN_FOOD_GRADE DEFAULT 0,
        AUTHORITY nvarchar(200) NULL,
        LEGAL_REF nvarchar(300) NULL,
        INSTRUCTIONS nvarchar(2000) NULL,
        LAST_DATE int NULL,
        LAST_METER float NULL,
        UPD datetime NULL,
        UPD_UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.CMMS_PLAN_STEP', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_PLAN_STEP (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CMMS_PLAN_STEP PRIMARY KEY,
        RM_IDD int NOT NULL CONSTRAINT FK_CMMS_PLAN_STEP_PM_RUMAINTE FOREIGN KEY REFERENCES dbo.PM_RUMAINTE (IDD) ON DELETE CASCADE,
        SEQ int NOT NULL,
        [TEXT] nvarchar(300) NOT NULL,
        KIND tinyint NOT NULL CONSTRAINT DF_CMMS_PLAN_STEP_KIND DEFAULT 0,
        UNIT nvarchar(30) NULL,
        MIN_V float NULL,
        MAX_V float NULL,
        REQUIRED bit NOT NULL CONSTRAINT DF_CMMS_PLAN_STEP_REQUIRED DEFAULT 1
    );
    CREATE INDEX IX_CMMS_PLAN_STEP_PLAN ON dbo.CMMS_PLAN_STEP (RM_IDD, SEQ);
END;
GO

IF OBJECT_ID(N'dbo.CMMS_WO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_WO (
        JO_IDD int NOT NULL CONSTRAINT PK_CMMS_WO PRIMARY KEY
            CONSTRAINT FK_CMMS_WO_PM_JOB FOREIGN KEY REFERENCES dbo.PM_JOB (IDD) ON DELETE CASCADE,
        STAGE tinyint NOT NULL CONSTRAINT DF_CMMS_WO_STAGE DEFAULT 1,
        [TYPE] tinyint NOT NULL CONSTRAINT DF_CMMS_WO_TYPE DEFAULT 2,
        PRIORITY tinyint NOT NULL CONSTRAINT DF_CMMS_WO_PRIORITY DEFAULT 3,
        TITLE nvarchar(200) NULL,
        DESCRIPTION nvarchar(2000) NULL,
        REQ_ID int NULL,
        DUE_DATE int NULL,
        PLAN_DUE int NULL,
        PLAN_METER float NULL,
        ACT_START datetime NULL,
        ACT_END datetime NULL,
        DOWN_MIN int NULL,
        FAIL_MECH nvarchar(10) NULL,
        FAIL_CAUSE nvarchar(10) NULL,
        DETECTION nvarchar(10) NULL,
        ACTIVITY nvarchar(10) NULL,
        WORK_DONE nvarchar(2000) NULL,
        TEMP_FIX bit NOT NULL CONSTRAINT DF_CMMS_WO_TEMP_FIX DEFAULT 0,
        FOLLOWUP_ID int NULL,
        PARENT_ID int NULL,
        LOTO_DONE bit NOT NULL CONSTRAINT DF_CMMS_WO_LOTO_DONE DEFAULT 0,
        PERMIT_NO nvarchar(50) NULL,
        HYG_STATE tinyint NOT NULL CONSTRAINT DF_CMMS_WO_HYG_STATE DEFAULT 0,
        HYG_UID int NULL,
        HYG_AT datetime NULL,
        HYG_NOTE nvarchar(500) NULL,
        CONTRACTOR nvarchar(50) NULL,
        CONTRACTOR_COST float NULL,
        CONTRACTOR_REF nvarchar(100) NULL,
        CAPEX bit NOT NULL CONSTRAINT DF_CMMS_WO_CAPEX DEFAULT 0,
        METER_VALUE float NULL,
        CAL_RESULT tinyint NULL,
        CERT_NO nvarchar(100) NULL,
        CERT_EXPIRY int NULL,
        IMPACT_NOTE nvarchar(1000) NULL,
        REQ_UID int NULL,
        DONE_UID int NULL,
        CLOSED_UID int NULL,
        CLOSED_AT datetime NULL,
        UPD datetime NULL,
        UPD_UID int NULL
    );
    CREATE INDEX IX_CMMS_WO_STAGE ON dbo.CMMS_WO (STAGE) INCLUDE (DUE_DATE, PRIORITY, [TYPE]);
END;
GO

IF OBJECT_ID(N'dbo.CMMS_WO_STEP', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_WO_STEP (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CMMS_WO_STEP PRIMARY KEY,
        JO_IDD int NOT NULL CONSTRAINT FK_CMMS_WO_STEP_PM_JOB FOREIGN KEY REFERENCES dbo.PM_JOB (IDD) ON DELETE CASCADE,
        SEQ int NOT NULL,
        [TEXT] nvarchar(300) NOT NULL,
        KIND tinyint NOT NULL CONSTRAINT DF_CMMS_WO_STEP_KIND DEFAULT 0,
        UNIT nvarchar(30) NULL,
        MIN_V float NULL,
        MAX_V float NULL,
        REQUIRED bit NOT NULL CONSTRAINT DF_CMMS_WO_STEP_REQUIRED DEFAULT 1,
        RESULT tinyint NULL,
        [VALUE] float NULL,
        NOTE nvarchar(300) NULL,
        DONE_UID int NULL,
        DONE_AT datetime NULL
    );
    CREATE INDEX IX_CMMS_WO_STEP_WO ON dbo.CMMS_WO_STEP (JO_IDD, SEQ);
END;
GO

IF OBJECT_ID(N'dbo.CMMS_WO_LABOR', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_WO_LABOR (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CMMS_WO_LABOR PRIMARY KEY,
        JO_IDD int NOT NULL CONSTRAINT FK_CMMS_WO_LABOR_PM_JOB FOREIGN KEY REFERENCES dbo.PM_JOB (IDD) ON DELETE CASCADE,
        PERSON_UID int NULL,
        PERSON nvarchar(100) NOT NULL,
        WDATE int NOT NULL,
        HOURS float NOT NULL,
        RATE float NULL,
        NOTE nvarchar(200) NULL,
        UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_CMMS_WO_LABOR_CRT DEFAULT GETDATE()
    );
    CREATE INDEX IX_CMMS_WO_LABOR_WO ON dbo.CMMS_WO_LABOR (JO_IDD);
END;
GO

IF OBJECT_ID(N'dbo.CMMS_WO_DOC', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_WO_DOC (
        JO_IDD int NOT NULL CONSTRAINT FK_CMMS_WO_DOC_PM_JOB FOREIGN KEY REFERENCES dbo.PM_JOB (IDD) ON DELETE CASCADE,
        TAG int NOT NULL,
        NUMBER float NOT NULL,
        UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_CMMS_WO_DOC_CRT DEFAULT GETDATE(),
        CONSTRAINT PK_CMMS_WO_DOC PRIMARY KEY (JO_IDD, TAG, NUMBER)
    );
END;
GO

IF OBJECT_ID(N'dbo.CMMS_REQ', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_REQ (
        EV_IDD int NOT NULL CONSTRAINT PK_CMMS_REQ PRIMARY KEY
            CONSTRAINT FK_CMMS_REQ_PM_EM_FAILURE_EVENTS FOREIGN KEY REFERENCES dbo.PM_EM_FAILURE_EVENTS (IDD) ON DELETE CASCADE,
        STATE tinyint NOT NULL CONSTRAINT DF_CMMS_REQ_STATE DEFAULT 0,
        PRIORITY tinyint NOT NULL CONSTRAINT DF_CMMS_REQ_PRIORITY DEFAULT 3,
        FOOD_SAFETY bit NOT NULL CONSTRAINT DF_CMMS_REQ_FOOD_SAFETY DEFAULT 0,
        SAFETY bit NOT NULL CONSTRAINT DF_CMMS_REQ_SAFETY DEFAULT 0,
        DECISION_NOTE nvarchar(500) NULL,
        REPORTER_UID int NULL,
        DECIDED_UID int NULL,
        DECIDED_AT datetime NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.CMMS_LOG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CMMS_LOG (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CMMS_LOG PRIMARY KEY,
        KIND tinyint NOT NULL,
        REF int NOT NULL,
        ACTION nvarchar(40) NOT NULL,
        NOTE nvarchar(1000) NULL,
        UID int NULL,
        USER_NAME nvarchar(100) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_CMMS_LOG_CRT DEFAULT GETDATE()
    );
    CREATE INDEX IX_CMMS_LOG_REF ON dbo.CMMS_LOG (KIND, REF, ID);
END;
GO

/* ── کلاس‌های پیش‌فرض (۱ و ۳ همان گروه‌های داده‌ی Access: تولید و آزمایشگاه) ── */
IF NOT EXISTS (SELECT 1 FROM dbo.CMMS_CLASS)
BEGIN
    INSERT INTO dbo.CMMS_CLASS (ID, NAME, SORT) VALUES
        (1, N'تجهیزات خط تولید', 1),
        (2, N'تأسیسات و تجهیزات جانبی', 2),
        (3, N'آزمایشگاه و ابزار اندازه‌گیری', 3),
        (4, N'سرمایش و سردخانه', 4),
        (5, N'دیگ بخار و مخازن تحت فشار', 5),
        (6, N'بالابر و حمل‌ونقل داخلی', 6),
        (7, N'برق، تابلو و مولد', 7),
        (8, N'ایمنی و آتش‌نشانی', 8),
        (9, N'ساختمان و ابنیه', 9),
        (10, N'اداری و فناوری اطلاعات', 10);
END;
GO

/* ── تنظیماتِ پیش‌فرض (فقط کلیدهای نبوده) ── */
INSERT INTO dbo.CMMS_CONFIG ([KEY], [VALUE])
SELECT v.K, v.V
FROM (VALUES
        (N'HYGIENE_ON', N'1'),
        (N'LEAD_DAYS', N'7'),
        (N'METER_LEAD_PCT', N'10'),
        (N'LABOR_RATE', N'0'),
        (N'CERT_WARN_DAYS', N'30'),
        (N'P1_DAYS', N'0'),
        (N'P2_DAYS', N'1'),
        (N'P3_DAYS', N'7'),
        (N'P4_DAYS', N'30')
     ) v (K, V)
WHERE NOT EXISTS (SELECT 1 FROM dbo.CMMS_CONFIG c WHERE c.[KEY] = v.K);
GO

/* ── فرم‌های دسترسی ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    DECLARE @Grp int = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'PMASSETS'), 12);
    DECLARE @F TABLE (N int, FORMNAME nvarchar(50), CAPTION nvarchar(100));
    INSERT INTO @F (N, FORMNAME, CAPTION) VALUES
        (1, N'PMASSETS', N'تعريف تجهيزات تعمير نگهداري CMMS'),
        (2, N'MDKAR', N'مديريت دستور کارها CMMS'),
        (3, N'PMLOCATION', N'تعريف محل استفاده دستگاه ها CMMS'),
        (4, N'PMPMEM', N'اعلام خرابي CMMS'),
        (5, N'PMKIND', N'تعريف نوع تعمير و نگهداري (Work) CMMS'),
        (6, N'PMCALEN', N'تقويم دستور کارها CMMS'),
        (7, N'PMDARKH', N'درخواست کالا از انبار CMMS'),
        (8, N'PMWORKOR', N'ليست دستور کارها CMMS'),
        (9, N'PMFAILDEF', N'تعريف نوع خرابي CMMS'),
        (10, N'PMHYGIENE', N'ترخیص بهداشتی تجهیزات پس از تعمیر CMMS'),
        (11, N'PMCONFIG', N'تنظیمات نگهداری و تعمیرات CMMS');

    DECLARE @n int = 1, @name nvarchar(50), @cap nvarchar(100);
    WHILE @n <= 11
    BEGIN
        SELECT @name = FORMNAME, @cap = CAPTION FROM @F WHERE N = @n;
        IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = @name)
            INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
            VALUES (@name, @cap, 3, @Grp, (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
        SET @n = @n + 1;
    END;
END
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @Mdkar int = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'MDKAR');

    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH, ISNULL(P.ON_, 0), ISNULL(P.ON_, 0), 0, 0, 0, GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    OUTER APPLY (SELECT TOP 1 CASE WHEN F.FORMNAME = N'PMCONFIG' AND S.RUN = 1 AND S.UPD = 1 THEN 1 ELSE 0 END ON_
                 FROM [dbo].[SAL_CHEK] S
                 WHERE S.USERCO = D.IDD AND S.[OBJECT] = @Mdkar) P
    WHERE F.FORMNAME IN (N'PMASSETS', N'MDKAR', N'PMLOCATION', N'PMPMEM', N'PMKIND', N'PMCALEN', N'PMDARKH', N'PMWORKOR',
                         N'PMFAILDEF', N'PMHYGIENE', N'PMCONFIG')
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END
GO

PRINT N'نگهداری و تعمیرات (CMMS): جدول‌های PM_* (اگر نبودند)، CMMS_* و فرم‌های PMHYGIENE و PMCONFIG آماده شد.';
GO";
    }
}
