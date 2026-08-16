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
        private static void SalaryScript(bool isCustomCall, SqlConnection db)
        {
            if (isCustomCall) //
            {
                // ===========================================================
                // 1. DDL — جداول، ویوها، فانکشن‌ها، تریگرها
                //    ایجاد می‌شوند اگر وجود ندارند؛ آپدیت اگر موجودند
                // ===========================================================
                string tablescript = @"
-- ================================================================
-- PAY2 — سیستم حقوق و دستمزد — نسخه v6.0
-- نرم‌افزار مستر کارکت
-- کد: PAY2-DB-006  |  تاریخ: ۱۴۰۴/۱۲/۲۱
-- ================================================================
-- ترتیب ایجاد جداول بر اساس وابستگی‌های FK طراحی شده است.
-- اجرای کامل این اسکریپت ساختار کامل سیستم را می‌سازد.
-- ================================================================

SET NOCOUNT ON;
GO

-- ================================================================
-- گروه A — پیکربندی سیستم
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_CONFIG', N'U') IS NULL
BEGIN

-- ── ۱. PAY2_CONFIG — تنظیمات مرکزی ─────────────────────────────

CREATE TABLE [dbo].[PAY2_CONFIG]
(
    [CFG_KEY]      NVARCHAR(80)   NOT NULL,                                        -- کلید یکتا تنظیم
    [CFG_VALUE]    NVARCHAR(500)  NOT NULL,                                        -- مقدار جاری
    [CFG_OPTIONS]  NVARCHAR(500)  NULL,                                            -- گزینه‌های مجاز با | (مثال '30|REAL')
    [CFG_DEFAULT]  NVARCHAR(500)  NOT NULL,                                        -- مقدار پیش‌فرض کارخانه
    [CFG_SECTION]  NVARCHAR(60)   NOT NULL,                                        -- گروه در UI تنظیمات
    [LABEL_FA]     NVARCHAR(200)  NOT NULL,                                        -- عنوان فارسی
    [DESC_FA]      NVARCHAR(1000) NULL,                                            -- توضیح کامل
    [OPT_LABELS]   NVARCHAR(500)  NULL,                                            -- عنوان فارسی هر گزینه با |
    [DATA_TYPE]    NVARCHAR(20)   NOT NULL CONSTRAINT DF_CFG_DT DEFAULT('TEXT'),   -- TEXT|INT|DECIMAL|BOOL|DATE
    [ACCESS_LEVEL] TINYINT        NOT NULL CONSTRAINT DF_CFG_AL DEFAULT(2),        -- 1=Super Admin | 2=Admin | 3=Payroll Manager
    [CHANGED_AT]   DATETIME       NULL,
    [CHANGED_BY]   INT            NULL,
    [CHANGE_NOTE]  NVARCHAR(300)  NULL,

    CONSTRAINT PK_PAY2_CONFIG PRIMARY KEY ([CFG_KEY])
);
END;
GO

-- ── ۲. PAY2_CONFIG_LOG — لاگ تغییرات تنظیمات ───────────────────
IF OBJECT_ID(N'dbo.PAY2_CONFIG_LOG', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_CONFIG_LOG]
(
    [LOG_ID]     INT           NOT NULL IDENTITY(1,1),
    [CFG_KEY]    NVARCHAR(80)  NOT NULL,
    [OLD_VALUE]  NVARCHAR(500) NULL,
    [NEW_VALUE]  NVARCHAR(500) NOT NULL,
    [CHANGED_BY] INT           NOT NULL,
    [CHANGED_AT] DATETIME      NOT NULL CONSTRAINT DF_CFL_DT DEFAULT(GETDATE()),
    [REASON]     NVARCHAR(300) NULL,

    CONSTRAINT PK_PAY2_CONFIG_LOG PRIMARY KEY ([LOG_ID])
);
END;
GO

-- ── Trigger — لاگ خودکار تغییرات PAY2_CONFIG ───────────────────

CREATE OR ALTER TRIGGER [dbo].[TR_PAY2_CONFIG_LOG]
ON [dbo].[PAY2_CONFIG]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO PAY2_CONFIG_LOG
        (CFG_KEY, OLD_VALUE, NEW_VALUE, CHANGED_BY, CHANGED_AT, REASON)
    SELECT
        i.CFG_KEY,
        d.CFG_VALUE,
        i.CFG_VALUE,
        ISNULL(i.CHANGED_BY, 0),
        GETDATE(),
        i.CHANGE_NOTE
    FROM INSERTED i
    INNER JOIN DELETED d ON i.CFG_KEY = d.CFG_KEY
    WHERE i.CFG_VALUE <> d.CFG_VALUE;
END;
GO

-- ── ۳. PAY2_TAX_BRACKET — جدول مالیات پلکانی ───────────────────
IF OBJECT_ID(N'dbo.PAY2_TAX_BRACKET', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_TAX_BRACKET]
(
    [BRK_ID]     INT           NOT NULL IDENTITY(1,1),
    [TAX_YEAR]   SMALLINT      NOT NULL,                                       -- سال شمسی (مثال: 1403)
    [UPPER_LIMIT] BIGINT       NOT NULL,                                       -- سقف سالانه این پله (ریال) — NULL=پله آخر
    [RATE_PCT]   DECIMAL(5,2)  NOT NULL,                                       -- نرخ این پله (درصد)
    [FIXED_TAX]  BIGINT        NOT NULL CONSTRAINT DF_BRK_FT DEFAULT(0),      -- مالیات ثابت پله‌های قبل (برای سرعت محاسبه)
    [SORT_ORDER] SMALLINT      NOT NULL,

    CONSTRAINT PK_PAY2_TAX_BRACKET PRIMARY KEY ([BRK_ID]),
    CONSTRAINT UQ_BRK UNIQUE ([TAX_YEAR], [SORT_ORDER])
);
END;
GO

-- نمونه داده سال ۱۴۰۳
IF NOT EXISTS (SELECT 1 FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = 1403)
BEGIN
INSERT INTO PAY2_TAX_BRACKET (TAX_YEAR, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER) VALUES
(1403, 1800000000, 10,   0,         1),
(1403, 2700000000, 15,   180000000, 2),
(1403, 3600000000, 20,   315000000, 3),
(1403, 4800000000, 25,   495000000, 4),
(1403, 9999999999, 30,   795000000, 5);
END;
GO

-- ── بارگذاری اولیه PAY2_CONFIG ──────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM PAY2_CONFIG WHERE CFG_KEY = 'MONTH_DAYS_MODE')
BEGIN

INSERT INTO PAY2_CONFIG
    (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION,
     LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
VALUES
-- ─── محاسبه حقوق ───────────────────────────────────────────────
('MONTH_DAYS_MODE',      '30',            '30|REAL',                '30',            N'محاسبه',
 N'مبنای روزهای ماه',        N'30=ثابت ۳۰ روز | REAL=روز واقعی شمسی',
 N'۳۰ روز ثابت|روز واقعی ماه', 'TEXT', 2),

('MID_MONTH_PRORATE',    'CALENDAR',      'CALENDAR|EXACT',         'CALENDAR',      N'محاسبه',
 N'روش تغییر حکم وسط ماه',   N'CALENDAR=روز تقویمی | EXACT=نسبت دقیق',
 N'روز تقویمی|نسبت دقیق',    'TEXT', 2),

('OT_NORMAL_MULT',       '1.40',          NULL,                     '1.40',          N'محاسبه',
 N'ضریب اضافه‌کار عادی',      N'طبق ماده ۵۹ ق.ک ضریب ۱.۴۰ (۴۰٪ اضافه)',
 NULL, 'DECIMAL', 2),

('OT_HOLIDAY_MULT',      '1.40',          NULL,                     '1.40',          N'محاسبه',
 N'ضریب اضافه‌کار تعطیل',     N'طبق ماده ۶۲ ق.ک. برخی کارگاه‌ها بالاتر توافق می‌کنند',
 NULL, 'DECIMAL', 2),

('OT_HOUR_BASE',         '7.33',          NULL,                     '7.33',          N'محاسبه',
 N'ساعت کاری روزانه (مبنای نرخ ساعتی)', N'۷.۳۳ ساعت = ۴۴ ساعت هفتگی ÷ ۶',
 NULL, 'DECIMAL', 2),

('SHIFT_MODE',           'PCT',           'PCT|FIXED',              'PCT',           N'محاسبه',
 N'روش محاسبه حق شیفت',      N'PCT=درصدی از حقوق پایه | FIXED=مبلغ ثابت در حکم',
 N'درصدی|مبلغ ثابت',         'TEXT', 2),

('ROUND_MODE',           '1000',          '1|100|1000|10000',       '1000',          N'محاسبه',
 N'گرد کردن مبالغ (ریال)',    N'مبلغ خالص به نزدیک‌ترین مضرب این عدد گرد می‌شود',
 N'تومان|صدگان|هزارگان|ده‌هزارگان', 'INT', 2),

-- ─── بیمه ──────────────────────────────────────────────────────
('INS_WORKER_RATE',      '7.00',          NULL,                     '7.00',          N'بیمه',
 N'نرخ بیمه کارگر (درصد)',    NULL, NULL, 'DECIMAL', 1),

('INS_EMPLOYER_RATE',    '20.00',         NULL,                     '20.00',         N'بیمه',
 N'نرخ بیمه کارفرما — بدون بیمه بیکاری (درصد)', NULL, NULL, 'DECIMAL', 1),

('INS_UNEMP_RATE',       '3.00',          NULL,                     '3.00',          N'بیمه',
 N'نرخ بیمه بیکاری کارفرما (درصد) — برای غیرمدیران', NULL, NULL, 'DECIMAL', 1),

('INS_CEILING_APPLY',    '1',             '1|0',                    '1',             N'بیمه',
 N'اعمال سقف دستمزد مشمول بیمه', N'1=اعمال (قانونی) | 0=بدون سقف',
 N'اعمال سقف|بدون سقف',      'BOOL', 1),

('INS_CEILING_MONTHLY',  '126000000',     NULL,                     '126000000',     N'بیمه',
 N'سقف ماهیانه دستمزد مشمول بیمه (ریال)',
 N'هر سال با ابلاغ تأمین اجتماعی به‌روز شود', NULL, 'INT', 1),

('INS_EXEMPT_COUNT',     '5',             NULL,                     '5',             N'بیمه',
 N'تعداد کارگران معاف در تبصره ماده ۷',
 N'کارگاه‌هایی با ≤ این تعداد نفر از ۲۰٪ کارفرما معاف‌اند', NULL, 'INT', 1),

('INS_JANBAZ_RATE',      '0.18',          NULL,                     '0.18',          N'بیمه',
 N'نرخ بیمه کارفرما برای جانبازان',
 N'جانباز: ۱۸٪ (بدون ۳٪ بیکاری و بدون ۷٪ کارگر)', NULL, 'DECIMAL', 1),

('INS_TAB56_UNEMP',      '0',             '1|0',                    '0',             N'بیمه',
 N'آیا تبصره ۵۶ مشمول ۳٪ بیکاری هست؟',
 N'0=خیر (پیش‌فرض) | 1=بله',
 N'خیر|بله',                  'BOOL', 1),

-- ─── مالیات ────────────────────────────────────────────────────
('TAX_YEAR',             '1403',          NULL,                     '1403',          N'مالیات',
 N'سال مالیاتی جاری',         N'برای انتخاب ردیف‌های صحیح از PAY2_TAX_BRACKET',
 NULL, 'INT', 1),

('TAX_EXEMPT_MONTHLY',   '84000000',      NULL,                     '84000000',      N'مالیات',
 N'معافیت ماهیانه مالیاتی (ریال)', N'= معافیت سالانه ماده ۸۴ تقسیم بر ۱۲',
 NULL, 'INT', 1),

('TAX_DEDUCT_INS',       '1',             '1|0',                    '1',             N'مالیات',
 N'کسر سهم بیمه کارگر از مبنای مالیات', N'۱=بله (تبصره ۱ ماده ۸۶ ق.م.م)',
 N'بله — قانونی|خیر',         'BOOL', 2),

('TAX_DEPRIVATION_APPLY','1',             '1|0',                    '1',             N'مالیات',
 N'اعمال معافیت مناطق محروم', N'تا ۵۰٪ معافیت برای شاغلان مناطق محروم',
 N'اعمال|عدم اعمال',          'BOOL', 2),

-- ─── مساعده هوشمند ─────────────────────────────────────────────
-- منطق: SUM(BED-BES) از DEED_DTL
-- شرط: HES_K=ADV_HES_K AND HES_M=ADV_HES_M AND HES_T=PCODE
-- AND DEED_HED.N_S < N_S_سند_حقوق_جاری
-- AND ماه سند = ماه حقوق
-- ADV_HES_K و ADV_HES_M به PAY2_WORKSHOP_ACC منتقل شده‌اند (v6 — کارگاه‌محور)
('ADV_ENABLED',          '1',             '1|0',                    '1',             N'مساعده',
 N'آیا مساعده هوشمند فعال باشد؟',
 N'1=مانده حساب معین پرسنل از DEED_DTL محاسبه می‌شود | 0=بدون کسر مساعده',
 N'فعال (هوشمند)|غیرفعال',    'BOOL', 2),

('ADV_SCOPE',            'CURRENT_MONTH', 'CURRENT_MONTH|OPEN_BALANCE', 'CURRENT_MONTH', N'مساعده',
 N'محدوده محاسبه مساعده',
 N'CURRENT_MONTH=فقط اسناد همان ماه | OPEN_BALANCE=کل مانده باز تا سند حقوق',
 N'فقط ماه جاری|کل مانده باز','TEXT', 2),

('ADV_USE_HES_T_FILTER', '1',             '1|0',                    '1',             N'مساعده',
 N'آیا فیلتر HES_T (تفصیلی=کد پرسنل) اعمال شود؟',
 N'1=هر پرسنل فقط مانده حساب خودش (معمول) | 0=جمع کل معین بدون تفکیک تفصیلی',
 N'به تفکیک پرسنل|کل معین',   'BOOL', 2),

('ADV_MIN_POSITIVE',     '1',             '1|0',                    '1',             N'مساعده',
 N'مساعده فقط اگر مانده بدهکار (مثبت) باشد کسر شود',
 N'1=بله — اگر حساب بستانکار بود مساعده صفر در نظر گرفته می‌شود | 0=همیشه کسر',
 N'فقط بدهکار|همیشه',         'BOOL', 2),

-- ─── مرخصی ─────────────────────────────────────────────────────
('LEAVE_ANNUAL_DAYS',    '26',            '24|26|30',               '26',            N'مرخصی',
 N'روزهای مرخصی استحقاقی سالانه', N'ماده ۶۴ ق.ک',
 N'۲۴ روز|۲۶ روز|۳۰ روز',    'INT', 2),

('LEAVE_MINS_PER_DAY',   '440',           NULL,                     '440',           N'مرخصی',
 N'دقیقه معادل یک روز مرخصی',
 N'v6: ۴۴۰ دقیقه (طبق کارکرد سیستم قدیم) — مبنای LEAVE_BAL و تسویه مرخصی',
 NULL, 'INT', 2),

('LEAVE_CARRYOVER_MAX',  '9',             '0|9|999',                '9',             N'مرخصی',
 N'حداکثر روز انتقال مرخصی به سال بعد', N'ماده ۶۶ ق.ک — ۹ روز',
 N'ممنوع|۹ روز (قانون)|نامحدود', 'INT', 2),

('LEAVE_HOURLY_MAX_MINS', '200', NULL, '200', N'مرخصی', N'حداکثر زمان مرخصی ساعتی (دقیقه)', N'حداکثر دقایق مجاز برای ثبت در یک برگ مرخصی ساعتی (مثلاً ۲۰۰ دقیقه = ۳ ساعت و ۲۰ دقیقه)', NULL, 'INT', 2),
-- ─── تسویه حساب ────────────────────────────────────────────────
('BONUS_MODE',           'MIN_WAGE',      'MIN_WAGE|ACTUAL|CUSTOM',  'MIN_WAGE',     N'تسویه',
 N'مبنای محاسبه عیدی',
 N'MIN_WAGE=حداقل مزد ۶۰-۹۰ روز | ACTUAL=حقوق واقعی | CUSTOM=روز سفارشی',
 N'حداقل مزد|حقوق واقعی|سفارشی', 'TEXT', 2),

('BONUS_CUSTOM_DAYS',    '60',            NULL,                     '60',            N'تسویه',
 N'روز عیدی در حالت سفارشی',  NULL, NULL, 'INT', 2),

('MIN_WAGE_DAILY',       '73200',         NULL,                     '73200',         N'تسویه',
 N'حداقل دستمزد روزانه (ریال) طبق قانون کار',
 N'هر سال با ابلاغ شورای عالی کار به‌روز شود.',
 NULL, 'INT', 1),

('MIN_WAGE_MONTHLY',     '2196000',       NULL,                     '2196000',       N'تسویه',
 N'حداقل دستمزد ماهیانه (ریال) طبق قانون کار',
 N'= MIN_WAGE_DAILY × ۳۰. مبنای محاسبه عیدی در حالت MIN_WAGE.',
 NULL, 'INT', 1),

('EIDI_MIN_DAYS',        '60',            NULL,                     '60',            N'تسویه',
 N'حداقل روز برای محاسبه عیدی (قانون: ۶۰ روز)', NULL, NULL, 'INT', 1),

('EIDI_MAX_DAYS',        '90',            NULL,                     '90',            N'تسویه',
 N'حداکثر روز برای محاسبه عیدی (قانون: ۹۰ روز)', NULL, NULL, 'INT', 1),

('SENIORITY_MODE',       'LAST_SAL',      'LAST_SAL|DAILY|FIXED',   'LAST_SAL',      N'تسویه',
 N'مبنای حق سنوات',
 N'LAST_SAL=آخرین ماه×سال | DAILY=نرخ روزانه×۳۰×سال | FIXED=مبلغ ثابت per سال',
 N'آخرین حقوق|نرخ روزانه|مبلغ ثابت', 'TEXT', 2),

('SENIORITY_FIXED_AMT',  '0',             NULL,                     '0',             N'تسویه',
 N'مبلغ ثابت سنوات per سال (ریال) — فقط حالت FIXED', NULL, NULL, 'INT', 2),

-- ─── امنیت ─────────────────────────────────────────────────────
('ITEM_DEF_MIN_ROLE',    'ADMIN',         'SUPER|ADMIN|MGR',        'ADMIN',         N'امنیت',
 N'حداقل نقش برای تعریف آیتم حکم', NULL,
 N'فقط مدیر ارشد|ادمین|مدیر حقوق', 'TEXT', 1),

('CONFIG_MIN_ROLE',      'SUPER',         'SUPER|ADMIN',            'SUPER',         N'امنیت',
 N'حداقل نقش برای تغییر PAY2_CONFIG', NULL,
 N'فقط مدیر ارشد|ادمین',      'TEXT', 1);
END;
GO

-- ── افزودن کلید MONTHLY_ITEM_PRORATE برای دیتابیس‌های موجود (idempotent) ──
IF NOT EXISTS (SELECT 1 FROM PAY2_CONFIG WHERE CFG_KEY = 'MONTHLY_ITEM_PRORATE')
    INSERT INTO PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES ('MONTHLY_ITEM_PRORATE', '0', '1|0', '0', N'محاسبه',
            N'کسر آیتم‌های ماهیانه به‌نسبت غیبت',
            N'1=آیتم‌های ماهانه (حق تأهل/جذب/شرایط محیط کار/سایر ثابت) با غیبت کم می‌شوند | 0=کامل پرداخت می‌شوند',
            N'به‌نسبت کارکرد|کامل', 'BOOL', 2);
GO

-- ================================================================
-- گروه B — سازمان و کارگاه
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_WORKSHOP', N'U') IS NULL
BEGIN

-- ── ۴. PAY2_WORKSHOP — کارگاه‌ها ────────────────────────────────

CREATE TABLE [dbo].[PAY2_WORKSHOP]
(
    [WS_ID]           INT           NOT NULL IDENTITY(1,1),
    [WS_CODE]         NVARCHAR(20)  NOT NULL,                                    -- کد کارگاه (مرجع TAGCOD.CODE در صورت مهاجرت)
    [WS_NAME]         NVARCHAR(100) NOT NULL,
    [SHIFT_MODE]      NVARCHAR(10)  NULL,                                    -- نام کارگاه
    [EMPLOYER_NAME]   NVARCHAR(100) NULL,                                        -- نام کارفرما (فیلد جدید v6)
    [NATIONAL_ID]     NVARCHAR(11)  NULL,                                        -- شناسه ملی کارگاه
    [SOCIAL_INS_CODE] NVARCHAR(20)  NULL,                                        -- کد کارگاه نزد تأمین اجتماعی
    [TAX_CODE]        NVARCHAR(20)  NULL,                                        -- شناسه مالیاتی
    [POSTAL_CODE]     NVARCHAR(20)  NULL,                                        -- کد پستی کارگاه (فیلد جدید v6)
    [ADDRESS]         NVARCHAR(300) NULL,
    [PHONE]           NVARCHAR(30)  NULL,
    [INS_MODE]        TINYINT       NOT NULL CONSTRAINT DF_WS_INS DEFAULT(1),    -- 1=کارگاه معمولی (SANAD) | 2=تبصره ماده ۷ (SANAD10)
    [DEFAULT_DEED_MODE] TINYINT     NOT NULL CONSTRAINT DF_WS_DEED_MODE DEFAULT(1), -- روش پیش‌فرض صدور سند (1=خلاصه، 2=تفکیکی)
    [IS_ACTIVE]       BIT           NOT NULL CONSTRAINT DF_WS_ACT DEFAULT(1),
    [CREATED_AT]      DATETIME      NOT NULL CONSTRAINT DF_WS_CRT DEFAULT(GETDATE()),
    [CREATED_BY]      INT           NULL,

    CONSTRAINT PK_PAY2_WORKSHOP PRIMARY KEY ([WS_ID]),
    CONSTRAINT UQ_WS_CODE UNIQUE ([WS_CODE])
);
END;
GO

-- ── ۵. PAY2_WORKSHOP_ACC — سرفصل‌های حسابداری هر کارگاه ─────────
IF OBJECT_ID(N'dbo.PAY2_WORKSHOP_ACC', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_WORKSHOP_ACC]
(
    [WS_ID]    INT           NOT NULL,
    [ACC_KEY]  NVARCHAR(50)  NOT NULL,   -- SALARY_EXP | INS_EXP | SALARY_PAYABLE | INS_PAYABLE | TAX_PAYABLE | ADV_HES_K | ADV_HES_M
    [ACC_CODE] NVARCHAR(20)  NOT NULL,   -- کد سرفصل در سیستم حسابداری
    [ACC_DESC] NVARCHAR(100) NULL,

    CONSTRAINT PK_PAY2_WS_ACC PRIMARY KEY ([WS_ID], [ACC_KEY]),
    CONSTRAINT FK_WS_ACC FOREIGN KEY ([WS_ID]) REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ================================================================
-- گروه C — پرسنل
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_JOB', N'U') IS NULL
BEGIN

-- ── ۶. PAY2_JOB — جدول مشاغل ────────────────────────────────────

CREATE TABLE [dbo].[PAY2_JOB]
(
    [JOB_ID]    INT           NOT NULL IDENTITY(1,1),
    [JOB_CODE]  NVARCHAR(20)  NOT NULL,                                      -- کد شغل
    [JOB_NAME]  NVARCHAR(100) NOT NULL,                                      -- عنوان شغل (فارسی)
    [JOB_GROUP] NVARCHAR(50)  NULL,                                          -- گروه شغلی
    [IS_ACTIVE] BIT           NOT NULL CONSTRAINT DF_JOB_ACT DEFAULT(1),

    CONSTRAINT PK_PAY2_JOB PRIMARY KEY ([JOB_ID]),
    CONSTRAINT UQ_JOB_CODE UNIQUE ([JOB_CODE])
);
END;
GO

-- ── ۷. PAY2_EMPLOYEE — مشخصات پرسنل ────────────────────────────
IF OBJECT_ID(N'dbo.PAY2_EMPLOYEE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_EMPLOYEE]
(
    [EMP_ID]             INT           NOT NULL IDENTITY(1,1),
    [EMP_CODE]           NVARCHAR(20)  NOT NULL,                             -- کد یکتا (می‌تواند = CODE در PERSONEL قدیم)
    [WS_ID]              INT           NOT NULL,                             -- کارگاه اصلی

    -- مشخصات فردی
    [FIRST_NAME]         NVARCHAR(50)  NOT NULL,
    [LAST_NAME]          NVARCHAR(50)  NOT NULL,
    [FATHER_NAME]        NVARCHAR(50)  NULL,
    [NATIONAL_CODE]      NVARCHAR(10)  NULL,                                 -- v6: NULL مجاز (خارجی/موقت — filtered unique)
    [ID_NUMBER]          NVARCHAR(20)  NULL,                                 -- شماره شناسنامه
    [BIRTH_PLACE]        NVARCHAR(50)  NULL,                                 -- محل صدور شناسنامه
    [BIRTH_DATE]         BIGINT        NULL,                                 -- تاریخ تولد شمسی (YYYYMMDD)
    [GENDER]             TINYINT       NOT NULL CONSTRAINT DF_EMP_GND DEFAULT(1),  -- 1=مذکر، 2=مونث
    [NATIONALITY]        TINYINT       NOT NULL CONSTRAINT DF_EMP_NAT DEFAULT(1),  -- 1=ایرانی، 2=خارجی
    [IS_JANBAZ]          BIT           NOT NULL CONSTRAINT DF_EMP_JAN DEFAULT(0),  -- جانباز

    -- اشتغال
    [HIRE_DATE]          BIGINT        NOT NULL,                             -- تاریخ شروع به کار شمسی
    [FIRE_DATE]          BIGINT        NULL,                                 -- تاریخ ترک کار
    [JOB_ID]             INT           NULL,                                 -- شغل — FK به PAY2_JOB
    [UNIT]               TINYINT       NULL,                                 -- 1=تولید، 2=فروش، 3=خدمات، 4=اداری
    [EDU_LEVEL]          TINYINT       NULL,                                 -- مدرک تحصیلی
    [MARITAL]            TINYINT       NOT NULL CONSTRAINT DF_EMP_MAR DEFAULT(2),  -- 1=متأهل، 2=مجرد
    [IS_MANAGER]         BIT           NOT NULL CONSTRAINT DF_EMP_MGR DEFAULT(0),  -- مدیر — غیرمشمول ۳٪ بیمه بیکاری

    -- بیمه
    [INS_CODE]           NVARCHAR(15)  NULL,                                 -- شماره بیمه تأمین اجتماعی
    [INS_TYPE]           TINYINT       NOT NULL CONSTRAINT DF_EMP_INS DEFAULT(1),  -- 1=معمولی، 2=تبصره‌ای، 3=معاف از بیمه

    -- مالیات
    [TAX_EXEMPT]         BIT           NOT NULL CONSTRAINT DF_EMP_TEX DEFAULT(0),
    [REGION_DEPRIVATION] TINYINT       NOT NULL CONSTRAINT DF_EMP_DEP DEFAULT(0), -- ۰=عادی، یا درصد معافیت منطقه محروم (مثال ۵۰)

    -- ارتباط با حسابداری
    [ACC_T] NVARCHAR(50) NULL,                                 -- کد تفصیلی پرسنل در DEED_DTL.HES_T — مبنای مساعده هوشمند

    -- اطلاعات تماس و پرداخت
    [CARD_NO]            NVARCHAR(20)  NULL,                                 -- شماره کارت ساعت
    [MOBILE]             NVARCHAR(15)  NULL,
    [BANK_ACC]           NVARCHAR(30)  NULL,                                 -- شماره حساب بانکی
    [IBAN]               NVARCHAR(26)  NULL,                                 -- شماره شبا برای پرداخت الکترونیک

    -- وضعیت
    [IS_ACTIVE]          BIT           NOT NULL CONSTRAINT DF_EMP_ACT DEFAULT(1),
    [NOTES]              NVARCHAR(300) NULL,
    [CREATED_AT]         DATETIME      NOT NULL CONSTRAINT DF_EMP_CRT DEFAULT(GETDATE()),
    [CREATED_BY]         INT           NULL,

    CONSTRAINT PK_PAY2_EMPLOYEE  PRIMARY KEY ([EMP_ID]),
    CONSTRAINT UQ_EMP_CODE       UNIQUE ([EMP_CODE]),
    CONSTRAINT FK_EMP_WS         FOREIGN KEY ([WS_ID])  REFERENCES [PAY2_WORKSHOP]([WS_ID]),
    CONSTRAINT FK_EMP_JOB        FOREIGN KEY ([JOB_ID]) REFERENCES [PAY2_JOB]([JOB_ID])
);
END;
GO

-- v6: Filtered Unique Index برای کد ملی (NULL مجاز — برای خارجی‌ها)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_EMP_NATCODE' AND object_id = OBJECT_ID(N'dbo.PAY2_EMPLOYEE'))
CREATE UNIQUE INDEX UX_EMP_NATCODE
    ON PAY2_EMPLOYEE([NATIONAL_CODE])
    WHERE [NATIONAL_CODE] IS NOT NULL AND [NATIONAL_CODE] <> N'';
GO

-- ── ۸. PAY2_CONTRACT — قراردادها ────────────────────────────────
IF OBJECT_ID(N'dbo.PAY2_CONTRACT', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_CONTRACT]
(
    [CON_ID]       INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]       INT           NOT NULL,
    [CON_TYPE]     TINYINT       NOT NULL,                                   -- 1=دائم، 2=موقت، 3=پیمانی، 4=ساعتی
    [START_DATE]   BIGINT        NOT NULL,                                   -- شمسی
    [END_DATE]     BIGINT        NULL,                                       -- NULL=نامحدود
    [TRIAL_END]    BIGINT        NULL,                                       -- پایان دوره آزمایشی
    [WEEKLY_HOURS] DECIMAL(5,2)  NOT NULL CONSTRAINT DF_CON_WH DEFAULT(44),
    [NOTES]        NVARCHAR(200) NULL,
    [CREATED_AT]   DATETIME      NOT NULL CONSTRAINT DF_CON_CRT DEFAULT(GETDATE()),

    CONSTRAINT PK_PAY2_CONTRACT PRIMARY KEY ([CON_ID]),
    CONSTRAINT FK_CON_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ── ۹. PAY2_LEAVE_BAL — مانده مرخصی به دقیقه ───────────────────
IF OBJECT_ID(N'dbo.PAY2_LEAVE_BAL', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_LEAVE_BAL]
(
    [EMP_ID]           INT       NOT NULL,
    [YEAR]             SMALLINT  NOT NULL,                                   -- سال شمسی
    [ENTITLEMENT_MIN]  INT       NOT NULL CONSTRAINT DF_LB_ENT DEFAULT(11440), -- استحقاق سالانه (دقیقه) — ۲۶روز × ۴۴۰دق
    [USED_MIN]         INT       NOT NULL CONSTRAINT DF_LB_USD DEFAULT(0),    -- مجموع مرخصی‌های استفاده‌شده (دقیقه)
    [CARRIED_IN_MIN]   INT       NOT NULL CONSTRAINT DF_LB_CIN DEFAULT(0),    -- انتقالی از سال قبل — MAX: 9روز=3960دق
    [CARRIED_OUT_MIN]  INT       NOT NULL CONSTRAINT DF_LB_COU DEFAULT(0),    -- منتقل‌شده به سال بعد (دقیقه)

    -- ستون‌های محاسبه‌شده (نمایشی)
    [BALANCE_MIN]  AS ([ENTITLEMENT_MIN] + [CARRIED_IN_MIN] - [USED_MIN]),                        -- مانده کل به دقیقه
    [BALANCE_DAYS] AS (([ENTITLEMENT_MIN] + [CARRIED_IN_MIN] - [USED_MIN]) / 440),                -- مانده به روز (تقریبی)

    [UPDATED_AT] DATETIME NULL,

    CONSTRAINT PK_PAY2_LEAVE_BAL PRIMARY KEY ([EMP_ID], [YEAR]),
    CONSTRAINT FK_LB_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ================================================================
-- گروه D — تعریف آیتم‌های حقوق
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_ITEM_DEF', N'U') IS NULL
BEGIN

-- ── ۱۰. PAY2_ITEM_DEF — آیتم‌های پویا ──────────────────────────

CREATE TABLE [dbo].[PAY2_ITEM_DEF]
(
    [ITEM_ID]       INT           NOT NULL IDENTITY(1,1),
    [ITEM_CODE]     NVARCHAR(30)  NOT NULL,                                  -- کد یکتا: 'BASE_SAL','HOME','CHILDREN'...
    [ITEM_NAME]     NVARCHAR(100) NOT NULL,                                  -- نام فارسی برای فیش
    [ITEM_TYPE]     TINYINT       NOT NULL,
    -- 1=پرداختی ثابت، 2=پرداختی متغیر/کارکردی، 3=کسر ثابت، 4=کسر متغیر، 5=آگاهی(نمایش)
    [CALC_BASIS]    TINYINT       NOT NULL CONSTRAINT DF_ID_CB  DEFAULT(2),  -- 1=روزانه، 2=ماهیانه
    [INS_SUBJECT]   BIT           NOT NULL CONSTRAINT DF_ID_INS DEFAULT(1),  -- مشمول بیمه
    [TAX_SUBJECT]   BIT           NOT NULL CONSTRAINT DF_ID_TAX DEFAULT(1),  -- مشمول مالیات
    [INS_BASE_DAYS] TINYINT       NOT NULL CONSTRAINT DF_ID_IBD DEFAULT(1),  -- 1=DAYS (کارکرد اسمی) | 2=DAYSB (کارکرد رسمی)
    [PAY_BASE_DAYS] TINYINT       NOT NULL CONSTRAINT DF_ID_PBD DEFAULT(2),  -- 1=DAYS | 2=DAYSB — پرداخت همیشه رسمی
    [IS_SYSTEM]     BIT           NOT NULL CONSTRAINT DF_ID_SYS DEFAULT(0),  -- سیستمی؟ (حذف ممنوع)
    [SHOW_IN_SLIP]  BIT           NOT NULL CONSTRAINT DF_ID_SLP DEFAULT(1),
    -- شمارهٔ تفصیلیِ حسابِ هزینه در سند تفصیلی کامل (DEED_MODE=3). به ریشهٔ
    -- حسابِ مرکز هزینه چسبانده می‌شود: «711-1» + «2» → «711-1-2» (اضافه‌کار تولید).
    -- NULL یعنی این قلم هزینه‌ای تولید نمی‌کند (کسورات). پیش‌فرضِ اقلام جدید ۹=سایر است.
    [EXP_TAFSILI]   SMALLINT      NULL,
    [SORT_ORDER]    SMALLINT      NOT NULL CONSTRAINT DF_ID_SRT DEFAULT(100),
    [IS_ACTIVE]     BIT           NOT NULL CONSTRAINT DF_ID_ACT DEFAULT(1),
    [NOTES]         NVARCHAR(200) NULL,
    [CREATED_AT]    DATETIME      NOT NULL CONSTRAINT DF_ID_CRT DEFAULT(GETDATE()),
    [CREATED_BY]    INT           NULL,

    CONSTRAINT PK_PAY2_ITEM_DEF  PRIMARY KEY ([ITEM_ID]),
    CONSTRAINT UQ_ITEM_CODE      UNIQUE ([ITEM_CODE]),
    CONSTRAINT CK_ITEM_TYPE      CHECK ([ITEM_TYPE]   BETWEEN 1 AND 5),
    CONSTRAINT CK_CALC_BASIS     CHECK ([CALC_BASIS]  IN (1,2)),
    CONSTRAINT CK_INS_BASE_DAYS  CHECK ([INS_BASE_DAYS] IN (1,2)),
    CONSTRAINT CK_PAY_BASE_DAYS  CHECK ([PAY_BASE_DAYS] IN (1,2))
);
END;
GO

-- آیتم‌های سیستمی پیش‌فرض (IS_SYSTEM=1)
IF NOT EXISTS (SELECT 1 FROM PAY2_ITEM_DEF WHERE ITEM_CODE = 'BASE_SAL')
BEGIN
INSERT INTO PAY2_ITEM_DEF
    (ITEM_CODE, ITEM_NAME, ITEM_TYPE, CALC_BASIS, INS_SUBJECT, TAX_SUBJECT, INS_BASE_DAYS, PAY_BASE_DAYS, IS_SYSTEM, SORT_ORDER)
VALUES
-- عمداً EXP_TAFSILI اینجا نیست: این batch روی دیتابیسی هم اجرا می‌شود که جدول
-- را از قبل دارد ولی ستون را نه (ستون پایین‌تر با ALTER اضافه می‌شود). چون
-- SQL Server کل batch را پیش از اجرا کامپایل می‌کند، صرفِ نامِ آن ستون کافی
-- بود تا کل اسکریپت با «Invalid column name» رد شود — حتی با وجود IF NOT
-- EXISTS که جلوی اجرای INSERT را می‌گرفت. مقداردهی‌اش پایین‌تر و داخل EXEC است.
('BASE_SAL_B',  N'حقوق روزانه رسمی',        1, 1, 1, 1, 1, 2, 1, 1),   -- SALARY_DAYLYB
('BASE_SAL',    N'حقوق روزانه اسمی',         1, 1, 1, 1, 1, 2, 1, 2),   -- SALARY_DAYLY
('HOME',        N'خواربار و مسکن',           1, 1, 1, 1, 1, 2, 1, 3),   -- قانون ۲۸ روز
('CHILDREN',    N'حق اولاد',                 1, 1, 0, 1, 1, 2, 1, 4),   -- معاف بیمه، مشمول مالیات
('FAMILY_ALLOW',N'حق تأهل',                  1, 2, 1, 1, 1, 2, 1, 5),   -- ماهیانه
('ATTRACT',     N'حق جذب',                   1, 2, 1, 1, 1, 2, 1, 6),   -- ماهیانه
('GROCERY',     N'بن کارگری',                1, 1, 1, 0, 1, 2, 1, 7),   -- مشمول بیمه، معاف مالیات
('HARD_COND',   N'شرایط محیط کار',           1, 2, 1, 1, 1, 2, 1, 8),
('NAHAR',       N'حق نهار',                  1, 2, 0, 0, 2, 2, 1, 9),   -- معاف بیمه/مالیات
('SHIFT',       N'حق شیفت/نوبت/شب‌کاری',    1, 1, 0, 1, 1, 2, 1, 10),  -- درصد از BASE_SAL_B
('OTHER_FIX',   N'سایر ثابت',               1, 2, 1, 1, 1, 2, 1, 11),
('OT_NORMAL',   N'اضافه‌کار عادی',           2, 1, 1, 1, 1, 2, 1, 12),
('OT_HOLIDAY',  N'اضافه‌کار تعطیل',          2, 1, 1, 1, 1, 2, 1, 13),
('OT_ADMIN',    N'اضافه‌کار اداری',           2, 1, 1, 1, 1, 2, 1, 14),
('PERF_BONUS',  N'پاداش/راندمان',            2, 2, 1, 1, 1, 2, 1, 15),
('TRANSP',      N'حق ناقل/ایاب‌ذهاب',        2, 2, 0, 0, 1, 2, 1, 16),  -- معاف بیمه/مالیات
('INS_DED',     N'کسر بیمه کارگر',           4, 1, 0, 0, 1, 2, 1, 17),  -- خودکار
('TAX_DED',     N'کسر مالیات',              4, 1, 0, 0, 1, 2, 1, 18),  -- خودکار
('LOAN_DED',    N'قسط وام',                  3, 2, 0, 0, 1, 2, 1, 19),  -- از PAY2_LOAN_SCHED
('ADVANCE_DED', N'مساعده',                   4, 2, 0, 0, 1, 2, 1, 20),  -- هوشمند از DEED_DTL
('OTHER_DED',   N'سایر کسورات',             3, 2, 0, 0, 1, 2, 1, 21);
END;
GO

-- ── ۱۱. PAY2_ITEM_TEMPLATE — قالب‌های حکم ───────────────────────
IF OBJECT_ID(N'dbo.PAY2_ITEM_TEMPLATE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ITEM_TEMPLATE]
(
    [TMPL_ID]   INT           NOT NULL IDENTITY(1,1),
    [TMPL_CODE] NVARCHAR(30)  NOT NULL,
    [TMPL_NAME] NVARCHAR(100) NOT NULL,                                      -- مثال: 'کارگر تولید پایه'
    [WS_ID]     INT           NULL,                                          -- NULL=برای همه کارگاه‌ها
    [IS_ACTIVE] BIT           NOT NULL CONSTRAINT DF_TMPL_ACT DEFAULT(1),
    [NOTES]     NVARCHAR(200) NULL,

    CONSTRAINT PK_PAY2_TMPL    PRIMARY KEY ([TMPL_ID]),
    CONSTRAINT UQ_TMPL_CODE    UNIQUE ([TMPL_CODE]),
    CONSTRAINT FK_TMPL_WS      FOREIGN KEY ([WS_ID]) REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ── ۱۲. PAY2_ITEM_TMPL_LINE — آیتم‌های هر قالب ─────────────────
IF OBJECT_ID(N'dbo.PAY2_ITEM_TMPL_LINE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ITEM_TMPL_LINE]
(
    [TMPL_ID]   INT      NOT NULL,
    [ITEM_ID]   INT      NOT NULL,
    [DEF_AMOUNT] DECIMAL(18,2) NOT NULL CONSTRAINT DF_TL_AMT DEFAULT(0),
    [INS_OV]    BIT      NULL,                                               -- NULL=از تعریف آیتم
    [TAX_OV]    BIT      NULL,
    [BASIS_OV]  TINYINT  NULL,
    [SHIFT_MODE_OV] NVARCHAR(10) NULL,

    CONSTRAINT PK_PAY2_TMPL_LINE PRIMARY KEY ([TMPL_ID], [ITEM_ID]),
    CONSTRAINT FK_TL_TMPL FOREIGN KEY ([TMPL_ID]) REFERENCES [PAY2_ITEM_TEMPLATE]([TMPL_ID]) ON DELETE CASCADE,
    CONSTRAINT FK_TL_ITEM FOREIGN KEY ([ITEM_ID])  REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ================================================================
-- گروه E — احکام کارگزینی
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_DECREE', N'U') IS NULL
BEGIN

-- ── ۱۳. PAY2_DECREE — هدر احکام ────────────────────────────────

CREATE TABLE [dbo].[PAY2_DECREE]
(
    [DEC_ID]       INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]       INT           NOT NULL,
    [WS_ID]        INT           NOT NULL,
    [ISSUED_DATE]  BIGINT        NOT NULL,
    [SHIFT_MODE]   NVARCHAR(10)  NULL,                                   -- تاریخ صدور شمسی
    [EFF_FROM]     BIGINT        NOT NULL,                                   -- تاریخ شروع اجرا (شمسی)
    [EFF_TO]       BIGINT        NULL,                                       -- پایان اجرا (NULL=تا حکم بعدی)
    [EDU_LEVEL]    TINYINT       NULL,
    [MARITAL]      TINYINT       NULL,                                       -- تأهل در زمان این حکم
    [IS_MANAGER]   BIT           NULL,                                       -- مدیر در این حکم
    [TMPL_ID]      INT           NULL,                                       -- قالب استفاده‌شده
    [IS_CONFIRMED] BIT           NOT NULL CONSTRAINT DF_DEC_CON DEFAULT(0), -- تأیید نهایی؟
    [CONFIRMED_BY] INT           NULL,
    [CONFIRMED_AT] DATETIME      NULL,
    [NOTES]        NVARCHAR(300) NULL,
    [CREATED_AT]   DATETIME      NOT NULL CONSTRAINT DF_DEC_CRT DEFAULT(GETDATE()),
    [CREATED_BY]   INT           NULL,

    CONSTRAINT PK_PAY2_DECREE   PRIMARY KEY ([DEC_ID]),
    CONSTRAINT FK_DEC_EMP        FOREIGN KEY ([EMP_ID])  REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_DEC_WS         FOREIGN KEY ([WS_ID])   REFERENCES [PAY2_WORKSHOP]([WS_ID]),
    CONSTRAINT FK_DEC_TMPL       FOREIGN KEY ([TMPL_ID]) REFERENCES [PAY2_ITEM_TEMPLATE]([TMPL_ID])
);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_DEC_EMP_DATE' AND object_id = OBJECT_ID(N'dbo.PAY2_DECREE'))
CREATE NONCLUSTERED INDEX IX_DEC_EMP_DATE
    ON PAY2_DECREE ([EMP_ID], [EFF_FROM], [EFF_TO]);
GO

-- ── ۱۴. PAY2_DECREE_LINE — آیتم‌های هر حکم ─────────────────────
IF OBJECT_ID(N'dbo.PAY2_DECREE_LINE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_DECREE_LINE]
(
    [DEC_ID]   INT      NOT NULL,
    [ITEM_ID]  INT      NOT NULL,
    [AMOUNT]   DECIMAL(18,2) NOT NULL CONSTRAINT DF_DL_AMT DEFAULT(0),
    [NOMINAL_AMOUNT_OV] DECIMAL(18,2) NULL, -- فقط برای اقلام دو ریلی مانند SANOVAT_PAYE
    [OFFICIAL_AMOUNT_OV] DECIMAL(18,2) NULL,
    [INS_OV]   BIT      NULL,                                                -- NULL=از PAY2_ITEM_DEF
    [TAX_OV]   BIT      NULL,
    [BASIS_OV] TINYINT  NULL,
    [SHIFT_MODE_OV] NVARCHAR(10) NULL,

    CONSTRAINT PK_PAY2_DECREE_LINE PRIMARY KEY ([DEC_ID], [ITEM_ID]),
    CONSTRAINT FK_DL_DEC  FOREIGN KEY ([DEC_ID])  REFERENCES [PAY2_DECREE]([DEC_ID]) ON DELETE CASCADE,
    CONSTRAINT FK_DL_ITEM FOREIGN KEY ([ITEM_ID]) REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ── ۱۵. PAY2_OVERRIDE — استثناهای مشمولیت per پرسنل per آیتم ──
IF OBJECT_ID(N'dbo.PAY2_OVERRIDE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_OVERRIDE]
(
    [EMP_ID]     INT           NOT NULL,
    [ITEM_ID]    INT           NOT NULL,
    [INS_OV]     BIT           NULL,
    [TAX_OV]     BIT           NULL,
    [BASIS_OV]   TINYINT       NULL,
    [VALID_FROM] BIGINT        NOT NULL,
    [VALID_TO]   BIGINT        NULL,
    [REASON]     NVARCHAR(200) NULL,
    [CREATED_AT] DATETIME      NOT NULL CONSTRAINT DF_OV_CRT DEFAULT(GETDATE()),
    [CREATED_BY] INT           NULL,

    CONSTRAINT PK_PAY2_OVERRIDE PRIMARY KEY ([EMP_ID], [ITEM_ID], [VALID_FROM]),
    CONSTRAINT FK_OV_EMP  FOREIGN KEY ([EMP_ID])  REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_OV_ITEM FOREIGN KEY ([ITEM_ID]) REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ================================================================
-- گروه F — کارکرد ماهیانه
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_PERIOD', N'U') IS NULL
BEGIN

-- ── ۱۶. PAY2_PERIOD — دوره ماهیانه ─────────────────────────────

CREATE TABLE [dbo].[PAY2_PERIOD]
(
    [PER_ID]       INT           NOT NULL IDENTITY(1,1),
    [WS_ID]        INT           NOT NULL,
    [PERIOD_DATE]  BIGINT        NOT NULL,                                   -- تاریخ ماه شمسی (YYYYMM00)
    [HOLIDAY_DAYS] TINYINT       NOT NULL CONSTRAINT DF_PER_HD DEFAULT(0),  -- تعداد روزهای تعطیل رسمی این ماه
    [TENDAR_APPLY] BIT           NOT NULL CONSTRAINT DF_PER_TEN DEFAULT(0), -- ده‌درصدی: کسر ۱۰٪ این ماه؟
    [DEED_N_S_PAY] FLOAT         NULL,                                       -- شماره سند پرداخت صادرشده (از DEED_HED)
    [STATUS]       TINYINT       NOT NULL CONSTRAINT DF_PER_ST DEFAULT(1),  -- 1=باز، 2=بسته، 3=محاسبه‌شده، 4=سند صادر شده
    [OPENED_AT]    DATETIME      NOT NULL CONSTRAINT DF_PER_OA DEFAULT(GETDATE()),
    [CLOSED_AT]    DATETIME      NULL,
    [NOTES]        NVARCHAR(200) NULL,

    CONSTRAINT PK_PAY2_PERIOD PRIMARY KEY ([PER_ID]),
    CONSTRAINT UQ_PERIOD       UNIQUE ([WS_ID], [PERIOD_DATE]),
    CONSTRAINT FK_PER_WS       FOREIGN KEY ([WS_ID]) REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ── ۱۷. PAY2_ATTENDANCE — کارکرد هر پرسنل در هر دوره ──────────
IF OBJECT_ID(N'dbo.PAY2_ATTENDANCE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ATTENDANCE]
(
    [PER_ID]         INT           NOT NULL,
    [EMP_ID]         INT           NOT NULL,

    -- روزهای کارکرد با تفکیک واحد
    [WORK_DAYS]      DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_WD  DEFAULT(0),   -- کل روزهای کارکرد
    [DAYS_TOLID]     DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DTL DEFAULT(0),   -- روز تولید
    [DAYS_EDARI]     DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DED DEFAULT(0),   -- روز اداری
    [DAYS_KHADAMAT]  DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DKH DEFAULT(0),   -- روز خدمات
    [DAYS_FOROSH]    DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DFR DEFAULT(0),   -- روز فروش

    -- اضافه‌کار
    [OT_NORMAL_H]    DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_OTN DEFAULT(0),   -- ساعت اضافه‌کار عادی
    [OT_HOLIDAY_H]   DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_OTH DEFAULT(0),   -- ساعت اضافه‌کار تعطیل
    [OT_ADMIN_H]     DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_OTA DEFAULT(0),   -- ساعت اضافه‌کار اداری
    [SHORTAGE_H]     DECIMAL(6,2)  NOT NULL CONSTRAINT DF_ATT_SHRT DEFAULT(0),  -- ساعت کسر کار (کسر با ضریب ۱ از نرخ ساعتی)

    -- غیبت و مرخصی
    [LEAVE_DAYS]     DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_LD  DEFAULT(0),   -- روز مرخصی
    [ABSENT_DAYS]    DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_AD  DEFAULT(0),   -- روز غیبت
    [MISSION_DAYS]   DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_MD  DEFAULT(0),   -- روز مأموریت

    -- کارکرد اسمی / رسمی (v6)
    [DAYS]           DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DAYS  DEFAULT(0), -- کارکرد اسمی: مبنای محاسبه پایه بیمه
    [DAYSB]          DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_DAYSB DEFAULT(0), -- کارکرد رسمی: مبنای پرداخت آیتم‌های روزانه
    [FRID_COUNT]     TINYINT       NOT NULL CONSTRAINT DF_ATT_FRID  DEFAULT(0), -- تعداد جمعه‌های ماه (برای نهار)
    [TDAYS]          DECIMAL(5,2)  NOT NULL CONSTRAINT DF_ATT_TDAYS DEFAULT(0), -- تعطیلات رسمی قابل جبران

    -- آیتم‌های ثابت کارکردی
    [PERF_AMOUNT]    BIGINT        NOT NULL CONSTRAINT DF_ATT_PF DEFAULT(0),    -- راندمان/پاداش عملکرد (ریال)
    [TRANSP_AMOUNT]  BIGINT        NOT NULL CONSTRAINT DF_ATT_TR DEFAULT(0),    -- حق ناقل/ایاب و ذهاب (ریال)
    [KASR_OTHER]     BIGINT        NOT NULL CONSTRAINT DF_ATT_KO DEFAULT(0),    -- سایر کسورات (ریال)

    -- وضعیت ورود
    [SOURCE]         TINYINT       NOT NULL CONSTRAINT DF_ATT_SRC DEFAULT(1),   -- 1=دستی، 2=ورود دستگاه، 3=اکسل
    [LOCKED]         BIT           NOT NULL CONSTRAINT DF_ATT_LCK DEFAULT(0),
    [CREATED_AT]     DATETIME      NOT NULL CONSTRAINT DF_ATT_CRT DEFAULT(GETDATE()),
    [CREATED_BY]     INT           NULL,

    CONSTRAINT PK_PAY2_ATT     PRIMARY KEY ([PER_ID], [EMP_ID]),
    CONSTRAINT FK_ATT_PER      FOREIGN KEY ([PER_ID]) REFERENCES [PAY2_PERIOD]([PER_ID]),
    CONSTRAINT FK_ATT_EMP      FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT CK_ATT_DAYS     CHECK ([DAYS_TOLID]+[DAYS_EDARI]+[DAYS_KHADAMAT]+[DAYS_FOROSH] <= [WORK_DAYS] + 0.01),
    CONSTRAINT CK_ATT_DAYSB    CHECK ([DAYSB] <= [WORK_DAYS] + 0.01)
);
END;
GO

-- ── ۱۸. PAY2_ATT_VALUE — مقادیر متغیر اضافی per آیتم ───────────
IF OBJECT_ID(N'dbo.PAY2_ATT_VALUE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_ATT_VALUE]
(
    [PER_ID]  INT    NOT NULL,
    [EMP_ID]  INT    NOT NULL,
    [ITEM_ID] INT    NOT NULL,
    [VALUE]   BIGINT NOT NULL CONSTRAINT DF_AV_VAL DEFAULT(0),

    CONSTRAINT PK_PAY2_ATT_VAL PRIMARY KEY ([PER_ID], [EMP_ID], [ITEM_ID]),
    CONSTRAINT FK_AV_ATT  FOREIGN KEY ([PER_ID], [EMP_ID]) REFERENCES [PAY2_ATTENDANCE]([PER_ID],[EMP_ID]) ON DELETE CASCADE,
    CONSTRAINT FK_AV_ITEM FOREIGN KEY ([ITEM_ID]) REFERENCES [PAY2_ITEM_DEF]([ITEM_ID])
);
END;
GO

-- ── ۱۹. PAY2_LEAVE — ثبت مرخصی ─────────────────────────────────
IF OBJECT_ID(N'dbo.PAY2_LEAVE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_LEAVE]
(
    [LEV_ID]       INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]       INT           NOT NULL,
    [LEV_TYPE]     TINYINT       NOT NULL,                                   -- 1=استحقاقی، 2=استعلاجی، 3=بدون حقوق، 4=زایمان، 5=مأموریت
    [REQUEST_DATE] BIGINT        NOT NULL,                                   -- تاریخ درخواست
    [START_DATE]   BIGINT        NOT NULL,                                   -- تاریخ شروع
    [END_DATE]     BIGINT        NOT NULL,                                   -- تاریخ پایان

    -- مقدار مرخصی (روز+ساعت+دقیقه)
    [REQ_DAYS]     SMALLINT      NOT NULL CONSTRAINT DF_LEV_RD DEFAULT(0),
    [REQ_HOURS]    TINYINT       NOT NULL CONSTRAINT DF_LEV_RH DEFAULT(0),
    [REQ_MINUTES]  TINYINT       NOT NULL CONSTRAINT DF_LEV_RM DEFAULT(0),
    [TOTAL_MINUTES] AS ([REQ_DAYS]*440 + [REQ_HOURS]*60 + [REQ_MINUTES]),   -- ۱ روز = ۴۴۰ دقیقه
    [BAL_BEFORE]   INT           NULL,                                       -- مانده مرخصی قبل از این برگه (دقیقه)

    [DESCRIPTION]  NVARCHAR(300) NULL,                                       -- توضیحات (ساعت ورود-خروج)

    -- ارجاع و تأیید
    [REFER_TO]     INT           NULL,                                       -- ارجاع به (کد پرسنل مدیر)
    [STATUS]       TINYINT       NOT NULL CONSTRAINT DF_LEV_ST DEFAULT(1),  -- 1=ثبت، 2=تأیید درخواست‌کننده، 3=تأیید مدیر واحد، 4=تأیید مدیرعامل
    [APV1_BY]      INT           NULL, [APV1_AT] DATETIME NULL,             -- درخواست‌کننده
    [APV2_BY]      INT           NULL, [APV2_AT] DATETIME NULL,             -- مدیر واحد
    [APV3_BY]      INT           NULL, [APV3_AT] DATETIME NULL,             -- مدیر عامل
    [CREATED_AT]   DATETIME      NOT NULL CONSTRAINT DF_LEV_CRT DEFAULT(GETDATE()),
    [CREATED_BY]   INT           NULL,

    CONSTRAINT PK_PAY2_LEAVE   PRIMARY KEY ([LEV_ID]),
    CONSTRAINT FK_LEV_EMP      FOREIGN KEY ([EMP_ID])   REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_LEV_REFER    FOREIGN KEY ([REFER_TO]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ================================================================
-- گروه G — وام پرسنل
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_LOAN', N'U') IS NULL
BEGIN

-- ── ۲۰. PAY2_LOAN — وام ─────────────────────────────────────────

CREATE TABLE [dbo].[PAY2_LOAN]
(
    [LOAN_ID]    INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]     INT           NOT NULL,
    [WS_ID]      INT           NOT NULL,
    [LOAN_TYPE]  TINYINT       NOT NULL CONSTRAINT DF_LN_TYP DEFAULT(1),    -- 1=قرض‌الحسنه، 2=رفاهی، 3=ضروری، 4=مسکن، 5=سایر
    [LOAN_DATE]  BIGINT        NOT NULL,                                     -- تاریخ اعطا شمسی
    [AMOUNT]     BIGINT        NOT NULL,                                     -- مبلغ کل وام (ریال)
    [INSTALLMENT] BIGINT       NOT NULL,                                     -- مبلغ هر قسط
    [TOTAL_INST] SMALLINT      NOT NULL,                                     -- تعداد کل اقساط
    [PAID_INST]  SMALLINT      NOT NULL CONSTRAINT DF_LN_PI DEFAULT(0),
    [FIRST_PAY]  BIGINT        NOT NULL,                                     -- ماه اولین بازپرداخت شمسی (YYYYMM00)
    [PURPOSE]    NVARCHAR(200) NULL,
    [IS_ACTIVE]  BIT           NOT NULL CONSTRAINT DF_LN_ACT DEFAULT(1),
    [CREATED_AT] DATETIME      NOT NULL CONSTRAINT DF_LN_CRT DEFAULT(GETDATE()),
    [CREATED_BY] INT           NULL,

    CONSTRAINT PK_PAY2_LOAN PRIMARY KEY ([LOAN_ID]),
    CONSTRAINT FK_LN_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_LN_WS  FOREIGN KEY ([WS_ID])  REFERENCES [PAY2_WORKSHOP]([WS_ID])
);
END;
GO

-- ── ۲۱. PAY2_LOAN_SCHED — جدول اقساط ──────────────────────────
IF OBJECT_ID(N'dbo.PAY2_LOAN_SCHED', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_LOAN_SCHED]
(
    [SCHED_ID]  INT      NOT NULL IDENTITY(1,1),
    [LOAN_ID]   INT      NOT NULL,
    [INST_NUM]  SMALLINT NOT NULL,                                           -- شماره قسط
    [DUE_PERIOD] BIGINT  NOT NULL,                                           -- ماه سررسید شمسی (YYYYMM00)
    [AMOUNT]    BIGINT   NOT NULL,                                           -- مبلغ این قسط
    [RUN_ID]    INT      NULL,                                               -- شناسه اجرای حقوقی که کسر شد
    [PAID_AT]   DATETIME NULL,                                               -- تاریخ پرداخت واقعی

    CONSTRAINT PK_PAY2_LOAN_SCHED PRIMARY KEY ([SCHED_ID]),
    CONSTRAINT UQ_LOAN_INST       UNIQUE ([LOAN_ID], [INST_NUM]),
    CONSTRAINT FK_LS_LOAN         FOREIGN KEY ([LOAN_ID]) REFERENCES [PAY2_LOAN]([LOAN_ID])
);
END;
GO

-- View مانده وام هر پرسنل
CREATE OR ALTER VIEW [dbo].[V_PAY2_LOAN_BALANCE] AS
SELECT
    L.EMP_ID,
    L.LOAN_ID,
    L.AMOUNT                            AS TOTAL_AMOUNT,
    L.PAID_INST * L.INSTALLMENT         AS TOTAL_PAID,
    L.AMOUNT - L.PAID_INST*L.INSTALLMENT AS BALANCE,
    L.INSTALLMENT                       AS NEXT_INSTALLMENT,
    (L.TOTAL_INST - L.PAID_INST)        AS REMAINING_INST
FROM PAY2_LOAN L
WHERE L.IS_ACTIVE = 1 AND L.PAID_INST < L.TOTAL_INST;
GO

-- ================================================================
-- گروه H — مساعده هوشمند از حسابداری
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_ADVANCE_EXCL', N'U') IS NULL
BEGIN

-- ── ۲۲. PAY2_ADVANCE_EXCL — استثناهای دستی مساعده ──────────────

CREATE TABLE [dbo].[PAY2_ADVANCE_EXCL]
(
    [EXCL_ID]     INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]      INT           NOT NULL,
    [PERIOD_DATE] BIGINT        NOT NULL,                                    -- ماه شمسی اعمال استثنا (YYYYMM00)
    [EXCL_AMOUNT] BIGINT        NOT NULL,                                    -- مبلغ کسر از مانده (ریال)
    [REASON]      NVARCHAR(300) NOT NULL,
    [DEED_N_S]    FLOAT         NULL,                                        -- شماره سند مرجع در DEED_HED
    [CREATED_AT]  DATETIME      NOT NULL CONSTRAINT DF_AE_CRT DEFAULT(GETDATE()),
    [CREATED_BY]  INT           NULL,
    [APPROVED_BY] INT           NULL,

    CONSTRAINT PK_PAY2_ADV_EXCL PRIMARY KEY ([EXCL_ID]),
    CONSTRAINT FK_AE_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- ── تابع کمکی: تبدیل تاریخ شمسی به ماه (مشابه Umonth سیستم قدیم) ─

CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_MONTH](@DATE BIGINT)
RETURNS INT
AS
BEGIN
    RETURN @DATE / 100  -- YYYYMM
END;
GO

IF OBJECT_ID(N'dbo.PAY2_RUN', N'U') IS NULL
BEGIN

-- ================================================================
-- گروه I — نتایج محاسبه حقوق
-- ================================================================

-- ── ۲۳. PAY2_RUN — هدر اجرا ─────────────────────────────────────

CREATE TABLE [dbo].[PAY2_RUN]
(
    [RUN_ID]     INT           NOT NULL IDENTITY(1,1),
    [PER_ID]     INT           NOT NULL,                                     -- دوره ماهیانه
    [RUN_NO]     SMALLINT      NOT NULL CONSTRAINT DF_RUN_NO DEFAULT(1),     -- شماره ترتیبی نسخه — v6
    [IS_LATEST]  BIT           NOT NULL CONSTRAINT DF_RUN_IL DEFAULT(1),     -- ۱=آخرین نسخه این دوره — v6
    [CALC_AT]    DATETIME      NOT NULL CONSTRAINT DF_RUN_CA DEFAULT(GETDATE()),
    [CALC_BY]    INT           NULL,
    [STATUS]     TINYINT       NOT NULL CONSTRAINT DF_RUN_ST DEFAULT(1),     -- 1=پیش‌نویس، 2=نهایی، 3=سند صادرشده
    [PREV_RUN_ID] INT          NULL,                                         -- ارجاع به نسخه قبلی — v6
    [PAYROLL_ENGINE_VERSION] SMALLINT NULL,                                  -- 3 = Snapshot کامل اتمی مبلغ/پرسنل/عنوان آیتم
    [WS_ID_SNAP] INT NULL,                                                   -- کارگاه مؤثر همین Run
    [DEED_ID_SAL] INT          NULL,                                         -- شماره سند حقوق در حسابداری
    [DEED_ID_INS] INT          NULL,                                         -- شماره سند بیمه
    [DEED_MODE]              TINYINT  NULL,                                  -- روش صدور سند این اجرا (1=خلاصه، 2=تفکیکی)
    [DEED_GENERATOR_VERSION] SMALLINT NULL,                                  -- نسخه موتور تولید سند
    [NOTES]      NVARCHAR(300) NULL,

    CONSTRAINT PK_PAY2_RUN          PRIMARY KEY ([RUN_ID]),
    CONSTRAINT UQ_RUN_PERIOD_NO     UNIQUE ([PER_ID], [RUN_NO]),             -- v6: UNIQUE روی (PER_ID, RUN_NO)
    CONSTRAINT FK_RUN_PER           FOREIGN KEY ([PER_ID])     REFERENCES [PAY2_PERIOD]([PER_ID]),
    CONSTRAINT FK_RUN_PREV          FOREIGN KEY ([PREV_RUN_ID]) REFERENCES [PAY2_RUN]([RUN_ID])
);
END;
GO

-- ── ۲۴. PAY2_RUN_LINE — نتیجه per پرسنل ─────────────────────────
IF OBJECT_ID(N'dbo.PAY2_RUN_LINE', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_RUN_LINE]
(
    [RUN_ID]               INT           NOT NULL,
    [EMP_ID]               INT           NOT NULL,
    [DEC_ID]               INT           NULL,                               -- حکم استفاده‌شده
    [WORK_DAYS]            DECIMAL(5,2)  NOT NULL,

    -- پرداختی‌ها
    [GROSS_PAY]            BIGINT        NOT NULL,                           -- ناخالص (کل پرداختی قبل از کسر)

    -- بیمه
    [INS_BASE]             BIGINT        NOT NULL,                           -- مبنای بیمه
    [INS_WORKER]           BIGINT        NOT NULL,                           -- کسر بیمه کارگر (۷٪)
    [INS_EMPLOYER]         BIGINT        NOT NULL,                           -- بیمه کارفرما (۲۳٪)

    -- مالیات
    [TAX_BASE]             BIGINT        NOT NULL,                           -- مبنای مالیات
    [TAX_AMOUNT]           BIGINT        NOT NULL,

    -- کسورات دیگر
    [LOAN_DED]             BIGINT        NOT NULL CONSTRAINT DF_RL_LD DEFAULT(0),
    [ADVANCE_DED]          BIGINT        NOT NULL CONSTRAINT DF_RL_AD DEFAULT(0), -- مساعده هوشمند
    [OTHER_DED]            BIGINT        NOT NULL CONSTRAINT DF_RL_OD DEFAULT(0),
    [TOTAL_DED]            BIGINT        NOT NULL,

    -- خالص
    [NET_PAY]              BIGINT        NOT NULL,                           -- خالص پرداختی

    -- اطلاعات فیش
    [LEAVE_BAL_DAYS]       DECIMAL(5,2)  NULL,                              -- مانده مرخصی (روز)
    [LOAN_BALANCE]         BIGINT        NULL,                              -- مانده وام
    [ADVANCE_BALANCE_SNAP] BIGINT        NULL,                              -- عکس مانده مساعده در لحظه محاسبه

    CONSTRAINT PK_PAY2_RUN_LINE PRIMARY KEY ([RUN_ID], [EMP_ID]),
    CONSTRAINT FK_RL_RUN FOREIGN KEY ([RUN_ID]) REFERENCES [PAY2_RUN]([RUN_ID]),
    CONSTRAINT FK_RL_EMP FOREIGN KEY ([EMP_ID]) REFERENCES [PAY2_EMPLOYEE]([EMP_ID])
);
END;
GO

-- مشخصات غیرقابل‌تغییر پرسنل و شغل در لحظه محاسبه Run
IF OBJECT_ID(N'dbo.PAY2_RUN_EMP_SNAPSHOT', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[PAY2_RUN_EMP_SNAPSHOT]
(
    [RUN_ID] INT NOT NULL, [EMP_ID] INT NOT NULL,
    [EMP_CODE] NVARCHAR(50) NULL, [FIRST_NAME] NVARCHAR(100) NULL, [LAST_NAME] NVARCHAR(100) NULL,
    [FATHER_NAME] NVARCHAR(100) NULL, [NATIONAL_CODE] NVARCHAR(20) NULL, [INS_CODE] NVARCHAR(30) NULL,
    [ID_NUMBER] NVARCHAR(30) NULL, [BIRTH_PLACE] NVARCHAR(100) NULL, [BIRTH_DATE] BIGINT NULL,
    [GENDER] TINYINT NULL, [INS_TYPE_SNAP] TINYINT NOT NULL, [TAX_EXEMPT_SNAP] BIT NOT NULL,
    [MARITAL_SNAP] TINYINT NULL, [NATIONALITY_SNAP] TINYINT NULL, [MOBILE] NVARCHAR(30) NULL,
    [JOB_CODE_SNAP] NVARCHAR(50) NULL, [JOB_NAME_SNAP] NVARCHAR(200) NULL,
    [HIRE_DATE_SNAP] BIGINT NULL, [FIRE_DATE_SNAP] BIGINT NULL,
    [IS_MANAGER_SNAP] BIT NULL, [IS_JANBAZ_SNAP] BIT NULL,
    [REGION_DEPRIVATION_SNAP] TINYINT NULL, [ACC_T_SNAP] NVARCHAR(50) NULL,
    CONSTRAINT PK_PAY2_RUN_EMP_SNAPSHOT PRIMARY KEY ([RUN_ID],[EMP_ID]),
    CONSTRAINT FK_PAY2_RES_LINE FOREIGN KEY ([RUN_ID],[EMP_ID])
        REFERENCES [dbo].[PAY2_RUN_LINE]([RUN_ID],[EMP_ID]) ON DELETE CASCADE
);
END;
GO

-- ── ۲۵. PAY2_RUN_DETAIL — ریز آیتمی (فیش تفصیلی و حسابرسی) ────
IF OBJECT_ID(N'dbo.PAY2_RUN_DETAIL', N'U') IS NULL
BEGIN

CREATE TABLE [dbo].[PAY2_RUN_DETAIL]
(
    [RUN_ID]      INT    NOT NULL,
    [EMP_ID]      INT    NOT NULL,
    [ITEM_ID]     INT    NOT NULL,
    [AMOUNT]      BIGINT NOT NULL,                                           -- مبلغ رسمی پرداختی
    [NOMINAL_AMOUNT] BIGINT NULL,                                             -- مبلغ اسمی Snapshot برای بیمه/مالیات
    [ITEM_CODE_SNAP] NVARCHAR(30) NULL,
    [ITEM_NAME_SNAP] NVARCHAR(200) NULL,
    [CALC_BASIS_SNAP] TINYINT NULL,
    [ITEM_TYPE_SNAP] TINYINT NULL,
    [INS_SUBJECT_AMOUNT] BIGINT NULL,                                      -- بخش مبلغ اسمی که در همان Run مشمول بیمه بوده
    [TAX_SUBJECT_AMOUNT] BIGINT NULL,                                      -- بخش مبلغ اسمی که در همان Run مشمول مالیات بوده
    [INS_SUBJECT] BIT    NOT NULL,                                           -- مشمول بیمه بود؟
    [TAX_SUBJECT] BIT    NOT NULL,                                           -- مشمول مالیات بود؟

    CONSTRAINT PK_PAY2_RUN_DETAIL PRIMARY KEY ([RUN_ID], [EMP_ID], [ITEM_ID]),
    CONSTRAINT FK_RD_LINE FOREIGN KEY ([RUN_ID], [EMP_ID])
        REFERENCES [PAY2_RUN_LINE]([RUN_ID], [EMP_ID]) ON DELETE CASCADE
);
END;
GO

-- View لیست بیمه
CREATE OR ALTER VIEW [dbo].[V_PAY2_BIMEH] AS
SELECT
    P.PERIOD_DATE,
    W.SOCIAL_INS_CODE,
    W.WS_NAME,
    W.EMPLOYER_NAME, -- اضافه شده به خروجی لیست بیمه
    W.POSTAL_CODE,   -- اضافه شده به خروجی لیست بیمه
    E.INS_CODE,
    E.NATIONAL_CODE,
    E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
    RL.WORK_DAYS,
    RL.INS_BASE,
    RL.INS_WORKER,
    RL.INS_EMPLOYER,
    RL.INS_BASE * 0.30 AS TOTAL_BIMEH,
    E.INS_TYPE
FROM PAY2_RUN_LINE RL
INNER JOIN PAY2_RUN      R  ON RL.RUN_ID = R.RUN_ID
INNER JOIN PAY2_PERIOD   P  ON R.PER_ID  = P.PER_ID
INNER JOIN PAY2_WORKSHOP W  ON P.WS_ID   = W.WS_ID
INNER JOIN PAY2_EMPLOYEE E  ON RL.EMP_ID = E.EMP_ID;
GO

-- تابع محاسبه مالیات پلکانی
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_CALC_TAX]
    (@ANNUAL_BASE BIGINT, @TAX_YEAR SMALLINT)
RETURNS BIGINT
AS
BEGIN
    DECLARE @TAX        BIGINT      = 0;
    DECLARE @PREV_LIMIT BIGINT      = 0;
    DECLARE @RATE       DECIMAL(5,2);
    DECLARE @LIMIT      BIGINT;
    DECLARE @FIXED      BIGINT;

    DECLARE cur CURSOR FOR
        SELECT UPPER_LIMIT, RATE_PCT, FIXED_TAX
        FROM PAY2_TAX_BRACKET
        WHERE TAX_YEAR = @TAX_YEAR
        ORDER BY SORT_ORDER;

    OPEN cur;
    FETCH NEXT FROM cur INTO @LIMIT, @RATE, @FIXED;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @ANNUAL_BASE <= @LIMIT
        BEGIN
            SET @TAX = @FIXED + CAST((@ANNUAL_BASE - @PREV_LIMIT) * @RATE / 100 AS BIGINT);
            BREAK;
        END;
        SET @PREV_LIMIT = @LIMIT;
        FETCH NEXT FROM cur INTO @LIMIT, @RATE, @FIXED;
    END;

    -- اگر از همه پله‌ها بیشتر بود: پله آخر اعمال شود
    IF @@FETCH_STATUS <> 0 AND @TAX = 0
        SET @TAX = @FIXED + CAST((@ANNUAL_BASE - @PREV_LIMIT) * @RATE / 100 AS BIGINT);

    CLOSE cur;
    DEALLOCATE cur;

    RETURN @TAX;  -- مالیات سالانه — موتور ÷12 می‌کند
END;
GO

-- ================================================================
-- گروه J — تسویه حساب پرسنل
-- ================================================================
IF OBJECT_ID(N'dbo.PAY2_SETTLEMENT', N'U') IS NULL
BEGIN

-- ── ۲۶. PAY2_SETTLEMENT — تسویه حساب ───────────────────────────

CREATE TABLE [dbo].[PAY2_SETTLEMENT]
(
    [SET_ID]           INT           NOT NULL IDENTITY(1,1),
    [EMP_ID]           INT           NOT NULL,
    [WS_ID]            INT           NOT NULL,

    -- اطلاعات زمانی
    [SETTLE_DATE]      BIGINT        NOT NULL,                               -- تاریخ تسویه شمسی
    [HIRE_DATE]        BIGINT        NOT NULL,                               -- تاریخ شروع به کار
    [END_DATE]         BIGINT        NOT NULL,                               -- تاریخ پایان کار
    [SENIORITY_DAYS]   INT           NOT NULL,                               -- سابقه خدمت (روز) — محاسبه‌شده
    [SENIORITY_YEARS]  DECIMAL(6,2)  NOT NULL,                               -- سابقه (سال — برای نمایش)

    -- مبنای محاسبه
    [LAST_SALARY]      BIGINT        NOT NULL,                               -- دستمزد مبنا (آخرین حکم)
    [LAST_DAILY]       BIGINT        NOT NULL,                               -- نرخ روزانه (برای محاسبه مرخصی)

    -- سابقه تسویه قبلی
    [PREV_SET_ID]         INT        NULL,                                   -- FK به تسویه قبلی
    [PREV_SENIORITY_DAYS] INT        NOT NULL CONSTRAINT DF_SET_PSD DEFAULT(0), -- سابقه حساب‌شده در تسویه قبلی

    -- مانده مرخصی
    [LEAVE_BAL_MIN]    INT           NOT NULL CONSTRAINT DF_SET_LBM DEFAULT(0),   -- دقیقه‌های مانده مرخصی (مبنای ۴۴۰ دق/روز) — v6
    [LEAVE_BAL_DAYS]   DECIMAL(5,2)  NOT NULL CONSTRAINT DF_SET_LBD DEFAULT(0),   -- مانده مرخصی (روز — برای نمایش)

    -- ستون‌های درآمد تسویه
    [EIDI]             BIGINT        NOT NULL CONSTRAINT DF_SET_EID DEFAULT(0),   -- عیدی (بر اساس BONUS_MODE)
    [BON]              BIGINT        NOT NULL CONSTRAINT DF_SET_BON DEFAULT(0),   -- بن کارگری
    [LEAVE_PAY]        BIGINT        NOT NULL CONSTRAINT DF_SET_LPY DEFAULT(0),   -- مانده مرخصی به ریال
    [SANAVAT]          BIGINT        NOT NULL CONSTRAINT DF_SET_SAN DEFAULT(0),   -- حق سنوات
    [PREV_CREDIT]      BIGINT        NOT NULL CONSTRAINT DF_SET_PCR DEFAULT(0),   -- بستانکاری قبلی
    [OTHER_INCOME]     BIGINT        NOT NULL CONSTRAINT DF_SET_OIN DEFAULT(0),   -- سایر درآمدها
    [TOTAL_INCOME] AS (EIDI + BON + LEAVE_PAY + SANAVAT + PREV_CREDIT + OTHER_INCOME), -- جمع درآمدها

    -- ستون‌های کسورات تسویه
    [PREV_DEBIT]       BIGINT        NOT NULL CONSTRAINT DF_SET_PDB DEFAULT(0),   -- بدهکاری قبلی
    [EIDI_TAX]         BIGINT        NOT NULL CONSTRAINT DF_SET_ETX DEFAULT(0),   -- مالیات عیدی
    [LOAN_BALANCE]     BIGINT        NOT NULL CONSTRAINT DF_SET_LBL DEFAULT(0),   -- مانده وام قابل کسر
    [OTHER_DED]        BIGINT        NOT NULL CONSTRAINT DF_SET_ODE DEFAULT(0),   -- سایر کسورات
    [TOTAL_DED]    AS (PREV_DEBIT + EIDI_TAX + LOAN_BALANCE + OTHER_DED),         -- جمع کسورات

    -- نتیجه
    [NET_SETTLE]   AS (EIDI + BON + LEAVE_PAY + SANAVAT + PREV_CREDIT + OTHER_INCOME
                       - PREV_DEBIT - EIDI_TAX - LOAN_BALANCE - OTHER_DED),      -- خالص تسویه

    -- وضعیت و اسناد
    [STATUS]           TINYINT       NOT NULL CONSTRAINT DF_SET_ST DEFAULT(1),    -- 1=پیش‌نویس، 2=نهایی، 3=سند صادر شده
    [DEED_N_S]         FLOAT         NULL,                                         -- شماره سند حسابداری تسویه
    [CALC_METHOD]      NVARCHAR(200) NULL,                                         -- روش محاسبه (برای حسابرسی — JSON)
    [NOTES]            NVARCHAR(300) NULL,
    [CREATED_AT]       DATETIME      NOT NULL CONSTRAINT DF_SET_CRT DEFAULT(GETDATE()),
    [CREATED_BY]       INT           NULL,
    [APPROVED_BY]      INT           NULL,
    [APPROVED_AT]      DATETIME      NULL,

    CONSTRAINT PK_PAY2_SETTLEMENT  PRIMARY KEY ([SET_ID]),
    CONSTRAINT FK_SET_EMP          FOREIGN KEY ([EMP_ID])      REFERENCES [PAY2_EMPLOYEE]([EMP_ID]),
    CONSTRAINT FK_SET_WS           FOREIGN KEY ([WS_ID])       REFERENCES [PAY2_WORKSHOP]([WS_ID]),
    CONSTRAINT FK_SET_PREV         FOREIGN KEY ([PREV_SET_ID]) REFERENCES [PAY2_SETTLEMENT]([SET_ID])
);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_SET_EMP' AND object_id = OBJECT_ID(N'dbo.PAY2_SETTLEMENT'))
CREATE NONCLUSTERED INDEX IX_SET_EMP
    ON PAY2_SETTLEMENT ([EMP_ID], [SETTLE_DATE]);
GO

-- ================================================================
-- پایان اسکریپت
-- ================================================================
-- خلاصه اشیاء ایجاد شده:
--
--  گروه A : PAY2_CONFIG, PAY2_CONFIG_LOG, TR_PAY2_CONFIG_LOG,
--            PAY2_TAX_BRACKET
--            + INSERT داده‌های اولیه PAY2_CONFIG و PAY2_TAX_BRACKET
--
--  گروه B : PAY2_WORKSHOP, PAY2_WORKSHOP_ACC
--
--  گروه C : PAY2_JOB, PAY2_EMPLOYEE (+ Filtered Index),
--            PAY2_CONTRACT, PAY2_LEAVE_BAL
--
--  گروه D : PAY2_ITEM_DEF (+ INSERT آیتم‌های سیستمی),
--            PAY2_ITEM_TEMPLATE, PAY2_ITEM_TMPL_LINE
--
--  گروه E : PAY2_DECREE (+ Index IX_DEC_EMP_DATE),
--            PAY2_DECREE_LINE, PAY2_OVERRIDE
--
--  گروه F : PAY2_PERIOD, PAY2_ATTENDANCE, PAY2_ATT_VALUE, PAY2_LEAVE
--
--  گروه G : PAY2_LOAN, PAY2_LOAN_SCHED, V_PAY2_LOAN_BALANCE
--
--  گروه H : PAY2_ADVANCE_EXCL, FN_PAY2_MONTH, SP_PAY2_GET_ADVANCES
--
--  گروه I : PAY2_RUN, PAY2_RUN_LINE, PAY2_RUN_DETAIL,
--            V_PAY2_BIMEH, FN_PAY2_CALC_TAX
--
--  گروه J : PAY2_SETTLEMENT (+ Index IX_SET_EMP)
--
-- جمع: ۲۱ جدول، ۲ View، ۲ Function، ۱ Stored Procedure، ۱ Trigger
-- ================================================================

-- Migration 010: Fix configurations for Shift Allowance
GO
";
                ExecuteBatches(db, tablescript);

                // ===========================================================
                // 2. Schema Updates (Idempotent)
                // ===========================================================
                string schemaUpdates = @"
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'POSTAL_CODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [POSTAL_CODE] NVARCHAR(20) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'EMPLOYER_NAME') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [EMPLOYER_NAME] NVARCHAR(100) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'PROVINCE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [PROVINCE] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'CITY') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [CITY] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'REGISTRATION_NUMBER') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [REGISTRATION_NUMBER] NVARCHAR(20) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'SSO_BRANCH') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [SSO_BRANCH] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'FINANCIAL_MANAGER') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [FINANCIAL_MANAGER] NVARCHAR(100) NULL;
                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'ADMIN_MANAGER') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [ADMIN_MANAGER] NVARCHAR(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PAY2_JOB_PERFORMANCE')
                    CREATE NONCLUSTERED INDEX IX_PAY2_JOB_PERFORMANCE ON [dbo].[PAY2_JOB] ([IS_ACTIVE], [JOB_NAME]) INCLUDE ([JOB_ID]);

                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_WS_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));
                IF COL_LENGTH('dbo.PAY2_DECREE', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_DEC_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));
                IF COL_LENGTH('dbo.PAY2_DECREE_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_DL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));
                IF COL_LENGTH('dbo.PAY2_ITEM_TMPL_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_TL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));

                IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'DEFAULT_DEED_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [DEFAULT_DEED_MODE] TINYINT NOT NULL CONSTRAINT DF_WS_DEED_MODE DEFAULT(1);
                IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_MODE] TINYINT NULL;
                IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_GENERATOR_VERSION') IS NULL
                    ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_GENERATOR_VERSION] SMALLINT NULL;

                -- رهاسازی محدودیت Basis=3
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CALC_BASIS' AND parent_object_id = OBJECT_ID(N'dbo.PAY2_ITEM_DEF') AND definition NOT LIKE '%(3)%')
                BEGIN
                    ALTER TABLE dbo.PAY2_ITEM_DEF DROP CONSTRAINT CK_CALC_BASIS;
                    ALTER TABLE dbo.PAY2_ITEM_DEF ADD CONSTRAINT CK_CALC_BASIS CHECK ([CALC_BASIS] IN (1,2,3));
                END;

                -- رهاسازی محدودیت 6 برای LEV_TYPE
                DECLARE @sql NVARCHAR(MAX) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(10)
                FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) = 'PAY2_LEAVE' AND definition LIKE '%LEV_TYPE%' AND definition NOT LIKE '%(6)%';
                IF LEN(@sql) > 0 EXEC sp_executesql @sql;

                -- رهاسازی محدودیت Basis=3 روی Overrides
                SET @sql = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(10)
                FROM sys.check_constraints WHERE OBJECT_NAME(parent_object_id) IN ('PAY2_DECREE_LINE', 'PAY2_OVERRIDE', 'PAY2_ITEM_TMPL_LINE') AND definition LIKE '%BASIS_OV%' AND definition NOT LIKE '%(3)%';
                IF LEN(@sql) > 0 EXEC sp_executesql @sql;
                ";
                db.Execute(schemaUpdates);

                // نصب زیرساخت Preview/Apply سنوات؛ بدون هیچ اعمال خودکار روی احکام.
                ExecuteBatchesTransactional(db, @"SET XACT_ABORT ON;

IF COL_LENGTH('dbo.PAY2_DECREE_LINE','NOMINAL_AMOUNT_OV') IS NULL ALTER TABLE dbo.PAY2_DECREE_LINE ADD NOMINAL_AMOUNT_OV DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.PAY2_DECREE_LINE','OFFICIAL_AMOUNT_OV') IS NULL ALTER TABLE dbo.PAY2_DECREE_LINE ADD OFFICIAL_AMOUNT_OV DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','NOMINAL_AMOUNT') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD NOMINAL_AMOUNT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','ITEM_CODE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD ITEM_CODE_SNAP NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','ITEM_NAME_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD ITEM_NAME_SNAP NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','CALC_BASIS_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD CALC_BASIS_SNAP TINYINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','ITEM_TYPE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD ITEM_TYPE_SNAP TINYINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','INS_SUBJECT_AMOUNT') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD INS_SUBJECT_AMOUNT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_DETAIL','TAX_SUBJECT_AMOUNT') IS NULL ALTER TABLE dbo.PAY2_RUN_DETAIL ADD TAX_SUBJECT_AMOUNT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_ATTENDANCE','SHORTAGE_H') IS NULL ALTER TABLE dbo.PAY2_ATTENDANCE ADD SHORTAGE_H DECIMAL(6,2) NOT NULL CONSTRAINT DF_ATT_SHRT DEFAULT(0);

-- نگاشت «قلم حکم ← شمارهٔ تفصیلیِ حساب هزینه» برای سند تفصیلی کامل (DEED_MODE=3).
IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NULL
    ALTER TABLE dbo.PAY2_ITEM_DEF ADD EXP_TAFSILI SMALLINT NULL;
GO

-- مقداردهی جدا از ALTER است و داخل EXEC: در یک batch نمی‌شود ستونی را که همان‌جا
-- ساخته شده ارجاع داد، چون SQL Server کلِ batch را پیش از اجرا کامپایل می‌کند و
-- کلِ بلوک — با تمام ALTERهای دیگرش — رد می‌شود.
--
-- شرطِ «فقط وقتی ستون تازه ساخته شد» نیست، چون روی دیتابیس تازه ستون از همان
-- CREATE TABLE وجود دارد و آن شرط هرگز برقرار نمی‌شد؛ نتیجه‌اش این بود که نگاشت
-- خالی می‌مانْد و همه‌ی هزینه‌ها روی یک تفصیلی («سایر») جمع می‌شد.
-- فیلترِ IS NULL تضمین می‌کند ویرایش‌های کاربر با اجرای دوباره بازنویسی نشود.
IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NOT NULL
EXEC(N'
    UPDATE I SET I.EXP_TAFSILI = V.T
    FROM dbo.PAY2_ITEM_DEF I
    INNER JOIN (VALUES
        (''BASE_SAL_B'',1),(''BASE_SAL'',1),(''SANOVAT_PAYE'',1),
        (''OT_NORMAL'',2),(''OT_HOLIDAY'',2),(''OT_ADMIN'',2),
        (''PERF_BONUS'',3),(''CHILDREN'',4),(''HOME'',5),
        (''ATTRACT'',9),(''HARD_COND'',9),(''NAHAR'',9),(''OTHER_FIX'',9),(''TRANSP'',9),
        (''SHIFT'',11),(''GROCERY'',12),(''FAMILY_ALLOW'',13)
    ) AS V(C,T) ON V.C = I.ITEM_CODE
    WHERE I.EXP_TAFSILI IS NULL;

    -- کسورات حساب هزینه ندارند.
    UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI = NULL
    WHERE ITEM_TYPE IN (3,4,5) AND EXP_TAFSILI IS NOT NULL;

    -- اقلامِ تعریف‌شده توسط کاربر روی «سایر» می‌نشینند تا بدون تنظیم اضافه کار کنند.
    UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI = 9
    WHERE ITEM_TYPE IN (1,2) AND EXP_TAFSILI IS NULL;');
IF COL_LENGTH('dbo.PAY2_RUN','PAYROLL_ENGINE_VERSION') IS NULL ALTER TABLE dbo.PAY2_RUN ADD PAYROLL_ENGINE_VERSION SMALLINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN','WS_ID_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN ADD WS_ID_SNAP INT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','NOMINAL_GROSS') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD NOMINAL_GROSS BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','NOMINAL_DAYS') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD NOMINAL_DAYS DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','INS_EMPLOYER_BASE') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD INS_EMPLOYER_BASE BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','INS_UNEMPLOYMENT') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD INS_UNEMPLOYMENT BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','ROUNDING_ADJ') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD ROUNDING_ADJ BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','HIRE_DATE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD HIRE_DATE_SNAP BIGINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_LINE','FIRE_DATE_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_LINE ADD FIRE_DATE_SNAP BIGINT NULL;
IF OBJECT_ID(N'dbo.PAY2_RUN_EMP_SNAPSHOT',N'U') IS NULL
 CREATE TABLE dbo.PAY2_RUN_EMP_SNAPSHOT
 (
  RUN_ID INT NOT NULL, EMP_ID INT NOT NULL,
  EMP_CODE NVARCHAR(50) NULL, FIRST_NAME NVARCHAR(100) NULL, LAST_NAME NVARCHAR(100) NULL,
  FATHER_NAME NVARCHAR(100) NULL, NATIONAL_CODE NVARCHAR(20) NULL, INS_CODE NVARCHAR(30) NULL,
  ID_NUMBER NVARCHAR(30) NULL, BIRTH_PLACE NVARCHAR(100) NULL, BIRTH_DATE BIGINT NULL,
  GENDER TINYINT NULL, INS_TYPE_SNAP TINYINT NOT NULL, TAX_EXEMPT_SNAP BIT NOT NULL,
  MARITAL_SNAP TINYINT NULL, NATIONALITY_SNAP TINYINT NULL, MOBILE NVARCHAR(30) NULL,
  JOB_CODE_SNAP NVARCHAR(50) NULL, JOB_NAME_SNAP NVARCHAR(200) NULL,
  HIRE_DATE_SNAP BIGINT NULL, FIRE_DATE_SNAP BIGINT NULL,
  IS_MANAGER_SNAP BIT NULL, IS_JANBAZ_SNAP BIT NULL,
  REGION_DEPRIVATION_SNAP TINYINT NULL, ACC_T_SNAP NVARCHAR(50) NULL,
  CONSTRAINT PK_PAY2_RUN_EMP_SNAPSHOT PRIMARY KEY(RUN_ID,EMP_ID),
  CONSTRAINT FK_PAY2_RES_LINE FOREIGN KEY(RUN_ID,EMP_ID) REFERENCES dbo.PAY2_RUN_LINE(RUN_ID,EMP_ID) ON DELETE CASCADE
 );
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','IS_MANAGER_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD IS_MANAGER_SNAP BIT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','IS_JANBAZ_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD IS_JANBAZ_SNAP BIT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','REGION_DEPRIVATION_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD REGION_DEPRIVATION_SNAP TINYINT NULL;
IF COL_LENGTH('dbo.PAY2_RUN_EMP_SNAPSHOT','ACC_T_SNAP') IS NULL ALTER TABLE dbo.PAY2_RUN_EMP_SNAPSHOT ADD ACC_T_SNAP NVARCHAR(50) NULL;
IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='SANOVAT_PAYE')
 INSERT dbo.PAY2_ITEM_DEF(ITEM_CODE,ITEM_NAME,ITEM_TYPE,CALC_BASIS,INS_SUBJECT,TAX_SUBJECT,INS_BASE_DAYS,PAY_BASE_DAYS,IS_SYSTEM,SHOW_IN_SLIP,SORT_ORDER,IS_ACTIVE,NOTES)
 VALUES('SANOVAT_PAYE',N'پایه سنوات روزانه',1,1,1,1,1,2,1,1,3,1,N'مبلغ روزانه دو ریلی؛ مشمولیت از Snapshot مؤثر Run خوانده می‌شود');

-- سنوات هزینه‌ی «حقوق» است (تفصیلی ۱). این آیتم پایین‌تر از بلوکِ نگاشت اولیه
-- ساخته می‌شود، پس آنجا هنوز وجود ندارد و باید صریحاً اینجا مقدار بگیرد.
--
-- شرط هم NULL را می‌گیرد و هم ۹: روی دیتابیس تازه، سنوات همین‌جا و بدون مقدار
-- درج می‌شود (ستون DEFAULT ندارد) و اگر فقط دنبال ۹ می‌گشتیم مقدارش NULL
-- می‌مانْد و هزینه‌ی سنوات به‌جای «حقوق» روی «سایر» می‌افتاد. اگر کاربر عمداً
-- تفصیلی دیگری گذاشته باشد، دست‌نخورده می‌مانَد.
IF COL_LENGTH('dbo.PAY2_ITEM_DEF','EXP_TAFSILI') IS NOT NULL
    EXEC(N'UPDATE dbo.PAY2_ITEM_DEF SET EXP_TAFSILI=1
           WHERE ITEM_CODE=''SANOVAT_PAYE'' AND (EXP_TAFSILI=9 OR EXP_TAFSILI IS NULL);');

IF OBJECT_ID(N'dbo.PAY2_SANOVAT_MIGRATION_INPUT',N'U') IS NULL
 CREATE TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT
 (
   DEC_ID INT NOT NULL CONSTRAINT PK_PAY2_SANOVAT_MIGRATION_INPUT PRIMARY KEY,
   SOURCE_RAIL NVARCHAR(10) NULL,
   NOMINAL_SENIORITY_DAILY DECIMAL(18,2) NULL,
   OFFICIAL_SENIORITY_DAILY DECIMAL(18,2) NULL,
   IS_APPROVED BIT NOT NULL CONSTRAINT DF_PAY2_SMI_APPROVED DEFAULT(0),
   SOURCE_NOTE NVARCHAR(300) NOT NULL,
   ENTERED_BY INT NULL,
   ENTERED_AT DATETIME2 NOT NULL CONSTRAINT DF_PAY2_SMI_AT DEFAULT(SYSDATETIME()),
   CONSTRAINT FK_PAY2_SMI_DEC FOREIGN KEY(DEC_ID) REFERENCES dbo.PAY2_DECREE(DEC_ID)
 );
ELSE
BEGIN
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','SENIORITY_DAILY_AMOUNT') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ALTER COLUMN SENIORITY_DAILY_AMOUNT DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','SOURCE_RAIL') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ADD SOURCE_RAIL NVARCHAR(10) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','NOMINAL_SENIORITY_DAILY') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ADD NOMINAL_SENIORITY_DAILY DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_INPUT','OFFICIAL_SENIORITY_DAILY') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_INPUT ADD OFFICIAL_SENIORITY_DAILY DECIMAL(18,2) NULL;
END;

IF OBJECT_ID(N'dbo.PAY2_SANOVAT_MIGRATION_LOG',N'U') IS NULL
 CREATE TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG
 (
   DEC_ID INT NOT NULL CONSTRAINT PK_PAY2_SANOVAT_MIGRATION_LOG PRIMARY KEY,
   NEW_DEC_ID INT NULL, EFFECTIVE_FROM BIGINT NULL,
   SOURCE_RAIL NVARCHAR(10) NOT NULL,
   NOMINAL_BASE_BEFORE DECIMAL(18,2) NULL, NOMINAL_SENIORITY DECIMAL(18,2) NOT NULL, NOMINAL_BASE_AFTER DECIMAL(18,2) NULL,
   OFFICIAL_BASE_BEFORE DECIMAL(18,2) NULL, OFFICIAL_SENIORITY DECIMAL(18,2) NOT NULL, OFFICIAL_BASE_AFTER DECIMAL(18,2) NULL,
   APPLIED_BY INT NULL, APPLIED_AT DATETIME2 NOT NULL CONSTRAINT DF_PAY2_SML_AT DEFAULT(SYSDATETIME()),
   CONSTRAINT FK_PAY2_SML_DEC FOREIGN KEY(DEC_ID) REFERENCES dbo.PAY2_DECREE(DEC_ID)
 );
ELSE
BEGIN
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','BASE_BEFORE') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ALTER COLUMN BASE_BEFORE DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','SENIORITY_AMOUNT') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ALTER COLUMN SENIORITY_AMOUNT DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','BASE_AFTER') IS NOT NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ALTER COLUMN BASE_AFTER DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','NEW_DEC_ID') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD NEW_DEC_ID INT NULL, EFFECTIVE_FROM BIGINT NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','SOURCE_RAIL') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD SOURCE_RAIL NVARCHAR(10) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','NOMINAL_BASE_BEFORE') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD NOMINAL_BASE_BEFORE DECIMAL(18,2) NULL, NOMINAL_SENIORITY DECIMAL(18,2) NULL, NOMINAL_BASE_AFTER DECIMAL(18,2) NULL;
 IF COL_LENGTH('dbo.PAY2_SANOVAT_MIGRATION_LOG','OFFICIAL_BASE_BEFORE') IS NULL ALTER TABLE dbo.PAY2_SANOVAT_MIGRATION_LOG ADD OFFICIAL_BASE_BEFORE DECIMAL(18,2) NULL, OFFICIAL_SENIORITY DECIMAL(18,2) NULL, OFFICIAL_BASE_AFTER DECIMAL(18,2) NULL;
