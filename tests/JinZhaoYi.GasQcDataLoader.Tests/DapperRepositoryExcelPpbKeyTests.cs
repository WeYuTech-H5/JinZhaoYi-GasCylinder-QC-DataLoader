using FluentAssertions;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Infrastructure;

namespace JinZhaoYi.GasQcDataLoader.Tests;

public sealed class DapperRepositoryExcelPpbKeyTests
{
    [Fact]
    public void ComputeExcelExportKey_ignores_export_date_range_for_same_calculation_inputs()
    {
        var first = Request(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 25),
            "RF-01",
            ["STD-02", "STD-01"],
            ["PORT-02", "PORT-01"]);
        var second = Request(
            new DateTime(2026, 4, 1),
            new DateTime(2026, 5, 25),
            " rf-01 ",
            ["std-01", "STD-02", "STD-01"],
            ["port-01", "PORT-02"]);

        DapperRepository.ComputeExcelExportKey(first)
            .Should()
            .Be(DapperRepository.ComputeExcelExportKey(second));
    }

    [Fact]
    public void ComputeExcelExportKey_changes_when_calculation_input_changes()
    {
        var original = Request(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 25),
            "RF-01",
            ["STD-01"],
            ["PORT-01"]);
        var changedPortInput = Request(
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 25),
            "RF-01",
            ["STD-01"],
            ["PORT-02"]);

        DapperRepository.ComputeExcelExportKey(original)
            .Should()
            .NotBe(DapperRepository.ComputeExcelExportKey(changedPortInput));
    }

    private static ExcelPpbHistorySaveRequest Request(
        DateTime startDate,
        DateTime endDate,
        string rfId,
        IReadOnlyList<string> stdRawIds,
        IReadOnlyList<string> portRawIds) =>
        new(startDate, endDate, rfId, stdRawIds, portRawIds, [], DateTime.Now, "test");
}
