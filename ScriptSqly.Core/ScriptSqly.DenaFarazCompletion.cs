namespace ScriptSqly.Migrations;

public static partial class ScriptSqly
{
    // Read first, then run again with the exact approved code to clear it.
    // No schema change; called only by the explicit WPF conversion workflow.
    public const string DenaFarazCommissionAccountSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF (SELECT COUNT(*) FROM dbo.SAZMAN WITH (UPDLOCK,HOLDLOCK))<>1
        THROW 51029,N'تنظیمات سازمان یکتا نیست؛ حساب پورسانت تغییر نکرد.',1;
    DECLARE @Code nvarchar(100),@Invalid bit=0,@Cleared bit=0,@Reason nvarchar(300);
    SELECT @Code=NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),HPOR))),N'') FROM dbo.SAZMAN;
    IF @CLEAR_CONFIRMED=1 AND (@Code IS NULL OR @EXPECTED_CODE IS NULL OR @Code<>@EXPECTED_CODE)
        THROW 51030,N'حساب پورسانت پس از پرسش تغییر کرده است؛ تبدیل را دوباره اجرا کنید.',1;
    IF @Code IS NOT NULL AND @Code<>N'0'
        AND NOT EXISTS (SELECT 1 FROM dbo.CUST_HESAB WITH(HOLDLOCK) WHERE hes=@Code)
        SET @Invalid=1;

    IF @Invalid=1
    BEGIN
        -- Names are a fixed allowlist. Optional legacy history columns are
        -- checked through metadata before a statement is compiled.
        DECLARE @Checks TABLE (Id int IDENTITY,TableName sysname,C1 sysname NULL,C2 sysname NULL,
            C3 sysname NULL,Mode int,Description nvarchar(300));
        INSERT @Checks(TableName,C1,C2,C3,Mode,Description) VALUES
        (N'VISITOR_DTL',NULL,NULL,NULL,0,N'اطلاعات پورسانت فاکتورها موجود است.'),
        (N'TR_VISITOR_DTL',NULL,NULL,NULL,0,N'سابقهٔ پورسانت فاکتورها موجود است.'),
        (N'VISITORS_PORSANT',NULL,NULL,NULL,0,N'الگوی پورسانت تعریف شده است.'),
        (N'VISITORS_PORSANT_KALA',NULL,NULL,NULL,0,N'الگوی پورسانت کالا تعریف شده است.'),
        (N'DEED_DTL',N'HES',NULL,NULL,1,N'این حساب در اسناد حسابداری مصرف شده است.'),
        (N'TR_DEED_DTL',N'HES',NULL,NULL,1,N'این حساب در سابقهٔ اسناد حسابداری مصرف شده است.'),
        (N'DEED_DTL',N'HES_K',N'HES_M',N'HES_T',2,N'اجزای این حساب در اسناد حسابداری مصرف شده است.'),
        (N'TR_DEED_DTL',N'HES_K',N'HES_M',N'HES_T',2,N'اجزای این حساب در سابقهٔ اسناد مصرف شده است.'),
        (N'PGET_LST',N'FHES',NULL,NULL,1,N'این حساب در خزانه مصرف شده است.'),
        (N'PGET_LST',N'THES',NULL,NULL,1,N'این حساب در خزانه مصرف شده است.'),
        (N'TR_PGET_LST',N'FHES',NULL,NULL,1,N'این حساب در سابقهٔ خزانه مصرف شده است.'),
        (N'TR_PGET_LST',N'THES',NULL,NULL,1,N'این حساب در سابقهٔ خزانه مصرف شده است.'),
        (N'PGET_LST',N'FHES_K',N'FHES_M',N'FHES_T',2,N'اجزای این حساب در خزانه مصرف شده است.'),
        (N'PGET_LST',N'THES_K',N'THES_M',N'THES_T',2,N'اجزای این حساب در خزانه مصرف شده است.'),
        (N'TR_PGET_LST',N'FHES_K',N'FHES_M',N'FHES_T',2,N'اجزای این حساب در سابقهٔ خزانه مصرف شده است.'),
        (N'TR_PGET_LST',N'THES_K',N'THES_M',N'THES_T',2,N'اجزای این حساب در سابقهٔ خزانه مصرف شده است.'),
        (N'DEED_DTL',N'SHARH',NULL,NULL,3,N'در شرح اسناد نشانهٔ پورسانت وجود دارد.'),
        (N'TR_DEED_DTL',N'SHARH',NULL,NULL,3,N'در شرح سابقهٔ اسناد نشانهٔ پورسانت وجود دارد.'),
        (N'DEED_HED',N'SHARH_S',NULL,NULL,3,N'در شرح سند نشانهٔ پورسانت وجود دارد.'),
        (N'TR_DEED_HED',N'SHARH_S',NULL,NULL,3,N'در شرح سابقهٔ سند نشانهٔ پورسانت وجود دارد.'),
        (N'PGET_LST',N'SHARH',NULL,NULL,3,N'در شرح خزانه نشانهٔ پورسانت وجود دارد.'),
        (N'TR_PGET_LST',N'SHARH',NULL,NULL,3,N'در شرح سابقهٔ خزانه نشانهٔ پورسانت وجود دارد.'),
        (N'HEAD_LST',N'MOLAH',NULL,NULL,4,N'در ملاحظات فاکتور نشانهٔ پورسانت وجود دارد.'),
        (N'TR_HEAD_LST',N'MOLAH',NULL,NULL,4,N'در سابقهٔ ملاحظات فاکتور نشانهٔ پورسانت وجود دارد.'),
        (N'INVO_LST',N'MANDAH',NULL,NULL,4,N'در توضیح ردیف کالا نشانهٔ پورسانت وجود دارد.'),
        (N'TR_INVO_LST',N'MANDAH',NULL,NULL,4,N'در سابقهٔ ردیف کالا نشانهٔ پورسانت وجود دارد.'),
        (N'INVO_LST',N'VISITOR',NULL,NULL,5,N'برای ردیف کالا ویزیتور تعیین شده است.'),
        (N'TR_INVO_LST',N'VISITOR',NULL,NULL,5,N'در سابقهٔ ردیف کالا ویزیتور تعیین شده است.');
        DECLARE @Id int=0,@Table sysname,@C1 sysname,@C2 sysname,@C3 sysname,@Mode int,
            @Description nvarchar(300),@Value nvarchar(max),@Predicate nvarchar(max),@Sql nvarchar(max),@Found bit;
        WHILE @Reason IS NULL
        BEGIN
            SELECT @Id=MIN(Id) FROM @Checks WHERE Id>@Id;
            IF @Id IS NULL BREAK;
            SELECT @Table=TableName,@C1=C1,@C2=C2,@C3=C3,@Mode=Mode,@Description=Description
                FROM @Checks WHERE Id=@Id;
            IF OBJECT_ID(N'dbo.'+QUOTENAME(@Table),N'U') IS NULL CONTINUE;
            IF @Mode<>0 AND COL_LENGTH(N'dbo.'+QUOTENAME(@Table),@C1) IS NULL CONTINUE;
            IF @Mode=2 AND (COL_LENGTH(N'dbo.'+QUOTENAME(@Table),@C2) IS NULL
                OR COL_LENGTH(N'dbo.'+QUOTENAME(@Table),@C3) IS NULL) CONTINUE;
            SET @Value=N'LTRIM(RTRIM(CONVERT(nvarchar(max),'+QUOTENAME(@C1)+N')))';
            IF @Mode=2 SET @Value=N'CONCAT(CONVERT(bigint,'+QUOTENAME(@C1)+N'),N''-'',CONVERT(bigint,'+
                QUOTENAME(@C2)+N'),N''-'',CONVERT(bigint,'+QUOTENAME(@C3)+N'))';
            SET @Predicate=N'1=1';
            IF @Mode IN(1,2) SET @Predicate=@Value+N'=@Code OR LEFT('+@Value+N',LEN(@Code)+1)=@Code+N''-'''+
                N' OR LEFT(@Code,LEN('+@Value+N')+1)='+@Value+N'+N''-''';
            IF @Mode IN(3,4)
            BEGIN
                SET @Value=N'REPLACE(REPLACE(REPLACE(REPLACE('+@Value+N',N''ي'',N''ی''),N''ك'',N''ک''),N'' '',N''''),NCHAR(8204),N'''')';
                SET @Predicate=@Value+N' LIKE N''%پورسان%'' OR '+@Value+N' LIKE N''%پرسانت%'' OR '+@Value+
                    N' LIKE N''%پورسنت%'' OR '+@Value+N' LIKE N''%پرسنت%'' OR '+@Value+N' LIKE N''%درصدسهم%'' OR '+
                    @Value+N' LIKE N''%کمیسیون%'' OR '+@Value+N' LIKE N''%حقالعمل%''';
                IF @Mode=4 SET @Predicate=@Predicate+N' OR (LEFT('+@Value+N',1) IN(N''~'',N''.'') AND LEN('+@Value+N')>1)';
            END;
            IF @Mode=5 SET @Predicate=N'NULLIF('+@Value+N',N'''') IS NOT NULL AND '+@Value+N'<>N''0''';
            SET @Found=0;
            SET @Sql=N'IF EXISTS(SELECT 1 FROM dbo.'+QUOTENAME(@Table)+N' WITH(HOLDLOCK) WHERE '+@Predicate+
                N') SET @Found=1;';
            EXEC sys.sp_executesql @Sql,N'@Code nvarchar(100),@Found bit OUTPUT',@Code=@Code,@Found=@Found OUTPUT;
            IF @Found=1 SET @Reason=@Description;
        END;
        IF @CLEAR_CONFIRMED=1
        BEGIN
            IF @Reason IS NOT NULL
                THROW 51031,N'نشانهٔ مصرف پورسانت وجود دارد؛ حساب خالی نشد. حساب صحیح را در حساب‌های خودگردان تعیین کنید.',1;
            UPDATE dbo.SAZMAN SET HPOR=NULL WHERE LTRIM(RTRIM(HPOR))=@EXPECTED_CODE;
            IF @@ROWCOUNT<>1 THROW 51030,N'حساب پورسانت تغییر کرده است؛ تبدیل را دوباره اجرا کنید.',1;
            SET @Cleared=1;
        END;
    END;
    ELSE IF @CLEAR_CONFIRMED=1
        THROW 51030,N'حساب پورسانت اکنون معتبر است؛ خالی نشد. تبدیل را دوباره اجرا کنید.',1;
    COMMIT TRANSACTION;
    SELECT @Code AS Code,@Invalid AS IsInvalid,@Reason AS UsageReason,@Cleared AS Cleared;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;";

    // TR_ snapshots are written by CL_HESABDARI.TR, not database triggers.
    // Identifiers below come only from this allowlist and SQL Server metadata.
    public const string DenaFarazHistorySql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Sources TABLE (Name sysname PRIMARY KEY);
