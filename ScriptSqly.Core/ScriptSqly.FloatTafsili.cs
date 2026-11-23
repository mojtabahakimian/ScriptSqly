using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// تفصیلی شناور در سفیر: شخصِ شناور (SAFIR_FLOAT_TAF) و ستونِ اتصالِ TDETA_HES.FLOAT_CODE.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/96-float-tafsili.sql در مخزن Safir یکی بماند
        /// (آزمونِ FloatTafsiliTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void FloatTafsiliScript(SqlConnection db)
        {
            ExecuteBatches(db, FloatTafsiliSql);
        }

        private const string FloatTafsiliSql = @"/* ═══════════════════════════════════════════════════════════════════
   تفصیلی شناور (فقط سفیر): یک شخص یک بار تعریف می‌شود و تفصیلی‌هایش زیر چند معین به او وصل‌اند.

   • SAFIR_FLOAT_TAF: شخصِ شناور با کدِ یکتا (کلید) و نامِ یکتا؛ مشخصاتش روی تفصیلی‌های وصل هم نوشته می‌شود.
   • TDETA_HES.FLOAT_CODE: تفصیلیِ سطحِ ۱ ِ کدام شخصِ شناور است (خالی = شناور نیست). تفصیلی‌های تازه‌ی صفحه‌ی
     «تفصیلی شناور» زیرِ همه‌ی معین‌ها همان کدِ شخص را می‌گیرند؛ تفصیلی‌های قدیمی با کدِ خودشان وصل می‌شوند و
     هیچ سندی عوض نمی‌شود. گزارش‌ها شخص را از همین ستون می‌شناسند، نه از برابر بودنِ کدها (کدِ کالا، شیفت و
     حساب‌های خودکار هم زیرِ معین‌های دیگر TNUMBER اند).
   نمایه‌ی FLOAT_CODE عمداً filtered نیست: نمایه‌ی filtered هر INSERT/UPDATE ِ TDETA_HES را از اتصال یا رویه‌ای که
   QUOTED_IDENTIFIER یا ANSI_NULLS خاموش دارد می‌خواباند.
   مجوز: همان فرمِ «سرفصل حساب‌ها» (TOTA_HES_SHEET)؛ فرمِ تازه ندارد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_FLOAT_TAF]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_FLOAT_TAF] (
        [CODE]      INT            NOT NULL CONSTRAINT [PK_SAFIR_FLOAT_TAF] PRIMARY KEY,
        [NAME]      NVARCHAR(100)  NOT NULL,
        [MCODEM]    NVARCHAR(20)   NULL,
        [MOBILE]    NVARCHAR(55)   NULL,
        [TEL]       NVARCHAR(50)   NULL,
        [ADDRESS]   NVARCHAR(100)  NULL,
        [TOZIH]     NVARCHAR(255)  NULL,
        [USERCO]    INT            NULL,
        [USER_NAME] NVARCHAR(50)   NULL,
        [CRT]       DATETIME       NOT NULL CONSTRAINT [DF_SAFIR_FLOAT_TAF_CRT] DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX [UX_SAFIR_FLOAT_TAF_NAME] ON [dbo].[SAFIR_FLOAT_TAF] ([NAME]);
END;
GO

IF OBJECT_ID(N'[dbo].[TDETA_HES]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TDETA_HES', N'FLOAT_CODE') IS NULL
    ALTER TABLE [dbo].[TDETA_HES] ADD [FLOAT_CODE] INT NULL;
GO

IF COL_LENGTH(N'dbo.TDETA_HES', N'FLOAT_CODE') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TDETA_HES_SAFIR_FLOAT_TAF')
    ALTER TABLE [dbo].[TDETA_HES] WITH CHECK ADD CONSTRAINT [FK_TDETA_HES_SAFIR_FLOAT_TAF]
        FOREIGN KEY ([FLOAT_CODE]) REFERENCES [dbo].[SAFIR_FLOAT_TAF] ([CODE]);
GO

IF COL_LENGTH(N'dbo.TDETA_HES', N'FLOAT_CODE') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TDETA_HES_FLOAT_CODE' AND object_id = OBJECT_ID(N'dbo.TDETA_HES'))
    CREATE INDEX [IX_TDETA_HES_FLOAT_CODE] ON [dbo].[TDETA_HES] ([FLOAT_CODE]);
GO

PRINT N'تفصیلی شناور: جدول SAFIR_FLOAT_TAF و ستون TDETA_HES.FLOAT_CODE آماده شد.';
GO";
    }
}
