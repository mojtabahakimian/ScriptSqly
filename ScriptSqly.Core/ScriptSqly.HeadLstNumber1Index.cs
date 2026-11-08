using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ایندکسِ HEAD_LST(NUMBER1, TAG): قفلِ سربرگِ برگه‌های انبارِ سفیر با UPDLOCK و HOLDLOCK بی آن هر ردیفِ همان نوع فاکتور را
        /// قفل می‌کرد و SQL Server قفل را به کلِ HEAD_LST ارتقا می‌داد (بن‌بست با خواننده‌های هم‌زمان).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/78-head-lst-number1-index.sql در مخزن Safir یکی بماند
        /// (آزمونِ StockDocLockTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void HeadLstNumber1IndexScript(SqlConnection db)
        {
            ExecuteBatches(db, HeadLstNumber1IndexSql);
        }

        private const string HeadLstNumber1IndexSql = @"/* ═══════════════════════════════════════════════════════════════════
   ایندکسِ HEAD_LST(NUMBER1, TAG): قفلِ سربرگِ برگه‌های انبارِ سفیر (StockDocService.LockStateAsync) کلِ جدول را قفل نکند.

   • آن کوئری فاکتورِ وصل به برگه را با UPDLOCK و HOLDLOCK می‌خواند تا امضا یا قطعی‌شدنِ فاکتور تا پایانِ ذخیره صبر کند.
     بی ایندکس روی NUMBER1، هر ردیفِ همان نوع فاکتور خوانده و قفل می‌شد؛ بیش از ۵٬۰۰۰ قفل و SQL Server قفل را به
     کلِ HEAD_LST ارتقا می‌داد. نتیجه: بن‌بست با هر خواننده‌ی هم‌زمان (رئیس حسابداری، فهرستِ رسیدها؛ YAZDSEPAR1405،
     ۱۴۰۵/۰۸/۱۷) و صفِ همه‌ی برگه‌ها پشتِ یک ذخیره.
   • رسیدِ درخواستِ خرید فاکتورِ خود (رسیدِ خرید) را با NUMBER1 پیدا می‌کند؛ بقیه با NUMBER (ایندکسِ موجودِ NUMBER, TAG).
   • فقط یک ایندکسِ تازه؛ داده و ستونی عوض نمی‌شود. idempotent.
   ═══════════════════════════════════════════════════════════════════ */
IF OBJECT_ID(N'dbo.HEAD_LST', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.HEAD_LST', N'NUMBER1') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HEAD_LST') AND name = N'IX_HEAD_LST_NUMBER1_TAG')
    CREATE NONCLUSTERED INDEX IX_HEAD_LST_NUMBER1_TAG ON dbo.HEAD_LST (NUMBER1, TAG);
GO";
    }
}
