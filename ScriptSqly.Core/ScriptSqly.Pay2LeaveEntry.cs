using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ثبت مرخصی پرسنل در سفیر: فرمِ PAY2_LEAVE_ENTRY (کارگزینی بی دیدنِ حقوق و حکم برای دیگران مرخصی ثبت می‌کند).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/95-pay2-leave-entry.sql در مخزن Safir یکی بماند
        /// (آزمونِ StaffLeaveTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void Pay2LeaveEntryScript(SqlConnection db)
        {
            ExecuteBatches(db, Pay2LeaveEntrySql);
        }

        private const string Pay2LeaveEntrySql = @"/* ═══════════════════════════════════════════════════════════════════
   ثبت مرخصی پرسنل (فقط سفیر): کارگزینی برای هر پرسنل مرخصی ثبت می‌کند بی آن‌که حقوق، حکم یا پرونده‌ی پرسنلی را ببیند.

   • فرمِ PAY2_LEAVE_ENTRY: «مشاهده» = فهرستِ پرسنل (کد، نام، مانده‌ی مرخصی) و مرخصی‌هایشان، «ورود» = ثبت، «حذف» = حذفِ
     مرخصی‌ای که خودِ همین مسیر ثبت کرده. جدا از ACL_ENFORCE تیکِ صریح می‌خواهد؛ به هیچ‌کس داده نمی‌شود: ردیفِ هر کاربر با
     همه‌ی مجوزها صفر ساخته می‌شود.
   هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'PAY2_LEAVE_ENTRY')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'PAY2_EMPLOYEE'), 9);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'PAY2_LEAVE_ENTRY', N'ثبت مرخصی پرسنل (فقط سفیر)', 3, @Grp,
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH, 0, 0, 0, 0, 0, GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    WHERE F.FORMNAME = N'PAY2_LEAVE_ENTRY'
      AND D.IDD IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'ثبت مرخصی پرسنل: فرم PAY2_LEAVE_ENTRY آماده شد.';
GO";
    }
}
