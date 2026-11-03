-- SQL Server schema reference for the salary configuration module.
-- Apply through an EF migration after reviewing existing production tables.

CREATE TABLE dbo.SalaryComponents (
	Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryComponents PRIMARY KEY,
	Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_SalaryComponents_Code UNIQUE,
	Name NVARCHAR(200) NOT NULL,
	Category NVARCHAR(30) NOT NULL, -- Earning, Deduction, CalculationBase
	CalculationType NVARCHAR(30) NOT NULL, -- Manual, Fixed, Formula
	Formula NVARCHAR(2000) NULL,
	TaxTreatment NVARCHAR(30) NOT NULL, -- Taxable, Exempt, CappedExempt
	TaxExemptLimit DECIMAL(19,2) NULL,
	InsuranceTreatment NVARCHAR(30) NOT NULL, -- Included, Excluded
	IsActive BIT NOT NULL CONSTRAINT DF_SalaryComponents_IsActive DEFAULT 1,
	CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SalaryComponents_CreatedAt DEFAULT SYSUTCDATETIME(),
	UpdatedAt DATETIME2(3) NULL
);

/* Demo data for SalaryConfig. Safe to run repeatedly: existing codes are preserved. */
INSERT INTO dbo.SalaryComponents
	(Code, Name, Category, CalculationType, Formula, TaxTreatment, TaxExemptLimit, InsuranceTreatment)
SELECT v.Code, v.Name, v.Category, v.CalculationType, v.Formula, v.TaxTreatment, v.TaxExemptLimit, v.InsuranceTreatment
FROM (VALUES
	(N'SALARY_BASE', N'Lương cơ bản', N'Earning', N'Manual', NULL, N'Taxable', NULL, N'Included'),
	(N'ALLOW_MEAL', N'Phụ cấp ăn trưa', N'Earning', N'Fixed', NULL, N'CappedExempt', CONVERT(decimal(19,2), 730000), N'Excluded'),
	(N'ALLOW_RESP', N'Phụ cấp trách nhiệm', N'Earning', N'Fixed', NULL, N'Taxable', NULL, N'Included'),
	(N'BONUS_KPI', N'Thưởng KPI', N'Earning', N'Formula', N'KPI_RATE * SALARY_BASE', N'Taxable', NULL, N'Excluded'),
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
	(N'TPL_TECH_SENIOR', N'SALARY_BASE', CONVERT(decimal(19,2), NULL), NULL),
	(N'TPL_TECH_SENIOR', N'ALLOW_RESP', CONVERT(decimal(19,2), 2000000), NULL),
	(N'TPL_TECH_SENIOR', N'BONUS_KPI', CONVERT(decimal(19,2), NULL), N'KPI_RATE * SALARY_BASE'),
	(N'TPL_SALES_SPECIALIST', N'SALARY_BASE', CONVERT(decimal(19,2), NULL), NULL),
	(N'TPL_SALES_SPECIALIST', N'BONUS_KPI', CONVERT(decimal(19,2), NULL), N'SALES_RATE * REVENUE'),
	(N'TPL_OFFICE', N'SALARY_BASE', CONVERT(decimal(19,2), NULL), NULL),
	(N'TPL_OFFICE', N'ALLOW_MEAL', CONVERT(decimal(19,2), 730000), NULL),
	(N'TPL_OFFICE', N'ALLOW_RESP', CONVERT(decimal(19,2), 1000000), NULL)
) AS d(TemplateCode, ComponentCode, DefaultValue, Formula)
INNER JOIN dbo.SalaryTemplates t ON t.Code = d.TemplateCode
INNER JOIN dbo.SalaryComponents c ON c.Code = d.ComponentCode
WHERE NOT EXISTS (
	SELECT 1
	FROM dbo.SalaryTemplateDetails x
	WHERE x.SalaryTemplateId = t.Id AND x.SalaryComponentId = c.Id
);

