using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// «برگشت به انبار» در سفیر: TAGCOD 32/33، فرم‌های HAVALAH_RETURN و HAVALAH_RETURN_SAYER (مجوز از فرمِ خروجِ متناظر)، و شاخه‌ی
        /// ورودِ TAG 32/33 در نماها و توابعِ قدیمیِ موجودی و کارت کالا.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/85-stock-return.sql در مخزن Safir یکی بماند
        /// (آزمونِ StockReturnTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void StockReturnScript(SqlConnection db)
        {
            ExecuteBatches(db, StockReturnSql);
        }

        private const string StockReturnSql = @"/* ═══════════════════════════════════════════════════════════════════
   برگشت به انبار: برگه‌ی تازه برای برگرداندنِ بخشی از خروجِ مواد (TAG 32) و خروجِ سایر مواد (TAG 33) در تاریخِ خودِ برگشت

   ── چرا برگه‌ی جدا ──
   برگشت‌های قدیمی روی خودِ سطرِ برگه‌ی اصلی می‌نشستند (MEGH_MAR)، یعنی ماهِ خروج عوض می‌شد. این‌جا برگه‌ی خروج و ماهش دست
   نمی‌خورند: برگشتِ مهر موجودیِ مهر را زیاد و هزینه‌ی مهر را کم می‌کند و سود و زیانِ شهریور همان می‌ماند. NUMBER1 ِ سربرگ
   شماره‌ی برگه‌ی مبدأ است؛ هر سطر کالا + محلِ مصرف (N_RASID) + مرکزِ هزینه (SANAD_NO) ِ همان سطرِ مبدأ را دارد و با نرخِ
   همان خروج (AVRAGE) وارد می‌شود. سندش NO_S = 32 / 33 است.

   ── این اسکریپت ──
   ۱) TAGCOD: ردیف‌های ۳۲ و ۳۳ (tartib مثلِ ورودها، فقط اگر خالی باشد).
   ۲) TFORMS: فرم‌های HAVALAH_RETURN و HAVALAH_RETURN_SAYER؛ مجوزِ هر کاربر همان مجوزش روی فرمِ خروجِ متناظر
      (HAVALAH_EXIT / HAVALAH_EXIT_SAYER) — کسی که خروج می‌زند برگشت هم می‌زند. ردیفِ موجود دست نمی‌خورد.
   ۳) نماها و توابعِ قدیمیِ موجودی که فهرستِ ورودها را هاردکد دارند (همان خانواده‌ی 42-legacy-views-tag30.sql): یک شاخه‌ی
      «ورود» برای TAG 32/33 به انتهایشان اضافه می‌شود. هر شیء فقط وقتی دست می‌خورد که امضای شناخته‌شده را داشته باشد و
      هنوز ۳۲/۳۳ را نشناسد؛ وگرنه نامش چاپ می‌شود. MOGHA_ANBAR و رویه‌های بستنِ ماه در فایل‌های خودشان به‌روز شده‌اند.

   نرم‌افزارِ ویندوزی TAG 32/33 را نمی‌شناسد؛ گزارش‌هایی که فهرستِ TAG را در کدِ خودش دارد برگشت را نمی‌بیند.
   idempotent است. نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ───────── ۱) TAGCOD ───────── */
MERGE dbo.TAGCOD AS t
USING (VALUES
    (32, N'برگشت مواد به انبار'),
    (33, N'برگشت ساير مواد به انبار')
) AS s (CODE, BARGAH)
ON t.CODE = s.CODE
WHEN NOT MATCHED THEN INSERT (CODE, BARGAH) VALUES (s.CODE, s.BARGAH);
GO

IF COL_LENGTH('dbo.TAGCOD', 'tartib') IS NOT NULL
BEGIN
    -- ورود است: در یک روز پیش از خروج‌ها، هم‌رتبه‌ی «حواله ورود» (۹)
    UPDATE dbo.TAGCOD SET tartib = 9 WHERE CODE IN (32, 33) AND tartib IS NULL;
END
GO

/* ───────── ۲) TFORMS و مجوزها ───────── */
IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
BEGIN
    DECLARE @forms TABLE (Name nvarchar(60), Caption nvarchar(200), Source nvarchar(60));
    INSERT @forms VALUES
        (N'HAVALAH_RETURN',       N'برگشت مواد اولیه به انبار',  N'HAVALAH_EXIT'),
        (N'HAVALAH_RETURN_SAYER', N'برگشت سایر مواد به انبار',   N'HAVALAH_EXIT_SAYER');

    DECLARE @name nvarchar(60), @cap nvarchar(200), @src nvarchar(60);
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name, Caption, Source FROM @forms;
    OPEN c;
    FETCH NEXT FROM c INTO @name, @cap, @src;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = @name)
        BEGIN
            DECLARE @Grp int = ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = @src), 3);
            INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
            VALUES (@name, @cap, 3, @Grp, (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]), GETDATE());

            IF OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
                INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
                SELECT s.USERCO, f.IDH, s.RUN, s.SEE, s.INP, s.UPD, s.DEL, GETDATE()
                FROM [dbo].[SAL_CHEK] s
                JOIN [dbo].[TFORMS] fs ON fs.IDH = s.[OBJECT] AND fs.FORMNAME = @src
                CROSS JOIN [dbo].[TFORMS] f
                WHERE f.FORMNAME = @name
                  AND NOT EXISTS (SELECT 1 FROM [dbo].[SAL_CHEK] e WHERE e.USERCO = s.USERCO AND e.[OBJECT] = f.IDH);
        END
        FETCH NEXT FROM c INTO @name, @cap, @src;
    END
    CLOSE c;
    DEALLOCATE c;
