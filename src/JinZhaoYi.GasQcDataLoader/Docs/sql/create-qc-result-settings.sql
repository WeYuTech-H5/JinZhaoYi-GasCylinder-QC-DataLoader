IF OBJECT_ID(N'dbo.ZZ_NF_GAS_QC_PRESSURE_RULE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ZZ_NF_GAS_QC_PRESSURE_RULE
    (
        ContainerType NVARCHAR(16) NOT NULL CONSTRAINT PK_ZZ_NF_GAS_QC_PRESSURE_RULE PRIMARY KEY,
        IniPrsMin DECIMAL(18, 4) NULL,
        FnlPrsMin DECIMAL(18, 4) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ZZ_NF_GAS_QC_PRESSURE_RULE_IsActive DEFAULT (1),
        CreateUser NVARCHAR(100) NOT NULL,
        CreateTime DATETIME2(3) NOT NULL,
        EditUser NVARCHAR(100) NULL,
        EditTime DATETIME2(3) NULL,
        CONSTRAINT CK_ZZ_NF_GAS_QC_PRESSURE_RULE_ContainerType
            CHECK (ContainerType IN (N'0.5L', N'1L')),
        CONSTRAINT CK_ZZ_NF_GAS_QC_PRESSURE_RULE_IniPrsMin
            CHECK (IniPrsMin IS NULL OR IniPrsMin >= 0),
        CONSTRAINT CK_ZZ_NF_GAS_QC_PRESSURE_RULE_FnlPrsMin
            CHECK (FnlPrsMin IS NULL OR FnlPrsMin >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.ZZ_NF_GAS_QC_PRESSURE_RULE WHERE ContainerType = N'0.5L')
BEGIN
    INSERT INTO dbo.ZZ_NF_GAS_QC_PRESSURE_RULE
    (
        ContainerType,
        IniPrsMin,
        FnlPrsMin,
        IsActive,
        CreateUser,
        CreateTime
    )
    VALUES
    (
        N'0.5L',
        NULL,
        NULL,
        1,
        N'SYSTEM',
        SYSDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.ZZ_NF_GAS_QC_PRESSURE_RULE WHERE ContainerType = N'1L')
BEGIN
    INSERT INTO dbo.ZZ_NF_GAS_QC_PRESSURE_RULE
    (
        ContainerType,
        IniPrsMin,
        FnlPrsMin,
        IsActive,
        CreateUser,
        CreateTime
    )
    VALUES
    (
        N'1L',
        NULL,
        NULL,
        1,
        N'SYSTEM',
        SYSDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.ZZ_NF_GAS_QC_CONCENTRATION_RULE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ZZ_NF_GAS_QC_CONCENTRATION_RULE
    (
        ContainerType NVARCHAR(16) NOT NULL,
        AnalyteKey NVARCHAR(100) NOT NULL,
        MinPpb DECIMAL(18, 6) NULL,
        MaxPpb DECIMAL(18, 6) NULL,
        SortOrder INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ZZ_NF_GAS_QC_CONCENTRATION_RULE_IsActive DEFAULT (1),
        CreateUser NVARCHAR(100) NOT NULL,
        CreateTime DATETIME2(3) NOT NULL,
        EditUser NVARCHAR(100) NULL,
        EditTime DATETIME2(3) NULL,
        CONSTRAINT PK_ZZ_NF_GAS_QC_CONCENTRATION_RULE
            PRIMARY KEY (ContainerType, AnalyteKey),
        CONSTRAINT CK_ZZ_NF_GAS_QC_CONCENTRATION_RULE_ContainerType
            CHECK (ContainerType IN (N'0.5L', N'1L')),
        CONSTRAINT CK_ZZ_NF_GAS_QC_CONCENTRATION_RULE_MinPpb
            CHECK (MinPpb IS NULL OR MinPpb >= 0),
        CONSTRAINT CK_ZZ_NF_GAS_QC_CONCENTRATION_RULE_MaxPpb
            CHECK (MaxPpb IS NULL OR MaxPpb >= 0),
        CONSTRAINT CK_ZZ_NF_GAS_QC_CONCENTRATION_RULE_Range
            CHECK (MinPpb IS NULL OR MaxPpb IS NULL OR MinPpb <= MaxPpb)
    );

    CREATE INDEX IX_ZZ_NF_GAS_QC_CONCENTRATION_RULE_AnalyteKey
        ON dbo.ZZ_NF_GAS_QC_CONCENTRATION_RULE (AnalyteKey, SortOrder);
END;
