using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// مغایرت‌گیریِ بانک در Safir: صورتحساب‌های واردشده (BANK_STMT، BANK_STMT_LINE)، قالبِ فایلِ هر حساب (BANK_REC_ACCOUNT)
        /// و تطبیقِ ردیف‌های بانک با دفاتر (BANK_REC_MATCH، BANK_REC_MATCH_BANK، BANK_REC_MATCH_BOOK)
        /// و قطعی کردنِ مغایرت‌گیری تا یک تاریخ (BANK_REC_CLOSE).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/64-bank-rec.sql در مخزن Safir یکی بماند
        /// (آزمونِ BankRecTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void BankRecScript(SqlConnection db)
        {
            ExecuteBatches(db, BankRecSql);
        }

        private const string BankRecSql = @"/* ═══════════════════════════════════════════════════════════════════
   مغایرت‌گیریِ بانک در Safir

   • BANK_REC_ACCOUNT — تنظیمِ هر حسابِ بانکی؛ PROFILE قالبِ فایلِ صورتحسابِ همان بانک است (JSON: ستون‌ها و سطرِ عنوان) تا
                        فایلِ بعدی بی‌تعیینِ دوباره خوانده شود.
   • BANK_STMT        — هر فایلِ صورتحسابی که وارد شده (بازه، مانده‌ی اول و آخر، نامِ فایل، کاربر).
   • BANK_STMT_LINE   — ردیف‌های صورتحساب. AMOUNT مثبت = واریز به حساب، منفی = برداشت (از دیدِ شرکت، همان «بدهکار − بستانکار»
                        ِ دفاتر؛ در صورتحسابِ بانک «بدهکار» یعنی برداشت). KEY_ (SHA1 ِ تاریخ، ساعت، مبلغ، مانده، پیگیری، …)
                        با UX_BANK_STMT_LINE_KEY جلوی ردیفِ تکراری را می‌گیرد: همان فایل دوباره، یا دو صورتحسابِ هم‌پوشان.
   • BANK_REC_MATCH، BANK_REC_MATCH_BANK، BANK_REC_MATCH_BOOK — تطبیق‌ها: چند ردیفِ بانک با چند ردیفِ دفتر (DEED_DTL ِ همان
                        حساب). هر ردیفِ بانک فقط در یک تطبیق (UX_BANK_REC_MATCH_BANK_LINE). ردیفِ دفتر با N_S، مبلغ و شرح نگه داشته
                        می‌شود و DTL_ID فقط نشانی است، چون «صدور سند» ِ خزانه ردیف‌های DEED_DTL را پاک و دوباره درج می‌کند.
                        KIND: ۱ دستی، ۲ خودکار، ۳ قاعده، ۴ قلمِ شروعِ دوره.
   • BANK_REC_CLOSE   — قطعی کردنِ مغایرت‌گیری تا AS_OF: SNAPSHOT (JSON) عکسِ همان لحظه است — صورت مغایرت، ردیف‌های دفترِ تا AS_OF و
                        تعداد و جمعِ ردیف‌های بانک — تا هر تغییرِ بعدی با تاریخِ تا AS_OF هشدار بگیرد. قفل نمی‌کند.
   جدول‌های MOGHHEAD و MO_DTL ِ WPF («مغایرت‌گیری یک حساب») دست نمی‌خورند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.BANK_REC_ACCOUNT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_REC_ACCOUNT (
        HES nvarchar(60) NOT NULL CONSTRAINT PK_BANK_REC_ACCOUNT PRIMARY KEY,
        PROFILE nvarchar(max) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_BANK_REC_ACCOUNT_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.BANK_STMT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_STMT (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_BANK_STMT PRIMARY KEY,
        HES nvarchar(60) NOT NULL,
        FROM_DATE bigint NOT NULL,
        TO_DATE bigint NOT NULL,
        OPEN_BAL float NULL,
        CLOSE_BAL float NULL,
        ROWS_ int NOT NULL,
        FILE_NAME nvarchar(260) NULL,
        FILE_HASH char(64) NULL,
        USER_NAME nvarchar(100) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_BANK_STMT_CRT DEFAULT GETDATE(),
        UID int NULL
    );
    CREATE INDEX IX_BANK_STMT_HES ON dbo.BANK_STMT (HES, FROM_DATE);
END;
GO

IF OBJECT_ID(N'dbo.BANK_STMT_LINE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_STMT_LINE (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_BANK_STMT_LINE PRIMARY KEY,
        STMT_ID int NOT NULL CONSTRAINT FK_BANK_STMT_LINE_STMT REFERENCES dbo.BANK_STMT (ID) ON DELETE CASCADE,
        HES nvarchar(60) NOT NULL,
        SEQ int NOT NULL,
        DATE_ bigint NOT NULL,
        TIME_ nvarchar(8) NULL,
        SHARH nvarchar(500) NULL,
        REF_NO nvarchar(100) NULL,
        CHEQUE_NO nvarchar(100) NULL,
        BRANCH nvarchar(100) NULL,
        AMOUNT float NOT NULL,
        BALANCE float NULL,
        KEY_ char(40) NOT NULL
    );
    CREATE UNIQUE INDEX UX_BANK_STMT_LINE_KEY ON dbo.BANK_STMT_LINE (HES, KEY_);
    CREATE INDEX IX_BANK_STMT_LINE_DATE ON dbo.BANK_STMT_LINE (HES, DATE_) INCLUDE (AMOUNT, BALANCE);
END;
GO

IF OBJECT_ID(N'dbo.BANK_REC_MATCH', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_REC_MATCH (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_BANK_REC_MATCH PRIMARY KEY,
        HES nvarchar(60) NOT NULL,
        KIND tinyint NOT NULL,
        NOTE nvarchar(200) NULL,
        USER_NAME nvarchar(100) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_BANK_REC_MATCH_CRT DEFAULT GETDATE(),
        UID int NULL
    );
    CREATE INDEX IX_BANK_REC_MATCH_HES ON dbo.BANK_REC_MATCH (HES);
END;
GO

IF OBJECT_ID(N'dbo.BANK_REC_MATCH_BANK', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_REC_MATCH_BANK (
        MATCH_ID int NOT NULL CONSTRAINT FK_BANK_REC_MATCH_BANK_MATCH REFERENCES dbo.BANK_REC_MATCH (ID) ON DELETE CASCADE,
        LINE_ID int NOT NULL CONSTRAINT FK_BANK_REC_MATCH_BANK_LINE REFERENCES dbo.BANK_STMT_LINE (ID),
        CONSTRAINT PK_BANK_REC_MATCH_BANK PRIMARY KEY (MATCH_ID, LINE_ID)
    );
    CREATE UNIQUE INDEX UX_BANK_REC_MATCH_BANK_LINE ON dbo.BANK_REC_MATCH_BANK (LINE_ID);
END;
GO

IF OBJECT_ID(N'dbo.BANK_REC_MATCH_BOOK', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_REC_MATCH_BOOK (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_BANK_REC_MATCH_BOOK PRIMARY KEY,
        MATCH_ID int NOT NULL CONSTRAINT FK_BANK_REC_MATCH_BOOK_MATCH REFERENCES dbo.BANK_REC_MATCH (ID) ON DELETE CASCADE,
        N_S float NOT NULL,
        DTL_ID bigint NULL,
        DATE_S bigint NOT NULL,
        AMOUNT float NOT NULL,
        SHARH nvarchar(500) NULL,
        N_SERI float NULL
    );
    CREATE INDEX IX_BANK_REC_MATCH_BOOK_MATCH ON dbo.BANK_REC_MATCH_BOOK (MATCH_ID);
END;
GO

IF OBJECT_ID(N'dbo.BANK_REC_CLOSE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BANK_REC_CLOSE (
        ID int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_BANK_REC_CLOSE PRIMARY KEY,
        HES nvarchar(60) NOT NULL,
        AS_OF bigint NOT NULL,
        BANK_BAL float NOT NULL,
        BOOK_BAL float NOT NULL,
        SNAPSHOT nvarchar(max) NOT NULL,
        USER_NAME nvarchar(100) NULL,
        CRT datetime NOT NULL CONSTRAINT DF_BANK_REC_CLOSE_CRT DEFAULT GETDATE(),
        UID int NULL
    );
    CREATE INDEX IX_BANK_REC_CLOSE_HES ON dbo.BANK_REC_CLOSE (HES, AS_OF);
END;
GO
";
    }
}
