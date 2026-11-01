using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «مسیر حرکت روی نقشه» (VISIT_MAS) در Safir: GPSLOCATION (اگر نبود) و ایندکسِ (FDATE, UID) برای خواندنِ مسیرِ یک روز.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/63-gps-location.sql در مخزن Safir یکی بماند
        /// (آزمونِ VisitorRouteTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void GpsLocationScript(SqlConnection db)
        {
            ExecuteBatches(db, GpsLocationSql);
        }

        private const string GpsLocationSql = @"/* ═══════════════════════════════════════════════════════════════════
   «مسیر حرکت روی نقشه» (VISIT_MAS) در Safir: خودِ Safir موقعیتِ هر کاربرِ واردشده را در GPSLOCATION می‌نویسد
   (همان جدولِ Access/GPSH.EXE؛ VISITOR = نامِ کاربر، FDATE = EDATE = تاریخِ شمسی، CRT = زمانِ نقطه، UID = کدِ کاربر).

   • اگر جدول نبود، با همان ساختارِ Access ساخته می‌شود.
   • ایندکسِ (FDATE, UID): صفحه‌ی نقشه همه‌ی نقطه‌های یک روز را می‌خواند و جدول روزانه هزاران سطر می‌گیرد.
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[GPSLOCATION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GPSLOCATION] (
        [VISITOR]   NVARCHAR(40) NOT NULL,
        [FDATE]     BIGINT       NOT NULL,
        [EDATE]     BIGINT       NOT NULL,
        [LATITUDE]  REAL         NULL,
        [LONGITUDE] REAL         NULL,
        [SPEED]     FLOAT        NOT NULL,
        [IDD]       INT          IDENTITY(1, 1) NOT NULL,
        [CRT]       DATETIME     NULL CONSTRAINT [DF_GPSLOCATION_CRT] DEFAULT (GETDATE()),
        [UID]       INT          NULL,
        CONSTRAINT [PK_GPSLOCATION] PRIMARY KEY CLUSTERED ([IDD])
    );
END;
GO

IF OBJECT_ID(N'[dbo].[GPSLOCATION]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[GPSLOCATION]') AND name = N'IX_GPSLOCATION_FDATE_UID')
    CREATE NONCLUSTERED INDEX [IX_GPSLOCATION_FDATE_UID] ON [dbo].[GPSLOCATION] ([FDATE], [UID])
        INCLUDE ([VISITOR], [LATITUDE], [LONGITUDE], [SPEED], [CRT]);
GO
";
    }
}
