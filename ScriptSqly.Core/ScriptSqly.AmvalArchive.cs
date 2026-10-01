using Dapper;
using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        public const string AmvalArchiveSql = @"
SET XACT_ABORT ON;
IF @PREVIEW_ONLY = 1
BEGIN
    SELECT CASE WHEN OBJECT_ID(N'dbo.TR_AMVAL', N'U') IS NULL
        THEN N'Create dbo.TR_AMVAL from dbo.AMVAL'
        ELSE N'dbo.TR_AMVAL already exists' END AS Preview;
    RETURN;
END;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.TR_AMVAL', N'U') IS NULL
    BEGIN
        IF OBJECT_ID(N'dbo.AMVAL', N'U') IS NULL
            THROW 50001, 'dbo.AMVAL is missing', 1;
        SELECT TOP (0)
            CONVERT(bigint, 0) AS UP_DATE,
            CONVERT(float, 0) AS UP_TIME,
            CONVERT(nvarchar(40), NULL) AS UP_USER_NAME,
            CONVERT(nvarchar(50), NULL) AS PC_NAME,
            CONVERT(nvarchar(50), NULL) AS IPADD,
            barchasb, aname, calmethod, rate, hesab, khdate, price, place,
            arzesh, astatus, Description, USER_NAME, CRT, UID
        INTO dbo.TR_AMVAL
        FROM dbo.AMVAL;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;";

        public static void AmvalArchiveScript(SqlConnection db, bool previewOnly = false)
        {
            ExecuteMigration(db, AmvalArchiveSql, new { PREVIEW_ONLY = previewOnly });
        }
    }
}
