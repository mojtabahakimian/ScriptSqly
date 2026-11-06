using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// کارمند حسابداری مجازی در سفیر: پیگیری در پس‌زمینه (VCL_Watch) — «وقتی … ← آن‌وقت …»، از جمله آزاد شدنِ خودکارِ مسدودی بعد از تسویه.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/76-clerk-watch.sql در مخزن Safir یکی بماند
        /// (آزمونِ ClerkWatchTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void ClerkWatchScript(SqlConnection db)
        {
            ExecuteBatches(db, ClerkWatchSql);
        }

        private const string ClerkWatchSql = @"/* ═══════════════════════════════════════════════════════════════════
   کارمند حسابداری مجازی: پیگیری در پس‌زمینه (VCL_Watch) — «وقتی … ← آن‌وقت …».

   «آرتا، هر وقت فروشگاهِ X مانده‌ی شهریور را تسویه کرد خودکار آزادش کن»، «اگر تا ۲۰ آذر پرداخت نکرد خبرم کن»، «وقتی چکِ … وصول شد
   به آقای … بگو»: هر ردیف یک پیگیری است. شرط‌ها (WhenJson: تاریخ، مانده یا بدهیِ پیش از یک تاریخ، وضعیتِ یک چک — همه با هم) را
   Safir هر چند دقیقه می‌سنجد (Server/Clerk/ClerkWatches.cs) و وقتی برقرار شد کار (ThenJson) را با نام و مجوزِ درخواست‌کننده انجام
   می‌دهد: پیام، درخواستِ تازه به کارمند (که مثلِ همیشه تأیید می‌خواهد)، یا پایانِ مسدودی (BlockId).
   • Status: ۰ منتظر، ۱ انجام شد، ۲ لغو، ۳ تا Until نتیجه نداد، ۴ نشد (Note).
   • جدول‌های WPF دست نمی‌خورند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[VCL_Watch]', N'U') IS NULL
CREATE TABLE [dbo].[VCL_Watch] (
    [Id]            INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_VCL_Watch] PRIMARY KEY,
    [DraftId]       INT            NULL,
    [RequesterCo]   INT            NOT NULL,
    [RequesterName] NVARCHAR(50)   NOT NULL,
    [Depatman]      INT            NULL,
    [Shift]         INT            NULL,
    [Title]         NVARCHAR(300)  NOT NULL,
    [WhenJson]      NVARCHAR(MAX)  NOT NULL,
    [ThenJson]      NVARCHAR(MAX)  NOT NULL,
    [Account]       NVARCHAR(80)   NULL,
    [BlockId]       BIGINT         NULL,
    [Until]         INT            NULL,
    [Status]        TINYINT        NOT NULL CONSTRAINT [DF_VCL_Watch_Status] DEFAULT (0),
    [LastState]     NVARCHAR(400)  NULL,
    [CreatedAtUtc]  DATETIME2      NOT NULL CONSTRAINT [DF_VCL_Watch_Created] DEFAULT (SYSUTCDATETIME()),
    [CheckedAtUtc]  DATETIME2      NULL,
    [DoneAtUtc]     DATETIME2      NULL,
    [Note]          NVARCHAR(400)  NULL
);
GO

IF OBJECT_ID(N'[dbo].[VCL_Watch]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VCL_Watch_Status' AND object_id = OBJECT_ID(N'[dbo].[VCL_Watch]'))
CREATE INDEX [IX_VCL_Watch_Status] ON [dbo].[VCL_Watch] ([Status], [RequesterCo]);
GO

IF OBJECT_ID(N'[dbo].[VCL_Watch]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VCL_Watch_Draft' AND object_id = OBJECT_ID(N'[dbo].[VCL_Watch]'))
CREATE INDEX [IX_VCL_Watch_Draft] ON [dbo].[VCL_Watch] ([DraftId]);
GO

PRINT N'کارمند حسابداری مجازی: جدولِ پیگیری در پس‌زمینه (VCL_Watch) آماده شد.';
GO";
    }
}
