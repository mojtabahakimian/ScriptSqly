using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// Safir/MrCorrect: از رده خارج کردنِ مسترکارکت — فرمِ مجازیِ WPFRUN «اجرای مسترکارکت بعد از بسته شدن» در TFORMS.
        /// کلیدِ روشن/خاموش (GENERAL_OPTIONS.WPF_RETIRED) را خودِ سفیر می‌نویسد؛ اینجا فقط ردیفِ فرم ساخته می‌شود.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/65-wpf-retire.sql در مخزن Safir یکی بماند
        /// (آزمونِ WpfRetireTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void WpfRetireScript(SqlConnection db)
        {
            ExecuteBatches(db, WpfRetireSql);
        }

        private const string WpfRetireSql = @"/* ═══════════════════════════════════════════════════════════════════
   از رده خارج کردنِ مسترکارکت (نرم‌افزارِ ویندوزی) — فرمِ مجازیِ WPFRUN «اجرای مسترکارکت».
   یک فرمِ واقعی نیست؛ کلیدِ per-user است، همان الگوی CUSTEN و CRMALL. وقتی در «مشخصات سیستم ← تنظیمات بیشتر»
   ِ سفیر «مسترکارکت بسته شود» (GENERAL_OPTIONS.WPF_RETIRED = '1') روشن است، فقط کاربرانی که RUN این فرم را دارند
   هنوز وارد مسترکارکت می‌شوند و بقیه به سفیر فرستاده می‌شوند. پیش‌فرض به هیچ‌کس داده نمی‌شود؛ تا کلید خاموش است
   هیچ رفتاری عوض نمی‌شود. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'WPFRUN')
BEGIN
    INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'WPFRUN',
            N'اجرای مسترکارکت بعد از بسته شدن',
            3,
            ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'SAZMAN'), 1),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
            GETDATE());
END;
GO
";
    }
}