END;

IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'INS_NON_SUBJECT_OPT_IN')
 INSERT dbo.PAY2_CONFIG(CFG_KEY,CFG_VALUE,CFG_OPTIONS,CFG_DEFAULT,CFG_SECTION,LABEL_FA,DESC_FA,OPT_LABELS,DATA_TYPE,ACCESS_LEVEL)
 VALUES(N'INS_NON_SUBJECT_OPT_IN',N'DISABLED',NULL,N'DISABLED',N'INSURANCE',N'مجوز تغییر مشمولیت شیفت و اضافه‌کار',
        N'فقط برای دیتابیس هدف، مقدار باید به APPROVED:<DatabaseName> تغییر کند؛ Updater هرگز آن را خودکار فعال نمی‌کند.',NULL,N'TEXT',1);
IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM')
 INSERT dbo.PAY2_CONFIG(CFG_KEY,CFG_VALUE,CFG_OPTIONS,CFG_DEFAULT,CFG_SECTION,LABEL_FA,DESC_FA,OPT_LABELS,DATA_TYPE,ACCESS_LEVEL)
 VALUES(N'INS_NON_SUBJECT_EFFECTIVE_FROM',N'0',NULL,N'0',N'INSURANCE',N'تاریخ اثر عدم مشمولیت شیفت و اضافه‌کار',
        N'تاریخ شمسی روز اول ماه؛ فقط Procedure تأییدشده Opt-in آن را مقداردهی می‌کند.',NULL,N'DATE',1);