CREATE TABLE dbo.SalaryTemplates (
	Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryTemplates PRIMARY KEY,
	Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_SalaryTemplates_Code UNIQUE,
	Name NVARCHAR(200) NOT NULL,
	PositionGroup NVARCHAR(120) NULL,
	IsActive BIT NOT NULL CONSTRAINT DF_SalaryTemplates_IsActive DEFAULT 1,
	CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SalaryTemplates_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.SalaryTemplateDetails (
	Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalaryTemplateDetails PRIMARY KEY,
	SalaryTemplateId BIGINT NOT NULL,
	SalaryComponentId BIGINT NOT NULL,
	DefaultValue DECIMAL(19,2) NULL,
	Formula NVARCHAR(2000) NULL,
	CONSTRAINT FK_SalaryTemplateDetails_Template FOREIGN KEY (SalaryTemplateId) REFERENCES dbo.SalaryTemplates(Id),
	CONSTRAINT FK_SalaryTemplateDetails_Component FOREIGN KEY (SalaryComponentId) REFERENCES dbo.SalaryComponents(Id),
	CONSTRAINT UQ_SalaryTemplateDetails UNIQUE (SalaryTemplateId, SalaryComponentId)
);

CREATE TABLE dbo.EmployeeSalaries (
	Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSalaries PRIMARY KEY,
	EmployeeId INT NOT NULL,
	SalaryTemplateId BIGINT NULL,
	EffectiveDate DATE NOT NULL,
	EndDate DATE NULL,
	Status NVARCHAR(30) NOT NULL, -- Draft, PendingApproval, Active, Inactive
	Reason NVARCHAR(1000) NULL,
	DecisionNo NVARCHAR(100) NULL,
	CreatedBy INT NOT NULL,
	ApprovedBy INT NULL,
	CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmployeeSalaries_CreatedAt DEFAULT SYSUTCDATETIME(),
	ApprovedAt DATETIME2(3) NULL,
	CONSTRAINT FK_EmployeeSalaries_Employee FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id),
	CONSTRAINT FK_EmployeeSalaries_Template FOREIGN KEY (SalaryTemplateId) REFERENCES dbo.SalaryTemplates(Id),
	CONSTRAINT CK_EmployeeSalaries_DateRange CHECK (EndDate IS NULL OR EndDate >= EffectiveDate)
);

CREATE TABLE dbo.EmployeeSalaryDetails (
	Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSalaryDetails PRIMARY KEY,
	EmployeeSalaryId BIGINT NOT NULL,
	SalaryComponentId BIGINT NOT NULL,
	Amount DECIMAL(19,2) NULL,
	Formula NVARCHAR(2000) NULL,
	EncryptedAmount VARBINARY(MAX) NULL,
	CONSTRAINT FK_EmployeeSalaryDetails_Salary FOREIGN KEY (EmployeeSalaryId) REFERENCES dbo.EmployeeSalaries(Id),
	CONSTRAINT FK_EmployeeSalaryDetails_Component FOREIGN KEY (SalaryComponentId) REFERENCES dbo.SalaryComponents(Id),
	CONSTRAINT UQ_EmployeeSalaryDetails UNIQUE (EmployeeSalaryId, SalaryComponentId)
);

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

CREATE INDEX IX_EmployeeSalaries_Employee_EffectiveDate ON dbo.EmployeeSalaries(EmployeeId, EffectiveDate DESC, EndDate, Status);
CREATE INDEX IX_EmployeeSalaries_Status_EffectiveDate ON dbo.EmployeeSalaries(Status, EffectiveDate DESC);
CREATE INDEX IX_EmployeeSalaryDetails_Component ON dbo.EmployeeSalaryDetails(SalaryComponentId, EmployeeSalaryId);
CREATE INDEX IX_SalaryAuditTrails_EmployeeSalary_ChangedAt ON dbo.SalaryAuditTrails(EmployeeSalaryId, ChangedAt DESC);

-- Active snapshot for a payroll period:
-- WHERE EmployeeId = @EmployeeId AND Status = 'Active'
--   AND EffectiveDate <= @PeriodStart
--   AND (EndDate IS NULL OR EndDate >= @PeriodStart)

