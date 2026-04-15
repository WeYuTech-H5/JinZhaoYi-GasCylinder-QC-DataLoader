using System.Globalization;
using System.Text.RegularExpressions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

/// <summary>
/// 負責解析 Quant.txt 檔案內容並轉成系統內部使用的 ParsedQuantFile
/// </summary>
/// <remarks>
/// 此類別專注在文字檔解析，不負責資料庫寫入與商業流程控制。
/// 
/// 主要職責如下：
/// 1. 讀取 Quant.txt 全部內容
/// 2. 解析必要 header 欄位
/// 3. 從 Misc 中擷取 LOT
/// 4. 從檔案路徑中擷取 SampleNo
/// 5. 解析 compound 區塊並轉成強型別資料
/// 
/// 此類別的輸出是 ParsedQuantFile，供後續 orchestrator 與 repository 使用。
/// </remarks>
public sealed partial class QuantParser : IQuantParser
{
    /// <summary>
    /// Quant 檔案內日期時間解析所使用的文化設定
    /// </summary>
    /// <remarks>
    /// Quant 原始資料的時間格式依賴 en-US 文化格式，
    /// 因此需固定使用 en-US 避免在不同系統地區設定下解析失敗。
    /// </remarks>
    private static readonly CultureInfo QuantCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// 解析單一 Quant 檔案
    /// </summary>
    /// <param name="candidate">待解析的 Quant 檔案資訊</param>
    /// <param name="cancellationToken">取消權杖</param>
    /// <returns>解析完成的 ParsedQuantFile</returns>
    /// <remarks>
    /// 解析流程如下：
    /// 1. 讀取檔案所有行
    /// 2. 解析必要 header
    /// 3. 解析採樣時間
    /// 4. 由 Misc 擷取 LOT
    /// 5. 由路徑擷取 SampleNo
    /// 6. 解析化合物列表
    /// 7. 組成 ParsedQuantFile 回傳
    /// 
    /// 若任何必要欄位缺失或格式不正確，會拋出 InvalidDataException。
    /// </remarks>
    public async Task<ParsedQuantFile> ParseAsync(QuantFileCandidate candidate, CancellationToken cancellationToken)
    {
        // 一次讀取整個 Quant.txt，後續解析 header 與 compound 表格都依賴完整內容
        var lines = await File.ReadAllLinesAsync(candidate.FullPath, cancellationToken);

        // 解析必要 header 欄位
        // Data File 為原始資料檔名稱
        // Acq On 為採樣時間
        // Sample 為樣品描述
        // Misc 內目前包含 LOT 資訊
        var dataFile = ReadRequiredHeader(lines, "Data File", candidate.FullPath);
        var acquiredAt = ParseAcquiredAt(ReadRequiredHeader(lines, "Acq On", candidate.FullPath), candidate.FullPath);
        var sample = ReadRequiredHeader(lines, "Sample", candidate.FullPath);
        var misc = ReadRequiredHeader(lines, "Misc", candidate.FullPath);

        // LOT 目前不是獨立 header，而是埋在 Misc 欄位最後的井字號後面
        var lotNo = ParseLotNo(misc, candidate.FullPath);

        // SampleNo 目前由 .D 資料夾名稱推得，不從檔案內容讀取
        var sampleNo = ParseSampleNo(candidate);

        // 解析 compound 區塊
        var compounds = ParseCompounds(lines);

        return new ParsedQuantFile
        {
            Source = candidate,
            AcquiredAt = acquiredAt,
            DataFile = dataFile,
            Sample = sample,
            Misc = misc,
            LotNo = lotNo,
            SampleNo = sampleNo,
            Compounds = compounds
        };
    }

