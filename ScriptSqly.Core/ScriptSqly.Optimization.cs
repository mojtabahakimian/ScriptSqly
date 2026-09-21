using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// به ایندکس موجودِ IX_DEED_DTL ستون‌های BED و BES را به‌صورت INCLUDE اضافه می‌کند.
        ///
        /// چرا: مانده‌ی حساب مشتری با
        ///     SELECT SUM(BED - BES) FROM dbo.DEED_DTL WHERE HES = @hes
        /// گرفته می‌شود و در فاکتور فروش در هر جابه‌جایی رکورد اجرا می‌شود. ایندکس فعلی
        /// فقط کلید HES را دارد، پس برای خواندن BED/BES باید به ازای هر ردیفِ آن حساب یک
        /// key lookup به جدول اصلی بزند.
        ///
        /// اندازه‌گیری روی ۴۰ حساب واقعی با DBCC DROPCLEANBUFFERS بین دو حالت
        /// (YAZDSEPAR1405_06_25، DEED_DTL با ۳۲۲٬۸۶۷ ردیف):
        ///     بدون INCLUDE : ۱۰۷۷۹ / ۱۰۵۶۱ / ۱۰۴۰۵ ms
        ///     با INCLUDE   :   ۳۱۸ /    ۳۲ /     ۴ ms
        /// وقتی جدول کامل در buffer pool باشد تفاوتی دیده نمی‌شود؛ سود این ایندکس برای
        /// حالتی است که داده در حافظه نیست (شروع کار، یا فشار حافظه).
        ///
        /// هزینه‌اش: حجم ایندکس ۱۳ → ۱۸ مگابایت، و در نوشتن روی ۲۰۰۰ ردیف
        /// INSERT ۸۹→۱۱۶ms، UPDATE ۱۶→۲۵ms، DELETE ۳۵→۴۱ms — یعنی حدود
        /// ۰٫۰۱۴ms به ازای هر ردیف.
        ///
        /// عمداً ایندکس **جدیدی ساخته نمی‌شود**: همان IX_DEED_DTL با DROP_EXISTING
        /// بازسازی می‌گردد تا تعداد ایندکس‌های جدول ثابت بماند و بار نوشتن بالا نرود.
        ///
        /// برگرداندن (در صورت نیاز):
        ///     CREATE NONCLUSTERED INDEX [IX_DEED_DTL] ON [dbo].[DEED_DTL]([HES])
        ///         WITH (DROP_EXISTING = ON, ONLINE = ON);
        ///
        /// مثل بقیه‌ی این فایل فقط با اجرای دستیِ اسکریپت از «درباره تهیه‌کنندگان»
        /// اجرا می‌شود، نه خودکار هنگام ورود کاربر.
        /// </summary>
        private static void DeedDtlBalanceCoveringIndex(SqlConnection db)
        {
            try
            {
                //بازسازی یک ایندکس ۳۳۵ مگابایتی از مهلت پیش‌فرض ۳۰ ثانیه رد می‌شود؛
                //بدون این، روی دیتابیس بزرگ timeout می‌خورد و کاربر خبردار نمی‌شود.
                db.Execute(commandTimeout: 3600, sql: @"
IF OBJECT_ID(N'dbo.DEED_DTL', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.DEED_DTL', N'BED') IS NOT NULL
   AND COL_LENGTH(N'dbo.DEED_DTL', N'BES') IS NOT NULL
   AND COL_LENGTH(N'dbo.DEED_DTL', N'HES') IS NOT NULL
   -- فقط روی همان ایندکسِ موجود کار می‌کنیم؛ اگر نبود چیزی نمی‌سازیم
   AND EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.DEED_DTL') AND name = N'IX_DEED_DTL')
   -- فقط وقتی دست می‌زنیم که ایندکس *دقیقاً* همان شکل اصلی باشد: تک‌کلید HES و بدون
   -- هیچ INCLUDE. اگر روی سیستم مشتری کسی ستون دیگری به آن اضافه کرده باشد، بازسازی
   -- آن را بی‌صدا پاک می‌کرد و گزارشی که به آن وابسته است کند می‌شد. در آن حالت
   -- کاری نمی‌کنیم و تصمیم با آدم است. اگر هم قبلاً اعمال شده باشد همین شرط ردش می‌کند.
   AND 1 = (SELECT COUNT(*) FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.object_id = OBJECT_ID(N'dbo.DEED_DTL') AND i.name = N'IX_DEED_DTL')
   AND EXISTS (SELECT 1 FROM sys.indexes i
               JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
               JOIN sys.columns c        ON c.object_id  = ic.object_id AND c.column_id = ic.column_id
               WHERE i.object_id = OBJECT_ID(N'dbo.DEED_DTL') AND i.name = N'IX_DEED_DTL'
                 AND ic.is_included_column = 0 AND ic.key_ordinal = 1 AND c.name = N'HES')
BEGIN
    -- ONLINE فقط در ادیشن‌هایی که پشتیبانی می‌کنند؛ وگرنه ساخت، جدول را کوتاه قفل می‌کند
    DECLARE @online nvarchar(3) =
        CASE WHEN CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8) THEN N'ON' ELSE N'OFF' END;

    DECLARE @sql nvarchar(max) =
        N'CREATE NONCLUSTERED INDEX [IX_DEED_DTL] ON [dbo].[DEED_DTL]([HES]) INCLUDE ([BED], [BES])' +
        N' WITH (DROP_EXISTING = ON, SORT_IN_TEMPDB = ON, ONLINE = ' + @online + N');';

    EXEC sys.sp_executesql @sql;
END");
            }
            catch (Exception ex)
            {
                //کاربر خودش دکمه را زده و پیام «اسکریپت‌ها اجرا شدند» می‌بیند؛ اگر این
                //یکی بی‌صدا شکست بخورد خیال می‌کند اعمال شده. پس ثبتش می‌کنیم.
                LogNonBlockingMigrationFailure(nameof(DeedDtlBalanceCoveringIndex), db.Database, ex);
            }
        }

        private static void SequentialKeyContentionScript(SqlConnection db)
        {
            try
            {
                // بهینه‌سازی کلید صعودی (OPTIMIZE_FOR_SEQUENTIAL_KEY) برای SQL Server 2019+
                db.Execute(@"
IF TRY_CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) >= 15
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'';

    SELECT @sql = @sql + N'ALTER INDEX ' + QUOTENAME(i.name)
                       + N' ON ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
                       + N' SET (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON);' + CHAR(10)
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t
        ON t.object_id = i.object_id
    INNER JOIN sys.index_columns AS ic
        ON ic.object_id = i.object_id
       AND ic.index_id  = i.index_id
       AND ic.key_ordinal = 1
    INNER JOIN sys.columns AS c
        ON c.object_id = i.object_id
       AND c.column_id = ic.column_id
    WHERE i.index_id > 0
      AND i.is_hypothetical = 0
      AND i.is_disabled = 0
      AND i.optimize_for_sequential_key = 0
      AND c.is_identity = 1
      AND t.name IN (N'DEED_DTL', N'INVO_LST', N'PGET_LST', N'PGET_HED', N'DEED_HED', N'HEAD_LST');

    IF LEN(@sql) > 0
    BEGIN
        EXEC sys.sp_executesql @sql;
    END
END");
            }
            catch { }
        }
    }
}
