using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «سال‌های مالی» در Safir — فرمِ جدای دسترسی (FISCAL_YEARS) به‌جای فرمِ «تعیین سطح دسترسی».
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/51-fiscal-years-form.sql در مخزن Safir یکی بماند
        /// (آزمونِ FiscalArchiveTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void FiscalYearsFormScript(SqlConnection db)
        {
            ExecuteBatches(db, FiscalYearsFormSql);
        }

        private const string FiscalYearsFormSql = @"/* ═══════════════════════════════════════════════════════════════════
   «سال‌های مالی» — فرمِ جدای دسترسی (FORMNAME = FISCAL_YEARS)

   صفحه‌ی /admin/fiscal-years و تبِ «سال‌های مالی» ِ «تعیین سطح دسترسی» در Safir: کدام دیتابیس‌های
   سالِ قبل برای گزارش‌های چندساله‌ی هر کاربر باز باشد، و علامت زدنِ اسنادِ افتتاحیه/عملکرد/اختتامیه.
   SEE = دیدن، UPD = تغییر.

   تا امروز مجوزش همان فرمِ «تعیین سطح دسترسی» (F_USER_PERMITION FORMS) بود؛ برای اینکه هیچ‌کس
   چیزی را از دست ندهد، ردیفِ هر کاربر یک‌بار با همان سطحِ آن فرم ساخته می‌شود و بقیه بسته‌اند.
   ردیفِ موجود هرگز دوباره نوشته نمی‌شود، پس مجوزی که ادمین گرفته با اجرای دوباره برنمی‌گردد.
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/* ── ۱) تعریفِ فرم در TFORMS، در همان گروهِ «تعیین سطح دسترسی» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'FISCAL_YEARS')
BEGIN
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'FISCAL_YEARS',
            N'سال‌های مالی (گزارش چندساله)',
            3,
            ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS'), 0),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
            GETDATE());
END
GO

/* ── ۲) ردیفِ دسترسی برای همه‌ی کاربران: سطحِ «تعیین سطح دسترسی» ِ همان کاربر، وگرنه بسته ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @FiscalId INT = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'FISCAL_YEARS');
    DECLARE @PermiId  INT = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS');

    IF @FiscalId IS NOT NULL
        INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
        SELECT D.IDD, @FiscalId,
               ISNULL(P.RUN, 0), ISNULL(P.SEE, 0), ISNULL(P.INP, 0), ISNULL(P.UPD, 0), ISNULL(P.DEL, 0),
               GETDATE()
        FROM [dbo].[SALA_DTL] D
        OUTER APPLY (SELECT TOP 1 S.RUN, S.SEE, S.INP, S.UPD, S.DEL
                     FROM [dbo].[SAL_CHEK] S
                     WHERE S.USERCO = D.IDD AND S.[OBJECT] = @PermiId) P
        WHERE NOT EXISTS (
            SELECT 1 FROM [dbo].[SAL_CHEK] E
            WHERE E.USERCO = D.IDD AND E.[OBJECT] = @FiscalId
        );
END
GO

PRINT N'«سال‌های مالی»: فرمِ FISCAL_YEARS آماده شد.';
GO
";
    }
}
