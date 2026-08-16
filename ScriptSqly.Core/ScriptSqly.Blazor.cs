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
        }
    }
}