IF OBJECT_ID(N'dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG',N'U') IS NULL
 CREATE TABLE dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG
 (
   LOG_ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PAY2_INS_NON_SUBJECT_OPTIN_LOG PRIMARY KEY,
   DATABASE_NAME SYSNAME NOT NULL,
   ITEM_CODE NVARCHAR(30) NOT NULL,
   EFFECTIVE_FROM BIGINT NOT NULL,
   OLD_INS_SUBJECT BIT NOT NULL,
   NEW_INS_SUBJECT BIT NOT NULL,
   TAX_SUBJECT_SNAPSHOT BIT NOT NULL,
   APPLIED_BY INT NOT NULL,
   APPLIED_AT DATETIME2 NOT NULL CONSTRAINT DF_PAY2_INS_OPTIN_AT DEFAULT(SYSDATETIME())
 );
ELSE IF COL_LENGTH('dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG','EFFECTIVE_FROM') IS NULL
 ALTER TABLE dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG ADD EFFECTIVE_FROM BIGINT NOT NULL CONSTRAINT DF_PAY2_INS_OPTIN_EFF DEFAULT(0) WITH VALUES;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_PREVIEW_SANOVAT_MIGRATION @EFFECTIVE_FROM BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12
  THROW 51010,N'تاریخ اثر باید تاریخ شمسی معتبر در روز اول ماه (سال 1300 تا 1600 و ماه 1 تا 12) باشد.',1;
 DECLARE @ItemId INT=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='SANOVAT_PAYE');
 SELECT D.DEC_ID,D.EMP_ID,E.EMP_CODE,E.LAST_NAME+N' '+E.FIRST_NAME FULL_NAME,D.EFF_FROM,D.EFF_TO,@EFFECTIVE_FROM NEW_EFFECTIVE_FROM,
   I.SOURCE_RAIL,I.NOMINAL_SENIORITY_DAILY,I.OFFICIAL_SENIORITY_DAILY,I.IS_APPROVED,I.SOURCE_NOTE,
   BN.AMOUNT NOMINAL_BASE_BEFORE,
   CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN BN.AMOUNT-I.NOMINAL_SENIORITY_DAILY ELSE BN.AMOUNT END NOMINAL_BASE_AFTER,
   CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN BN.AMOUNT-I.NOMINAL_SENIORITY_DAILY+I.NOMINAL_SENIORITY_DAILY ELSE BN.AMOUNT END NOMINAL_TOTAL_AFTER,
   BO.AMOUNT OFFICIAL_BASE_BEFORE,
   CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN BO.AMOUNT-I.OFFICIAL_SENIORITY_DAILY ELSE BO.AMOUNT END OFFICIAL_BASE_AFTER,
   CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN BO.AMOUNT-I.OFFICIAL_SENIORITY_DAILY+I.OFFICIAL_SENIORITY_DAILY ELSE BO.AMOUNT END OFFICIAL_TOTAL_AFTER,
   CASE
    WHEN I.DEC_ID IS NULL THEN N'مبلغ واقعی سنوات و ریل منبع ثبت نشده؛ اعمال ممنوع'
    WHEN I.SOURCE_RAIL IS NULL OR I.SOURCE_RAIL NOT IN('NOMINAL','OFFICIAL','BOTH') THEN N'ریل منبع باید NOMINAL، OFFICIAL یا BOTH باشد'
    WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') AND (BN.AMOUNT IS NULL OR I.NOMINAL_SENIORITY_DAILY IS NULL) THEN N'پایه یا سنوات اسمی ناقص است'
    WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') AND (BO.AMOUNT IS NULL OR I.OFFICIAL_SENIORITY_DAILY IS NULL) THEN N'پایه یا سنوات رسمی ناقص است'
    WHEN ISNULL(I.NOMINAL_SENIORITY_DAILY,0)<0 OR ISNULL(I.OFFICIAL_SENIORITY_DAILY,0)<0 THEN N'سنوات منفی مجاز نیست'
    WHEN ISNULL(I.NOMINAL_SENIORITY_DAILY,0)>ISNULL(BN.AMOUNT,0) OR ISNULL(I.OFFICIAL_SENIORITY_DAILY,0)>ISNULL(BO.AMOUNT,0) THEN N'سنوات از پایه ریل مربوط بیشتر است'
    WHEN EXISTS(SELECT 1 FROM dbo.PAY2_RUN R JOIN dbo.PAY2_PERIOD P ON P.PER_ID=R.PER_ID JOIN dbo.PAY2_RUN_LINE RL ON RL.RUN_ID=R.RUN_ID WHERE RL.EMP_ID=D.EMP_ID AND R.STATUS IN(2,3) AND P.PERIOD_DATE/100>=@EFFECTIVE_FROM/100) THEN N'Run قطعی/سندشده در یا پس از تاریخ اثر وجود دارد'
    WHEN EXISTS(SELECT 1 FROM dbo.PAY2_DECREE NX WHERE NX.EMP_ID=D.EMP_ID AND NX.DEC_ID<>D.DEC_ID AND NX.EFF_FROM>=@EFFECTIVE_FROM) THEN N'حکم جدیدتر از تاریخ اثر وجود دارد'
    WHEN D.EFF_FROM=@EFFECTIVE_FROM THEN N'تاریخ اثر با شروع حکم جاری برابر است؛ اعمال ممنوع'
    WHEN I.IS_APPROVED=0 THEN N'در انتظار تأیید'
    WHEN L.DEC_ID IS NOT NULL THEN N'قبلاً اعمال شده'
    WHEN S.ITEM_ID IS NOT NULL THEN N'پایه سنوات از قبل در حکم وجود دارد'
    ELSE N'آماده ایجاد حکم جدید' END MIGRATION_STATUS
 FROM dbo.PAY2_DECREE D
 JOIN dbo.PAY2_EMPLOYEE E ON E.EMP_ID=D.EMP_ID
 LEFT JOIN dbo.PAY2_SANOVAT_MIGRATION_INPUT I ON I.DEC_ID=D.DEC_ID
 LEFT JOIN dbo.PAY2_DECREE_LINE BN ON BN.DEC_ID=D.DEC_ID AND BN.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL')
 LEFT JOIN dbo.PAY2_DECREE_LINE BO ON BO.DEC_ID=D.DEC_ID AND BO.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL_B')
 LEFT JOIN dbo.PAY2_DECREE_LINE S ON S.DEC_ID=D.DEC_ID AND S.ITEM_ID=@ItemId
 LEFT JOIN dbo.PAY2_SANOVAT_MIGRATION_LOG L ON L.DEC_ID=D.DEC_ID
 WHERE D.IS_CONFIRMED=1 AND D.EFF_FROM<=@EFFECTIVE_FROM AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
 ORDER BY E.LAST_NAME,E.FIRST_NAME,D.EFF_FROM;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_APPLY_SANOVAT_MIGRATION @EFFECTIVE_FROM BIGINT,@APPLIED_BY INT=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12 THROW 51010,N'تاریخ اثر باید تاریخ شمسی معتبر در روز اول ماه (سال 1300 تا 1600 و ماه 1 تا 12) باشد.',1;

 IF @APPLIED_BY IS NULL THROW 51012,N'شناسه کاربر اعمال‌کننده برای ثبت حسابرسی الزامی است.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @LockResult INT;
  EXEC @LockResult=sys.sp_getapplock @Resource=N'PAY2_SANOVAT_MIGRATION',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 51011,N'Migration سنوات هم‌اکنون در اجرای دیگری فعال است.',1;
  DECLARE @ItemId INT=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WITH(UPDLOCK,HOLDLOCK) WHERE ITEM_CODE='SANOVAT_PAYE');
  IF @ItemId IS NULL THROW 51000,N'آیتم SANOVAT_PAYE نصب نشده است.',1;
  IF EXISTS(SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I WITH(UPDLOCK,HOLDLOCK) JOIN dbo.PAY2_DECREE D WITH(UPDLOCK,HOLDLOCK) ON D.DEC_ID=I.DEC_ID WHERE I.IS_APPROVED=1 AND D.EFF_FROM=@EFFECTIVE_FROM) THROW 51005,N'تاریخ اثر با تاریخ شروع حکم جاری برابر است؛ برای جلوگیری از بازه نامعتبر اعمال متوقف شد.',1;
  SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_LOG WITH(UPDLOCK,HOLDLOCK) WHERE 1=0;
  IF EXISTS(
    SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I
    JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID AND D.IS_CONFIRMED=1 AND D.EFF_FROM<=@EFFECTIVE_FROM AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
    LEFT JOIN dbo.PAY2_DECREE_LINE BN ON BN.DEC_ID=I.DEC_ID AND BN.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL')
    LEFT JOIN dbo.PAY2_DECREE_LINE BO ON BO.DEC_ID=I.DEC_ID AND BO.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL_B')
    WHERE I.IS_APPROVED=1 AND (I.SOURCE_RAIL IS NULL OR I.SOURCE_RAIL NOT IN('NOMINAL','OFFICIAL','BOTH')
      OR (I.SOURCE_RAIL IN('NOMINAL','BOTH') AND (BN.AMOUNT IS NULL OR I.NOMINAL_SENIORITY_DAILY IS NULL OR I.NOMINAL_SENIORITY_DAILY<0 OR I.NOMINAL_SENIORITY_DAILY>BN.AMOUNT))
      OR (I.SOURCE_RAIL IN('OFFICIAL','BOTH') AND (BO.AMOUNT IS NULL OR I.OFFICIAL_SENIORITY_DAILY IS NULL OR I.OFFICIAL_SENIORITY_DAILY<0 OR I.OFFICIAL_SENIORITY_DAILY>BO.AMOUNT))))
   THROW 51001,N'ریل منبع یا مبلغ سنوات کامل/معتبر نیست؛ ابتدا Preview را بررسی کنید.',1;
  IF EXISTS(SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID JOIN dbo.PAY2_RUN_LINE RL ON RL.EMP_ID=D.EMP_ID JOIN dbo.PAY2_RUN R ON R.RUN_ID=RL.RUN_ID JOIN dbo.PAY2_PERIOD P ON P.PER_ID=R.PER_ID WHERE I.IS_APPROVED=1 AND R.STATUS IN(2,3) AND P.PERIOD_DATE/100>=@EFFECTIVE_FROM/100)
   THROW 51003,N'Run قطعی یا سندشده در/پس از تاریخ اثر وجود دارد؛ Migration مجاز نیست.',1;
  IF EXISTS(SELECT 1 FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID JOIN dbo.PAY2_DECREE NX ON NX.EMP_ID=D.EMP_ID AND NX.DEC_ID<>D.DEC_ID AND NX.EFF_FROM>=@EFFECTIVE_FROM WHERE I.IS_APPROVED=1)
   THROW 51004,N'برای حداقل یک پرسنل حکم جدیدتر از تاریخ اثر وجود دارد.',1;

  DECLARE @PrevDate BIGINT,@Y INT=@EFFECTIVE_FROM/10000,@M INT=(@EFFECTIVE_FROM/100)%100;
  SET @PrevDate=CASE WHEN @M=1 THEN (@Y-1)*10000+1200+CASE WHEN ((25*(@Y-1)+11)%33)<8 THEN 30 ELSE 29 END WHEN @M<=7 THEN @Y*10000+(@M-1)*100+31 ELSE @Y*10000+(@M-1)*100+30 END;
  DECLARE @W TABLE(DEC_ID INT PRIMARY KEY,SOURCE_RAIL NVARCHAR(10),NB DECIMAL(18,2),NS DECIMAL(18,2),NA DECIMAL(18,2),OB DECIMAL(18,2),OS DECIMAL(18,2),OA DECIMAL(18,2));
  INSERT @W SELECT I.DEC_ID,I.SOURCE_RAIL,BN.AMOUNT,CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN I.NOMINAL_SENIORITY_DAILY ELSE 0 END,CASE WHEN I.SOURCE_RAIL IN('NOMINAL','BOTH') THEN BN.AMOUNT-I.NOMINAL_SENIORITY_DAILY ELSE BN.AMOUNT END,BO.AMOUNT,CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN I.OFFICIAL_SENIORITY_DAILY ELSE 0 END,CASE WHEN I.SOURCE_RAIL IN('OFFICIAL','BOTH') THEN BO.AMOUNT-I.OFFICIAL_SENIORITY_DAILY ELSE BO.AMOUNT END
  FROM dbo.PAY2_SANOVAT_MIGRATION_INPUT I JOIN dbo.PAY2_DECREE D ON D.DEC_ID=I.DEC_ID AND D.IS_CONFIRMED=1 AND D.EFF_FROM<=@EFFECTIVE_FROM AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
  LEFT JOIN dbo.PAY2_DECREE_LINE BN ON BN.DEC_ID=I.DEC_ID AND BN.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL') LEFT JOIN dbo.PAY2_DECREE_LINE BO ON BO.DEC_ID=I.DEC_ID AND BO.ITEM_ID=(SELECT ITEM_ID FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE='BASE_SAL_B') LEFT JOIN dbo.PAY2_DECREE_LINE S ON S.DEC_ID=I.DEC_ID AND S.ITEM_ID=@ItemId LEFT JOIN dbo.PAY2_SANOVAT_MIGRATION_LOG L ON L.DEC_ID=I.DEC_ID WHERE I.IS_APPROVED=1 AND S.ITEM_ID IS NULL AND L.DEC_ID IS NULL;
  IF EXISTS(SELECT 1 FROM @W WHERE (NB IS NOT NULL AND NB<>NA+NS) OR (OB IS NOT NULL AND OB<>OA+OS)) THROW 51002,N'کنترل مستقل تساوی ریل اسمی/رسمی ناموفق بود.',1;

  DECLARE @Old INT,@New INT,@Rail NVARCHAR(10),@NB DECIMAL(18,2),@NS DECIMAL(18,2),@NA DECIMAL(18,2),@OB DECIMAL(18,2),@OS DECIMAL(18,2),@OA DECIMAL(18,2);
  DECLARE C CURSOR LOCAL FAST_FORWARD FOR SELECT DEC_ID,SOURCE_RAIL,NB,NS,NA,OB,OS,OA FROM @W; OPEN C; FETCH NEXT FROM C INTO @Old,@Rail,@NB,@NS,@NA,@OB,@OS,@OA;
  WHILE @@FETCH_STATUS=0
  BEGIN
   INSERT dbo.PAY2_DECREE(EMP_ID,WS_ID,ISSUED_DATE,EFF_FROM,EFF_TO,EDU_LEVEL,MARITAL,IS_MANAGER,TMPL_ID,IS_CONFIRMED,CONFIRMED_BY,CONFIRMED_AT,CREATED_AT,CREATED_BY,NOTES,SHIFT_MODE)
    SELECT EMP_ID,WS_ID,@EFFECTIVE_FROM,@EFFECTIVE_FROM,EFF_TO,EDU_LEVEL,MARITAL,IS_MANAGER,TMPL_ID,1,@APPLIED_BY,SYSDATETIME(),SYSDATETIME(),@APPLIED_BY,CONCAT(ISNULL(NOTES,N''),N' | تفکیک پایه سنوات از حکم ',DEC_ID),SHIFT_MODE FROM dbo.PAY2_DECREE WHERE DEC_ID=@Old;
   SET @New=SCOPE_IDENTITY();
   INSERT dbo.PAY2_DECREE_LINE(DEC_ID,ITEM_ID,AMOUNT,INS_OV,TAX_OV,BASIS_OV,SHIFT_MODE_OV,NOMINAL_AMOUNT_OV,OFFICIAL_AMOUNT_OV)
    SELECT @New,ITEM_ID,AMOUNT,INS_OV,TAX_OV,BASIS_OV,SHIFT_MODE_OV,NOMINAL_AMOUNT_OV,OFFICIAL_AMOUNT_OV FROM dbo.PAY2_DECREE_LINE WHERE DEC_ID=@Old;
   UPDATE L SET AMOUNT=@NA FROM dbo.PAY2_DECREE_LINE L JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=L.ITEM_ID WHERE L.DEC_ID=@New AND I.ITEM_CODE='BASE_SAL' AND @Rail IN('NOMINAL','BOTH');
   UPDATE L SET AMOUNT=@OA FROM dbo.PAY2_DECREE_LINE L JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=L.ITEM_ID WHERE L.DEC_ID=@New AND I.ITEM_CODE='BASE_SAL_B' AND @Rail IN('OFFICIAL','BOTH');
   INSERT dbo.PAY2_DECREE_LINE(DEC_ID,ITEM_ID,AMOUNT,NOMINAL_AMOUNT_OV,OFFICIAL_AMOUNT_OV) VALUES(@New,@ItemId,@OS,@NS,@OS);
   UPDATE dbo.PAY2_DECREE SET EFF_TO=@PrevDate WHERE DEC_ID=@Old AND (EFF_TO IS NULL OR EFF_TO>=@EFFECTIVE_FROM);
   INSERT dbo.PAY2_SANOVAT_MIGRATION_LOG(DEC_ID,NEW_DEC_ID,EFFECTIVE_FROM,SOURCE_RAIL,NOMINAL_BASE_BEFORE,NOMINAL_SENIORITY,NOMINAL_BASE_AFTER,OFFICIAL_BASE_BEFORE,OFFICIAL_SENIORITY,OFFICIAL_BASE_AFTER,APPLIED_BY) VALUES(@Old,@New,@EFFECTIVE_FROM,@Rail,@NB,@NS,@NA,@OB,@OS,@OA,@APPLIED_BY);
   FETCH NEXT FROM C INTO @Old,@Rail,@NB,@NS,@NA,@OB,@OS,@OA;
  END; CLOSE C; DEALLOCATE C;
  COMMIT; SELECT COUNT(*) APPLIED_COUNT FROM @W;
 END TRY BEGIN CATCH IF CURSOR_STATUS('local','C')>=0 CLOSE C; IF CURSOR_STATUS('local','C')>-3 DEALLOCATE C; IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_PREVIEW_INS_NON_SUBJECT_OPTIN @EFFECTIVE_FROM BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12
  THROW 51105,N'تاریخ اثر باید روز اول یک ماه شمسی معتبر باشد.',1;
 SELECT DB_NAME() DATABASE_NAME,C.CFG_VALUE OPT_IN_VALUE,
        CASE WHEN C.CFG_VALUE=N'APPROVED:'+DB_NAME() THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END IS_TARGET_APPROVED,
        @EFFECTIVE_FROM EFFECTIVE_FROM,
        TRY_CAST((SELECT CFG_VALUE FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM') AS BIGINT) CURRENT_RULE_EFFECTIVE_FROM
 FROM dbo.PAY2_CONFIG C WHERE C.CFG_KEY=N'INS_NON_SUBJECT_OPT_IN';

 SELECT I.ITEM_ID,I.ITEM_CODE,I.ITEM_NAME,I.INS_SUBJECT CURRENT_INS_SUBJECT,I.TAX_SUBJECT UNCHANGED_TAX_SUBJECT
 FROM dbo.PAY2_ITEM_DEF I WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') ORDER BY I.ITEM_CODE;

 -- همه Overrideها نمایش داده می‌شوند؛ IS_BLOCKING فقط جاری/آینده را مشخص می‌کند.
 SELECT N'DECREE' OVERRIDE_SCOPE,D.EMP_ID,DL.DEC_ID,CAST(DL.DEC_ID AS NVARCHAR(80)) REF_KEY,I.ITEM_CODE,DL.INS_OV,
        D.EFF_FROM VALID_FROM,D.EFF_TO VALID_TO,
        CAST(CASE WHEN D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM THEN 1 ELSE 0 END AS bit) IS_BLOCKING
 FROM dbo.PAY2_DECREE_LINE DL JOIN dbo.PAY2_DECREE D ON D.DEC_ID=DL.DEC_ID JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=DL.ITEM_ID
 WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND DL.INS_OV IS NOT NULL
 UNION ALL
 SELECT N'EMPLOYEE',O.EMP_ID,NULL,CONCAT(O.EMP_ID,N':',O.ITEM_ID,N':',O.VALID_FROM),I.ITEM_CODE,O.INS_OV,
        O.VALID_FROM,O.VALID_TO,CAST(CASE WHEN O.VALID_TO IS NULL OR O.VALID_TO>=@EFFECTIVE_FROM THEN 1 ELSE 0 END AS bit)
 FROM dbo.PAY2_OVERRIDE O JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=O.ITEM_ID
 WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND O.INS_OV IS NOT NULL
 UNION ALL
 SELECT N'TEMPLATE',NULL,NULL,CONCAT(TL.TMPL_ID,N':',TL.ITEM_ID),I.ITEM_CODE,TL.INS_OV,
        NULL,NULL,CAST(T.IS_ACTIVE AS bit)
 FROM dbo.PAY2_ITEM_TMPL_LINE TL JOIN dbo.PAY2_ITEM_TEMPLATE T ON T.TMPL_ID=TL.TMPL_ID JOIN dbo.PAY2_ITEM_DEF I ON I.ITEM_ID=TL.ITEM_ID
 WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND TL.INS_OV IS NOT NULL
 ORDER BY OVERRIDE_SCOPE,ITEM_CODE,REF_KEY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_PAY2_APPLY_INS_NON_SUBJECT_OPTIN
 @CONFIRM_DATABASE SYSNAME,@EFFECTIVE_FROM BIGINT,@APPLIED_BY INT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @LockResult INT;
  EXEC @LockResult=sys.sp_getapplock @Resource=N'PAY2_INS_NON_SUBJECT_OPTIN',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;
  IF @LockResult<0 THROW 51104,N'اعمال Opt-in هم‌اکنون در اجرای دیگری فعال است.',1;

  -- تمام Validationهای قابل تغییر پس از قفل و داخل همان Transaction انجام می‌شوند.
  IF @APPLIED_BY IS NULL THROW 51100,N'شناسه کاربر اعمال‌کننده الزامی است.',1;
  IF @EFFECTIVE_FROM IS NULL OR @EFFECTIVE_FROM%100<>1 OR @EFFECTIVE_FROM/10000 NOT BETWEEN 1300 AND 1600 OR (@EFFECTIVE_FROM/100)%100 NOT BETWEEN 1 AND 12
   THROW 51105,N'تاریخ اثر باید روز اول یک ماه شمسی معتبر باشد.',1;
  IF @CONFIRM_DATABASE IS NULL OR @CONFIRM_DATABASE<>DB_NAME() THROW 51101,N'نام دیتابیس تأییدشده با دیتابیس جاری یکسان نیست.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WITH(UPDLOCK,HOLDLOCK) WHERE CFG_KEY=N'INS_NON_SUBJECT_OPT_IN' AND CFG_VALUE=N'APPROVED:'+DB_NAME())
   THROW 51102,N'Opt-in این دیتابیس فعال نیست؛ ابتدا Preview و سپس مقدار APPROVED:<DatabaseName> را با تأیید صریح ثبت کنید.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.PAY2_CONFIG WITH(UPDLOCK,HOLDLOCK) WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM')
   THROW 51106,N'تنظیم تاریخ اثر نصب نشده است؛ ابتدا Updater را کامل اجرا کنید.',1;
  IF (SELECT COUNT(*) FROM dbo.PAY2_ITEM_DEF WITH(UPDLOCK,HOLDLOCK) WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN'))<>4
   THROW 51107,N'هر چهار آیتم SHIFT، OT_NORMAL، OT_HOLIDAY و OT_ADMIN باید پیش از Apply موجود باشند.',1;

  IF EXISTS
  (
   SELECT 1 FROM dbo.PAY2_DECREE_LINE DL WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.PAY2_DECREE D WITH(UPDLOCK,HOLDLOCK) ON D.DEC_ID=DL.DEC_ID
   JOIN dbo.PAY2_ITEM_DEF I WITH(UPDLOCK,HOLDLOCK) ON I.ITEM_ID=DL.ITEM_ID
   WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND DL.INS_OV=1
     AND (D.EFF_TO IS NULL OR D.EFF_TO>=@EFFECTIVE_FROM)
   UNION ALL
   SELECT 1 FROM dbo.PAY2_OVERRIDE O WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.PAY2_ITEM_DEF I WITH(UPDLOCK,HOLDLOCK) ON I.ITEM_ID=O.ITEM_ID
   WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND O.INS_OV=1
     AND (O.VALID_TO IS NULL OR O.VALID_TO>=@EFFECTIVE_FROM)
   UNION ALL
   SELECT 1 FROM dbo.PAY2_ITEM_TMPL_LINE TL WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.PAY2_ITEM_TEMPLATE T WITH(UPDLOCK,HOLDLOCK) ON T.TMPL_ID=TL.TMPL_ID
   JOIN dbo.PAY2_ITEM_DEF I WITH(UPDLOCK,HOLDLOCK) ON I.ITEM_ID=TL.ITEM_ID
   WHERE I.ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') AND TL.INS_OV=1 AND T.IS_ACTIVE=1
  ) THROW 51103,N'Override مشمول بیمه جاری/آینده وجود دارد؛ Preview را بررسی و Overrideها را صریحاً تعیین تکلیف کنید.',1;

  INSERT dbo.PAY2_INS_NON_SUBJECT_OPTIN_LOG(DATABASE_NAME,ITEM_CODE,EFFECTIVE_FROM,OLD_INS_SUBJECT,NEW_INS_SUBJECT,TAX_SUBJECT_SNAPSHOT,APPLIED_BY)
  SELECT DB_NAME(),ITEM_CODE,@EFFECTIVE_FROM,INS_SUBJECT,0,TAX_SUBJECT,@APPLIED_BY FROM dbo.PAY2_ITEM_DEF WITH(UPDLOCK,HOLDLOCK)
  WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN');

  UPDATE dbo.PAY2_CONFIG
  SET CFG_VALUE=CAST(@EFFECTIVE_FROM AS NVARCHAR(20)),CHANGED_BY=@APPLIED_BY,CHANGED_AT=GETDATE(),
      CHANGE_NOTE=CONCAT(N'قاعده عدم مشمولیت SHIFT/OT از ',@EFFECTIVE_FROM,N' برای دیتابیس ',DB_NAME(),N' زمان‌بندی شد.')
  WHERE CFG_KEY=N'INS_NON_SUBJECT_EFFECTIVE_FROM';

  -- مجوز یک‌بارمصرف است و در همان تراکنش مصرف می‌شود؛ TAX_SUBJECT و تاریخچه Run دست‌نخورده می‌مانند.
  UPDATE dbo.PAY2_CONFIG
  SET CFG_VALUE=N'DISABLED',CHANGED_BY=@APPLIED_BY,CHANGED_AT=GETDATE(),
      CHANGE_NOTE=CONCAT(N'Opt-in بیمه برای SHIFT/OT در تاریخ اثر ',@EFFECTIVE_FROM,N' روی دیتابیس ',DB_NAME(),N' اعمال و مجوز مصرف شد.')
  WHERE CFG_KEY=N'INS_NON_SUBJECT_OPT_IN';

  COMMIT;
  SELECT ITEM_CODE,INS_SUBJECT BASE_INS_SUBJECT,TAX_SUBJECT,@EFFECTIVE_FROM EFFECTIVE_FROM,CAST(0 AS bit) EFFECTIVE_INS_SUBJECT
  FROM dbo.PAY2_ITEM_DEF WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN') ORDER BY ITEM_CODE;
 END TRY
 BEGIN CATCH
  IF @@TRANCOUNT>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
");

                // ===========================================================
                // 3. Stored Procedures â CREATE OR ALTER (همیشه آخرین نسخه)
                // ===========================================================
                string procScript = @"
-- ================================================================
-- PAY2 — Stored Procedures & Business Logic — v6.0
-- نرم‌افزار مستر کارکت | کد: PAY2-DB-006
-- ================================================================
-- این فایل باید پس از PAY2_DDL_v6.sql اجرا شود.
--
-- محتوا:
--   1. SP_PAY2_CALC_RUN   — موتور محاسبه حقوق ماهیانه (۱۲ گام)
--   2. SP_PAY2_GEN_DEED   — تولید سند حسابداری حقوق و بیمه
--   3. SP_PAY2_CALC_SETTLE — محاسبه تسویه حساب پرسنل
--   4. SP_PAY2_GEN_DEED_SETTLE — تولید سند حسابداری تسویه
--   5. SP_PAY2_CLOSE_PERIOD   — بستن دوره و کنترل نهایی
--   6. SP_PAY2_REVERT_RUN     — برگشت محاسبه (bak به پیش‌نویس)
-- ================================================================

SET NOCOUNT ON;
GO

-- ================================================================
-- پارامترها:
--   @WS_ID        : شناسه کارگاه
--   @PER_ID       : شناسه دوره (از PAY2_PERIOD)
--   @PAYROLL_N_S  : شماره سند حقوق در DEED_HED (برای مساعده)
--   @CALC_BY      : کد کاربر محاسبه‌گر
--   @IS_RERUN     : 0=اول بار | 1=بازمحاسبه (RUN_NO جدید ایجاد می‌کند)
-- خروجی:
--   @NEW_RUN_ID   OUTPUT — شناسه PAY2_RUN ایجادشده
-- ================================================================
-- ================================================================
-- ۱. SP_PAY2_CALC_RUN — موتور محاسبه حقوق ماهیانه
-- موتور قطعی دو ریل مستقل؛ بدون Fallback خاموش بین اسمی و رسمی
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CALC_RUN]
    @WS_ID       INT,
    @PER_ID      INT,
    @PAYROLL_N_S FLOAT,
    @CALC_BY     INT          = NULL,
    @IS_RERUN    BIT          = 0,
    @NEW_RUN_ID  INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET ANSI_WARNINGS OFF;

    -- 🚀 گام صفر: اعلان (DECLARE) تمامی متغیرها در سطح Batch برای جلوگیری از نشت اسکوپ در T-SQL
    DECLARE
        @MONTH_DAYS_MODE NVARCHAR(10), @MONTH_DAYS TINYINT,
        @OT_NORMAL_MULT DECIMAL(6,4), @OT_HOLIDAY_MULT DECIMAL(6,4),
        @OT_HOUR_BASE DECIMAL(6,4), @SHIFT_MODE NVARCHAR(10),
        @ROUND_MODE INT, @INS_WORKER_RATE DECIMAL(6,4),
        @INS_EMPLOYER_RATE DECIMAL(6,4), @INS_UNEMP_RATE DECIMAL(6,4),
        @INS_CEILING_APPLY BIT, @INS_CEILING BIGINT,
        @TAX_YEAR SMALLINT, @TAX_EXEMPT BIGINT,
        @TAX_DEDUCT_INS BIT, @TAX_DEP_APPLY BIT,
        @ADV_ENABLED BIT, @PERIOD_DATE BIGINT, @INS_NON_SUBJECT_EFFECTIVE_FROM BIGINT,
        @PERIOD_MONTH INT, @PERIOD_YEAR INT,
        @MONTHLY_PRORATE BIT;

    DECLARE @INS_DED_ID INT, @TAX_DED_ID INT, @LOAN_DED_ID INT, @ADV_DED_ID INT;
    DECLARE @PREV_RUN_ID INT, @PREV_STATUS TINYINT, @NEXT_RUN_NO SMALLINT = 1;
    DECLARE @IS_LEAP_YEAR BIT;

    -- متغیرهای حلقه پرسنل
    DECLARE @PER_START BIGINT, @PER_END BIGINT;
    DECLARE @WS_SHIFT_MODE NVARCHAR(10);
    DECLARE @EMP_ID INT, @IS_MANAGER BIT, @INS_TYPE TINYINT, @TAX_EXEMPT_FLAG BIT, @REGION_DEP TINYINT, @ACC_T NVARCHAR(50);
    -- برچسب خوانا برای پیام‌های خطا (کد پرسنلی و نام) و تشخیص دلیل نبود حکم معتبر
    DECLARE @EMP_LABEL NVARCHAR(300), @DEC_REASON NVARCHAR(400);
    DECLARE @DEC_ANY_COUNT INT, @DEC_UNCONFIRMED_COUNT INT, @DEC_CONFIRMED_NO_LINE_COUNT INT;

    DECLARE @WORK_DAYS DECIMAL(5,2), @DAYS DECIMAL(5,2), @DAYSB DECIMAL(5,2),
            @FRID_COUNT TINYINT, @TDAYS DECIMAL(5,2), @OT_NORMAL_H DECIMAL(6,2),
            @OT_HOLIDAY_H DECIMAL(6,2), @OT_ADMIN_H DECIMAL(6,2), @SHORTAGE_H DECIMAL(6,2), @LEAVE_DAYS DECIMAL(5,2),
            @PERF_AMOUNT BIGINT, @TRANSP_AMOUNT BIGINT, @KASR_OTHER BIGINT;

    -- متغیرهای حلقه‌های احکام و اقلام
    DECLARE @DEC_ID INT, @DEC_FROM BIGINT, @DEC_TO BIGINT, @DEC_SHIFT_MODE NVARCHAR(10);
    DECLARE @DEC_ACTUAL_START BIGINT, @DEC_ACTUAL_END BIGINT, @DEC_ACTIVE_DAYS INT, @PRORATE_FACTOR DECIMAL(18,6);

    DECLARE @HAS_BOTH_SAL BIT, @HAS_NOMINAL_RATE BIT, @HAS_OFFICIAL_RATE BIT, @DAILY_NOMINAL DECIMAL(18,2), @DAILY_OFFICIAL DECIMAL(18,2), @DAILY_SEN_NOMINAL DECIMAL(18,2), @DAILY_SEN_OFFICIAL DECIMAL(18,2);
    DECLARE @INS_OFFICIAL_VALID BIT, @TAX_OFFICIAL_VALID BIT, @INS_DROP_SAL NVARCHAR(30), @TAX_DROP_SAL NVARCHAR(30);

    DECLARE @ITEM_ID INT, @ITEM_CODE NVARCHAR(30), @ITEM_TYPE TINYINT, @ITEM_AMOUNT DECIMAL(18,2),
            @ITEM_BASIS TINYINT, @ITEM_INS BIT, @ITEM_TAX BIT, @ITEM_PBD TINYINT, @ITEM_IBD TINYINT, @DL_SHIFT_MODE_OV NVARCHAR(10),
            @DL_NOMINAL_AMOUNT_OV DECIMAL(18,2), @DL_OFFICIAL_AMOUNT_OV DECIMAL(18,2);
    DECLARE @OV_INS BIT, @OV_TAX BIT, @OV_BASIS TINYINT;
    DECLARE @CALC_AMOUNT BIGINT, @INS_CALC_AMOUNT BIGINT;
    DECLARE @PAY_DAYS DECIMAL(18,6), @BASE_DAYS_RAW DECIMAL(5,2), @INS_DAYS DECIMAL(18,6), @INS_DAYS_RAW DECIMAL(5,2);
    DECLARE @FULL_MONTH BIGINT, @FULL_MONTH_INS BIGINT, @NAHAR_DAYS DECIMAL(18,6), @EFF_SHIFT_MODE NVARCHAR(10);

    -- متغیرهای محاسباتی نهایی
    DECLARE @TOTAL_NOMINAL_BASE BIGINT, @TOTAL_OFFICIAL_BASE BIGINT;
    DECLARE @EFFECTIVE_HOURLY DECIMAL(18,2), @OFFICIAL_HOURLY DECIMAL(18,2);

    DECLARE @GROSS_PAY BIGINT, @NOMINAL_GROSS BIGINT, @INS_BASE BIGINT, @INS_WORKER BIGINT, @INS_EMPLOYER BIGINT, @INS_EMPLOYER_BASE BIGINT, @INS_UNEMPLOYMENT BIGINT;
    DECLARE @EFFECTIVE_INS_CEILING BIGINT, @EMP_IS_JANBAZ BIT, @JANBAZ_RATE DECIMAL(6,4);
    DECLARE @TAX_BASE BIGINT, @TAX_AMOUNT BIGINT;
    DECLARE @ADVANCE_DED BIGINT, @LOAN_DED BIGINT, @OTHER_DED BIGINT, @SHORTAGE_DED BIGINT, @TOTAL_DED BIGINT, @NET_PAY BIGINT;
    DECLARE @LEAVE_BAL_DAYS DECIMAL(5,2), @LOAN_BAL BIGINT, @LEAVE_MIN_USED INT;

    DECLARE @ItemCalc TABLE (
        ITEM_ID INT, ITEM_CODE NVARCHAR(30), ITEM_TYPE TINYINT,
        AMOUNT BIGINT, INS_AMOUNT BIGINT, INS_SUBJECT BIT, TAX_SUBJECT BIT
    );

    -- گام ۱ — بارگذاری تنظیمات
    SELECT
        @MONTH_DAYS_MODE   = ISNULL(MAX(CASE WHEN CFG_KEY='MONTH_DAYS_MODE'    THEN CFG_VALUE END), '30'),
        @OT_NORMAL_MULT    = ISNULL(MAX(CASE WHEN CFG_KEY='OT_NORMAL_MULT'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOLIDAY_MULT   = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOLIDAY_MULT'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 1.40),
        @OT_HOUR_BASE      = ISNULL(MAX(CASE WHEN CFG_KEY='OT_HOUR_BASE'       THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END), 7.33),
        @SHIFT_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='SHIFT_MODE'         THEN CFG_VALUE END), 'PCT'),
        @ROUND_MODE        = ISNULL(MAX(CASE WHEN CFG_KEY='ROUND_MODE'         THEN CAST(CFG_VALUE AS INT) END), 1),
        @INS_WORKER_RATE   = ISNULL(MAX(CASE WHEN CFG_KEY='INS_WORKER_RATE'    THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.07),
        @INS_EMPLOYER_RATE = ISNULL(MAX(CASE WHEN CFG_KEY='INS_EMPLOYER_RATE'  THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.20),
        @INS_UNEMP_RATE    = ISNULL(MAX(CASE WHEN CFG_KEY='INS_UNEMP_RATE'     THEN CAST(CFG_VALUE AS DECIMAL(6,4)) END) / 100.0, 0.03),
        @INS_CEILING       = ISNULL(MAX(CASE WHEN CFG_KEY='INS_CEILING_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 999999999),
        @TAX_YEAR          = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'           THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT        = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @INS_CEILING_APPLY = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='INS_CEILING_APPLY'  THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEDUCT_INS    = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEDUCT_INS'     THEN CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @TAX_DEP_APPLY     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='TAX_DEPRIVATION_APPLY' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @ADV_ENABLED       = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='ADV_ENABLED'        THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @MONTHLY_PRORATE   = ISNULL(CAST(MAX(CASE WHEN CFG_KEY='MONTHLY_ITEM_PRORATE' THEN CAST(CFG_VALUE AS INT) END) AS BIT), 0),
        @INS_NON_SUBJECT_EFFECTIVE_FROM = ISNULL(MAX(CASE WHEN CFG_KEY='INS_NON_SUBJECT_EFFECTIVE_FROM' THEN TRY_CAST(CFG_VALUE AS BIGINT) END),0)
    FROM PAY2_CONFIG;

    SELECT @PERIOD_DATE = PERIOD_DATE
    FROM PAY2_PERIOD WITH (UPDLOCK, HOLDLOCK)
    WHERE PER_ID = @PER_ID AND WS_ID = @WS_ID;
    IF @PERIOD_DATE IS NULL
    BEGIN
        RAISERROR(N'دوره %d به کارگاه %d تعلق ندارد یا یافت نشد.', 16, 1, @PER_ID, @WS_ID);
        RETURN;
    END;

    SET @PERIOD_MONTH = (@PERIOD_DATE / 100) % 100;
    SET @PERIOD_YEAR  = @PERIOD_DATE / 10000;
    SET @IS_LEAP_YEAR = CASE WHEN ((25 * @PERIOD_YEAR + 11) % 33) < 8 THEN 1 ELSE 0 END;

    SET @MONTH_DAYS = CASE
        WHEN @MONTH_DAYS_MODE = '30' THEN 30
        WHEN @PERIOD_MONTH <= 6 THEN 31
        WHEN @PERIOD_MONTH BETWEEN 7 AND 11 THEN 30
        WHEN @PERIOD_MONTH = 12 AND @IS_LEAP_YEAR = 1 THEN 30
        ELSE 29
    END;

    -- محاسبه و Snapshot از یک تصویر ثابت از Masterها استفاده می‌کنند.
    SELECT I.ITEM_ID,I.ITEM_CODE,I.ITEM_NAME,I.ITEM_TYPE,I.CALC_BASIS,I.INS_SUBJECT,I.TAX_SUBJECT,
           I.PAY_BASE_DAYS,I.INS_BASE_DAYS,I.IS_ACTIVE,I.SORT_ORDER
    INTO #ItemDefSource
    FROM PAY2_ITEM_DEF I;

    SELECT E.EMP_ID,E.EMP_CODE,E.FIRST_NAME,E.LAST_NAME,E.FATHER_NAME,E.NATIONAL_CODE,E.INS_CODE,
           E.ID_NUMBER,E.BIRTH_PLACE,E.BIRTH_DATE,E.GENDER,ISNULL(E.INS_TYPE,1) INS_TYPE,
           ISNULL(E.TAX_EXEMPT,0) TAX_EXEMPT,E.MARITAL,E.NATIONALITY,E.MOBILE,E.HIRE_DATE,E.FIRE_DATE,
           E.IS_MANAGER,ISNULL(E.IS_JANBAZ,0) IS_JANBAZ,E.REGION_DEPRIVATION,E.ACC_T,
           J.JOB_CODE,J.JOB_NAME
    INTO #EmployeeSource
    FROM PAY2_EMPLOYEE E
    LEFT JOIN PAY2_JOB J ON J.JOB_ID=E.JOB_ID
    WHERE E.WS_ID=@WS_ID AND E.IS_ACTIVE=1
      AND EXISTS(SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID=@PER_ID AND A.EMP_ID=E.EMP_ID);

    SET @INS_DED_ID  = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='INS_DED');
    SET @TAX_DED_ID  = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='TAX_DED');
    SET @LOAN_DED_ID = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='LOAN_DED');
    SET @ADV_DED_ID  = (SELECT ITEM_ID FROM #ItemDefSource WHERE ITEM_CODE='ADVANCE_DED');

    -- گام ۲ — ایجاد هدر PAY2_RUN
    IF @IS_RERUN = 1
    BEGIN
        SELECT TOP 1 @PREV_RUN_ID = RUN_ID, @NEXT_RUN_NO = RUN_NO + 1, @PREV_STATUS = STATUS
        FROM PAY2_RUN WHERE PER_ID = @PER_ID AND IS_LATEST = 1 ORDER BY RUN_NO DESC;

        IF @PREV_STATUS >= 2
        BEGIN
            RAISERROR(N'اجرای قبلی تأیید نهایی شده است. دیتابیس اجازه بازمحاسبه را نمی‌دهد.', 16, 1);
            RETURN;
        END

        IF @PREV_RUN_ID IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM PAY2_RUN WHERE RUN_ID = @PREV_RUN_ID AND STATUS = 1)
               AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @PREV_RUN_ID)
            BEGIN
                EXEC SP_PAY2_REVERT_RUN @RUN_ID = @PREV_RUN_ID, @REVERT_BY = @CALC_BY;
            END
        END

        UPDATE PAY2_RUN SET IS_LATEST = 0 WHERE PER_ID = @PER_ID;
    END;

    INSERT INTO PAY2_RUN (PER_ID, RUN_NO, IS_LATEST, CALC_AT, CALC_BY, STATUS, PREV_RUN_ID, PAYROLL_ENGINE_VERSION, WS_ID_SNAP)
    VALUES (@PER_ID, @NEXT_RUN_NO, 1, GETDATE(), @CALC_BY, 1, @PREV_RUN_ID, 3, @WS_ID);

    SET @NEW_RUN_ID = SCOPE_IDENTITY();

    CREATE TABLE #AdvResult (EMP_ID INT, PCODE NVARCHAR(50), FULL_NAME NVARCHAR(150), RAW_BALANCE BIGINT, MANUAL_EXCL BIGINT, ADVANCE_DEDUCTION BIGINT);
    IF @ADV_ENABLED = 1
    BEGIN
        INSERT INTO #AdvResult (EMP_ID, PCODE, FULL_NAME, RAW_BALANCE, MANUAL_EXCL, ADVANCE_DEDUCTION)
        EXEC SP_PAY2_GET_ADVANCES @PERIOD_DATE = @PERIOD_DATE, @PAYROLL_N_S = @PAYROLL_N_S, @WS_ID = @WS_ID;
    END;

    SELECT @WS_SHIFT_MODE = NULLIF(SHIFT_MODE, N'') FROM PAY2_WORKSHOP WHERE WS_ID = @WS_ID;

    DECLARE cur_emp CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
        SELECT E.EMP_ID, E.INS_TYPE, E.TAX_EXEMPT, E.REGION_DEPRIVATION, E.ACC_T
        FROM #EmployeeSource E;

    OPEN cur_emp;
    FETCH NEXT FROM cur_emp INTO @EMP_ID, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;

    -- گام ۳ — حلقه روی پرسنل فعال کارگاه
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DELETE FROM @ItemCalc;

        -- 🚀 ریست صریح مقادیر در هر چرخش حلقه پرسنل
        SET @HAS_BOTH_SAL = 0; SET @HAS_NOMINAL_RATE = 0; SET @HAS_OFFICIAL_RATE = 0;
        SET @DAILY_NOMINAL = 0; SET @DAILY_OFFICIAL = 0; SET @DAILY_SEN_NOMINAL = 0; SET @DAILY_SEN_OFFICIAL = 0;
        SET @TOTAL_NOMINAL_BASE = 0; SET @TOTAL_OFFICIAL_BASE = 0;
        SET @EFFECTIVE_HOURLY = 0; SET @OFFICIAL_HOURLY = 0;

        SELECT
            @WORK_DAYS = ISNULL(WORK_DAYS,0), @DAYS = ISNULL(DAYS,0), @DAYSB = ISNULL(DAYSB,0),
            @FRID_COUNT = ISNULL(FRID_COUNT,0), @TDAYS = ISNULL(TDAYS,0), @OT_NORMAL_H = ISNULL(OT_NORMAL_H,0),
            @OT_HOLIDAY_H = ISNULL(OT_HOLIDAY_H,0), @OT_ADMIN_H = ISNULL(OT_ADMIN_H,0), @SHORTAGE_H = ISNULL(SHORTAGE_H,0), @LEAVE_DAYS = ISNULL(LEAVE_DAYS,0),
            @PERF_AMOUNT = ISNULL(PERF_AMOUNT,0), @TRANSP_AMOUNT = ISNULL(TRANSP_AMOUNT,0), @KASR_OTHER = ISNULL(KASR_OTHER,0)
        FROM PAY2_ATTENDANCE WHERE PER_ID = @PER_ID AND EMP_ID = @EMP_ID;

        SET @PER_START = @PERIOD_DATE + 1;
        SET @PER_END   = @PERIOD_DATE + @MONTH_DAYS;

        SET @EMP_LABEL = NULL;
        SELECT @EMP_LABEL = N'کد پرسنلی ' + ISNULL(E.EMP_CODE, N'-') + N' (' + LTRIM(RTRIM(ISNULL(E.FIRST_NAME, N'') + N' ' + ISNULL(E.LAST_NAME, N''))) + N')'
        FROM #EmployeeSource E WHERE E.EMP_ID = @EMP_ID;
        SET @EMP_LABEL = ISNULL(@EMP_LABEL, N'EMP_ID=' + CAST(@EMP_ID AS NVARCHAR(20)));

        -- وضعیت مدیر از آخرین حکم تأییدشده و مؤثر همین ماه خوانده می‌شود؛ همان مقدار برای بیمه بیکاری و Snapshot استفاده می‌شود.
        SET @IS_MANAGER = 0;
        SELECT TOP 1 @IS_MANAGER=ISNULL(D.IS_MANAGER,0)
        FROM PAY2_DECREE D
        WHERE D.EMP_ID=@EMP_ID AND D.IS_CONFIRMED=1
          AND D.EFF_FROM<=@PER_END AND (D.EFF_TO IS NULL OR D.EFF_TO>=@PER_START)
        ORDER BY D.EFF_FROM DESC,D.DEC_ID DESC;

        DECLARE cur_dec CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
            SELECT DEC_ID, EFF_FROM, ISNULL(EFF_TO, 99991231), NULLIF(SHIFT_MODE, N'')
            FROM PAY2_DECREE
            WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1
              AND EFF_FROM <= @PER_END
              AND (EFF_TO IS NULL OR EFF_TO >= @PER_START)
            ORDER BY EFF_FROM;

        OPEN cur_dec;
        FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @DEC_ACTUAL_START = CASE WHEN @DEC_FROM > @PER_START THEN @DEC_FROM ELSE @PER_START END;
            SET @DEC_ACTUAL_END   = CASE WHEN @DEC_TO < @PER_END THEN @DEC_TO ELSE @PER_END END;
            SET @DEC_ACTIVE_DAYS = 0;

            IF @DEC_ACTUAL_START <= @DEC_ACTUAL_END
                SET @DEC_ACTIVE_DAYS = (@DEC_ACTUAL_END % 100) - (@DEC_ACTUAL_START % 100) + 1;

            IF @DEC_ACTIVE_DAYS > 0
            BEGIN
                SET @PRORATE_FACTOR = CAST(@DEC_ACTIVE_DAYS AS DECIMAL(18,6)) / CAST(@MONTH_DAYS AS DECIMAL(18,6));

                SET @DAILY_NOMINAL=0; SET @DAILY_OFFICIAL=0; SET @DAILY_SEN_NOMINAL=0; SET @DAILY_SEN_OFFICIAL=0;
                SELECT
                    @DAILY_NOMINAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE = 'BASE_SAL' THEN DL.AMOUNT END),0),
                    @DAILY_OFFICIAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE = 'BASE_SAL_B' THEN DL.AMOUNT END),0),
                    @DAILY_SEN_NOMINAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE='SANOVAT_PAYE' THEN ISNULL(DL.NOMINAL_AMOUNT_OV,DL.AMOUNT) END),0),
                    @DAILY_SEN_OFFICIAL = ISNULL(MAX(CASE WHEN ID.ITEM_CODE='SANOVAT_PAYE' THEN ISNULL(DL.OFFICIAL_AMOUNT_OV,DL.AMOUNT) END),0)
                FROM PAY2_DECREE_LINE DL INNER JOIN #ItemDefSource ID ON DL.ITEM_ID = ID.ITEM_ID
                WHERE DL.DEC_ID = @DEC_ID;

                IF @DAILY_NOMINAL <= 0 OR @DAILY_OFFICIAL <= 0
                BEGIN
                    DECLARE @MissingDecreeRailMsg NVARCHAR(1000)=N'حکم پرسنل '+@EMP_LABEL+N' ناقص است: مبلغ حقوق پایه در حکم ثبت نشده. لطفاً آیتم‌های «حقوق پایه اسمی (BASE_SAL)» و «حقوق پایه رسمی (BASE_SAL_B)» را در حکم پر و حکم را تأیید کنید. (DEC_ID='+CAST(@DEC_ID AS NVARCHAR(20))+N'، بازه محاسباتی='+CAST(@DEC_ACTUAL_START AS NVARCHAR(20))+N' تا '+CAST(@DEC_ACTUAL_END AS NVARCHAR(20))+N'، BASE_SAL='+CAST(@DAILY_NOMINAL AS NVARCHAR(40))+N'، BASE_SAL_B='+CAST(@DAILY_OFFICIAL AS NVARCHAR(40))+N')';
                    RAISERROR(@MissingDecreeRailMsg,16,1);
                    RETURN;
                END;

                IF @DAILY_NOMINAL > 0 SET @HAS_NOMINAL_RATE = 1;
                IF @DAILY_OFFICIAL > 0 SET @HAS_OFFICIAL_RATE = 1;

                -- ریل‌ها عمداً مستقل‌اند؛ نبود هیچ ریل با دیگری جبران نمی‌شود.
                DECLARE cur_line CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
                    SELECT DL.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, ISNULL(DL.AMOUNT, 0),
                        DL.SHIFT_MODE_OV, DL.NOMINAL_AMOUNT_OV, DL.OFFICIAL_AMOUNT_OV,
                        ISNULL(DL.BASIS_OV, ID.CALC_BASIS), ISNULL(DL.INS_OV, ID.INS_SUBJECT), ISNULL(DL.TAX_OV, ID.TAX_SUBJECT), ID.PAY_BASE_DAYS, ID.INS_BASE_DAYS
                    FROM PAY2_DECREE_LINE DL INNER JOIN #ItemDefSource ID ON DL.ITEM_ID = ID.ITEM_ID
                    WHERE DL.DEC_ID = @DEC_ID AND ID.IS_ACTIVE = 1 AND ID.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                    ORDER BY ID.SORT_ORDER;

                OPEN cur_line;
                FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @DL_NOMINAL_AMOUNT_OV, @DL_OFFICIAL_AMOUNT_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @OV_INS = NULL; SET @OV_TAX = NULL; SET @OV_BASIS = NULL;
                    SELECT TOP 1 @OV_INS = INS_OV, @OV_TAX = TAX_OV, @OV_BASIS = BASIS_OV
                    FROM PAY2_OVERRIDE WHERE EMP_ID = @EMP_ID AND ITEM_ID = @ITEM_ID
                      AND VALID_FROM/100 <= @PERIOD_DATE/100
                      AND (VALID_TO IS NULL OR VALID_TO/100 >= @PERIOD_DATE/100)
                    ORDER BY VALID_FROM DESC;

                    IF @OV_INS IS NOT NULL SET @ITEM_INS = @OV_INS;
                    IF @OV_TAX IS NOT NULL SET @ITEM_TAX = @OV_TAX;
                    IF @OV_BASIS IS NOT NULL SET @ITEM_BASIS = @OV_BASIS;

                    SET @PAY_DAYS      = (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @PRORATE_FACTOR;
                    SET @BASE_DAYS_RAW = (CASE @ITEM_PBD WHEN 1 THEN @DAYS ELSE @DAYSB END);
                    SET @INS_DAYS      = (CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END) * @PRORATE_FACTOR;
                    SET @INS_DAYS_RAW  = (CASE @ITEM_IBD WHEN 1 THEN @DAYS ELSE @DAYSB END);

                    IF @ITEM_CODE = 'SANOVAT_PAYE'
                    BEGIN
                        SET @CALC_AMOUNT = CAST(ISNULL(@DL_OFFICIAL_AMOUNT_OV, @ITEM_AMOUNT) * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(ISNULL(@DL_NOMINAL_AMOUNT_OV, @ITEM_AMOUNT) * @INS_DAYS AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE IN ('BASE_SAL', 'BASE_SAL_B')
                    BEGIN
                        SET @CALC_AMOUNT     = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE IN ('HOME','CHILDREN','GROCERY')
                    BEGIN
                        SET @FULL_MONTH     = CASE WHEN @BASE_DAYS_RAW >= 28 THEN CAST(@ITEM_AMOUNT AS BIGINT) ELSE CAST(@ITEM_AMOUNT * (@BASE_DAYS_RAW / 30.0) AS BIGINT) END;
                        SET @FULL_MONTH_INS = CASE WHEN @INS_DAYS_RAW  >= 28 THEN CAST(@ITEM_AMOUNT AS BIGINT) ELSE CAST(@ITEM_AMOUNT * (@INS_DAYS_RAW  / 30.0) AS BIGINT) END;
                        SET @CALC_AMOUNT     = CAST(@FULL_MONTH     * @PRORATE_FACTOR AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@FULL_MONTH_INS * @PRORATE_FACTOR AS BIGINT);
                    END
                    ELSE IF @ITEM_CODE = 'NAHAR'
                    BEGIN
                        SET @NAHAR_DAYS = (@DAYSB - @FRID_COUNT - @LEAVE_DAYS + @TDAYS) * @PRORATE_FACTOR;
                        SET @CALC_AMOUNT = CASE WHEN @NAHAR_DAYS > 0 THEN CAST(@ITEM_AMOUNT * @NAHAR_DAYS AS BIGINT) ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT) END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_CODE = 'SHIFT'
                    BEGIN
                        SET @EFF_SHIFT_MODE = COALESCE(NULLIF(@DL_SHIFT_MODE_OV, N''), @DEC_SHIFT_MODE, @WS_SHIFT_MODE, @SHIFT_MODE, 'PCT');
                        IF @EFF_SHIFT_MODE = 'FIXED'
                        BEGIN
                            SET @CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                            SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * (@INS_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT);
                        END
                        ELSE
                        BEGIN
                            -- حق شیفت پرداختی از رسمی، حق شیفت بیمه/مالیات از اسمی
                            SET @CALC_AMOUNT = CAST(ROUND(((@DAILY_OFFICIAL + @DAILY_SEN_OFFICIAL) * @PAY_DAYS * @ITEM_AMOUNT / 100.0), 0) AS BIGINT);
                            SET @INS_CALC_AMOUNT = CAST(ROUND(((@DAILY_NOMINAL + @DAILY_SEN_NOMINAL) * @INS_DAYS * @ITEM_AMOUNT / 100.0), 0) AS BIGINT);
                        END
                    END
                    ELSE IF @ITEM_BASIS = 3
                    BEGIN
                        SET @CALC_AMOUNT =
                            CASE @ITEM_CODE
                                WHEN 'OT_NORMAL'  THEN CAST(@ITEM_AMOUNT * @OT_NORMAL_H  AS BIGINT)
                                WHEN 'OT_HOLIDAY' THEN CAST(@ITEM_AMOUNT * @OT_HOLIDAY_H AS BIGINT)
                                WHEN 'OT_ADMIN'   THEN CAST(@ITEM_AMOUNT * @OT_ADMIN_H   AS BIGINT)
                                ELSE CAST(@ITEM_AMOUNT * @PAY_DAYS * @OT_HOUR_BASE AS BIGINT)
                            END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_BASIS = 2
                    BEGIN
                        SET @CALC_AMOUNT = CASE
                            WHEN @MONTHLY_PRORATE = 1
                                THEN CAST(@ITEM_AMOUNT * (@PAY_DAYS / CAST(@MONTH_DAYS AS DECIMAL(5,2))) AS BIGINT)
                            ELSE CAST(@ITEM_AMOUNT * @PRORATE_FACTOR AS BIGINT)
                        END;
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END
                    ELSE IF @ITEM_BASIS = 1
                    BEGIN
                        SET @CALC_AMOUNT     = CAST(@ITEM_AMOUNT * @PAY_DAYS AS BIGINT);
                        SET @INS_CALC_AMOUNT = CAST(@ITEM_AMOUNT * @INS_DAYS AS BIGINT);
                    END
                    ELSE
                    BEGIN
                        SET @CALC_AMOUNT = ISNULL(@ITEM_AMOUNT, 0);
                        SET @INS_CALC_AMOUNT = @CALC_AMOUNT;
                    END

                    INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
                    VALUES (@ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @CALC_AMOUNT, @INS_CALC_AMOUNT, @ITEM_INS, @ITEM_TAX);

                    FETCH NEXT FROM cur_line INTO @ITEM_ID, @ITEM_CODE, @ITEM_TYPE, @ITEM_AMOUNT, @DL_SHIFT_MODE_OV, @DL_NOMINAL_AMOUNT_OV, @DL_OFFICIAL_AMOUNT_OV, @ITEM_BASIS, @ITEM_INS, @ITEM_TAX, @ITEM_PBD, @ITEM_IBD;
                END;
                CLOSE cur_line; DEALLOCATE cur_line;
            END;

            FETCH NEXT FROM cur_dec INTO @DEC_ID, @DEC_FROM, @DEC_TO, @DEC_SHIFT_MODE;
        END;
        CLOSE cur_dec; DEALLOCATE cur_dec;

        -- هیچ Fallback خاموشی بین BASE_SAL و BASE_SAL_B مجاز نیست.

        IF @HAS_NOMINAL_RATE=1 AND @HAS_OFFICIAL_RATE=1
            SET @HAS_BOTH_SAL = 1;

        IF @HAS_NOMINAL_RATE=0
        BEGIN
            SET @DEC_ANY_COUNT = (SELECT COUNT(*) FROM PAY2_DECREE WHERE EMP_ID=@EMP_ID AND EFF_FROM<=@PER_END AND (EFF_TO IS NULL OR EFF_TO>=@PER_START));
            SET @DEC_UNCONFIRMED_COUNT = (SELECT COUNT(*) FROM PAY2_DECREE WHERE EMP_ID=@EMP_ID AND ISNULL(IS_CONFIRMED,0)=0 AND EFF_FROM<=@PER_END AND (EFF_TO IS NULL OR EFF_TO>=@PER_START));
            SET @DEC_CONFIRMED_NO_LINE_COUNT = (SELECT COUNT(*) FROM PAY2_DECREE D WHERE D.EMP_ID=@EMP_ID AND D.IS_CONFIRMED=1 AND D.EFF_FROM<=@PER_END AND (D.EFF_TO IS NULL OR D.EFF_TO>=@PER_START) AND NOT EXISTS(SELECT 1 FROM PAY2_DECREE_LINE DL WHERE DL.DEC_ID=D.DEC_ID));

            SET @DEC_REASON =
                CASE
                    WHEN @DEC_UNCONFIRMED_COUNT > 0 THEN N'حکم این ماه ثبت شده ولی «تأیید» نشده است؛ حکم را تأیید کنید.'
                    WHEN @DEC_CONFIRMED_NO_LINE_COUNT > 0 THEN N'حکم این ماه هیچ آیتم حقوقی ندارد؛ آیتم‌های حکم (مثلاً از قالب حکم) را ثبت کنید.'
                    WHEN @DEC_ANY_COUNT = 0 THEN N'هیچ حکمی برای این ماه وجود ندارد (حکم قبلی تمام شده است)؛ حکم جدید ثبت و تأیید کنید.'
                    ELSE N'حکم تأییدشده‌ای با بازه معتبر در این ماه یافت نشد؛ تاریخ شروع/پایان حکم را بررسی کنید.'
                END;

            DECLARE @MissingNominalMsg NVARCHAR(1000) = N'محاسبه انجام نشد: برای پرسنل ' + @EMP_LABEL + N' حکم تأییدشده با حقوق پایه اسمی (BASE_SAL) در این ماه وجود ندارد. ' + @DEC_REASON;
            RAISERROR(@MissingNominalMsg,16,1);
            RETURN;
        END;
        IF @HAS_OFFICIAL_RATE=0
        BEGIN
            DECLARE @MissingOfficialMsg NVARCHAR(1000)=N'محاسبه انجام نشد: برای پرسنل '+@EMP_LABEL+N' حقوق پایه رسمی (BASE_SAL_B) در حکم این ماه ثبت نشده یا صفر است؛ حکم را کامل و تأیید کنید.';
            RAISERROR(@MissingOfficialMsg,16,1);
            RETURN;
        END;

        -- گام ۶ — افزودن آیتم‌های متغیر
        SET @TOTAL_NOMINAL_BASE = ISNULL((
            SELECT SUM(INS_AMOUNT) FROM @ItemCalc
            WHERE ITEM_CODE IN ('BASE_SAL','SANOVAT_PAYE')
        ), 0);

        SET @TOTAL_OFFICIAL_BASE = ISNULL((
            SELECT SUM(AMOUNT) FROM @ItemCalc
            WHERE ITEM_CODE IN ('BASE_SAL_B','SANOVAT_PAYE')
        ), 0);

        -- ریل رسمی (برای پرداختی اضافه‌کار)
        IF @DAYSB > 0 AND @OT_HOUR_BASE > 0
        BEGIN
            SET @EFFECTIVE_HOURLY = ISNULL((CAST(@TOTAL_OFFICIAL_BASE AS DECIMAL(18,2)) / @DAYSB) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END


        -- ریل اسمی (برای بیمه و مالیات اضافه‌کار)
        IF @DAYS > 0 AND @OT_HOUR_BASE > 0
        BEGIN
            SET @OFFICIAL_HOURLY = ISNULL((CAST(@TOTAL_NOMINAL_BASE AS DECIMAL(18,2)) / @DAYS) / NULLIF(@OT_HOUR_BASE, 0), 0);
        END


        IF @OT_NORMAL_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_NORMAL')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_NORMAL', 2, CAST(@EFFECTIVE_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_NORMAL_H * @OT_NORMAL_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'OT_NORMAL';

        IF @OT_HOLIDAY_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_HOLIDAY')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_HOLIDAY', 2, CAST(@EFFECTIVE_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_HOLIDAY_H * @OT_HOLIDAY_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'OT_HOLIDAY';

        IF @OT_ADMIN_H > 0 AND NOT EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'OT_ADMIN')
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'OT_ADMIN', 2, CAST(@EFFECTIVE_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), CAST(@OFFICIAL_HOURLY * @OT_ADMIN_H * @OT_NORMAL_MULT AS BIGINT), INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'OT_ADMIN';

        IF @PERF_AMOUNT > 0
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'PERF_BONUS', 2, @PERF_AMOUNT, @PERF_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'PERF_BONUS';

        IF @TRANSP_AMOUNT > 0
            INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
            SELECT ITEM_ID, 'TRANSP', 2, @TRANSP_AMOUNT, @TRANSP_AMOUNT, INS_SUBJECT, TAX_SUBJECT FROM #ItemDefSource WHERE ITEM_CODE = 'TRANSP';

        INSERT INTO @ItemCalc (ITEM_ID, ITEM_CODE, ITEM_TYPE, AMOUNT, INS_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT AV.ITEM_ID, ID.ITEM_CODE, ID.ITEM_TYPE, AV.VALUE, AV.VALUE, ID.INS_SUBJECT, ID.TAX_SUBJECT
        FROM PAY2_ATT_VALUE AV INNER JOIN #ItemDefSource ID ON AV.ITEM_ID = ID.ITEM_ID
        WHERE AV.PER_ID = @PER_ID AND AV.EMP_ID = @EMP_ID AND AV.VALUE <> 0
          AND NOT EXISTS (SELECT 1 FROM @ItemCalc X WHERE X.ITEM_ID = AV.ITEM_ID);

        -- برای دوره‌های قبل از تاریخ اثر، قاعده اختصاصی مشتری اعمال نمی‌شود.
        -- Runهای نهایی نیز طبق کنترل موتور قابل بازمحاسبه نیستند.
        IF @INS_NON_SUBJECT_EFFECTIVE_FROM>0 AND @PERIOD_DATE/100>=@INS_NON_SUBJECT_EFFECTIVE_FROM/100
            UPDATE @ItemCalc SET INS_SUBJECT=0
            WHERE ITEM_CODE IN('SHIFT','OT_NORMAL','OT_HOLIDAY','OT_ADMIN');

        -- گام ۷ — محاسبه بیمه
        SET @GROSS_PAY = 0; SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0; SET @INS_EMPLOYER_BASE = 0; SET @INS_UNEMPLOYMENT = 0;

        -- ناخالص پرداختی بر اساس حقوق رسمی (با جلوگیری از دوبارشماری)
        SELECT @GROSS_PAY = ISNULL(SUM(AMOUNT), 0)
        FROM @ItemCalc
        WHERE ITEM_TYPE IN (1, 2) AND (@HAS_BOTH_SAL = 0 OR ITEM_CODE <> 'BASE_SAL');

        SELECT @NOMINAL_GROSS = ISNULL(SUM(INS_AMOUNT), 0)
        FROM @ItemCalc
        WHERE ITEM_TYPE IN (1, 2) AND (@HAS_BOTH_SAL = 0 OR ITEM_CODE <> 'BASE_SAL_B');

        -- مبنای بیمه بر اساس حقوق اسمی (با استفاده از INS_AMOUNT)
        SET @INS_OFFICIAL_VALID = 0; SET @INS_DROP_SAL = NULL;
        IF @HAS_BOTH_SAL = 1
        BEGIN
            IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B' AND INS_SUBJECT = 1 AND INS_AMOUNT <> 0)
                SET @INS_OFFICIAL_VALID = 1;
            SET @INS_DROP_SAL = 'BASE_SAL_B';
        END;

        SELECT @INS_BASE = ISNULL(SUM(INS_AMOUNT), 0)
        FROM @ItemCalc
        WHERE INS_SUBJECT = 1 AND ITEM_TYPE IN (1, 2) AND (@INS_DROP_SAL IS NULL OR ITEM_CODE <> @INS_DROP_SAL);

        SET @EFFECTIVE_INS_CEILING = CAST((@INS_CEILING / 30.0) * @DAYS AS BIGINT);
        IF @INS_CEILING_APPLY = 1 AND @INS_TYPE <> 3
            SET @INS_BASE = CASE WHEN @INS_BASE > @EFFECTIVE_INS_CEILING THEN @EFFECTIVE_INS_CEILING ELSE @INS_BASE END;

        IF @INS_TYPE = 3
        BEGIN
            SET @INS_BASE = 0; SET @INS_WORKER = 0; SET @INS_EMPLOYER = 0; SET @INS_EMPLOYER_BASE = 0; SET @INS_UNEMPLOYMENT = 0;
        END;
        ELSE
        BEGIN
            SET @INS_WORKER = ISNULL(CAST(@INS_BASE * @INS_WORKER_RATE AS BIGINT), 0);
            SET @EMP_IS_JANBAZ = ISNULL((SELECT IS_JANBAZ FROM #EmployeeSource WHERE EMP_ID = @EMP_ID), 0);
            SET @JANBAZ_RATE = ISNULL(CAST((SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY='INS_JANBAZ_RATE') AS DECIMAL(6,4)), 0.18);

            IF @EMP_IS_JANBAZ = 1
                SET @INS_EMPLOYER_BASE = ISNULL(CAST(@INS_BASE * @JANBAZ_RATE AS BIGINT), 0);
            ELSE
            BEGIN
                SET @INS_EMPLOYER_BASE = ISNULL(CAST(@INS_BASE * @INS_EMPLOYER_RATE AS BIGINT), 0);
                SET @INS_UNEMPLOYMENT = CASE WHEN ISNULL(@IS_MANAGER,0)=0 THEN ISNULL(CAST(@INS_BASE * @INS_UNEMP_RATE AS BIGINT),0) ELSE 0 END;
            END;
            SET @INS_EMPLOYER = @INS_EMPLOYER_BASE + @INS_UNEMPLOYMENT;
        END;

        -- گام ۸ — محاسبه مالیات
        SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        IF @TAX_EXEMPT_FLAG = 1
        BEGIN
            SET @TAX_BASE = 0; SET @TAX_AMOUNT = 0;
        END;
        ELSE
        BEGIN
            -- مالیات کاملاً بر اساس حقوق اسمی و مقادیر INS_AMOUNT محاسبه می‌شود
            SET @TAX_OFFICIAL_VALID = 0; SET @TAX_DROP_SAL = NULL;
            IF @HAS_BOTH_SAL = 1
            BEGIN
                IF EXISTS (SELECT 1 FROM @ItemCalc WHERE ITEM_CODE = 'BASE_SAL_B' AND TAX_SUBJECT = 1 AND INS_AMOUNT <> 0)
                    SET @TAX_OFFICIAL_VALID = 1;
                SET @TAX_DROP_SAL = 'BASE_SAL_B';
            END;

            SELECT @TAX_BASE = ISNULL(SUM(INS_AMOUNT), 0)
            FROM @ItemCalc
            WHERE TAX_SUBJECT = 1 AND ITEM_TYPE IN (1, 2) AND (@TAX_DROP_SAL IS NULL OR ITEM_CODE <> @TAX_DROP_SAL);

            IF @TAX_DEDUCT_INS = 1 SET @TAX_BASE = @TAX_BASE - @INS_WORKER;
            SET @TAX_BASE = CASE WHEN @TAX_BASE > @TAX_EXEMPT THEN @TAX_BASE - @TAX_EXEMPT ELSE 0 END;
            IF @TAX_DEP_APPLY = 1 AND @REGION_DEP > 0 SET @TAX_BASE = CAST(@TAX_BASE * (1.0 - @REGION_DEP / 100.0) AS BIGINT);
            SET @TAX_AMOUNT = ISNULL([dbo].[FN_PAY2_CALC_TAX](@TAX_BASE * 12, @TAX_YEAR) / 12, 0);
            IF @TAX_AMOUNT < 0 SET @TAX_AMOUNT = 0;
        END;

        SET @ADVANCE_DED = 0;
        IF @ADV_ENABLED = 1 SELECT @ADVANCE_DED = ISNULL(ADVANCE_DEDUCTION, 0) FROM #AdvResult WHERE EMP_ID = @EMP_ID;

        SET @LOAN_DED = 0;
        SELECT @LOAN_DED = ISNULL(SUM(LS.AMOUNT), 0) FROM PAY2_LOAN_SCHED LS INNER JOIN PAY2_LOAN L ON LS.LOAN_ID = L.LOAN_ID
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1 AND LS.DUE_PERIOD = @PERIOD_DATE AND LS.RUN_ID IS NULL;

        -- کسر کار: هر ساعت دقیقاً معادل «نرخ یک ساعت کار عادی» (ضریب ۱، بدون ضریب اضافه‌کار)
        SET @SHORTAGE_DED = 0;
        IF @SHORTAGE_H > 0 AND @EFFECTIVE_HOURLY > 0
            SET @SHORTAGE_DED = ISNULL(CAST(@EFFECTIVE_HOURLY * @SHORTAGE_H AS BIGINT), 0);

        SET @OTHER_DED = ISNULL(@KASR_OTHER, 0) + @SHORTAGE_DED;
        SET @TOTAL_DED = @INS_WORKER + @TAX_AMOUNT + @LOAN_DED + @ADVANCE_DED + @OTHER_DED;

        -- فرمول تراز: پیدا کردن اختلاف گرد کردن و اعمال آن روی ناخالص پرداختی
        DECLARE @RAW_NET BIGINT = @GROSS_PAY - @TOTAL_DED;
        SET @NET_PAY = @RAW_NET;

        IF @ROUND_MODE > 1
            SET @NET_PAY = ISNULL(ROUND(CAST(@RAW_NET AS FLOAT) / @ROUND_MODE, 0) * @ROUND_MODE, 0);

        -- اختلافی که بخاطر گرد کردن ایجاد شده را به ناخالص اضافه/کم میکنیم تا معادله تراز بماند
        DECLARE @ROUNDING_DIFF BIGINT = @NET_PAY - @RAW_NET;
        SET @GROSS_PAY = @GROSS_PAY + @ROUNDING_DIFF;

        SET @LEAVE_BAL_DAYS = NULL;
        SELECT @LEAVE_BAL_DAYS = CAST(BALANCE_MIN AS DECIMAL(10,2)) / 440.0 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;

        SET @LOAN_BAL = NULL;
        SELECT @LOAN_BAL = ISNULL(SUM(BALANCE), 0) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID;

        INSERT INTO PAY2_RUN_LINE (
            RUN_ID, EMP_ID, WORK_DAYS, GROSS_PAY, INS_BASE, INS_WORKER, INS_EMPLOYER, TAX_BASE, TAX_AMOUNT,
            LOAN_DED, ADVANCE_DED, OTHER_DED, TOTAL_DED, NET_PAY, LEAVE_BAL_DAYS, LOAN_BALANCE, ADVANCE_BALANCE_SNAP,
            NOMINAL_GROSS, NOMINAL_DAYS, INS_EMPLOYER_BASE, INS_UNEMPLOYMENT, ROUNDING_ADJ, HIRE_DATE_SNAP, FIRE_DATE_SNAP
        ) VALUES (
            @NEW_RUN_ID, @EMP_ID, @DAYSB, @GROSS_PAY, @INS_BASE, @INS_WORKER, @INS_EMPLOYER, @TAX_BASE, @TAX_AMOUNT,
            @LOAN_DED, @ADVANCE_DED, @OTHER_DED, @TOTAL_DED, @NET_PAY, @LEAVE_BAL_DAYS, @LOAN_BAL, @ADVANCE_DED,
            @NOMINAL_GROSS, @DAYS, @INS_EMPLOYER_BASE, @INS_UNEMPLOYMENT, @ROUNDING_DIFF,
            (SELECT HIRE_DATE FROM #EmployeeSource WHERE EMP_ID=@EMP_ID),
            (SELECT FIRE_DATE FROM #EmployeeSource WHERE EMP_ID=@EMP_ID)
        );

        INSERT INTO PAY2_RUN_EMP_SNAPSHOT
        (
            RUN_ID,EMP_ID,EMP_CODE,FIRST_NAME,LAST_NAME,FATHER_NAME,NATIONAL_CODE,INS_CODE,
            ID_NUMBER,BIRTH_PLACE,BIRTH_DATE,GENDER,INS_TYPE_SNAP,TAX_EXEMPT_SNAP,
            MARITAL_SNAP,NATIONALITY_SNAP,MOBILE,JOB_CODE_SNAP,JOB_NAME_SNAP,HIRE_DATE_SNAP,FIRE_DATE_SNAP,
            IS_MANAGER_SNAP,IS_JANBAZ_SNAP,REGION_DEPRIVATION_SNAP,ACC_T_SNAP
        )
        SELECT @NEW_RUN_ID,E.EMP_ID,E.EMP_CODE,E.FIRST_NAME,E.LAST_NAME,E.FATHER_NAME,E.NATIONAL_CODE,E.INS_CODE,
               E.ID_NUMBER,E.BIRTH_PLACE,E.BIRTH_DATE,E.GENDER,ISNULL(E.INS_TYPE,1),ISNULL(E.TAX_EXEMPT,0),
               E.MARITAL,E.NATIONALITY,E.MOBILE,E.JOB_CODE,E.JOB_NAME,E.HIRE_DATE,E.FIRE_DATE,
               @IS_MANAGER,E.IS_JANBAZ,E.REGION_DEPRIVATION,E.ACC_T
        FROM #EmployeeSource E
        WHERE E.EMP_ID=@EMP_ID;

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID, EMP_ID, ITEM_ID, AMOUNT, NOMINAL_AMOUNT, ITEM_CODE_SNAP, ITEM_NAME_SNAP, CALC_BASIS_SNAP, ITEM_TYPE_SNAP, INS_SUBJECT_AMOUNT, TAX_SUBJECT_AMOUNT, INS_SUBJECT, TAX_SUBJECT)
        SELECT @NEW_RUN_ID, @EMP_ID, C.ITEM_ID, SUM(C.AMOUNT), SUM(C.INS_AMOUNT), MAX(C.ITEM_CODE), MAX(I.ITEM_NAME), MAX(I.CALC_BASIS), MAX(C.ITEM_TYPE),
               SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.INS_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END),
               SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.TAX_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END),
               CASE WHEN SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.INS_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END)<>0 THEN 1 ELSE 0 END,
               CASE WHEN SUM(CASE WHEN C.ITEM_CODE<>'BASE_SAL_B' AND C.TAX_SUBJECT=1 THEN C.INS_AMOUNT ELSE 0 END)<>0 THEN 1 ELSE 0 END
        FROM @ItemCalc C INNER JOIN #ItemDefSource I ON I.ITEM_ID=C.ITEM_ID
        GROUP BY C.ITEM_ID
        HAVING SUM(C.AMOUNT) <> 0 OR SUM(C.INS_AMOUNT) <> 0 OR MAX(C.ITEM_CODE) IN ('BASE_SAL','BASE_SAL_B','SANOVAT_PAYE');

        INSERT INTO PAY2_RUN_DETAIL (RUN_ID,EMP_ID,ITEM_ID,AMOUNT,NOMINAL_AMOUNT,ITEM_CODE_SNAP,ITEM_NAME_SNAP,CALC_BASIS_SNAP,ITEM_TYPE_SNAP,INS_SUBJECT_AMOUNT,TAX_SUBJECT_AMOUNT,INS_SUBJECT,TAX_SUBJECT)
        SELECT @NEW_RUN_ID,@EMP_ID,I.ITEM_ID,V.AMOUNT,0,I.ITEM_CODE,I.ITEM_NAME,I.CALC_BASIS,I.ITEM_TYPE,0,0,0,0
        FROM #ItemDefSource I
        CROSS APPLY (VALUES(CASE I.ITEM_CODE WHEN 'INS_DED' THEN @INS_WORKER WHEN 'TAX_DED' THEN @TAX_AMOUNT WHEN 'LOAN_DED' THEN @LOAN_DED WHEN 'ADVANCE_DED' THEN @ADVANCE_DED ELSE 0 END)) V(AMOUNT)
        WHERE I.ITEM_ID IN(@INS_DED_ID,@TAX_DED_ID,@LOAN_DED_ID,@ADV_DED_ID) AND V.AMOUNT>0;

        UPDATE PAY2_LOAN_SCHED SET RUN_ID = @NEW_RUN_ID, PAID_AT = GETDATE()
        WHERE DUE_PERIOD = @PERIOD_DATE AND RUN_ID IS NULL AND LOAN_ID IN (SELECT LOAN_ID FROM PAY2_LOAN WHERE EMP_ID=@EMP_ID AND IS_ACTIVE=1);

        UPDATE L
        SET L.PAID_INST = L.PAID_INST + (
            SELECT COUNT(1) FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @NEW_RUN_ID
        )
        FROM PAY2_LOAN L
        WHERE L.EMP_ID = @EMP_ID AND L.IS_ACTIVE = 1
          AND EXISTS (SELECT 1 FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @NEW_RUN_ID);

        SET @LEAVE_MIN_USED = CAST(@LEAVE_DAYS * 440 AS INT);
        IF @LEAVE_MIN_USED > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000)
            BEGIN
                UPDATE PAY2_LEAVE_BAL SET USED_MIN = USED_MIN + @LEAVE_MIN_USED, UPDATED_AT = GETDATE()
                WHERE EMP_ID = @EMP_ID AND YEAR = @PERIOD_DATE / 10000;
            END
            ELSE
            BEGIN
                INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN, CARRIED_OUT_MIN, UPDATED_AT)
                VALUES (@EMP_ID, @PERIOD_DATE / 10000, 11440, @LEAVE_MIN_USED, 0, 0, GETDATE());
            END
        END;

        FETCH NEXT FROM cur_emp INTO @EMP_ID, @INS_TYPE, @TAX_EXEMPT_FLAG, @REGION_DEP, @ACC_T;
    END;

    CLOSE cur_emp; DEALLOCATE cur_emp;
    DROP TABLE #AdvResult;

    UPDATE PAY2_PERIOD SET STATUS = 3 WHERE PER_ID = @PER_ID;

END;
GO

-- ================================================================
-- ۳. SP_PAY2_CALC_SETTLE — محاسبه تسویه حساب پرسنل
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CALC_SETTLE]
    @EMP_ID        INT,
    @WS_ID         INT,
    @SETTLE_DATE   BIGINT,
    @END_DATE      BIGINT,
    @PREV_CREDIT   BIGINT = 0,
    @OTHER_INCOME  BIGINT = 0,
    @OTHER_DED     BIGINT = 0,
    @CALC_BY       INT    = NULL,
    @NEW_SET_ID    INT    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @BONUS_MODE          NVARCHAR(20),
        @BONUS_CUSTOM_DAYS   INT,
        @MIN_WAGE_DAILY      BIGINT,
        @MIN_WAGE_MONTHLY    BIGINT,
        @EIDI_MIN_DAYS       INT,
        @EIDI_MAX_DAYS       INT,
        @SENIORITY_MODE      NVARCHAR(20),
        @SENIORITY_FIXED_AMT BIGINT,
        @TAX_YEAR            SMALLINT,
        @TAX_EXEMPT_MONTHLY  BIGINT,
        @LEAVE_MINS_PER_DAY  INT;

    SELECT
        @BONUS_MODE          = ISNULL(MAX(CASE WHEN CFG_KEY='BONUS_MODE'          THEN CFG_VALUE END), 'MIN_WAGE'),
        @BONUS_CUSTOM_DAYS   = ISNULL(MAX(CASE WHEN CFG_KEY='BONUS_CUSTOM_DAYS'   THEN CAST(CFG_VALUE AS INT) END), 60),
        @MIN_WAGE_DAILY      = ISNULL(MAX(CASE WHEN CFG_KEY='MIN_WAGE_DAILY'      THEN CAST(CFG_VALUE AS BIGINT) END), 73200),
        @MIN_WAGE_MONTHLY    = ISNULL(MAX(CASE WHEN CFG_KEY='MIN_WAGE_MONTHLY'    THEN CAST(CFG_VALUE AS BIGINT) END), 2196000),
        @EIDI_MIN_DAYS       = ISNULL(MAX(CASE WHEN CFG_KEY='EIDI_MIN_DAYS'       THEN CAST(CFG_VALUE AS INT) END), 60),
        @EIDI_MAX_DAYS       = ISNULL(MAX(CASE WHEN CFG_KEY='EIDI_MAX_DAYS'       THEN CAST(CFG_VALUE AS INT) END), 90),
        @SENIORITY_MODE      = ISNULL(MAX(CASE WHEN CFG_KEY='SENIORITY_MODE'      THEN CFG_VALUE END), 'LAST_SAL'),
        @SENIORITY_FIXED_AMT = ISNULL(MAX(CASE WHEN CFG_KEY='SENIORITY_FIXED_AMT' THEN CAST(CFG_VALUE AS BIGINT) END), 0),
        @TAX_YEAR            = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_YEAR'            THEN CAST(CFG_VALUE AS SMALLINT) END), 1403),
        @TAX_EXEMPT_MONTHLY  = ISNULL(MAX(CASE WHEN CFG_KEY='TAX_EXEMPT_MONTHLY'  THEN CAST(CFG_VALUE AS BIGINT) END), 84000000),
        @LEAVE_MINS_PER_DAY  = ISNULL(MAX(CASE WHEN CFG_KEY='LEAVE_MINS_PER_DAY'  THEN CAST(CFG_VALUE AS INT) END), 440)
    FROM PAY2_CONFIG
    WHERE CFG_KEY IN ('BONUS_MODE','BONUS_CUSTOM_DAYS','MIN_WAGE_DAILY','MIN_WAGE_MONTHLY','EIDI_MIN_DAYS','EIDI_MAX_DAYS','SENIORITY_MODE','SENIORITY_FIXED_AMT','TAX_YEAR','TAX_EXEMPT_MONTHLY','LEAVE_MINS_PER_DAY');

    DECLARE @HIRE_DATE BIGINT, @EMP_FIRST_NAME NVARCHAR(50), @EMP_LAST_NAME NVARCHAR(50);
    SELECT @HIRE_DATE = HIRE_DATE, @EMP_FIRST_NAME = FIRST_NAME, @EMP_LAST_NAME = LAST_NAME FROM PAY2_EMPLOYEE WHERE EMP_ID = @EMP_ID;

    IF @HIRE_DATE IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_CALC_SETTLE: پرسنل %d یافت نشد.', 16, 1, @EMP_ID);
        RETURN;
    END;

    DECLARE @PREV_SET_ID INT = NULL, @PREV_SEN_DAYS INT = 0, @PREV_SETTLE_DATE BIGINT = NULL;
    SELECT TOP 1 @PREV_SET_ID = SET_ID, @PREV_SEN_DAYS = SENIORITY_DAYS + PREV_SENIORITY_DAYS, @PREV_SETTLE_DATE = SETTLE_DATE
    FROM PAY2_SETTLEMENT WHERE EMP_ID = @EMP_ID AND STATUS >= 2 ORDER BY SETTLE_DATE DESC;

   -- سابقه کل بر اساس استاندارد ۳۶۵ روزه (جایگزین خط اشتباه قبلی)
    DECLARE @START_Y INT = @HIRE_DATE / 10000;
    DECLARE @START_M INT = (@HIRE_DATE / 100) % 100;
    DECLARE @START_D INT = @HIRE_DATE % 100;

    DECLARE @END_Y INT = @END_DATE / 10000;
    DECLARE @END_M INT = (@END_DATE / 100) % 100;
    DECLARE @END_D INT = @END_DATE % 100;

    DECLARE @DAYS_START INT = CASE WHEN @START_M <= 6 THEN (@START_M - 1) * 31 + @START_D ELSE (6 * 31) + (@START_M - 7) * 30 + @START_D END;
    DECLARE @DAYS_END   INT = CASE WHEN @END_M <= 6 THEN (@END_M - 1) * 31 + @END_D ELSE (6 * 31) + (@END_M - 7) * 30 + @END_D END;

    -- محاسبه دقیق روزهای بین دو تاریخ شمسی با احتساب سال‌های ۳۶۵ روزه
    DECLARE @SENIORITY_DAYS INT = ((@END_Y - @START_Y) * 365) + @DAYS_END - @DAYS_START - @PREV_SEN_DAYS;
    IF @SENIORITY_DAYS < 0 SET @SENIORITY_DAYS = 0;

    DECLARE @SENIORITY_YEARS  DECIMAL(6,2) = CAST(@SENIORITY_DAYS AS DECIMAL(10,2)) / 365.0;
    DECLARE @SENIORITY_FULL   INT           = @SENIORITY_DAYS / 365;
    DECLARE @SENIORITY_REMAIN INT           = @SENIORITY_DAYS % 365;

    DECLARE @LAST_DEC_ID  INT;
    SELECT TOP 1 @LAST_DEC_ID = DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @EMP_ID AND IS_CONFIRMED = 1 AND EFF_FROM <= @SETTLE_DATE AND (EFF_TO IS NULL OR EFF_TO >= @SETTLE_DATE) ORDER BY EFF_FROM DESC;

    DECLARE @LAST_DAILY_ONLY BIGINT = ISNULL((SELECT SUM(DL.AMOUNT) FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_TYPE = 1 AND ID.INS_SUBJECT = 1 AND ID.CALC_BASIS = 1), 0);
    DECLARE @LAST_MONTHLY_ONLY BIGINT = ISNULL((SELECT SUM(DL.AMOUNT) FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_TYPE = 1 AND ID.INS_SUBJECT = 1 AND ID.CALC_BASIS = 2), 0);
    DECLARE @LAST_DAILY BIGINT = @LAST_DAILY_ONLY + CAST(@LAST_MONTHLY_ONLY / 30.0 AS BIGINT);
    IF @LAST_DAILY < @MIN_WAGE_DAILY SET @LAST_DAILY = @MIN_WAGE_DAILY;
    DECLARE @LAST_SALARY BIGINT = @LAST_DAILY * 30;

    -- محاسبه روزهای عیدی محدود به سال جاری تقویمی / پس از آخرین تسویه
    DECLARE @EIDI BIGINT = 0;

    DECLARE @START_OF_YEAR BIGINT = (@END_DATE / 10000) * 10000 + 101;
    DECLARE @EIDI_START_DATE BIGINT = @HIRE_DATE;

    IF @START_OF_YEAR > @EIDI_START_DATE SET @EIDI_START_DATE = @START_OF_YEAR;
    IF @PREV_SETTLE_DATE IS NOT NULL AND @PREV_SETTLE_DATE > @EIDI_START_DATE SET @EIDI_START_DATE = @PREV_SETTLE_DATE;

    DECLARE @END_M_EIDI INT = (@END_DATE / 100) % 100;
    DECLARE @END_D_EIDI INT = @END_DATE % 100;

    DECLARE @START_M_EIDI INT = (@EIDI_START_DATE / 100) % 100;
    DECLARE @START_D_EIDI INT = @EIDI_START_DATE % 100;

    DECLARE @DAYS_SINCE_YEAR_START_END INT =
        CASE
            WHEN @END_M_EIDI <= 6 THEN (@END_M_EIDI - 1) * 31 + @END_D_EIDI
            ELSE (6 * 31) + (@END_M_EIDI - 7) * 30 + @END_D_EIDI
        END;

    DECLARE @DAYS_SINCE_YEAR_START_START INT =
        CASE
            WHEN @START_M_EIDI <= 6 THEN (@START_M_EIDI - 1) * 31 + @START_D_EIDI
            ELSE (6 * 31) + (@START_M_EIDI - 7) * 30 + @START_D_EIDI
        END;

    DECLARE @WORKED_DAYS_FOR_EIDI INT = @DAYS_SINCE_YEAR_START_END - @DAYS_SINCE_YEAR_START_START + 1;

    IF @WORKED_DAYS_FOR_EIDI < 0 SET @WORKED_DAYS_FOR_EIDI = 0;
    IF @WORKED_DAYS_FOR_EIDI > 365 SET @WORKED_DAYS_FOR_EIDI = 365;

    IF @WORKED_DAYS_FOR_EIDI > 0
    BEGIN
        DECLARE @EIDI_BASE_DAILY BIGINT = @LAST_DAILY;

        IF @BONUS_MODE = 'MIN_WAGE'
            SET @EIDI_BASE_DAILY = CASE WHEN @LAST_SALARY < @MIN_WAGE_MONTHLY THEN @LAST_DAILY ELSE (@MIN_WAGE_MONTHLY / 30) END;

        IF @BONUS_MODE = 'CUSTOM'
        BEGIN
            SET @EIDI = @LAST_DAILY * ISNULL(@BONUS_CUSTOM_DAYS, 60);
        END
        ELSE
        BEGIN
            DECLARE @CALC_EIDI BIGINT = CAST((@EIDI_BASE_DAILY * @EIDI_MIN_DAYS * CAST(@WORKED_DAYS_FOR_EIDI AS FLOAT)) / 365.0 AS BIGINT);
            DECLARE @MAX_EIDI BIGINT  = CAST((@EIDI_BASE_DAILY * @EIDI_MAX_DAYS * CAST(@WORKED_DAYS_FOR_EIDI AS FLOAT)) / 365.0 AS BIGINT);

            IF @CALC_EIDI > @MAX_EIDI SET @EIDI = @MAX_EIDI;
            ELSE SET @EIDI = @CALC_EIDI;
        END
    END;

    -- معافیت مالیات عیدی طبق قانون: معادل «یک ماه» معافیت کامل، بدون پروریت بر حسب روزهای کارکرد
    DECLARE @EIDI_TAX BIGINT = 0;
    IF @EIDI > @TAX_EXEMPT_MONTHLY
    BEGIN
        SET @EIDI_TAX = [dbo].[FN_PAY2_CALC_TAX]((@EIDI - @TAX_EXEMPT_MONTHLY) * 12, @TAX_YEAR) / 12;
    END

    DECLARE @SANAVAT BIGINT = CASE
        WHEN @SENIORITY_MODE = 'LAST_SAL' THEN @LAST_SALARY * @SENIORITY_FULL + CAST(@LAST_SALARY * @SENIORITY_REMAIN / 365.0 AS BIGINT)
        WHEN @SENIORITY_MODE = 'DAILY' THEN @LAST_DAILY * 30 * @SENIORITY_FULL + CAST(@LAST_DAILY * @SENIORITY_REMAIN AS BIGINT)
        ELSE ISNULL(@SENIORITY_FIXED_AMT, 0) * @SENIORITY_FULL END;

    DECLARE @LEAVE_BAL_MIN  INT = ISNULL((SELECT SUM(BALANCE_MIN) FROM PAY2_LEAVE_BAL WHERE EMP_ID = @EMP_ID), 0);
    IF @LEAVE_BAL_MIN < 0 SET @LEAVE_BAL_MIN = 0;

    DECLARE @LEAVE_BAL_DAYS_CALC DECIMAL(5,2) = CAST(@LEAVE_BAL_MIN AS DECIMAL(10,2)) / ISNULL(NULLIF(@LEAVE_MINS_PER_DAY, 0), 440);
    DECLARE @LEAVE_PAY BIGINT = CAST(@LEAVE_BAL_DAYS_CALC * @LAST_DAILY AS BIGINT);

    DECLARE @BON_SETTLE BIGINT = ISNULL((SELECT TOP 1 DL.AMOUNT * @SENIORITY_FULL FROM PAY2_DECREE_LINE DL INNER JOIN PAY2_ITEM_DEF ID ON DL.ITEM_ID = ID.ITEM_ID WHERE DL.DEC_ID = @LAST_DEC_ID AND ID.ITEM_CODE = 'GROCERY'), 0);
    DECLARE @LOAN_BALANCE_TOT BIGINT = ISNULL((SELECT SUM(BALANCE) FROM V_PAY2_LOAN_BALANCE WHERE EMP_ID = @EMP_ID), 0);

    INSERT INTO PAY2_SETTLEMENT (EMP_ID, WS_ID, SETTLE_DATE, HIRE_DATE, END_DATE, SENIORITY_DAYS, SENIORITY_YEARS, LAST_SALARY, LAST_DAILY, PREV_SET_ID, PREV_SENIORITY_DAYS, LEAVE_BAL_MIN, LEAVE_BAL_DAYS, EIDI, BON, LEAVE_PAY, SANAVAT, PREV_CREDIT, OTHER_INCOME, PREV_DEBIT, EIDI_TAX, LOAN_BALANCE, OTHER_DED, STATUS, CALC_METHOD, CREATED_BY)
    VALUES (@EMP_ID, @WS_ID, @SETTLE_DATE, @HIRE_DATE, @END_DATE, @SENIORITY_DAYS, @SENIORITY_YEARS, @LAST_SALARY, @LAST_DAILY, @PREV_SET_ID, @PREV_SEN_DAYS, @LEAVE_BAL_MIN, @LEAVE_BAL_DAYS_CALC, @EIDI, @BON_SETTLE, @LEAVE_PAY, @SANAVAT, @PREV_CREDIT, @OTHER_INCOME, 0, @EIDI_TAX, @LOAN_BALANCE_TOT, @OTHER_DED, 1,
        N'{""bonus_mode"":""' + @BONUS_MODE + N'"",""seniority_mode"":""' + @SENIORITY_MODE + N'"",""tax_year"":' + CAST(@TAX_YEAR AS NVARCHAR) + N'}', @CALC_BY);

    SET @NEW_SET_ID = SCOPE_IDENTITY();

END;
GO

-- ================================================================
-- ۴. SP_PAY2_GEN_DEED_SETTLE — تولید آرتیکل‌های سند تسویه
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GEN_DEED_SETTLE]
    @SET_ID  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT;
    DECLARE @WS_ID  INT;
    DECLARE @EMP_ID INT;
    DECLARE @EMP_NAME NVARCHAR(100);

    SELECT @STATUS = S.STATUS, @WS_ID = S.WS_ID, @EMP_ID = S.EMP_ID,
           @EMP_NAME = E.LAST_NAME + N' ' + E.FIRST_NAME
    FROM PAY2_SETTLEMENT S
    INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID
    WHERE S.SET_ID = @SET_ID;
    IF @STATUS IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_GEN_DEED_SETTLE: تسویه‌ای با شناسه %d یافت نشد.', 16, 1, @SET_ID);
        RETURN;
    END;
IF @STATUS <> 2
    BEGIN
        RAISERROR(N'SP_PAY2_GEN_DEED_SETTLE: تسویه %d باید نهایی (STATUS=2) شود.', 16, 1, @SET_ID);
        RETURN;
    END;

    DECLARE @ACC_SALARY_PAY  NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50), @ACC_TAX_PAYABLE NVARCHAR(50);
    DECLARE @ACC_COST_EIDI   NVARCHAR(50), @ACC_COST_SANAVAT NVARCHAR(50), @ACC_COST_LEAVE NVARCHAR(50);
    DECLARE @ACC_LOAN_HES    NVARCHAR(50), @ACC_ADV_HES NVARCHAR(50);

    SELECT
        @ACC_SALARY_PAY   = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE' THEN ACC_CODE END),
        @ACC_INS_PAYABLE  = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'    THEN ACC_CODE END),
        @ACC_TAX_PAYABLE  = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'    THEN ACC_CODE END),
        @ACC_COST_EIDI    = MAX(CASE WHEN ACC_KEY='COST_EIDI'      THEN ACC_CODE END),
        @ACC_COST_SANAVAT = MAX(CASE WHEN ACC_KEY='COST_SANAVAT'   THEN ACC_CODE END),
        @ACC_COST_LEAVE   = MAX(CASE WHEN ACC_KEY='COST_LEAVE'     THEN ACC_CODE END),
        @ACC_LOAN_HES     = MAX(CASE WHEN ACC_KEY='LOAN_HES'       THEN ACC_CODE END),
        @ACC_ADV_HES      = MAX(CASE WHEN ACC_KEY='ADV_HES'        THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    SELECT @ACC_COST_EIDI AS HES_CODE, N'هزینه عیدی' AS SHARH, EIDI AS BED, 0 AS BES, 'COST_EIDI' AS ACC_KEY, NULL AS EMP_ID
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI > 0
    UNION ALL
    SELECT @ACC_COST_SANAVAT, N'هزینه حق سنوات', SANAVAT, 0, 'COST_SANAVAT', NULL
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND SANAVAT > 0
    UNION ALL
    SELECT @ACC_COST_LEAVE, N'هزینه بازخرید مرخصی', LEAVE_PAY, 0, 'COST_LEAVE', NULL
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND LEAVE_PAY > 0
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_SALARY_PAY), N'پرداختنی تسویه حساب: ' + @EMP_NAME, 0, CAST(EIDI+BON+LEAVE_PAY+SANAVAT+PREV_CREDIT+OTHER_INCOME-PREV_DEBIT-EIDI_TAX-LOAN_BALANCE-OTHER_DED AS BIGINT), 'SETTLE_PAYABLE', S.EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_LOAN_HES), N'وصول مانده وام از تسویه: ' + @EMP_NAME, 0, LOAN_BALANCE, 'LOAN_COLLECT', @EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID AND LOAN_BALANCE > 0
    UNION ALL
    SELECT ISNULL(E.ACC_T, @ACC_ADV_HES), N'وصول بدهکاری (مساعده): ' + @EMP_NAME, 0, PREV_DEBIT, 'ADV_COLLECT', @EMP_ID
    FROM PAY2_SETTLEMENT S INNER JOIN PAY2_EMPLOYEE E ON S.EMP_ID = E.EMP_ID WHERE SET_ID=@SET_ID AND PREV_DEBIT > 0
    UNION ALL
    SELECT @ACC_TAX_PAYABLE, N'مالیات عیدی', 0, EIDI_TAX, 'TAX_PAYABLE', NULL
    FROM PAY2_SETTLEMENT WHERE SET_ID=@SET_ID AND EIDI_TAX > 0;
