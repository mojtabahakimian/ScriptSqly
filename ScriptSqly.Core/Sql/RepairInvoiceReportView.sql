DECLARE @PREVIEW_ONLY bit = 1;
EXEC sys.sp_executesql N'
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @PREVIEW_ONLY IS NULL THROW 51140, N''حالت پیش‌نمایش باید مشخص باشد.'', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N''ScriptSqly.InvoiceReportView'',
        @LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=15000;
    IF @LockResult<0 THROW 51141,N''نمای چاپ در حال به‌روزرسانی است؛ دوباره تلاش کنید.'',1;
    IF OBJECT_ID(N''dbo.HEAD_LST'',N''U'') IS NULL
        THROW 51142,N''جدول اصلی فاکتور وجود ندارد؛ نمای چاپ تغییر نکرد.'',1;

    DECLARE @Columns TABLE (Id int IDENTITY, Name sysname);
    INSERT @Columns(Name) VALUES
        (N''NUMBER''),(N''HTAG''),(N''ANBAR''),(N''NUMBER1''),(N''DATE_N''),(N''TAH''),(N''MAS''),(N''VAS''),
        (N''N_S''),(N''CUST_NO''),(N''MOLAH''),(N''M_NAGHD''),(N''MABL_VAR''),(N''MOIN_VAR''),
        (N''MABL_HAV''),(N''MOIN_HAV''),(N''MABL_HAZ''),(N''MOIN_HAZ''),(N''TAKHFIF''),(N''MOIN_KHF''),
        (N''ANBARF''),(N''FNUMCO''),(N''DEPATMAN''),(N''SHIFT''),(N''CUST_KIND''),(N''USER_NAME''),
        (N''MBAA''),(N''SADER''),(N''SHARAYET''),(N''TAG''),
        (N''SGN1usid''),(N''sgn2usid''),(N''sgn3usid''),(N''SGN1''),(N''SGN2''),(N''SGN3'');
    IF EXISTS(SELECT 1 FROM @Columns WHERE Name<>N''HTAG'' AND COL_LENGTH(N''dbo.HEAD_LST'',Name) IS NULL)
        THROW 51143,N''ستون‌های موردنیاز فاکتور کامل نیستند؛ ابتدا ساختار اصلی را ارتقا دهید.'',1;

    DECLARE @ViewId int=OBJECT_ID(N''dbo.HEAD_BACK_ANBAR'');
    IF @ViewId IS NOT NULL AND OBJECT_ID(N''dbo.HEAD_BACK_ANBAR'',N''V'') IS NULL
        THROW 51144,N''شیء هم‌نام نمای چاپ وجود دارد؛ تغییر خودکار انجام نشد.'',1;
    IF @ViewId IS NOT NULL AND NOT EXISTS
        (SELECT 1 FROM @Columns x WHERE NOT EXISTS
            (SELECT 1 FROM sys.columns c WHERE c.object_id=@ViewId AND c.name=x.Name))
    BEGIN
        COMMIT;
        SELECT CAST(0 AS bit) Changed,CAST(0 AS bit) NeedsUpgrade,N''AlreadyCurrent'' AS State;
        RETURN;
    END;

    DECLARE @Definition nvarchar(max),@Actual nvarchar(max),@Expected nvarchar(max),@Projection nvarchar(max);
    IF @ViewId IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM sys.sql_modules WHERE object_id=@ViewId AND is_schema_bound=1)
            THROW 51145,N''نمای چاپ وابستگی ساختاری ویژه دارد؛ تغییر خودکار انجام نشد.'',1;
        IF EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=@ViewId AND NOT EXISTS
            (SELECT 1 FROM @Columns x WHERE x.Name=c.name))
            THROW 51146,N''نمای چاپ ستون سفارشی دارد؛ تغییر خودکار انجام نشد.'',1;
        IF EXISTS(SELECT 1 FROM @Columns x WHERE x.Id<=30 AND NOT EXISTS
            (SELECT 1 FROM sys.columns c WHERE c.object_id=@ViewId AND c.name=x.Name))
            THROW 51147,N''ساختار پایه نمای چاپ ناشناخته است؛ تغییر خودکار انجام نشد.'',1;
        SELECT @Projection=STUFF((SELECT N'',''+CASE WHEN name=N''HTAG''
            THEN N''TAG-11 AS HTAG'' ELSE QUOTENAME(name) END
            FROM sys.columns WHERE object_id=@ViewId ORDER BY column_id
            FOR XML PATH(''''),TYPE).value(''.'',''nvarchar(max)''),1,1,N'''');
        SET @Expected=N''SELECT''+@Projection+N''FROMHEAD_LSTWHERE(TAG-11>0)'';
        SET @Definition=OBJECT_DEFINITION(@ViewId);
        IF @Definition IS NULL THROW 51148,N''تعریف نمای چاپ قابل خواندن نیست؛ تغییر انجام نشد.'',1;
        SET @Actual=UPPER(@Definition);
        SET @Expected=UPPER(@Expected);
        SET @Actual=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@Actual,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''),N''['',N''''),N'']'',N'''');
        SET @Expected=REPLACE(REPLACE(REPLACE(@Expected,N'' '',N''''),N''['',N''''),N'']'',N'''');
        SET @Actual=REPLACE(REPLACE(@Actual,N''DBO.'',N''''),N''HEAD_LST.'',N'''');
        IF CHARINDEX(N''ASSELECT'',@Actual)=0 THROW 51149,N''تعریف نمای چاپ ناشناخته است؛ تغییر خودکار انجام نشد.'',1;
        SET @Actual=SUBSTRING(@Actual,CHARINDEX(N''ASSELECT'',@Actual)+2,LEN(@Actual));
        WHILE RIGHT(@Actual,1)=N'';'' SET @Actual=LEFT(@Actual,LEN(@Actual)-1);
        IF @Actual<>@Expected THROW 51149,N''فیلتر یا محاسبه نمای چاپ سفارشی است؛ تغییر خودکار انجام نشد.'',1;
    END;

    SELECT @Projection=STUFF((SELECT N'',''+CASE WHEN x.Name=N''HTAG''
        THEN N''[TAG]-11 AS [HTAG]'' ELSE QUOTENAME(x.Name) END
        FROM @Columns x LEFT JOIN sys.columns c ON c.object_id=@ViewId AND c.name=x.Name
        ORDER BY COALESCE(c.column_id,1000+x.Id)
        FOR XML PATH(''''),TYPE).value(''.'',''nvarchar(max)''),1,1,N'''');
    DECLARE @Sql nvarchar(max)=N''CREATE OR ALTER VIEW dbo.HEAD_BACK_ANBAR AS SELECT ''+@Projection+
        N'' FROM dbo.HEAD_LST WHERE (TAG-11>0);'';
    IF @PREVIEW_ONLY=1
    BEGIN
        COMMIT;
        SELECT CAST(0 AS bit) Changed,CAST(1 AS bit) NeedsUpgrade,N''Preview'' AS State,@Sql AS ProposedDefinition;
        RETURN;
    END;
    EXEC sys.sp_executesql @Sql;
    COMMIT;
    SELECT CAST(1 AS bit) Changed,CAST(0 AS bit) NeedsUpgrade,N''Updated'' AS State;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
',N'@PREVIEW_ONLY bit',@PREVIEW_ONLY=@PREVIEW_ONLY;
EXEC sys.sp_executesql N'SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @PREVIEW_ONLY IS NULL THROW 51150,N''PREVIEW_ONLY is required.'',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult int;
 EXEC @LockResult=sys.sp_getapplock @Resource=N''ScriptSqly.ConvertedPrintViews'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=15000;
 IF @LockResult<0 THROW 51151,N''Print view repair is already running.'',1;
 DECLARE @Actual nvarchar(max),@Expected nvarchar(max),@Sql nvarchar(max);
 DECLARE @Result TABLE(ViewName sysname,NeedsUpgrade bit,Changed bit);

 IF OBJECT_ID(N''dbo.QSL_INVOICE_PISHFROOSH'',N''V'') IS NULL INSERT @Result VALUES(N''QSL_INVOICE_PISHFROOSH'',0,0);
 ELSE IF COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''sgn1usid'') IS NULL OR COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''sgn2usid'') IS NULL OR COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''sgn3usid'') IS NULL OR COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''EMZA1'') IS NULL OR COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''EMZA2'') IS NULL OR COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''EMZA3'') IS NULL OR COL_LENGTH(N''dbo.QSL_INVOICE_PISHFROOSH'',N''Barcode'') IS NULL
 BEGIN
 SET @Actual=OBJECT_DEFINITION(OBJECT_ID(N''dbo.QSL_INVOICE_PISHFROOSH''));
 SET @Expected=N''CREATE VIEW QSL_INVOICE_PISHFROOSH

 AS



 SELECT     dbo.HEAD_LST.NUMBER, dbo.HEAD_LST.TAG, dbo.HEAD_LST.DATE_N, dbo.HEAD_LST.CUST_NO, dbo.HEAD_LST.ANBAR, dbo.HEAD_LST.NUMBER1,

                       dbo.HEAD_LST.TAH, dbo.HEAD_LST.MAS, dbo.HEAD_LST.VAS, dbo.HEAD_LST.N_S, dbo.HEAD_LST.M_NAGHD, dbo.HEAD_LST.MABL_VAR,

                       dbo.HEAD_LST.MOIN_VAR, dbo.HEAD_LST.MABL_HAV, dbo.HEAD_LST.MOIN_HAV, dbo.HEAD_LST.MABL_HAZ, dbo.HEAD_LST.MOIN_HAZ,

                       dbo.HEAD_LST.TAKHFIF, dbo.HEAD_LST.MOIN_KHF, dbo.HEAD_LST.ANBARF, dbo.HEAD_LST.FNUMCO, dbo.INVO_LST.NUMBER AS INUMBER,

                       dbo.INVO_LST.TAG AS ITAG, dbo.INVO_LST.ANBAR AS IANBAR, dbo.INVO_LST.RADIF, dbo.INVO_LST.CODE, dbo.INVO_LST.MEGH,

                       dbo.INVO_LST.MEGHk, dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.MANDAH, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.FROM_A,

                       dbo.INVO_LST.N_RASID, dbo.INVO_LST.MEGH_R, dbo.INVO_LST.CUST_NO AS ICUST_NO, dbo.CUST_HESAB.NAME, dbo.CUST_HESAB.ADDRESS,

                       dbo.CUST_HESAB.TEL, dbo.TCOD_ANBAR.NAMES, dbo.TCOD_VAHEDS.NAMES AS VNAMES, dbo.HEAD_LST.MOLAH, dbo.CUST_HESAB.hes,

                       ISNULL(dbo.STUF_DEF.NAME, N'''' '''') + N'''' '''' + ISNULL(dbo.INVO_LST.MANDAH, N'''' '''') AS KALA, dbo.INVO_LST.N_KOL, dbo.CUST_HESAB.CODE_E,

                       dbo.CUST_HESAB.ECODE, dbo.CUST_HESAB.PCODE, dbo.CUST_HESAB.IYALAT, dbo.CUST_HESAB.CITY, dbo.CUST_HESAB.MCODEM,

                       dbo.CUST_HESAB.TOZIH, dbo.CUST_HESAB.CUST_COD, dbo.CUST_HESAB.MOBILE, dbo.INVO_LST.IMBAA, dbo.INVO_LST.N_MOIN,

                       dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS MABKBT, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS MABKN,

                       dbo.CUST_HESAB.NAME AS HESAB, dbo.DEPART.DEPNAME, dbo.DEPART.DEPART, dbo.STUF_DEF.N_FANI, ISNULL(dbo.STUF_DEF.NAME, N'''' '''')

                       + N''''-'''' + ISNULL(dbo.HIDVISIT(dbo.INVO_LST.MANDAH), N'''' '''') AS KALA2, dbo.INVO_LST.TKHN

 FROM         dbo.INVO_LST INNER JOIN

                       dbo.TCOD_VAHEDS ON dbo.INVO_LST.VAHED_K = dbo.TCOD_VAHEDS.CODE INNER JOIN

                       dbo.HEAD_LST ON dbo.INVO_LST.NUMBER = dbo.HEAD_LST.NUMBER AND dbo.INVO_LST.TAG = dbo.HEAD_LST.TAG INNER JOIN

                       dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes INNER JOIN

                       dbo.STUF_DEF ON dbo.INVO_LST.CODE = dbo.STUF_DEF.CODE INNER JOIN

                       dbo.TCOD_ANBAR ON dbo.INVO_LST.ANBAR = dbo.TCOD_ANBAR.CODE LEFT OUTER JOIN

                       dbo.DEPART ON dbo.HEAD_LST.DEPATMAN = dbo.DEPART.DEPATMAN

 WHERE     (dbo.HEAD_LST.TAG = 20)'';
 SET @Actual=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@Actual,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''));
 SET @Expected=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@Expected,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''));
 IF @Actual IS NULL OR @Actual<>@Expected THROW 51153,N''Unknown/custom incomplete view: QSL_INVOICE_PISHFROOSH; not overwritten.'',1;
 IF EXISTS(SELECT 1 FROM dbo.SALA_DTL GROUP BY IDD HAVING COUNT_BIG(*)>1) OR EXISTS(SELECT 1 FROM dbo.CUST_HESAB GROUP BY hes HAVING COUNT_BIG(*)>1) THROW 51154,N''Duplicate signature/customer keys; repair refused.'',1;
 SET @Sql=N''CREATE OR ALTER VIEW dbo.[QSL_INVOICE_PISHFROOSH] AS SELECT     dbo.HEAD_LST.NUMBER, dbo.HEAD_LST.TAG, dbo.HEAD_LST.DATE_N, dbo.HEAD_LST.CUST_NO, dbo.HEAD_LST.ANBAR, dbo.HEAD_LST.NUMBER1,

                       dbo.HEAD_LST.TAH, dbo.HEAD_LST.MAS, dbo.HEAD_LST.VAS, dbo.HEAD_LST.N_S, dbo.HEAD_LST.M_NAGHD, dbo.HEAD_LST.MABL_VAR,

                       dbo.HEAD_LST.MOIN_VAR, dbo.HEAD_LST.MABL_HAV, dbo.HEAD_LST.MOIN_HAV, dbo.HEAD_LST.MABL_HAZ, dbo.HEAD_LST.MOIN_HAZ,

                       dbo.HEAD_LST.TAKHFIF, dbo.HEAD_LST.MOIN_KHF, dbo.HEAD_LST.ANBARF, dbo.HEAD_LST.FNUMCO, dbo.INVO_LST.NUMBER AS INUMBER,

                       dbo.INVO_LST.TAG AS ITAG, dbo.INVO_LST.ANBAR AS IANBAR, dbo.INVO_LST.RADIF, dbo.INVO_LST.CODE, dbo.INVO_LST.MEGH,

                       dbo.INVO_LST.MEGHk, dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.MANDAH, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.FROM_A,

                       dbo.INVO_LST.N_RASID, dbo.INVO_LST.MEGH_R, dbo.INVO_LST.CUST_NO AS ICUST_NO, dbo.CUST_HESAB.NAME, dbo.CUST_HESAB.ADDRESS,

                       dbo.CUST_HESAB.TEL, dbo.TCOD_ANBAR.NAMES, dbo.TCOD_VAHEDS.NAMES AS VNAMES, dbo.HEAD_LST.MOLAH, dbo.CUST_HESAB.hes,

                       ISNULL(dbo.STUF_DEF.NAME, N'''' '''') + N'''' '''' + ISNULL(dbo.INVO_LST.MANDAH, N'''' '''') AS KALA, dbo.INVO_LST.N_KOL, dbo.CUST_HESAB.CODE_E,

                       dbo.CUST_HESAB.ECODE, dbo.CUST_HESAB.PCODE, dbo.CUST_HESAB.IYALAT, dbo.CUST_HESAB.CITY, dbo.CUST_HESAB.MCODEM,

                       dbo.CUST_HESAB.TOZIH, dbo.CUST_HESAB.CUST_COD, dbo.CUST_HESAB.MOBILE, dbo.INVO_LST.IMBAA, dbo.INVO_LST.N_MOIN,

                       dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS MABKBT, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS MABKN,

                       dbo.CUST_HESAB.NAME AS HESAB, dbo.DEPART.DEPNAME, dbo.DEPART.DEPART, dbo.STUF_DEF.N_FANI, ISNULL(dbo.STUF_DEF.NAME, N'''' '''')

                       + N''''-'''' + ISNULL(dbo.HIDVISIT(dbo.INVO_LST.MANDAH), N'''' '''') AS KALA2, dbo.INVO_LST.TKHN, dbo.HEAD_LST.sgn1usid, dbo.HEAD_LST.sgn2usid, dbo.HEAD_LST.sgn3usid, RepairSig1.EMZA AS EMZA1, RepairSig2.EMZA AS EMZA2, RepairSig3.EMZA AS EMZA3, dbo.STUF_DEF.Barcode FROM dbo.INVO_LST INNER JOIN

                       dbo.TCOD_VAHEDS ON dbo.INVO_LST.VAHED_K = dbo.TCOD_VAHEDS.CODE INNER JOIN

                       dbo.HEAD_LST ON dbo.INVO_LST.NUMBER = dbo.HEAD_LST.NUMBER AND dbo.INVO_LST.TAG = dbo.HEAD_LST.TAG INNER JOIN

                       dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes INNER JOIN

                       dbo.STUF_DEF ON dbo.INVO_LST.CODE = dbo.STUF_DEF.CODE INNER JOIN

                       dbo.TCOD_ANBAR ON dbo.INVO_LST.ANBAR = dbo.TCOD_ANBAR.CODE LEFT OUTER JOIN

                       dbo.DEPART ON dbo.HEAD_LST.DEPATMAN = dbo.DEPART.DEPATMAN

   LEFT JOIN dbo.SALA_DTL RepairSig1 ON RepairSig1.IDD=dbo.HEAD_LST.sgn1usid  LEFT JOIN dbo.SALA_DTL RepairSig2 ON RepairSig2.IDD=dbo.HEAD_LST.sgn2usid  LEFT JOIN dbo.SALA_DTL RepairSig3 ON RepairSig3.IDD=dbo.HEAD_LST.sgn3usid WHERE     (dbo.HEAD_LST.TAG = 20);'';
 IF @PREVIEW_ONLY=0 EXEC sys.sp_executesql @Sql;
 INSERT @Result VALUES(N''QSL_INVOICE_PISHFROOSH'',1,CASE WHEN @PREVIEW_ONLY=0 THEN 1 ELSE 0 END);
 END
 ELSE INSERT @Result VALUES(N''QSL_INVOICE_PISHFROOSH'',0,0);

 IF OBJECT_ID(N''dbo.Q_LIST_FCT2'',N''V'') IS NULL INSERT @Result VALUES(N''Q_LIST_FCT2'',0,0);
 ELSE IF COL_LENGTH(N''dbo.Q_LIST_FCT2'',N''ADDRESS'') IS NULL OR COL_LENGTH(N''dbo.Q_LIST_FCT2'',N''TEL'') IS NULL
 BEGIN
 SET @Actual=OBJECT_DEFINITION(OBJECT_ID(N''dbo.Q_LIST_FCT2''));
 SET @Expected=N''CREATE view Q_LIST_FCT2 as SELECT     dbo.HEAD_LST.NUMBER, dbo.CUST_HESAB.NAME, SUM(dbo.INVO_LST.MABL_K + dbo.INVO_LST.IMBAA) AS SumOfMABL_K, dbo.HEAD_LST.DATE_N,  dbo.HEAD_LST.TAG, COUNT(dbo.INVO_LST.CODE) AS AGHLAM, dbo.CUST_HESAB.hes, dbo.HEAD_LST.DEPATMAN, dbo.HEAD_LST.SHIFT,    dbo.HEAD_LST.CUST_KIND, dbo.HEAD_LST.USER_NAME, SUM(dbo.INVO_LST.N_MOIN) AS TAKHFIF, SUM(dbo.INVO_LST.IMBAA) AS ARZESH, dbo.CUST_HESAB.ROUTE_NAME , dbo.HEAD_LST.MBAA FROM         dbo.HEAD_LST INNER JOIN   dbo.INVO_LST ON dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG AND dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER INNER JOIN  dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes WHERE     (dbo.HEAD_LST.TAG = 2) OR  (dbo.HEAD_LST.TAG = 14) GROUP BY dbo.HEAD_LST.NUMBER, dbo.CUST_HESAB.NAME, dbo.HEAD_LST.DATE_N, dbo.HEAD_LST.TAG, dbo.CUST_HESAB.hes, dbo.HEAD_LST.DEPATMAN, dbo.HEAD_LST.SHIFT, dbo.HEAD_LST.CUST_KIND, dbo.HEAD_LST.USER_NAME, dbo.CUST_HESAB.ROUTE_NAME, dbo.HEAD_LST.MBAA'';
 SET @Actual=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@Actual,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''));
 SET @Expected=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@Expected,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''));
 IF @Actual IS NULL OR @Actual<>@Expected THROW 51153,N''Unknown/custom incomplete view: Q_LIST_FCT2; not overwritten.'',1;
 IF EXISTS(SELECT 1 FROM dbo.SALA_DTL GROUP BY IDD HAVING COUNT_BIG(*)>1) OR EXISTS(SELECT 1 FROM dbo.CUST_HESAB GROUP BY hes HAVING COUNT_BIG(*)>1) THROW 51154,N''Duplicate signature/customer keys; repair refused.'',1;
 SET @Sql=N''CREATE OR ALTER VIEW dbo.[Q_LIST_FCT2] AS SELECT     dbo.HEAD_LST.NUMBER, dbo.CUST_HESAB.NAME, SUM(dbo.INVO_LST.MABL_K + dbo.INVO_LST.IMBAA) AS SumOfMABL_K, dbo.HEAD_LST.DATE_N,  dbo.HEAD_LST.TAG, COUNT(dbo.INVO_LST.CODE) AS AGHLAM, dbo.CUST_HESAB.hes, dbo.HEAD_LST.DEPATMAN, dbo.HEAD_LST.SHIFT,    dbo.HEAD_LST.CUST_KIND, dbo.HEAD_LST.USER_NAME, SUM(dbo.INVO_LST.N_MOIN) AS TAKHFIF, SUM(dbo.INVO_LST.IMBAA) AS ARZESH, dbo.CUST_HESAB.ROUTE_NAME , dbo.HEAD_LST.MBAA, dbo.CUST_HESAB.ADDRESS, dbo.CUST_HESAB.TEL FROM dbo.HEAD_LST INNER JOIN   dbo.INVO_LST ON dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG AND dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER INNER JOIN  dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes WHERE     (dbo.HEAD_LST.TAG = 2) OR  (dbo.HEAD_LST.TAG = 14) GROUP BY dbo.HEAD_LST.NUMBER, dbo.CUST_HESAB.NAME, dbo.HEAD_LST.DATE_N, dbo.HEAD_LST.TAG, dbo.CUST_HESAB.hes, dbo.HEAD_LST.DEPATMAN, dbo.HEAD_LST.SHIFT, dbo.HEAD_LST.CUST_KIND, dbo.HEAD_LST.USER_NAME, dbo.CUST_HESAB.ROUTE_NAME, dbo.HEAD_LST.MBAA, dbo.CUST_HESAB.ADDRESS, dbo.CUST_HESAB.TEL;'';
 IF @PREVIEW_ONLY=0 EXEC sys.sp_executesql @Sql;
 INSERT @Result VALUES(N''Q_LIST_FCT2'',1,CASE WHEN @PREVIEW_ONLY=0 THEN 1 ELSE 0 END);
 END
 ELSE INSERT @Result VALUES(N''Q_LIST_FCT2'',0,0);

 IF OBJECT_ID(N''dbo.HEAD_BACK'',N''V'') IS NULL INSERT @Result VALUES(N''HEAD_BACK'',0,0);
 ELSE IF COL_LENGTH(N''dbo.HEAD_BACK'',N''SGN1'') IS NULL OR COL_LENGTH(N''dbo.HEAD_BACK'',N''SGN2'') IS NULL OR COL_LENGTH(N''dbo.HEAD_BACK'',N''SGN3'') IS NULL OR COL_LENGTH(N''dbo.HEAD_BACK'',N''SGN1usid'') IS NULL OR COL_LENGTH(N''dbo.HEAD_BACK'',N''sgn2usid'') IS NULL OR COL_LENGTH(N''dbo.HEAD_BACK'',N''sgn3usid'') IS NULL
 BEGIN
 SET @Actual=OBJECT_DEFINITION(OBJECT_ID(N''dbo.HEAD_BACK''));
 SET @Expected=N''CREATE  view [dbo].[HEAD_BACK]
as
SELECT     NUMBER, TAG - 2 AS HTAG, ANBAR, NUMBER1, DATE_N, TAH, MAS, VAS, N_S, CUST_NO, MOLAH, M_NAGHD, MABL_VAR, MOIN_VAR, MABL_HAV,
                      MOIN_HAV, MABL_HAZ, MOIN_HAZ, TAKHFIF, MOIN_KHF, ANBARF, FNUMCO, DEPATMAN, MBAA
FROM         dbo.HEAD_LST
WHERE     (TAG - 2 > 0)'';
 SET @Actual=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@Actual,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''));
 SET @Expected=UPPER(REPLACE(REPLACE(REPLACE(REPLACE(@Expected,N'' '',N''''),NCHAR(9),N''''),NCHAR(10),N''''),NCHAR(13),N''''));
 IF @Actual IS NULL OR @Actual<>@Expected THROW 51153,N''Unknown/custom incomplete view: HEAD_BACK; not overwritten.'',1;
 IF EXISTS(SELECT 1 FROM dbo.SALA_DTL GROUP BY IDD HAVING COUNT_BIG(*)>1) OR EXISTS(SELECT 1 FROM dbo.CUST_HESAB GROUP BY hes HAVING COUNT_BIG(*)>1) THROW 51154,N''Duplicate signature/customer keys; repair refused.'',1;
 SET @Sql=N''CREATE OR ALTER VIEW dbo.[HEAD_BACK] AS SELECT     NUMBER, TAG - 2 AS HTAG, ANBAR, NUMBER1, DATE_N, TAH, MAS, VAS, N_S, CUST_NO, MOLAH, M_NAGHD, MABL_VAR, MOIN_VAR, MABL_HAV,
                      MOIN_HAV, MABL_HAZ, MOIN_HAZ, TAKHFIF, MOIN_KHF, ANBARF, FNUMCO, DEPATMAN, MBAA, dbo.HEAD_LST.[SGN1], dbo.HEAD_LST.[SGN2], dbo.HEAD_LST.[SGN3], dbo.HEAD_LST.[SGN1usid], dbo.HEAD_LST.[sgn2usid], dbo.HEAD_LST.[sgn3usid] FROM dbo.HEAD_LST
WHERE     (TAG - 2 > 0);'';
 IF @PREVIEW_ONLY=0 EXEC sys.sp_executesql @Sql;
 INSERT @Result VALUES(N''HEAD_BACK'',1,CASE WHEN @PREVIEW_ONLY=0 THEN 1 ELSE 0 END);
 END
 ELSE INSERT @Result VALUES(N''HEAD_BACK'',0,0);
 COMMIT;
 SELECT ViewName,NeedsUpgrade,Changed FROM @Result;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
',N'@PREVIEW_ONLY bit',@PREVIEW_ONLY=@PREVIEW_ONLY;
