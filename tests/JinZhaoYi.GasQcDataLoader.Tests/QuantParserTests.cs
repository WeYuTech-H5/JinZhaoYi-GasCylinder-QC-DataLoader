using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Service;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class QuantParserTests
{
    [Fact]
    public async Task ParseAsync_maps_quant_headers_area_and_rt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dataFolder = Path.Combine(root, "STD", "STD[20251119 0947]_903.D");
        Directory.CreateDirectory(dataFolder);
        var quantPath = Path.Combine(dataFolder, "Quant.txt");
        await File.WriteAllTextAsync(quantPath, QuantFixtures.Std0947);

        try
        {
            var candidate = new QuantFileCandidate(
                FullPath: quantPath,
                DayFolderPath: root,
                TopFolderName: "STD",
                SourceKind: QuantSourceKind.Std,
                Port: "STD",
                DataFilename: Path.Combine("STD[20251119 0947]_903.D", "Quant.txt"),
                DataFilepath: dataFolder);

            var parsed = await new QuantParser().ParseAsync(candidate, CancellationToken.None);

            parsed.LotNo.Should().Be("20251030001");
            parsed.SampleNo.Should().Be(903);
            parsed.AcquiredAt.Should().Be(new DateTime(2025, 11, 19, 9, 47, 0));
            parsed.Misc.Should().Be(" port 1  903  872>  #20251030001");
            parsed.Compounds["IPA"].Response.Should().Be(4534252m);
            parsed.Compounds["IPA"].RetentionTime.Should().Be(2.164m);
            parsed.Compounds["IPA"].ConcentrationPpb.Should().Be(143.41m);
            parsed.Compounds["Methlene"].Response.Should().Be(2513092m);
            parsed.Compounds["Chlorobenzene-D5"].ConcentrationPpb.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
