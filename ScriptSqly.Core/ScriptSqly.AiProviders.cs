using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// مشاور ارشد در سفیر: چند اتصالِ هوش مصنوعی (AI_Provider) و جدولِ «کدام قابلیت یا نقش با کدام اتصال و مدل» (AI_Route).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/85-ai-providers.sql در مخزن Safir یکی بماند
        /// (آزمونِ AiRoutesTests.Providers_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد).
        /// idempotent است.
        /// </summary>
        private static void AiProvidersScript(SqlConnection db)
        {
            ExecuteBatches(db, AiProvidersSql);
        }

        private const string AiProvidersSql = @"/* ═══════════════════════════════════════════════════════════════════
   مشاور ارشد — چند اتصال و جدولِ «کدام قابلیت با کدام اتصال و مدل»

   • AI_Provider: اتصال‌های اضافه کنارِ «اتصالِ اصلی» (همان AI_Config که سرجایش می‌ماند): نام، نوع (openai = سازگار با
     OpenAI مثلِ 9router و AvalAI، anthropic، gemini = کلیدِ مستقیمِ گوگل، elevenlabs = فقط تولیدِ صدا)، آدرس، کلید،
     پروکسی و تایم‌اوت. کلید مثلِ AI_Config متنِ ساده است و هیچ‌وقت به کلاینت برنمی‌گردد.
   • AI_Route: برای هر قابلیت یا نقش (hear = شنیدنِ صدا، vision = دیدنِ عکس، tts = تولیدِ صدا، tts_support = صدای النا،
     clerk = آرتا، chief = رئیس حسابداری، support = النا) اتصال و مدل. ProviderId خالی = اتصالِ اصلی؛ ردیف نبودن = همان
     رفتارِ قبلی (مدلِ گفتگو).
   • فقط سفیر این دو جدول را می‌شناسد؛ WPF دست نمی‌خورد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[AI_Provider]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AI_Provider] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [Name]           NVARCHAR(60)   NOT NULL,
        [Kind]           VARCHAR(20)    NOT NULL,
        [BaseUrl]        NVARCHAR(400)  NULL,
        [ApiKey]         NVARCHAR(1000) NULL,
        [ProxyUrl]       NVARCHAR(400)  NULL,
        [TimeoutSeconds] INT            NOT NULL CONSTRAINT [DF_AI_Provider_TimeoutSeconds] DEFAULT (120),
        [UpdatedBy]      NVARCHAR(100)  NULL,
        [UpdatedAtUtc]   DATETIME2(0)   NULL,
        CONSTRAINT [PK_AI_Provider] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [UQ_AI_Provider_Name] UNIQUE ([Name])
    );
END;
GO

IF OBJECT_ID(N'[dbo].[AI_Route]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AI_Route] (
        [Purpose]      VARCHAR(20)   NOT NULL,
        [ProviderId]   INT           NULL,
        [Model]        NVARCHAR(120) NULL,
        [Voice]        NVARCHAR(80)  NULL,
        [UpdatedBy]    NVARCHAR(100) NULL,
        [UpdatedAtUtc] DATETIME2(0)  NULL,
        CONSTRAINT [PK_AI_Route] PRIMARY KEY CLUSTERED ([Purpose])
    );
END;
GO

PRINT N'مشاور ارشد: جدول‌های اتصال‌ها (AI_Provider) و قابلیت‌ها (AI_Route) آماده شد؛ «تنظیمات سرویس مشاور ارشد» ← «اتصال‌ها و قابلیت‌ها».';
GO";
    }
}