END;
GO

-- ================================================================
-- ۵. SP_PAY2_CLOSE_PERIOD — بستن دوره و کنترل نهایی
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CLOSE_PERIOD]
    @PER_ID  INT,
    @CLOSE_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WS_ID INT;
    DECLARE @STATUS TINYINT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT @WS_ID = WS_ID, @STATUS = STATUS, @PERIOD_DATE = PERIOD_DATE
    FROM PAY2_PERIOD WHERE PER_ID = @PER_ID;
    IF @STATUS IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_CLOSE_PERIOD: دوره‌ای با شناسه %d یافت نشد.', 16, 1, @PER_ID);
        RETURN;
    END;
IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_CLOSE_PERIOD: دوره %d در وضعیت %d است. فقط دوره باز (1) قابل بستن است.', 16, 1, @PER_ID, @STATUS);
        RETURN;
    END;

    DECLARE @EMP_NO_ATT INT;
    SELECT @EMP_NO_ATT = COUNT(*)
    FROM PAY2_EMPLOYEE E
    WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
      AND NOT EXISTS (
          SELECT 1 FROM PAY2_ATTENDANCE A
          WHERE A.PER_ID = @PER_ID AND A.EMP_ID = E.EMP_ID
      );

    IF @EMP_NO_ATT > 0
        PRINT N'هشدار: ' + CAST(@EMP_NO_ATT AS NVARCHAR) + N' پرسنل فاقد ورودی کارکرد در این دوره هستند.';

    UPDATE PAY2_PERIOD SET STATUS = 2, CLOSED_AT = GETDATE() WHERE PER_ID = @PER_ID;

    PRINT N'دوره ' + CAST(@PER_ID AS NVARCHAR) + N' (ماه ' + CAST(@PERIOD_DATE AS NVARCHAR) + N') بسته شد.';
