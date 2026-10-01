using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations;

public static partial class ScriptSqly
{
    // Keep identical to Server/Database/reservation_timeout_job.sql. Jobs belong
    // to msdb, so their names must distinguish customer databases on one server.
    private static void ReservationTimeoutJobScript(SqlConnection db)
        => ExecuteMigration(db, ReservationTimeoutJobSql);

    private const string ReservationTimeoutJobSql = @"
DECLARE @DbName sysname = DB_NAME();
DECLARE @JobName sysname = CASE WHEN LEN(@DbName) <= 104
    THEN N'CheckReservationTimeout_' + @DbName
    ELSE N'CheckReservationTimeout_' + LEFT(@DbName, 39) + N'_' +
         CONVERT(varchar(64), HASHBYTES('SHA2_256', @DbName), 2) END;
DECLARE @JobId uniqueidentifier;
DECLARE @LegacyJobId uniqueidentifier;
DECLARE @OwnTransaction bit = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
DECLARE @ReturnCode int;

BEGIN TRY
    IF @OwnTransaction = 1 BEGIN TRANSACTION;

    SELECT @JobId = job_id FROM msdb.dbo.sysjobs WHERE name = @JobName;
    IF @JobId IS NULL
    BEGIN
        SELECT @LegacyJobId = job_id FROM msdb.dbo.sysjobs
        WHERE name = N'CheckReservationTimeout';

        -- Adopt only the unmodified legacy job for this database. A job for
        -- another database, or one with additional/custom steps, stays intact.
        IF @LegacyJobId IS NOT NULL
            AND (SELECT COUNT(*) FROM msdb.dbo.sysjobsteps WHERE job_id = @LegacyJobId) = 1
            AND EXISTS (SELECT 1 FROM msdb.dbo.sysjobsteps
                WHERE job_id = @LegacyJobId AND step_id = 1 AND subsystem = N'TSQL'
                    AND database_name = @DbName
                    AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LOWER(LTRIM(RTRIM(command))), N'[', N''), N']', N''), N';', N''),
                        N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')
                        = N'execdbo.sp_checkreservationtimeout')
        BEGIN
            EXEC @ReturnCode = msdb.dbo.sp_update_job
                @job_id = @LegacyJobId, @new_name = @JobName;
            IF @ReturnCode <> 0 THROW 51031, N'تغییر نام کار زمان‌بندی رزرو ناموفق بود.', 1;
            SET @JobId = @LegacyJobId;
        END;
    END;

    -- Existing jobs retain their enabled state, steps, ownership and schedules.
    IF @JobId IS NULL
    BEGIN
        DECLARE @Enabled bit = CASE WHEN @DbName LIKE N'SafirTest%' THEN 0 ELSE 1 END;
        IF NOT EXISTS (SELECT 1 FROM msdb.dbo.syscategories
            WHERE name = N'[Uncategorized (Local)]' AND category_class = 1)
        BEGIN
            EXEC @ReturnCode = msdb.dbo.sp_add_category
                @class = N'JOB', @type = N'LOCAL', @name = N'[Uncategorized (Local)]';
            IF @ReturnCode <> 0 THROW 51032, N'ساخت دسته کار زمان‌بندی رزرو ناموفق بود.', 1;
        END;

        EXEC @ReturnCode = msdb.dbo.sp_add_job
            @job_name = @JobName, @enabled = @Enabled,
            @notify_level_eventlog = 0, @notify_level_email = 0,
            @notify_level_netsend = 0, @notify_level_page = 0, @delete_level = 0,
            @description = N'بررسی و لغو خودکار رزروهای منقضی شده (بیش از 96 ساعت).',
            @category_name = N'[Uncategorized (Local)]', @owner_login_name = N'sa',
            @job_id = @JobId OUTPUT;
        IF @ReturnCode <> 0 THROW 51033, N'ساخت کار زمان‌بندی رزرو ناموفق بود.', 1;

        EXEC @ReturnCode = msdb.dbo.sp_add_jobstep
            @job_id = @JobId, @step_name = N'Execute SP CheckReservationTimeout',
            @step_id = 1, @cmdexec_success_code = 0, @on_success_action = 1,
            @on_success_step_id = 0, @on_fail_action = 2, @on_fail_step_id = 0,
            @retry_attempts = 2, @retry_interval = 5, @os_run_priority = 0,
            @subsystem = N'TSQL', @command = N'EXEC [dbo].[sp_CheckReservationTimeout]',
            @database_name = @DbName, @flags = 0;
        IF @ReturnCode <> 0 THROW 51034, N'ساخت مرحله کار زمان‌بندی رزرو ناموفق بود.', 1;

        EXEC @ReturnCode = msdb.dbo.sp_update_job @job_id = @JobId, @start_step_id = 1;
        IF @ReturnCode <> 0 THROW 51035, N'تنظیم مرحله شروع کار رزرو ناموفق بود.', 1;

        EXEC @ReturnCode = msdb.dbo.sp_add_jobschedule
            @job_id = @JobId, @name = N'Hourly Schedule', @enabled = 1,
            @freq_type = 4, @freq_interval = 1, @freq_subday_type = 8,
            @freq_subday_interval = 1, @freq_relative_interval = 0,
            @freq_recurrence_factor = 0, @active_start_date = 20240101,
            @active_end_date = 99991231, @active_start_time = 0, @active_end_time = 235959;
        IF @ReturnCode <> 0 THROW 51036, N'ساخت زمان‌بندی رزرو ناموفق بود.', 1;

        EXEC @ReturnCode = msdb.dbo.sp_add_jobserver @job_id = @JobId, @server_name = N'(local)';
        IF @ReturnCode <> 0 THROW 51037, N'اتصال کار رزرو به سرور ناموفق بود.', 1;
    END;

    IF @OwnTransaction = 1 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @OwnTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
";
}