INSERT @Sources VALUES
(N'HEAD_LST'),(N'INVO_LST'),(N'DEED_HED'),(N'DEED_DTL'),
(N'PGET_HED'),(N'PGET_LST'),(N'PAY_GETD'),(N'PAY_GETP'),
(N'STUF_DEF'),(N'STUF_FSK'),(N'MODULE_D'),(N'TAKHPERS'),(N'RewardRules'),
(N'ANBGRD_HEAD'),(N'ANBGRD_LST'),(N'SAZMAN'),(N'AMVAL'),
(N'TOTA_HES'),(N'DETA_HES'),(N'TDETA_HES'),(N'TDETA_HES2'),(N'TDETA_HES3'),(N'TDETA_HES4'),
(N'PRICE_ELAMIE'),(N'PRICE_ELAMIE_DTL'),(N'PRICE_ELAMIETF'),(N'PRICE_ELAMIETF_DTL'),
(N'TAKHFIF_APLAY'),(N'TAKHFIF_DEF'),(N'TAKHFIF_DEF_DTL'),(N'CUSTKIND_TF'),
(N'HEAD_MANF'),(N'DTL_MANF'),(N'OTHER_DTL'),(N'VISITOR_DTL'),
(N'TASKS'),(N'EVENTS'),(N'CHREC_HP'),(N'CHRE_LSP'),(N'CHREC_LSP'),(N'payorder'),
(N'WORKHEAD'),(N'WORKING'),(N'PERSONEL'),(N'PVAM'),(N'PVAM_BAZ'),
(N'PHOKM'),(N'PMORAKH'),(N'MEHMAN'),(N'MEHMAN_CHARJ');

