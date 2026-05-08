using System.Globalization;
using JinZhaoYi.GasQcDataLoader.Configuration;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;
using Microsoft.Extensions.Options;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

/// <summary>
/// 負責協調 Quant 檔案的匯入流程
/// </summary>
/// <remarks>
/// 此類別的職責是串接掃描、解析、驗證、計算與資料寫入流程。
/// 
/// 整體流程大致如下：
/// 先取得檔案內容並解析成中立模型，接著驗證必要主檔資料是否存在，
/// 再依來源類型與資料群組建立對應的寫入資料集合，最後交由 repository
/// 以 transaction 方式統一寫入資料庫。
/// 
/// 此類別本身不直接負責 SQL 寫入細節，而是專注在匯入流程控制與資料組裝。
/// </remarks>
public sealed class ImportOrchestrator(
    IGasFolderScanner scanner,
    IQuantParser parser,
    IDapperRepository repository,
    IImportWriteSetBuilder writeSetBuilder,
    IOptions<SchedulerOptions> options,
    ILogger<ImportOrchestrator> logger) : IImportOrchestrator
{
    /// <summary>
    /// 排程相關設定
    /// </summary>
    /// <remarks>
    /// 目前主要用於控制 DryRun 等匯入行為。
    /// </remarks>
    private readonly SchedulerOptions _options = options.Value;

    /// <summary>
    /// 匯入單一 Quant 檔案
    /// </summary>
    /// <param name="candidate">待匯入的 Quant 檔案資訊</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>匯入結果</returns>
    /// <remarks>
    /// 此方法適合單檔處理情境。
    /// 
    /// 流程如下：
    /// 1. 解析單一 Quant 檔案
    /// 2. 驗證該檔案對應的 LOT 是否存在
    /// 3. 依檔案取得對應 RF 資料
    /// 4. 建立單檔寫入資料集合
    /// 5. 若非 DryRun 則執行資料庫匯入
    /// </remarks>
    public async Task<ImportResult> ImportQuantFileAsync(QuantFileCandidate candidate, CancellationToken cancellationToken)
    {
        logger.LogInformation("開始匯入 Quant 檔案：{QuantFile}。", candidate.FullPath);
        return await ImportCandidatesAsync([candidate], cancellationToken);
    }

    /// <summary>
    /// 匯入整個日期資料夾底下的 Quant 檔案
    /// </summary>
    /// <param name="dayFolderPath">日期資料夾路徑</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>匯入結果</returns>
    /// <remarks>
    /// 此方法適合批次處理單日資料。
    /// 
    /// 主要流程如下：
    /// 1. 掃描日期資料夾內所有 Quant.txt
    /// 2. 全部解析為 ParsedQuantFile
    /// 3. 統一檢查所有 LOT 是否存在
    /// 4. 取得該批資料使用的 RF
    /// 5. 建立整批寫入資料集合
    /// 6. 若非 DryRun 則以單一 transaction 匯入
    /// 
    /// 任何一個必要條件不成立時，整批停止，不做部分寫入。
    /// </remarks>
    public async Task<ImportResult> ImportDayFolderAsync(string dayFolderPath, CancellationToken cancellationToken)
    {
        logger.LogInformation("開始掃描日期資料夾底下的 Quant 檔案：{DayFolder}。", dayFolderPath);

        // 掃描日期資料夾內所有可找到的 Quant.txt
        // 一個日期資料夾底下可能有 STD、PORT X 與多層 .D 目錄
        var candidates = scanner.FindQuantFiles(dayFolderPath);

        return await ImportCandidatesAsync(candidates, cancellationToken);
    }

    /// <summary>
    /// 匯入同一輪掃描到的一批 Quant 檔案
    /// </summary>
    /// <param name="candidates">已確認穩定且待匯入的 Quant 檔案集合</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>匯入結果</returns>
    /// <remarks>
    /// 此方法保留掃描器已判斷過的穩定檔案清單，不會重新掃描日期資料夾。
    /// 這樣可以避免同一輪匯入時把尚未穩定的新檔案一起吃進來。
    /// </remarks>
    public async Task<ImportResult> ImportCandidatesAsync(IReadOnlyCollection<QuantFileCandidate> candidates, CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dayFolderPath = orderedCandidates.FirstOrDefault()?.DayFolderPath ?? string.Empty;

        if (orderedCandidates.Length == 0)
        {
            messages.Add("未找到 Quant.txt 檔案。");
            return new ImportResult(dayFolderPath, 0, 0, _options.DryRun, true, messages);
        }

        var parsedFiles = new List<ParsedQuantFile>();
        logger.LogInformation("開始解析本輪穩定 Quant 檔案，檔案數={Count}。", orderedCandidates.Length);

        foreach (var candidate in orderedCandidates)
        {
            // 每個檔案先轉成中立模型，後續才依來源與群組決定寫入哪張表
            ParsedQuantFile parsed;
            try
            {
                parsed = await parser.ParseAsync(candidate, cancellationToken);
            }
            catch (InvalidDataException ex) when (IsIgnorableQuantData(ex))
            {
                logger.LogInformation(ex, "Skipping Quant file because it is not formal import data. QuantPath={QuantPath}.", candidate.FullPath);
                messages.Add($"已略過非正式 Quant 資料：{candidate.FullPath}。");
                continue;
            }

            if (!IsValidCylinderLot(parsed.LotNo))
            {
                logger.LogInformation(
                    "Skipping Quant file because LOT is not 11 digits. QuantPath={QuantPath}, LotNo={LotNo}.",
                    candidate.FullPath,
                    parsed.LotNo);
                messages.Add($"已略過非 11 位數字 LOT：{parsed.LotNo}，Quant={candidate.FullPath}。");
                continue;
            }

            parsedFiles.Add(parsed);
        }

        if (parsedFiles.Count == 0)
        {
            messages.Add("本輪沒有符合正式規則的 Quant.txt 可匯入。");
            return new ImportResult(dayFolderPath, orderedCandidates.Length, 0, _options.DryRun, true, messages);
        }

        logger.LogInformation(
            "開始驗證 LOT 主檔，LOT 數={Count}。",
            parsedFiles.Select(file => file.LotNo).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // 批次查詢所有用到的 LOT，避免逐筆查詢造成額外 IO 成本
        // 只要有任一個 LOT 缺失，就整批停止
        var lots = await repository.GetLotsByLotNoAsync(parsedFiles.Select(file => file.LotNo), cancellationToken);

        var missingLots = parsedFiles
            .Select(file => file.LotNo)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(lotNo => !lots.ContainsKey(lotNo))
            .OrderBy(lotNo => lotNo, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingLots.Length > 0)
        {
            messages.Add($"ZZ_NF_GAS_MFG_LOT 查無 LOT：{string.Join(", ", missingLots)}。");
            return new ImportResult(dayFolderPath, orderedCandidates.Length, 0, _options.DryRun, false, messages);
        }

        var existingIdentityIds = await repository.GetExistingRawIdentityIdsAsync(
            parsedFiles.Select(file => RawDataIdentity.FromParsed(file, lots[file.LotNo])).ToArray(),
            cancellationToken);
        var newParsedFiles = parsedFiles
            .Where(file => !existingIdentityIds.Contains(RawDataIdentity.FromParsed(file, lots[file.LotNo]).ToStableId()))
            .ToArray();

        var duplicateCount = parsedFiles.Count - newParsedFiles.Length;
        if (duplicateCount > 0)
        {
            logger.LogInformation("Skipped duplicate Quant files by DB raw identity. Count={Count}.", duplicateCount);
            messages.Add($"已略過 DB 中既有的正式資料筆數：{duplicateCount}。");
        }

        if (newParsedFiles.Length == 0)
        {
            messages.Add("本輪資料 DB 已存在，未新增匯入。");
            return new ImportResult(dayFolderPath, orderedCandidates.Length, 0, _options.DryRun, true, messages);
        }

        // 匯入日期優先使用日期資料夾名稱，若格式不符則退回最早採樣日期
        var importDate = ParseImportDate(dayFolderPath, newParsedFiles.Min(file => file.AcquiredAt).Date);

        logger.LogInformation("開始取得 RF 資料，基準時間={AsOf}。", newParsedFiles.Min(file => file.AcquiredAt));

        // RF 目前以最早採樣時間往前找最近一筆可用資料
        // 若未來規則改變，可在 repository 端替換邏輯
        var rf = await repository.GetLatestRfAsync(newParsedFiles.Min(file => file.AcquiredAt), cancellationToken);

        if (rf is null)
        {
            messages.Add("ZZ_NF_GAS_QC_RF 查無可用 RF 資料。");
            return new ImportResult(dayFolderPath, orderedCandidates.Length, 0, _options.DryRun, false, messages);
        }

        // 建立整批資料的寫入集合，內含 STD、PORT 與各種衍生計算列
        var writeSet = writeSetBuilder.BuildWriteSet(newParsedFiles, lots, rf);

        messages.Add($"Quant 檔案解析完成，正式檔案數：{newParsedFiles.Length}。");
        messages.Add($"預計資料庫資料列數：{writeSet.TotalRows}。");

        // DryRun 模式下不實際寫入資料庫，僅用來驗證解析與計算流程
        if (_options.DryRun)
        {
            messages.Add("DryRun=true，本次未寫入資料庫。");
            return new ImportResult(dayFolderPath, orderedCandidates.Length, writeSet.TotalRows, true, true, messages);
        }

        // 正式模式交由 repository 以單一 transaction 完成寫入
        await repository.ExecuteImportAsync(writeSet, rf, importDate, cancellationToken);

        messages.Add("資料庫交易已提交。");
        return new ImportResult(dayFolderPath, orderedCandidates.Length, writeSet.TotalRows, false, true, messages);
    }

    private static bool IsIgnorableQuantData(InvalidDataException ex) =>
        ex.Message.Contains("Misc value does not contain a LOT marker", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("does not contain a sample number", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidCylinderLot(string lotNo) =>
        lotNo.Length == 11 && lotNo.All(char.IsDigit);

    /// <summary>
    /// 從日期資料夾名稱解析匯入日期
    /// </summary>
    /// <param name="dayFolderPath">日期資料夾路徑</param>
    /// <param name="fallback">解析失敗時的預設日期</param>
    /// <returns>匯入日期</returns>
    /// <remarks>
    /// 會嘗試從資料夾名稱解析 yyyyMMdd 格式。
    /// 
    /// 若資料夾名稱不符合預期格式，則退回使用 fallback，
    /// 避免因資料夾命名異常導致整個匯入流程失敗。
    /// </remarks>
    private static DateTime ParseImportDate(string dayFolderPath, DateTime fallback)
    {
        var folderName = Path.GetFileName(dayFolderPath);

        return DateTime.TryParseExact(
            folderName,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
                ? parsed
                : fallback;
    }
}
