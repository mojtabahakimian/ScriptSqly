using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «فروش صادرات» در حساب‌های خودگردانِ Safir — ستونِ SAZMAN.SFROSH (و TR_SAZMAN.SFROSH).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/55-sazman-export-sales.sql در مخزن Safir یکی بماند
        /// (آزمونِ AutomaticExportSalesTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void ExportSalesAccountScript(SqlConnection db)
        {
            ExecuteBatches(db, ExportSalesAccountSql);
        }

        private const string ExportSalesAccountSql = @"/* ═══════════════════════════════════════════════════════════════════
   «فروش صادرات» در حساب‌های خودگردانِ Safir — ستونِ SAZMAN.SFROSH (کدِ حسابِ کل)

   سندِ فاکتورِ فروشِ صادراتی (HEAD_LST.SADER <> 0) در Safir ردیف‌های درآمدِ کالا را به این حساب می‌برد؛
   خالی که باشد، مثلِ قبل همان «فروش» (FROSH). نرم‌افزارِ ویندوزی این ستون را نمی‌خواند.
   ستونِ TR_SAZMAN هم ساخته می‌شود تا تاریخچه‌ی «حساب‌های خودگردان» آن را هم نگه دارد.
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'[dbo].[SAZMAN]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SAZMAN', N'SFROSH') IS NULL
    ALTER TABLE [dbo].[SAZMAN] ADD [SFROSH] FLOAT NULL;
GO

IF OBJECT_ID(N'[dbo].[TR_SAZMAN]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TR_SAZMAN', N'SFROSH') IS NULL
    ALTER TABLE [dbo].[TR_SAZMAN] ADD [SFROSH] FLOAT NULL;
GO
";
    }
}
