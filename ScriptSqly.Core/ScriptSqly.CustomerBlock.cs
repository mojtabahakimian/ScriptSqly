using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// مسدودی حساب مشتریان در سفیر: فرمِ دسترسیِ جدای CUSTOMER_BLOCK برای لیست سیاه (BLOCK_CUSTOMER).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/71-customer-block.sql در مخزن Safir یکی بماند
        /// (آزمونِ CustomerBlockTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void CustomerBlockScript(SqlConnection db)
        {
            ExecuteBatches(db, CustomerBlockSql);
        }

        private const string CustomerBlockSql = @"/* ═══════════════════════════════════════════════════════════════════
   مسدودی حساب مشتریان: فرمِ دسترسیِ جدای «لیست سیاه» (BLOCK_CUSTOMER) تا بشود این کار را به کاربرانِ مالی داد
   بی‌آن‌که «تعیین سطح دسترسی» ِ همه‌ی کاربران را هم به آن‌ها داد.

   • فرمِ CUSTOMER_BLOCK در TFORMS (صفحه‌ی «مسدودی حساب مشتریان» و تبِ «لیست سیاه» در سفیر):
     SEE دیدنِ فهرست، INP مسدود کردنِ مشتریِ تازه، UPD اصلاح و پایانِ مسدودی.
   • ردیفِ هر کاربر یک‌بار با سطحِ فرمِ «F_USER_PERMITION FORMS» ساخته می‌شود؛ پس هر که تا امروز لیست سیاه
     را می‌دید، باز هم می‌بیند و بقیه تا وقتی مدیر به آن‌ها ندهد، نه.
   هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'CUSTOMER_BLOCK')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS'), 0);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'CUSTOMER_BLOCK', N'مسدودی حساب مشتریان (لیست سیاه)', 3, @Grp,
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminId INT = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS');

    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH,
           ISNULL(P.RUN, 0), ISNULL(P.SEE, 0), ISNULL(P.INP, 0), ISNULL(P.UPD, 0), ISNULL(P.DEL, 0),
           GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    OUTER APPLY (SELECT TOP 1 S.RUN, S.SEE, S.INP, S.UPD, S.DEL
                 FROM [dbo].[SAL_CHEK] S
                 WHERE S.USERCO = D.IDD AND S.[OBJECT] = @AdminId) P
    WHERE F.FORMNAME = N'CUSTOMER_BLOCK'
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'مسدودی حساب مشتریان: فرمِ CUSTOMER_BLOCK آماده شد.';
GO";
    }
}
