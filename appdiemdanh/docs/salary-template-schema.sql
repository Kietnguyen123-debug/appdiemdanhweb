-- SQL Server supplement for the MVC salary-template flow.
-- SalaryComponents is an existing catalog table and is intentionally not changed here.
-- The application model uses SalaryTemplateDetails as the link table (not SalaryTemplateComponents).
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SalaryComponents', N'U') IS NULL
	PRINT 'MISSING TABLE: dbo.SalaryComponents (required by the existing salary catalog). Run the EF migration or the original salary catalog schema first.';
ELSE
BEGIN
	IF COL_LENGTH(N'dbo.SalaryComponents', N'Id') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryComponents.Id';
	IF COL_LENGTH(N'dbo.SalaryComponents', N'Code') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryComponents.Code';
	IF COL_LENGTH(N'dbo.SalaryComponents', N'Name') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryComponents.Name';
	IF COL_LENGTH(N'dbo.SalaryComponents', N'IsActive') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryComponents.IsActive';
END;

IF OBJECT_ID(N'dbo.SalaryTemplates', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.SalaryTemplates
	(
		Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryTemplates PRIMARY KEY,
		Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_SalaryTemplates_Code UNIQUE,
		Name NVARCHAR(200) NOT NULL,
		PositionGroup NVARCHAR(120) NULL,
		IsActive BIT NOT NULL CONSTRAINT DF_SalaryTemplates_IsActive DEFAULT (1),
		CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SalaryTemplates_CreatedAt DEFAULT (SYSUTCDATETIME())
	);
	PRINT 'CREATED TABLE: dbo.SalaryTemplates';
END;
ELSE
BEGIN
	PRINT 'EXISTS TABLE: dbo.SalaryTemplates';
	IF COL_LENGTH(N'dbo.SalaryTemplates', N'Id') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplates.Id';
	IF COL_LENGTH(N'dbo.SalaryTemplates', N'Code') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplates.Code';
	IF COL_LENGTH(N'dbo.SalaryTemplates', N'Name') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplates.Name';
	IF COL_LENGTH(N'dbo.SalaryTemplates', N'PositionGroup') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplates.PositionGroup';
	IF COL_LENGTH(N'dbo.SalaryTemplates', N'IsActive') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplates.IsActive';
	IF COL_LENGTH(N'dbo.SalaryTemplates', N'CreatedAt') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplates.CreatedAt';
END;

IF OBJECT_ID(N'dbo.SalaryTemplateDetails', N'U') IS NULL
BEGIN
	IF OBJECT_ID(N'dbo.SalaryComponents', N'U') IS NULL
		PRINT 'SKIPPED TABLE: dbo.SalaryTemplateDetails because dbo.SalaryComponents is missing.';
	ELSE
	BEGIN
		CREATE TABLE dbo.SalaryTemplateDetails
		(
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryTemplateDetails PRIMARY KEY,
			SalaryTemplateId BIGINT NOT NULL,
			SalaryComponentId BIGINT NOT NULL,
			DefaultValue DECIMAL(19,2) NULL,
			Formula NVARCHAR(2000) NULL,
			CONSTRAINT FK_SalaryTemplateDetails_Template FOREIGN KEY (SalaryTemplateId)
				REFERENCES dbo.SalaryTemplates(Id) ON DELETE CASCADE,
			CONSTRAINT FK_SalaryTemplateDetails_Component FOREIGN KEY (SalaryComponentId)
				REFERENCES dbo.SalaryComponents(Id),
			CONSTRAINT UQ_SalaryTemplateDetails UNIQUE (SalaryTemplateId, SalaryComponentId)
		);
		PRINT 'CREATED TABLE: dbo.SalaryTemplateDetails';
	END;
END;
ELSE
	PRINT 'EXISTS TABLE: dbo.SalaryTemplateDetails';

IF OBJECT_ID(N'dbo.SalaryTemplateDetails', N'U') IS NOT NULL
BEGIN
	IF COL_LENGTH(N'dbo.SalaryTemplateDetails', N'Id') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplateDetails.Id';
	IF COL_LENGTH(N'dbo.SalaryTemplateDetails', N'SalaryTemplateId') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplateDetails.SalaryTemplateId';
	IF COL_LENGTH(N'dbo.SalaryTemplateDetails', N'SalaryComponentId') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplateDetails.SalaryComponentId';
	IF COL_LENGTH(N'dbo.SalaryTemplateDetails', N'DefaultValue') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplateDetails.DefaultValue';
	IF COL_LENGTH(N'dbo.SalaryTemplateDetails', N'Formula') IS NULL PRINT 'MISSING COLUMN: dbo.SalaryTemplateDetails.Formula';
END;