END;
GO

-- ================================================================
-- ۶. SP_PAY2_REVERT_RUN — برگشت محاسبه (بازگشت به حالت قابل ویرایش)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_REVERT_RUN]
    @RUN_ID   INT,
    @REVERT_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- این پروسیجر همیشه داخل تراکنشِ لایه‌ی C# (ExecuteInTransactionAsync) اجرا می‌شود،
    -- چه مستقیم و چه از طریق SP_PAY2_CALC_RUN. پس تراکنش داخلی نمی‌گذاریم تا تداخلِ
    -- تراکنش تو‌در‌تو (ROLLBACK داخلی که تراکنش بیرونی را می‌کشد) رخ ندهد. XACT_ABORT
    -- تضمین می‌کند در صورت خطا، تراکنش بیرونی doomed و توسط C# رول‌بک شود.
    SET XACT_ABORT ON;

    DECLARE @STATUS TINYINT;
    DECLARE @PER_ID INT;
    DECLARE @IS_LATEST BIT;
    DECLARE @PERIOD_DATE BIGINT;

    SELECT @STATUS = R.STATUS, @PER_ID = R.PER_ID, @IS_LATEST = R.IS_LATEST, @PERIOD_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE R.RUN_ID = @RUN_ID;

    IF @PER_ID IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: محاسبه‌ای با این شناسه یافت نشد.', 16, 1);
        RETURN;
    END;

    IF @STATUS >= 3
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: سند حسابداری صادر شده — برگشت ممکن نیست.', 16, 1);
        RETURN;
    END;

    IF @IS_LATEST = 0
    BEGIN
        RAISERROR(N'SP_PAY2_REVERT_RUN: فقط آخرین نسخه (IS_LATEST=1) قابل برگشت است.', 16, 1);
        RETURN;
    END;

    -- گارد Idempotency: جلوگیری از برگشت دوباره مرخصی یا اقساط پس از حذف خروجی‌های RUN.
    IF NOT EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID)
    BEGIN
        RETURN;
    END;

    -- 1. بازگرداندن دقیق تعداد اقساط کسر شده در این RUN (فقط وام‌های درگیر همین RUN)
    UPDATE L SET L.PAID_INST = L.PAID_INST - (
        SELECT COUNT(1) FROM PAY2_LOAN_SCHED LS
        WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @RUN_ID
    )
    FROM PAY2_LOAN L
    WHERE EXISTS (SELECT 1 FROM PAY2_LOAN_SCHED LS WHERE LS.LOAN_ID = L.LOAN_ID AND LS.RUN_ID = @RUN_ID);

    UPDATE PAY2_LOAN_SCHED
    SET RUN_ID = NULL, PAID_AT = NULL
    WHERE RUN_ID = @RUN_ID;

    -- 2. بازگرداندن دقیقه‌های مرخصی کسر شده (محافظت در برابر اعداد منفی)
    UPDATE LB
    SET LB.USED_MIN = CASE
                        WHEN LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT) < 0 THEN 0
                        ELSE LB.USED_MIN - CAST(A.LEAVE_DAYS * 440 AS INT)
                      END,
        LB.UPDATED_AT = GETDATE()
    FROM PAY2_LEAVE_BAL LB
    INNER JOIN PAY2_ATTENDANCE A ON LB.EMP_ID = A.EMP_ID
    WHERE A.PER_ID = @PER_ID AND LB.YEAR = (@PERIOD_DATE / 10000)
      AND A.LEAVE_DAYS > 0;

    -- 3. حذف فیش‌ها
    DELETE FROM PAY2_RUN_DETAIL WHERE RUN_ID = @RUN_ID;
    DELETE FROM PAY2_RUN_LINE    WHERE RUN_ID = @RUN_ID;

    -- 4. باز کردن دوره و ثبت لاگ
    UPDATE PAY2_RUN
    SET STATUS = 1,
        NOTES = SUBSTRING(ISNULL(NOTES,'') + N' | Reverted by ' + CAST(ISNULL(@REVERT_BY,0) AS NVARCHAR), 1, 300)
    WHERE RUN_ID = @RUN_ID;

    UPDATE PAY2_PERIOD SET STATUS = 2 WHERE PER_ID = @PER_ID;

