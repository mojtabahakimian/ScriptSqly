using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// حساب‌های خودگردانِ «مدرن»: سوئیچِ SAZMAN.AUTO_MODERN و جدولِ AUTO_HES (کل، کل-معین یا کل-معین-تفصیلی).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/56-auto-accounts-modern.sql در مخزن Safir یکی بماند
        /// (آزمونِ AutoAccountsTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void AutoAccountsModernScript(SqlConnection db)
        {
            ExecuteBatches(db, AutoAccountsModernSql);
        }

        private const string AutoAccountsModernSql = @"/* ═══════════════════════════════════════════════════════════════════
   حساب‌های خودگردانِ «مدرن» — کل، کل-معین یا کل-معین-تفصیلی به‌جای فقط کدِ کل

   SAZMAN.AUTO_MODERN — سوئیچِ «مشخصات سیستم»: NULL/0 = کلاسیک (همان کدهای کلِ SAZMAN)، 1 = مدرن.
   AUTO_HES — حسابِ مدرنِ هر ستونِ کلِ SAZMAN (COL = نامِ ستون، مثلاً FROSH). سطح‌های پُر ثابت‌اند و سطح‌های خالی را
   سندزن مثلِ حالتِ کلاسیک خودش می‌سازد (مثلاً FROSH = 511-1 → 511-1-کدِکالا). ستونی که این‌جا ردیف ندارد، در حالتِ
   مدرن هم از SAZMAN خوانده می‌شود. کدهای کلاسیکِ SAZMAN دست نمی‌خورند، پس برگشت به کلاسیک بی‌خطر است.
   Safir و MrCorrect (WPF و AUTO_BAZ) هر دو همین را می‌خوانند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'[dbo].[SAZMAN]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SAZMAN', N'AUTO_MODERN') IS NULL
    ALTER TABLE [dbo].[SAZMAN] ADD [AUTO_MODERN] BIT NULL;
GO

IF OBJECT_ID(N'[dbo].[TR_SAZMAN]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TR_SAZMAN', N'AUTO_MODERN') IS NULL
    ALTER TABLE [dbo].[TR_SAZMAN] ADD [AUTO_MODERN] BIT NULL;
GO

IF OBJECT_ID(N'[dbo].[AUTO_HES]', N'U') IS NULL
    CREATE TABLE [dbo].[AUTO_HES](
        [COL]      NVARCHAR(30) NOT NULL CONSTRAINT [PK_AUTO_HES] PRIMARY KEY,
        [HES_K]    INT          NOT NULL,
        [HES_M]    INT          NULL,
        [HES_T]    BIGINT       NULL,
        [UP_USER]  NVARCHAR(50) NULL,
        [UP_DATE]  DATETIME     NULL CONSTRAINT [DF_AUTO_HES_UP_DATE] DEFAULT (GETDATE()),
        CONSTRAINT [CK_AUTO_HES_LEVELS] CHECK ([HES_T] IS NULL OR [HES_M] IS NOT NULL)
    );
GO
";
    }
}
