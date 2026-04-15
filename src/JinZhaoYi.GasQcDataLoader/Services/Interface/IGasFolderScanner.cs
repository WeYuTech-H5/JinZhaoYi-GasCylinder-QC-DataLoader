using JinZhaoYi.GasQcDataLoader.DataModels;

namespace JinZhaoYi.GasQcDataLoader.Services.Interface;

public interface IGasFolderScanner
{
    IReadOnlyList<string> FindStableDayFolders(string watchRoot, TimeSpan stableAge);

    IReadOnlyList<QuantFileCandidate> FindStableQuantFiles(string watchRoot, TimeSpan stableAge);

    IReadOnlyList<QuantFileCandidate> FindQuantFiles(string dayFolderPath);
}
