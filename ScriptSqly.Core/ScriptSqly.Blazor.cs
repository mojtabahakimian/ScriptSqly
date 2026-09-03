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
        private static void BlazorDbScriptUpdate(SqlConnection db)
        {
            //ذخیره اطلاعات پیش فرض کاربران سمت سرور
            try { db.Execute(@"CREATE TABLE [dbo].[UserState](
								       [UserId]   INT            NOT NULL PRIMARY KEY,
								       [StateJson] NVARCHAR(MAX) NOT NULL
								   );"); } catch { }

            CrmAclScript(db);
        }

        /// <summary>
        /// کنترل دسترسی CRM — «همه را ببیند» یا «فقط داده‌ی خودش».
        ///
        /// معادل مو‌به‌موی Server/Database/crm_acl_migration.sql در مخزن Safir
        /// است. اگر یکی را عوض کردید، دیگری هم باید عوض شود.
        ///
        /// ⚠️ اجرای این متد به‌تنهایی هیچ رفتاری را عوض نمی‌کند: کلید
        /// CRM_ACL_ENFORCE با مقدار '0' (خاموش) ساخته می‌شود. فعال‌سازی دستی
        /// است و در انتهای فایل .sql توضیح داده شده.
        ///
        /// برخلاف بلوک بالا، اینجا از try/catch سراسری استفاده نشده و شرط‌ها
        /// صریحاً IF NOT EXISTS هستند. دلیلش این است که در یک مهاجرتِ دسترسی،
        /// خطای بی‌صدا خطرناک است: اگر درج ردیف CRMALL شکست بخورد و کسی متوجه
        /// نشود، بعد از روشن کردن کلید هیچ‌کس نمی‌تواند داده‌ی بقیه را ببیند و
        /// علتش هم پیدا نیست.
        /// </summary>
        private static void CrmAclScript(SqlConnection db)
        {
            const string script = @"
/* ── ۱) فرم مجازی «مشاهده CRM همه کاربران» ────────────────────────────
   یک فرم واقعی نیست؛ کلید روشن/خاموش per-user است که از نرم‌افزار WPF
   تنظیم می‌شود — همان الگوی CUSTEN و AZADPAY.
   هر کاربری که RUN این فرم را داشته باشد، CRM همه‌ی کاربران را می‌بیند.
   پیش‌فرض به هیچ‌کس داده نمی‌شود. */
IF NOT EXISTS (SELECT 1 FROM dbo.TFORMS WHERE FORMNAME = N'CRMALL')
BEGIN
    INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
    VALUES (N'CRMALL',
            N'مشاهده CRM همه کاربران',
            3,
            ISNULL((SELECT TOP 1 GRP FROM dbo.TFORMS WHERE FORMNAME = N'CRMMAIN'), 16),
            (SELECT ISNULL(MAX(IDH), 0) + 1 FROM dbo.TFORMS),
            GETDATE());
END
GO

/* ── ۲) کلید فعال‌سازی، هم‌خانواده‌ی ACL_ENFORCE حقوق و دستمزد ─────────
   اگر PAY2_CONFIG نباشد، بلوک رد می‌شود و سرور کلید را «خاموش» می‌خواند. */
IF OBJECT_ID(N'dbo.PAY2_CONFIG', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.PAY2_CONFIG WHERE CFG_KEY = N'CRM_ACL_ENFORCE')
BEGIN
    INSERT INTO dbo.PAY2_CONFIG
        (CFG_KEY, CFG_VALUE, CFG_OPTIONS, CFG_DEFAULT, CFG_SECTION,
         LABEL_FA, DESC_FA, OPT_LABELS, DATA_TYPE, ACCESS_LEVEL, CRT)
    VALUES
        (N'CRM_ACL_ENFORCE',
         N'0',
         N'1|0',
         N'0',
         N'امنیت',
         N'محدودکردن CRM به داده‌های خودِ کاربر',
         N'۰ = خاموش (پیش‌فرض): هر کاربر CRM همه را می‌بیند، مثل قبل. ' +
         N'۱ = روشن: هر کاربر فقط شرکت‌ها، پیگیری‌ها و یادداشت‌های خودش را ' +
         N'می‌بیند، مگر مجوز فرم CRMALL («مشاهده CRM همه کاربران») را داشته باشد.',
         N'روشن — هر کاربر فقط داده‌ی خودش|خاموش — همه همه‌چیز را می‌بینند',
         N'BOOL',
         1,
         GETDATE());
END
GO

/* ── ۳) ایندکس‌های پشتیبان ───────────────────────────────────────────
   با روشن شدن محدودیت، شرط WHERE همه‌ی کوئری‌های CRM عوض می‌شود.
   روی بعضی دیتابیس‌ها این‌ها از قبل دستی ساخته شده‌اند. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_COPMANES_USERID_STATUS'
                 AND object_id = OBJECT_ID(N'dbo.COPMANES'))
    CREATE NONCLUSTERED INDEX [IX_COPMANES_USERID_STATUS]
        ON [dbo].[COPMANES] ([userid], [STATUS]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CRMEVENTS_USERID'
                 AND object_id = OBJECT_ID(N'dbo.CRMEVENTS'))
    CREATE NONCLUSTERED INDEX [IX_CRMEVENTS_USERID]
        ON [dbo].[CRMEVENTS] ([USERID])
        INCLUDE ([idc], [NEXT_DATE], [miting], [STATUS]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CRMEVENTS_IDC'
                 AND object_id = OBJECT_ID(N'dbo.CRMEVENTS'))
    CREATE NONCLUSTERED INDEX [IX_CRMEVENTS_IDC]
        ON [dbo].[CRMEVENTS] ([idc])
        INCLUDE ([STATUS], [NEXT_DATE], [NEXT_TIME], [miting]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_CRMEVENTS_NEXTDATE'
                 AND object_id = OBJECT_ID(N'dbo.CRMEVENTS'))
    CREATE NONCLUSTERED INDEX [IX_CRMEVENTS_NEXTDATE]
        ON [dbo].[CRMEVENTS] ([NEXT_DATE])
        INCLUDE ([idc], [miting], [STATUS], [USERID]);
GO

IF OBJECT_ID(N'dbo.Notes', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_Notes_USERID_NDONE'
                     AND object_id = OBJECT_ID(N'dbo.Notes'))
    CREATE NONCLUSTERED INDEX [IX_Notes_USERID_NDONE]
        ON [dbo].[Notes] ([userid], [Ndone]);
GO
";

            ExecuteBatches(db, script);
        }
    }
}