    /// <summary>
    /// 解析 Quant 檔案中的 compound 區塊
    /// </summary>
    /// <param name="lines">檔案所有文字行</param>
    /// <returns>以 analyte suffix 為 key 的化合物資料集合</returns>
    /// <remarks>
    /// 此方法只會解析符合 compound 資料列格式的行。
    /// 
    /// 處理流程如下：
    /// 1. 使用 regex 篩選 compound 資料列
    /// 2. 將 Quant 名稱正規化
    /// 3. 對應系統 analyte 定義
    /// 4. 解析 retention time、response、concentration
    /// 5. 建立 QuantCompound 並存入 dictionary
    /// 
    /// 若遇到系統未定義的 compound 名稱，會直接略過，不視為錯誤。
    /// </remarks>
    private static IReadOnlyDictionary<string, QuantCompound> ParseCompounds(IEnumerable<string> lines)
    {
        var compounds = new Dictionary<string, QuantCompound>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            // 只解析符合表格格式的資料列
            // 其它例如 header、空白行或非 compound 區段內容都會被排除
            var match = CompoundLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            // 將 Quant 檔案中的名稱轉成系統內部統一格式
            var quantName = CompoundMap.NormalizeQuantName(match.Groups["name"].Value);

            // 若無法對應到系統 analyte 定義則略過
            // 這代表該欄位不是目前系統需要追蹤的成分
            if (!CompoundMap.TryGetByQuantName(quantName, out var analyte))
            {
                continue;
            }

            // 解析保留時間與 response
            var retentionTime = decimal.Parse(match.Groups["rt"].Value, CultureInfo.InvariantCulture);
            var response = decimal.Parse(match.Groups["response"].Value, CultureInfo.InvariantCulture);

            // Conc 欄位可能是數字、N.D. 或 No Calib
            // 因此先取字串再判斷是否可轉 decimal
            var concentrationText = match.Groups["conc"].Value.Trim();

            // 第一版 raw row 雖然不一定會使用 concentration，
            // 但仍保留解析結果，方便測試、追查與後續規則擴充
            var concentration = decimal.TryParse(
                concentrationText,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsedConcentration)
                    ? parsedConcentration
                    : (decimal?)null;

            // 以 analyte.Suffix 作為 key，方便後續對應固定欄位
            compounds[analyte.Suffix] = new QuantCompound(analyte, retentionTime, response, concentration);
        }