IF @PREVIEW_ONLY = 1
BEGIN
    SELECT S.Name AS SourceTable, N'TR_' + S.Name AS HistoryTable,
        CASE WHEN OBJECT_ID(N'dbo.' + QUOTENAME(S.Name), N'U') IS NULL THEN N'Source absent; skipped'
             WHEN OBJECT_ID(N'dbo.' + QUOTENAME(N'TR_' + S.Name), N'U') IS NULL THEN N'Create empty history table'
             ELSE N'Check missing columns and history metadata' END AS Action
    FROM @Sources S ORDER BY S.Name;
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock = sys.sp_getapplock @Resource=N'MrCorrect.DenaFaraz.Completion',
        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @lock < 0 THROW 51020, N'تکمیل ساختار تبدیل دیگری در حال اجراست.', 1;

    DECLARE @Source sysname, @History sysname, @SourceId int, @HistoryId int,
        @Column sysname, @Definition nvarchar(max), @Sql nvarchar(max), @Exists bit,
        @IdentityColumn sysname, @LegacyIdentity sysname, @QualifiedColumn nvarchar(776);
    DECLARE tables_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT Name FROM @Sources WHERE OBJECT_ID(N'dbo.' + QUOTENAME(Name), N'U') IS NOT NULL;
    OPEN tables_cur;
    FETCH NEXT FROM tables_cur INTO @Source;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @History = N'TR_' + @Source;
        SET @SourceId = OBJECT_ID(N'dbo.' + QUOTENAME(@Source), N'U');
        SET @HistoryId = OBJECT_ID(N'dbo.' + QUOTENAME(@History), N'U');
        IF @HistoryId IS NULL
        BEGIN
            SET @Sql = N'CREATE TABLE dbo.' + QUOTENAME(@History) +
                N' ([TRIDD] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [UP_DATE] bigint NULL, [UP_TIME] float NULL,
                    [UP_USER_NAME] nvarchar(40) NULL, [PC_NAME] nvarchar(50) NULL, [IPADD] nvarchar(50) NULL);';
            EXEC sys.sp_executesql @Sql;
            SET @HistoryId = OBJECT_ID(N'dbo.' + QUOTENAME(@History), N'U');
        END;

        -- A legacy history identity named like a source field (e.g. INVO_LST.id)
        -- rejects the explicit source value used by CL_HESABDARI.TR. Keep that
        -- identity and its indexes under a reserved name, then materialize the
        -- original field below. No history table is dropped or renamed.
        SET @IdentityColumn=NULL;
        SELECT @IdentityColumn=H.name FROM sys.identity_columns H
        WHERE H.object_id=@HistoryId AND H.name<>N'TRIDD'
          AND EXISTS (SELECT 1 FROM sys.columns C WHERE C.object_id=@SourceId AND C.name=H.name);
        IF @IdentityColumn IS NOT NULL
        BEGIN
            SET @LegacyIdentity=N'__DENAF_IDENTITY_' + @IdentityColumn;
            IF LEN(@IdentityColumn)>100 OR EXISTS
                (SELECT 1 FROM sys.columns WHERE object_id=@HistoryId AND name=@LegacyIdentity)
                THROW 51021, N'نام ستون نگهداری identity سابقه تداخل دارد؛ تغییرات برگشت داده شد.', 1;
            SET @QualifiedColumn=N'dbo.' + QUOTENAME(@History) + N'.' + QUOTENAME(@IdentityColumn);
            EXEC sys.sp_rename @objname=@QualifiedColumn,@newname=@LegacyIdentity,@objtype=N'COLUMN';
        END;

        -- Source identity/computed columns become ordinary nullable snapshot values.
        -- Never copy source PK/FK/default constraints into a history table.
        DECLARE columns_cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT C.name,
                QUOTENAME(C.name) + N' ' +
                CASE WHEN T.name IN (N'timestamp', N'rowversion') THEN N'binary(8)'
                     WHEN T.is_user_defined=1 THEN QUOTENAME(SCHEMA_NAME(T.schema_id)) + N'.' + QUOTENAME(T.name)
                     ELSE QUOTENAME(T.name) +
                        CASE WHEN T.name IN (N'varchar',N'char',N'varbinary',N'binary')
                                  THEN N'(' + CASE WHEN C.max_length=-1 THEN N'max' ELSE CONVERT(nvarchar(10),C.max_length) END + N')'
                             WHEN T.name IN (N'nvarchar',N'nchar')
                                  THEN N'(' + CASE WHEN C.max_length=-1 THEN N'max' ELSE CONVERT(nvarchar(10),C.max_length/2) END + N')'
                             WHEN T.name IN (N'decimal',N'numeric')
                                  THEN N'(' + CONVERT(nvarchar(10),C.precision) + N',' + CONVERT(nvarchar(10),C.scale) + N')'
                             WHEN T.name IN (N'datetime2',N'datetimeoffset',N'time')
                                  THEN N'(' + CONVERT(nvarchar(10),C.scale) + N')'
                             WHEN T.name=N'float' THEN N'(' + CONVERT(nvarchar(10),C.precision) + N')'
                             ELSE N'' END END +
                CASE WHEN C.collation_name IS NOT NULL THEN N' COLLATE ' + C.collation_name ELSE N'' END + N' NULL',
                CONVERT(bit, CASE WHEN H.column_id IS NULL THEN 0 ELSE 1 END)
            FROM sys.columns C JOIN sys.types T ON T.user_type_id=C.user_type_id
            LEFT JOIN sys.columns H ON H.object_id=@HistoryId AND H.name=C.name
            WHERE C.object_id=@SourceId AND C.name NOT IN
                (N'TRIDD',N'UP_DATE',N'UP_TIME',N'UP_USER_NAME',N'PC_NAME',N'IPADD')
              AND (H.column_id IS NULL
                OR (C.user_type_id=H.user_type_id AND T.name IN (N'nvarchar',N'varchar',N'varbinary')
                    AND H.max_length<>-1 AND (C.max_length=-1 OR C.max_length>H.max_length))
                OR (T.name=N'bigint' AND H.system_type_id IN (48,52,56))
                OR (T.name=N'int' AND H.system_type_id IN (48,52))
                OR (T.name=N'nvarchar' AND H.system_type_id IN (48,52,56,127)
                    AND (C.max_length=-1 OR C.max_length>=40)))
            ORDER BY C.column_id;
        OPEN columns_cur;
        FETCH NEXT FROM columns_cur INTO @Column,@Definition,@Exists;
        WHILE @@FETCH_STATUS=0
        BEGIN
            SET @Sql=N'ALTER TABLE dbo.' + QUOTENAME(@History) +
                CASE WHEN @Exists=1 THEN N' ALTER COLUMN ' ELSE N' ADD ' END + @Definition + N';';
            EXEC sys.sp_executesql @Sql;
            FETCH NEXT FROM columns_cur INTO @Column,@Definition,@Exists;
        END;
        CLOSE columns_cur;
        DEALLOCATE columns_cur;
        IF @IdentityColumn IS NOT NULL
        BEGIN
            SET @Sql=N'UPDATE dbo.' + QUOTENAME(@History) + N' SET ' + QUOTENAME(@IdentityColumn) +
                N'=' + QUOTENAME(@LegacyIdentity) + N';';
            EXEC sys.sp_executesql @Sql;
        END;

        -- Older DenaFaraz history tables often omit the snapshot identity/metadata.
        DECLARE metadata_cur CURSOR LOCAL FAST_FORWARD FOR
            SELECT Name, Definition FROM (VALUES
                (N'UP_DATE',N'[UP_DATE] bigint NULL'),
                (N'UP_TIME',N'[UP_TIME] float NULL'),
                (N'UP_USER_NAME',N'[UP_USER_NAME] nvarchar(40) NULL'),
                (N'PC_NAME',N'[PC_NAME] nvarchar(50) NULL'),
                (N'IPADD',N'[IPADD] nvarchar(50) NULL'),
                (N'TRIDD',N'[TRIDD] bigint IDENTITY(1,1) NOT NULL')) M(Name,Definition)
            WHERE NOT EXISTS (SELECT 1 FROM sys.columns C WHERE C.object_id=@HistoryId AND C.name=M.Name)
              AND (M.Name<>N'TRIDD' OR NOT EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id=@HistoryId));
        OPEN metadata_cur;
        FETCH NEXT FROM metadata_cur INTO @Column,@Definition;
        WHILE @@FETCH_STATUS=0
        BEGIN
            SET @Sql=N'ALTER TABLE dbo.' + QUOTENAME(@History) + N' ADD ' + @Definition + N';';
            EXEC sys.sp_executesql @Sql;
            FETCH NEXT FROM metadata_cur INTO @Column,@Definition;
        END;
        CLOSE metadata_cur;
        DEALLOCATE metadata_cur;
        FETCH NEXT FROM tables_cur INTO @Source;
    END;
    CLOSE tables_cur;
    DEALLOCATE tables_cur;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;";

    public const string DenaFarazAccountsSql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @PREVIEW_ONLY=1
