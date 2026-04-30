using System.Collections.ObjectModel;

namespace JinZhaoYi.GasQcDataLoader.DataModels;

public static class To14cCsvAnalyteMap
{
    private static readonly To14cCsvAnalyte[] OrderedItems =
    [
        new(1, "67641", "Acetone", "Acetone"),
        new(2, "71432", "Benzene", "Benzene"),
        new(3, "120821", "Benzene-1-2-4-trichloro", "1,2,4-TCB"),
        new(4, "95636", "Benzene-1-2-4-trimethyl", "1,2,4-TMB"),
        new(5, "95501", "Benzene-1-2-dichloro", "1,2-Dichlorobenzene"),
        new(6, "108678", "Benzene-1-3-5-trimethyl", "1,3,5-TMB"),
        new(7, "541731", "Benzene-1-3-dichloro", "1,3-Dichlorobenzene"),
        new(8, "106467", "Benzene-1-4-dichloro", "1,4-Dichlorobenzene"),
        new(9, "107062", "Ethane-1-2-dichloro", "1,2-Dichloroethane"),
        new(10, "75343", "Ethane1-1-dichloro", "1,1-Dichloroethane"),
        new(11, "75354", "Ethene11-dichloro", "1,1-Dichloroethene"),
        new(12, "141786", "Ethyl_Acetate", "Ethyl Acetate"),
        new(13, "100414", "Ethyl_Benzene", "Ethylbenzene"),
        new(14, "76131", "Freon113", "Freon113"),
        new(15, "76142", "Freon114", "Freon114"),
        new(16, "67663", "Freon20", "Freon20"),
        new(17, "87683", "Hexachloro-1-3-Butadiene", "HCBD"),
        new(18, "67630", "Isopropanol", "IPA"),
        new(19, "75092", "MethyleneChloride", "Methlene"),
        new(20, "311897", "Perfluorotributylamine", "CNF"),
        new(21, "78875", "Propane-1-2-dichloro", "1,2-Dichloropropane"),
        new(22, null, "RemainLifeTime", null),
        new(23, "100425", "Styrene", "Styrene"),
        new(24, "127184", "Tetrachloroethylene", "Tetrachloroethylene"),
        new(25, "108883", "Toluene", "Toluene"),
        new(26, "10061026", "Trans-1-3-Dichloropropylene", "trans-1,3-Dichloropropene"),
        new(27, "79016", "Trichloroethylene", "Trichloroethylene"),
        new(28, "95476", "o-Xylene", "o-Xylene"),
        new(29, "106423", "p-m_Xylenes(mixed)", "p-Xylene"),
        new(30, "78933", "Butan-2-one", "2-Butanone"),
        new(31, "56235", "CarbonTetrachloride", "Carbon Tetrachloride"),
        new(32, "108907", "Chlorobenzene", "ChloroBenzene"),
        new(33, "156592", "Cis-1-2-Dichloroethylene", "cis-1,2-Dichloroethene"),
        new(34, "10061015", "Cis-1-3-Dichloropropylene", "cis-1,3-Dichloropropene"),
        new(35, "287923", "Cyclopentane", "Cyclopentane"),
        new(36, "71556", "Ethane-1-1-1-trichloro", "1,1,1-Trichloroethane"),
        new(37, "79345", "Ethane-1-1-2-2-tetrachloro", "1,1,2,2-Tetrachloroethane"),
        new(38, "79005", "Ethane-1-1-2-trichloro", "1,1,2-Trichloroethane"),
        new(39, "106934", "Ethane-1-2-dibromo", "1,2-Dibromoethane")
    ];

    public static IReadOnlyList<To14cCsvAnalyte> Items { get; } =
        new ReadOnlyCollection<To14cCsvAnalyte>(OrderedItems);
}

public sealed record To14cCsvAnalyte(int SeqNo, string? ReptId, string ReptName, string? CompoundSuffix);
