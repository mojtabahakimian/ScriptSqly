using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// جمع‌داری اموال در Safir: استهلاک، خروج، تحویل‌گیرنده و جابه‌جایی — جدول‌های جدا کنارِ AMVAL ِ WPF.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/60-amval-assets.sql در مخزن Safir یکی بماند
        /// (آزمونِ AmvalTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AmvalAssetsScript(SqlConnection db)
        {
            ExecuteBatches(db, AmvalAssetsSql);
        }

        private const string AmvalAssetsSql = @"/* ═══════════════════════════════════════════════════════════════════
   جمع‌داری اموال در Safir: استهلاک، خروج (فروش / اسقاط)، تحویل‌گیرنده و جابه‌جایی

   جدولِ AMVAL ِ WPF دست نمی‌خورد؛ هر چه تازه است در جدول‌های جدا می‌آید تا فرمِ WIN_AMVAL ِ WPF
   (INSERT با ستون‌های صریح و SELECT *) همان‌طور کار کند.
   • AMVAL_EXT        — یک ردیف برای هر دارایی: استهلاکِ انباشته‌ی قبلی (OPEN_ACC تا پایانِ ماهِ OPEN_YM)،
                         تحویل‌گیرنده، و خروج (EXIT_*؛ سندش EXIT_DEED_BASE). با حذفِ دارایی پاک می‌شود.
   • AMVAL_DEPR_RUN   — هر بار محاسبه‌ی استهلاک برای ماه‌های FROM_YM تا TO_YM (yyyymm) و سندش (DEED_BASE).
   پیوندِ سند با DEED_HED.base است، نه N_S: WPF و گامِ S04 ِ بستنِ ماه سندها را بازشماره‌گذاری می‌کنند و
   شماره‌ی ذخیره‌شده می‌توانست به سندِ دیگری اشاره کند (همان دلیلِ Pay2DeedLink).
   • AMVAL_DEPR_LST   — سهمِ هر دارایی در آن محاسبه. کلیدِ خارجی به AMVAL دارد (بی cascade): دارایی‌ای که
                         استهلاک خورده در WPF هم حذف نمی‌شود («دارای اطلاعات وابسته است»).
   • AMVAL_MOVE       — سابقه‌ی جابه‌جایی بین محل‌ها و تحویل‌گیرنده‌ها. با حذفِ دارایی پاک می‌شود.
   • AMVAL_CONFIG     — حساب‌های سند: هزینه‌ی استهلاک، استهلاکِ انباشته، سود و زیانِ فروش.
   جدول‌هایی که به AMVAL کلیدِ خارجی دارند فقط وقتی ساخته می‌شوند که AMVAL باشد؛ دیتابیسِ بی AMVAL خطا نمی‌دهد.
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.AMVAL', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AMVAL_EXT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AMVAL_EXT (
        BARCHASB float NOT NULL CONSTRAINT PK_AMVAL_EXT PRIMARY KEY
            CONSTRAINT FK_AMVAL_EXT_AMVAL FOREIGN KEY REFERENCES dbo.AMVAL (barchasb) ON DELETE CASCADE,
        OPEN_ACC float NOT NULL CONSTRAINT DF_AMVAL_EXT_OPEN_ACC DEFAULT 0,
        OPEN_YM int NULL,
        RECEIVER nvarchar(100) NULL,
        EXIT_KIND tinyint NULL,
        EXIT_DATE bigint NULL,
        EXIT_PRICE float NULL,
        EXIT_HES nvarchar(50) NULL,
        EXIT_DEED_BASE int NULL,
        EXIT_PREV_STATUS int NULL,
        USER_NAME nvarchar(100) NULL,
        UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_AMVAL_EXT_CRT DEFAULT GETDATE()
    );
END;
GO

IF OBJECT_ID(N'dbo.AMVAL_DEPR_RUN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AMVAL_DEPR_RUN (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AMVAL_DEPR_RUN PRIMARY KEY,
        FROM_YM int NOT NULL,
        TO_YM int NOT NULL,
        DEED_BASE int NULL,
        SHARH nvarchar(250) NULL,
        USER_NAME nvarchar(100) NULL,
        UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_AMVAL_DEPR_RUN_CRT DEFAULT GETDATE()
    );
END;
GO

IF OBJECT_ID(N'dbo.AMVAL', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AMVAL_DEPR_LST', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AMVAL_DEPR_LST (
        RUN_ID int NOT NULL CONSTRAINT FK_AMVAL_DEPR_LST_RUN FOREIGN KEY REFERENCES dbo.AMVAL_DEPR_RUN (ID) ON DELETE CASCADE,
        BARCHASB float NOT NULL CONSTRAINT FK_AMVAL_DEPR_LST_AMVAL FOREIGN KEY REFERENCES dbo.AMVAL (barchasb),
        MONTHS int NOT NULL,
        BASE float NOT NULL,
        AMOUNT float NOT NULL,
        CONSTRAINT PK_AMVAL_DEPR_LST PRIMARY KEY (RUN_ID, BARCHASB)
    );
END;
GO

IF OBJECT_ID(N'dbo.AMVAL_DEPR_LST', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AMVAL_DEPR_LST_BARCHASB' AND object_id = OBJECT_ID(N'dbo.AMVAL_DEPR_LST'))
    CREATE INDEX IX_AMVAL_DEPR_LST_BARCHASB ON dbo.AMVAL_DEPR_LST (BARCHASB);
GO

IF OBJECT_ID(N'dbo.AMVAL', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AMVAL_MOVE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AMVAL_MOVE (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AMVAL_MOVE PRIMARY KEY,
        BARCHASB float NOT NULL CONSTRAINT FK_AMVAL_MOVE_AMVAL FOREIGN KEY REFERENCES dbo.AMVAL (barchasb) ON DELETE CASCADE,
        DATE bigint NOT NULL,
        FROM_PLACE nvarchar(255) NULL,
        TO_PLACE nvarchar(255) NULL,
        FROM_RECEIVER nvarchar(100) NULL,
        TO_RECEIVER nvarchar(100) NULL,
        DESCRIPTION nvarchar(255) NULL,
        USER_NAME nvarchar(100) NULL,
        UID int NULL,
        CRT datetime NOT NULL CONSTRAINT DF_AMVAL_MOVE_CRT DEFAULT GETDATE()
    );
END;
GO

IF OBJECT_ID(N'dbo.AMVAL_MOVE', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AMVAL_MOVE_BARCHASB' AND object_id = OBJECT_ID(N'dbo.AMVAL_MOVE'))
    CREATE INDEX IX_AMVAL_MOVE_BARCHASB ON dbo.AMVAL_MOVE (BARCHASB);
GO

IF OBJECT_ID(N'dbo.AMVAL_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AMVAL_CONFIG (
        [KEY] nvarchar(40) NOT NULL CONSTRAINT PK_AMVAL_CONFIG PRIMARY KEY,
        [VALUE] nvarchar(100) NULL
    );
END;
GO
";
    }
}