        return compounds;
    }

    /// <summary>
    /// 讀取指定名稱的必要 header 欄位
    /// </summary>
    /// <param name="lines">檔案所有文字行</param>
    /// <param name="name">header 名稱</param>
    /// <param name="path">檔案路徑，供錯誤訊息使用</param>
    /// <returns>header 對應的值</returns>
    /// <remarks>
    /// 此方法會從所有行中尋找指定 header 名稱開頭的行，
    /// 並取冒號後面的值作為結果。
    /// 
    /// 若找不到指定 header，視為檔案格式不完整，直接拋出例外。
    /// </remarks>
    private static string ReadRequiredHeader(IEnumerable<string> lines, string name, string path)
    {
        var prefix = $"{name}";

        foreach (var line in lines)
        {
            // Quant header 可能有前置空白，因此先 TrimStart 再判斷
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // header 格式預期為 Name: Value
            // 若找到冒號則取後面的內容作為值
            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex >= 0)
            {
                return trimmed[(separatorIndex + 1)..].TrimEnd();
            }
        }

        throw new InvalidDataException($"Quant file '{path}' is missing header '{name}'.");
    }

    /// <summary>
    /// 解析 Acq On 欄位文字為 DateTime
    /// </summary>
    /// <param name="value">Acq On 原始文字</param>
    /// <param name="path">檔案路徑，供錯誤訊息使用</param>
    /// <returns>解析後的採樣時間</returns>
    /// <remarks>
    /// 此方法固定使用 QuantCulture，也就是 en-US，
    /// 以避免不同主機文化設定導致解析結果不一致。
    /// 
    /// 若格式不合法則拋出例外。
    /// </remarks>
    private static DateTime ParseAcquiredAt(string value, string path)
    {
        if (DateTime.TryParse(value, QuantCulture, DateTimeStyles.AllowWhiteSpaces, out var acquiredAt))
        {
            return acquiredAt;
        }

        throw new InvalidDataException($"Quant file '{path}' has invalid Acq On value '{value}'.");
    }

    /// <summary>
    /// 從 Misc 欄位擷取 LOT 編號
    /// </summary>
    /// <param name="misc">Misc 原始文字</param>
    /// <param name="path">檔案路徑，供錯誤訊息使用</param>
    /// <returns>LOT 編號</returns>
    /// <remarks>
    /// 目前 LOT 的格式預期出現在 Misc 最後方，
    /// 形式類似 #20251030001。
    /// 
    /// 若 Misc 中找不到符合格式的 LOT 標記，則拋出例外。
    /// </remarks>
    private static string ParseLotNo(string misc, string path)
    {
        // LOT 來源為 Misc 最後的井字號後方內容
        var match = LotNoRegex().Match(misc);
        if (!match.Success)
        {
            throw new InvalidDataException($"Quant file '{path}' Misc value does not contain a LOT marker: '{misc}'.");
        }

        return match.Groups["lot"].Value;
    }

    /// <summary>
    /// 從 Quant 檔案路徑解析 SampleNo
    /// </summary>
    /// <param name="candidate">待解析的 Quant 檔案資訊</param>
    /// <returns>SampleNo</returns>
    /// <remarks>
    /// 目前 SampleNo 不是從檔案內容取得，
    /// 而是由 Quant.txt 所在的 .D 資料夾名稱解析。
    /// 
    /// 例如資料夾名稱為 abc_903.D，則 SampleNo 為 903。
    /// 
    /// 若路徑格式不符合預期則拋出例外。
    /// </remarks>
    private static int ParseSampleNo(QuantFileCandidate candidate)
    {
        // 取 Quant.txt 上一層資料夾名稱，例如 xxx_903.D
        var directoryName = Path.GetFileName(Path.GetDirectoryName(candidate.FullPath));

        var match = SampleNoRegex().Match(directoryName ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidDataException($"Quant path '{candidate.FullPath}' does not contain a sample number like '_903.D'.");
        }

        return int.Parse(match.Groups["sampleNo"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// LOT 解析 regex
    /// </summary>
    /// <remarks>
    /// 用於從 Misc 最後方擷取井字號後的 LOT 編號。
    /// 
    /// 範例：
    /// abc test #20251030001
    /// 
    /// 會擷取出 20251030001。
    /// </remarks>
    [GeneratedRegex(@"#(?<lot>[A-Za-z0-9_-]+)\s*$", RegexOptions.Compiled)]
    private static partial Regex LotNoRegex();

    /// <summary>
    /// SampleNo 解析 regex
    /// </summary>
    /// <remarks>
    /// 用於從 .D 資料夾名稱中擷取底線後的數字。
    /// 
    /// 範例：
    /// TEST_903.D
    /// 
    /// 會擷取出 903。
    /// </remarks>
    [GeneratedRegex(@"_(?<sampleNo>\d+)\.D$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SampleNoRegex();

    /// <summary>
    /// Compound 資料列解析 regex
    /// </summary>
    /// <remarks>
    /// 用於辨識 Quant 檔案中化合物表格的資料列。
    /// 
    /// 目前會擷取以下欄位：
    /// 1. name
    /// 2. rt
    /// 3. qion
    /// 4. response
    /// 5. conc
    /// 
    /// conc 欄位允許數字、N.D. 與 No Calib。
    /// </remarks>
    [GeneratedRegex(@"^\s*\d+\)\s+(?<name>.+?)\s+(?<rt>\d+\.\d{3})\s+(?:(?<qion>\d+)\s+)?(?<response>\d+)\s+(?<conc>\d+(?:\.\d+)?|N\.D\.|No Calib)\b", RegexOptions.Compiled)]
    private static partial Regex CompoundLineRegex();
}