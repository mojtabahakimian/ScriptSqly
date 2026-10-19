using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «داشبورد من» در Safir — جدولِ DSH_Board برای داشبوردهای شخصیِ هر کاربر.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/49-dashboards.sql در مخزن Safir یکی بماند
        /// (آزمون DashboardMigrationTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void DashboardScript(SqlConnection db)
        {
            ExecuteBatches(db, DashboardSql);
            ExecuteBatches(db, DashboardPublishSql);
        }

        private const string DashboardSql = @"/* ═══════════════════════════════════════════════════════════════════
   «داشبورد من» در Safir — داشبوردهای شخصیِ هر کاربر (Server/Dashboard).

   DSH_Board — هر ردیف یک داشبورد از یک کاربر. چیدمانِ کارت‌ها در LayoutJson
               است (آرایه‌ی JSON: نوعِ کارت، جا و اندازه روی شبکه‌ی ۱۲ ستونه،
               تنظیماتِ کارت). داده‌ی کارت‌ها اینجا ذخیره نمی‌شود؛ هر بار از
               همان منطقِ صفحه‌های Safir خوانده می‌شود. Version برای همزمانی:
               ذخیره با نسخه‌ی کهنه رد می‌شود.

   نرم‌افزار ویندوزی این جدول را نمی‌خواند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.DSH_Board', N'U') IS NULL
CREATE TABLE dbo.DSH_Board (
    BoardId       INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DSH_Board PRIMARY KEY,
    UserCo        INT            NOT NULL,     -- SALA_DTL.IDD ِ صاحبِ داشبورد
    Title         NVARCHAR(60)   NOT NULL,
    Icon          NVARCHAR(40)   NULL,         -- نامِ آیکونِ Bootstrap Icons
    SortOrder     INT            NOT NULL CONSTRAINT DF_DSH_Board_Sort DEFAULT 0,
    IsHome        BIT            NOT NULL CONSTRAINT DF_DSH_Board_Home DEFAULT 0,   -- در صفحه‌ی اصلی نشان داده شود
    Period        NVARCHAR(10)   NOT NULL CONSTRAINT DF_DSH_Board_Period DEFAULT N'30',
    RefreshSec    INT            NOT NULL CONSTRAINT DF_DSH_Board_Refresh DEFAULT 0, -- ۰ = تازه‌سازیِ خودکار خاموش
    LayoutJson    NVARCHAR(MAX)  NOT NULL,
    Version       INT            NOT NULL CONSTRAINT DF_DSH_Board_Version DEFAULT 1,
    CreatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_DSH_Board_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_DSH_Board_Updated DEFAULT SYSUTCDATETIME()
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DSH_Board_User' AND object_id = OBJECT_ID(N'dbo.DSH_Board'))
    CREATE INDEX IX_DSH_Board_User ON dbo.DSH_Board (UserCo, SortOrder);
GO

PRINT N'«داشبورد من»: جدولِ DSH_Board آماده شد.';
GO
";

        /// <summary>
        /// دسترسیِ انتشارِ قالبِ داشبورد (DSH_PUBLISH). مو‌به‌مو با Server/Database/50-dashboard-publish.sql
        /// در مخزنِ Safir (آزمونِ DashboardTests همین را می‌سنجد).
        /// </summary>
        private const string DashboardPublishSql = @"/* ═══════════════════════════════════════════════════════════════════
   «داشبورد من» — دسترسیِ انتشارِ قالب برای همه (FORMNAME = DSH_PUBLISH)

   کاربری که RUN ِ این فرم را دارد می‌تواند یکی از داشبوردهای خودش را به‌عنوانِ «قالبِ سازمان»
   منتشر کند یا قالبِ منتشرشده را بردارد. قالب‌ها در همان DSH_Board با UserCo = 0 ذخیره می‌شوند
   (جدولِ تازه لازم نیست). پیش‌فرض برای همه بسته است؛ از فرمِ تعیینِ سطحِ دسترسیِ نرم‌افزارِ ویندوزی
   (با جستجوی «داشبورد») داده می‌شود. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/* ── ۱) تعریفِ فرم در TFORMS، در همان گروهِ «نبض سازمان» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'DSH_PUBLISH')
BEGIN
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'DSH_PUBLISH',
            N'انتشار قالب داشبورد برای همه',
            3,
            ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'PULSE'), 2),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
            GETDATE());
END
GO

/* ── ۲) ردیفِ دسترسیِ بسته برای همه‌ی کاربران تا در فرمِ سطحِ دسترسی دیده و باز شود ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @PublishId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'DSH_PUBLISH');

    IF @PublishId IS NOT NULL
        INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
        SELECT D.IDD, @PublishId, 0, 0, 0, 0, 0, GETDATE()
        FROM [dbo].[SALA_DTL] D
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[SAL_CHEK] E
            WHERE E.USERCO = D.IDD AND E.[OBJECT] = @PublishId
        );
END
GO

PRINT N'«داشبورد من»: دسترسیِ DSH_PUBLISH آماده شد.';
GO
";
    }
}