END
GO

/* ───────── ۳) MOGO_AVL_KOL_SUB — ورود، در سطح کد کالا ───────── */
IF OBJECT_ID('dbo.MOGO_AVL_KOL_SUB','V') IS NOT NULL
BEGIN
    DECLARE @v1 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.MOGO_AVL_KOL_SUB'));
    IF @v1 LIKE '%TAG IN (32, 33)%'
        PRINT N'MOGO_AVL_KOL_SUB از قبل برگشت به انبار را می‌شناسد.';
    ELSE IF @v1 NOT LIKE '%TAG = 24%' OR CHARINDEX('CREATE VIEW', UPPER(@v1)) = 0
        PRINT N'⚠ MOGO_AVL_KOL_SUB تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        SET @v1 = STUFF(@v1, CHARINDEX('CREATE VIEW', UPPER(@v1)), 11, 'ALTER VIEW') + N'
UNION
/* برگشت به انبار (TAG 32/33) — ورود با نرخِ همان خروج */
SELECT     CODE, SUM(MEGHk) AS MEG, SUM(MABL_K) AS SumOfMABL_K, 5 AS AA
FROM         dbo.INVO_LST
WHERE     TAG IN (32, 33)
GROUP BY CODE';
        BEGIN TRY EXEC sp_executesql @v1; PRINT N'MOGO_AVL_KOL_SUB به‌روز شد.'; END TRY
        BEGIN CATCH PRINT N'⚠ MOGO_AVL_KOL_SUB به‌روز نشد: ' + ERROR_MESSAGE(); END CATCH
    END
END
GO

/* ───────── ۴) B_MOG_KOL_SUB — ورود، در سطح انبار ───────── */
IF OBJECT_ID('dbo.B_MOG_KOL_SUB','V') IS NOT NULL
BEGIN
    DECLARE @v2 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.B_MOG_KOL_SUB'));
    IF @v2 LIKE '%TAG IN (32, 33)%'
        PRINT N'B_MOG_KOL_SUB از قبل برگشت به انبار را می‌شناسد.';
    ELSE IF @v2 NOT LIKE '%TAG = 24%' OR CHARINDEX('CREATE VIEW', UPPER(@v2)) = 0
        PRINT N'⚠ B_MOG_KOL_SUB تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        SET @v2 = STUFF(@v2, CHARINDEX('CREATE VIEW', UPPER(@v2)), 11, 'ALTER VIEW') + N'
UNION
/* برگشت به انبار (TAG 32/33) */
SELECT     ANBAR, CODE, SUM(MEGHk) AS MEG, 5 AS AA
FROM         dbo.INVO_LST
WHERE     TAG IN (32, 33)
GROUP BY ANBAR, CODE';
        BEGIN TRY EXEC sp_executesql @v2; PRINT N'B_MOG_KOL_SUB به‌روز شد.'; END TRY
        BEGIN CATCH PRINT N'⚠ B_MOG_KOL_SUB به‌روز نشد: ' + ERROR_MESSAGE(); END CATCH
    END
END
GO

/* ───────── ۵) mogudi_1 — ورودِ یک انبار (تابعِ درون‌خطی: شاخه پیش از آخرین پرانتز) ───────── */
IF OBJECT_ID('dbo.mogudi_1','IF') IS NOT NULL
BEGIN
    DECLARE @f1 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.mogudi_1')), @c1 INT;
    IF @f1 LIKE '%TAG IN (32, 33)%'
        PRINT N'mogudi_1 از قبل برگشت به انبار را می‌شناسد.';
    ELSE IF @f1 NOT LIKE '%TAG = 24%' OR @f1 NOT LIKE '%@Forms___F_MENU_ANBAR___MANBAR%'
        PRINT N'⚠ mogudi_1 تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        SET @f1 = REPLACE(@f1, 'CREATE FUNCTION', 'ALTER FUNCTION');
        SET @c1 = LEN(@f1) - CHARINDEX(')', REVERSE(@f1));
        IF @c1 <= 0 PRINT N'⚠ mogudi_1 ساختار غیرمنتظره دارد — دست نخورد.';
        ELSE
        BEGIN
            SET @f1 = LEFT(@f1, @c1) + N'
 UNION
 /* برگشت به انبار (TAG 32/33) */
 SELECT CODE, SUM(MEGHk) AS MEG, ANBAR, 5 AS AA
 FROM dbo.INVO_LST WHERE TAG IN (32, 33)
 GROUP BY CODE, ANBAR HAVING (ANBAR = @Forms___F_MENU_ANBAR___MANBAR)
