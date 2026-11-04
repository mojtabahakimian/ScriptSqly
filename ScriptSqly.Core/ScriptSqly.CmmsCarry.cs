using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// نگهداری و تعمیرات (CMMS) در سفیر — فازِ دو: ستونِ CMMS_WO.PREV_MAT (ارزشِ موادِ سال‌های قبل برای دستور کارِ منتقل‌شده به سالِ
        /// تازه) و کلیدهای صدورِ خودکار در CMMS_CONFIG.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/74-cmms-carry.sql در مخزن Safir یکی بماند
        /// (آزمونِ CmmsTests.Carry_migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void CmmsCarryScript(SqlConnection db)
        {
            ExecuteBatches(db, CmmsCarrySql);
        }

        private const string CmmsCarrySql = @"/* ═══════════════════════════════════════════════════════════════════
   نگهداری و تعمیرات (CMMS) — فازِ دو. راهنما: docs/guides/cmms.md

   • CMMS_WO.PREV_MAT — ارزشِ «برگه خروج سایر مواد» ِ سال‌های قبل برای دستور کاری که با «انتقال به سالِ مالیِ تازه» آمده است.
     برگه‌های انبار در هر سال شماره‌ی خودشان را دارند و پیوندشان (CMMS_WO_DOC) منتقل نمی‌شود؛ ارزششان هنگامِ انتقال این‌جا
     می‌ماند تا هزینه‌ی دستور کارِ دوساله (مثلاً تعمیراتِ تعطیلاتِ نوروز) کامل بماند.
   • کلیدهای تازه‌ی CMMS_CONFIG با پیش‌فرض: صدورِ خودکار خاموش (AUTO_GENERATE)، ساعتِ صدور ۶ صبح (GEN_HOUR) و یادآوری به
     مجری در اتوماسیون خاموش (REMIND) — دستور کارهای کهنه‌ی آزمایشی (مثلِ ۱۴۰۱) نباید از روزِ اول هر روز پیام بسازند.
   idempotent است؛ جدول‌ها و داده‌ی Access (PM_*) دست نمی‌خورند.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.CMMS_WO', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.CMMS_WO', N'PREV_MAT') IS NULL
    ALTER TABLE dbo.CMMS_WO ADD PREV_MAT float NULL;
GO

IF OBJECT_ID(N'dbo.CMMS_CONFIG', N'U') IS NOT NULL
    INSERT INTO dbo.CMMS_CONFIG ([KEY], [VALUE])
    SELECT v.K, v.V
    FROM (VALUES
            (N'AUTO_GENERATE', N'0'),
            (N'GEN_HOUR', N'6'),
            (N'REMIND', N'0')
         ) v (K, V)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.CMMS_CONFIG c WHERE c.[KEY] = v.K);
GO

PRINT N'نگهداری و تعمیرات (CMMS) — فازِ دو: ستونِ PREV_MAT و کلیدهای صدورِ خودکار آماده شد.';
GO";
    }
}
