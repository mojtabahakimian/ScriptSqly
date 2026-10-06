namespace ScriptSqly.Migrations;

public static partial class ScriptSqly
{
    /// <summary>
    /// ترمیم علامت صادراتی فاکتورهای فروش: فاکتورهای ثبت‌شده از روی حواله‌ی صادراتی (SADER=1)
    /// که خودشان SADER=0 گرفته‌اند (فرم فاکتور این علامت را نمی‌گذاشت) و به همین دلیل در فرم
    /// «فروش صادراتی» دیده نمی‌شدند. فقط حواله‌ی صادراتی در فرم صادراتی قابل انتخاب است،
    /// پس فاکتورِ چنین حواله‌ای حتماً صادراتی است. همین ترمیم برای فاکتور خرید (TAG=12) از روی
    /// رسید صادراتی (TAG=1) هم انجام می‌شود. idempotent است.
    /// </summary>
    public const string ExportInvoiceSaderRepairSql = @"
UPDATE f
SET SADER = 1
FROM dbo.HEAD_LST f
INNER JOIN dbo.HEAD_LST h ON h.[NUMBER] = f.[NUMBER] AND h.TAG = 2
WHERE f.TAG = 13
  AND ISNULL(f.SADER, 0) = 0
  AND h.SADER = 1;

UPDATE f
SET SADER = 1
FROM dbo.HEAD_LST f
INNER JOIN dbo.HEAD_LST h ON h.[NUMBER] = f.[NUMBER] AND h.TAG = 1
WHERE f.TAG = 12
  AND ISNULL(f.SADER, 0) = 0
  AND h.SADER = 1;";

    public const string InvoicePricingAndDiscountDefinition = @"CREATE OR ALTER PROCEDURE [dbo].[sp_UpdateInvoicePricingAndDiscount]
							     @numb INT,
							     @tgg INT,
							     @PEPID_In INT,
							     @PEID_In INT,
							     @MODAT_PPID_In INT,
							     @TICMBAA_In BIT,
							     @CUST_KIND_In INT,
							     @DTT_In INT,
							     @DEPATMAN_In INT,
                             @PreservePrices_In BIT = 0
							 AS
							 BEGIN
							     SET NOCOUNT ON;
							     BEGIN TRANSACTION;
							 
							     DECLARE @effective_tgg INT;
                             DECLARE @PreservePrices BIT = @PreservePrices_In;
							     DECLARE @CurrentPEPID INT;
							     DECLARE @CurrentPEID INT;
							     
							     DECLARE @General_TF1 REAL;
							     DECLARE @General_TF2 REAL;
							     DECLARE @PETID INT; 
							 
							     DECLARE @stf_total_discount FLOAT = 0;
							     DECLARE @MLBAA_total_vat FLOAT = 0;
							     DECLARE @ErrorMessage NVARCHAR(1000);
							     
							     DECLARE @modat_from_price_payno INT;
							     DECLARE @current_mas_in_head_lst FLOAT;
							 
							 	 SET @effective_tgg = CASE WHEN @tgg = 13 THEN 2 WHEN @tgg = 25 THEN 24 ELSE @tgg END;
                             IF EXISTS (SELECT 1 FROM dbo.HEAD_LST WHERE [NUMBER] = @numb
                                        AND TAG IN (@tgg, @effective_tgg) AND ISNULL(SADER, 0) = 1)
                                 SET @PreservePrices = 1;

							     -- بخش جدید: محاسبه و به‌روزرسانی MAS در HEAD_LST
							     IF @MODAT_PPID_In IS NOT NULL AND @MODAT_PPID_In <> 0
							     BEGIN
							         SELECT @modat_from_price_payno = COALESCE(MODAT, 0) 
							         FROM dbo.PRICE_PAYNO 
							         WHERE PPID = @MODAT_PPID_In;
							 
							         -- خواندن مقدار فعلی MAS از HEAD_LST
							         SELECT @current_mas_in_head_lst = MAS 
							         FROM dbo.HEAD_LST 
							         WHERE ""NUMBER"" = @numb AND TAG = @tgg; 
							 
							         IF @modat_from_price_payno <> ISNULL(@current_mas_in_head_lst, -1) -- مقایسه با مقدار فعلی، اگر MAS قبلا Null بوده با -1 مقایسه می‌شود تا آپدیت شود
							         BEGIN
							             UPDATE dbo.HEAD_LST 
							             SET MAS = @modat_from_price_payno 
							             WHERE ""NUMBER"" = @numb AND TAG = @tgg; 
							 
