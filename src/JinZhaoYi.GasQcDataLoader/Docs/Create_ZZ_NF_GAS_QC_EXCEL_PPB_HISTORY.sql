/*
    使用者成功產生 Query2 Excel 後，系統會把當次 Excel 裡的 PPB rows
    快照到這張表，供前端後續勾選產生 TO14C CSV。
    這張表不是匯入流程即時計算使用的 ZZ_NF_GAS_QC_LOT_PORT_PPB。
*/
IF OBJECT_ID(N'dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY', N'U') IS NULL
BEGIN
    SELECT TOP (0) *
    INTO dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY
    FROM dbo.ZZ_NF_GAS_QC_LOT_PORT_PPB;
END;

IF COL_LENGTH(N'dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY', N'ExcelPpbExportId') IS NULL
BEGIN
    ALTER TABLE dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY ADD
        ExcelPpbExportId nvarchar(64) NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelPpbExportId DEFAULT CONVERT(nvarchar(64), NEWID()),
        ExcelExportKey nvarchar(64) NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelExportKey DEFAULT CONVERT(nvarchar(64), NEWID()),
        ExcelExportedAt datetime2 NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelExportedAt DEFAULT SYSUTCDATETIME(),
        ExcelExportUser nvarchar(128) NULL,
        ExcelStartDate date NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelStartDate DEFAULT CONVERT(date, GETDATE()),
        ExcelEndDate date NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelEndDate DEFAULT CONVERT(date, GETDATE()),
        ExcelRfId nvarchar(255) NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelRfId DEFAULT N'',
        ExcelStdRawIds nvarchar(max) NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelStdRawIds DEFAULT N'',
        ExcelPortRawIds nvarchar(max) NOT NULL
            CONSTRAINT DF_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelPortRawIds DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY')
      AND name = N'UX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelPpbExportId'
)
BEGIN
    CREATE UNIQUE INDEX UX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelPpbExportId
        ON dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY (ExcelPpbExportId);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY')
      AND name = N'IX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelExportKey'
)
BEGIN
    CREATE INDEX IX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelExportKey
        ON dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY (ExcelExportKey);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY')
      AND name = N'IX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExportLookup'
)
BEGIN
    CREATE INDEX IX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExportLookup
        ON dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY (AnlzTime, Port, LotNo, SampleName);
END;
