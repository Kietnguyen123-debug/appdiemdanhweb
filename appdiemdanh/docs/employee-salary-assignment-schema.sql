-- Safe SQL Server supplement for employee salary-template assignment.
-- Does not alter SalaryComponents, SalaryTemplates, or existing employee salary data.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
	PRINT 'MISSING TABLE: dbo.Employees';
IF OBJECT_ID(N'dbo.SalaryTemplates', N'U') IS NULL
	PRINT 'MISSING TABLE: dbo.SalaryTemplates. Run salary-template-schema.sql first.';
IF OBJECT_ID(N'dbo.SalaryComponents', N'U') IS NULL
	PRINT 'MISSING TABLE: dbo.SalaryComponents. Run the salary catalog schema first.';

IF OBJECT_ID(N'dbo.EmployeeSalaries', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.EmployeeSalaries
	(
		Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSalaries PRIMARY KEY,
		EmployeeId INT NOT NULL,
		SalaryTemplateId BIGINT NULL,
		EffectiveDate DATE NOT NULL,
		EndDate DATE NULL,
		Status NVARCHAR(30) NOT NULL CONSTRAINT DF_EmployeeSalaries_Status DEFAULT (N'Active'),
		Reason NVARCHAR(1000) NULL,
		CreatedBy INT NOT NULL,
		ApprovedBy INT NULL,
		CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmployeeSalaries_CreatedAt DEFAULT (SYSUTCDATETIME()),
		CONSTRAINT FK_EmployeeSalaries_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id),
		CONSTRAINT FK_EmployeeSalaries_Template FOREIGN KEY (SalaryTemplateId) REFERENCES dbo.SalaryTemplates(Id) ON DELETE SET NULL,
		CONSTRAINT FK_EmployeeSalaries_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Employees(Id),
		CONSTRAINT FK_EmployeeSalaries_ApprovedBy FOREIGN KEY (ApprovedBy) REFERENCES dbo.Employees(Id),
		CONSTRAINT CK_EmployeeSalaries_DateRange CHECK (EndDate IS NULL OR EndDate >= EffectiveDate)
	);
	CREATE INDEX IX_EmployeeSalaries_Employee_EffectiveDate ON dbo.EmployeeSalaries(EmployeeId, EffectiveDate);
	PRINT 'CREATED TABLE: dbo.EmployeeSalaries';
END;
ELSE
	PRINT 'EXISTS TABLE: dbo.EmployeeSalaries';

IF OBJECT_ID(N'dbo.EmployeeSalaryDetails', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.EmployeeSalaryDetails
	(
		Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSalaryDetails PRIMARY KEY,
		EmployeeSalaryId BIGINT NOT NULL,
		SalaryComponentId BIGINT NOT NULL,
		Amount DECIMAL(19,2) NULL,
		Formula NVARCHAR(2000) NULL,
		CONSTRAINT FK_EmployeeSalaryDetails_Salary FOREIGN KEY (EmployeeSalaryId) REFERENCES dbo.EmployeeSalaries(Id) ON DELETE CASCADE,
		CONSTRAINT FK_EmployeeSalaryDetails_Component FOREIGN KEY (SalaryComponentId) REFERENCES dbo.SalaryComponents(Id),
		CONSTRAINT UQ_EmployeeSalaryDetails UNIQUE (EmployeeSalaryId, SalaryComponentId)
	);
	PRINT 'CREATED TABLE: dbo.EmployeeSalaryDetails';
END;
ELSE
	PRINT 'EXISTS TABLE: dbo.EmployeeSalaryDetails';
