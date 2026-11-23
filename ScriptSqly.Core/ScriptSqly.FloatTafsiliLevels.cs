using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// تفصیلی شناور در سطح‌های ۲ تا ۴: ستونِ اتصالِ FLOAT_CODE روی TDETA_HES2/3/4 (پرسنل: حقوق پرداختنی و حساب‌های ماهانه).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/97-float-tafsili-levels.sql در مخزن Safir یکی بماند
        /// (آزمونِ FloatTafsiliTests.Levels_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void FloatTafsiliLevelsScript(SqlConnection db)
        {
            ExecuteBatches(db, FloatTafsiliLevelsSql);
        }

        private const string FloatTafsiliLevelsSql = @"/* ═══════════════════════════════════════════════════════════════════
   تفصیلی شناور در سطح‌های ۲ تا ۴ (فقط سفیر): ستونِ اتصالِ FLOAT_CODE روی TDETA_HES2، TDETA_HES3 و TDETA_HES4.

   پرسنل در یزدسپار هم زیرِ معین‌ها (۱۱۶-۹-۴۲۰، ۲۵۳-۱-۴۲۰) هستند، هم در تفصیلیِ سطحِ دوم (حقوق پرداختنی ۲۱۳-۱-۱-۴۲۰) و
   هم در سطحِ سوم برای هر ماه (۲۱۳-۹-ماه-۳-۴۲۰)؛ همه باید به یک شخصِ شناور (SAFIR_FLOAT_TAF، مهاجرتِ ۹۶) وصل شوند.
   مثلِ مهاجرتِ ۹۶: نمایه filtered نیست، کلیدِ خارجی به SAFIR_FLOAT_TAF، هیچ داده‌ای عوض نمی‌شود. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_FLOAT_TAF]', N'U') IS NOT NULL
BEGIN
    DECLARE @t sysname, @sql nvarchar(max);
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT v.t FROM (VALUES (N'TDETA_HES2'), (N'TDETA_HES3'), (N'TDETA_HES4')) v(t) WHERE OBJECT_ID(N'dbo.' + v.t, N'U') IS NOT NULL;
    OPEN c;
    FETCH NEXT FROM c INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF COL_LENGTH(N'dbo.' + @t, N'FLOAT_CODE') IS NULL
        BEGIN
            SET @sql = N'ALTER TABLE [dbo].' + QUOTENAME(@t) + N' ADD [FLOAT_CODE] INT NULL;';
            EXEC sp_executesql @sql;
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_' + @t + N'_SAFIR_FLOAT_TAF')
        BEGIN
            SET @sql = N'ALTER TABLE [dbo].' + QUOTENAME(@t) + N' WITH CHECK ADD CONSTRAINT ' + QUOTENAME(N'FK_' + @t + N'_SAFIR_FLOAT_TAF') +
                       N' FOREIGN KEY ([FLOAT_CODE]) REFERENCES [dbo].[SAFIR_FLOAT_TAF] ([CODE]);';
            EXEC sp_executesql @sql;
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_' + @t + N'_FLOAT_CODE' AND object_id = OBJECT_ID(N'dbo.' + @t))
        BEGIN
            SET @sql = N'CREATE INDEX ' + QUOTENAME(N'IX_' + @t + N'_FLOAT_CODE') + N' ON [dbo].' + QUOTENAME(@t) + N' ([FLOAT_CODE]);';
            EXEC sp_executesql @sql;
        END;
        FETCH NEXT FROM c INTO @t;
    END;
    CLOSE c;
    DEALLOCATE c;
END;
GO

PRINT N'تفصیلی شناور: ستون FLOAT_CODE روی تفصیلی‌های سطح ۲ تا ۴ آماده شد.';
GO";
    }
}