							             IF @tgg = 13 -- اگر فاکتور فروش بود، MAS حواله مرتبط را نیز به‌روز کن
							             BEGIN
							                 UPDATE dbo.HEAD_LST 
							                 SET MAS = @modat_from_price_payno 
							                 WHERE ""NUMBER"" = @numb AND TAG = 2; 
							             END
							         END
							     END
							     -- پایان بخش جدید
							 
							     -- 1. تعیین PEPID (شناسه اعلامیه قیمت)
							     IF @PEPID_In IS NULL OR @PEPID_In = 0
							     BEGIN
							         SELECT TOP 1 @CurrentPEPID = PEPID 
							         FROM dbo.PRICE_ELAMIE 
							         WHERE PEPDATE <= @DTT_In AND PEPDEPART = @DEPATMAN_In 
							         ORDER BY PEPID DESC;
							     END
							     ELSE
							     BEGIN
							         SET @CurrentPEPID = @PEPID_In;
							     END
							 
							     IF @CurrentPEPID IS NULL AND @PreservePrices = 0
							     BEGIN
							         IF EXISTS (SELECT 1 FROM dbo.INVO_LST WHERE ""NUMBER"" = @numb AND TAG = @effective_tgg)
							         BEGIN
							              UPDATE dbo.INVO_LST SET IMBAA = 0, N_KOL = 0, N_MOIN = 0, TKHN = 0, MABL_K = 0, MABL = 0 
							              WHERE ""NUMBER"" = @numb AND TAG = @effective_tgg;
							              
							              SET @ErrorMessage = N'اعلامیه قیمت فعال برای تاریخ ' + CAST(@DTT_In AS NVARCHAR(10)) + N' و واحد ' + CAST(@DEPATMAN_In AS NVARCHAR(10)) + N' یافت نشد. قیمت‌ها به‌روز نشدند.';
							              RAISERROR(@ErrorMessage, 16, 1);
							              IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
							              RETURN -1; 
							         END
							     END
							 
							     -- 2. تعیین PEID (شناسه اعلامیه تخفیف)
							     IF @PEID_In IS NULL OR @PEID_In = 0
							     BEGIN
							         SELECT TOP 1 @CurrentPEID = PEID 
							         FROM dbo.PRICE_ELAMIETF 
							         WHERE PEDATE <= @DTT_In AND PEPDEPART = @DEPATMAN_In 
							         ORDER BY PEID DESC;
							     END
							     ELSE
							     BEGIN
							         SET @CurrentPEID = @PEID_In;
							     END
							 
							     -- 3. به‌روزرسانی PEPID و PEID در جدول HEAD_LST (اگر از قبل به‌روز نشده باشند یا تغییر کرده باشند)
							     UPDATE dbo.HEAD_LST 
							     SET PEPID = @CurrentPEPID, PEID = @CurrentPEID 
							     WHERE ""NUMBER"" = @numb AND TAG = @tgg 
							       AND (ISNULL(PEPID, -1) <> ISNULL(@CurrentPEPID, -1) OR ISNULL(PEID, -1) <> ISNULL(@CurrentPEID, -1) ); -- فقط در صورت تغییر آپدیت کن
							 
							     IF @tgg = 13
							     BEGIN
							         UPDATE dbo.HEAD_LST 
							         SET PEPID = @CurrentPEPID, PEID = @CurrentPEID 
							         WHERE ""NUMBER"" = @numb AND TAG = 2
							           AND (ISNULL(PEPID, -1) <> ISNULL(@CurrentPEPID, -1) OR ISNULL(PEID, -1) <> ISNULL(@CurrentPEID, -1) );
							     END
							     
							     -- 4. Preserve currency-derived prices for export invoices; discount/VAT remain shared.
                             IF @PreservePrices = 0
                             BEGIN
							     IF @CurrentPEPID IS NOT NULL
							     BEGIN
							         DECLARE @MissingPriceProductCode_HAVEPRICE NVARCHAR(15);
							         DECLARE @MissingPriceProductName_HAVEPRICE NVARCHAR(80);
							 
							         SELECT TOP 1 @MissingPriceProductCode_HAVEPRICE = il.CODE, @MissingPriceProductName_HAVEPRICE = sd.NAME
							         FROM dbo.INVO_LST il
							         JOIN dbo.STUF_DEF sd ON il.CODE = sd.CODE
							         LEFT JOIN dbo.PRICE_ELAMIE_DTL ped ON sd.PGID = ped.PGID AND ped.PEPID = @CurrentPEPID
							         WHERE il.""NUMBER"" = @numb AND il.TAG = @effective_tgg AND ped.PRICE1 IS NULL;
							 
