-- Unified idempotent payroll/salary deployment script for SQL Server.
-- Run against the application's database. Requires dbo.Employees, dbo.Departments,
-- dbo.Shifts, and dbo.EmployeeShifts to exist first.
-- Safe to rerun: creates missing objects and inserts only missing seed codes.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
	THROW 51000, 'Required base table dbo.Employees is missing.', 1;
IF OBJECT_ID(N'dbo.Departments', N'U') IS NULL
	THROW 51000, 'Required base table dbo.Departments is missing.', 1;
IF OBJECT_ID(N'dbo.Shifts', N'U') IS NULL
	THROW 51000, 'Required base table dbo.Shifts is missing.', 1;
IF OBJECT_ID(N'dbo.EmployeeShifts', N'U') IS NULL
	THROW 51000, 'Required base table dbo.EmployeeShifts is missing.', 1;

BEGIN TRY
	BEGIN TRANSACTION;

	IF OBJECT_ID(N'dbo.SalaryComponents', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.SalaryComponents (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryComponents PRIMARY KEY,
			Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_SalaryComponents_Code UNIQUE,
			Name NVARCHAR(200) NOT NULL,
			Category NVARCHAR(30) NOT NULL,
			CalculationType NVARCHAR(30) NOT NULL,
			Formula NVARCHAR(2000) NULL,
			TaxTreatment NVARCHAR(30) NOT NULL,
			TaxExemptLimit DECIMAL(19,2) NULL,
			InsuranceTreatment NVARCHAR(30) NOT NULL,
			IsActive BIT NOT NULL CONSTRAINT DF_SalaryComponents_IsActive DEFAULT (1),
			CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SalaryComponents_CreatedAt DEFAULT SYSUTCDATETIME(),
			UpdatedAt DATETIME2(3) NULL
		);
	END;

	IF OBJECT_ID(N'dbo.PayrollPolicies', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.PayrollPolicies (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollPolicies PRIMARY KEY,
			EffectiveDate DATETIME2(0) NOT NULL,
			ReferenceInsuranceSalary DECIMAL(19,2) NOT NULL,
			InsuranceCeiling DECIMAL(19,2) NOT NULL,
			EmployeeSocialInsuranceRate DECIMAL(9,6) NOT NULL,
			EmployeeHealthInsuranceRate DECIMAL(9,6) NOT NULL,
			EmployeeUnemploymentInsuranceRate DECIMAL(9,6) NOT NULL,
			EmployerSocialInsuranceRate DECIMAL(9,6) NOT NULL,
			EmployerHealthInsuranceRate DECIMAL(9,6) NOT NULL,
			EmployerUnemploymentInsuranceRate DECIMAL(9,6) NOT NULL,
			PersonalDeduction DECIMAL(19,2) NOT NULL,
			DependentDeduction DECIMAL(19,2) NOT NULL,
			CasualIncomeWithholdingThreshold DECIMAL(19,2) NOT NULL,
			CasualIncomeWithholdingRate DECIMAL(9,6) NOT NULL,
			MealTaxExemptLimit DECIMAL(19,2) NOT NULL,
			SourceReference NVARCHAR(500) NOT NULL,
			ChangeReason NVARCHAR(1000) NULL,
			CreatedBy INT NOT NULL,
			CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_PayrollPolicies_CreatedAt DEFAULT SYSUTCDATETIME(),
			CONSTRAINT UQ_PayrollPolicies_EffectiveDate UNIQUE (EffectiveDate)
		);
	END;

	IF OBJECT_ID(N'dbo.PayrollTaxBrackets', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.PayrollTaxBrackets (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollTaxBrackets PRIMARY KEY,
			PayrollPolicyId BIGINT NOT NULL,
			Sequence INT NOT NULL,
			UpperLimit DECIMAL(19,2) NULL,
			Rate DECIMAL(9,6) NOT NULL,
			Name NVARCHAR(200) NOT NULL,
			CONSTRAINT FK_PayrollTaxBrackets_PayrollPolicies FOREIGN KEY (PayrollPolicyId) REFERENCES dbo.PayrollPolicies(Id) ON DELETE CASCADE,
			CONSTRAINT UQ_PayrollTaxBrackets_Policy_Sequence UNIQUE (PayrollPolicyId, Sequence)
		);
	END;

	IF OBJECT_ID(N'dbo.SalaryTemplates', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.SalaryTemplates (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryTemplates PRIMARY KEY,
			Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_SalaryTemplates_Code UNIQUE,
			Name NVARCHAR(200) NOT NULL,
			PositionGroup NVARCHAR(120) NULL,
			IsActive BIT NOT NULL CONSTRAINT DF_SalaryTemplates_IsActive DEFAULT (1),
			CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SalaryTemplates_CreatedAt DEFAULT SYSUTCDATETIME()
		);
	END;

	IF OBJECT_ID(N'dbo.SalaryTemplateDetails', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.SalaryTemplateDetails (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryTemplateDetails PRIMARY KEY,
			SalaryTemplateId BIGINT NOT NULL,
			SalaryComponentId BIGINT NOT NULL,
			DefaultValue DECIMAL(19,2) NULL,
			Formula NVARCHAR(2000) NULL,
			CONSTRAINT FK_SalaryTemplateDetails_Template FOREIGN KEY (SalaryTemplateId) REFERENCES dbo.SalaryTemplates(Id) ON DELETE CASCADE,
			CONSTRAINT FK_SalaryTemplateDetails_Component FOREIGN KEY (SalaryComponentId) REFERENCES dbo.SalaryComponents(Id),
			CONSTRAINT UQ_SalaryTemplateDetails UNIQUE (SalaryTemplateId, SalaryComponentId)
		);
	END;

	IF OBJECT_ID(N'dbo.EmployeeSalaries', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.EmployeeSalaries (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSalaries PRIMARY KEY,
			EmployeeId INT NOT NULL,
			SalaryTemplateId BIGINT NULL,
			EffectiveDate DATE NOT NULL,
			EndDate DATE NULL,
			Status NVARCHAR(30) NOT NULL CONSTRAINT DF_EmployeeSalaries_Status DEFAULT (N'Active'),
			IsCasualIncome BIT NOT NULL CONSTRAINT DF_EmployeeSalaries_IsCasualIncome DEFAULT (0),
			Reason NVARCHAR(1000) NULL,
			CreatedBy INT NOT NULL,
			ApprovedBy INT NULL,
			CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmployeeSalaries_CreatedAt DEFAULT SYSUTCDATETIME(),
			CONSTRAINT FK_EmployeeSalaries_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id) ON DELETE CASCADE,
			CONSTRAINT FK_EmployeeSalaries_Template FOREIGN KEY (SalaryTemplateId) REFERENCES dbo.SalaryTemplates(Id) ON DELETE SET NULL,
			CONSTRAINT FK_EmployeeSalaries_CreatedBy FOREIGN KEY (CreatedBy) REFERENCES dbo.Employees(Id),
			CONSTRAINT FK_EmployeeSalaries_ApprovedBy FOREIGN KEY (ApprovedBy) REFERENCES dbo.Employees(Id),
			CONSTRAINT CK_EmployeeSalaries_DateRange CHECK (EndDate IS NULL OR EndDate >= EffectiveDate)
		);
	END;

	IF COL_LENGTH(N'dbo.EmployeeSalaries', N'IsCasualIncome') IS NULL
		ALTER TABLE dbo.EmployeeSalaries ADD IsCasualIncome BIT NOT NULL CONSTRAINT DF_EmployeeSalaries_IsCasualIncome DEFAULT (0) WITH VALUES;
	IF COL_LENGTH(N'dbo.EmployeeSalaries', N'Reason') IS NULL
		ALTER TABLE dbo.EmployeeSalaries ADD Reason NVARCHAR(1000) NULL;

	IF OBJECT_ID(N'dbo.EmployeeSalaryDetails', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.EmployeeSalaryDetails (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSalaryDetails PRIMARY KEY,
			EmployeeSalaryId BIGINT NOT NULL,
			SalaryComponentId BIGINT NOT NULL,
			Amount DECIMAL(19,2) NULL,
			Formula NVARCHAR(2000) NULL,
			EncryptedAmount VARBINARY(MAX) NULL,
			CONSTRAINT FK_EmployeeSalaryDetails_Salary FOREIGN KEY (EmployeeSalaryId) REFERENCES dbo.EmployeeSalaries(Id) ON DELETE CASCADE,
			CONSTRAINT FK_EmployeeSalaryDetails_Component FOREIGN KEY (SalaryComponentId) REFERENCES dbo.SalaryComponents(Id),
			CONSTRAINT UQ_EmployeeSalaryDetails UNIQUE (EmployeeSalaryId, SalaryComponentId)
		);
	END;

	IF OBJECT_ID(N'dbo.SalaryAuditTrails', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.SalaryAuditTrails (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryAuditTrails PRIMARY KEY,
			EmployeeSalaryId BIGINT NULL,
			EmployeeSalaryDetailId BIGINT NULL,
			Action NVARCHAR(40) NOT NULL,
			FieldName NVARCHAR(120) NOT NULL,
			OldValue NVARCHAR(MAX) NULL,
			NewValue NVARCHAR(MAX) NULL,
			Reason NVARCHAR(1000) NULL,
			DecisionNo NVARCHAR(100) NULL,
			ChangedBy INT NOT NULL,
			ChangedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SalaryAuditTrails_ChangedAt DEFAULT SYSUTCDATETIME()
		);
	END;

	IF OBJECT_ID(N'dbo.PayrollHolidays', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.PayrollHolidays (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollHolidays PRIMARY KEY,
			HolidayDate DATE NOT NULL,
			Name NVARCHAR(200) NOT NULL,
			IsActive BIT NOT NULL CONSTRAINT DF_PayrollHolidays_IsActive DEFAULT (1),
			CONSTRAINT UQ_PayrollHolidays_HolidayDate UNIQUE (HolidayDate)
		);
	END;

	IF OBJECT_ID(N'dbo.WorkGroups', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.WorkGroups (
			Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkGroups PRIMARY KEY,
			Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_WorkGroups_Code UNIQUE,
			Name NVARCHAR(200) NOT NULL,
			DepartmentId INT NULL,
			DefaultShiftId INT NULL,
			IsActive BIT NOT NULL CONSTRAINT DF_WorkGroups_IsActive DEFAULT (1),
			CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_WorkGroups_CreatedAt DEFAULT SYSUTCDATETIME(),
			CONSTRAINT FK_WorkGroups_Department FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE SET NULL,
			CONSTRAINT FK_WorkGroups_DefaultShift FOREIGN KEY (DefaultShiftId) REFERENCES dbo.Shifts(Id) ON DELETE SET NULL
		);
	END;

	IF OBJECT_ID(N'dbo.GroupMembers', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.GroupMembers (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GroupMembers PRIMARY KEY,
			WorkGroupId INT NOT NULL,
			EmployeeId INT NOT NULL,
			EffectiveDate DATE NOT NULL,
			EndDate DATE NULL,
			CONSTRAINT CK_GroupMembers_DateRange CHECK (EndDate IS NULL OR EndDate >= EffectiveDate),
			CONSTRAINT FK_GroupMembers_Group FOREIGN KEY (WorkGroupId) REFERENCES dbo.WorkGroups(Id) ON DELETE CASCADE,
			CONSTRAINT FK_GroupMembers_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id) ON DELETE CASCADE
		);
	END;

	IF OBJECT_ID(N'dbo.GroupSchedules', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.GroupSchedules (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GroupSchedules PRIMARY KEY,
			WorkGroupId INT NOT NULL,
			WorkDate DATE NOT NULL,
			ShiftId INT NOT NULL,
			CreatedBy INT NOT NULL,
			CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_GroupSchedules_CreatedAt DEFAULT SYSUTCDATETIME(),
			CONSTRAINT UQ_GroupSchedules_Group_Date UNIQUE (WorkGroupId, WorkDate),
			CONSTRAINT FK_GroupSchedules_Group FOREIGN KEY (WorkGroupId) REFERENCES dbo.WorkGroups(Id) ON DELETE CASCADE,
			CONSTRAINT FK_GroupSchedules_Shift FOREIGN KEY (ShiftId) REFERENCES dbo.Shifts(Id),
			CONSTRAINT FK_GroupSchedules_Creator FOREIGN KEY (CreatedBy) REFERENCES dbo.Employees(Id)
		);
	END;

	IF OBJECT_ID(N'dbo.EmployeeShiftExceptions', N'U') IS NULL
	BEGIN
		CREATE TABLE dbo.EmployeeShiftExceptions (
			Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeShiftExceptions PRIMARY KEY,
			EmployeeId INT NOT NULL,
			WorkDate DATE NOT NULL,
			OverrideShiftId INT NOT NULL,
			Reason NVARCHAR(1000) NOT NULL,
			CreatedBy INT NOT NULL,
			CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmployeeShiftExceptions_CreatedAt DEFAULT SYSUTCDATETIME(),
			UpdatedAt DATETIME2(3) NULL,
			CONSTRAINT UQ_EmployeeShiftExceptions_Employee_Date UNIQUE (EmployeeId, WorkDate),
			CONSTRAINT FK_EmployeeShiftExceptions_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id) ON DELETE CASCADE,
			CONSTRAINT FK_EmployeeShiftExceptions_Shift FOREIGN KEY (OverrideShiftId) REFERENCES dbo.Shifts(Id),
			CONSTRAINT FK_EmployeeShiftExceptions_Creator FOREIGN KEY (CreatedBy) REFERENCES dbo.Employees(Id)
		);
	END;

	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeeSalaries') AND name = N'IX_EmployeeSalaries_Employee_EffectiveDate')
		CREATE INDEX IX_EmployeeSalaries_Employee_EffectiveDate ON dbo.EmployeeSalaries(EmployeeId, EffectiveDate DESC, EndDate, Status);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeeSalaries') AND name = N'IX_EmployeeSalaries_Status_EffectiveDate')
		CREATE INDEX IX_EmployeeSalaries_Status_EffectiveDate ON dbo.EmployeeSalaries(Status, EffectiveDate DESC);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeeSalaryDetails') AND name = N'IX_EmployeeSalaryDetails_Component')
		CREATE INDEX IX_EmployeeSalaryDetails_Component ON dbo.EmployeeSalaryDetails(SalaryComponentId, EmployeeSalaryId);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SalaryAuditTrails') AND name = N'IX_SalaryAuditTrails_EmployeeSalary_ChangedAt')
		CREATE INDEX IX_SalaryAuditTrails_EmployeeSalary_ChangedAt ON dbo.SalaryAuditTrails(EmployeeSalaryId, ChangedAt DESC);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GroupMembers') AND name = N'IX_GroupMembers_Employee_Effective')
		CREATE INDEX IX_GroupMembers_Employee_Effective ON dbo.GroupMembers(EmployeeId, EffectiveDate, EndDate);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WorkGroups') AND name = N'IX_WorkGroups_DepartmentId_IsActive')
		CREATE INDEX IX_WorkGroups_DepartmentId_IsActive ON dbo.WorkGroups(DepartmentId, IsActive);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GroupMembers') AND name = N'IX_GroupMembers_Group_Effective')
		CREATE INDEX IX_GroupMembers_Group_Effective ON dbo.GroupMembers(WorkGroupId, EffectiveDate, EndDate);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GroupSchedules') AND name = N'IX_GroupSchedules_Date_Group')
		CREATE INDEX IX_GroupSchedules_Date_Group ON dbo.GroupSchedules(WorkDate, WorkGroupId);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeeShiftExceptions') AND name = N'IX_EmployeeShiftExceptions_Date_Employee')
		CREATE INDEX IX_EmployeeShiftExceptions_Date_Employee ON dbo.EmployeeShiftExceptions(WorkDate, EmployeeId);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PayrollPolicies_EffectiveDate' AND object_id = OBJECT_ID(N'dbo.PayrollPolicies'))
		CREATE INDEX IX_PayrollPolicies_EffectiveDate ON dbo.PayrollPolicies(EffectiveDate DESC);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PayrollTaxBrackets_Policy_Sequence' AND object_id = OBJECT_ID(N'dbo.PayrollTaxBrackets'))
		CREATE INDEX IX_PayrollTaxBrackets_Policy_Sequence ON dbo.PayrollTaxBrackets(PayrollPolicyId, Sequence);

	INSERT INTO dbo.SalaryComponents (Code, Name, Category, CalculationType, Formula, TaxTreatment, TaxExemptLimit, InsuranceTreatment)
	SELECT v.Code, v.Name, v.Category, v.CalculationType, v.Formula, v.TaxTreatment, v.TaxExemptLimit, v.InsuranceTreatment
	FROM (VALUES
		(N'SALARY_BASE', N'Lương cơ bản', N'Earning', N'Manual', NULL, N'Taxable', NULL, N'Included'),
		(N'ALLOW_MEAL', N'Phụ cấp ăn trưa', N'Earning', N'Fixed', NULL, N'CappedExempt', CONVERT(DECIMAL(19,2), 730000), N'Excluded'),
		(N'ALLOW_RESP', N'Phụ cấp trách nhiệm', N'Earning', N'Fixed', NULL, N'Taxable', NULL, N'Included'),
		(N'BONUS_KPI', N'Thưởng KPI', N'Earning', N'Formula', N'KPI_RATE * SALARY_BASE', N'Taxable', NULL, N'Excluded'),
		(N'KPI_RATE', N'Tỷ lệ KPI', N'CalculationBase', N'Manual', NULL, N'Exempt', NULL, N'Excluded'),
		(N'SALES_RATE', N'Tỷ lệ doanh số', N'CalculationBase', N'Manual', NULL, N'Exempt', NULL, N'Excluded'),
		(N'REVENUE', N'Doanh thu', N'CalculationBase', N'Manual', NULL, N'Exempt', NULL, N'Excluded'),
		(N'INSURANCE_EMP', N'BHXH NLĐ', N'Deduction', N'Formula', N'INSURANCE_BASE * EMP_INSURANCE_RATE', N'Exempt', NULL, N'Excluded'),
		(N'PERSONAL_TAX', N'Thuế TNCN', N'Deduction', N'Formula', N'TAXABLE_INCOME * TAX_RATE', N'Exempt', NULL, N'Excluded')
	) AS v(Code, Name, Category, CalculationType, Formula, TaxTreatment, TaxExemptLimit, InsuranceTreatment)
	WHERE NOT EXISTS (SELECT 1 FROM dbo.SalaryComponents c WHERE c.Code = v.Code);

	INSERT INTO dbo.SalaryTemplates (Code, Name, PositionGroup)
	SELECT v.Code, v.Name, v.PositionGroup
	FROM (VALUES
		(N'TPL_TECH_SENIOR', N'Tech Senior', N'Kỹ thuật'),
		(N'TPL_SALES_SPECIALIST', N'Sales Specialist', N'Kinh doanh'),
		(N'TPL_OFFICE', N'Khối văn phòng', N'Vận hành')
	) AS v(Code, Name, PositionGroup)
	WHERE NOT EXISTS (SELECT 1 FROM dbo.SalaryTemplates t WHERE t.Code = v.Code);

	INSERT INTO dbo.SalaryTemplateDetails (SalaryTemplateId, SalaryComponentId, DefaultValue, Formula)
	SELECT t.Id, c.Id, d.DefaultValue, d.Formula
	FROM (VALUES
		(N'TPL_TECH_SENIOR', N'SALARY_BASE', CONVERT(DECIMAL(19,2), NULL), NULL),
		(N'TPL_TECH_SENIOR', N'ALLOW_RESP', CONVERT(DECIMAL(19,2), 2000000), NULL),
		(N'TPL_TECH_SENIOR', N'KPI_RATE', CONVERT(DECIMAL(19,2), 0), NULL),
		(N'TPL_TECH_SENIOR', N'BONUS_KPI', CONVERT(DECIMAL(19,2), NULL), N'KPI_RATE * SALARY_BASE'),
		(N'TPL_SALES_SPECIALIST', N'SALARY_BASE', CONVERT(DECIMAL(19,2), NULL), NULL),
		(N'TPL_SALES_SPECIALIST', N'SALES_RATE', CONVERT(DECIMAL(19,2), 0), NULL),
		(N'TPL_SALES_SPECIALIST', N'REVENUE', CONVERT(DECIMAL(19,2), 0), NULL),
		(N'TPL_SALES_SPECIALIST', N'BONUS_KPI', CONVERT(DECIMAL(19,2), NULL), N'SALES_RATE * REVENUE'),
		(N'TPL_OFFICE', N'SALARY_BASE', CONVERT(DECIMAL(19,2), NULL), NULL),
		(N'TPL_OFFICE', N'ALLOW_MEAL', CONVERT(DECIMAL(19,2), 730000), NULL),
		(N'TPL_OFFICE', N'ALLOW_RESP', CONVERT(DECIMAL(19,2), 1000000), NULL)
	) AS d(TemplateCode, ComponentCode, DefaultValue, Formula)
	INNER JOIN dbo.SalaryTemplates t ON t.Code = d.TemplateCode
	INNER JOIN dbo.SalaryComponents c ON c.Code = d.ComponentCode
	WHERE NOT EXISTS (
		SELECT 1 FROM dbo.SalaryTemplateDetails x
		WHERE x.SalaryTemplateId = t.Id AND x.SalaryComponentId = c.Id
	);

	DECLARE @EffectiveDate DATETIME2(0) = CONVERT(DATETIME2(0), '2026-07-01T00:00:00');
	DECLARE @PolicyId BIGINT;
	SELECT @PolicyId = Id FROM dbo.PayrollPolicies WHERE EffectiveDate = @EffectiveDate;

	IF @PolicyId IS NULL
	BEGIN
		INSERT dbo.PayrollPolicies (
			EffectiveDate, ReferenceInsuranceSalary, InsuranceCeiling,
			EmployeeSocialInsuranceRate, EmployeeHealthInsuranceRate, EmployeeUnemploymentInsuranceRate,
			EmployerSocialInsuranceRate, EmployerHealthInsuranceRate, EmployerUnemploymentInsuranceRate,
			PersonalDeduction, DependentDeduction, CasualIncomeWithholdingThreshold,
			CasualIncomeWithholdingRate, MealTaxExemptLimit, SourceReference, ChangeReason, CreatedBy)
		VALUES (
			@EffectiveDate, 2530000, 50600000,
			0.080000, 0.015000, 0.010000,
			0.175000, 0.030000, 0.010000,
			15500000, 6200000, 5000000,
			0.100000, 1200000,
			N'Policy configuration effective 2026-07-01',
			N'Initial versioned tax and social insurance parameters',
			0);
		SET @PolicyId = SCOPE_IDENTITY();
	END;

	DECLARE @ExpectedBrackets TABLE (
		Sequence INT NOT NULL PRIMARY KEY,
		UpperLimit DECIMAL(19,2) NULL,
		Rate DECIMAL(9,6) NOT NULL,
		Name NVARCHAR(200) NOT NULL
	);
	INSERT INTO @ExpectedBrackets (Sequence, UpperLimit, Rate, Name)
	VALUES
		(1, 10000000, 0.050000, N'Thu nhập tính thuế đến 10 triệu/tháng - 5%'),
		(2, 30000000, 0.100000, N'Thu nhập tính thuế trên 10 đến 30 triệu/tháng - 10%'),
		(3, 60000000, 0.200000, N'Thu nhập tính thuế trên 30 đến 60 triệu/tháng - 20%'),
		(4, 100000000, 0.300000, N'Thu nhập tính thuế trên 60 đến 100 triệu/tháng - 30%'),
		(5, NULL, 0.350000, N'Thu nhập tính thuế trên 100 triệu/tháng - 35%');

	DECLARE @LegacySevenBrackets TABLE (
		Sequence INT NOT NULL PRIMARY KEY,
		UpperLimit DECIMAL(19,2) NULL,
		Rate DECIMAL(9,6) NOT NULL
	);
	INSERT INTO @LegacySevenBrackets (Sequence, UpperLimit, Rate)
	VALUES
		(1, 5000000, 0.050000),
		(2, 10000000, 0.100000),
		(3, 18000000, 0.150000),
		(4, 32000000, 0.200000),
		(5, 52000000, 0.250000),
		(6, 80000000, 0.300000),
		(7, NULL, 0.350000);

	IF EXISTS (
		SELECT p.Id
		FROM dbo.PayrollPolicies p
		WHERE p.EffectiveDate >= @EffectiveDate
			AND NOT EXISTS (
				SELECT b.Sequence, b.UpperLimit, b.Rate
				FROM dbo.PayrollTaxBrackets b
				WHERE b.PayrollPolicyId = p.Id
				EXCEPT
				SELECT Sequence, UpperLimit, Rate FROM @ExpectedBrackets
			)
			AND NOT EXISTS (
				SELECT Sequence, UpperLimit, Rate FROM @ExpectedBrackets
				EXCEPT
				SELECT b.Sequence, b.UpperLimit, b.Rate
				FROM dbo.PayrollTaxBrackets b
				WHERE b.PayrollPolicyId = p.Id
			)
	)
		PRINT 'INFO: Five-tier tax brackets are already configured for a policy effective on/after 2026-07-01.';
	ELSE IF EXISTS (
		SELECT 1
		FROM dbo.PayrollPolicies p
		WHERE p.EffectiveDate >= @EffectiveDate
			AND EXISTS (SELECT 1 FROM dbo.PayrollTaxBrackets b WHERE b.PayrollPolicyId = p.Id)
	)
		PRINT 'WARNING: Existing policy bracket data was preserved. A policy effective on/after 2026-07-01 has different tax tiers; review it in Salary Configuration and create a new policy version with the five current brackets.';

	COMMIT TRANSACTION;
	PRINT 'Payroll schema deployment completed. Existing records were preserved.';
END TRY
BEGIN CATCH
	IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
	THROW;
END CATCH;
