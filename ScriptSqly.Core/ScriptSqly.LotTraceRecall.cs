using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ردیابیِ سری ساخت در Safir، فازِ سه: رسیدگی به شکایت (TRACE_COMPLAINT، TRACE_COMPLAINT_LOT، TRACE_COMPLAINT_NOTE کنارِ
        /// CustomerComplaints ِ فرمِ عمومی) و فراخوانِ نسخه‌دار (TRACE_RECALL، _VER، _LOT، _PARTY، _TRACK)، با فرم‌های دسترسیِ
        /// TRACE_COMPLAINT و TRACE_RECALL. بعد از LotTraceProductionScript اجرا می‌شود (FK به TRACE_LOT).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/70-lot-trace-recall.sql در مخزن Safir یکی بماند
        /// (آزمونِ LotTraceRecallTests.Migration_matches_the_ScriptSqly_copy_and_runs_after_phase_two همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void LotTraceRecallScript(SqlConnection db)
        {
            ExecuteBatches(db, LotTraceRecallSql);
        }

        private const string LotTraceRecallSql = @"/* ═══════════════════════════════════════════════════════════════════
   ردیابیِ سری ساخت — فازِ سه: پرونده‌ی شکایت و فراخوان (طرحِ ۱۴۰۵/۰۸/۱۱، بخش‌های ۹ و ۱۰).

   شکایت‌ها همان CustomerComplaints ِ فرمِ عمومیِ Safir است (WPF این جدول را نمی‌خواند)؛ این‌جا فقط رسیدگیِ داخلی کنارش می‌نشیند:
   • TRACE_COMPLAINT       — پرونده‌ی هر شکایت (کلید = CustomerComplaints.ComplaintID، بی FK): وضعیت (۰ جدید، ۱ در حالِ بررسی،
                             ۲ بسته)، شدت (۱ کم تا ۴ بحرانی)، مسئول، مهلت، مدرک، نتیجه‌ی بررسی و علتِ تأییدشده.
   • TRACE_COMPLAINT_LOT   — سری‌هایی که به شکایت وصل‌اند: ۰ نامزد، ۱ تأییدشده، ۲ ردشده، با مدرک و تصمیم‌گیرنده. اطلاعاتِ مشتری
                             مشخصاتِ سری را عوض نمی‌کند.
   • TRACE_COMPLAINT_NOTE  — دفترِ اقدام‌ها: یادداشت، پاسخ به مشتری، تعویض، استرداد، اصلاحِ فرایند، تأمین‌کننده، فراخوان.
   • TRACE_RECALL          — فراخوان (یا مانورِ فراخوان، DRILL): دلیل، شکایتِ مبدأ، وضعیت (۰ باز، ۱ اطلاع‌رسانی تأیید شد، ۲ بسته).
   • TRACE_RECALL_VER      — هر استخراجِ دامنه یک نسخه با زمانِ برش، دلیل و شمارش‌ها؛ استخراجِ دوباره نسخه‌ی قبلی را پاک نمی‌کند.
   • TRACE_RECALL_LOT      — سری‌های هر نسخه: ریشه (انتخابِ مسئول) یا وابسته (ردیابیِ جلورو) با قوتِ پیوند.
   • TRACE_RECALL_PARTY    — دریافت‌کنندگانِ هر نسخه با تماس و مقدارِ همان لحظه.
   • TRACE_RECALL_TRACK    — پیگیریِ هر دریافت‌کننده (۰ در انتظار، ۱ اطلاع داده شد، ۲ پاسخ داد، ۳ جمع شد، ۴ مصرف‌شده یا بی‌دسترس).
   • فرم‌های TRACE_COMPLAINT «ردیابی: رسیدگی به شکایت» و TRACE_RECALL «ردیابی: فراخوان» با ردیفِ بسته‌ی SAL_CHEK برای همه.
   هیچ جدول، ویو، رویه یا تریگرِ قبلی (از جمله CustomerComplaints) عوض نمی‌شود. idempotent است و بعد از مهاجرت‌های 68 و 69 اجرا می‌شود.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.TRACE_COMPLAINT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_COMPLAINT (
        COMPLAINT_ID int NOT NULL CONSTRAINT PK_TRACE_COMPLAINT PRIMARY KEY,
        STATUS tinyint NOT NULL CONSTRAINT DF_TRACE_COMPLAINT_STATUS DEFAULT 0,
        SEVERITY tinyint NOT NULL CONSTRAINT DF_TRACE_COMPLAINT_SEVERITY DEFAULT 2,
        OWNER_UID int NULL,
        DUE_DATE bigint NULL,
        EVIDENCE nvarchar(1000) NULL,
        RESULT nvarchar(2000) NULL,
        ROOT_CAUSE nvarchar(1000) NULL,
        CLOSED_AT datetime NULL,
        CLOSED_UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_COMPLAINT_CRT DEFAULT GETDATE(),
        UID int NULL,
        UPD datetime NULL,
        UPD_UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.TRACE_COMPLAINT_LOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_COMPLAINT_LOT (
        COMPLAINT_ID int NOT NULL,
        LOT_ID int NOT NULL CONSTRAINT FK_TRACE_COMPLAINT_LOT_LOT REFERENCES dbo.TRACE_LOT (ID),
        STATUS tinyint NOT NULL CONSTRAINT DF_TRACE_COMPLAINT_LOT_STATUS DEFAULT 0,
        NOTE nvarchar(500) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_COMPLAINT_LOT_CRT DEFAULT GETDATE(),
        UID int NULL,
        DECIDED_AT datetime NULL,
        DECIDED_UID int NULL,
        CONSTRAINT PK_TRACE_COMPLAINT_LOT PRIMARY KEY (COMPLAINT_ID, LOT_ID)
    );
    CREATE INDEX IX_TRACE_COMPLAINT_LOT_LOT ON dbo.TRACE_COMPLAINT_LOT (LOT_ID);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_COMPLAINT_NOTE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_COMPLAINT_NOTE (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TRACE_COMPLAINT_NOTE PRIMARY KEY,
        COMPLAINT_ID int NOT NULL,
        KIND tinyint NOT NULL,
        NOTE nvarchar(1000) NOT NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_COMPLAINT_NOTE_CRT DEFAULT GETDATE(),
        UID int NULL
    );
    CREATE INDEX IX_TRACE_COMPLAINT_NOTE_COMPLAINT ON dbo.TRACE_COMPLAINT_NOTE (COMPLAINT_ID);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_RECALL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_RECALL (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TRACE_RECALL PRIMARY KEY,
        TITLE nvarchar(200) NOT NULL,
        REASON nvarchar(1000) NULL,
        COMPLAINT_ID int NULL,
        DRILL bit NOT NULL CONSTRAINT DF_TRACE_RECALL_DRILL DEFAULT 0,
        BLOCKED bit NOT NULL CONSTRAINT DF_TRACE_RECALL_BLOCKED DEFAULT 0,
        STATUS tinyint NOT NULL CONSTRAINT DF_TRACE_RECALL_STATUS DEFAULT 0,
        NOTIFY_AT datetime NULL,
        NOTIFY_UID int NULL,
        CLOSED_AT datetime NULL,
        CLOSED_UID int NULL,
        CLOSE_NOTE nvarchar(1000) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_RECALL_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.TRACE_RECALL_VER', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_RECALL_VER (
        RECALL_ID int NOT NULL CONSTRAINT FK_TRACE_RECALL_VER_RECALL REFERENCES dbo.TRACE_RECALL (ID) ON DELETE CASCADE,
        VER int NOT NULL,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_RECALL_VER_CRT DEFAULT GETDATE(),
        UID int NULL,
        NOTE nvarchar(500) NULL,
        LOTS int NOT NULL,
        PARTIES int NOT NULL,
        QTY decimal(19, 6) NOT NULL,
        POSSIBLE int NOT NULL,
        HIDDEN int NOT NULL,
        TRUNCATED bit NOT NULL,
        MS int NOT NULL,
        CONSTRAINT PK_TRACE_RECALL_VER PRIMARY KEY (RECALL_ID, VER)
    );
END;
GO

IF OBJECT_ID(N'dbo.TRACE_RECALL_LOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_RECALL_LOT (
        RECALL_ID int NOT NULL,
        VER int NOT NULL,
        LOT_ID int NOT NULL CONSTRAINT FK_TRACE_RECALL_LOT_LOT REFERENCES dbo.TRACE_LOT (ID),
        ROOT bit NOT NULL,
        LINK nvarchar(12) NOT NULL,
        DEPTH int NOT NULL,
        VIA nvarchar(200) NULL,
        CONSTRAINT PK_TRACE_RECALL_LOT PRIMARY KEY (RECALL_ID, VER, LOT_ID),
        CONSTRAINT FK_TRACE_RECALL_LOT_VER FOREIGN KEY (RECALL_ID, VER) REFERENCES dbo.TRACE_RECALL_VER (RECALL_ID, VER) ON DELETE CASCADE
    );
    CREATE INDEX IX_TRACE_RECALL_LOT_LOT ON dbo.TRACE_RECALL_LOT (LOT_ID);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_RECALL_PARTY', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_RECALL_PARTY (
        RECALL_ID int NOT NULL,
        VER int NOT NULL,
        CUST_NO nvarchar(60) NOT NULL,
        NAME nvarchar(200) NULL,
        PHONE nvarchar(60) NULL,
        CITY nvarchar(100) NULL,
        DELIVERED decimal(19, 6) NOT NULL,
        RETURNED decimal(19, 6) NOT NULL,
        LOTS nvarchar(1000) NULL,
        POSSIBLE bit NOT NULL,
        FIRST_DATE bigint NULL,
        LAST_DATE bigint NULL,
        DOCS int NOT NULL,
        CONSTRAINT PK_TRACE_RECALL_PARTY PRIMARY KEY (RECALL_ID, VER, CUST_NO),
        CONSTRAINT FK_TRACE_RECALL_PARTY_VER FOREIGN KEY (RECALL_ID, VER) REFERENCES dbo.TRACE_RECALL_VER (RECALL_ID, VER) ON DELETE CASCADE
    );
END;
GO

IF OBJECT_ID(N'dbo.TRACE_RECALL_TRACK', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_RECALL_TRACK (
        RECALL_ID int NOT NULL CONSTRAINT FK_TRACE_RECALL_TRACK_RECALL REFERENCES dbo.TRACE_RECALL (ID) ON DELETE CASCADE,
        CUST_NO nvarchar(60) NOT NULL,
        STATE tinyint NOT NULL CONSTRAINT DF_TRACE_RECALL_TRACK_STATE DEFAULT 0,
        NOTE nvarchar(500) NULL,
        UPD datetime NOT NULL CONSTRAINT DF_TRACE_RECALL_TRACK_UPD DEFAULT GETDATE(),
        UPD_UID int NULL,
        CONSTRAINT PK_TRACE_RECALL_TRACK PRIMARY KEY (RECALL_ID, CUST_NO)
    );
END;
GO

/* ── فرم‌های دسترسی، کنارِ «ردیابی سری ساخت کالا» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    DECLARE @Grp int = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'TRACE_LOT'),
                       ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'HEAD_LST_RASID'), 1));

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'TRACE_COMPLAINT')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'TRACE_COMPLAINT', N'ردیابی: رسیدگی به شکایت', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'TRACE_RECALL')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'TRACE_RECALL', N'ردیابی: فراخوان', 3, @Grp,
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
    WHERE F.FORMNAME IN (N'TRACE_COMPLAINT', N'TRACE_RECALL')
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END
GO

PRINT N'ردیابی سری ساخت (شکایت و فراخوان): جدول‌های TRACE_COMPLAINT* و TRACE_RECALL* و فرم‌های TRACE_COMPLAINT و TRACE_RECALL آماده شد.';
GO";
    }
}
