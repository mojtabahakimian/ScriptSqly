using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «کارمند حسابداری مجازی» در Safir — فرم ACC_CLERK و جدول‌های VCL_* (پیش‌نویس‌ها، گفتگو، پیوست‌ها، تنظیمات).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/58-acc-clerk.sql در مخزن Safir یکی بماند
        /// (آزمونِ AccClerkTests همین را می‌سنجد). بعد از AccAuditScript (دسترسی از ACC_AUDIT کپی می‌شود). idempotent است.
        /// </summary>
        private static void AccClerkScript(SqlConnection db)
        {
            ExecuteBatches(db, AccClerkSql);
        }

        private const string AccClerkSql = @"/* ═══════════════════════════════════════════════════════════════════
   «کارمند حسابداری مجازی» (Server/Clerk)

   کاربر رویدادِ مالی را با متن، صدا یا عکس می‌گوید؛ کارمندِ مجازی (مدلِ
   هوش مصنوعی) پیش‌نویسِ سند را از راهِ فرمِ درست تنظیم می‌کند (خزانه برای
   نقد و بانک، سندِ دستی برای بقیه)، رئیس حسابداری مجازی (ایجنتِ اسکریپت
   ۴۶) پیش از ثبت رسیدگی می‌کند و تأییدکننده‌ی انسانی تصمیم می‌گیرد.

   سندِ واقعی فقط با تأییدِ انسان در DEED_HED / PGET_HED ساخته می‌شود:
   در این دیتابیس سندِ تأییدنشده هم در همه‌ی گزارش‌ها حساب می‌شود، پس
   پیش‌نویس تا آن لحظه فقط در جدول‌های همین اسکریپت است.

   ۱) فرم ACC_CLERK در TFORMS — RUN یعنی درخواست به کارمند و دیدنِ
      درخواست‌های خود، SEE یعنی دیدنِ درخواست‌های همه، UPD یعنی تنظیمات.
      هر سه از ACC_AUDIT (اسکریپت ۴۵) کپی می‌شود.
   ۲) VCL_Config     — تنظیمات (یک ردیف). پیش‌فرض خاموش. هر کارمندِ مجازی
                       اسم دارد (ClerkName، ChiefName)؛ درخواستِ تازه با صدا
                       زدنِ اسمِ کارمند شروع می‌شود.
   ۳) VCL_Draft      — هر درخواست، پیش‌نویسِ کارمند و حکمِ رئیس.
   ۴) VCL_Event      — گفتگو و رویدادهای هر درخواست.
   ۵) VCL_Attachment — عکس و صدای درخواست (شاهدِ سند).

   idempotent است؛ چند بار اجرا شدنش ضرری ندارد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.VCL_Config', N'U') IS NULL
CREATE TABLE dbo.VCL_Config (
    Id                 TINYINT        NOT NULL CONSTRAINT PK_VCL_Config PRIMARY KEY CONSTRAINT CK_VCL_Config_One CHECK (Id = 1),
    IsEnabled          BIT            NOT NULL CONSTRAINT DF_VCL_Config_Enabled   DEFAULT 0,
    ApproverUserCo     INT            NULL,
    HighAmount         FLOAT          NULL,       -- بالای این مبلغ (ریال) تأیید با HighApproverUserCo است
    HighApproverUserCo INT            NULL,
    MaxRevisions       TINYINT        NOT NULL CONSTRAINT DF_VCL_Config_Revisions DEFAULT 2,
    DailyCallLimit     INT            NOT NULL CONSTRAINT DF_VCL_Config_Calls     DEFAULT 200,
    ClerkName          NVARCHAR(30)   NOT NULL CONSTRAINT DF_VCL_Config_ClerkName DEFAULT N'آرتا',
    ChiefName          NVARCHAR(30)   NOT NULL CONSTRAINT DF_VCL_Config_ChiefName DEFAULT N'کاوه',
    UpdatedBy          NVARCHAR(50)   NULL,
    UpdatedAtUtc       DATETIME2      NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.VCL_Config)
    INSERT INTO dbo.VCL_Config (Id) VALUES (1);
GO

