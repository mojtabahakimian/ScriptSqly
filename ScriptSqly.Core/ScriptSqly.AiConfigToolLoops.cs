using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// مشاور ارشد در سفیر: پیش‌فرضِ AI_Config.MaxToolLoops از ۴ (مهاجرتِ ۳۱) به ۱۰، و یک بار سطری که هنوز روی ۴ است.
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/84-ai-config-tool-loops.sql در مخزن Safir یکی بماند
        /// (آزمونِ AiAdvisorHardeningTests.ToolLoops_migration_matches_the_ScriptSqly_copy_and_is_registered همین را می‌سنجد).
        /// idempotent است و روی دیتابیسی که AI_Config ندارد کاری نمی‌کند.
        /// </summary>
        private static void AiConfigToolLoopsScript(SqlConnection db)
        {
            ExecuteBatches(db, AiConfigToolLoopsSql);
        }

        private const string AiConfigToolLoopsSql = @"/* ═══════════════════════════════════════════════════════════════════
   مشاور ارشد — پیش‌فرضِ «سقف مراحل ابزار» از ۴ به ۱۰

   • مهاجرتِ ۳۱ ستونِ AI_Config.MaxToolLoops را با DEFAULT 4 ساخت. تا «فعال» خاموش است appsettings (۱۰) به کار می‌رود؛
     با روشن کردنش همان ۴ مؤثر می‌شود و مدل پیش از رسیدن به کوئری از مرحله می‌افتد (آرمان، ۱۴۰۵/۰۸/۲۱: «خرید مهر» بی‌عدد ماند).
   • پیش‌فرضِ ستون ۱۰ می‌شود، و فقط همین یک بار (وقتی پیش‌فرضِ قبلی هنوز ۴ است) سطرِ تنظیماتی که روی همان ۴ مانده هم ۱۰ می‌شود.
     اجرای دوباره کاری نمی‌کند؛ پس اگر مدیر بعداً خودش ۴ را انتخاب کند، دست نمی‌خورد.
   • فقط روی دیتابیسی که AI_Config دارد. idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[AI_Config]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AI_Config', N'MaxToolLoops') IS NOT NULL
BEGIN
    DECLARE @df SYSNAME, @def NVARCHAR(4000), @drop NVARCHAR(400);
    SELECT @df = dc.name, @def = dc.definition
    FROM   sys.default_constraints dc
    JOIN   sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE  dc.parent_object_id = OBJECT_ID(N'dbo.AI_Config') AND c.name = N'MaxToolLoops';

    IF @df IS NULL OR @def <> N'((10))'
    BEGIN
        IF @df IS NOT NULL
        BEGIN
            SET @drop = N'ALTER TABLE [dbo].[AI_Config] DROP CONSTRAINT ' + QUOTENAME(@df);
            EXEC (@drop);
        END;
        ALTER TABLE [dbo].[AI_Config] ADD CONSTRAINT [DF_AI_Config_MaxToolLoops] DEFAULT (10) FOR [MaxToolLoops];

        IF @def = N'((4))'
            UPDATE [dbo].[AI_Config] SET [MaxToolLoops] = 10 WHERE [MaxToolLoops] = 4;
    END;
END;
GO

PRINT N'مشاور ارشد: پیش‌فرضِ سقف مراحل ابزار ۱۰ شد.';
GO";
    }
}
