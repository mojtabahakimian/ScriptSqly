using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// Safir: «دستیار هوش مصنوعی» شد «مشاور ارشد هوشمند»؛ نامِ سه فرمِ دسترسیِ آن (AI_ACCESS/AI_SETTINGS/AI_KNOWLEDGE) در TFORMS.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/62-ai-advisor-titles.sql در مخزن Safir یکی بماند
        /// (آزمونِ AccClerkTests.AdvisorTitles_migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AiAdvisorTitlesScript(SqlConnection db)
        {
            ExecuteBatches(db, AiAdvisorTitlesSql);
        }

        private const string AiAdvisorTitlesSql = @"/* ═══════════════════════════════════════════════════════════════════
   «دستیار هوش مصنوعی» در Safir «مشاور ارشد هوشمند» شد (نسخه‌ی ۳۳۲)؛ نامِ سه فرمِ دسترسیِ آن در TFORMS هم
   (همان که در «تعیین سطح دسترسی» دیده می‌شود). فقط CAPTION عوض می‌شود و فقط اگر هنوز همان نامِ قدیمی است،
   تا اگر کسی دستی نامِ دیگری گذاشته، دست نخورد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    UPDATE [dbo].[TFORMS] SET CAPTION = N'دسترسی کاربران به مشاور ارشد هوشمند'
    WHERE FORMNAME = N'AI_ACCESS' AND CAPTION = N'دسترسی کاربران به دستیار هوش مصنوعی';

    UPDATE [dbo].[TFORMS] SET CAPTION = N'تنظیمات سرویس مشاور ارشد هوشمند'
    WHERE FORMNAME = N'AI_SETTINGS' AND CAPTION = N'تنظیمات سرویس دستیار هوش مصنوعی';

    UPDATE [dbo].[TFORMS] SET CAPTION = N'دانش کسب‌وکار مشاور ارشد هوشمند'
    WHERE FORMNAME = N'AI_KNOWLEDGE' AND CAPTION = N'دانش کسب‌وکار دستیار هوش مصنوعی';
END;
GO
";
    }
}
