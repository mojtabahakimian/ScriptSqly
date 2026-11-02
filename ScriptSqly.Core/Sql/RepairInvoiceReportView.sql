DECLARE @PREVIEW_ONLY bit = 1;

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @PREVIEW_ONLY IS NULL THROW 51140, N'حالت پیش‌نمایش باید مشخص باشد.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'ScriptSqly.InvoiceReportView',
        @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
    IF @LockResult<0 THROW 51141,N'نمای چاپ در حال به‌روزرسانی است؛ دوباره تلاش کنید.',1;
    IF OBJECT_ID(N'dbo.HEAD_LST',N'U') IS NULL
        THROW 51142,N'جدول اصلی فاکتور وجود ندارد؛ نمای چاپ تغییر نکرد.',1;

    DECLARE @Columns TABLE (Id int IDENTITY, Name sysname);
    INSERT @Columns(Name) VALUES
        (N'NUMBER'),(N'HTAG'),(N'ANBAR'),(N'NUMBER1'),(N'DATE_N'),(N'TAH'),(N'MAS'),(N'VAS'),
        (N'N_S'),(N'CUST_NO'),(N'MOLAH'),(N'M_NAGHD'),(N'MABL_VAR'),(N'MOIN_VAR'),
        (N'MABL_HAV'),(N'MOIN_HAV'),(N'MABL_HAZ'),(N'MOIN_HAZ'),(N'TAKHFIF'),(N'MOIN_KHF'),
        (N'ANBARF'),(N'FNUMCO'),(N'DEPATMAN'),(N'SHIFT'),(N'CUST_KIND'),(N'USER_NAME'),
        (N'MBAA'),(N'SADER'),(N'SHARAYET'),(N'TAG'),
        (N'SGN1usid'),(N'sgn2usid'),(N'sgn3usid'),(N'SGN1'),(N'SGN2'),(N'SGN3');
    IF EXISTS(SELECT 1 FROM @Columns WHERE Name<>N'HTAG' AND COL_LENGTH(N'dbo.HEAD_LST',Name) IS NULL)
        THROW 51143,N'ستون‌های موردنیاز فاکتور کامل نیستند؛ ابتدا ساختار اصلی را ارتقا دهید.',1;

    DECLARE @ViewId int=OBJECT_ID(N'dbo.HEAD_BACK_ANBAR');
    IF @ViewId IS NOT NULL AND OBJECT_ID(N'dbo.HEAD_BACK_ANBAR',N'V') IS NULL
        THROW 51144,N'شیء هم‌نام نمای چاپ وجود دارد؛ تغییر خودکار انجام نشد.',1;
    IF @ViewId IS NOT NULL AND NOT EXISTS
        (SELECT 1 FROM @Columns x WHERE NOT EXISTS
            (SELECT 1 FROM sys.columns c WHERE c.object_id=@ViewId AND c.name=x.Name))
    BEGIN
        COMMIT;
        SELECT CAST(0 AS bit) Changed,CAST(0 AS bit) NeedsUpgrade,N'AlreadyCurrent' AS State;
        RETURN;
    END;

    DECLARE @Definition nvarchar(max),@Actual nvarchar(max),@Expected nvarchar(max),@Projection nvarchar(max);
    IF @ViewId IS NOT NULL
    BEGIN
        IF EXISTS(SELECT 1 FROM sys.sql_modules WHERE object_id=@ViewId AND is_schema_bound=1)
            THROW 51145,N'نمای چاپ وابستگی ساختاری ویژه دارد؛ تغییر خودکار انجام نشد.',1;
        IF EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=@ViewId AND NOT EXISTS
            (SELECT 1 FROM @Columns x WHERE x.Name=c.name))
            THROW 51146,N'نمای چاپ ستون سفارشی دارد؛ تغییر خودکار انجام نشد.',1;
        IF EXISTS(SELECT 1 FROM @Columns x WHERE x.Id<=30 AND NOT EXISTS
            (SELECT 1 FROM sys.columns c WHERE c.object_id=@ViewId AND c.name=x.Name))
            THROW 51147,N'ساختار پایه نمای چاپ ناشناخته است؛ تغییر خودکار انجام نشد.',1;
        SELECT @Projection=STUFF((SELECT N','+CASE WHEN name=N'HTAG'
            THEN N'TAG-11 AS HTAG' ELSE QUOTENAME(name) END
            FROM sys.columns WHERE object_id=@ViewId ORDER BY column_id
            FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'');
        SET @Expected=N'SELECT'+@Projection+N'FROMHEAD_LSTWHERE(TAG-11>0)';
        SET @Definition=OBJECT_DEFINITION(@ViewId);
        IF @Definition IS NULL THROW 51148,N'تعریف نمای چاپ قابل خواندن نیست؛ تغییر انجام نشد.',1;
        SET @Actual=UPPER(@Definition);
        SET @Expected=UPPER(@Expected);
        SET @Actual=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@Actual,N' ',N''),NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N''),N'[',N''),N']',N'');
        SET @Expected=REPLACE(REPLACE(REPLACE(@Expected,N' ',N''),N'[',N''),N']',N'');
        SET @Actual=REPLACE(REPLACE(@Actual,N'DBO.',N''),N'HEAD_LST.',N'');
        IF CHARINDEX(N'ASSELECT',@Actual)=0 THROW 51149,N'تعریف نمای چاپ ناشناخته است؛ تغییر خودکار انجام نشد.',1;
        SET @Actual=SUBSTRING(@Actual,CHARINDEX(N'ASSELECT',@Actual)+2,LEN(@Actual));
        WHILE RIGHT(@Actual,1)=N';' SET @Actual=LEFT(@Actual,LEN(@Actual)-1);
        IF @Actual<>@Expected THROW 51149,N'فیلتر یا محاسبه نمای چاپ سفارشی است؛ تغییر خودکار انجام نشد.',1;
    END;

    SELECT @Projection=STUFF((SELECT N','+CASE WHEN x.Name=N'HTAG'
        THEN N'[TAG]-11 AS [HTAG]' ELSE QUOTENAME(x.Name) END
        FROM @Columns x LEFT JOIN sys.columns c ON c.object_id=@ViewId AND c.name=x.Name
        ORDER BY COALESCE(c.column_id,1000+x.Id)
        FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'');
    DECLARE @Sql nvarchar(max)=N'CREATE OR ALTER VIEW dbo.HEAD_BACK_ANBAR AS SELECT '+@Projection+
        N' FROM dbo.HEAD_LST WHERE (TAG-11>0);';
    IF @PREVIEW_ONLY=1
    BEGIN
        COMMIT;
        SELECT CAST(0 AS bit) Changed,CAST(1 AS bit) NeedsUpgrade,N'Preview' AS State,@Sql AS ProposedDefinition;
        RETURN;
    END;
    EXEC sys.sp_executesql @Sql;
    COMMIT;
    SELECT CAST(1 AS bit) Changed,CAST(0 AS bit) NeedsUpgrade,N'Updated' AS State;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
