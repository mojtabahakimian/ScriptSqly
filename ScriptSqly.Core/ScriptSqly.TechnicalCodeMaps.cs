using System.Linq;
using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        // Additive only: legacy map tables and existing technical-code values stay unchanged.
        public static void TechnicalCodeMapGroupsScript(SqlConnection db, bool previewOnly = false)
        {
            if (previewOnly) return;
            ExecuteMigration(db, TechnicalCodeMapGroupsSql);
        }

        // Consumers of stuf_def_nfani must not decode a scoped code with the global layout.
        public static string TechnicalCodeMapReportSelect(string legacySelect)
        {
            var codes = Enumerable.Range(1, 9).Select(i =>
                "CAST(legacy.col" + i + " AS nvarchar(30)) AS col" + i);
            var names = Enumerable.Range(1, 9).Select(i => "legacy.colN" + i);
            var scopedCodes = Enumerable.Range(1, 9).Select(i =>
                "ISNULL(MAX(CASE WHEN p.Seq=" + i + " THEN SUBSTRING(s.N_FANI,p.Pos,p.SIZEF) END),N'1') AS col" + i);
            var scopedNames = Enumerable.Range(1, 9).Select(i =>
                "ISNULL(MAX(CASE WHEN p.Seq=" + i + " THEN o.MPNAME END),N' ') AS colN" + i);
            return "SELECT legacy.CODE,legacy.NAME,legacy.N_FANI," + string.Join(",", codes.Concat(names)) +
                " FROM (" + legacySelect + ") legacy JOIN dbo.STUF_DEF s ON s.CODE=legacy.CODE" +
                " WHERE NOT EXISTS (SELECT 1 FROM dbo.TCOD_MAP_GRP_BYGROUP p WHERE p.GROUP_CODE=s.N_FANI_MAP_GROUP AND s.N_FANI_MAP_CODE=s.N_FANI)" +
                " UNION ALL SELECT s.CODE,s.NAME,s.N_FANI," + string.Join(",", scopedCodes.Concat(scopedNames)) + @"
FROM dbo.STUF_DEF s
CROSS APPLY (
 SELECT MPP,SIZEF,ROW_NUMBER() OVER(ORDER BY MPP) AS Seq,
  CONVERT(int,SUM(SIZEF) OVER(ORDER BY MPP ROWS UNBOUNDED PRECEDING)-SIZEF+1) AS Pos
 FROM dbo.TCOD_MAP_GRP_BYGROUP WHERE GROUP_CODE=s.N_FANI_MAP_GROUP AND s.N_FANI_MAP_CODE=s.N_FANI
) p
LEFT JOIN dbo.TCOD_MAP_BYGROUP o ON o.GROUP_CODE=s.N_FANI_MAP_GROUP AND o.MPP=p.MPP
 AND o.MPCODE=TRY_CONVERT(int,SUBSTRING(s.N_FANI,p.Pos,p.SIZEF))
GROUP BY s.CODE,s.NAME,s.N_FANI";
        }

        public const string TechnicalCodeMapGroupsSql = @"
SET XACT_ABORT ON;
DECLARE @PREVIEW_ONLY bit = 0;
IF @PREVIEW_ONLY = 1 RETURN;
BEGIN TRANSACTION;
BEGIN TRY
 -- NULL means legacy/global. Bind a generated code to its source group only when
 -- the item is saved; merely defining a new map must not reinterpret old codes.
 -- Keep the code alongside the source: other clients can edit N_FANI without
 -- knowing these optional columns, and stale provenance must then be ignored.
 IF COL_LENGTH(N'dbo.STUF_DEF',N'N_FANI_MAP_GROUP') IS NULL
  ALTER TABLE dbo.STUF_DEF ADD N_FANI_MAP_GROUP float NULL;
 IF COL_LENGTH(N'dbo.STUF_DEF',N'N_FANI_MAP_CODE') IS NULL
  ALTER TABLE dbo.STUF_DEF ADD N_FANI_MAP_CODE nvarchar(30) NULL;
 IF OBJECT_ID(N'dbo.TCOD_MAP_GRP_BYGROUP', N'U') IS NULL
 BEGIN
  CREATE TABLE dbo.TCOD_MAP_GRP_BYGROUP (
   ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
   GROUP_CODE float NOT NULL,
   MPP int NOT NULL CHECK(MPP BETWEEN 1 AND 10),
   MPNAME nvarchar(50) NOT NULL,
   SIZEF int NOT NULL CHECK(SIZEF BETWEEN 1 AND 10),
   STARTF int NULL,
   CONSTRAINT UQ_TCOD_MAP_GRP_BYGROUP UNIQUE(GROUP_CODE,MPP),
   CONSTRAINT FK_TCOD_MAP_GRP_BYGROUP_GROUP FOREIGN KEY(GROUP_CODE) REFERENCES dbo.TCOD_STUFGROUP(CODE)
  );
 END;
 IF OBJECT_ID(N'dbo.TCOD_MAP_BYGROUP', N'U') IS NULL
 BEGIN
  CREATE TABLE dbo.TCOD_MAP_BYGROUP (
   ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
   GROUP_CODE float NOT NULL,
   MPP int NOT NULL,
   MPCODE int NOT NULL CHECK(MPCODE>=0),
   MPNAME nvarchar(50) NOT NULL,
   OSCO int NULL,
   CONSTRAINT UQ_TCOD_MAP_BYGROUP UNIQUE(GROUP_CODE,MPP,MPCODE),
   CONSTRAINT FK_TCOD_MAP_BYGROUP_PART FOREIGN KEY(GROUP_CODE,MPP)
    REFERENCES dbo.TCOD_MAP_GRP_BYGROUP(GROUP_CODE,MPP)
  );
 END;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;";
    }
}
