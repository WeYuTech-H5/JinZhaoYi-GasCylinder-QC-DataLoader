using System.Collections.ObjectModel;

namespace JinZhaoYi.GasQcDataLoader.DataModels;

public static class CompoundMap
{
    // Quant.txt 的 compound 名稱與 DB 欄位名稱不完全一致，統一在這裡集中管理。
    // Suffix 會組成 Area_* / ppb_* / RT_* 欄位；少數特殊欄位可明確覆寫。
    private static readonly AnalyteDefinition[] OrderedAnalytes =
    [
        Define("Acetone", "Acetone"),
        Define("IPA", "IPA"),
        Define("Methlene", "Methylene Chloride"),
        Define("CNF", "CNF"),
        Define("Cyclopentane", "Cyclopentane"),
        Define("2-Butanone", "2-Butanone"),
        Define("Ethyl Acetate", "Ethyl Acetate"),
        Define("Benzene", "Benzene"),
        Define("Carbon Tetrachloride", "Carbon Tetrachloride"),
        Define("Toluene", "Toluene"),
        Define("1,2,4-TMB", "1,2,4-TMB"),
        Define("Chlorobenzene-D5", "Chlorobenzene-D5"),
        Define("Freon114", "Freon114"),
        Define("1,1-Dichloroethene", "1,1-Dichloroethene"),
        Define("Freon113", "Freon113"),
        Define("1,1-Dichloroethane", "1,1-Dichloroethane"),
        Define("cis-1,2-Dichloroethene", "cis-1,2-Dichloroethene"),
        Define("Freon20", "Freon20"),
        Define("1,1,1-Trichloroethane", "1,1,1-Trichloroethane"),
        Define("1,2-Dichloroethane", "1,2-Dichloroethane", "AREA_1,2-Dichloroethane"),
        Define("Trichloroethylene", "Trichloroethylene"),
        Define("1,2-Dichloropropane", "1,2-Dichloropropane"),
        Define("cis-1,3-Dichloropropene", "cis-1,3-Dichloropropene"),
        Define("trans-1,3-Dichloropropene", "trans-1,3-Dichloropropene"),
        Define("1,1,2-Trichloroethane", "1,1,2-Trichloroethane"),
        Define("Tetrachloroethylene", "Tetrachloroethylene"),
        Define("1,2-Dibromoethane", "1,2-Dibromoethane"),
        Define("ChloroBenzene", "ChloroBenzene"),
        Define("Ethylbenzene", "EthylBenzene"),
        Define("p-Xylene", "m/p-Xylene"),
        Define("Styrene", "Styrene"),
        Define("o-Xylene", "o-Xylene"),
        Define("1,1,2,2-Tetrachloroethane", "1,1,2,2-Tetrachloroethane"),
        Define("1,3,5-TMB", "1,3,5-TMB"),
        Define("1,3-Dichlorobenzene", "1,3-DCB"),
        Define("1,4-Dichlorobenzene", "1,4-DCB"),
        Define("1,2-Dichlorobenzene", "1,2-DCB"),
        Define("1,2,4-TCB", "1,2,4-TCB"),
        Define("HCBD", "HCBD")
    ];

    private static readonly Dictionary<string, AnalyteDefinition> ByQuantName =
        OrderedAnalytes.ToDictionary(a => NormalizeQuantName(a.QuantName), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AnalyteDefinition> Analytes { get; } =
        new ReadOnlyCollection<AnalyteDefinition>(OrderedAnalytes);

    // 解析 Quant compound 時先正規化名稱，再回查對應 DB 欄位定義。
    public static bool TryGetByQuantName(string quantName, out AnalyteDefinition analyte) =>
        ByQuantName.TryGetValue(NormalizeQuantName(quantName), out analyte!);

    // Quant 檔可能有多餘空白或句點，例如 ChloroBenzene.，先正規化再比對。
    public static string NormalizeQuantName(string value)
    {
        var trimmed = value.Trim().TrimEnd('.');
        return string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static AnalyteDefinition Define(string suffix, string quantName, string? areaColumn = null) =>
        new(suffix, quantName, areaColumn ?? $"Area_{suffix}", $"ppb_{suffix}", $"RT_{suffix}");
}
