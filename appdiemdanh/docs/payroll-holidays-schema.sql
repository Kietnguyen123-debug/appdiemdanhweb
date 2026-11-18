SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PayrollHolidays', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.PayrollHolidays
	(
		Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollHolidays PRIMARY KEY,
		HolidayDate DATE NOT NULL,
		Name NVARCHAR(200) NOT NULL,
		IsActive BIT NOT NULL CONSTRAINT DF_PayrollHolidays_IsActive DEFAULT (1),
		CONSTRAINT UQ_PayrollHolidays_HolidayDate UNIQUE (HolidayDate)
	);
END;
