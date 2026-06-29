using System.Globalization;
using System.Text.Json;
using JinZhaoYi.GasQcDataLoader.DataModels;
using JinZhaoYi.GasQcDataLoader.Services.Interface;

namespace JinZhaoYi.GasQcDataLoader.Services.Service;

public sealed class MfgJsonParser : IMfgJsonParser
{
    private static readonly HashSet<string> AllowedNullFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cal_id",
        "CalType",
        "FnlPrs",
        "IniPrs",
        "QCComplete",
        "QCInst",
        "QCPort",
        "QCTime",
        "Result",
        "RF_ID"
    };

    public IReadOnlyList<MfgJsonLotRecord> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("MFG JSON root must be an array.");
        }

        var rows = new List<MfgJsonLotRecord>();
        var index = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"MFG JSON item #{index} must be an object.");
            }

            var si0Id = ReadRequiredString(item, "si0_id", index);
            var lotNo = ReadRequiredString(item, "LotNo", index);
            rows.Add(new MfgJsonLotRecord
            {
                NullFields = GetExplicitNullFields(item),
                Id = ReadRequiredDecimal(item, "si0_id", index),
                Si0Id = si0Id,
                LotNo = lotNo,
                SampleName = ReadString(item, "si0_SampleName"),
                ProdDate = ReadDate(item, "si0_ProdDate", index),
                ProdType = ReadString(item, "ProdType"),
                ProdOperator = ReadString(item, "Prod_Operator"),
                ProdIniPrs = ReadInt(item, "Prod_IniPrs", index),
                ProdLeakTest1 = ReadInt(item, "Prod_LeakTest1", index),
                ProdVacuumPrs = ReadInt(item, "Prod_VacuumPrs", index),
                ProdCan1FillingPrs = ReadInt(item, "Prod_Can1_FillingPrs", index),
                ProdCan2FillingPrs = ReadInt(item, "Prod_Can2_FillingPrs", index),
                ProdBomb2FillingPrs = ReadInt(item, "Prod_Bomb2_FillingPrs", index),
                ProdBomb1FillingPrs = ReadInt(item, "Prod_Bomb1_FillingPrs", index),
                ProdBomb3FillingPrs = ReadInt(item, "Prod_Bomb3_FillingPrs", index),
                ProdLeakTest2 = ReadInt(item, "Prod_LeakTest2", index),
                ProdCan1LotNo = ReadString(item, "Prod_Can1_LotNo"),
                ProdCan1Flow = ReadInt(item, "Prod_Can1_Flow", index),
                ProdCan1Sec = ReadInt(item, "Prod_Can1_Sec", index),
                ProdCan1Prs = ReadDecimal(item, "Prod_Can1_Prs", index),
                ProdCan2LotNo = ReadString(item, "Prod_Can2_LotNo"),
                ProdCan2Flow = ReadInt(item, "Prod_Can2_Flow", index),
                ProdCan2Sec = ReadInt(item, "Prod_Can2_Sec", index),
                ProdCan2Prs = ReadInt(item, "Prod_Can2_Prs", index),
                ProdBomb2LotNo = ReadString(item, "Prod_Bomb2_LotNo"),
                ProdBomb2Flow = ReadInt(item, "Prod_Bomb2_Flow", index),
                ProdBomb2Sec = ReadInt(item, "Prod_Bomb2_Sec", index),
                ProdBomb2Prs = ReadInt(item, "Prod_Bomb2_Prs", index),
                ProdBomb1LotNo = ReadString(item, "Prod_Bomb1_LotNo"),
                ProdBomb1SetFillingPrs = ReadInt(item, "Prod_Bomb1_SetFillingPrs", index),
                ProdBomb1Prs = ReadInt(item, "Prod_Bomb1_Prs", index),
                ProdBomb3LotNo = ReadString(item, "Prod_Bomb3_LotNo"),
                ProdBomb3SetFillingPrs = ReadInt(item, "Prod_Bomb3_SetFillingPrs", index),
                ProdBomb3Prs = ReadInt(item, "Prod_Bomb3_Prs", index),
                SampleNo = ReadString(item, "SampleNo"),
                SampleType = ReadString(item, "SampleType"),
                Container = ReadString(item, "Container"),
                ProdOrder = ReadString(item, "ProdOrder"),
                CalType = ReadString(item, "CalType"),
                CalId = ReadString(item, "Cal_id"),
                IniPrs = ReadString(item, "IniPrs"),
                QcComplete = ReadString(item, "QCComplete"),
                QcInst = ReadString(item, "QCInst"),
                QcPort = ReadString(item, "QCPort"),
                QcTime = ReadDateTime(item, "QCTime", index),
                Result = ReadString(item, "Result"),
                RfId = ReadString(item, "RF_ID"),
                FnlPrs = ReadString(item, "FnlPrs")
            });
        }

        EnsureNoDuplicate(rows, row => row.Si0Id, "si0_id");
        EnsureNoDuplicate(rows, row => row.LotNo, "LotNo");
        return rows;
    }

    private static IReadOnlyList<string> GetExplicitNullFields(JsonElement item) =>
        item.EnumerateObject()
            .Where(property =>
                property.Value.ValueKind == JsonValueKind.Null &&
                !AllowedNullFields.Contains(property.Name))
            .Select(property => property.Name)
            .ToArray();

    private static void EnsureNoDuplicate(
        IReadOnlyList<MfgJsonLotRecord> rows,
        Func<MfgJsonLotRecord, string> keySelector,
        string fieldName)
    {
        var duplicate = rows
            .Select(keySelector)
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"MFG JSON contains duplicate {fieldName}: {duplicate.Key}.");
        }
    }

    private static string ReadRequiredString(JsonElement item, string propertyName, int index) =>
        ReadString(item, propertyName)
        ?? throw new InvalidOperationException($"MFG JSON item #{index} missing required field '{propertyName}'.");

    private static string? ReadString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int? ReadInt(JsonElement item, string propertyName, int index)
    {
        var text = ReadString(item, propertyName);
        if (text is null)
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"MFG JSON item #{index} field '{propertyName}' is not a valid integer: {text}.");
    }

    private static decimal ReadRequiredDecimal(JsonElement item, string propertyName, int index) =>
        ReadDecimal(item, propertyName, index)
        ?? throw new InvalidOperationException($"MFG JSON item #{index} missing required decimal field '{propertyName}'.");

    private static decimal? ReadDecimal(JsonElement item, string propertyName, int index)
    {
        var text = ReadString(item, propertyName);
        if (text is null)
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"MFG JSON item #{index} field '{propertyName}' is not a valid decimal: {text}.");
    }

    private static DateTime? ReadDate(JsonElement item, string propertyName, int index)
    {
        var value = ReadDateTime(item, propertyName, index);
        return value?.Date;
    }

    private static DateTime? ReadDateTime(JsonElement item, string propertyName, int index)
    {
        var text = ReadString(item, propertyName);
        if (text is null)
        {
            return null;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"MFG JSON item #{index} field '{propertyName}' is not a valid date/time: {text}.");
    }
}
