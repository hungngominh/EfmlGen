-- ----------------------------------------------------------------------------
-- EfmlGen sample schema for SQL Server (mirror of samples/categories-postgres)
--
-- Run on a fresh SQL Server database (Server 2019+ recommended):
--   sqlcmd -S localhost -d SampleDb -i setup.sql
-- Then point EfmlGen at it:
--   $env:MSSQL_CONN = "Server=localhost;Database=SampleDb;User Id=sa;Password=...;TrustServerCertificate=true"
--   EfmlGen.Cli.exe db-smoke --provider SqlServer --conn-env MSSQL_CONN --schemas dbo
-- ----------------------------------------------------------------------------

IF OBJECT_ID('dbo.Product_Company_Mapping', 'U') IS NOT NULL DROP TABLE dbo.Product_Company_Mapping;
IF OBJECT_ID('dbo.Department', 'U') IS NOT NULL DROP TABLE dbo.Department;
IF OBJECT_ID('dbo.Mioto_VehicleOwner', 'U') IS NOT NULL DROP TABLE dbo.Mioto_VehicleOwner;
IF OBJECT_ID('dbo.ConfigRentalServiceRatingPoint', 'U') IS NOT NULL DROP TABLE dbo.ConfigRentalServiceRatingPoint;
IF OBJECT_ID('dbo.ConfigReportUserReason', 'U') IS NOT NULL DROP TABLE dbo.ConfigReportUserReason;
IF OBJECT_ID('dbo.ConfigReportOrderReason', 'U') IS NOT NULL DROP TABLE dbo.ConfigReportOrderReason;
IF OBJECT_ID('dbo.ConfigState', 'U') IS NOT NULL DROP TABLE dbo.ConfigState;
GO

CREATE TABLE dbo.ConfigState (
    Id              bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code            nvarchar(max) NOT NULL,
    [Name]          nvarchar(max) NULL,
    IsDeleted       bit NOT NULL,
    IsDisable       bit NOT NULL,
    Log_CreatedDate datetimeoffset NULL,
    Log_CreatedBy   nvarchar(max) NULL,
    Log_UpdatedDate datetimeoffset NULL,
    Log_UpdatedBy   nvarchar(max) NULL,
    [TimeStamp]     datetimeoffset NULL,
    TimeStampText   nvarchar(max) NULL,
    Note            nvarchar(max) NULL,
    OrderNo         int NOT NULL DEFAULT ((0)),
    ID_GUID         uniqueidentifier NOT NULL DEFAULT (newid())
);
GO

CREATE TABLE dbo.ConfigReportOrderReason (
    Id              bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IsDeleted       bit NOT NULL DEFAULT ((0)),
    Log_CreatedDate datetimeoffset NULL,
    Log_CreatedBy   nvarchar(500) NULL,
    Log_UpdatedDate datetimeoffset NULL,
    Log_UpdatedBy   nvarchar(500) NULL,
    Note            nvarchar(max) NULL,
    OrderNo         decimal(20,6) NOT NULL DEFAULT ((0)),
    IsDisable       bit NOT NULL DEFAULT ((0)),
    Code            nvarchar(max) NULL,
    [Name]          nvarchar(max) NULL,
    [Description]   nvarchar(max) NULL,
    ColorCode       nvarchar(50) NULL,
    IconUrl         nvarchar(max) NULL,
    IsShowExtReason bit NOT NULL DEFAULT ((0)),
    ID_GUID         uniqueidentifier NOT NULL DEFAULT (newid())
);
GO

CREATE TABLE dbo.ConfigReportUserReason (
    Id              bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IsDeleted       bit NOT NULL DEFAULT ((0)),
    Log_CreatedDate datetimeoffset NULL,
    Log_CreatedBy   nvarchar(500) NULL,
    Log_UpdatedDate datetimeoffset NULL,
    Log_UpdatedBy   nvarchar(500) NULL,
    Note            nvarchar(max) NULL,
    OrderNo         decimal(20,6) NOT NULL DEFAULT ((0)),
    IsDisable       bit NOT NULL DEFAULT ((0)),
    Code            nvarchar(max) NULL,
    [Name]          nvarchar(max) NULL,
    [Description]   nvarchar(max) NULL,
    ColorCode       nvarchar(50) NULL,
    IconUrl         nvarchar(max) NULL,
    ShowExtReason   bit NULL,
    ID_GUID         uniqueidentifier NOT NULL DEFAULT (newid())
);
GO

