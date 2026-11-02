using Microsoft.Data.SqlClient;

namespace ScriptSqly.Migrations
{
    public static partial class ScriptSqly
    {
        /// <summary>
        /// ورودِ وبِ سفیر: جدولِ SAFIR_USER_AUTH (رمزِ هش‌شده‌ی جدا و کدِ دومِ اختیاری برای کاربرانِ SALA_DTL).
        ///
        /// متن زیر باید مو‌به‌مو با Server/Database/64-safir-user-auth.sql در مخزن Safir یکی بماند
        /// (آزمونِ SafirAuthMigrationTests.Migration_matches_the_ScriptSqly_copy همین را می‌سنجد). idempotent است.
        /// </summary>
        private static void SafirUserAuthScript(SqlConnection db)
        {
            ExecuteBatches(db, SafirUserAuthSql);
        }

        private const string SafirUserAuthSql = @"/* ═══════════════════════════════════════════════════════════════════
   ورودِ وبِ سفیر: رمزِ جدای هش‌شده و کدِ دومِ اختیاری (Authenticator) برای کاربرانِ همان SALA_DTL.
   طرح: docs/plans/2026-11-02-web-login-2fa-brief.md

   • یک ردیف برای هر کاربر (IDD)؛ نبودِ ردیف یعنی رمزِ سفیرِ او هنوز ساخته نشده.
   • SALA_DTL و PSAL_NAME دست نمی‌خورند؛ WPF، Access و MOADIAN.EXE مثلِ قبل کار می‌کنند.
   • FK به SALA_DTL(IDD) با CASCADE (مثلِ SAL_CHEK): حذفِ کاربر اطلاعاتِ ورودش را هم پاک می‌کند تا IDD ِ تکراری
     (MAX+1) رمزِ نفرِ قبل را نگیرد. اگر IDD ایندکسِ یکتا نداشت، FK ساخته نمی‌شود.
   • رمز فقط هش (PasswordHasher)، رازِ Authenticator رمزگذاری‌شده با کلیدِ سرور؛ هیچ مقدارِ خامی اینجا نیست.
   idempotent است.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_USER_AUTH]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SAFIR_USER_AUTH] (
        [IDD]            INT            NOT NULL,
        [PWD_HASH]       NVARCHAR(400)  NULL,
        [PWD_CHANGED]    DATETIME       NULL,
        [PWD_FROM_WPF]   BIT            NOT NULL CONSTRAINT [DF_SAFIR_USER_AUTH_PWD_FROM_WPF] DEFAULT (0),
        [TOTP_SECRET]    NVARCHAR(400)  NULL,
        [TOTP_ON]        BIT            NOT NULL CONSTRAINT [DF_SAFIR_USER_AUTH_TOTP_ON] DEFAULT (0),
        [TOTP_LAST_STEP] BIGINT         NULL,
        [RECOVERY]       NVARCHAR(MAX)  NULL,
        [ENROLL_HASH]    NVARCHAR(400)  NULL,
        [ENROLL_UNTIL]   DATETIME       NULL,
        [SEC_VER]        INT            NOT NULL CONSTRAINT [DF_SAFIR_USER_AUTH_SEC_VER] DEFAULT (1),
        [FAILS]          INT            NOT NULL CONSTRAINT [DF_SAFIR_USER_AUTH_FAILS] DEFAULT (0),
        [LOCK_UNTIL]     DATETIME       NULL,
        [CRT]            DATETIME       NULL CONSTRAINT [DF_SAFIR_USER_AUTH_CRT] DEFAULT (GETDATE()),
        CONSTRAINT [PK_SAFIR_USER_AUTH] PRIMARY KEY CLUSTERED ([IDD])
    );
END;
GO

IF OBJECT_ID(N'[dbo].[SAFIR_USER_AUTH]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SALA_DTL]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[FK_SAFIR_USER_AUTH_SALA_DTL]', N'F') IS NULL
   AND EXISTS (SELECT 1 FROM sys.indexes i
               JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
               JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE i.object_id = OBJECT_ID(N'[dbo].[SALA_DTL]') AND i.is_unique = 1 AND c.name = N'IDD'
                 AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id = i.object_id AND x.index_id = i.index_id) = 1)
BEGIN
    -- ردیفِ یتیم (کاربرِ حذف‌شده) مانعِ ساختِ FK نشود
    DELETE A FROM [dbo].[SAFIR_USER_AUTH] A WHERE NOT EXISTS (SELECT 1 FROM [dbo].[SALA_DTL] S WHERE S.IDD = A.IDD);
    ALTER TABLE [dbo].[SAFIR_USER_AUTH] WITH CHECK
        ADD CONSTRAINT [FK_SAFIR_USER_AUTH_SALA_DTL] FOREIGN KEY ([IDD])
        REFERENCES [dbo].[SALA_DTL] ([IDD]) ON UPDATE CASCADE ON DELETE CASCADE;
END;
GO";
    }
}