END;
GO

-- ================================================================
-- ۷. SP_PAY2_FINALIZE_RUN — نهایی‌کردن محاسبه (STATUS 1→2)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_FINALIZE_RUN]
    @RUN_ID   INT,
    @FINAL_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @STATUS TINYINT;
    SELECT @STATUS = STATUS FROM PAY2_RUN WHERE RUN_ID = @RUN_ID;

    IF @STATUS IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: اجرای %d یافت نشد.', 16, 1, @RUN_ID);
        RETURN;
    END;

    IF @STATUS <> 1
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: اجرا %d باید در وضعیت پیش‌نویس (1) باشد.', 16, 1, @RUN_ID);
        RETURN;
    END;

    DECLARE @PER_ID INT;
    DECLARE @WS_ID  INT;
    SELECT @PER_ID = R.PER_ID, @WS_ID = P.WS_ID
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID=P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    DECLARE @MISSING INT;
    SELECT @MISSING = COUNT(*)
    FROM PAY2_EMPLOYEE E
    WHERE E.WS_ID = @WS_ID AND E.IS_ACTIVE = 1
      AND EXISTS (SELECT 1 FROM PAY2_ATTENDANCE A WHERE A.PER_ID=@PER_ID AND A.EMP_ID=E.EMP_ID)
      AND NOT EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL WHERE RL.RUN_ID=@RUN_ID AND RL.EMP_ID=E.EMP_ID);

    IF @MISSING > 0
    BEGIN
        RAISERROR(N'SP_PAY2_FINALIZE_RUN: %d پرسنل هنوز محاسبه نشده‌اند.', 16, 1, @MISSING);
        RETURN;
    END;

    UPDATE PAY2_RUN
    SET STATUS = 2, NOTES = ISNULL(NOTES,'') + N' | Finalized by ' + CAST(ISNULL(@FINAL_BY,0) AS NVARCHAR)
    WHERE RUN_ID = @RUN_ID;

    PRINT N'SP_PAY2_FINALIZE_RUN — RUN_ID ' + CAST(@RUN_ID AS NVARCHAR) + N' نهایی شد.';
END;
GO

-- ================================================================
-- ۸. SP_PAY2_FINALIZE_SETTLE — نهایی‌کردن تسویه (STATUS 1→2)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_FINALIZE_SETTLE]
    @SET_ID     INT,
    @APPROVED_BY INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @STATUS TINYINT;
        DECLARE @EMP_ID INT;
        DECLARE @END_DATE BIGINT;
        DECLARE @LOAN_BALANCE BIGINT;

        -- قفلِ واقعی (UPDLOCK) تا زمان COMMIT/ROLLBACK روی این سطر باقی می‌ماند
        SELECT @STATUS = STATUS, @EMP_ID = EMP_ID, @END_DATE = END_DATE, @LOAN_BALANCE = LOAN_BALANCE
        FROM PAY2_SETTLEMENT WITH (UPDLOCK)
        WHERE SET_ID = @SET_ID;

        IF @STATUS IS NULL
            RAISERROR(N'تسویه حسابی با این شناسه یافت نشد.', 16, 1);

        IF @STATUS <> 1
            RAISERROR(N'تسویه در وضعیت پیش‌نویس (1) نیست یا قبلاً تأیید شده است.', 16, 1);

        UPDATE PAY2_SETTLEMENT
        SET STATUS = 2, APPROVED_BY = @APPROVED_BY, APPROVED_AT = GETDATE()
        WHERE SET_ID = @SET_ID;

        -- پایان همکاری و غیرفعال شدن پرسنل
        UPDATE PAY2_EMPLOYEE
        SET FIRE_DATE = @END_DATE, IS_ACTIVE = 0
        WHERE EMP_ID = @EMP_ID AND IS_ACTIVE = 1;

        -- بستن قطعی وام‌های فعالِ تسویه‌شده
        IF @LOAN_BALANCE > 0
        BEGIN
            UPDATE PAY2_LOAN
            SET IS_ACTIVE = 0,
                PURPOSE = SUBSTRING(ISNULL(PURPOSE, '') + N' (بسته‌شده در تسویه)', 1, 200)
            WHERE EMP_ID = @EMP_ID AND IS_ACTIVE = 1 AND PAID_INST < TOTAL_INST;
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

        DECLARE @ERR_MSG NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ERR_MSG, 16, 1);
    END CATCH;
END;
GO

-- ================================================================
-- ۹. SP_PAY2_LOAN_GEN_SCHED — تولید خودکار جدول اقساط وام
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_LOAN_GEN_SCHED]
    @LOAN_ID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @TOTAL_INST  SMALLINT,
        @INSTALLMENT BIGINT,
        @FIRST_PAY   BIGINT,
        @EMP_ID      INT;

    SELECT
        @TOTAL_INST  = TOTAL_INST,
        @INSTALLMENT = INSTALLMENT,
        @FIRST_PAY   = FIRST_PAY,
        @EMP_ID      = EMP_ID
    FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID;

    IF @TOTAL_INST IS NULL
    BEGIN
        RAISERROR(N'SP_PAY2_LOAN_GEN_SCHED: وام %d یافت نشد.', 16, 1, @LOAN_ID);
        RETURN;
    END;

    DELETE FROM PAY2_LOAN_SCHED WHERE LOAN_ID = @LOAN_ID AND PAID_AT IS NULL;

    DECLARE @I SMALLINT = 1;
    DECLARE @DUE BIGINT = @FIRST_PAY;

    DECLARE @DUE_YEAR  INT = @FIRST_PAY / 10000;
    DECLARE @DUE_MONTH INT = (@FIRST_PAY % 10000) / 100;

    WHILE @I <= @TOTAL_INST
    BEGIN
        DECLARE @THIS_AMT BIGINT =
            CASE WHEN @I = @TOTAL_INST
                 THEN (
                    SELECT CASE
                             WHEN AMOUNT - (@INSTALLMENT * (@TOTAL_INST - 1)) < 0 THEN 0
                             ELSE AMOUNT - (@INSTALLMENT * (@TOTAL_INST - 1))
                           END
                    FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID
                 )
                 ELSE @INSTALLMENT
            END;

        INSERT INTO PAY2_LOAN_SCHED (LOAN_ID, INST_NUM, DUE_PERIOD, AMOUNT)
        VALUES (@LOAN_ID, @I, @DUE_YEAR * 10000 + @DUE_MONTH * 100, @THIS_AMT);

        SET @DUE_MONTH = @DUE_MONTH + 1;
        IF @DUE_MONTH > 12
        BEGIN
            SET @DUE_MONTH = 1;
            SET @DUE_YEAR  = @DUE_YEAR + 1;
        END;

        SET @I = @I + 1;
    END;

    PRINT N'SP_PAY2_LOAN_GEN_SCHED — ' + CAST(@TOTAL_INST AS NVARCHAR) + N' قسط برای وام ' + CAST(@LOAN_ID AS NVARCHAR) + N' ایجاد شد.';
END;
GO

-- ================================================================
-- ۱۰. SP_PAY2_CARRYOVER_LEAVE — انتقال مانده مرخصی به سال بعد
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_CARRYOVER_LEAVE]
    @FROM_YEAR INT,
    @TO_YEAR   INT,
    @WS_ID     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CARRYOVER_MAX INT;
    SELECT @CARRYOVER_MAX = CAST(CFG_VALUE AS INT)
    FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_CARRYOVER_MAX';

    DECLARE @LEAVE_MINS_PER_DAY INT;
    SELECT @LEAVE_MINS_PER_DAY = CAST(CFG_VALUE AS INT)
    FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_MINS_PER_DAY';

    DECLARE @MAX_CARRY_MIN INT = @CARRYOVER_MAX * @LEAVE_MINS_PER_DAY;

    UPDATE PAY2_LEAVE_BAL
    SET CARRIED_OUT_MIN = CASE
        WHEN BALANCE_MIN > @MAX_CARRY_MIN THEN @MAX_CARRY_MIN
        WHEN BALANCE_MIN < 0 THEN 0
        ELSE BALANCE_MIN
    END,
    UPDATED_AT = GETDATE()
    WHERE YEAR = @FROM_YEAR
      AND (@WS_ID IS NULL OR EMP_ID IN (
          SELECT EMP_ID FROM PAY2_EMPLOYEE WHERE WS_ID = @WS_ID
      ));

    DECLARE @ANNUAL_DAYS INT;
    SELECT @ANNUAL_DAYS = CAST(CFG_VALUE AS INT) FROM PAY2_CONFIG WHERE CFG_KEY='LEAVE_ANNUAL_DAYS';
    DECLARE @ENTITLEMENT INT = @ANNUAL_DAYS * @LEAVE_MINS_PER_DAY;

    INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN)
    SELECT
        LB.EMP_ID, @TO_YEAR, @ENTITLEMENT, 0, LB.CARRIED_OUT_MIN
    FROM PAY2_LEAVE_BAL LB
    WHERE LB.YEAR = @FROM_YEAR
      AND (@WS_ID IS NULL OR LB.EMP_ID IN (SELECT EMP_ID FROM PAY2_EMPLOYEE WHERE WS_ID = @WS_ID))
      AND NOT EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL X WHERE X.EMP_ID = LB.EMP_ID AND X.YEAR = @TO_YEAR);

    PRINT N'SP_PAY2_CARRYOVER_LEAVE — انتقال از ' + CAST(@FROM_YEAR AS NVARCHAR) + N' به ' + CAST(@TO_YEAR AS NVARCHAR) + N' انجام شد.';
END;
GO

-- ================================================================
-- ۱۱. SP_PAY2_NEW_PERIOD — ایجاد دوره ماهیانه جدید
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_NEW_PERIOD]
    @WS_ID        INT,
    @PERIOD_DATE  BIGINT,
    @HOLIDAY_DAYS TINYINT = 0,
    @OPENED_BY    INT     = NULL,
    @NEW_PER_ID   INT     OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM PAY2_PERIOD WHERE WS_ID=@WS_ID AND PERIOD_DATE=@PERIOD_DATE)
    BEGIN
        RAISERROR(N'SP_PAY2_NEW_PERIOD: دوره %I64d برای کارگاه %d قبلاً ایجاد شده است.', 16, 1, @PERIOD_DATE, @WS_ID);
        RETURN;
    END;

    INSERT INTO PAY2_PERIOD (WS_ID, PERIOD_DATE, HOLIDAY_DAYS, STATUS, OPENED_AT)
    VALUES (@WS_ID, @PERIOD_DATE, @HOLIDAY_DAYS, 1, GETDATE());

    SET @NEW_PER_ID = SCOPE_IDENTITY();

    PRINT N'SP_PAY2_NEW_PERIOD — دوره ' + CAST(@PERIOD_DATE AS NVARCHAR) + N' با PER_ID=' + CAST(@NEW_PER_ID AS NVARCHAR) + N' ایجاد شد.';
END;
GO
";
                ExecuteBatches(db, procScript);

                // ===========================================================
                // 3. Modify â تغییرات ساختاری و بازنویسی Procedureهای خاص
                // ===========================================================
                // بخش اصلاح شده و کامل modify1
                string modify1 = @"
-- ================================================================
-- ۱. اصلاح ساختار ستون ACC_T در صورت قدیمی بودن دیتابیس
-- ================================================================
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.PAY2_EMPLOYEE')
      AND name = 'ACC_T'
      AND system_type_id = TYPE_ID('int')
)
BEGIN
    PRINT N'در حال تغییر نوع ستون ACC_T از INT به NVARCHAR...';
    ALTER TABLE PAY2_EMPLOYEE ALTER COLUMN ACC_T NVARCHAR(50) NULL;
END;
GO

-- ================================================================
-- ۲. SP_PAY2_GET_ADVANCES — محاسبه مساعده هوشمند (نسخه نهایی — JSON_VALUE)
-- ================================================================
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GET_ADVANCES]
    @PERIOD_DATE  BIGINT,
    @PAYROLL_N_S  FLOAT,
    @WS_ID        INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. خواندن کد کامل حساب مساعده
    DECLARE @FULL_HES NVARCHAR(100);
    SELECT @FULL_HES = ACC_CODE
    FROM PAY2_WORKSHOP_ACC WITH (NOLOCK)
    WHERE WS_ID = @WS_ID AND ACC_KEY = 'ADV_HES';

    IF @FULL_HES IS NULL
    BEGIN
        RAISERROR(N'حساب مساعده (ADV_HES) برای این کارگاه تنظیم نشده است.', 16, 1);
        RETURN;
    END;

    -- 2. پارس کردن کد ترکیبی با استفاده از JSON_VALUE
    DECLARE @JsonArr NVARCHAR(250) = N'[""' + REPLACE(@FULL_HES, '-', '"",""') + N'""]';

    DECLARE @HES_K  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[0]'), '') AS INT);
    DECLARE @HES_M  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[1]'), '') AS INT);
    DECLARE @HES_T  INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[2]'), '') AS INT);
    DECLARE @HES_T2 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[3]'), '') AS INT);
    DECLARE @HES_T3 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[4]'), '') AS INT);
    DECLARE @HES_T4 INT = TRY_CAST(NULLIF(JSON_VALUE(@JsonArr, '$[5]'), '') AS INT);

    -- بررسی امنیتی حساب
    IF @HES_K IS NULL OR @HES_M IS NULL
    BEGIN
        RAISERROR(N'فرمت حساب مساعده نادرست است. باید حداقل شامل کل و معین باشد (مثال: 112-1).', 16, 1);
        RETURN;
    END;

    -- 3. تعیین سطح اعمال فیلتر کد پرسنل (ACC_T)
    DECLARE @EMP_FILTER_LEVEL TINYINT =
        CASE
            WHEN @HES_T  IS NULL THEN 3
            WHEN @HES_T2 IS NULL THEN 4
            WHEN @HES_T3 IS NULL THEN 5
            ELSE                     6
        END;

    -- 4. خواندن تنظیمات اضافی به صورت ایمن
    DECLARE @USE_T BIT = 1, @MIN_POS BIT = 1, @ADV_SCOPE NVARCHAR(20) = 'CURRENT_MONTH';

    SELECT
        @USE_T     = ISNULL(CAST(MAX(CASE WHEN CFG_KEY = 'ADV_USE_HES_T_FILTER' THEN TRY_CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @MIN_POS   = ISNULL(CAST(MAX(CASE WHEN CFG_KEY = 'ADV_MIN_POSITIVE'   THEN TRY_CAST(CFG_VALUE AS INT) END) AS BIT), 1),
        @ADV_SCOPE = ISNULL(MAX(CASE WHEN CFG_KEY = 'ADV_SCOPE' THEN CFG_VALUE END), 'CURRENT_MONTH')
    FROM PAY2_CONFIG WITH (NOLOCK)
    WHERE CFG_KEY IN ('ADV_USE_HES_T_FILTER', 'ADV_MIN_POSITIVE', 'ADV_SCOPE');

    -- 5. محاسبه بازه تاریخ به صورت امن و بدون تقسیم خطرناک
    -- تبدیل 14030700 به بازه 14030700 تا 14030799
    DECLARE @MONTH_START BIGINT = (@PERIOD_DATE / 100) * 100;
    DECLARE @MONTH_END   BIGINT = @MONTH_START + 99;

    -- 6. اجرای کوئری نهایی مالی
    ;WITH AdvBase AS
    (
        SELECT
            E.EMP_ID,
            E.ACC_T                            AS PCODE,
            E.LAST_NAME + N' ' + E.FIRST_NAME  AS FULL_NAME,

            -- مانده خام از حسابداری
            ISNULL((
                SELECT CAST(SUM(D.BED - D.BES) AS BIGINT)
                FROM DEED_HED H
                INNER JOIN DEED_DTL D ON H.N_S = D.N_S
                WHERE
                    D.HES_K = @HES_K
                    AND D.HES_M = @HES_M
                    -- 🚀 فیلتر دقیق سطوح بالادستی (باید دقیقاً برابر با مقدار کانفیگ باشند)
                    AND (@EMP_FILTER_LEVEL <= 3 OR D.HES_T  = @HES_T)
                    AND (@EMP_FILTER_LEVEL <= 4 OR D.HES_T2 = @HES_T2)
                    AND (@EMP_FILTER_LEVEL <= 5 OR D.HES_T3 = @HES_T3)
                    AND (@EMP_FILTER_LEVEL <= 6 OR D.HES_T4 = @HES_T4)

                    -- 🚀 فیلتر سطح پرسنل (یا فعال نیست، یا باید دقیقاً برابر با کد پرسنل باشد)
                    AND (
                        @USE_T = 0
                        OR TRY_CAST(NULLIF(TRIM(E.ACC_T), '') AS INT) =
                           CASE @EMP_FILTER_LEVEL
                                WHEN 3 THEN D.HES_T
                                WHEN 4 THEN D.HES_T2
                                WHEN 5 THEN D.HES_T3
                                WHEN 6 THEN D.HES_T4
                           END
                    )

                    -- 🚀 جلوگیری از نشت داده (سطوح پایین‌تر از پرسنل باید خالی یا صفر باشند)
                    AND (@EMP_FILTER_LEVEL >= 4 OR ISNULL(D.HES_T2, 0) = 0)
                    AND (@EMP_FILTER_LEVEL >= 5 OR ISNULL(D.HES_T3, 0) = 0)
                    AND (@EMP_FILTER_LEVEL >= 6 OR ISNULL(D.HES_T4, 0) = 0)

                    AND H.N_S < ISNULL(@PAYROLL_N_S, 999999999)
                    AND H.OKF = 1
                    AND (
                        @ADV_SCOPE = 'OPEN_BALANCE'
                        OR (H.DATE_S BETWEEN @MONTH_START AND @MONTH_END)
                    )
            ), 0) AS RAW_BALANCE,

            -- استثناهای دستی مساعده
            ISNULL((
                SELECT SUM(EXCL_AMOUNT)
                FROM PAY2_ADVANCE_EXCL WITH (NOLOCK)
                WHERE EMP_ID = E.EMP_ID
                  AND PERIOD_DATE BETWEEN @MONTH_START AND @MONTH_END
            ), 0) AS MANUAL_EXCL

        FROM PAY2_EMPLOYEE E WITH (NOLOCK)
        INNER JOIN PAY2_PERIOD P WITH (NOLOCK)
            ON P.WS_ID = E.WS_ID
            AND P.PERIOD_DATE = @PERIOD_DATE
        WHERE E.WS_ID     = @WS_ID
          AND E.IS_ACTIVE = 1
          AND E.ACC_T IS NOT NULL
    )
    SELECT
        EMP_ID,
        PCODE,
        FULL_NAME,
        RAW_BALANCE,
        MANUAL_EXCL,
        CASE
            WHEN @MIN_POS = 1 AND (RAW_BALANCE - MANUAL_EXCL) <= 0
                THEN 0
            ELSE CASE
                    WHEN (RAW_BALANCE - MANUAL_EXCL) < 0 THEN 0
                    ELSE RAW_BALANCE - MANUAL_EXCL
                 END
        END AS ADVANCE_DEDUCTION
    FROM AdvBase
    OPTION (RECOMPILE);

END;
GO

-- ================================================================
-- ۳. باز کردن قید CK_CALC_BASIS برای مقدار 3 (ساعتی)
-- ================================================================
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_CALC_BASIS'
             AND parent_object_id = OBJECT_ID(N'dbo.PAY2_ITEM_DEF')
             AND definition NOT LIKE '%(3)%')
BEGIN
    ALTER TABLE dbo.PAY2_ITEM_DEF DROP CONSTRAINT CK_CALC_BASIS;
    ALTER TABLE dbo.PAY2_ITEM_DEF ADD CONSTRAINT CK_CALC_BASIS CHECK ([CALC_BASIS] IN (1,2,3));
END;
GO

-- ================================================================
-- ۴. سایر قیدهای احتمالی روی BASIS_OV که مقدار 3 را مجاز نمی‌دانند
-- ================================================================
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) IN ('PAY2_DECREE_LINE', 'PAY2_OVERRIDE', 'PAY2_ITEM_TMPL_LINE')
  AND cc.definition LIKE '%BASIS_OV%'
  AND cc.definition NOT LIKE '%(3)%';
IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

-- ================================================================
-- ۵. پشتیبانی از نوع مرخصی «ساعتی» (مقدار 6) در جدول PAY2_LEAVE
--
-- انواع مرخصی:
--   1=استحقاقی  2=استعلاجی  3=بدون حقوق  4=زایمان  5=مأموریت  6=ساعتی (جدید)
--
-- اگر CHECK CONSTRAINT روی LEV_TYPE مقدار 6 را مجاز نمی‌داند، حذف می‌شود.
-- سقف مرخصی ساعتی (3 ساعت و 20 دقیقه) در سمت سرور (Pay2EmployeesController)
-- و سمت کلاینت اعتبارسنجی می‌شود.
-- ================================================================
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(cc.parent_object_id))
            + N'.' + QUOTENAME(OBJECT_NAME(cc.parent_object_id))
            + N' DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
