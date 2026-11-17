namespace TransactionProcessor;

/// <summary>Table definitions for the reference data the handler looks up. Shared by the tests and the seeder.</summary>
public static class ReferenceDataSchema
{
    /// <summary>Item: numeric key (the lookup key, clustered primary key), required ExternalItemId and filler columns.</summary>
    public const string CreateItemTableSql = """
        CREATE TABLE dbo.Item (
            ItemId         BIGINT        NOT NULL,
            ExternalItemId NVARCHAR(64)  NOT NULL,
            Name           NVARCHAR(200) NULL,
            Category       NVARCHAR(100) NULL,
            CreatedUtc     DATETIME2     NOT NULL CONSTRAINT DF_Item_CreatedUtc DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_Item PRIMARY KEY CLUSTERED (ItemId)
        );
        """;

    /// <summary>Sku: text key (clustered primary key) and filler columns.</summary>
    public const string CreateSkuTableSql = """
        CREATE TABLE dbo.Sku (
            SkuId       NVARCHAR(64)  NOT NULL,
            Description NVARCHAR(200) NULL,
            Brand       NVARCHAR(100) NULL,
            CreatedUtc  DATETIME2     NOT NULL CONSTRAINT DF_Sku_CreatedUtc DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_Sku PRIMARY KEY CLUSTERED (SkuId)
        );
        """;

    public const string CreateTablesSql = CreateItemTableSql + "\n\n" + CreateSkuTableSql;
}
