using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «مرخصی من»: تنظیمِ LEAVE_REQUIRES_APPROVAL؛ ردیفِ موجود دست نمی‌خورد.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/76-leave-requires-approval.sql در مخزن Safir یکی بماند
        /// (آزمونِ Pay2MyLeaveRulesTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد).
        /// </summary>
        private static void LeaveRequiresApprovalScript(SqlConnection db)
        {
            ExecuteBatches(db, LeaveRequiresApprovalSql);
        }

        private const string LeaveRequiresApprovalSql = @"/* ═══════════════════════════════════════════════════════════════════
   «مرخصی من»: تنظیمِ LEAVE_REQUIRES_APPROVAL در PAY2_CONFIG (بخشِ مرخصی).

   • 1 (پیش‌فرض، همان رفتارِ قبلی): ثبتِ کارمند «ثبت اولیه» است و منتظرِ تأیید می‌ماند.
   • 0: ثبتِ کارمند همان لحظه نهایی است (STATUS = 4)؛ مدیر فقط خبردار می‌شود و کارمند خودش لغو نمی‌کند.
   • تا این ردیف نباشد، برنامه مثلِ 1 رفتار می‌کند. idempotent است و مقدارِ موجود را عوض نمی‌کند.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[PAY2_CONFIG]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[PAY2_CONFIG] WHERE [CFG_KEY] = N'LEAVE_REQUIRES_APPROVAL')
    INSERT INTO [dbo].[PAY2_CONFIG] ([CFG_KEY], [CFG_VALUE], [CFG_OPTIONS], [CFG_DEFAULT], [CFG_SECTION], [LABEL_FA], [DESC_FA], [OPT_LABELS], [DATA_TYPE], [ACCESS_LEVEL])
    VALUES (N'LEAVE_REQUIRES_APPROVAL', N'1', N'0|1', N'1', N'مرخصی', N'مرخصی کارمند نیاز به تأیید دارد',
            N'«بله»: ثبتِ کارمند در «مرخصی من» منتظرِ تأیید می‌ماند. «خیر»: همان ثبت نهایی است، مدیر فقط خبردار می‌شود و کارمند خودش نمی‌تواند لغو کند.',
            N'خیر|بله', N'BOOL', 2);
GO

PRINT N'مرخصی من: تنظیمِ نیاز به تأیید آماده شد.';
GO";
    }
}