CREATE TABLE dbo.ConfigRentalServiceRatingPoint (
    Id              bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IsDeleted       bit NOT NULL DEFAULT ((0)),
    Log_CreatedDate datetimeoffset NULL,
    Log_CreatedBy   nvarchar(500) NULL,
    Log_UpdatedDate datetimeoffset NULL,
    Log_UpdatedBy   nvarchar(500) NULL,
    Note            nvarchar(max) NULL,
    OrderNo         decimal(20,6) NOT NULL DEFAULT ((0)),
    IsDisable       bit NOT NULL DEFAULT ((0)),
    Code            nvarchar(max) NULL,
    [Name]          nvarchar(max) NULL,
    [Description]   nvarchar(max) NULL,
    ColorCode       nvarchar(50) NULL,
    IconUrl         nvarchar(max) NULL,
    Rating          decimal(18,3) NULL,
    ID_GUID         uniqueidentifier NOT NULL DEFAULT (newid())
);
GO

CREATE TABLE dbo.Mioto_VehicleOwner (
    Id              bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IsDeleted       bit NOT NULL DEFAULT ((0)),
    Log_CreatedDate datetimeoffset NULL,
    Log_CreatedBy   nvarchar(500) NULL,
    Log_UpdatedDate datetimeoffset NULL,
    Log_UpdatedBy   nvarchar(500) NULL,
    Note            nvarchar(max) NULL,
    OrderNo         decimal(20,6) NOT NULL DEFAULT ((0)),
    IsDisable       bit NOT NULL DEFAULT ((0)),
    Code            nvarchar(max) NULL,
    [Name]          nvarchar(max) NULL,
    [Description]   nvarchar(max) NULL,
    ColorCode       nvarchar(50) NULL,
    IconUrl         nvarchar(max) NULL,
    NameUnsign      nvarchar(max) NULL,
    ID_GUID         uniqueidentifier NOT NULL DEFAULT (newid())
);
GO

CREATE TABLE dbo.Department (
    Id                  bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code                nvarchar(250) NOT NULL,
    [Name]              nvarchar(500) NULL,
    OrderNo             int NOT NULL DEFAULT ((0)),
    ParentId            bigint NULL,
    IsDeleted           bit NOT NULL DEFAULT ((0)),
    Log_CreatedDate     datetimeoffset NULL,
    Log_CreatedBy       nvarchar(500) NULL,
    Log_UpdatedDate     datetimeoffset NULL,
    Log_UpdatedBy       nvarchar(500) NULL,
    IsDisable           bit NOT NULL DEFAULT ((0)),
    ColorCode           nvarchar(50) NULL,
    DisplayName         nvarchar(250) NULL,
    Note                nvarchar(max) NULL,
    AllowCreatePassword bit NOT NULL DEFAULT ((0)),
    CompanyId           bigint NULL,
    IsReadOnly          bit NOT NULL DEFAULT ((0)),
    [TimeStamp]         rowversion NULL,
    TimeStampText       nvarchar(max) NULL,
    JoinToOrgChart      bit NOT NULL DEFAULT ((0)),
    ID_GUID             uniqueidentifier NOT NULL DEFAULT (newid()),
    CONSTRAINT FK_Department_Department FOREIGN KEY (ParentId) REFERENCES dbo.Department(Id)
);
GO

CREATE TABLE dbo.Product_Company_Mapping (
    Id                  bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    IsDeleted           bit NOT NULL,
    Log_CreatedDate     datetimeoffset NULL,
    Log_CreatedBy       nvarchar(500) NULL,
    Log_UpdatedDate     datetimeoffset NULL,
    Log_UpdatedBy       nvarchar(500) NULL,
    Note                nvarchar(max) NULL,
    OrderNo             decimal(20,6) NOT NULL,
    IsDisable           bit NOT NULL,
    Code                nvarchar(max) NULL,
    [Name]              nvarchar(max) NULL,
    [Description]       nvarchar(max) NULL,
    ColorCode           nvarchar(50) NULL,
    IconUrl             nvarchar(max) NULL,
    ID_GUID             uniqueidentifier NOT NULL,
    MerchantProductGUID uniqueidentifier NULL,
    CompanyGUID         uniqueidentifier NULL,
    ValidFrom           datetimeoffset NULL,
    ExpiredAt           datetimeoffset NULL,
    Images              nvarchar(max) NULL,
    MoreConfig          nvarchar(max) NULL
);
GO