BEGIN
    SELECT N'تکمیل حساب‌های خودگردان و فعال‌سازی صدور آرتیکل اسناد صنعتی؛ کدهای معتبر و اسناد قبلی حفظ می‌شوند.' AS Preview;
    RETURN;
END;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lock int;
    EXEC @lock=sys.sp_getapplock @Resource=N'MrCorrect.DenaFaraz.Completion',
        @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @lock<0 THROW 51020, N'تکمیل ساختار تبدیل دیگری در حال اجراست.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.SAZMAN)
        THROW 51022, N'تنظیمات سازمان موجود نیست؛ حساب‌های خودگردان تعیین نشد.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER=411)
        THROW 51023, N'حساب خرید 411 موجود نیست؛ اصلاح کدینگ متوقف شد.', 1;
    -- Preferred numbers are optional: reuse a configured/existing role instead
    -- of duplicating it (Bandar already has the cost-of-sales account at 714).
    DECLARE @Contra int, @Cost int;
    SELECT @Contra=MIN(T.NUMBER) FROM dbo.SAZMAN S
        JOIN dbo.TOTA_HES T ON T.NUMBER=S.PKHARID WHERE S.PKHARID>0;
    SELECT @Cost=MIN(T.NUMBER) FROM dbo.SAZMAN S
        JOIN dbo.TOTA_HES T ON T.NUMBER=S.GHEYMAT WHERE S.GHEYMAT>0;
    IF @Contra IS NULL
        SELECT @Contra=MIN(NUMBER) FROM dbo.TOTA_HES
        WHERE LTRIM(RTRIM(REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک')))
            IN (N'پایاپای خرید',N'پایاپای حسابهای خرید');
    IF @Cost IS NULL
        SELECT @Cost=MIN(NUMBER) FROM dbo.TOTA_HES
        WHERE LTRIM(RTRIM(REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک')))
            =N'قیمت تمام شده کالای فروش رفته';
    SET @Contra=COALESCE(@Contra,410);
    SET @Cost=COALESCE(@Cost,761);
    -- Do not silently reuse an occupied preferred code for an unrelated account.
    IF @Contra=410 AND EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER=410
               AND REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک') NOT LIKE N'%پایاپای%')
        THROW 51024, N'کد 410 برای حساب دیگری استفاده شده است؛ نیاز به بررسی کدینگ دارد.', 1;
    IF @Cost=761 AND EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER=761
               AND REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک') NOT LIKE N'%تمام%شده%')
        THROW 51025, N'کد 761 برای حساب دیگری استفاده شده است؛ نیاز به بررسی کدینگ دارد.', 1;

    -- Do not inherit legacy purchase expense classification (e.g. Bandar 41/1).
    -- Control/contra accounts use the verified MrCorrect self-balancing 91/4/2.
    INSERT dbo.TOTA_HES (NUMBER,NAME,[GROUP],NO_HES,M_D)
    SELECT @Contra,N'پایاپای خرید',91,4,2
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER=@Contra);
    -- Values verified against the working MrCorrect 761 account.
    INSERT dbo.TOTA_HES (NUMBER,NAME,[GROUP],NO_HES,M_D)
    SELECT @Cost,N'قیمت تمام شده کالای فروش رفته',71,1,2
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES WHERE NUMBER=@Cost);
    UPDATE dbo.TOTA_HES SET [GROUP]=91,NO_HES=4,M_D=2 WHERE NUMBER=@Contra
        AND LTRIM(RTRIM(REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک')))
            IN (N'پایاپای خرید',N'پایاپای حسابهای خرید');
    UPDATE dbo.TOTA_HES SET NAME=N'کنترل خرید',[GROUP]=91,NO_HES=4,M_D=2
      WHERE NUMBER=411 AND LTRIM(RTRIM(REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک'))) IN (N'خرید',N'کنترل خرید');

    -- Existing valid custom codes remain valid. Repair dangling settings only.
    UPDATE S SET PKHARID=@Contra FROM dbo.SAZMAN S
      WHERE S.PKHARID IS NULL OR S.PKHARID<=0
         OR NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES T WHERE T.NUMBER=S.PKHARID);
    UPDATE S SET GHEYMAT=@Cost FROM dbo.SAZMAN S
      WHERE S.GHEYMAT IS NULL OR S.GHEYMAT<=0
         OR NOT EXISTS (SELECT 1 FROM dbo.TOTA_HES T WHERE T.NUMBER=S.GHEYMAT);

    INSERT dbo.DETA_HES (N_KOL,NUMBER,NAME)
    SELECT @Contra,1,N'پایاپای خرید'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DETA_HES WHERE N_KOL=@Contra AND NUMBER=1);
    INSERT dbo.TDETA_HES (N_KOL,NUMBER,TNUMBER,NAME)
    SELECT @Contra,1,1,N'پایاپای خرید'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TDETA_HES WHERE N_KOL=@Contra AND NUMBER=1 AND TNUMBER=1);

    -- Completing only purchase/cost-of-sales leaves the other self-balancing
    -- settings dangling in a periodic-inventory DenaFaraz database.
    -- Roles come from the legacy AUTOMATIC form; classifications were checked
    -- against the working MrCorrect chart. Customer-specific names are not copied.
    DECLARE @Roles TABLE(FieldName sysname PRIMARY KEY, DefaultCode int, AccountName nvarchar(100),
        GroupCode int, AccountType int, Nature int);
    INSERT @Roles VALUES
        (N'MOGODIA',121,N'موجودی مواد و کالا',11,1,1),
        (N'HKHARID',722,N'هزینه های خرید',71,1,2),
        (N'HDARAM',611,N'تخفیفات درآمد',61,3,2),
        (N'HAZ_TOL',741,N'کنترل مواد مصرفی تولید',71,3,2),
        (N'PJHAZ_TOL1',731,N'پایاپای هزینه های تولید و خدمات',71,2,2),
        (N'PHAZ_TOL',744,N'جذب هزینه های تولید',71,2,2),
        (N'PPDAST',742,N'کنترل دستمزد',71,3,2),
        (N'PPSAR',743,N'کنترل سربار تولید',71,3,2),
        (N'AMALKARD',771,N'عملکرد و انحراف هزینه ها',71,3,2),
        (N'CONKAL',751,N'کنترل کالای در جریان ساخت',71,3,2);
    DECLARE @Field sysname,@Default int,@Name nvarchar(100),@Group int,@Type int,@Nature int,
        @Previous int,@Resolved int,@Command nvarchar(max),@Message nvarchar(2048);
    DECLARE roles_cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT FieldName,DefaultCode,AccountName,GroupCode,AccountType,Nature FROM @Roles;
    OPEN roles_cur;
    FETCH NEXT FROM roles_cur INTO @Field,@Default,@Name,@Group,@Type,@Nature;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @Previous=NULL;
        -- Only a dangling, explicitly assigned setting is repaired. Null/zero
        -- optional settings and valid customized account numbers are preserved.
        SET @Command=N'SELECT @Code=MIN(CONVERT(int,S.' + QUOTENAME(@Field) + N')) FROM dbo.SAZMAN S
            WHERE S.' + QUOTENAME(@Field) + N'>0 AND NOT EXISTS
                (SELECT 1 FROM dbo.TOTA_HES T WHERE T.NUMBER=S.' + QUOTENAME(@Field) + N');';
        EXEC sys.sp_executesql @Command,N'@Code int OUTPUT',@Code=@Previous OUTPUT;
        IF @Previous IS NOT NULL
        BEGIN
            IF @Previous<>@Default
            BEGIN
                SET @Message=N'حساب تنظیم ' + @Field + N' با کد ' + CONVERT(nvarchar(20),@Previous) +
                    N' در کدینگ موجود نیست و کد استاندارد تبدیل هم نیست؛ کدینگ بررسی شود.';
                THROW 51026,@Message,1;
            END;
            SET @Resolved=NULL;
            SELECT @Resolved=MIN(NUMBER) FROM dbo.TOTA_HES
            WHERE LTRIM(RTRIM(REPLACE(REPLACE(NAME,N'ي',N'ی'),N'ك',N'ک')))=@Name;
            IF @Resolved IS NULL
            BEGIN
                SET @Resolved=@Default;
                INSERT dbo.TOTA_HES(NUMBER,NAME,[GROUP],NO_HES,M_D)
                    VALUES(@Resolved,@Name,@Group,@Type,@Nature);
            END;
            -- Reuse an existing equivalent role (Yazd purchase expense is 414),
            -- rather than creating a duplicate name under the legacy default 722.
            SET @Command=N'UPDATE S SET ' + QUOTENAME(@Field) + N'=@Code FROM dbo.SAZMAN S
                WHERE S.' + QUOTENAME(@Field) + N'=@Previous AND NOT EXISTS
                    (SELECT 1 FROM dbo.TOTA_HES T WHERE T.NUMBER=S.' + QUOTENAME(@Field) + N');';
            EXEC sys.sp_executesql @Command,N'@Code int,@Previous int',@Code=@Resolved,@Previous=@Previous;
        END;
        FETCH NEXT FROM roles_cur INTO @Field,@Default,@Name,@Group,@Type,@Nature;
    END;
    CLOSE roles_cur;
    DEALLOCATE roles_cur;

    -- Do not report conversion success while another assigned root/detail is
    -- unresolved. Values are read as data; only the fixed role allowlist above
    -- is used to construct SQL identifiers.
    SET @Message=NULL;
    SELECT TOP(1) @Message=N'حساب تنظیم ' + J.FieldName + N' با مقدار ' + CONVERT(nvarchar(40),J.Code) + N' در کدینگ موجود نیست.'
    FROM dbo.SAZMAN S CROSS APPLY (VALUES
        (N'SANDOGH',S.SANDOGH),(N'BANKHA',S.BANKHA),(N'BESTANKAR',S.BESTANKAR),(N'BEDEHKAR',S.BEDEHKAR),
        (N'KHARID',S.KHARID),(N'MKHARID',S.MKHARID),(N'TKHARID',S.TKHARID),(N'HKHARID',S.HKHARID),
        (N'FROSH',S.FROSH),(N'MFROSH',S.MFROSH),(N'TFROSH',S.TFROSH),(N'HFROSH',S.HFROSH),
        (N'MOGODIA',S.MOGODIA),(N'DARAM',S.DARAM),(N'HDARAM',S.HDARAM),(N'HAVALAH',S.HAVALAH),
        (N'HAZ_TOL',S.HAZ_TOL),(N'PJHAZ_TOL1',S.PJHAZ_TOL1),(N'PHAZ_TOL',S.PHAZ_TOL),(N'GHEYMAT',S.GHEYMAT),
        (N'PPDAST',S.PPDAST),(N'PPSAR',S.PPSAR),(N'AMALKARD',S.AMALKARD),(N'CONKAL',S.CONKAL),(N'PKHARID',S.PKHARID)
    ) J(FieldName,Code)
    WHERE J.Code>0 AND NOT EXISTS(SELECT 1 FROM dbo.TOTA_HES T WHERE T.NUMBER=J.Code);
    IF @Message IS NOT NULL THROW 51027,@Message,1;
    SELECT TOP(1) @Message=N'حساب تنظیم ' + J.FieldName + N' با مقدار ' + J.Code + N' در کدینگ موجود نیست.'
    FROM dbo.SAZMAN S CROSS APPLY (VALUES
        (N'ADA',CONVERT(nvarchar(100),S.ADA)),(N'APA',CONVERT(nvarchar(100),S.APA)),
        (N'ADV',CONVERT(nvarchar(100),S.ADV)),(N'APV',CONVERT(nvarchar(100),S.APV)),
        (N'HPOR',CONVERT(nvarchar(100),S.HPOR)),(N'PERSONEL',CONVERT(nvarchar(100),S.PERSONEL)),
        (N'PERVAM',CONVERT(nvarchar(100),S.PERVAM))
    ) J(FieldName,Code)
    WHERE NULLIF(LTRIM(RTRIM(J.Code)),N'') IS NOT NULL AND LTRIM(RTRIM(J.Code))<>N'0'
        AND NOT EXISTS(SELECT 1 FROM dbo.CUST_HESAB C WHERE C.hes=LTRIM(RTRIM(J.Code)))
        AND NOT (J.FieldName IN(N'PERSONEL',N'PERVAM') AND
            EXISTS(SELECT 1 FROM dbo.TOTA_HES T WHERE CONVERT(nvarchar(40),CONVERT(bigint,T.NUMBER))=LTRIM(RTRIM(J.Code))));
    IF @Message IS NOT NULL THROW 51028,@Message,1;
    -- WIN_SAZMAN / legacy SAZMAN: SANAT is the industrial-article checkbox.
    -- Keep this outside the one-time invoice conversion guard, so a converted
    -- database can also apply the required setting on a subsequent run.
    UPDATE dbo.SAZMAN SET SANAT=1 WHERE SANAT IS NULL OR SANAT=0;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;";
}
