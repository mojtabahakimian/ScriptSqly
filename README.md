# ScriptSqly — Central Database Migration Engine

کتابخانه و ابزار مرکزی ارتقا و مهاجرت دیتابیس (SQL Server DDL / Stored Procedures / Indexes) برای پروژه‌های **MrCorrect** و **Safir**.

---

## 🤖 AI Context & Architecture Guidelines

این مخزن شامل تمامی کدهای تغییرات دیتابیس، ساختار جداول، رویه‌ها و مایگریشن‌های نرم‌افزار است.

### ساختار فایل‌ها و بخش‌ها (`ScriptSqly.Core`):

1. **`ScriptSqly.Salary.cs` — بخش حقوق و دستمزد (Payroll & PAY2):**
   - مایگریشن‌ها، جداول و رویه‌های ذخیره‌شده مربوط به **حقوق و دستمزد**.
   - شامل تعاریف فرمول‌ها، احکام، کارکرد، وام، مساعده و ساختار `PAY2`.

2. **`ScriptSqly.CostClose.cs` — بخش بستن حساب‌ها و بهای تمام شده (Cost Accounting & Year-End Close):**
   - اسکریپت‌های اسناد اختتامیه، افتتاحیه، محاسبات **بهای تمام شده**، انبار و حسابداری صنعتی.

3. **`ScriptSqly.Main.cs` — مایگریشن‌های اصلی و عمومی MrCorrect:**
   - جداول پایه حسابداری، خزانه، خرید و فروش، اشخاص، فاکتورها و پرمیشن‌ها.

4. **`ScriptSqly.Infra.cs` — ابزارها و زیرساخت (Execution Infrastructure):**
   - متدهای اجرای Dapper، مدیریت Transaction، لاگ‌گیری و پشتیبانی از حالت `@PREVIEW_ONLY`.

5. **`ScriptSqly.Optimization.cs` — بهینه‌سازی و ایندکس‌ها:**
   - اسکریپت‌های ساخت ایندکس‌های غیرخوشه‌ای (Non-Clustered Indexes) و بهینه‌سازی کوئری‌ها.

6. **`ScriptSqly.Blazor.cs` — بخش اختصاصی Safir Web:**
   - رویه‌ها و مایگریشن‌های مربوط به پنل وب Safir.

---

## 🔗 ارتباط با پروژه‌ها

- **MrCorrect WPF (`E:\prg\MrCorrect`):**
  - از طریق ProjectReference به `ScriptSqly.Core` متصل است.
  - موقع اجرا/لاگین، متد `ScriptSqly.Migrations.ScriptSqly.LetsGo(...)` را فراخوانی می‌کند.

- **Safir Blazor (`E:\prg\Blazor WebAssembly\Safir`):**
  - از طریق Git Submodule در مسیر `External/ScriptSqly` لینک است.
  - مایگریشن‌های دیتابیس مشترک توسط MrCorrect یا ابزار Runner اعمال می‌شوند.

---

## 🛠️ روش اجرا مستقیم (CLI Runner)

پروژه `ScriptSqly.Runner` امکان اجرای مستقل اسکریپت‌ها را بدون نیاز به UI فراهم می‌کند:

```bash
# اجرای کامل مایگریشن‌ها روی دیتابیس:
dotnet run --project ScriptSqly.Runner -- --conn "Server=localhost;Database=YAZDSEPAR1405;Integrated Security=True;TrustServerCertificate=True"

# اجرای تست بدون اعمال تغییرات (Preview):
dotnet run --project ScriptSqly.Runner -- --conn "..." --preview-only
```
