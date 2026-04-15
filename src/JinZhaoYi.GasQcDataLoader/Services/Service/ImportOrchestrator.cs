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
    IRawRowFactory rawRowFactory,
    ICalculationService calculationService,
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
        var messages = new List<string>();
        logger.LogInformation("開始匯入 Quant 檔案：{QuantFile}。", candidate.FullPath);

        // 將 Quant.txt 解析為系統內部可處理的中立資料模型
        var parsed = await parser.ParseAsync(candidate, cancellationToken);

        // 依 LotNo 查詢製造 LOT 主檔，單檔模式只會查一筆 LotNo
        var lots = await repository.GetLotsByLotNoAsync([parsed.LotNo], cancellationToken);

        // 若找不到對應的 LOT，代表主檔資料不完整，不能繼續匯入
        if (!lots.TryGetValue(parsed.LotNo, out var lot))
        {
            messages.Add($"ZZ_NF_GAS_MFG_LOT 查無 LOT：{parsed.LotNo}。");
            return new ImportResult(candidate.DayFolderPath, 1, 0, _options.DryRun, false, messages);
        }

        // 依採樣時間取得對應 RF 資料，供後續計算使用
        var rf = await repository.GetLatestRfAsync(parsed.AcquiredAt, cancellationToken);

        // 若找不到可用 RF 資料，則無法進行後續計算
        if (rf is null)
        {
            messages.Add("ZZ_NF_GAS_QC_RF 查無可用 RF 資料。");
            return new ImportResult(candidate.DayFolderPath, 1, 0, _options.DryRun, false, messages);
        }

        // 匯入日期優先從日期資料夾名稱解析，解析失敗時退回使用採樣日期
        var importDate = ParseImportDate(candidate.DayFolderPath, parsed.AcquiredAt.Date);

        // 建立單檔寫入集合，僅會產生一筆 STD 或 PORT raw 資料
        var writeSet = BuildSingleFileWriteSet(parsed, lot);

        messages.Add($"Quant 檔案解析完成：{candidate.FullPath}。");
        messages.Add($"預計 raw 資料列數：{writeSet.StdRawRows.Count + writeSet.PortRawRows.Count}。");

        // DryRun 模式只驗證流程與資料組裝，不實際寫入資料庫
        if (_options.DryRun)
        {
            messages.Add("DryRun=true，本次未寫入資料庫，也未搬移檔案。");
            return new ImportResult(candidate.DayFolderPath, 1, writeSet.TotalRows, true, true, messages);
        }

        // 正式寫入由 repository 統一負責 transaction 管理
        await repository.ExecuteImportAsync(writeSet, rf, importDate, cancellationToken);

        messages.Add("資料庫交易已提交。");
        return new ImportResult(candidate.DayFolderPath, 1, writeSet.TotalRows, false, true, messages);
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
            parsedFiles.Add(await parser.ParseAsync(candidate, cancellationToken));
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

        // 匯入日期優先使用日期資料夾名稱，若格式不符則退回最早採樣日期
        var importDate = ParseImportDate(dayFolderPath, parsedFiles.Min(file => file.AcquiredAt).Date);

        logger.LogInformation("開始取得 RF 資料，基準時間={AsOf}。", parsedFiles.Min(file => file.AcquiredAt));

        // RF 目前以最早採樣時間往前找最近一筆可用資料
        // 若未來規則改變，可在 repository 端替換邏輯
        var rf = await repository.GetLatestRfAsync(parsedFiles.Min(file => file.AcquiredAt), cancellationToken);

        if (rf is null)
        {
            messages.Add("ZZ_NF_GAS_QC_RF 查無可用 RF 資料。");
            return new ImportResult(dayFolderPath, orderedCandidates.Length, 0, _options.DryRun, false, messages);
        }

        // 建立整批資料的寫入集合，內含 STD、PORT 與各種衍生計算列
        var writeSet = BuildWriteSet(parsedFiles, lots, rf);

        messages.Add($"Quant 檔案解析完成，檔案數：{orderedCandidates.Length}。");
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

    /// <summary>
    /// 建立單一檔案的寫入資料集合
    /// </summary>
    /// <param name="parsed">已解析完成的 Quant 檔案</param>
    /// <param name="lot">對應的製造 LOT 主檔</param>
    /// <returns>寫入資料集合</returns>
    /// <remarks>
    /// 單檔模式下只會建立一筆 raw row，
    /// 並依來源類型決定放入 STD raw 或 PORT raw 清單。
    /// </remarks>
    private ImportWriteSet BuildSingleFileWriteSet(ParsedQuantFile parsed, MfgLot lot)
    {
        var writeSet = new ImportWriteSet();

        // 建立單筆 raw row，STD 與 PORT 都使用相同的 Id 產生規則
        var rawRow = rawRowFactory.Create(parsed, lot, CreateRawId(parsed));

        // 根據來源決定寫入 STD raw 或 PORT raw
        if (parsed.Source.SourceKind == QuantSourceKind.Std)
        {
            writeSet.StdRawRows.Add(rawRow);
        }
        else
        {
            writeSet.PortRawRows.Add(rawRow);
        }

        return writeSet;
    }

    /// <summary>
    /// 建立整批匯入的寫入資料集合
    /// </summary>
    /// <param name="parsedFiles">已解析的 Quant 檔案集合</param>
    /// <param name="lots">LOT 主檔對照資料</param>
    /// <param name="rf">RF 資料</param>
    /// <returns>寫入資料集合</returns>
    /// <remarks>
    /// 此方法是整批匯入的核心邏輯。
    /// 
    /// 流程如下：
    /// 1. 先依採樣時間、Port、檔名排序
    /// 2. 將相鄰且屬於同一群組條件的檔案分成同組
    /// 3. 每組先轉成 raw row
    /// 4. STD 組依條件產生 AVG 與 RPD
    /// 5. PORT 組依 activeStdAverage 計算 PPB，並產生 AVG 與 RPD
    /// 
    /// activeStdAverage 代表目前可供 PORT 使用的 STD 平均值，
    /// 因此 PORT 的計算會依賴先前處理過的 STD 組結果。
    /// </remarks>
    private ImportWriteSet BuildWriteSet(
        IReadOnlyCollection<ParsedQuantFile> parsedFiles,
        IReadOnlyDictionary<string, MfgLot> lots,
        QcDataRow rf)
    {
        var writeSet = new ImportWriteSet();

        // 先將檔案排序，確保後續分組與計算順序穩定一致
        // 排序規則先看採樣時間，再看 Port，最後以檔名穩定排序
        var orderedFiles = parsedFiles
            .OrderBy(file => file.AcquiredAt)
            .ThenBy(file => file.Source.Port, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Source.DataFilename, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // activeStdAverage 代表目前可供 PORT 計算使用的最新 STD AVG
        QcDataRow? activeStdAverage = null;

        // previousStdAverage 目前僅保留狀態，尚未在 writeSet 中實際使用
        // 後續若需要做 STD 間的額外比較或 QC 計算可直接延伸
        QcDataRow? previousStdAverage = null;

        foreach (var group in BuildContiguousGroups(orderedFiles))
        {
            // 每一組資料先全部轉成 raw row
            // 同組內的每個檔案都需要對應到一筆 raw 資料
            var rawRows = group
                .Select(file => rawRowFactory.Create(file, lots[file.LotNo], CreateRawId(file)))
                .ToList();

            // 若為 STD 組，先寫入 STD raw，再依筆數決定是否產生 AVG 與 RPD
            if (group[0].Source.SourceKind == QuantSourceKind.Std)
            {
                // STD raw 一律保留
                writeSet.StdRawRows.AddRange(rawRows);

                // 少於兩筆資料時無法進行兩筆比較計算，因此略過 AVG 與 RPD
                if (rawRows.Count < 2)
                {
                    logger.LogWarning("STD 群組資料不足兩筆，略過 AVG/RPD 計算：時間={Time}。", group[0].AcquiredAt);
                    continue;
                }

                // 取最後兩筆 raw 資料作為 AVG 與 RPD 的計算基礎
                var (first, second) = LastTwo(rawRows);

                var stdAverage = calculationService.CreateAverageRow($"AVG({first.Id}:{second.Id})", first, second);
                var stdRpd = calculationService.CreateRpdRow($"RPD({first.Id}:{second.Id})", first, second);

                writeSet.StdAverageRows.Add(stdAverage);
                writeSet.StdRpdRows.Add(stdRpd);

                // 若已有前一個 STD AVG，則可進一步建立 STD_QC 計算
                // 目前此計算結果尚未確認對應的資料表，因此僅保留呼叫，不加入 writeSet
                if (activeStdAverage is not null)
                {
                    _ = calculationService.CreateStdQcRow($"QC({stdAverage.Id},{activeStdAverage.Id})", activeStdAverage, stdAverage);
                }

                // 更新 STD AVG 狀態，供後續 PORT 組使用
                previousStdAverage = activeStdAverage;
                activeStdAverage = stdAverage;
                continue;
            }

            // 若為 PORT 組且目前存在可用的 STD AVG，則先對 PORT raw 補算 ppb
            if (activeStdAverage is not null)
            {
                foreach (var rawRow in rawRows)
                {
                    // 這裡先在記憶體中補算 PORT raw 的 ppb 欄位
                    // 正式寫入時若 DB 端有更完整規則，仍可由 DB 重新計算
                    calculationService.ApplyPortRawPpb(rawRow, rf, activeStdAverage);
                }
            }

            // PORT raw 一律保留
            writeSet.PortRawRows.AddRange(rawRows);

            // 少於兩筆資料時無法計算 AVG、RPD 與後續 PPB 平均值
            if (rawRows.Count < 2)
            {
                logger.LogWarning("PORT 群組資料不足兩筆，略過 AVG/PPB/RPD 計算：Port={Port}，時間={Time}。", group[0].Source.Port, group[0].AcquiredAt);
                continue;
            }

            // 取最後兩筆 PORT raw 資料作為 AVG 與 RPD 的計算基礎
            var (portFirst, portSecond) = LastTwo(rawRows);

            var portAverage = calculationService.CreateAverageRow($"AVG({portFirst.Id}:{portSecond.Id})", portFirst, portSecond);
            var portRpd = calculationService.CreateRpdRow($"RPD({portFirst.Id}:{portSecond.Id})", portFirst, portSecond);

            // PORT 衍生資料分別寫入不同清單，供後續 repository 寫入不同資料表
            writeSet.PortAverageRows.Add(portAverage);
            writeSet.PortRpdRows.Add(portRpd);

            // 只有在存在 STD AVG 時，才可進一步建立 PORT PPB 資料
            if (activeStdAverage is not null)
            {
                var portPpb = calculationService.CreatePortPpbRow($"ppb({portAverage.Si0Id})", portAverage, rf, activeStdAverage);
                writeSet.PortPpbRows.Add(portPpb);
            }
            else
            {
                // 若當前批次沒有任何先前 STD AVG，則無法在目前上下文中估算 PORT PPB
                logger.LogWarning("PORT 群組在目前 DryRun 上下文中沒有可用 STD AVG，略過 PPB 預估：Port={Port}，時間={Time}。", group[0].Source.Port, group[0].AcquiredAt);
            }
        }

        // 目前 previousStdAverage 僅保留變數狀態，避免未來擴充時重構成本增加
        _ = previousStdAverage;
        return writeSet;
    }

    /// <summary>
    /// 將已排序的檔案依連續條件切成多個群組
    /// </summary>
    /// <param name="orderedFiles">已排序的檔案集合</param>
    /// <returns>依條件切分後的群組清單</returns>
    /// <remarks>
    /// 此方法假設輸入資料已先排序。
    /// 
    /// 分組規則是比較相鄰兩筆資料是否屬於同一組。
    /// 若不是同一組，則切開建立新群組。
    /// 
    /// 這樣的設計可保留原始順序，並避免單純以 GroupBy 造成順序被打散。
    /// </remarks>
    private static IReadOnlyList<IReadOnlyList<ParsedQuantFile>> BuildContiguousGroups(IReadOnlyList<ParsedQuantFile> orderedFiles)
    {
        var groups = new List<IReadOnlyList<ParsedQuantFile>>();
        var current = new List<ParsedQuantFile>();

        foreach (var file in orderedFiles)
        {
            // 當目前群組已有資料，且新資料不屬於同一組時，先封存目前群組再開新群組
            if (current.Count > 0 && !IsSameGroup(current[^1], file))
            {
                groups.Add(current);
                current = [];
            }

            current.Add(file);
        }

        // 將最後一組補進結果
        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    /// <summary>
    /// 判斷兩筆 ParsedQuantFile 是否屬於同一群組
    /// </summary>
    /// <param name="left">左側資料</param>
    /// <param name="right">右側資料</param>
    /// <returns>若為同一組則回傳 true，否則回傳 false</returns>
    /// <remarks>
    /// 同一組的條件如下：
    /// 1. 來源類型相同
    /// 2. Port 相同
    /// 3. LotNo 相同
    /// 
    /// 只要任一條件不同，就視為不同組。
    /// </remarks>
    private static bool IsSameGroup(ParsedQuantFile left, ParsedQuantFile right) =>
        left.Source.SourceKind == right.Source.SourceKind &&
        string.Equals(left.Source.Port, right.Source.Port, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.LotNo, right.LotNo, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 取得集合中的最後兩筆 QcDataRow
    /// </summary>
    /// <param name="rows">資料列集合</param>
    /// <returns>最後兩筆資料</returns>
    /// <remarks>
    /// 此方法假設呼叫端已保證 rows 至少有兩筆資料。
    /// 主要用於 AVG 與 RPD 計算。
    /// </remarks>
    private static (QcDataRow First, QcDataRow Second) LastTwo(IReadOnlyList<QcDataRow> rows) =>
        (rows[^2], rows[^1]);

    /// <summary>
    /// 建立 raw Id
    /// </summary>
    /// <param name="parsed">已解析的 Quant 檔案</param>
    /// <returns>raw Id</returns>
    /// <remarks>
    /// STD 與 PORT raw table 都使用相同格式：
    /// yyyyMMdd 加上三位 SampleNo。
    /// </remarks>
    private static string CreateRawId(ParsedQuantFile parsed) =>
        $"{parsed.AcquiredAt:yyyyMMdd}{parsed.SampleNo:000}";

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
