using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «تعیین سطح دسترسی محدود» در سفیر: فرمِ جدای USER_ACCESS_LIMITED برای مدیری که دسترسیِ دیگران را می‌دهد ولی نه دسترسیِ
        /// خودش و نه این صفحه را. به هیچ‌کس داده نمی‌شود.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/77-user-access-limited.sql در مخزن Safir یکی بماند
        /// (آزمونِ UserAccessLimitedTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void UserAccessLimitedScript(SqlConnection db)
        {
            ExecuteBatches(db, UserAccessLimitedSql);
        }

        private const string UserAccessLimitedSql = @"/* ═══════════════════════════════════════════════════════════════════
   «تعیین سطح دسترسی محدود»: فرمِ جدای USER_ACCESS_LIMITED تا بشود «تعیین سطح دسترسی» را به کسی مثلِ مدیرِ مالی داد
   بی‌آن‌که دسترسیِ خودش را عوض کند یا این صفحه را به کسی بدهد که بعد دسترسیِ او را عوض کند.

   • فقط سفیر آن را می‌خواند (Server/Access/UserAccessService.cs): کسی که این را دارد و فرمِ «F_USER_PERMITION FORMS» را نه،
     همان صفحه را می‌بیند ولی به مدیرانِ دسترسی (از جمله خودش) دست نمی‌زند، فرم‌هایی را که دسترسی می‌دهند (دو فرمِ این صفحه،
     PAY2_ADMIN_ACL، AI_ACCESS و USERS) نه می‌دهد نه می‌گیرد، و ورودِ وب را بازنشانی نمی‌کند.
     SEE دیدنِ صفحه، INP افزودن، UPD اصلاح، DEL حذف — مثلِ فرمِ اصلی.
   • نرم‌افزارِ ویندوزی پنجره‌ی دسترسی را فقط با فرمِ اصلی باز می‌کند؛ پس این فرم آن‌جا راهی باز نمی‌کند.
   • به هیچ‌کس داده نمی‌شود: ردیفِ هر کاربر با همه‌ی مجوزها صفر ساخته می‌شود (تا در پنجره‌ی WPF هم دیده شود).
   هیچ جدولِ موجودی دست نمی‌خورد؛ idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'USER_ACCESS_LIMITED')
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS'), 0);
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'USER_ACCESS_LIMITED', N'تعیین سطح دسترسی محدود (فقط سفیر)', 3, @Grp,
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
    WHERE F.FORMNAME = N'USER_ACCESS_LIMITED'
      AND D.IDD IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END;
GO

PRINT N'تعیین سطح دسترسی محدود: فرمِ USER_ACCESS_LIMITED آماده شد.';
GO";
    }
}
