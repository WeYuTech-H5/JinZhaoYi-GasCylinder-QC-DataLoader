using System.Globalization;
using System.Text.RegularExpressions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

/// <summary>
/// 掃描金兆益機台輸出的資料夾，找出「穩定可處理」的 Quant.txt 檔案
/// </summary>
/// <remarks>
/// 設計重點：
/// 1. 機台寫檔是「非原子操作」，會有半套資料問題
/// 2. 必須透過「最後寫入時間 + 延遲」判斷是否穩定
/// 3. 資料夾結構有固定規則：
///    yyyyMMdd / (STD | PORT X) / *.D / Quant.txt
/// </remarks>
public sealed partial class GasFolderScanner : IGasFolderScanner
{
    /// <summary>
    /// 取得已經「穩定」的日期資料夾（yyyyMMdd）
    /// </summary>
    /// <param name="watchRoot">監控根目錄</param>
    /// <param name="stableAge">穩定時間（例如：5 分鐘）</param>
    /// <returns>符合條件的日期資料夾路徑清單</returns>
    public IReadOnlyList<string> FindStableDayFolders(string watchRoot, TimeSpan stableAge)
    {
        // Root 不存在直接回空，避免例外
        if (!Directory.Exists(watchRoot))
        {
            return [];
        }

        // 計算穩定時間 cutoff（現在時間 - 延遲）
        var cutoff = DateTime.Now.Subtract(stableAge);

        return Directory.EnumerateDirectories(watchRoot)
            // 只處理 yyyyMMdd 命名格式的資料夾（避免誤掃）
            .Where(path => DayFolderRegex().IsMatch(Path.GetFileName(path)))
            // 判斷資料夾是否已停止寫入（穩定）
            .Where(path => Directory.GetLastWriteTime(path) <= cutoff)
            // 固定排序，確保處理順序穩定（避免 DB 時間亂序）
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 取得所有「穩定可處理」的 Quant.txt 檔案
    /// </summary>
    /// <remarks>
    /// ⚠重點：
    /// - Quant.txt 可能還在寫入中（尤其是 .D 資料夾）
    /// - 必須同時確認：
    ///   1. 檔案最後寫入時間
    ///   2. 所在資料夾最後寫入時間
    /// 
    /// 排序策略：
    /// - 依照資料夾時間（yyyyMMdd HHmm）
    /// - 避免 DB CREATE_TIME 出現亂序
    /// </remarks>
    public IReadOnlyList<QuantFileCandidate> FindStableQuantFiles(string watchRoot, TimeSpan stableAge)
    {
        var cutoff = DateTime.Now.Subtract(stableAge);

        return FindStableDayFolders(watchRoot, TimeSpan.Zero)
            .SelectMany(FindQuantFiles)
            // 過濾仍在寫入中的資料
            .Where(candidate => IsStable(candidate, cutoff))
            // 依資料時間排序（業務要求：舊 → 新）
            .OrderBy(GetCandidateSortTime)
            .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 掃描單一日期資料夾中的 Quant.txt 檔案
    /// </summary>
    /// <param name="dayFolderPath">yyyyMMdd 資料夾</param>
    /// <returns>Quant 檔案候選清單</returns>
    public IReadOnlyList<QuantFileCandidate> FindQuantFiles(string dayFolderPath)
    {
        if (!Directory.Exists(dayFolderPath))
        {
            return [];
        }

        var dayRoot = Path.GetFullPath(dayFolderPath);
        var candidates = new List<QuantFileCandidate>();

        // 掃描 STD / PORT X
        foreach (var topFolder in Directory.EnumerateDirectories(dayRoot))
        {
            var topFolderName = Path.GetFileName(topFolder);

            // 只允許 STD 或 PORT X（避免亂資料）
            if (!TryClassifyTopFolder(topFolderName, out var sourceKind, out var port))
            {
                continue;
            }

            // 每個 .D 資料夾底下的 Quant.txt 都是一筆資料
            foreach (var quantPath in Directory.EnumerateFiles(topFolder, "Quant.txt", SearchOption.AllDirectories))
            {
                var dataFolder = Path.GetDirectoryName(quantPath) ?? topFolder;
                var dataFilename = Path.GetRelativePath(topFolder, quantPath);

                candidates.Add(new QuantFileCandidate(
                    FullPath: Path.GetFullPath(quantPath),
                    DayFolderPath: dayRoot,
                    TopFolderName: topFolderName,
                    SourceKind: sourceKind,
                    Port: port,
                    DataFilename: dataFilename,
                    DataFilepath: dataFolder));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 判斷檔案與資料夾是否已穩定（不再寫入）
    /// </summary>
    /// <remarks>
    /// 為什麼要雙重判斷？
    /// - 有些情況：Quant.txt 已寫完，但 .D 資料夾還在更新
    /// - 或反過來
    /// → 必須兩者都穩定才安全
    /// </remarks>
    private static bool IsStable(QuantFileCandidate candidate, DateTime cutoff)
    {
        // 檔案或資料夾不存在直接視為不穩定
        if (!File.Exists(candidate.FullPath) || !Directory.Exists(candidate.DataFilepath))
        {
            return false;
        }

        return File.GetLastWriteTime(candidate.FullPath) <= cutoff &&
               Directory.GetLastWriteTime(candidate.DataFilepath) <= cutoff;
    }

    /// <summary>
    /// 取得排序時間（優先使用資料夾名稱中的時間）
    /// </summary>
    /// <remarks>
    /// 資料夾命名格式：
    /// [yyyyMMdd HHmm]
    /// 
    /// fallback：
    /// 若解析失敗 → 使用資料夾 LastWriteTime
    /// </remarks>
    private static DateTime GetCandidateSortTime(QuantFileCandidate candidate)
    {
        var folderName = Path.GetFileName(candidate.DataFilepath);
        var match = DataFolderTimeRegex().Match(folderName);

        if (match.Success &&
            DateTime.TryParseExact(
                match.Groups["value"].Value,
                "yyyyMMdd HHmm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        // fallback（避免 parse 失敗導致排序亂掉）
        return Directory.GetLastWriteTime(candidate.DataFilepath);
    }

    /// <summary>
    /// 判斷頂層資料夾是 STD 還是 PORT X
    /// </summary>
    /// <remarks>
    /// 現場資料容錯：
    /// - 曾出現 "PROT 11" typo → 視為 "PORT 11"
    /// </remarks>
    private static bool TryClassifyTopFolder(string topFolderName, out QuantSourceKind sourceKind, out string port)
    {
        if (topFolderName.Equals("STD", StringComparison.OrdinalIgnoreCase))
        {
            sourceKind = QuantSourceKind.Std;
            port = "STD";
            return true;
        }

        var match = PortFolderRegex().Match(topFolderName);
        if (match.Success)
        {
            sourceKind = QuantSourceKind.Port;
            port = $"PORT {match.Groups["number"].Value}";
            return true;
        }

        sourceKind = default;
        port = string.Empty;
        return false;
    }

    /// <summary>
    /// yyyyMMdd 資料夾判斷
    /// </summary>
    [GeneratedRegex(@"^\d{8}$", RegexOptions.Compiled)]
    private static partial Regex DayFolderRegex();

    /// <summary>
    /// PORT / PROT 資料夾判斷（含 typo 容錯）
    /// </summary>
    [GeneratedRegex(@"^P(?:OR|RO)T\s+(?<number>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PortFolderRegex();

    /// <summary>
    /// 解析資料夾時間：[yyyyMMdd HHmm]
    /// </summary>
    [GeneratedRegex(@"\[(?<value>\d{8}\s\d{4})\]", RegexOptions.Compiled)]
    private static partial Regex DataFolderTimeRegex();
}