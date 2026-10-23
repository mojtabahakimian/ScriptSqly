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
            ExecuteMigration(db, 
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

            ExecuteMigration(db, 
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

        /// <summary>
        /// دسترسی «نبض سازمان» (FORMNAME = PULSE).
        ///
        /// اضافه شدن به TFORMS با عنوان «نبض سازمان» تا در جستجوی فرم‌های WPF قابل مشاهده و تخصیص باشد.
        /// برای کاربرانی که قبلاً تراز آزمایشی (TARAZ_4) را داشتند مقادیر RUN و SEE به صورت خودکار منتقل
        /// می‌شود و برای بقیه سطر با 0 ساخته می‌شود.
        /// </summary>
        private static void PulsePermissionScript(SqlConnection db)
        {
            ExecuteMigration(db,
                @"IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
                     AND NOT EXISTS (SELECT 1 FROM [dbo].[TFORMS] WHERE FORMNAME = N'PULSE')
                  BEGIN
                      INSERT INTO [dbo].[TFORMS] (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
                      VALUES (N'PULSE',
                              N'نبض سازمان',
                              3,
                              ISNULL((SELECT TOP 1 GRP FROM [dbo].[TFORMS] WHERE FORMNAME = N'TARAZ_4'), 2),
                              (SELECT ISNULL(MAX(IDH), 0) + 1 FROM [dbo].[TFORMS]),
                              GETDATE());
                  END");

            ExecuteMigration(db,
                @"IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
                  BEGIN
                      UPDATE [dbo].[TFORMS]
                      SET CAPTION = N'نبض سازمان'
                      WHERE FORMNAME = N'PULSE' AND CAPTION <> N'نبض سازمان';
                  END");

            ExecuteMigration(db,
                @"IF OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
                     AND OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
                     AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
                  BEGIN
                      DECLARE @PulseId INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'PULSE');
                      DECLARE @Taraz4Id INT = (SELECT IDH FROM [dbo].[TFORMS] WHERE FORMNAME = N'TARAZ_4');

                      IF @PulseId IS NOT NULL
                      BEGIN
                          INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
                          SELECT D.IDD, @PulseId,
                                 ISNULL(scTaraz.RUN, 0),
                                 ISNULL(scTaraz.SEE, 0),
                                 0, 0, 0, GETDATE()
                          FROM [dbo].[SALA_DTL] D
                          LEFT JOIN [dbo].[SAL_CHEK] scTaraz ON scTaraz.USERCO = D.IDD AND scTaraz.[OBJECT] = @Taraz4Id
                          WHERE NOT EXISTS (
                              SELECT 1 FROM [dbo].[SAL_CHEK] E
                              WHERE E.USERCO = D.IDD AND E.[OBJECT] = @PulseId
                          );
                      END
                  END");
        }

        /// <summary>
        /// سطرِ SAL_CHEK ِ هر کاربر برای هر فرم — تا همه‌ی فرم‌ها در «تعیین سطح دسترسی» ِ WPF دیده شوند.
        ///
        /// آن پنجره (F_USER_PERMITION_FORMS) فهرست را از TFORMS INNER JOIN SAL_CHEK می‌سازد و خودش فقط برای
        /// کاربری که هیچ سطری ندارد همه را می‌سازد؛ پس فرمی که بعداً به TFORMS اضافه می‌شود (PAY2_*، COST_*، …)
        /// برای کاربرانِ قدیمی هرگز در آن نمی‌آید، هر چند بار هم به‌روزرسانی اجرا شود (Arman1405: ۵۱ فرم برای ۲۱
        /// کاربر از ۲۲). سطرِ نبوده با همه‌ی مجوزها صفر ساخته می‌شود — صریح، چون پیش‌فرضِ ستون‌های SAL_CHEK یک
        /// است — پس دسترسیِ کسی عوض نمی‌شود: نبودِ سطر هم در هر دو برنامه «اجازه ندارد» است.
        /// در هر اجرا، بعد از همه‌ی اسکریپت‌هایی که فرم می‌سازند. مو‌به‌مو برابر با Server/Database/sal_chek_complete_rows.sql ِ Safir.
        /// </summary>
        private static void CompleteFormPermissionRowsScript(SqlConnection db)
        {
            ExecuteMigration(db,
                @"IF OBJECT_ID(N'[dbo].[SAL_CHEK]', N'U') IS NOT NULL
                     AND OBJECT_ID(N'[dbo].[TFORMS]', N'U') IS NOT NULL
                     AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
                  INSERT INTO [dbo].[SAL_CHEK] (USERCO, [OBJECT], RUN, SEE, INP, UPD, DEL, CRT)
                  SELECT D.IDD, F.IDH, 0, 0, 0, 0, 0, GETDATE()
                  FROM [dbo].[SALA_DTL] D
                  CROSS JOIN [dbo].[TFORMS] F
                  WHERE D.IDD IS NOT NULL AND F.IDH IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM [dbo].[SAL_CHEK] E
                        WHERE E.USERCO = D.IDD AND E.[OBJECT] = F.IDH
                    );");
        }
    }
}
