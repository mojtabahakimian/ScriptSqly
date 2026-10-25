using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        public static void SmsMessageTextScript(SqlConnection db, bool previewOnly = false)
        {
            if (previewOnly) return;
            ExecuteMigration(db, SmsMessageTextSql);
        }

        // Keep the whole message in the shared outbox/history. Do not truncate a sent
        // or queued message merely to fit an old 250-character logging column.
        public const string SmsMessageTextSql = @"
SET XACT_ABORT ON;
DECLARE @PREVIEW_ONLY bit = 0;
IF @PREVIEW_ONLY = 1 RETURN;
BEGIN TRANSACTION;
BEGIN TRY
 IF EXISTS (SELECT 1 FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.SMS_SENDS') AND name=N'AMSG'
              AND (system_type_id<>231 OR max_length<>-1))
 BEGIN
  DECLARE @sql nvarchar(max) = N'ALTER TABLE dbo.SMS_SENDS ALTER COLUMN AMSG nvarchar(max) ' +
   (SELECT CASE WHEN is_nullable=1 THEN N'NULL' ELSE N'NOT NULL' END
    FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.SMS_SENDS') AND name=N'AMSG');
  EXEC sys.sp_executesql @sql;
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;";
    }
}
