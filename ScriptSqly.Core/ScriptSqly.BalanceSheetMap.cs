using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// صورت وضعیت مالی (ترازنامه) در Safir: جدولِ نگاشتِ حساب‌های کل به سطرهای استاندارد (ACC_BS_MAP).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/61-balance-sheet-map.sql در مخزن Safir یکی بماند
        /// (آزمونِ BalanceSheetTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void BalanceSheetMapScript(SqlConnection db)
        {
            ExecuteBatches(db, BalanceSheetMapSql);
        }

        private const string BalanceSheetMapSql = @"/* ═══════════════════════════════════════════════════════════════════
   صورت وضعیت مالی (ترازنامه) در Safir: نگاشتِ حساب‌های کل به سطرهای استاندارد

   • ACC_BS_MAP — یک ردیف برای هر حسابِ کل: سطرِ ترازنامه برای مانده‌ی بدهکارِ هر تفصیلی (DEB_LINE) و برای مانده‌ی
                  بستانکار (CRED_LINE). کدها همان BalanceSheetCatalog ِ Safir‌اند (مثلاً CASH، TRADEREC، ADVREC؛ PL = حسابِ موقت،
                  EX = خارج از صورت). حسابی که ردیف ندارد با پیشنهادِ خودکار (از نامِ گروه و حساب) طبقه‌بندی می‌شود.
   جدولِ TARAZHES ِ WPF («حساب‌های ترازنامه») دست نمی‌خورد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

IF OBJECT_ID(N'dbo.ACC_BS_MAP', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ACC_BS_MAP (
        KOL float NOT NULL CONSTRAINT PK_ACC_BS_MAP PRIMARY KEY,
        DEB_LINE nvarchar(20) NOT NULL,
        CRED_LINE nvarchar(20) NOT NULL,
        CRT datetime NOT NULL CONSTRAINT DF_ACC_BS_MAP_CRT DEFAULT GETDATE(),
        UID int NULL
    );
END;
GO
";
    }
}
