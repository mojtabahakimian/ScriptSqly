using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «دیدنِ کارِ دیگران» در Safir — فرم‌های مجازیِ MSGALL، AIALL و DSHALL (پیام‌ها، گفتگوی دستیار
        /// و داشبوردِ کاربرانِ دیگر؛ «چه کسی» را چارتِ سازمانی می‌گوید).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/54-oversight-forms.sql در مخزن Safir یکی بماند
        /// (آزمونِ OversightTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void OversightFormsScript(SqlConnection db)
        {
            ExecuteBatches(db, OversightFormsSql);
        }

        private const string OversightFormsSql = @"/* ═══════════════════════════════════════════════════════════════════
   «دیدنِ کارِ دیگران» در Safir — سه فرمِ مجازی (Shared/Constants/OversightForms.cs)

   MSGALL — مشاهده‌ی پیام‌های اتوماسیونِ کاربرانِ دیگر
   AIALL  — مشاهده‌ی گفتگوهای دستیارِ هوش مصنوعیِ کاربرانِ دیگر
   DSHALL — مشاهده‌ی داشبوردهای («داشبورد من») کاربرانِ دیگر

   هیچ‌کدام فرمِ واقعی نیست؛ کلیدِ روشن/خاموشِ هر کاربر است (مثلِ CRMALL و DSH_PUBLISH). «چه کسی» را
   چارتِ سازمانی (CHARTSAZMANI) می‌گوید، مثلِ کارتابل: زیرمجموعه‌ها، یا همه با ردیفِ SUBUSERCO = 0.
   پیش‌فرض برای همه بسته است؛ از فرمِ تعیینِ سطحِ دسترسیِ نرم‌افزارِ ویندوزی داده می‌شود.
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/* ── ۱) تعریفِ فرم‌ها در TFORMS، در همان گروهِ «نبض سازمان» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'PULSE'), 2);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'MSGALL')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'MSGALL', N'مشاهده پیام‌های کاربران دیگر', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'AIALL')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'AIALL', N'مشاهده گفتگوی هوش مصنوعی کاربران دیگر', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'DSHALL')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'DSHALL', N'مشاهده داشبورد کاربران دیگر', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END
GO

/* ── ۲) ردیفِ دسترسیِ بسته برای همه‌ی کاربران تا در فرمِ سطحِ دسترسی دیده و باز شود ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH, 0, 0, 0, 0, 0, GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    WHERE F.FORMNAME IN (N'MSGALL', N'AIALL', N'DSHALL')
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END
GO

PRINT N'«دیدنِ کارِ دیگران»: فرم‌های MSGALL، AIALL و DSHALL آماده شد.';
GO
";
    }
}
