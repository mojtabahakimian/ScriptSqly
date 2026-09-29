using Dapper;
using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// دسترسی «تغییر پورسانت فاکتور فروش امضاشده» (FORMNAME = FROOSH_PORSANT_SGN).
        ///
        /// کاربری که این دسترسی (RUN) را داشته باشد می‌تواند ردیف‌های پورسانت ویزیتور را در فاکتور
        /// فروشِ امضاشده، بدون برداشتن امضا، تغییر دهد. سطرهای SAL_CHEK برای همه‌ی کاربران با مقدار
        /// پیش‌فرض غیرفعال ساخته می‌شود تا در فرم مدیریت مجوزها قابل مشاهده و تخصیص باشد.
        /// اسکریپت idempotent است.
        /// </summary>
        private static void SignedPorsantPermissionScript(SqlConnection db)
        {
            db.Execute(
                @"IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
                     AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'FROOSH_PORSANT_SGN')
                  BEGIN
                      INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
                      VALUES (N'FROOSH_PORSANT_SGN',
                              N'تغییر پورسانت فاکتور فروش امضاشده',
                              3,
                              ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'FACTFRMO'), 5),
                              (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
                              GETDATE());
                  END");

            db.Execute(
                @"IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
                     AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
                     AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
                  BEGIN
                      DECLARE @PorsantSignedId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'FROOSH_PORSANT_SGN');

                      IF @PorsantSignedId IS NOT NULL
                      BEGIN
                          INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
                          SELECT D.IDD, @PorsantSignedId, 0, 0, 0, 0, 0, GETDATE()
                          FROM [dbo].[SALA_DTL] D
                          WHERE NOT EXISTS (
                              SELECT 1 FROM [dbo].[SAL_CHEK] E
                              WHERE E.USERCO = D.IDD AND E.[OBJECT] = @PorsantSignedId
                          );
                      END
                  END");
        }
    }
}
