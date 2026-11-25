using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// پیش‌فاکتور در سفیر: تأیید یا رد از راه دور با واتس‌اپ و پیامک (PROFORMA_REMOTE_OK).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/89-proforma-remote-ok.sql در مخزن Safir یکی بماند
        /// (آزمونِ RemoteOkTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد).
        /// idempotent است.
        /// </summary>
        private static void ProformaRemoteOkScript(SqlConnection db)
        {
            ExecuteBatches(db, ProformaRemoteOkSql);
        }

        private const string ProformaRemoteOkSql = @"/* ═══════════════════════════════════════════════════════════════════
   پیش‌فاکتور — تأیید یا رد از راه دور (واتس‌اپ و پیامک)، فقط مستر کارکت

   • PROFORMA_REMOTE_OK: هر بار که پیش‌فاکتور با تیکِ «به واتس‌اپ و پیامک هم برود» به کسی که امضای «مدیر عامل»
     (SGN3) دارد ارجاع می‌شود یک ردیف: شماره، تأییدکننده و موبایلش، توکنِ لینکِ یک‌بارمصرف، شناسه‌ی پیامِ واتس‌اپ
     (برای شناختنِ جوابی که روی همان پیام داده شده)، فرستنده، مهلت و نتیجه.
     STATUS: 0 منتظر، 1 تأیید شد، 2 رد شد، 3 بسته شد (منقضی، امضا از جای دیگر، یا ارسالِ تازه‌تر).
     ANSWER_VIA: 'wa' جوابِ واتس‌اپ، 'link' لینکِ پیامک/واتس‌اپ. ANSWER_MSG: شناسه‌ی پیامِ جوابِ واتس‌اپ تا یک پیام دو بار
     (برای دو پیش‌فاکتور) خوانده نشود.
   • بی کلیدِ خارجی به HEAD_LST: حذفِ پیش‌فاکتور با این جدول گیر نمی‌کند.
   idempotent است. نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[PROFORMA_REMOTE_OK]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PROFORMA_REMOTE_OK] (
        [Id]           INT IDENTITY(1,1) NOT NULL,
        [NUMBER]       FLOAT          NOT NULL,
        [APPROVER]     INT            NOT NULL,
        [MOBILE]       VARCHAR(20)    NULL,
        [TOKEN]        VARCHAR(40)    NOT NULL,
        [WA_MSG_ID]    NVARCHAR(200)  NULL,
        [SENT_WA]      BIT            NOT NULL CONSTRAINT [DF_PROFORMA_REMOTE_OK_SENT_WA] DEFAULT (0),
        [SENT_SMS]     BIT            NOT NULL CONSTRAINT [DF_PROFORMA_REMOTE_OK_SENT_SMS] DEFAULT (0),
        [SENT_BY]      INT            NOT NULL,
        [SENT_BY_NAME] NVARCHAR(100)  NULL,
        [SENT_AT]      DATETIME2(0)   NOT NULL CONSTRAINT [DF_PROFORMA_REMOTE_OK_SENT_AT] DEFAULT (SYSDATETIME()),
        [EXPIRES]      DATETIME2(0)   NOT NULL,
        [STATUS]       TINYINT        NOT NULL CONSTRAINT [DF_PROFORMA_REMOTE_OK_STATUS] DEFAULT (0),
        [ANSWER_VIA]   VARCHAR(10)    NULL,
        [ANSWER_AT]    DATETIME2(0)   NULL,
        [REASON]       NVARCHAR(400)  NULL,
        [ANSWER_MSG]   NVARCHAR(200)  NULL,
        CONSTRAINT [PK_PROFORMA_REMOTE_OK] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [UQ_PROFORMA_REMOTE_OK_TOKEN] UNIQUE ([TOKEN])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PROFORMA_REMOTE_OK_PENDING' AND object_id = OBJECT_ID(N'[dbo].[PROFORMA_REMOTE_OK]'))
    CREATE INDEX [IX_PROFORMA_REMOTE_OK_PENDING] ON [dbo].[PROFORMA_REMOTE_OK] ([STATUS], [APPROVER]) INCLUDE ([NUMBER], [MOBILE], [WA_MSG_ID], [SENT_AT], [EXPIRES]);
GO
";
    }
}
