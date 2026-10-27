using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// صفحه‌های مدیریتیِ Safir — فرمِ جدای دسترسی برای هر کدام (DB_UPGRADE، AI_ACCESS، AI_SETTINGS، AI_KNOWLEDGE)
        /// به‌جای «مدیریت دسترسی‌های حقوق و دستمزد» (PAY2_ADMIN_ACL)؛ سطحِ همان فرم برای هر کاربر کپی می‌شود.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/57-admin-forms.sql در مخزن Safir یکی بماند
        /// (آزمونِ AdminFormsTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AdminFormsScript(SqlConnection db)
        {
            ExecuteBatches(db, AdminFormsSql);
        }

        private const string AdminFormsSql = @"/* ═══════════════════════════════════════════════════════════════════
   صفحه‌های مدیریتیِ Safir — هر کدام فرمِ جدای دسترسی (Shared/Constants/AdminForms.cs)

   DB_UPGRADE   — به‌روزرسانی دیتابیس (/admin/db-upgrade): SEE دیدن، RUN اجرای اسکریپت‌ها
   AI_ACCESS    — دسترسی کاربران به دستیار هوش مصنوعی (/ai/access): SEE دیدن و لاگ، UPD تغییر، DEL حذف
   AI_SETTINGS  — تنظیمات سرویس دستیار هوش مصنوعی (/ai/settings): SEE دیدن و فایلِ تشخیص، UPD ذخیره
   AI_KNOWLEDGE — دانش کسب‌وکار دستیار هوش مصنوعی (/ai/knowledge): SEE دیدن، UPD ذخیره، DEL حذف

   تا امروز هر چهار صفحه زیرِ «مدیریت دسترسی‌های حقوق و دستمزد» (PAY2_ADMIN_ACL) بودند؛ برای اینکه
   هیچ‌کس چیزی را از دست ندهد، ردیفِ هر کاربر یک‌بار با همان سطحِ آن فرم ساخته می‌شود و بقیه بسته‌اند.
   ردیفِ موجود هرگز دوباره نوشته نمی‌شود، پس مجوزی که ادمین گرفته با اجرای دوباره برنمی‌گردد.
   تا وقتی این اسکریپت اجرا نشده، Safir همان PAY2_ADMIN_ACL را می‌سنجد (Pay2Authorize.Fallback).
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

/* ── ۱) تعریفِ فرم‌ها در TFORMS، در همان گروهِ «تعیین سطح دسترسی» ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    DECLARE @Grp INT = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'F_USER_PERMITION FORMS'), 0);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'DB_UPGRADE')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'DB_UPGRADE', N'به‌روزرسانی دیتابیس', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'AI_ACCESS')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'AI_ACCESS', N'دسترسی کاربران به دستیار هوش مصنوعی', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'AI_SETTINGS')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'AI_SETTINGS', N'تنظیمات سرویس دستیار هوش مصنوعی', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

    IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'AI_KNOWLEDGE')
        INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
        VALUES (N'AI_KNOWLEDGE', N'دانش کسب‌وکار دستیار هوش مصنوعی', 3, @Grp,
                (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());
END
GO

/* ── ۲) ردیفِ دسترسی برای همه‌ی کاربران: سطحِ «مدیریت دسترسی‌های حقوق و دستمزد» ِ همان کاربر، وگرنه بسته ── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminId INT = (SELECT TOP 1 IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'PAY2_ADMIN_ACL');

    INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
    SELECT D.IDD, F.IDH,
           ISNULL(P.RUN, 0), ISNULL(P.SEE, 0), ISNULL(P.INP, 0), ISNULL(P.UPD, 0), ISNULL(P.DEL, 0),
           GETDATE()
    FROM [dbo].[SALA_DTL] D
    CROSS JOIN [dbo].[TFORMS] F
    OUTER APPLY (SELECT TOP 1 S.RUN, S.SEE, S.INP, S.UPD, S.DEL
                 FROM [dbo].[SAL_CHEK] S
                 WHERE S.USERCO = D.IDD AND S.[OBJECT] = @AdminId) P
    WHERE F.FORMNAME IN (N'DB_UPGRADE', N'AI_ACCESS', N'AI_SETTINGS', N'AI_KNOWLEDGE')
      AND NOT EXISTS (
          SELECT 1 FROM [dbo].[SAL_CHEK] E
          WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
      );
END
GO

PRINT N'صفحه‌های مدیریتی: فرم‌های DB_UPGRADE، AI_ACCESS، AI_SETTINGS و AI_KNOWLEDGE آماده شد.';
GO
";
    }
}
