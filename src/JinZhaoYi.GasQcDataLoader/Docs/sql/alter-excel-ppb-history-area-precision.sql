DECLARE @table sysname = N'ZZ_NF_GAS_QC_EXCEL_PPB_HISTORY';
DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql + N'ALTER TABLE dbo.' + QUOTENAME(@table) +
    N' ALTER COLUMN ' + QUOTENAME(COLUMN_NAME) + N' decimal(28, 12) NULL;' + CHAR(13) + CHAR(10)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = N'dbo'
  AND TABLE_NAME = @table
  AND COLUMN_NAME LIKE N'Area[_]%'
  AND DATA_TYPE IN (N'decimal', N'numeric')
  AND (
      NUMERIC_PRECISION < 28
      OR NUMERIC_SCALE <> 12
  )
ORDER BY ORDINAL_POSITION;

IF LEN(@sql) > 0
BEGIN
    EXEC sp_executesql @sql;
END;
