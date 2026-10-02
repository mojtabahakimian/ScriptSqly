namespace ScriptSqly.Migrations;

public static partial class ScriptSqly
{
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
    SELECT N'410: پایاپای خرید؛ 411: کنترل خرید؛ 761: قیمت تمام شده کالای فروش رفته. تنظیم معتبر حفظ می‌شود؛ اسناد قبلی تغییر نمی‌کنند.' AS Preview;
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
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;";
}
