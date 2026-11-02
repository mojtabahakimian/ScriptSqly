using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ردیابیِ سری ساخت و تاریخ انقضا در Safir: کالاهای مشمول (TRACE_ITEM_POLICY)، سری‌ها (TRACE_LOT)، تقسیمِ هر طرف از
        /// سطرِ INVO_LST بینِ سری‌ها (TRACE_DOC، TRACE_DOC_LOT)، موجودیِ شروع (TRACE_OPENING)، قرنطینه (TRACE_HOLD)، سابقه
        /// (TRACE_AUDIT) و فرم‌های دسترسیِ TRACE_LOT و TRACE_QC. فقط با «دریافتِ شماره‌ی سریالِ کالا فعال شود» (OPTIONSS[49]) به کار می‌آید.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/68-lot-trace.sql در مخزن Safir یکی بماند
        /// (آزمونِ LotTraceTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void LotTraceScript(SqlConnection db)
        {
            ExecuteBatches(db, LotTraceSql);
        }

        private const string LotTraceSql = @"/* ═══════════════════════════════════════════════════════════════════
   ردیابیِ سری ساخت و تاریخ انقضا در Safir — فقط وقتی «دریافتِ شماره‌ی سریالِ کالا فعال شود» (SAZMAN.OPTIONSS[49] = '5')
   روشن است و فقط برای کالاهایی که در TRACE_ITEM_POLICY آمده‌اند.

   • TRACE_ITEM_POLICY — کالاهای مشمول: از کدام تاریخ هر گردش سری می‌خواهد (START_DATE؛ اول سال یا وسطِ سال)، اجباری بودنِ
                         تاریخِ انقضا و تولید، عمرِ مفید برای پیشنهادِ تاریخ، حداقلِ عمرِ مانده هنگامِ فروش و بازه‌ی هشدار.
   • TRACE_LOT         — هویتِ هر سری: کالا، شماره‌ی چاپ‌شده عیناً (LOT_NO) و متنِ یکتاییِ آن (LOT_KEY)، سازنده، تاریخ‌ها و
                         مسدودیِ سراسری. یک کالا و یک سازنده با یک شماره فقط یک سری دارد (UX_TRACE_LOT_KEY).
   • TRACE_DOC         — عکسِ هر طرف از سطرِ INVO_LST که سری گرفته: SIDE ۱ = MEGHk، SIDE ۲ = MEGH_MAR (مرجوعیِ همان سطر یا ورودِ
                         کالای مقصدِ تبدیل)، با TAG، کالا، انبارها و مقدارِ همان لحظه. اگر سطر بیرون از Safir عوض یا حذف شود این
                         عکس دیگر نمی‌خواند، مقدارش «بی‌سری» حساب می‌شود و در «گردش بدون سری» می‌آید.
   • TRACE_DOC_LOT     — تقسیمِ مقدارِ همان طرف بینِ سری‌ها (جمعش برابرِ QTY ِ عکس).
   • TRACE_OPENING     — مانده‌ی هر سری در هر انبار پیش از START_DATE ِ کالا (موجودیِ شروع).
   • TRACE_HOLD        — قرنطینه‌ی بخشی از یک سری در یک انبار: موجودی را کم نمی‌کند، فقط فروش و مصرفش را می‌بندد.
   • TRACE_AUDIT       — سابقه‌ی هر تغییر با قبل و بعد؛ در محاسبه‌ی مانده نمی‌آید.
   • فرم‌های TRACE_LOT «ردیابی سری ساخت کالا» و TRACE_QC «ردیابی: مسدودی، قرنطینه و اصلاح تاریخ سری» در TFORMS، با ردیفِ
     بسته‌ی SAL_CHEK برای همه‌ی کاربران تا در «تعیین سطح دسترسی» دیده و داده شوند.
   هیچ جدول، ویو، رویه یا تریگرِ قبلی عوض نمی‌شود و FK به INVO_LST نیست تا حذفِ سند سابقه‌ی ردیابی را پاک نکند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.TRACE_ITEM_POLICY', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_ITEM_POLICY (
        CODE nvarchar(15) NOT NULL CONSTRAINT PK_TRACE_ITEM_POLICY PRIMARY KEY,
        ACTIVE bit NOT NULL CONSTRAINT DF_TRACE_ITEM_POLICY_ACTIVE DEFAULT 1,
        START_DATE bigint NOT NULL,
        NEED_EXP bit NOT NULL CONSTRAINT DF_TRACE_ITEM_POLICY_NEED_EXP DEFAULT 1,
        NEED_PROD bit NOT NULL CONSTRAINT DF_TRACE_ITEM_POLICY_NEED_PROD DEFAULT 0,
        SHELF_DAYS int NULL,
        MIN_LIFE_DAYS int NULL,
        WARN_DAYS int NULL,
        NOTE nvarchar(300) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_ITEM_POLICY_CRT DEFAULT GETDATE(),
        UID int NULL,
        UPD datetime NULL,
        UPD_UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.TRACE_LOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_LOT (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TRACE_LOT PRIMARY KEY,
        LOT_GUID uniqueidentifier NOT NULL CONSTRAINT DF_TRACE_LOT_GUID DEFAULT NEWID(),
        CODE nvarchar(15) NOT NULL,
        LOT_NO nvarchar(60) NOT NULL,
        LOT_KEY nvarchar(60) NOT NULL,
        MAKER nvarchar(100) NULL,
        MAKER_KEY nvarchar(100) NOT NULL CONSTRAINT DF_TRACE_LOT_MAKER_KEY DEFAULT N'',
        PROD_DATE bigint NULL,
        EXP_DATE bigint NULL,
        BLOCKED bit NOT NULL CONSTRAINT DF_TRACE_LOT_BLOCKED DEFAULT 0,
        BLOCK_NOTE nvarchar(300) NULL,
        BLOCK_AT datetime NULL,
        BLOCK_UID int NULL,
        NOTE nvarchar(300) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_LOT_CRT DEFAULT GETDATE(),
        UID int NULL
    );
    CREATE UNIQUE INDEX UX_TRACE_LOT_GUID ON dbo.TRACE_LOT (LOT_GUID);
    CREATE UNIQUE INDEX UX_TRACE_LOT_KEY ON dbo.TRACE_LOT (CODE, LOT_KEY, MAKER_KEY);
    CREATE INDEX IX_TRACE_LOT_EXP ON dbo.TRACE_LOT (CODE, EXP_DATE);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_DOC', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_DOC (
        ROW_ID bigint NOT NULL,
        SIDE tinyint NOT NULL,
        TAG int NOT NULL,
        NUMBER float NOT NULL,
        CODE nvarchar(40) NOT NULL,
        ANBAR int NOT NULL,
        ANBARF int NOT NULL,
        QTY float NOT NULL,
        AUTO bit NOT NULL CONSTRAINT DF_TRACE_DOC_AUTO DEFAULT 0,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_DOC_CRT DEFAULT GETDATE(),
        UID int NULL,
        UPD datetime NULL,
        UPD_UID int NULL,
        CONSTRAINT PK_TRACE_DOC PRIMARY KEY (ROW_ID, SIDE),
        CONSTRAINT CK_TRACE_DOC_SIDE CHECK (SIDE IN (1, 2))
    );
    CREATE INDEX IX_TRACE_DOC_DOC ON dbo.TRACE_DOC (TAG, NUMBER);
    CREATE INDEX IX_TRACE_DOC_CODE ON dbo.TRACE_DOC (CODE);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_DOC_LOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_DOC_LOT (
        ROW_ID bigint NOT NULL,
        SIDE tinyint NOT NULL,
        LOT_ID int NOT NULL CONSTRAINT FK_TRACE_DOC_LOT_LOT REFERENCES dbo.TRACE_LOT (ID),
        QTY decimal(19, 6) NOT NULL,
        CONSTRAINT PK_TRACE_DOC_LOT PRIMARY KEY (ROW_ID, SIDE, LOT_ID),
        CONSTRAINT FK_TRACE_DOC_LOT_DOC FOREIGN KEY (ROW_ID, SIDE) REFERENCES dbo.TRACE_DOC (ROW_ID, SIDE) ON DELETE CASCADE,
        CONSTRAINT CK_TRACE_DOC_LOT_QTY CHECK (QTY > 0)
    );
    CREATE INDEX IX_TRACE_DOC_LOT_LOT ON dbo.TRACE_DOC_LOT (LOT_ID) INCLUDE (QTY);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_OPENING', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_OPENING (
        LOT_ID int NOT NULL CONSTRAINT FK_TRACE_OPENING_LOT REFERENCES dbo.TRACE_LOT (ID),
        ANBAR int NOT NULL,
        QTY decimal(19, 6) NOT NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_OPENING_CRT DEFAULT GETDATE(),
        UID int NULL,
        CONSTRAINT PK_TRACE_OPENING PRIMARY KEY (LOT_ID, ANBAR),
        CONSTRAINT CK_TRACE_OPENING_QTY CHECK (QTY > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.TRACE_HOLD', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_HOLD (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TRACE_HOLD PRIMARY KEY,
        LOT_ID int NOT NULL CONSTRAINT FK_TRACE_HOLD_LOT REFERENCES dbo.TRACE_LOT (ID),
        ANBAR int NOT NULL,
        QTY decimal(19, 6) NOT NULL,
        REASON nvarchar(300) NOT NULL,
        ACTIVE bit NOT NULL CONSTRAINT DF_TRACE_HOLD_ACTIVE DEFAULT 1,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_HOLD_CRT DEFAULT GETDATE(),
        UID int NULL,
        REL_AT datetime NULL,
        REL_UID int NULL,
        REL_NOTE nvarchar(300) NULL,
        CONSTRAINT CK_TRACE_HOLD_QTY CHECK (QTY > 0)
    );
    CREATE INDEX IX_TRACE_HOLD_LOT ON dbo.TRACE_HOLD (LOT_ID, ANBAR, ACTIVE);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_AUDIT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_AUDIT (
        ID bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TRACE_AUDIT PRIMARY KEY,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_AUDIT_CRT DEFAULT GETDATE(),
        UID int NULL,
        USER_NAME nvarchar(100) NULL,
        KIND nvarchar(30) NOT NULL,
        CODE nvarchar(40) NULL,
        LOT_ID int NULL,
        ROW_ID bigint NULL,
        SIDE tinyint NULL,
        BEFORE_ nvarchar(max) NULL,
        AFTER_ nvarchar(max) NULL,
        NOTE nvarchar(400) NULL
    );
    CREATE INDEX IX_TRACE_AUDIT_LOT ON dbo.TRACE_AUDIT (LOT_ID, CRT);
    CREATE INDEX IX_TRACE_AUDIT_ROW ON dbo.TRACE_AUDIT (ROW_ID, CRT);
END;
GO

/* ── فرم‌های دسترسی، کنارِ «رسید انبار» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    DECLARE @Grp int = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'HEAD_LST_RASID'), 1);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'TRACE_LOT')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'TRACE_LOT', N'ردیابی سری ساخت کالا', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'TRACE_QC')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'TRACE_QC', N'ردیابی: مسدودی، قرنطینه و اصلاح تاریخ سری', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH, 0, 0, 0, 0, 0, GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    WHERE F.FORMNAME IN (N'TRACE_LOT', N'TRACE_QC')
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END
GO

PRINT N'ردیابی سری ساخت: جدول‌های TRACE_* و فرم‌های TRACE_LOT و TRACE_QC آماده شد.';
GO";
    }
}
