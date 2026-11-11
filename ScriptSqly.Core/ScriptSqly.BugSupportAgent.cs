using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// گزارش خطا در سفیر: «النا» — پشتیبانِ هوش مصنوعی (BugReportAi، BugReportAiRun و BugReportAiConfig).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/82-bug-support-agent.sql در مخزن Safir یکی بماند
        /// (آزمونِ SupportAgentTests.Migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد). idempotent است و
        /// روی دیتابیسی که BugReports ندارد کاری نمی‌کند.
        /// </summary>
        private static void BugSupportAgentScript(SqlConnection db)
        {
            ExecuteBatches(db, BugSupportAgentSql);
        }

        private const string BugSupportAgentSql = @"/* ═══════════════════════════════════════════════════════════════════
   گزارش خطا: «النا» — پشتیبانِ هوش مصنوعی (/support-agent)

   • BugReportAi: نتیجه‌ی النا برای هر گزارش (یک ردیف به ازای BugReports.Id): حکم (آموزش / اشکال / درخواست /
     اطلاعاتِ بیشتر / تکراری)، متنِ مرتب‌شده برای مسئول، موضوع‌های آموزش، مسئولِ سپرده‌شده و جوابِ گزارش‌دهنده («حل شد» / «حل نشد»).
   • BugReportAiRun: کارنامه‌ی هر بار کارِ النا (تعدادِ تماس با مدل، زمان، خطا) — سقفِ روزانه هم از همین حساب می‌شود.
   • BugReportAiConfig: یک ردیف؛ روشن/خاموش (پیش‌فرض خاموش)، نام، مسئولِ پیش‌فرض و مسئولِ هر نرم‌افزار و بخش (JSON).
   • با حذفِ گزارش، ردیف‌هایش پاک می‌شوند. جدولِ BugReports فقط روی دیتابیسِ سرورِ گزارش‌ها هست؛ جایی که نیست، این اسکریپت
     کاری نمی‌کند. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[BugReportAi]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BugReportAi] (
        [BugReportId] INT            NOT NULL,
        [State]       NVARCHAR(20)   NOT NULL CONSTRAINT [DF_BugReportAi_State] DEFAULT (N'Queued'),
        [Verdict]     NVARCHAR(20)   NULL,
        [Confidence]  TINYINT        NULL,
        [Summary]     NVARCHAR(200)  NULL,
        [CleanText]   NVARCHAR(MAX)  NULL,
        [Category]    NVARCHAR(100)  NULL,
        [Software]    NVARCHAR(20)   NULL,
        [Topics]      NVARCHAR(400)  NULL,
        [DuplicateOf] INT            NULL,
        [Reply]       NVARCHAR(MAX)  NULL,
        [HandlerIdd]  INT            NULL,
        [HandlerName] NVARCHAR(100)  NULL,
        [Feedback]    NVARCHAR(20)   NULL,
        [HumanTook]   BIT            NOT NULL CONSTRAINT [DF_BugReportAi_HumanTook] DEFAULT (0),
        [Rounds]      INT            NOT NULL CONSTRAINT [DF_BugReportAi_Rounds] DEFAULT (0),
        [ModelCalls]  INT            NOT NULL CONSTRAINT [DF_BugReportAi_ModelCalls] DEFAULT (0),
        [Error]       NVARCHAR(1000) NULL,
        [CreatedAt]   DATETIME       NOT NULL CONSTRAINT [DF_BugReportAi_CreatedAt] DEFAULT (GETDATE()),
        [UpdatedAt]   DATETIME       NOT NULL CONSTRAINT [DF_BugReportAi_UpdatedAt] DEFAULT (GETDATE()),
        CONSTRAINT [PK_BugReportAi] PRIMARY KEY CLUSTERED ([BugReportId]),
        CONSTRAINT [FK_BugReportAi_BugReports] FOREIGN KEY ([BugReportId]) REFERENCES [dbo].[BugReports] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_BugReportAi_HandlerIdd] ON [dbo].[BugReportAi] ([HandlerIdd]) WHERE [HandlerIdd] IS NOT NULL;
END;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[BugReportAiRun]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BugReportAiRun] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [BugReportId] INT            NOT NULL,
        [Trigger]     NVARCHAR(20)   NOT NULL,
        [Verdict]     NVARCHAR(20)   NULL,
        [ModelCalls]  INT            NOT NULL CONSTRAINT [DF_BugReportAiRun_ModelCalls] DEFAULT (0),
        [DurationMs]  INT            NOT NULL CONSTRAINT [DF_BugReportAiRun_DurationMs] DEFAULT (0),
        [Error]       NVARCHAR(1000) NULL,
        [CreatedAt]   DATETIME       NOT NULL CONSTRAINT [DF_BugReportAiRun_CreatedAt] DEFAULT (GETDATE()),
        CONSTRAINT [PK_BugReportAiRun] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_BugReportAiRun_BugReports] FOREIGN KEY ([BugReportId]) REFERENCES [dbo].[BugReports] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_BugReportAiRun_CreatedAt] ON [dbo].[BugReportAiRun] ([CreatedAt]) INCLUDE ([ModelCalls]);
END;
GO

IF OBJECT_ID(N'[dbo].[BugReports]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[BugReportAiConfig]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BugReportAiConfig] (
        [Id]             INT            NOT NULL CONSTRAINT [DF_BugReportAiConfig_Id] DEFAULT (1),
        [Enabled]        BIT            NOT NULL CONSTRAINT [DF_BugReportAiConfig_Enabled] DEFAULT (0),
        [AgentName]      NVARCHAR(40)   NOT NULL CONSTRAINT [DF_BugReportAiConfig_AgentName] DEFAULT (N'النا'),
        [AutoReply]      BIT            NOT NULL CONSTRAINT [DF_BugReportAiConfig_AutoReply] DEFAULT (1),
        [FollowUp]       BIT            NOT NULL CONSTRAINT [DF_BugReportAiConfig_FollowUp] DEFAULT (1),
        [SiteUrl]        NVARCHAR(300)  NULL,
        [DefaultHandler] INT            NULL,
        [Handlers]       NVARCHAR(MAX)  NULL,
        [DailyCallLimit] INT            NOT NULL CONSTRAINT [DF_BugReportAiConfig_DailyCallLimit] DEFAULT (150),
        [UpdatedAt]      DATETIME       NOT NULL CONSTRAINT [DF_BugReportAiConfig_UpdatedAt] DEFAULT (GETDATE()),
        [UpdatedBy]      INT            NULL,
        CONSTRAINT [PK_BugReportAiConfig] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [CK_BugReportAiConfig_One] CHECK ([Id] = 1)
    );
    INSERT INTO [dbo].[BugReportAiConfig] ([Id]) VALUES (1);
END;
GO

PRINT N'گزارش خطا: جدول‌های «النا» (پشتیبانِ هوش مصنوعی) آماده شد؛ از «مدیریت سیستم ← پشتیبان هوش مصنوعی» روشنش کنید.';
GO";
    }
}
