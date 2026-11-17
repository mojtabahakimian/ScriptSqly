using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// اتوماسیون در سفیر: ثبتِ باز شدنِ پیوستِ رویدادها (EVENT_FILE_VIEW) — چه کسی و کی.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/88-event-file-view.sql در مخزن Safir یکی بماند
        /// (آزمونِ EventFileViewTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد).
        /// idempotent است.
        /// </summary>
        private static void EventFileViewScript(SqlConnection db)
        {
            ExecuteBatches(db, EventFileViewSql);
        }

        private const string EventFileViewSql = @"/* ═══════════════════════════════════════════════════════════════════
   اتوماسیون — چه کسی و کی پیوستِ یک رویداد را باز کرده است (فقط مستر کارکت ِ وب)

   • EVENT_FILE_VIEW: هر بار که کاربری پیوستِ رویدادی را از «رویدادها» ی یک کار باز می‌کند یک ردیف
     (کار IDNUM، رویداد IDD، کاربر USERCO و نامش، زمان). در فهرستِ رویدادها برای هر پیوست خلاصه می‌شود:
     هر کاربر، اولین و آخرین بار و تعداد.
   • بی کلیدِ خارجی به EVENTS: حذفِ رویداد در نرم‌افزارِ ویندوزی با این جدول گیر نمی‌کند.
   • «مجری دیده است» ِ خودِ کار (TASKS.SEE / SEET) جدولِ تازه نمی‌خواهد و اینجا نیست.
   idempotent است. نکته: عمداً هیچ «USE <database>» اینجا نیست.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[EVENT_FILE_VIEW]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EVENT_FILE_VIEW] (
        [Id]       BIGINT IDENTITY(1,1) NOT NULL,
        [IDNUM]    BIGINT        NOT NULL,
        [IDD]      INT           NOT NULL,
        [USERCO]   INT           NOT NULL,
        [USERNAME] NVARCHAR(100) NULL,
        [ViewedAt] DATETIME2(0)  NOT NULL CONSTRAINT [DF_EVENT_FILE_VIEW_ViewedAt] DEFAULT (SYSDATETIME()),
        CONSTRAINT [PK_EVENT_FILE_VIEW] PRIMARY KEY CLUSTERED ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EVENT_FILE_VIEW_TASK' AND object_id = OBJECT_ID(N'[dbo].[EVENT_FILE_VIEW]'))
    CREATE INDEX [IX_EVENT_FILE_VIEW_TASK] ON [dbo].[EVENT_FILE_VIEW] ([IDNUM], [IDD]) INCLUDE ([USERCO], [USERNAME], [ViewedAt]);
GO
";
    }
}