' + SUBSTRING(@f1, @c1 + 1, LEN(@f1));
            BEGIN TRY EXEC sp_executesql @f1; PRINT N'mogudi_1 به‌روز شد.'; END TRY
            BEGIN CATCH PRINT N'⚠ mogudi_1 به‌روز نشد: ' + ERROR_MESSAGE(); END CATCH
        END
    END
END
GO

/* ───────── ۶) AK_MOGO_AVL_KOL_SUB — ورودِ تاریخ‌دار (موجودیِ لحظه‌ای و تراز انبار از همین است) ───────── */
IF OBJECT_ID('dbo.AK_MOGO_AVL_KOL_SUB','IF') IS NOT NULL
BEGIN
    DECLARE @f2 NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.AK_MOGO_AVL_KOL_SUB')), @c2 INT;
    IF @f2 LIKE '%TAG IN (32, 33)%'
        PRINT N'AK_MOGO_AVL_KOL_SUB از قبل برگشت به انبار را می‌شناسد.';
    ELSE IF @f2 NOT LIKE '%@Forms___F_MENU_ANBAR___MANBAR%'
        PRINT N'⚠ AK_MOGO_AVL_KOL_SUB تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        SET @f2 = REPLACE(@f2, 'CREATE FUNCTION', 'ALTER FUNCTION');
        SET @c2 = LEN(@f2) - CHARINDEX(')', REVERSE(@f2));
        IF @c2 <= 0 PRINT N'⚠ AK_MOGO_AVL_KOL_SUB ساختار غیرمنتظره دارد — دست نخورد.';
        ELSE
        BEGIN
            SET @f2 = LEFT(@f2, @c2) + N'
   UNION
   /* برگشت به انبار (TAG 32/33) */
   SELECT     i.CODE, SUM(i.MEGHk) AS MEG, SUM(i.MABL_K) AS SumOfMABL_K, i.ANBAR, 5 AS AA
   FROM       dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
   WHERE      i.TAG IN (32, 33) AND h.DATE_N <= @Forms___F_MENU_ANBAR___DT2
   GROUP BY   i.CODE, i.ANBAR
   HAVING     (i.ANBAR LIKE @Forms___F_MENU_ANBAR___MANBAR)
' + SUBSTRING(@f2, @c2 + 1, LEN(@f2));
            BEGIN TRY EXEC sp_executesql @f2; PRINT N'AK_MOGO_AVL_KOL_SUB به‌روز شد.'; END TRY
            BEGIN CATCH PRINT N'⚠ AK_MOGO_AVL_KOL_SUB به‌روز نشد: ' + ERROR_MESSAGE(); END CATCH
        END
    END
END
GO

/* ───────── ۷) KA_KH — کارت کالا ───────── */
IF OBJECT_ID('dbo.KA_KH','IF') IS NOT NULL
BEGIN
    DECLARE @k NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.KA_KH')), @ck INT;
    IF @k LIKE '%TAG IN (32, 33)%'
        PRINT N'KA_KH از قبل برگشت به انبار را می‌شناسد.';
    ELSE IF @k NOT LIKE '%BEDNAME%' OR @k NOT LIKE '%TAG = 11%'
        PRINT N'⚠ KA_KH تعریف غیرمنتظره دارد — دست نخورد.';
    ELSE
    BEGIN
        SET @k = REPLACE(@k, 'CREATE FUNCTION', 'ALTER FUNCTION');
        SET @ck = LEN(@k) - CHARINDEX(')', REVERSE(@k));
        IF @ck <= 0 PRINT N'⚠ KA_KH ساختار غیرمنتظره دارد — دست نخورد.';
        ELSE
        BEGIN
            SET @k = LEFT(@k, @ck) + N'
        UNION
        /* برگشت به انبار (TAG 32/33): ورود با نرخِ همان خروج؛ «شرح» شماره‌ی برگه‌ی خروجِ مبدأ */
        SELECT     i.ANBAR, i.CODE, i.MEGHk AS MEG, i.MABL, i.MABL_K, h.TAG,
                   i.MEGHk, h.DATE_N, i.NUMBER,
                   N''برگشت از خروج '' + CAST(CAST(ISNULL(h.NUMBER1, 0) AS bigint) AS nvarchar(20)) AS BEDNAME,
                   i.AVRAGE2, h.FNUMCO, i.id, ISNULL(i.MANDAH, N''  '') AS mol
        FROM       dbo.HEAD_LST h
                   INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE      h.TAG IN (32, 33)
   ' + SUBSTRING(@k, @ck + 1, LEN(@k));
            BEGIN TRY EXEC sp_executesql @k; PRINT N'KA_KH به‌روز شد.'; END TRY
            BEGIN CATCH PRINT N'⚠ KA_KH به‌روز نشد: ' + ERROR_MESSAGE(); END CATCH
        END
    END
END
GO

PRINT N'برگشت به انبار: TAGCOD 32/33، فرم‌های HAVALAH_RETURN*، نماها و کارت کالا بررسی شد.';
GO";
    }
}