FROM sys.check_constraints cc
WHERE OBJECT_NAME(cc.parent_object_id) = 'PAY2_LEAVE'
  AND cc.definition LIKE '%LEV_TYPE%'
  AND cc.definition NOT LIKE '%(6)%';
IF LEN(@sql) > 0
    EXEC sp_executesql @sql;
GO

";
                ExecuteBatches(db, modify1);

                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'POSTAL_CODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [POSTAL_CODE] NVARCHAR(20) NULL;");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'EMPLOYER_NAME') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [EMPLOYER_NAME] NVARCHAR(100) NULL;");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'PROVINCE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD
                        [PROVINCE] NVARCHAR(50) NULL,
                        [CITY] NVARCHAR(50) NULL,
                        [REGISTRATION_NUMBER] NVARCHAR(20) NULL,
                        [SSO_BRANCH] NVARCHAR(50) NULL,
                        [FINANCIAL_MANAGER] NVARCHAR(100) NULL,
                        [ADMIN_MANAGER] NVARCHAR(100) NULL;");

                //-- ساخت ایندکس ترکیبی برای حذف عملیات سورت و اسکن جدول شغل‌ها
                db.Execute(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PAY2_JOB_PERFORMANCE')
                    CREATE NONCLUSTERED INDEX IX_PAY2_JOB_PERFORMANCE ON [dbo].[PAY2_JOB] ([IS_ACTIVE], [JOB_NAME]) INCLUDE ([JOB_ID]);");

                // Migration 009: افزودن تنظیمات حق شیفت به تفکیک کارگاه و پرسنل
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_WS_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_DECREE', 'SHIFT_MODE') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE] ADD [SHIFT_MODE] NVARCHAR(10) NULL CONSTRAINT [CK_DEC_SHIFT_MODE] CHECK ([SHIFT_MODE] IN ('PCT','FIXED'));");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_DECREE_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_DECREE_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_DL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));");
                db.Execute(@"IF COL_LENGTH('dbo.PAY2_ITEM_TMPL_LINE', 'SHIFT_MODE_OV') IS NULL
                    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] ADD [SHIFT_MODE_OV] NVARCHAR(10) NULL CONSTRAINT [CK_TL_SHIFT_MODE_OV] CHECK ([SHIFT_MODE_OV] IN ('PCT','FIXED'));");

                // Migration 010: افزودن فیلدهای مربوط به روش صدور سند (Dual Deed Modes)
                try
                {
                    db.Execute(@"IF COL_LENGTH('dbo.PAY2_WORKSHOP', 'DEFAULT_DEED_MODE') IS NULL
                        ALTER TABLE [dbo].[PAY2_WORKSHOP] ADD [DEFAULT_DEED_MODE] TINYINT NOT NULL CONSTRAINT DF_WS_DEED_MODE DEFAULT(1);");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در Migration دیتابیس (PAY2_WORKSHOP.DEFAULT_DEED_MODE). آپدیت متوقف شد: {ex.Message}", ex);
                }

                try
                {
                    db.Execute(@"
                    IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_MODE') IS NULL
                        ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_MODE] TINYINT NULL;

                    IF COL_LENGTH('dbo.PAY2_RUN', 'DEED_GENERATOR_VERSION') IS NULL
                        ALTER TABLE [dbo].[PAY2_RUN] ADD [DEED_GENERATOR_VERSION] SMALLINT NULL;
                    ");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در Migration دیتابیس (PAY2_RUN.DEED_MODE). آپدیت متوقف شد: {ex.Message}", ex);
                }

                try
                {
                    //-- ================================================================
                    //-- ۱.۵ FN_PAY2_ACC_PARENT — برداشتن آخرین سطح یک کد حساب
                    //-- ================================================================
                    db.Execute(@"
CREATE OR ALTER FUNCTION [dbo].[FN_PAY2_ACC_PARENT](@CODE NVARCHAR(50))
RETURNS NVARCHAR(50)
AS
BEGIN
    -- «711-1-1» ← «711-1». اگر کد کمتر از دو سطح داشته باشد NULL برمی‌گردد تا
    -- فراخوان مجبور شود ریشه را صریح تنظیم کند و ما حسابِ ناقص نسازیم.
    SET @CODE = NULLIF(LTRIM(RTRIM(@CODE)), '');
    IF @CODE IS NULL OR CHARINDEX('-', @CODE) = 0 RETURN NULL;
    RETURN NULLIF(LEFT(@CODE, LEN(@CODE) - CHARINDEX('-', REVERSE(@CODE))), '');
END;");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در دیتابیس (FN_PAY2_ACC_PARENT). آپدیت متوقف شد: {ex.Message}", ex);
                }

                try
                {
                    //-- ================================================================
                    //-- ۲. SP_PAY2_GEN_DEED — تولید سند حسابداری حقوق و بیمه
                    //-- ================================================================
                    db.Execute(@"
CREATE OR ALTER PROCEDURE [dbo].[SP_PAY2_GEN_DEED]
    @RUN_ID  INT,
    @CALC_BY INT = NULL,
    @DEED_MODE TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#SalarySplit') IS NOT NULL DROP TABLE #SalarySplit;
    IF OBJECT_ID('tempdb..#FinalArticles') IS NOT NULL DROP TABLE #FinalArticles;
    IF OBJECT_ID('tempdb..#UniqueAccounts') IS NOT NULL DROP TABLE #UniqueAccounts;

    DECLARE @PER_ID INT, @WS_ID INT, @PER_DATE BIGINT;

    SELECT @PER_ID = R.PER_ID, @WS_ID = P.WS_ID, @PER_DATE = P.PERIOD_DATE
    FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
    WHERE R.RUN_ID = @RUN_ID;

    DECLARE @MonthNum INT = (@PER_DATE / 100) % 100;
    DECLARE @MonthName NVARCHAR(10) = CASE @MonthNum
        WHEN 1 THEN N'فروردین' WHEN 2 THEN N'اردیبهشت' WHEN 3 THEN N'خرداد'
        WHEN 4 THEN N'تیر'     WHEN 5 THEN N'مرداد'    WHEN 6 THEN N'شهریور'
        WHEN 7 THEN N'مهر'     WHEN 8 THEN N'آبان'     WHEN 9 THEN N'آذر'
        WHEN 10 THEN N'دی'     WHEN 11 THEN N'بهمن'    WHEN 12 THEN N'اسفند' ELSE N'نامشخص' END;
    DECLARE @ML NVARCHAR(20) = RIGHT('0' + CAST(@MonthNum AS NVARCHAR(2)), 2) + N'-' + @MonthName;

    DECLARE
        @ACC_SALARY_TOLID NVARCHAR(50), @ACC_SALARY_EDARI NVARCHAR(50),
        @ACC_SALARY_FOROSH NVARCHAR(50), @ACC_SALARY_KHADAMAT NVARCHAR(50),
        @ACC_SALARY_PAY NVARCHAR(50), @ACC_INS_PAYABLE NVARCHAR(50),
        @ACC_TAX_PAYABLE NVARCHAR(50), @ACC_INS_EXP NVARCHAR(50),
        @ACC_ADV_HES NVARCHAR(50), @ACC_LOAN_HES NVARCHAR(50),
        @ACC_OTHER_DED_HES NVARCHAR(50);

    -- شاخه‌ی حساب هزینه‌ی هر مرکز (کل-معین) برای سند تفصیلی کامل؛ شماره‌ی
    -- تفصیلی از روی نوع قلم به آن اضافه می‌شود. مستقیماً از همان حساب هزینه‌ی
    -- تنظیم‌شده‌ی کارگاه ساخته می‌شود تا کارگاه‌ها تنظیم تازه‌ای لازم نداشته باشند.
    DECLARE
        @ROOT_TOLID NVARCHAR(50), @ROOT_EDARI NVARCHAR(50),
        @ROOT_FOROSH NVARCHAR(50), @ROOT_KHADAMAT NVARCHAR(50);

    SELECT
        @ACC_SALARY_TOLID   = MAX(CASE WHEN ACC_KEY='SALARY_EXP_TOLID'    THEN ACC_CODE END),
        @ACC_SALARY_EDARI   = MAX(CASE WHEN ACC_KEY='SALARY_EXP_EDARI'    THEN ACC_CODE END),
        @ACC_SALARY_FOROSH  = MAX(CASE WHEN ACC_KEY='SALARY_EXP_FOROSH'   THEN ACC_CODE END),
        @ACC_SALARY_KHADAMAT= MAX(CASE WHEN ACC_KEY='SALARY_EXP_KHADAMAT' THEN ACC_CODE END),
        @ACC_SALARY_PAY     = MAX(CASE WHEN ACC_KEY='SALARY_PAYABLE'      THEN ACC_CODE END),
        @ACC_INS_PAYABLE    = MAX(CASE WHEN ACC_KEY='INS_PAYABLE'         THEN ACC_CODE END),
        @ACC_TAX_PAYABLE    = MAX(CASE WHEN ACC_KEY='TAX_PAYABLE'         THEN ACC_CODE END),
        @ACC_INS_EXP        = MAX(CASE WHEN ACC_KEY='INS_EXP'             THEN ACC_CODE END),
        @ACC_ADV_HES        = MAX(CASE WHEN ACC_KEY='ADV_HES'             THEN ACC_CODE END),
        @ACC_LOAN_HES       = MAX(CASE WHEN ACC_KEY='LOAN_HES'            THEN ACC_CODE END),
        @ACC_OTHER_DED_HES  = MAX(CASE WHEN ACC_KEY='OTHER_DED_HES'       THEN ACC_CODE END)
    FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID;

    -- شاخه از خودِ حساب هزینه‌ی همان مرکز ساخته می‌شود: آخرین سطح برداشته
    -- می‌شود («711-1-1» ← «711-1»). پس کارگاه‌های موجود بدون هیچ تنظیم تازه‌ای
    -- سند تفصیلی می‌گیرند.
    SET @ROOT_TOLID    = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_TOLID))),    '');
    SET @ROOT_EDARI    = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_EDARI))),    '');
    SET @ROOT_FOROSH   = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_FOROSH))),   '');
    SET @ROOT_KHADAMAT = NULLIF(LTRIM(RTRIM(dbo.FN_PAY2_ACC_PARENT(@ACC_SALARY_KHADAMAT))), '');

    IF @DEED_MODE IS NULL
    BEGIN
        SELECT @DEED_MODE = CASE
            WHEN R.DEED_MODE IS NOT NULL THEN R.DEED_MODE
            WHEN R.STATUS >= 2 THEN 1
            ELSE W.DEFAULT_DEED_MODE
        END
        FROM PAY2_RUN R
        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
        INNER JOIN PAY2_WORKSHOP W ON P.WS_ID = W.WS_ID
        WHERE R.RUN_ID = @RUN_ID;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- گاردهای امنیتی (جلوگیری از منفی شدن خالص و کمبود حساب‌ها)
    -- ─────────────────────────────────────────────────────────────────
    -- خالص منفی برای پرسنلِ فقط‌بیمه (کارکرد رسمی صفر و بدون کسور وام/مساعده/سایر) مجاز است؛
    -- این افراد حقوق نمی‌گیرند و فقط بابت بیمه و مالیات بدهکار می‌شوند.
    DECLARE @NegEmpId INT, @NegEmpName NVARCHAR(100), @NegAmount BIGINT;
    SELECT TOP 1 @NegEmpId = RL.EMP_ID, @NegEmpName = E.LAST_NAME + N' ' + E.FIRST_NAME, @NegAmount = RL.NET_PAY
    FROM PAY2_RUN_LINE RL INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
    WHERE RL.RUN_ID = @RUN_ID AND RL.NET_PAY < 0
      AND NOT (RL.WORK_DAYS = 0 AND RL.LOAN_DED = 0 AND RL.ADVANCE_DED = 0 AND RL.OTHER_DED = 0);

    IF @NegEmpId IS NOT NULL
    BEGIN
        DECLARE @Err1 NVARCHAR(500) = N'صدور سند متوقف شد: خالص پرداختی پرسنل منفی است. کد: ' + CAST(@NegEmpId AS NVARCHAR) + N' | نام: ' + @NegEmpName + N' | مبلغ بدهی: ' + CAST(ABS(@NegAmount) AS NVARCHAR) + N' ریال.';
        RAISERROR(@Err1, 16, 1);
        RETURN;
    END

    IF @ACC_SALARY_PAY IS NULL
    BEGIN
        RAISERROR(N'حساب پرداختنی حقوق (SALARY_PAYABLE) برای کارگاه تنظیم نشده است.', 16, 1);
        RETURN;
    END

    DECLARE @MissingAcc NVARCHAR(MAX) = N'';
    -- در سند تفصیلی کامل، بیمه‌ی کارفرما به حساب هزینه‌ی مرکزِ خودِ پرسنل
    -- (ریشه + تفصیلی ۱۰) می‌رود، نه به حساب تکیِ INS_EXP.
    IF @DEED_MODE <> 3 AND @ACC_INS_EXP IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND INS_EMPLOYER > 0) SET @MissingAcc += N'هزینه بیمه کارفرما، ';
    IF @ACC_INS_PAYABLE IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND (INS_WORKER + INS_EMPLOYER) > 0) SET @MissingAcc += N'اداره بیمه، ';
    IF @ACC_TAX_PAYABLE IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND TAX_AMOUNT > 0) SET @MissingAcc += N'اداره مالیات، ';
    IF @ACC_LOAN_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND LOAN_DED > 0) SET @MissingAcc += N'صندوق وام، ';
    IF @ACC_ADV_HES IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE RUN_ID = @RUN_ID AND ADVANCE_DED > 0) SET @MissingAcc += N'حساب مساعده، ';
    -- OTHER_DED دو جزء دارد: «سایر کسورات» دستی (KASR_OTHER) و «کسر کار».
    -- در سند تفصیلی کامل فقط جزء اول به حساب سایر کسورات می‌رود؛ کسر کار
    -- حسابِ مقصد ندارد و هزینه‌ی حقوق را کم می‌کند. پس اگر ماهی فقط کسر کار
    -- داشته باشد، نباید حسابی را مطالبه کنیم که هیچ آرتیکلی به آن نمی‌خورد.
    IF @ACC_OTHER_DED_HES IS NULL AND EXISTS (
        SELECT 1 FROM PAY2_RUN_LINE RL
        LEFT JOIN PAY2_ATTENDANCE A ON A.EMP_ID = RL.EMP_ID AND A.PER_ID = @PER_ID
        WHERE RL.RUN_ID = @RUN_ID
          AND (CASE WHEN @DEED_MODE = 3 THEN ISNULL(A.KASR_OTHER, 0) ELSE RL.OTHER_DED END) > 0
    ) SET @MissingAcc += N'سایر کسورات، ';

    -- حالت ۳ به‌جای این چهار حساب، «ریشه»ی هر مرکز را می‌خواهد (پایین‌تر بررسی می‌شود).
    IF @DEED_MODE <> 3
    BEGIN
        IF @ACC_SALARY_TOLID IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_TOLID > 0) SET @MissingAcc += N'هزینه تولید، ';
        IF @ACC_SALARY_EDARI IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_EDARI > 0) SET @MissingAcc += N'هزینه اداری، ';
        IF @ACC_SALARY_FOROSH IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_FOROSH > 0) SET @MissingAcc += N'هزینه فروش، ';
        IF @ACC_SALARY_KHADAMAT IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_KHADAMAT > 0) SET @MissingAcc += N'هزینه خدمات، ';
    END

    -- حساب هزینه در این حالت «شاخه‌ی حساب مرکز + شماره‌ی تفصیلیِ قلم» است، پس
    -- برای هر مرکزی که کارکرد دارد باید حساب هزینه‌ی آن مرکز تنظیم شده باشد و
    -- دست‌کم دو سطح داشته باشد (وگرنه شاخه‌ای برای ساختن نمی‌ماند).
    IF @DEED_MODE = 3
    BEGIN
        IF @ROOT_TOLID IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_TOLID > 0) SET @MissingAcc += N'هزینه تولید، ';
        IF @ROOT_EDARI IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_EDARI > 0) SET @MissingAcc += N'هزینه اداری، ';
        IF @ROOT_FOROSH IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_FOROSH > 0) SET @MissingAcc += N'هزینه فروش، ';
        IF @ROOT_KHADAMAT IS NULL AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID WHERE RL.RUN_ID = @RUN_ID AND RL.GROSS_PAY > 0 AND A.DAYS_KHADAMAT > 0) SET @MissingAcc += N'هزینه خدمات، ';
    END

    -- ریشه‌ی مشترک بین دو مرکزِ فعال یعنی تفکیک مرکز هزینه از بین می‌رود: سند
    -- تراز می‌مانَد ولی هزینه‌ی تولید و اداری روی یک حساب می‌نشیند. این وقتی رخ
    -- می‌دهد که در دفتر حساب، خودِ مرکز هزینه در سطح تفصیلی تعریف شده باشد
    -- (مثل 71-1-1 تا 71-1-4) و ریشه‌ی خودکار برای همه «71-1» شود.
    IF @DEED_MODE = 3
    BEGIN
        DECLARE @DupRoot NVARCHAR(50);
        SELECT TOP 1 @DupRoot = ROOT
        FROM (VALUES (@ROOT_TOLID, 1), (@ROOT_EDARI, 2), (@ROOT_FOROSH, 3), (@ROOT_KHADAMAT, 4)) R(ROOT, CENTER)
        WHERE R.ROOT IS NOT NULL
          AND EXISTS (
              SELECT 1 FROM PAY2_RUN_LINE RL
              INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID
              WHERE RL.RUN_ID = @RUN_ID
                AND ((R.CENTER = 1 AND A.DAYS_TOLID > 0) OR (R.CENTER = 2 AND A.DAYS_EDARI > 0)
                  OR (R.CENTER = 3 AND A.DAYS_FOROSH > 0) OR (R.CENTER = 4 AND A.DAYS_KHADAMAT > 0)))
        GROUP BY ROOT
        HAVING COUNT(*) > 1;

        IF @DupRoot IS NOT NULL
        BEGIN
            DECLARE @ErrDup NVARCHAR(500) = N'صدور سند متوقف شد: بیش از یک مرکز هزینه به شاخه‌ی حساب «'
                + @DupRoot + N'» می‌رسد و هزینه‌ی مراکز روی هم می‌افتد. '
                + N'در «سند تفصیلی کامل»، حساب هزینه‌ی هر مرکز باید شاخه‌ی مستقل خودش را داشته باشد '
                + N'(مثلاً 711-1-1 برای تولید و 712-1-1 برای اداری، نه 71-1-1 و 71-1-2). '
                + N'سرفصل‌های هزینه‌ی کارگاه را مطابق این ساختار تنظیم کنید یا از روش «نیمه‌تفصیلی» استفاده کنید.';
            RAISERROR(@ErrDup, 16, 1);
            RETURN;
        END
    END

    IF LEN(@MissingAcc) > 0
    BEGIN
        -- LEN در T-SQL فاصله‌ی انتهایی را نمی‌شمارد، پس LEN-2 علاوه بر جداکننده یک
        -- کاراکترِ واقعی را هم می‌بُرید («سایر کسورات» → «سایر کسورا»). فقط «،» حذف شود.
        DECLARE @Err2 NVARCHAR(MAX) = N'صدور سند متوقف شد: حساب‌های زیر در تنظیمات کارگاه خالی هستند: ' + SUBSTRING(@MissingAcc, 1, LEN(@MissingAcc)-1);
        RAISERROR(@Err2, 16, 1);
        RETURN;
    END

    DECLARE @BadEmpName NVARCHAR(100), @BadAccT NVARCHAR(50);
    SELECT TOP 1 @BadEmpName = E.LAST_NAME + N' ' + E.FIRST_NAME, @BadAccT = ISNULL(E.ACC_T, N'خالی')
    FROM PAY2_RUN_LINE RL
    INNER JOIN PAY2_EMPLOYEE E ON RL.EMP_ID = E.EMP_ID
    WHERE RL.RUN_ID = @RUN_ID
      AND (
           (@DEED_MODE IN (2, 3))
           OR
           (@DEED_MODE = 1 AND (RL.LOAN_DED > 0 OR RL.ADVANCE_DED > 0 OR RL.OTHER_DED > 0))
      )
      AND (
           NULLIF(TRIM(E.ACC_T), '') IS NULL
           OR TRIM(E.ACC_T) = @ACC_SALARY_PAY
      );

    IF @BadEmpName IS NOT NULL
    BEGIN
        DECLARE @Err4 NVARCHAR(500) = N'صدور سند متوقف شد: کد تفصیلی (ACC_T) برای پرسنل نامعتبر است. حساب پرسنل نمی‌تواند خالی یا برابر با ریشه کل باشد. نام پرسنل: ' + @BadEmpName + N' (' + @BadAccT + N')';
        RAISERROR(@Err4, 16, 1);
        RETURN;
    END

    -- ─────────────────────────────────────────────────────────────────
    -- جدول موقت محاسبات و ایجاد ردیف‌های خام (Summary vs Traceable)
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #SalarySplit (
        EMP_ID INT PRIMARY KEY,
        FULL_NAME NVARCHAR(150),
        ACC_T NVARCHAR(50),
        EXP_TOLID BIGINT,
        EXP_EDARI BIGINT,
        EXP_FOROSH BIGINT,
        EXP_KHADAMAT BIGINT,
        NET_PAY BIGINT,
        PERSONAL_DEBT BIGINT,
        INS_WORKER BIGINT,
        INS_EMPLOYER BIGINT,
        TAX_AMOUNT BIGINT,
        LOAN_DED BIGINT,
        ADVANCE_DED BIGINT,
        OTHER_DED BIGINT
    );

    ;WITH EmpAcc AS (
        SELECT
            E.EMP_ID, E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
            -- ACC_T is a complete account path and may contain all six supported
            -- levels (for example 115-1-25-3-4-6). It must never be appended to
            -- another account hierarchy or reduced to a supposedly portable suffix.
            NULLIF(TRIM(E.ACC_T), '') AS ACC_T
        FROM PAY2_EMPLOYEE E
        INNER JOIN PAY2_RUN_LINE RL ON E.EMP_ID = RL.EMP_ID
        WHERE RL.RUN_ID = @RUN_ID
    ),
    SplitBase AS (
        SELECT
            RL.EMP_ID, RL.GROSS_PAY, A.DAYS_TOLID, A.DAYS_EDARI, A.DAYS_FOROSH, A.DAYS_KHADAMAT,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_TOLID)    / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_T,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_EDARI)    / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_E,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_FOROSH)   / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_F,
            CAST(CASE WHEN A.WORK_DAYS > 0 THEN ROUND((EB.EXP_BASE * A.DAYS_KHADAMAT) / A.WORK_DAYS, 0) ELSE 0 END AS BIGINT) AS R_K,
            EB.EXP_BASE,
            RL.NET_PAY, RL.INS_WORKER, RL.INS_EMPLOYER, RL.TAX_AMOUNT, RL.LOAN_DED, RL.ADVANCE_DED, RL.OTHER_DED
        FROM PAY2_RUN_LINE RL
        INNER JOIN PAY2_ATTENDANCE A ON RL.EMP_ID = A.EMP_ID AND A.PER_ID = @PER_ID
        -- مبنای هزینه «خالص + کسورات» است، نه مستقیماً GROSS_PAY.
        --
        -- سند، خالص را بستانکار و کسورات را بستانکار می‌کند؛ پس طرف بدهکار
        -- باید دقیقاً جمع همان‌ها باشد. در موتور امروز GROSS_PAY همین است و
        -- این تغییر بی‌اثر می‌مانَد، ولی در اجراهایی که با نسخه‌های قدیمی‌تر
        -- محاسبه شده‌اند خالص جداگانه رُند شده و GROSS_PAY همراهش تغییر نکرده.
        -- آنجا تفاوتِ رُند در هیچ طرف سند نمی‌نشست و سند به همان اندازه ناتراز
        -- می‌شد — یعنی کاربر پیام «سند تراز نیست» می‌گرفت و بازصدور سند آن
        -- ماه‌ها اصلاً ممکن نبود.
        CROSS APPLY (VALUES (RL.NET_PAY + RL.TOTAL_DED)) EB(EXP_BASE)
        WHERE RL.RUN_ID = @RUN_ID
    )
    INSERT INTO #SalarySplit (
        EMP_ID, FULL_NAME, ACC_T, EXP_TOLID, EXP_EDARI, EXP_FOROSH, EXP_KHADAMAT,
        NET_PAY, PERSONAL_DEBT, INS_WORKER, INS_EMPLOYER, TAX_AMOUNT, LOAN_DED, ADVANCE_DED, OTHER_DED
    )
    SELECT
        B.EMP_ID, E.FULL_NAME, E.ACC_T,
        CASE WHEN B.DAYS_TOLID > 0 THEN B.R_T + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_T END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI > 0 THEN B.R_E + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_E END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI = 0 AND B.DAYS_FOROSH > 0 THEN B.R_F + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_F END,
        CASE WHEN B.DAYS_TOLID = 0 AND B.DAYS_EDARI = 0 AND B.DAYS_FOROSH = 0 THEN B.R_K + (B.EXP_BASE - (B.R_T + B.R_E + B.R_F + B.R_K)) ELSE B.R_K END,
        B.NET_PAY,
        CASE WHEN B.NET_PAY < 0 THEN -B.NET_PAY ELSE 0 END,
        B.INS_WORKER, B.INS_EMPLOYER, B.TAX_AMOUNT, B.LOAN_DED, B.ADVANCE_DED, B.OTHER_DED
    FROM SplitBase B
    INNER JOIN EmpAcc E ON B.EMP_ID = E.EMP_ID;

    -- ─────────────────────────────────────────────────────────────────
    -- جمع‌آوری مقادیر نهایی در جدول برای ولیدیشن حساب‌ها
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #FinalArticles (
        HES_CODE NVARCHAR(100) COLLATE database_default,
        SHARH NVARCHAR(500),
        BED BIGINT,
        BES BIGINT,
        ACC_KEY NVARCHAR(50),
        EMP_ID INT NULL,
        EmployeeName NVARCHAR(150),
        SortOrder INT
    );

    IF @DEED_MODE = 1
    BEGIN
        INSERT INTO #FinalArticles
        SELECT CAST(@ACC_SALARY_TOLID AS NVARCHAR(100)), CAST(N'هزینه حقوق تولید ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_TOLID) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_TOLID' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 1
        FROM #SalarySplit HAVING SUM(EXP_TOLID) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_EDARI AS NVARCHAR(100)), CAST(N'هزینه حقوق اداری ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_EDARI) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_EDARI' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 2
        FROM #SalarySplit HAVING SUM(EXP_EDARI) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_FOROSH AS NVARCHAR(100)), CAST(N'هزینه حقوق فروش ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_FOROSH) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_FOROSH' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 3
        FROM #SalarySplit HAVING SUM(EXP_FOROSH) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_KHADAMAT AS NVARCHAR(100)), CAST(N'هزینه حقوق خدمات ' + @ML AS NVARCHAR(500)), CAST(SUM(EXP_KHADAMAT) AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_KHADAMAT' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 4
        FROM #SalarySplit HAVING SUM(EXP_KHADAMAT) > 0
        UNION ALL
        SELECT CAST(@ACC_INS_EXP AS NVARCHAR(100)), CAST(N'هزینه بیمه کارفرما ' + @ML AS NVARCHAR(500)), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST(0 AS BIGINT), CAST('INS_EXP' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_PAY AS NVARCHAR(100)), CAST(N'حقوق پرداختنی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(CASE WHEN NET_PAY > 0 THEN NET_PAY ELSE 0 END) AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 6
        FROM #SalarySplit HAVING SUM(CASE WHEN NET_PAY > 0 THEN NET_PAY ELSE 0 END) > 0
        UNION ALL
        -- بدهی پرسنل فقط از خالص منفی می‌آید؛ سهم کارفرما در ردیف INS_EXP ثبت شده است.
        SELECT CAST(@ACC_SALARY_PAY AS NVARCHAR(100)), CAST(N'بدهی بیمه و مالیات پرسنل ' + @ML AS NVARCHAR(500)), CAST(SUM(PERSONAL_DEBT) AS BIGINT), CAST(0 AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 6
        FROM #SalarySplit HAVING SUM(PERSONAL_DEBT) > 0
        UNION ALL
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه تأمین اجتماعی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_WORKER + INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 7
        FROM #SalarySplit HAVING SUM(INS_WORKER + INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(TAX_AMOUNT) AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 8
        FROM #SalarySplit HAVING SUM(TAX_AMOUNT) > 0
        UNION ALL
        SELECT CAST(@ACC_LOAN_HES AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 9
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL
        SELECT CAST(@ACC_ADV_HES AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 10
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL
        SELECT CAST(@ACC_OTHER_DED_HES AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(OTHER_DED AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 11
        FROM #SalarySplit WHERE OTHER_DED > 0;
    END
    ELSE IF @DEED_MODE = 2
    BEGIN
        INSERT INTO #FinalArticles
        SELECT CAST(@ACC_SALARY_TOLID AS NVARCHAR(100)), CAST(N'هزینه حقوق تولید ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_TOLID AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_TOLID' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 1
        FROM #SalarySplit WHERE EXP_TOLID > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_EDARI AS NVARCHAR(100)), CAST(N'هزینه حقوق اداری ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_EDARI AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_EDARI' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 2
        FROM #SalarySplit WHERE EXP_EDARI > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_FOROSH AS NVARCHAR(100)), CAST(N'هزینه حقوق فروش ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_FOROSH AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_FOROSH' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 3
        FROM #SalarySplit WHERE EXP_FOROSH > 0
        UNION ALL
        SELECT CAST(@ACC_SALARY_KHADAMAT AS NVARCHAR(100)), CAST(N'هزینه حقوق خدمات ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(EXP_KHADAMAT AS BIGINT), CAST(0 AS BIGINT), CAST('EXP_KHADAMAT' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 4
        FROM #SalarySplit WHERE EXP_KHADAMAT > 0
        UNION ALL
        SELECT CAST(@ACC_INS_EXP AS NVARCHAR(100)), CAST(N'هزینه بیمه کارفرما ' + @ML AS NVARCHAR(500)), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST(0 AS BIGINT), CAST('INS_EXP' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'حقوق پرداختنی: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(NET_PAY AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE NET_PAY > 0
        UNION ALL
        -- برای پرسنل فقط‌بیمه نیز حساب شخص صرفاً به اندازه خالص منفی بدهکار می‌شود.
        SELECT CAST(ACC_T AS NVARCHAR(100)), CAST(N'بدهی بیمه و مالیات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(PERSONAL_DEBT AS BIGINT), CAST(0 AS BIGINT), CAST('SALARY_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE PERSONAL_DEBT > 0
        UNION ALL
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه سهم کارگر ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(INS_WORKER AS BIGINT), CAST('INS_PAYABLE_W' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 7
        FROM #SalarySplit WHERE INS_WORKER > 0
        UNION ALL
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه سهم کارفرما ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE_E' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 8
        FROM #SalarySplit HAVING SUM(INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(TAX_AMOUNT AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 9
        FROM #SalarySplit WHERE TAX_AMOUNT > 0
        UNION ALL
        SELECT CAST(@ACC_LOAN_HES AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 10
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL
        SELECT CAST(@ACC_ADV_HES AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 11
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL
        SELECT CAST(@ACC_OTHER_DED_HES AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(OTHER_DED AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 12
        FROM #SalarySplit WHERE OTHER_DED > 0;
    END
    ELSE IF @DEED_MODE = 3
    BEGIN
        -- ═════════════════════════════════════════════════════════════
        -- سند تفصیلی کامل
        --   بدهکار : هزینه به تفکیک «مرکز هزینه × نوع قلم» + کسورات هر پرسنل
        --   بستانکار: اقلام حکم هر پرسنل + حساب‌های مقصد کسورات
        -- مانده‌ی حساب تفصیلی هر پرسنل دقیقاً «خالص پرداختی» او می‌شود.
        -- ═════════════════════════════════════════════════════════════

        -- ── ۱) اقلام پرداختیِ هر پرسنل ────────────────────────────────
        -- مبنا همان چیزی است که موتور برای GROSS_PAY استفاده می‌کند:
        -- ریلِ «رسمی». وقتی هر دو ریل در حکم هستند BASE_SAL (اسمی) کنار
        -- گذاشته می‌شود، وگرنه حقوق پایه دوبار شمرده می‌شود.
        CREATE TABLE #EmpItem (
            EMP_ID    INT,
            ITEM_ID   INT,
            ITEM_NAME NVARCHAR(200),
            TAFSILI   SMALLINT,
            AMOUNT    BIGINT,
            SORT      SMALLINT
        );

        -- کدام ریل «پرداختی» است از روی خودِ GROSS_PAY تشخیص داده می‌شود، نه با
        -- قاعده‌ی ثابت. موتورِ امروز ریل رسمی را می‌پردازد (BASE_SAL کنار می‌رود)
        -- ولی اجراهایی که با نسخه‌های قدیمی‌تر محاسبه شده‌اند ریل اسمی را
        -- پرداخته‌اند. با قاعده‌ی ثابت، سندِ آن ماه‌ها به اندازه‌ی فاصله‌ی دو ریل
        -- غلط می‌شد — اختلافی که هم‌جنسِ گِردکردن نیست و نباید جذب شود.
        -- جدول موقت در tempdb ساخته می‌شود و در نصب‌هایی که collation دیتابیس
        -- حسابداری با tempdb فرق دارد، مقایسه DROP_CODE با ITEM_CODE_SNAP بدون
        -- COLLATE صریح با خطای 468 متوقف می‌شود.
        CREATE TABLE #Rail (
            EMP_ID INT PRIMARY KEY,
            DROP_CODE NVARCHAR(30) COLLATE database_default NULL
        );

        ;WITH Sums AS (
            SELECT D.EMP_ID,
                   SUM(D.AMOUNT) AS S_ALL,
                   SUM(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL'   THEN D.AMOUNT ELSE 0 END) AS S_NOM,
                   SUM(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL_B' THEN D.AMOUNT ELSE 0 END) AS S_OFF,
                   MAX(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL'   THEN 1 ELSE 0 END) AS HAS_NOM,
                   MAX(CASE WHEN ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) = 'BASE_SAL_B' THEN 1 ELSE 0 END) AS HAS_OFF
            FROM PAY2_RUN_DETAIL D
            LEFT JOIN PAY2_ITEM_DEF I ON I.ITEM_ID = D.ITEM_ID
            WHERE D.RUN_ID = @RUN_ID
              AND ISNULL(D.ITEM_TYPE_SNAP, I.ITEM_TYPE) IN (1, 2)
              AND D.AMOUNT <> 0
            GROUP BY D.EMP_ID
        )
        -- سه رفتار تاریخی دیده شده است: کنار گذاشتن ریل اسمی (موتور امروز)،
        -- کنار گذاشتن ریل رسمی، و اصلاً کنار نگذاشتن هیچ‌کدام. به‌جای تطبیق
        -- دقیق، گزینه‌ای انتخاب می‌شود که جمعش کمترین فاصله را با هدف دارد؛
        -- این‌طور اختلافِ گِردکردن انتخاب را خراب نمی‌کند.
        INSERT INTO #Rail (EMP_ID, DROP_CODE)
        SELECT S.EMP_ID, X.DROP_CODE
        FROM Sums S
        INNER JOIN PAY2_RUN_LINE RL ON RL.RUN_ID = @RUN_ID AND RL.EMP_ID = S.EMP_ID
        -- OUTER (نه CROSS): پرسنلی که فقط یک ریل دارند باید بمانند و هیچ قلمی
        -- از آن‌ها کنار گذاشته نشود، وگرنه کلاً از سند حذف می‌شدند.
        OUTER APPLY (
            SELECT TOP 1 C.DROP_CODE
            FROM (VALUES
                    (CAST('BASE_SAL'   AS NVARCHAR(30)), S.S_ALL - S.S_NOM, 1),
                    (CAST('BASE_SAL_B' AS NVARCHAR(30)), S.S_ALL - S.S_OFF, 2),
                    (CAST(NULL         AS NVARCHAR(30)), S.S_ALL,           3)
                 ) C(DROP_CODE, TOTAL, PREF)
            WHERE S.HAS_NOM = 1 AND S.HAS_OFF = 1
            -- تساوی: اولویت با قاعده‌ی موتور امروز (کنار گذاشتن ریل اسمی)
            ORDER BY ABS(C.TOTAL - (RL.NET_PAY + RL.TOTAL_DED)), C.PREF
        ) X;

        INSERT INTO #EmpItem (EMP_ID, ITEM_ID, ITEM_NAME, TAFSILI, AMOUNT, SORT)
        SELECT D.EMP_ID, D.ITEM_ID,
               ISNULL(ISNULL(D.ITEM_NAME_SNAP, I.ITEM_NAME), N'قلم حکم'),
               ISNULL(I.EXP_TAFSILI, 9),
               D.AMOUNT,
               ISNULL(I.SORT_ORDER, 999)
        FROM PAY2_RUN_DETAIL D
        LEFT JOIN PAY2_ITEM_DEF I ON I.ITEM_ID = D.ITEM_ID
        INNER JOIN #Rail R ON R.EMP_ID = D.EMP_ID
        WHERE D.RUN_ID = @RUN_ID
          AND ISNULL(D.ITEM_TYPE_SNAP, I.ITEM_TYPE) IN (1, 2)
          AND D.AMOUNT <> 0
          AND ISNULL(D.ITEM_CODE_SNAP, I.ITEM_CODE) COLLATE database_default
              <> ISNULL(R.DROP_CODE, N'') COLLATE database_default;

        -- تعدیلِ گِردکردنِ خالص جزو هیچ قلمی نیست. ستون ROUNDING_ADJ مبنا نیست
        -- چون در اجراهای قدیمی‌تر NULL است؛ ضمناً بسته به نسخه‌ی موتور، گِردکردن
        -- گاهی داخل GROSS_PAY تا شده و گاهی بین GROSS_PAY و NET_PAY نشسته است.
        -- تنها چیزی که در همه‌ی نسخه‌ها معتبر است این تساوی است:
        --     جمع بستانکارِ اقلام = NET_PAY + TOTAL_DED
        -- پس هدف را از روی همان می‌سازیم تا مانده‌ی حساب پرسنل دقیقاً خالص شود.
        --
        -- فقط اختلافِ هم‌اندازه‌ی گِردکردن جذب می‌شود؛ اگر اختلاف بزرگ بود یعنی
        -- ایرادِ ساختاری است (مثلاً ریل حقوق اشتباه انتخاب شده) و باید گاردِ
        -- پایین آن را با پیام روشن بگیرد، نه اینکه بی‌صدا روی حقوق تَه‌نشین شود.
        DECLARE @ROUND_TOLERANCE BIGINT = 10000;

        -- پرسنلِ فقط‌بیمه هیچ قلم پرداختی ندارند، پس تعدیل جایی برای نشستن
        -- نداشت. یک ردیفِ صفرِ حامل ساخته می‌شود تا مسیرِ تعدیل برای همه یکسان
        -- بماند؛ اگر تعدیلی نگیرد، با فیلترِ AMOUNT > 0 آرتیکلی تولید نمی‌کند.
        INSERT INTO #EmpItem (EMP_ID, ITEM_ID, ITEM_NAME, TAFSILI, AMOUNT, SORT)
        SELECT SS.EMP_ID, -1, N'تعدیل گِردکردن خالص', 1, 0, 0
        FROM #SalarySplit SS
        WHERE NOT EXISTS (SELECT 1 FROM #EmpItem EI WHERE EI.EMP_ID = SS.EMP_ID);

        ;WITH Target AS (
            SELECT EI.EMP_ID, EI.ITEM_ID,
                   ROW_NUMBER() OVER (PARTITION BY EI.EMP_ID
                                      ORDER BY CASE WHEN EI.TAFSILI = 1 THEN 0 ELSE 1 END,
                                               EI.SORT, EI.ITEM_ID) AS RN
            FROM #EmpItem EI
        ),
        Diff AS (
            SELECT RL.EMP_ID, (RL.NET_PAY + RL.TOTAL_DED) - SUM(EI.AMOUNT) AS ADJ
            FROM PAY2_RUN_LINE RL
            INNER JOIN #EmpItem EI ON EI.EMP_ID = RL.EMP_ID
            WHERE RL.RUN_ID = @RUN_ID
            GROUP BY RL.EMP_ID, RL.NET_PAY, RL.TOTAL_DED
            HAVING ABS((RL.NET_PAY + RL.TOTAL_DED) - SUM(EI.AMOUNT)) BETWEEN 1 AND @ROUND_TOLERANCE
        )
        UPDATE EI
        SET EI.AMOUNT = EI.AMOUNT + D.ADJ
        FROM #EmpItem EI
        INNER JOIN Target T ON T.EMP_ID = EI.EMP_ID AND T.ITEM_ID = EI.ITEM_ID AND T.RN = 1
        INNER JOIN Diff   D ON D.EMP_ID = EI.EMP_ID;

        -- ── ۲) سهم هر مرکز هزینه از کارکرد هر پرسنل ───────────────────
        -- IS_PRIMARY همان اولویتی است که بقیه‌ی رویه هم دارد (تولید ← اداری
        -- ← فروش ← خدمات) و باقیمانده‌ی گِردکردن روی همان مرکز می‌نشیند.
        CREATE TABLE #EmpCenter (
            EMP_ID     INT,
            CENTER     TINYINT,
            DAYS       DECIMAL(9,2),
            TOT_DAYS   DECIMAL(9,2),
            IS_PRIMARY BIT
        );

        INSERT INTO #EmpCenter (EMP_ID, CENTER, DAYS, TOT_DAYS, IS_PRIMARY)
        SELECT X.EMP_ID, X.CENTER, X.DAYS, X.TOT,
               CASE WHEN X.CENTER = (SELECT MIN(Y.CENTER) FROM (
                        SELECT A2.EMP_ID, V2.CENTER, V2.DAYS
                        FROM PAY2_ATTENDANCE A2
                        CROSS APPLY (VALUES (1, A2.DAYS_TOLID), (2, A2.DAYS_EDARI),
                                            (3, A2.DAYS_FOROSH), (4, A2.DAYS_KHADAMAT)) V2(CENTER, DAYS)
                        WHERE A2.PER_ID = @PER_ID AND V2.DAYS > 0
                    ) Y WHERE Y.EMP_ID = X.EMP_ID) THEN 1 ELSE 0 END
        FROM (
            SELECT A.EMP_ID, V.CENTER, V.DAYS,
                   (A.DAYS_TOLID + A.DAYS_EDARI + A.DAYS_FOROSH + A.DAYS_KHADAMAT) AS TOT
            FROM PAY2_ATTENDANCE A
            INNER JOIN PAY2_RUN_LINE RL ON RL.EMP_ID = A.EMP_ID AND RL.RUN_ID = @RUN_ID
            CROSS APPLY (VALUES (1, A.DAYS_TOLID), (2, A.DAYS_EDARI),
                                (3, A.DAYS_FOROSH), (4, A.DAYS_KHADAMAT)) V(CENTER, DAYS)
            WHERE A.PER_ID = @PER_ID AND V.DAYS > 0
        ) X;

        -- پرسنلِ «فقط‌بیمه» کارکردی در هیچ مرکزی ندارند ولی بیمه‌ی سهم کارفرما
        -- دارند. بدون این ردیف، آن بیمه به هیچ حساب هزینه‌ای نمی‌رفت و سند
        -- ناتراز می‌شد. اولین مرکزِ دارای ریشه به عنوان مقصد پیش‌فرض برمی‌دارد.
        INSERT INTO #EmpCenter (EMP_ID, CENTER, DAYS, TOT_DAYS, IS_PRIMARY)
        SELECT SS.EMP_ID,
               CASE WHEN @ROOT_TOLID IS NOT NULL THEN 1
                    WHEN @ROOT_EDARI IS NOT NULL THEN 2
                    WHEN @ROOT_FOROSH IS NOT NULL THEN 3
                    ELSE 4 END,
               1, 1, 1
        FROM #SalarySplit SS
        WHERE NOT EXISTS (SELECT 1 FROM #EmpCenter EC WHERE EC.EMP_ID = SS.EMP_ID)
          AND COALESCE(@ROOT_TOLID, @ROOT_EDARI, @ROOT_FOROSH, @ROOT_KHADAMAT) IS NOT NULL;

        -- ── ۳) پخش هر قلم روی مراکز هزینه ─────────────────────────────
        CREATE TABLE #ExpAlloc (
            CENTER  TINYINT,
            TAFSILI SMALLINT,
            AMOUNT  BIGINT
        );

        ;WITH Part AS (
            SELECT EI.EMP_ID, EI.ITEM_ID, EI.TAFSILI, EC.CENTER, EC.IS_PRIMARY, EI.AMOUNT,
                   CAST(ROUND(EI.AMOUNT * EC.DAYS / NULLIF(EC.TOT_DAYS, 0), 0) AS BIGINT) AS PART
            FROM #EmpItem EI
            INNER JOIN #EmpCenter EC ON EC.EMP_ID = EI.EMP_ID
        ),
        Fixed AS (
            SELECT *, SUM(PART) OVER (PARTITION BY EMP_ID, ITEM_ID) AS SUM_PART FROM Part
        )
        INSERT INTO #ExpAlloc (CENTER, TAFSILI, AMOUNT)
        SELECT CENTER, TAFSILI,
               SUM(PART + CASE WHEN IS_PRIMARY = 1 THEN AMOUNT - SUM_PART ELSE 0 END)
        FROM Fixed
        GROUP BY CENTER, TAFSILI;

        -- بیمه‌ی سهم کارفرما هزینه‌ی همان مرکز است (تفصیلی ۱۰).
        ;WITH Part AS (
            SELECT SS.EMP_ID, EC.CENTER, EC.IS_PRIMARY, SS.INS_EMPLOYER AS AMOUNT,
                   CAST(ROUND(SS.INS_EMPLOYER * EC.DAYS / NULLIF(EC.TOT_DAYS, 0), 0) AS BIGINT) AS PART
            FROM #SalarySplit SS
            INNER JOIN #EmpCenter EC ON EC.EMP_ID = SS.EMP_ID
            WHERE SS.INS_EMPLOYER > 0
        ),
        Fixed AS (
            SELECT *, SUM(PART) OVER (PARTITION BY EMP_ID) AS SUM_PART FROM Part
        )
        INSERT INTO #ExpAlloc (CENTER, TAFSILI, AMOUNT)
        SELECT CENTER, 10, SUM(PART + CASE WHEN IS_PRIMARY = 1 THEN AMOUNT - SUM_PART ELSE 0 END)
        FROM Fixed
        GROUP BY CENTER;

        -- «کسر کار» حسابِ مقصد ندارد: بدهکارِ حساب پرسنل می‌شود و در مقابل،
        -- هزینه‌ی حقوقِ همان مرکز را کم می‌کند (دقیقاً رفتار سند قدیمی).
        INSERT INTO #ExpAlloc (CENTER, TAFSILI, AMOUNT)
        SELECT EC.CENTER, 1, -SUM(SH.SHORTAGE_DED)
        FROM (
            SELECT SS.EMP_ID, SS.OTHER_DED - ISNULL(A.KASR_OTHER, 0) AS SHORTAGE_DED
            FROM #SalarySplit SS
            INNER JOIN PAY2_ATTENDANCE A ON A.EMP_ID = SS.EMP_ID AND A.PER_ID = @PER_ID
        ) SH
        INNER JOIN #EmpCenter EC ON EC.EMP_ID = SH.EMP_ID AND EC.IS_PRIMARY = 1
        WHERE SH.SHORTAGE_DED > 0
        GROUP BY EC.CENTER;

        -- ── ۴) آرتیکل‌ها ──────────────────────────────────────────────
        INSERT INTO #FinalArticles
        -- (الف) هزینه: مرکز هزینه × نوع قلم
        -- شرح، نوع قلم را هم می‌گوید: بدون آن همه‌ی خطوط یک مرکز شرح یکسان
        -- می‌گرفتند و در دفتر حسابداری «711-1-2» از «711-1-11» قابل تشخیص نبود.
        SELECT CAST(R.ROOT + N'-' + CAST(X.TAFSILI AS NVARCHAR(10)) AS NVARCHAR(100)),
               CAST(N'هزینه ' + R.CNAME + N' — '
                    + ISNULL(TL.LBL, N'قلم ' + CAST(X.TAFSILI AS NVARCHAR(10)))
                    + N' ' + @ML AS NVARCHAR(500)),
               CAST(SUM(X.AMOUNT) AS BIGINT), CAST(0 AS BIGINT),
               CAST('EXP_' + R.CKEY AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 1
        FROM #ExpAlloc X
        INNER JOIN (VALUES (1, @ROOT_TOLID,    N'تولید',  'TOLID'),
                           (2, @ROOT_EDARI,    N'اداری',  'EDARI'),
                           (3, @ROOT_FOROSH,   N'فروش',   'FOROSH'),
                           (4, @ROOT_KHADAMAT, N'خدمات',  'KHADAMAT')) R(CENTER, ROOT, CNAME, CKEY)
             ON R.CENTER = X.CENTER
        -- برچسب‌ها ثابت‌اند چون یک تفصیلی چند قلم را می‌پوشاند (مثلاً ۱ هم
        -- حقوق پایه است هم سنوات) و نامِ یکی از آن‌ها گمراه‌کننده می‌شد.
        LEFT JOIN (VALUES (1, N'حقوق'), (2, N'اضافه‌کار'), (3, N'راندمان'),
                          (4, N'حق اولاد'), (5, N'خواربار و مسکن'), (9, N'سایر'),
                          (10, N'بیمه سهم کارفرما'), (11, N'حق شیفت'),
                          (12, N'بن کارگری'), (13, N'حق تأهل و سنوات')) TL(T, LBL)
             ON TL.T = X.TAFSILI
        GROUP BY R.ROOT, R.CNAME, R.CKEY, X.TAFSILI, TL.LBL
        HAVING SUM(X.AMOUNT) <> 0

        UNION ALL
        -- (ب) اقلام حکم هر پرسنل روی حساب تفصیلی خودش
        SELECT CAST(SS.ACC_T AS NVARCHAR(100)),
               CAST(EI.ITEM_NAME + N' ' + @ML + N' | ' + SS.FULL_NAME AS NVARCHAR(500)),
               CAST(0 AS BIGINT), CAST(EI.AMOUNT AS BIGINT),
               CAST('EMP_ITEM' AS NVARCHAR(50)), CAST(EI.EMP_ID AS INT), CAST(SS.FULL_NAME AS NVARCHAR(150)), 2
        FROM #EmpItem EI
        INNER JOIN #SalarySplit SS ON SS.EMP_ID = EI.EMP_ID
        WHERE EI.AMOUNT > 0

        UNION ALL
        -- (ج) کسورات هر پرسنل: بدهکارِ حساب خودش
        SELECT CAST(SS.ACC_T AS NVARCHAR(100)),
               CAST(D.LABEL + N' ' + @ML + N' | ' + SS.FULL_NAME AS NVARCHAR(500)),
               CAST(D.AMOUNT AS BIGINT), CAST(0 AS BIGINT),
               CAST('EMP_DED' AS NVARCHAR(50)), CAST(SS.EMP_ID AS INT), CAST(SS.FULL_NAME AS NVARCHAR(150)), 3
        FROM #SalarySplit SS
        INNER JOIN PAY2_ATTENDANCE A ON A.EMP_ID = SS.EMP_ID AND A.PER_ID = @PER_ID
        CROSS APPLY (VALUES
            (N'کسر بیمه سهم کارگر', SS.INS_WORKER),
            (N'کسر مالیات',         SS.TAX_AMOUNT),
            (N'کسر قسط وام',        SS.LOAN_DED),
            (N'تصفیه مساعده',       SS.ADVANCE_DED),
            (N'سایر کسورات',        ISNULL(A.KASR_OTHER, 0)),
            (N'کسر کار',            SS.OTHER_DED - ISNULL(A.KASR_OTHER, 0))
        ) D(LABEL, AMOUNT)
        WHERE D.AMOUNT > 0

        UNION ALL
        -- (د) حساب‌های مقصد کسورات («کسر کار» مقصد ندارد؛ هزینه را کم کرده است)
        SELECT CAST(@ACC_INS_PAYABLE AS NVARCHAR(100)), CAST(N'بیمه تأمین اجتماعی ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(INS_WORKER + INS_EMPLOYER) AS BIGINT), CAST('INS_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 4
        FROM #SalarySplit HAVING SUM(INS_WORKER + INS_EMPLOYER) > 0
        UNION ALL
        SELECT CAST(@ACC_TAX_PAYABLE AS NVARCHAR(100)), CAST(N'مالیات حقوق ' + @ML AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(SUM(TAX_AMOUNT) AS BIGINT), CAST('TAX_PAYABLE' AS NVARCHAR(50)), CAST(NULL AS INT), CAST(NULL AS NVARCHAR(150)), 5
        FROM #SalarySplit HAVING SUM(TAX_AMOUNT) > 0
        UNION ALL
        SELECT CAST(@ACC_LOAN_HES AS NVARCHAR(100)), CAST(N'کسر اقساط وام: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(LOAN_DED AS BIGINT), CAST('LOAN_HES' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 6
        FROM #SalarySplit WHERE LOAN_DED > 0
        UNION ALL
        SELECT CAST(@ACC_ADV_HES AS NVARCHAR(100)), CAST(N'تصفیه مساعده: ' + @ML + N' | ' + FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(ADVANCE_DED AS BIGINT), CAST('ADVANCE_SETTLE' AS NVARCHAR(50)), CAST(EMP_ID AS INT), CAST(FULL_NAME AS NVARCHAR(150)), 7
        FROM #SalarySplit WHERE ADVANCE_DED > 0
        UNION ALL
        SELECT CAST(@ACC_OTHER_DED_HES AS NVARCHAR(100)), CAST(N'سایر کسورات: ' + @ML + N' | ' + SS.FULL_NAME AS NVARCHAR(500)), CAST(0 AS BIGINT), CAST(A.KASR_OTHER AS BIGINT), CAST('OTHER_DED' AS NVARCHAR(50)), CAST(SS.EMP_ID AS INT), CAST(SS.FULL_NAME AS NVARCHAR(150)), 8
        FROM #SalarySplit SS
        INNER JOIN PAY2_ATTENDANCE A ON A.EMP_ID = SS.EMP_ID AND A.PER_ID = @PER_ID
        WHERE ISNULL(A.KASR_OTHER, 0) > 0;

        DROP TABLE #EmpItem;
        DROP TABLE #EmpCenter;
        DROP TABLE #ExpAlloc;
        DROP TABLE #Rail;

        -- ── ۵) گاردِ آشتی ─────────────────────────────────────────────
        -- مانده‌ی حساب هر پرسنل باید دقیقاً خالص پرداختی او باشد. اگر قلمی از
        -- RUN_DETAIL جا افتاده باشد یا ریلِ حقوق اشتباه انتخاب شده باشد، سند
        -- ممکن است در جمع کل تراز بمانَد ولی حساب اشخاص غلط باشد؛ این گارد
        -- دقیقاً همان حالت را می‌گیرد.
        DECLARE @MismatchName NVARCHAR(150), @MismatchExp BIGINT, @MismatchAct BIGINT;
        SELECT TOP 1
            @MismatchName = SS.FULL_NAME,
            @MismatchExp  = SS.NET_PAY,
            @MismatchAct  = ISNULL(F.BES, 0) - ISNULL(F.BED, 0)
        FROM #SalarySplit SS
        OUTER APPLY (
            SELECT SUM(FA.BES) AS BES, SUM(FA.BED) AS BED
            FROM #FinalArticles FA
            WHERE FA.EMP_ID = SS.EMP_ID AND FA.ACC_KEY IN ('EMP_ITEM', 'EMP_DED')
        ) F
        WHERE ISNULL(F.BES, 0) - ISNULL(F.BED, 0) <> SS.NET_PAY;

        IF @MismatchName IS NOT NULL
        BEGIN
            DECLARE @ErrRec NVARCHAR(500) = N'صدور سند متوقف شد: مانده‌ی حساب پرسنل با خالص پرداختی نمی‌خواند. نام: '
                + @MismatchName + N' | خالص پرداختی: ' + CAST(@MismatchExp AS NVARCHAR(30))
                + N' | مانده‌ی آرتیکل‌ها: ' + CAST(@MismatchAct AS NVARCHAR(30))
                + N'. لطفاً محاسبه‌ی این ماه را دوباره اجرا کنید.';
            RAISERROR(@ErrRec, 16, 1);
            RETURN;
        END
    END

    -- ─────────────────────────────────────────────────────────────────
    -- 🚨 اعتبارسنجی Set-Based سطح دیتابیس (جلوگیری از ساخت دیتای یتیم)
    -- ─────────────────────────────────────────────────────────────────
    CREATE TABLE #UniqueAccounts (
        HES_CODE NVARCHAR(100) COLLATE database_default
    );

    INSERT INTO #UniqueAccounts (HES_CODE)
    SELECT DISTINCT HES_CODE FROM #FinalArticles;

    DECLARE @MissingAccounts NVARCHAR(MAX) = N'';

    ;WITH Parsed AS (
        SELECT
            HES_CODE,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[0]') AS INT) AS K,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[1]') AS INT) AS M,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[2]') AS INT) AS T1,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[3]') AS INT) AS T2,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[4]') AS INT) AS T3,
            TRY_CAST(JSON_VALUE('[""' + REPLACE(HES_CODE, '-', '"",""') + '""]', '$[5]') AS INT) AS T4
        FROM #UniqueAccounts
    ),
    Leveled AS (
        SELECT *,
            CASE
                WHEN T4 IS NOT NULL THEN 6
                WHEN T3 IS NOT NULL THEN 5
                WHEN T2 IS NOT NULL THEN 4
                WHEN T1 IS NOT NULL THEN 3
                WHEN M IS NOT NULL THEN 2
                ELSE 1
            END AS Lvl
        FROM Parsed
    )
    SELECT @MissingAccounts = @MissingAccounts + U.HES_CODE + N', '
    FROM Leveled U
    LEFT JOIN TOTA_HES K ON U.K = K.NUMBER AND U.Lvl = 1
    LEFT JOIN DETA_HES M ON U.K = M.N_KOL AND U.M = M.NUMBER AND U.Lvl = 2
    LEFT JOIN TDETA_HES T1 ON U.K = T1.N_KOL AND U.M = T1.NUMBER AND U.T1 = T1.TNUMBER AND U.Lvl = 3
    LEFT JOIN TDETA_HES2 T2 ON U.K = T2.N_KOL AND U.M = T2.NUMBER AND U.T1 = T2.TNUMBER AND U.T2 = T2.TNUMBER2 AND U.Lvl = 4
    LEFT JOIN TDETA_HES3 T3 ON U.K = T3.N_KOL AND U.M = T3.NUMBER AND U.T1 = T3.TNUMBER AND U.T2 = T3.TNUMBER2 AND U.T3 = T3.TNUMBER3 AND U.Lvl = 5
    LEFT JOIN TDETA_HES4 T4 ON U.K = T4.N_KOL AND U.M = T4.NUMBER AND U.T1 = T4.TNUMBER AND U.T2 = T4.TNUMBER2 AND U.T3 = T4.TNUMBER3 AND U.T4 = T4.TNUMBER4 AND U.Lvl = 6
    WHERE
        (U.Lvl = 1 AND K.NUMBER IS NULL) OR
        (U.Lvl = 2 AND M.NUMBER IS NULL) OR
        (U.Lvl = 3 AND T1.TNUMBER IS NULL) OR
        (U.Lvl = 4 AND T2.TNUMBER2 IS NULL) OR
        (U.Lvl = 5 AND T3.TNUMBER3 IS NULL) OR
        (U.Lvl = 6 AND T4.TNUMBER4 IS NULL) OR
        U.Lvl > 6 OR
        U.T1 IS NULL; -- 🚀 تغییر حیاتی: U.M به U.T1 تغییر یافت تا حساب‌های کمتر از ۳ سطح بلوکه شوند

    IF LEN(@MissingAccounts) > 0
    BEGIN
        -- LEN در T-SQL فاصله‌ی انتهایی را نمی‌شمارد، پس LEN-2 علاوه بر جداکننده یک
        -- کاراکترِ واقعی را هم می‌بُرید («سایر کسورات» → «سایر کسورا»). فقط «،» حذف شود.
        DECLARE @ErrAcc NVARCHAR(MAX) = N'صدور سند متوقف شد. حساب‌های زیر نامعتبرند یا فاقد حداقل ۳ سطح (کل-معین-تفصیلی) می‌باشند: ' + SUBSTRING(@MissingAccounts, 1, LEN(@MissingAccounts)-1);
        RAISERROR(@ErrAcc, 16, 1);
        RETURN;
    END

    SELECT HES_CODE, SHARH, BED, BES, ACC_KEY, EMP_ID, EmployeeName
    FROM #FinalArticles
    ORDER BY SortOrder, EmployeeName;

    DROP TABLE #SalarySplit;
    DROP TABLE #FinalArticles;
    DROP TABLE #UniqueAccounts;
END;");
                }
                catch (Exception ex)
                {
                    throw new Exception($"خطای بحرانی در دیتابیس (SP_PAY2_GEN_DEED). آپدیت متوقف شد: {ex.Message}", ex);
                }

                // ===========================================================
                // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
                // ===========================================================
                string aclScript = @"
IF OBJECT_ID(N'dbo.PAY2_USER_WS', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAY2_USER_WS]
    (
        [USERCO] INT      NOT NULL,
        [WS_ID]  INT      NOT NULL,
        [CRT]    DATETIME NULL CONSTRAINT DF_PUW_CRT DEFAULT(GETDATE()),
        [UID]    INT      NULL,
        CONSTRAINT PK_PAY2_USER_WS PRIMARY KEY ([USERCO],[WS_ID]),
        CONSTRAINT FK_PUW_WS FOREIGN KEY ([WS_ID])
            REFERENCES [dbo].[PAY2_WORKSHOP]([WS_ID]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_PAY2_USER_WS_USER ON [dbo].[PAY2_USER_WS]([USERCO]);
END;
GO

IF OBJECT_ID(N'dbo.PAY2_SEC_AUDIT', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAY2_SEC_AUDIT]
    (
        [AUDIT_ID]  BIGINT        NOT NULL IDENTITY(1,1),
        [USERCO]    INT           NOT NULL,
        [USER_NAME] NVARCHAR(50)  NULL,
        [FORM_NAME] NVARCHAR(50)  NULL,
        [PERM_FLAG] NVARCHAR(10)  NULL,
        [WS_ID]     INT           NULL,
        [ENTITY_KEY] NVARCHAR(50) NULL,
        [ALLOWED]   BIT           NOT NULL,
        [HTTP_METHOD] NVARCHAR(10) NULL,
        [PATH]      NVARCHAR(300) NULL,
        [IP]        NVARCHAR(45)  NULL,
        [DETAILS]   NVARCHAR(MAX) NULL,
        [CRT]       DATETIME      NOT NULL CONSTRAINT DF_PSA_CRT DEFAULT(GETDATE()),
        CONSTRAINT PK_PAY2_SEC_AUDIT PRIMARY KEY ([AUDIT_ID])
    );
    CREATE NONCLUSTERED INDEX IX_PAY2_SEC_AUDIT_USER_DATE
        ON [dbo].[PAY2_SEC_AUDIT]([USERCO],[CRT] DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_DASHBOARD')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_DASHBOARD', N'داشبورد حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_WORKSHOP')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_WORKSHOP', N'کارگاه‌ها و سرفصل‌های حسابداری', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_EMPLOYEE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_EMPLOYEE', N'پرسنل، قرارداد و مرخصی', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_DECREE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_DECREE', N'احکام کارگزینی', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ATTENDANCE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ATTENDANCE', N'کارکرد ماهیانه', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ADVANCE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ADVANCE', N'مساعده', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_LOAN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_LOAN', N'وام پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_RUN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_RUN', N'محاسبه حقوق ماهیانه', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_SETTLEMENT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_SETTLEMENT', N'تسویه حساب پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ITEM_DEF')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ITEM_DEF', N'تعریف آیتم‌های حکم و قالب‌ها', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_SETTINGS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_SETTINGS', N'تنظیمات حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_REPORTS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_REPORTS', N'گزارش‌های حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_CALC')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_CALC', N'اجرای محاسبه حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_FINALIZE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_FINALIZE', N'نهایی‌کردن محاسبه حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_REVERT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_REVERT', N'برگشت محاسبه حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_DEED')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_DEED', N'صدور سند حسابداری حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_DEED_UNDO')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_DEED_UNDO', N'ابطال سند حسابداری حقوق', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_PERIOD_CLOSE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_PERIOD_CLOSE', N'بستن دوره کارکرد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_PERIOD_REOPEN')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_PERIOD_REOPEN', N'بازگشایی/حذف دوره کارکرد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_DECREE_CONFIRM')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_DECREE_CONFIRM', N'تأیید نهایی حکم کارگزینی', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_SETTLE_FINALIZE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_SETTLE_FINALIZE', N'نهایی‌کردن و برگشت تسویه حساب', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_VIEW_AMOUNTS')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_VIEW_AMOUNTS', N'مشاهده مبالغ حقوق سایر پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_CONFIG_CRITICAL')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_CONFIG_CRITICAL', N'تغییر تنظیمات حساس (نرخ بیمه/مالیات/سقف)', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_ADV_EXCL')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_ADV_EXCL', N'ثبت استثنای دستی مساعده', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_OVERRIDE')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_OVERRIDE', N'تغییر مشمولیت بیمه/مالیات پرسنل', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ACT_EXPORT')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ACT_EXPORT', N'خروجی اکسل/PDF و دیسکت بیمه و مالیات', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ADMIN_ACL')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_ADMIN_ACL', N'مدیریت دسترسی‌های حقوق و دستمزد', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO


IF COL_LENGTH(N'dbo.PAY2_EMPLOYEE', N'USERCO') IS NULL
BEGIN
    ALTER TABLE dbo.PAY2_EMPLOYEE ADD [USERCO] INT NULL;
    EXEC('CREATE UNIQUE NONCLUSTERED INDEX UX_EMP_USERCO ON dbo.PAY2_EMPLOYEE([USERCO]) WHERE [USERCO] IS NOT NULL');
END;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_SELF_PAYSLIP')
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_SELF_PAYSLIP', N'فیش حقوقی من', 3, 9, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_ENFORCE')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_ENFORCE', N'0', N'1|0', N'0', N'امنیت', N'فعال‌سازی کنترل دسترسی حقوق و دستمزد', N'۰ = خاموش (وضعیت فعلی). ۱ = کنترل دسترسی‌ها کاملا فعال و اعمال می‌شود.', NULL, N'BOOL', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_WS_SCOPE_ENFORCE')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_WS_SCOPE_ENFORCE', N'1', N'1|0', N'1', N'امنیت', N'محدودکردن کاربر به کارگاه‌های مجاز', N'۱ = کارگاه‌های مجاز هر فرد کنترل می‌شود', NULL, N'BOOL', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_CACHE_SECONDS')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_CACHE_SECONDS', N'120', NULL, N'120', N'امنیت', N'مدت نگهداری کش دسترسی‌ها (ثانیه)', NULL, NULL, N'INT', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_AUDIT_DENIED')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_AUDIT_DENIED', N'1', N'1|0', N'1', N'امنیت', N'ثبت لاگ تلاش‌های ناموفق دسترسی', NULL, NULL, N'BOOL', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY=N'ACL_AUDIT_SENSITIVE')
    INSERT INTO dbo.PAY2_CONFIG (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION, LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL)
    VALUES (N'ACL_AUDIT_SENSITIVE', N'1', N'1|0', N'1', N'امنیت', N'ثبت لاگ عملیات حساس', NULL, NULL, N'BOOL', 1);
GO

-- این چهار کلید موقع اضافه شدن، OPT_LABELS نداشتند؛ صفحه‌ی تنظیمات به‌جای
-- برچسب فارسی، خودِ عدد خام «۰»/«۱» را نشان می‌داد. روی نصب‌های قبلی هم
-- (که این کلیدها را از قبل INSERT کرده‌اند) این UPDATE لازم است، چون
-- IF NOT EXISTS بالا برای آن‌ها دیگر اجرا نمی‌شود.
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'روشن — دسترسی‌ها اعمال می‌شود|خاموش — همه به همه‌چیز دسترسی دارند' WHERE CFG_KEY = N'ACL_ENFORCE';
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'محدود به کارگاه‌های مجاز|بدون محدودیت کارگاه' WHERE CFG_KEY = N'ACL_WS_SCOPE_ENFORCE';
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'ثبت می‌شود|ثبت نمی‌شود' WHERE CFG_KEY = N'ACL_AUDIT_DENIED';
UPDATE dbo.PAY2_CONFIG SET OPT_LABELS = N'ثبت می‌شود|ثبت نمی‌شود' WHERE CFG_KEY = N'ACL_AUDIT_SENSITIVE';
GO

UPDATE dbo.PAY2_CONFIG
SET DESC_FA = N'منسوخ — جایگزین: کنترل دسترسی مبتنی بر SAL_CHEK'
WHERE CFG_KEY IN (N'CONFIG_MIN_ROLE', N'ITEM_DEF_MIN_ROLE');
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK WHERE [OBJECT] IN (SELECT IDH FROM dbo.TFORMS WHERE FORMNAME = N'PAY2_ADMIN_ACL'))
BEGIN
    DECLARE @Users TABLE (IDD INT);
    INSERT INTO @Users SELECT USERCO FROM dbo.SAL_CHEK WHERE [OBJECT] = (SELECT IDH FROM dbo.TFORMS WHERE FORMNAME = N'DEED_HEAD') AND RUN = 1
    AND USERCO IN (SELECT IDD FROM dbo.SALA_DTL WHERE ENABL = 1);

    IF NOT EXISTS (SELECT 1 FROM @Users)
    BEGIN
        INSERT INTO @Users SELECT TOP 1 IDD FROM dbo.SALA_DTL WHERE ENABL = 1 ORDER BY IDD ASC;
    END

    INSERT INTO dbo.SAL_CHEK ([USERCO], [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], [CRT])
    SELECT U.IDD, F.IDH, 1, 1, 1, 1, 1, GETDATE()
    FROM @Users U
    CROSS JOIN dbo.TFORMS F
    WHERE F.FORMNAME LIKE N'PAY2!_%' ESCAPE N'!'
      AND NOT EXISTS (SELECT 1 FROM dbo.SAL_CHEK SC WHERE SC.[USERCO] = U.IDD AND SC.[OBJECT] = F.IDH);

    INSERT INTO dbo.PAY2_USER_WS ([USERCO], [WS_ID], [CRT])
    SELECT U.IDD, W.WS_ID, GETDATE()
    FROM @Users U
    CROSS JOIN dbo.PAY2_WORKSHOP W
    WHERE W.IS_ACTIVE = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.PAY2_USER_WS UW WHERE UW.[USERCO] = U.IDD AND UW.[WS_ID] = W.WS_ID);

    PRINT N'مجوز اولیه (Bootstrap) برای کاربران صادر شد.';