IF OBJECT_ID(N'dbo.VCL_Draft', N'U') IS NULL
CREATE TABLE dbo.VCL_Draft (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VCL_Draft PRIMARY KEY,
    Status        TINYINT        NOT NULL CONSTRAINT DF_VCL_Draft_Status DEFAULT 1,
                                               -- 1 تنظیم، 2 پرسش از درخواست‌کننده، 3 نزد رئیس، 4 منتظر تأیید،
                                               -- 5 ثبت شد، 6 رد شد، 7 خطا، 8 انصراف، 9 در حال ثبت، 10 کارمند ثبت نکرد
    RequesterCo   INT            NOT NULL,
    RequesterName NVARCHAR(50)   NOT NULL,
    Depatman      INT            NULL,         -- واحد و شیفتِ درخواست‌کننده: صندوقِ سطرِ «نقد» از این‌ها
    Shift         INT            NULL,
    RequestText   NVARCHAR(1000) NULL,         -- اولین پیام، برای فهرست
    Kind          TINYINT        NULL,         -- 1 سند دستی، 2 خزانه
    DocDate       BIGINT         NULL,
    Title         NVARCHAR(200)  NULL,
    Amount        FLOAT          NULL,
    Proposal      NVARCHAR(MAX)  NULL,         -- آخرین پیش‌نویسِ کارمند (JSON)
    Chief         NVARCHAR(MAX)  NULL,         -- آخرین حکمِ رئیس (JSON)
    Question      NVARCHAR(1000) NULL,         -- سؤالِ بازِ کارمند از درخواست‌کننده
    Revisions     TINYINT        NOT NULL CONSTRAINT DF_VCL_Draft_Revisions DEFAULT 0,
    ApproverCo    INT            NULL,
    DecidedBy     NVARCHAR(50)   NULL,
    DecidedAtUtc  DATETIME2      NULL,
    DecisionNote  NVARCHAR(400)  NULL,
    PostedNs      FLOAT          NULL,
    PostedTreasuryId INT         NULL,
    ModelCalls    INT            NOT NULL CONSTRAINT DF_VCL_Draft_Calls DEFAULT 0,
    Error         NVARCHAR(400)  NULL,
    CreatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_VCL_Draft_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_VCL_Draft_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VCL_Draft_Status')
    CREATE INDEX IX_VCL_Draft_Status ON dbo.VCL_Draft (Status, UpdatedAtUtc);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VCL_Draft_Requester')
    CREATE INDEX IX_VCL_Draft_Requester ON dbo.VCL_Draft (RequesterCo, CreatedAtUtc);
GO

IF OBJECT_ID(N'dbo.VCL_Event', N'U') IS NULL
CREATE TABLE dbo.VCL_Event (
    Id        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VCL_Event PRIMARY KEY,
    DraftId   INT            NOT NULL,
    AtUtc     DATETIME2      NOT NULL CONSTRAINT DF_VCL_Event_At DEFAULT SYSUTCDATETIME(),
    Actor     TINYINT        NOT NULL,         -- 1 درخواست‌کننده، 2 کارمند، 3 رئیس، 4 تأییدکننده، 5 سیستم
    ActorName NVARCHAR(50)   NULL,
    Kind      VARCHAR(12)    NOT NULL CONSTRAINT DF_VCL_Event_Kind DEFAULT 'info',
                                               -- message، question، heard، proposal، chief، return، decision، info
    Text      NVARCHAR(2000) NOT NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VCL_Event_Draft')
    CREATE INDEX IX_VCL_Event_Draft ON dbo.VCL_Event (DraftId, Id);
GO

IF OBJECT_ID(N'dbo.VCL_Attachment', N'U') IS NULL
CREATE TABLE dbo.VCL_Attachment (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VCL_Attachment PRIMARY KEY,
    DraftId      INT            NOT NULL,
    EventId      BIGINT         NULL,          -- پیامی از درخواست‌کننده که این پیوست همراهش آمد (VCL_Event)
    Kind         TINYINT        NOT NULL,      -- 1 عکس، 2 صدا
    MediaType    VARCHAR(40)    NOT NULL,
    Data         VARBINARY(MAX) NOT NULL,
    CreatedAtUtc DATETIME2      NOT NULL CONSTRAINT DF_VCL_Attachment_Created DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VCL_Attachment_Draft')
    CREATE INDEX IX_VCL_Attachment_Draft ON dbo.VCL_Attachment (DraftId);
GO

/* ── فرم در TFORMS ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'ACC_CLERK')
BEGIN
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'ACC_CLERK',
            N'کارمند حسابداری مجازی',
            3,
            ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'ACC_AUDIT'), 2),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
            GETDATE());
END
GO

/* ── ردیف دسترسی برای همه‌ی کاربران؛ RUN، SEE و UPD از رئیس حسابداری مجازی ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @ClerkFormId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'ACC_CLERK');
    DECLARE @AuditId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'ACC_AUDIT');

    IF @ClerkFormId IS NOT NULL
    BEGIN
        INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
        SELECT D.IDD, @ClerkFormId,
               ISNULL(scAudit.RUN, 0),
               ISNULL(scAudit.SEE, 0),
               0,
               ISNULL(scAudit.UPD, 0),
               0, GETDATE()
        FROM [dbo].[SALA_DTL] D
        LEFT JOIN [dbo].[SAL_CHEK] scAudit ON scAudit.USERCO = D.IDD AND scAudit.[OBJECT] = @AuditId
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[SAL_CHEK] E
            WHERE E.USERCO = D.IDD AND E.[OBJECT] = @ClerkFormId
        );
    END
END
GO

PRINT N'کارمند حسابداری مجازی: جدول‌ها و دسترسی آماده شد (خاموش).';
GO
";
    }
}
