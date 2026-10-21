using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ایجنت رئیس حسابداری مجازی در Safir — «درست است» روی مانده‌های برخلافِ همیشه: جدولِ AGT_AccountAck.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/53-acc-agent-account-ack.sql در مخزن Safir یکی بماند
        /// (آزمونِ AccAgentAccountAckTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AccAgentAccountAckScript(SqlConnection db)
        {
            ExecuteBatches(db, AccAgentAccountAckSql);
        }

        private const string AccAgentAccountAckSql = @"/* ═══════════════════════════════════════════════════════════════════
   ایجنت «رئیس حسابداری مجازی» — «درست است» روی صفحه‌ی «حساب‌ها و اشخاص»
   (Server/Agent/AgentAccounts.cs). بعد از اسکریپت ۴۶.

   AGT_AccountAck — حسابی که مانده‌اش برخلافِ همیشه است و یک کاربر گفته «درست است، نشانش نده»
                    (مثلاً پیش‌پرداختِ عمدی). یک ردیف برای هر حساب. فقط همان دوره را می‌پوشاند:
                    Expected و Since همان مانده؛ اگر مانده برگردد و دوباره برخلاف شود، یا بیش از
                    دو برابرِ Balance شود، حساب دوباره در فهرست و گزارشِ روزانه می‌آید.

   idempotent است؛ چند بار اجرا شدنش ضرری ندارد.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.AGT_AccountAck', N'U') IS NULL
CREATE TABLE dbo.AGT_AccountAck (
    Hes           NVARCHAR(80)   NOT NULL CONSTRAINT PK_AGT_AccountAck PRIMARY KEY,
    Expected      NVARCHAR(10)   NOT NULL,     -- طرفِ مورد انتظار: بدهکار یا بستانکار
    Since         BIGINT         NOT NULL,     -- روزِ شروعِ همین دوره‌ی مانده (شمسی yyyymmdd)
    Balance       FLOAT          NOT NULL,     -- مانده هنگامِ تأیید (مثبت = بدهکار)
    Note          NVARCHAR(500)  NULL,
    CreatedBy     NVARCHAR(50)   NOT NULL,
    CreatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_AGT_AccountAck_Created DEFAULT SYSUTCDATETIME()
);
GO

PRINT N'ایجنت رئیس حسابداری مجازی: «درست است» برای حساب‌ها و اشخاص آماده شد.';
GO";
    }
}
