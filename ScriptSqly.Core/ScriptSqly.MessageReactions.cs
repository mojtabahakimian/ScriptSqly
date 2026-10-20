using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// پیام‌های اتوماسیون در Safir — واکنش (شکلک) روی پیام: جدولِ MSG_REACTION.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/52-message-reactions.sql در مخزن Safir یکی بماند
        /// (آزمونِ MessageReactionTests همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void MessageReactionsScript(SqlConnection db)
        {
            ExecuteBatches(db, MessageReactionsSql);
        }

        private const string MessageReactionsSql = @"/* ═══════════════════════════════════════════════════════════════════
   پیام‌های اتوماسیون — واکنش (شکلک) روی پیام: جدولِ MSG_REACTION

   هر ردیف: کاربرِ USERCO روی پیامِ IDNUM ِ MESAGEP شکلکِ EMOJI زده است (یک بار برای هر شکلک).
   شکلک‌های مجاز در Shared/Models/Automation/MessageReactions.cs است (فقط کاری).

   عمداً کلیدِ خارجی به MESAGEP ندارد: نرم‌افزارِ ویندوزی (WPF) هم پیام حذف می‌کند و این جدول را
   نمی‌شناسد؛ کلیدِ خارجی حذفِ پیام در WPF را می‌شکست. واکنشِ پیامِ حذف‌شده با join کنار می‌رود.

   EMOJI عمداً با Latin1_General_BIN2 است: در collation ِ پیش‌فرضِ دیتابیس (مثلاً Arabic_CI_AS) شکلک‌های
   بیرون از BMP (👍 🙏 👀) با هم «برابر»اند؛ بدونِ آن، زدنِ 🙏 واکنشِ 👍 ِ همان کس را برمی‌داشت و
   ایندکسِ یکتا هم این دو را تکراری می‌دید (آزمونِ دیتابیسِ واقعی ۱۴۰۵/۰۷/۲۹).
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[MSG_REACTION]', N'U') IS NULL
    CREATE TABLE [dbo].[MSG_REACTION](
        [Id]     INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MSG_REACTION] PRIMARY KEY,
        [IDNUM]  INT           NOT NULL,   -- MESAGEP.IDNUM
        [USERCO] INT           NOT NULL,   -- SALA_DTL.IDD
        [EMOJI]  NVARCHAR(8)   COLLATE Latin1_General_BIN2 NOT NULL,
        [CRT]    DATETIME2(0)  NOT NULL CONSTRAINT [DF_MSG_REACTION_CRT] DEFAULT (SYSUTCDATETIME())
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MSG_REACTION' AND object_id = OBJECT_ID(N'[dbo].[MSG_REACTION]'))
    CREATE UNIQUE NONCLUSTERED INDEX [UX_MSG_REACTION] ON [dbo].[MSG_REACTION] ([IDNUM], [USERCO], [EMOJI]);
GO

PRINT N'پیام‌های اتوماسیون: جدولِ MSG_REACTION آماده شد.';
GO
";
    }
}
