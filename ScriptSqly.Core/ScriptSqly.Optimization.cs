using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        private static void SequentialKeyContentionScript(SqlConnection db)
        {
            try
            {
                // بهینه‌سازی کلید صعودی (OPTIMIZE_FOR_SEQUENTIAL_KEY) برای SQL Server 2019+
                db.Execute(@"
IF TRY_CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) >= 15
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'';

    SELECT @sql = @sql + N'ALTER INDEX ' + QUOTENAME(i.name)
                       + N' ON ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
                       + N' SET (OPTIMIZE_FOR_SEQUENTIAL_KEY = ON);' + CHAR(10)
    FROM sys.indexes AS i
    INNER JOIN sys.tables AS t
        ON t.object_id = i.object_id
    INNER JOIN sys.index_columns AS ic
        ON ic.object_id = i.object_id
       AND ic.index_id  = i.index_id
       AND ic.key_ordinal = 1
    INNER JOIN sys.columns AS c
        ON c.object_id = i.object_id
       AND c.column_id = ic.column_id
    WHERE i.index_id > 0
      AND i.is_hypothetical = 0
      AND i.is_disabled = 0
      AND i.optimize_for_sequential_key = 0
      AND c.is_identity = 1
      AND t.name IN (N'DEED_DTL', N'INVO_LST', N'PGET_LST', N'PGET_HED', N'DEED_HED', N'HEAD_LST');

    IF LEN(@sql) > 0
    BEGIN
        EXEC sys.sp_executesql @sql;
    END
END");
            }
            catch { }
        }
    }
}
