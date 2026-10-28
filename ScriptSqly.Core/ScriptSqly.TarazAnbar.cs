using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// تراز انبار: MOG_EN و MOG_FR_A_sub با UNION ALL و سمتِ ورودِ تبدیل کالا (TAG 30)، تا مقدار برابرِ کارتِ کالا شود.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/59-taraz-anbar-qty.sql در مخزن Safir یکی بماند
        /// (آزمونِ StockProcLiveTests.Taraz_migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void TarazAnbarQtyScript(SqlConnection db)
        {
            ExecuteBatches(db, TarazAnbarQtySql);
        }

        private const string TarazAnbarQtySql = @"/* ═══════════════════════════════════════════════════════════════════
   تراز انبار (TARAZ_ANBAR_KHAS، C_TARAZ_ANBAR_KHAS، TARAZ_ANBAR_KHAS_GRP): مقدار برابرِ کارتِ کالا

   MOG_EN (ورود) و MOG_FR_A_sub (خروج) شاخه‌هایشان را با UNION می‌چسباندند؛ دو ردیفِ کاملاً یکسان
   (دو سطرِ انبارگردانیِ هم‌مقدار، یا ورود و انتقالِ هم‌مقدارِ یک کالا) یکی حساب می‌شد.
   MOG_EN سمتِ ورودِ تبدیل کالا (TAG 30: کالای N_RASID به انبارِ ANBARF با MEGH_MAR، ارزش MABL_K ِ همان برگه)
   را نداشت؛ سمتِ خروجش در 42-legacy-views-tag30 به MOG_FR_A_sub افزوده شده بود.
   بقیه‌ی فرمول‌ها همان قبلی است. روی ۵ کپیِ محلی، مقدارِ تراز = موجودی + رزرو ِ sp_Mogudi_Tafkik_Optimized
   (یزدسپار: ۲۲ ردیفِ ناجور ← ۰).
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER FUNCTION [dbo].[MOG_EN] (@FORMS___F_MENU_ANBAR_TARAZ___DT2 bigint)
RETURNS TABLE
AS
RETURN (
    SELECT i.ANBARF, i.CODE, SUM(i.MEGHk - i.MEGH_MAR) AS MEG, SUM(i.MABL_K) AS SumOfMABL_K
    FROM dbo.INVO_LST i INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
    WHERE i.TAG = 5 AND h.DATE_N <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY i.ANBARF, i.CODE
    UNION ALL
    SELECT i.ANBAR, i.CODE, SUM(i.MEGHk - i.MEGH_MAR), SUM(i.MABL_K - i.MEGH_MAR * i.AVRAGE)
    FROM dbo.INVO_LST i INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
    WHERE i.TAG IN (1, 7, 9) AND h.DATE_N <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY i.ANBAR, i.CODE
    UNION ALL
    SELECT i.ANBAR, i.CODE, SUM(i.MEGHk), SUM(i.AVRAGE * i.MEGHk)
    FROM dbo.INVO_LST i INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
    WHERE i.TAG IN (22, 24) AND h.DATE_N <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY i.ANBAR, i.CODE
    UNION ALL
    SELECT i.ANBARF, i.N_RASID, SUM(i.MEGH_MAR), SUM(i.MABL_K)
    FROM dbo.INVO_LST i INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
    WHERE i.TAG = 30 AND i.N_RASID IS NOT NULL AND i.ANBARF IS NOT NULL AND i.MEGH_MAR > 0
      AND h.DATE_N <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY i.ANBARF, i.N_RASID
    UNION ALL
    SELECT H.GRD_ANBAR, L.CODE, SUM(L.NUM3 - L.MOG), SUM(L.MABL * (L.NUM3 - L.MOG))
    FROM dbo.ANBGRD_LST L INNER JOIN dbo.ANBGRD_HEAD H ON L.GRD_NUM = H.GRD_NUM
    WHERE L.NUM3 - L.MOG > 0 AND H.N_S IS NOT NULL AND H.GRD_DATE <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY H.GRD_ANBAR, L.CODE
)
GO
CREATE OR ALTER FUNCTION [dbo].[MOG_FR_A_sub] (@FORMS___F_MENU_ANBAR_TARAZ___DT2 bigint)
RETURNS TABLE
AS
RETURN (
    SELECT i.ANBAR, i.CODE, SUM(i.MEGHk - i.MEGH_MAR) AS MEG,
           SUM(i.AVRAGE * i.MEGHk - ISNULL(i.AVRAGE2, 0) * i.MEGH_MAR) AS avgofmabl
    FROM dbo.INVO_LST i INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
    WHERE i.TAG IN (2, 5, 8, 10, 11, 26) AND h.DATE_N <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY i.ANBAR, i.CODE
    UNION ALL
    SELECT H.GRD_ANBAR, L.CODE, L.MOG - L.NUM3, ABS(L.MOG - L.NUM3) * L.MABL
    FROM dbo.ANBGRD_LST L INNER JOIN dbo.ANBGRD_HEAD H ON L.GRD_NUM = H.GRD_NUM
    WHERE L.MOG - L.NUM3 > 0 AND H.GRD_DATE <= @FORMS___F_MENU_ANBAR_TARAZ___DT2 AND H.N_S IS NOT NULL
    UNION ALL
    /* تبدیل کالا (TAG = 30) — سمت خروج */
    SELECT i.ANBAR, i.CODE, SUM(i.MEGHk), SUM(i.AVRAGE * i.MEGHk)
    FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
    WHERE i.TAG = 30 AND h.DATE_N <= @FORMS___F_MENU_ANBAR_TARAZ___DT2
    GROUP BY i.ANBAR, i.CODE
)
GO
";
    }
}
