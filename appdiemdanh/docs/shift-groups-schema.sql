IF OBJECT_ID(N'dbo.ShiftGroups', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.ShiftGroups
	(
		Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ShiftGroups PRIMARY KEY,
		Code NVARCHAR(80) NOT NULL,
		Name NVARCHAR(200) NOT NULL,
		Description NVARCHAR(1000) NULL,
		IsActive BIT NOT NULL CONSTRAINT DF_ShiftGroups_IsActive DEFAULT (1),
		CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_ShiftGroups_CreatedAt DEFAULT (SYSUTCDATETIME()),
		CONSTRAINT UQ_ShiftGroups_Code UNIQUE (Code)
	);
END;
GO

IF OBJECT_ID(N'dbo.ShiftGroupEmployees', N'U') IS NULL
BEGIN
	CREATE TABLE dbo.ShiftGroupEmployees
	(
		ShiftGroupId INT NOT NULL,
		EmployeeId INT NOT NULL,
		CONSTRAINT PK_ShiftGroupEmployees PRIMARY KEY (ShiftGroupId, EmployeeId),
		CONSTRAINT FK_ShiftGroupEmployees_ShiftGroups FOREIGN KEY (ShiftGroupId)
			REFERENCES dbo.ShiftGroups (Id) ON DELETE CASCADE,
		CONSTRAINT FK_ShiftGroupEmployees_Employees FOREIGN KEY (EmployeeId)
			REFERENCES dbo.Employees (Id) ON DELETE CASCADE
	);

	CREATE INDEX IX_ShiftGroupEmployees_EmployeeId
		ON dbo.ShiftGroupEmployees (EmployeeId);
END;
GO