/* Hybrid group + individual exception scheduling (SQL Server) */
CREATE TABLE dbo.WorkGroups (
	Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkGroups PRIMARY KEY,
	Code NVARCHAR(80) NOT NULL CONSTRAINT UQ_WorkGroups_Code UNIQUE,
	Name NVARCHAR(200) NOT NULL,
	DepartmentId INT NULL,
	DefaultShiftId INT NULL,
	IsActive BIT NOT NULL CONSTRAINT DF_WorkGroups_IsActive DEFAULT 1,
	CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_WorkGroups_CreatedAt DEFAULT SYSUTCDATETIME(),
	CONSTRAINT FK_WorkGroups_Department FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE SET NULL,
	CONSTRAINT FK_WorkGroups_DefaultShift FOREIGN KEY (DefaultShiftId) REFERENCES dbo.Shifts(Id) ON DELETE SET NULL
);

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

CREATE INDEX IX_GroupMembers_Employee_Effective ON dbo.GroupMembers(EmployeeId, EffectiveDate, EndDate);
CREATE INDEX IX_GroupMembers_Group_Effective ON dbo.GroupMembers(WorkGroupId, EffectiveDate, EndDate);
CREATE INDEX IX_GroupSchedules_Date_Group ON dbo.GroupSchedules(WorkDate, WorkGroupId);
CREATE INDEX IX_EmployeeShiftExceptions_Date_Employee ON dbo.EmployeeShiftExceptions(WorkDate, EmployeeId);

/* Waterfall: exception -> active group schedule -> legacy default EmployeeShift. */
-- @DepartmentId, @PeriodStart and @PeriodEnd are parameters.
WITH Calendar AS (
	SELECT @PeriodStart AS WorkDate
	UNION ALL SELECT DATEADD(DAY, 1, WorkDate) FROM Calendar WHERE WorkDate < @PeriodEnd
), EmployeesInScope AS (
	SELECT e.Id AS EmployeeId, e.EmployeeCode, e.FullName
	FROM dbo.Employees e WHERE e.IsActive = 1 AND (@DepartmentId IS NULL OR e.DepartmentId = @DepartmentId)
), Resolved AS (
	SELECT e.EmployeeId, e.EmployeeCode, e.FullName, c.WorkDate,
		   COALESCE(ex.OverrideShiftId, gs.ShiftId, es.ShiftId) AS ShiftId,
		   CASE WHEN ex.Id IS NOT NULL THEN 'Exception' WHEN gs.Id IS NOT NULL THEN 'Group' ELSE 'Default' END AS Source,
		   ex.Id AS ExceptionId, wg.Id AS WorkGroupId, wg.Name AS WorkGroupName
	FROM EmployeesInScope e CROSS JOIN Calendar c
	OUTER APPLY (SELECT TOP (1) gm.WorkGroupId FROM dbo.GroupMembers gm WHERE gm.EmployeeId=e.EmployeeId AND gm.EffectiveDate<=c.WorkDate AND (gm.EndDate IS NULL OR gm.EndDate>=c.WorkDate) ORDER BY gm.EffectiveDate DESC, gm.Id DESC) gm
	LEFT JOIN dbo.WorkGroups wg ON wg.Id=gm.WorkGroupId
	LEFT JOIN dbo.GroupSchedules gs ON gs.WorkGroupId=gm.WorkGroupId AND gs.WorkDate=c.WorkDate
	LEFT JOIN dbo.EmployeeShiftExceptions ex ON ex.EmployeeId=e.EmployeeId AND ex.WorkDate=c.WorkDate
	OUTER APPLY (SELECT TOP (1) es.ShiftId FROM dbo.EmployeeShifts es WHERE es.EmployeeId=e.EmployeeId AND es.EffectiveFromDate<=c.WorkDate AND (es.EffectiveToDate IS NULL OR es.EffectiveToDate>=c.WorkDate) ORDER BY es.EffectiveFromDate DESC, es.Id DESC) es
)
SELECT r.*, s.ShiftCode, s.ShiftName, s.StartTime, s.EndTime
FROM Resolved r LEFT JOIN dbo.Shifts s ON s.Id=r.ShiftId
ORDER BY r.EmployeeCode, r.WorkDate OPTION (MAXRECURSION 366);
