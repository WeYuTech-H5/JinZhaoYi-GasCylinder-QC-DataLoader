IF COL_LENGTH('dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY', 'ExcelExportSessionId') IS NULL
BEGIN
    ALTER TABLE dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY
    ADD ExcelExportSessionId UNIQUEIDENTIFIER NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY')
      AND name = 'IX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelExportSessionId'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY_ExcelExportSessionId
        ON dbo.ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY (ExcelExportSessionId)
        WHERE ExcelExportSessionId IS NOT NULL;
    ');
END;