END;
GO
";
                ExecuteBatches(db, aclScript);
                LoadJobData(db);

                // ===========================================================
                // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
                // ===========================================================

                return;
            }

            // ===========================================================
            // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
            // ===========================================================



            // ===========================================================
            // 4. Migration 011: PAY2 ACL Enforcements (Workshop scope, Auditing)
            // ===========================================================


        }
        /// <summary>
        /// رفع «ازدحام درج روی آخرین صفحه» (Last-Page Insert Contention).
        ///
        /// چرا لازم است: جدول‌های پرترافیک درج مثل DEED_DTL و INVO_LST روی یک ستون IDENTITY
        /// صعودی خوشه‌بندی شده‌اند، پس هر درج تازه روی «همان» آخرین صفحه می‌نشیند. وقتی
        /// بازسازی AUTO_BAZ بخش‌های C1..C11 را هم‌زمان اجرا می‌کند، همه‌ی Threadها برای همان
        /// یک صفحه latch انحصاری می‌خواهند و پشت هم صف می‌کشند. اندازه‌گیری روی YAZDSEPAR1405:
        /// PAGELATCH_EX با ۲٫۵ میلیون انتظار و ۸۵ میلیون میلی‌ثانیه، در حالی که قفل ردیف
        /// (LCK_M_X) فقط ۵٫۶ هزار میلی‌ثانیه بود — یعنی گلوگاه latch صفحه است نه قفل تراکنش.
        ///
        /// OPTIMIZE_FOR_SEQUENTIAL_KEY (از SQL Server 2019) همین صف را منظم می‌کند و
        /// convoy را می‌شکند. عملیات metadata-only است: نه Rebuild لازم دارد، نه قفل طولانی.
        ///
        /// دامنه: فقط ایندکس‌هایی که ستون کلید «اولشان» IDENTITY است — چون تنها همین‌ها
        /// الگوی درج صعودی دارند. ایندکس روی HES یا NUMBER از این گزینه سودی نمی‌برد.
        ///
        /// idempotent است: هر ایندکسی که از قبل ON باشد در فهرست نمی‌آید.
        /// </summary>
    }
}