							         IF @MissingPriceProductCode_HAVEPRICE IS NOT NULL
							         BEGIN
							             SET @ErrorMessage = N'کالای : ''' + @MissingPriceProductCode_HAVEPRICE + N''' - ''' + ISNULL(@MissingPriceProductName_HAVEPRICE, N'') + N''' دارای گروه بندی قیمتی نیست یا گروه آن در اعلامیه قیمت با شناسه ' + CAST(@CurrentPEPID AS NVARCHAR(10)) + N' تعریف نشده.';
							             RAISERROR(@ErrorMessage, 16, 1);
							             IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
							             RETURN -2; 
							         END
							 
							         UPDATE il
							         SET 
							             il.MABL = ped.PRICE1,
							             il.MABL_K = ROUND(ped.PRICE1 * il.MEGHk, 0)
							         FROM dbo.INVO_LST il
							         JOIN dbo.STUF_DEF sd ON il.CODE = sd.CODE
							         JOIN dbo.PRICE_ELAMIE_DTL ped ON sd.PGID = ped.PGID
							         WHERE il.""NUMBER"" = @numb 
							           AND il.TAG = @effective_tgg 
							           AND ped.PEPID = @CurrentPEPID;
							     END
							     ELSE 
							     BEGIN
							         IF EXISTS (SELECT 1 FROM dbo.INVO_LST WHERE ""NUMBER"" = @numb AND TAG = @effective_tgg)
							         BEGIN
							              UPDATE dbo.INVO_LST 
							              SET MABL = 0, MABL_K = 0, IMBAA = 0, N_KOL = 0, N_MOIN = 0, TKHN = 0 
							              WHERE ""NUMBER"" = @numb AND TAG = @effective_tgg;
							         END
							     END
							 
							     END
                             -- 5. اعمال تخفیفات و محاسبه ارزش افزوده
							     IF @CurrentPEID IS NOT NULL 
							     BEGIN
							         SELECT 
							             @General_TF1 = COALESCE(TF1, 0), 
							             @General_TF2 = COALESCE(TF2, 0), 
							             @PETID = PETID
							         FROM dbo.PRICE_ELAMIETF_DTL 
							         WHERE PEID = @CurrentPEID
							           AND CUSTCODE = @CUST_KIND_In 
							           AND PPID = @MODAT_PPID_In;
							 
							         IF @PETID IS NOT NULL 
							         BEGIN
							             WITH InvoiceLineCalculations AS (
							                 SELECT 
							                     il.id AS invo_lst_id,
							                     il.CODE AS ProductCode,
							                     il.MABL_K AS Current_MABL_K,
							                     sd.CMBAA,
							                     sd.vra AS VatRate 
							                 FROM dbo.INVO_LST il
							                 JOIN dbo.STUF_DEF sd ON il.CODE = sd.CODE
							                 WHERE il.""NUMBER"" = @numb AND il.TAG = @effective_tgg AND ISNULL(il.JAY, 0) = 0
							             ),
							             AppliedDiscounts AS (
							                 SELECT
							                     ild.invo_lst_id,
							                     ild.Current_MABL_K,
							                     ild.CMBAA,
							                     ild.VatRate,
							                     COALESCE(exc.EXCEPTION_TF1, @General_TF1) AS TF1_Final,
							                     COALESCE(exc.EXCEPTION_TF2, @General_TF2) AS TF2_Final
							                 FROM InvoiceLineCalculations ild
							                 LEFT JOIN dbo.PRICE_ELAMIETF_EXCEPTION exc ON exc.PETID = @PETID AND exc.CODE = ild.ProductCode
							             ),
							             FinalLineValues AS (
							                 SELECT
							                     ad.invo_lst_id,
							                     ad.TF1_Final,
							                     ad.TF2_Final,
							                     (ROUND(ad.Current_MABL_K * ad.TF1_Final / 100.0, 0) + 
							                      ROUND((ad.Current_MABL_K - ROUND(ad.Current_MABL_K * ad.TF1_Final / 100.0, 0)) * ad.TF2_Final / 100.0, 0))
							                     AS TotalLineDiscount,
							                     CASE 
							                         WHEN @TICMBAA_In = 1 AND ad.CMBAA = 1 AND ad.VatRate IS NOT NULL THEN 
							                             FLOOR((ad.Current_MABL_K - 
							                                    (ROUND(ad.Current_MABL_K * ad.TF1_Final / 100.0, 0) + 
							                                     ROUND((ad.Current_MABL_K - ROUND(ad.Current_MABL_K * ad.TF1_Final / 100.0, 0)) * ad.TF2_Final / 100.0, 0))
							                                   ) * ad.VatRate / 100.0)
							                         ELSE 0 
							                     END AS LineVAT
							                 FROM AppliedDiscounts ad
							             )
							             UPDATE il
							             SET 
							                 il.N_KOL = flv.TF1_Final,
							                 il.TKHN = flv.TF2_Final,
							                 il.N_MOIN = flv.TotalLineDiscount,
							                 il.IMBAA = CASE 
							                     WHEN @TICMBAA_In = 1 AND sd.CMBAA = 1 AND sd.vra IS NOT NULL THEN 
							                         FLOOR((il.MABL_K - flv.TotalLineDiscount) * sd.vra / 100.0)
							                     ELSE 0 
							                 END
							             FROM dbo.INVO_LST il
							             JOIN FinalLineValues flv ON il.id = flv.invo_lst_id
							             JOIN dbo.STUF_DEF sd ON il.CODE = sd.CODE
							             WHERE il.""NUMBER"" = @numb AND il.TAG = @effective_tgg AND ISNULL(il.JAY, 0) = 0;
							         END
							         ELSE 
							         BEGIN
							             UPDATE il
							             SET 
							                 il.N_KOL = 0,
							                 il.TKHN = 0,
							                 il.N_MOIN = 0,
							                 il.IMBAA = CASE 
							                     WHEN @TICMBAA_In = 1 AND sd.CMBAA = 1 AND sd.vra IS NOT NULL THEN 
							                         FLOOR(il.MABL_K * sd.vra / 100.0)
							                     ELSE 0 
							                 END
							             FROM dbo.INVO_LST il
							             JOIN dbo.STUF_DEF sd ON il.CODE = sd.CODE
							             WHERE il.""NUMBER"" = @numb AND il.TAG = @effective_tgg AND ISNULL(il.JAY, 0) = 0;
							         END
							     END
							     ELSE 
							     BEGIN
							         UPDATE il
							         SET 
							             il.N_KOL = 0,
							             il.TKHN = 0,
							             il.N_MOIN = 0,
							             il.IMBAA = CASE 
							                 WHEN @TICMBAA_In = 1 AND sd.CMBAA = 1 AND sd.vra IS NOT NULL THEN 
							                     FLOOR(il.MABL_K * sd.vra / 100.0)
							                 ELSE 0 
							             END
							         FROM dbo.INVO_LST il
							         JOIN dbo.STUF_DEF sd ON il.CODE = sd.CODE
							         WHERE il.""NUMBER"" = @numb AND il.TAG = @effective_tgg AND ISNULL(il.JAY, 0) = 0;
							     END
							 
							     SELECT 
							         @stf_total_discount = COALESCE(SUM(N_MOIN), 0), 
							         @MLBAA_total_vat = COALESCE(SUM(IMBAA), 0)
							     FROM dbo.INVO_LST 
							     WHERE ""NUMBER"" = @numb AND TAG = @effective_tgg;
							 
							     -- 6. به‌روزرسانی نهایی سرفصل فاکتور HEAD_LST
							     UPDATE dbo.HEAD_LST 
							     SET 
							         MBAA = @MLBAA_total_vat, 
							         TAKHFIF = @stf_total_discount
							     WHERE ""NUMBER"" = @numb AND TAG = @tgg;
							 
							     IF @tgg = 13
							     BEGIN
							         UPDATE dbo.HEAD_LST 
							         SET 
							             MBAA = @MLBAA_total_vat, 
							             TAKHFIF = @stf_total_discount
							         WHERE ""NUMBER"" = @numb AND TAG = 2;
							     END
							 
							     IF @@ERROR <> 0
							     BEGIN
							         IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
							         SET @ErrorMessage = N'خطایی در حین عملیات به‌روزرسانی رخ داد و تغییرات بازگردانده شد. کد خطای SQL: ' + CAST(@@ERROR AS NVARCHAR(10));
							         RAISERROR(@ErrorMessage, 16, 1);
							         RETURN -99; 
							     END
							 
							     IF @@TRANCOUNT > 0 COMMIT TRANSACTION;
							     RETURN 0; -- موفقیت
							 
							 END
							 ";
}
