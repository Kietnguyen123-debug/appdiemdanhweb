/*
  Versioned Payroll Policy - SQL Server
  Safe to run repeatedly. Existing versions are never overwritten.
  Run this script after reviewing the supplied legal source with compliance.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.PayrollPolicies', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.PayrollPolicies
	(
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
	CREATE TABLE dbo.PayrollTaxBrackets
	(
		Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollTaxBrackets PRIMARY KEY,
		PayrollPolicyId BIGINT NOT NULL,
		Sequence INT NOT NULL,
		UpperLimit DECIMAL(19,2) NULL,
		Rate DECIMAL(9,6) NOT NULL,
		Name NVARCHAR(200) NOT NULL,
		CONSTRAINT FK_PayrollTaxBrackets_PayrollPolicies FOREIGN KEY (PayrollPolicyId)
			REFERENCES dbo.PayrollPolicies(Id) ON DELETE CASCADE,
		CONSTRAINT UQ_PayrollTaxBrackets_Policy_Sequence UNIQUE (PayrollPolicyId, Sequence)
	);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PayrollPolicies_EffectiveDate' AND object_id = OBJECT_ID(N'dbo.PayrollPolicies'))
	CREATE INDEX IX_PayrollPolicies_EffectiveDate ON dbo.PayrollPolicies(EffectiveDate DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PayrollTaxBrackets_Policy_Sequence' AND object_id = OBJECT_ID(N'dbo.PayrollTaxBrackets'))
	CREATE INDEX IX_PayrollTaxBrackets_Policy_Sequence ON dbo.PayrollTaxBrackets(PayrollPolicyId, Sequence);

DECLARE @PolicyId BIGINT;
SELECT @PolicyId = Id FROM dbo.PayrollPolicies WHERE EffectiveDate = CONVERT(datetime2(0), '2026-07-01T00:00:00');

IF @PolicyId IS NULL
BEGIN
	INSERT dbo.PayrollPolicies
	(
		EffectiveDate, ReferenceInsuranceSalary, InsuranceCeiling,
		EmployeeSocialInsuranceRate, EmployeeHealthInsuranceRate, EmployeeUnemploymentInsuranceRate,
		EmployerSocialInsuranceRate, EmployerHealthInsuranceRate, EmployerUnemploymentInsuranceRate,
		PersonalDeduction, DependentDeduction, CasualIncomeWithholdingThreshold,
		CasualIncomeWithholdingRate, MealTaxExemptLimit, SourceReference, ChangeReason, CreatedBy
	)
	VALUES
	(
		'2026-07-01T00:00:00', 2530000, 50600000,
		0.080000, 0.015000, 0.010000,
		0.175000, 0.030000, 0.010000,
		15500000, 6200000, 5000000,
		0.100000, 1200000,
		N'Policy configuration effective 2026-07-01',
		N'Initial versioned tax and social insurance parameters',
		0
	);
	SET @PolicyId = SCOPE_IDENTITY();

	INSERT dbo.PayrollTaxBrackets (PayrollPolicyId, Sequence, UpperLimit, Rate, Name)
	VALUES
		(@PolicyId, 1, 10000000, 0.050000, N'Đến 10 triệu'),
		(@PolicyId, 2, 30000000, 0.100000, N'Trên 10 đến 30 triệu'),
		(@PolicyId, 3, 60000000, 0.200000, N'Trên 30 đến 60 triệu'),
		(@PolicyId, 4, 100000000, 0.300000, N'Trên 60 đến 100 triệu'),
		(@PolicyId, 5, NULL, 0.350000, N'Trên 100 triệu');
END
ELSE IF EXISTS
(
	SELECT 1
	FROM dbo.PayrollPolicies
	WHERE Id = @PolicyId
	  AND (ReferenceInsuranceSalary <> 2530000 OR InsuranceCeiling <> 50600000
		OR EmployeeSocialInsuranceRate <> 0.080000 OR EmployeeHealthInsuranceRate <> 0.015000
		OR EmployeeUnemploymentInsuranceRate <> 0.010000 OR EmployerSocialInsuranceRate <> 0.175000
		OR EmployerHealthInsuranceRate <> 0.030000 OR EmployerUnemploymentInsuranceRate <> 0.010000
		OR PersonalDeduction <> 15500000 OR DependentDeduction <> 6200000
		OR CasualIncomeWithholdingThreshold <> 5000000 OR CasualIncomeWithholdingRate <> 0.100000
		OR MealTaxExemptLimit <> 1200000)
)
BEGIN
	THROW 51001, 'Payroll policy 2026-07-01 already exists with different values; create a new reviewed version instead of overwriting history.', 1;
END;

COMMIT TRANSACTION;

SELECT p.*, b.Sequence, b.UpperLimit, b.Rate, b.Name AS BracketName
FROM dbo.PayrollPolicies p
LEFT JOIN dbo.PayrollTaxBrackets b ON b.PayrollPolicyId = p.Id
WHERE p.EffectiveDate = '2026-07-01T00:00:00'
ORDER BY b.Sequence;
