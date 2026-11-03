using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ردیابیِ سری ساخت در Safir، فازِ دو (تولید): سری‌های موادِ مصرفیِ هر تولید به «یک ماده در یک انبار از برگه‌ی خروجِ مواد»
        /// وصل است (TRACE_GRP، TRACE_GRP_LOT) تا با بازسازیِ آن برگه (خروجِ خودکارِ مواد و گامِ S07 ِ بستنِ بها) گم نشود.
        /// بعد از LotTraceScript اجرا می‌شود (FK به TRACE_LOT).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/69-lot-trace-production.sql در مخزن Safir یکی بماند
        /// (آزمونِ LotTraceProductionTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void LotTraceProductionScript(SqlConnection db)
        {
            ExecuteBatches(db, LotTraceProductionSql);
        }

        private const string LotTraceProductionSql = @"/* ═══════════════════════════════════════════════════════════════════
   ردیابیِ سری ساخت — فازِ دو، تولید: سری‌های موادِ مصرفیِ هر تولید.

   برگه‌ی خروجِ موادِ یک تولید (TAG 10 که شماره‌اش در NUMBER1 ِ برگه‌ی ورودِ کالای ساخته‌شده‌ی TAG 9 است) با هر تغییرِ برگه‌ی
   ورود (خروجِ خودکار، SAZMAN.ECONM) و در گامِ S07 ِ بستنِ بها از روی فرمول پاک و دوباره درج می‌شود؛ پس سریِ این موادِ به سطر
   (TRACE_DOC) نمی‌چسبد و با هر بازسازی گم می‌شد. این‌جا به «یک ماده در یک انبار از همان برگه» وصل می‌شود:
   • TRACE_GRP     — عکسِ گروه: TAG، شماره‌ی برگه، کالا، انبار و جمعِ MEGHk ِ سطرهایش در همان لحظه. تا جمعِ سطرها همان است
                     (بازسازی با همان فرمول) سری‌ها می‌مانند؛ اگر S07 یا تغییرِ فرمول مقدار را عوض کند گروه «ناهمخوان» می‌شود.
                     AUTO = پیشنهادِ FEFO ِ خودکار که کسی تأییدش نکرده (مصرفِ برآوردی).
   • TRACE_GRP_LOT — تقسیمِ همان مقدار بینِ سری‌های ماده.
   هیچ جدول، ویو یا رویه‌ی قبلی (از جمله CC_sp_S07_RebuildIssue) عوض نمی‌شود. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.TRACE_GRP', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_GRP (
        TAG int NOT NULL,
        NUMBER float NOT NULL,
        CODE nvarchar(40) NOT NULL,
        ANBAR int NOT NULL,
        QTY float NOT NULL,
        AUTO bit NOT NULL CONSTRAINT DF_TRACE_GRP_AUTO DEFAULT 0,
        CRT datetime NOT NULL CONSTRAINT DF_TRACE_GRP_CRT DEFAULT GETDATE(),
        UID int NULL,
        UPD datetime NULL,
        UPD_UID int NULL,
        CONSTRAINT PK_TRACE_GRP PRIMARY KEY (TAG, NUMBER, CODE, ANBAR)
    );
    CREATE INDEX IX_TRACE_GRP_CODE ON dbo.TRACE_GRP (CODE);
END;
GO

IF OBJECT_ID(N'dbo.TRACE_GRP_LOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRACE_GRP_LOT (
        TAG int NOT NULL,
        NUMBER float NOT NULL,
        CODE nvarchar(40) NOT NULL,
        ANBAR int NOT NULL,
        LOT_ID int NOT NULL CONSTRAINT FK_TRACE_GRP_LOT_LOT REFERENCES dbo.TRACE_LOT (ID),
        QTY decimal(19, 6) NOT NULL,
        CONSTRAINT PK_TRACE_GRP_LOT PRIMARY KEY (TAG, NUMBER, CODE, ANBAR, LOT_ID),
        CONSTRAINT FK_TRACE_GRP_LOT_GRP FOREIGN KEY (TAG, NUMBER, CODE, ANBAR) REFERENCES dbo.TRACE_GRP (TAG, NUMBER, CODE, ANBAR) ON DELETE CASCADE,
        CONSTRAINT CK_TRACE_GRP_LOT_QTY CHECK (QTY > 0)
    );
    CREATE INDEX IX_TRACE_GRP_LOT_LOT ON dbo.TRACE_GRP_LOT (LOT_ID) INCLUDE (QTY);
END;
GO

PRINT N'ردیابی سری ساخت (تولید): جدول‌های TRACE_GRP و TRACE_GRP_LOT آماده شد.';
GO";
    }
}
