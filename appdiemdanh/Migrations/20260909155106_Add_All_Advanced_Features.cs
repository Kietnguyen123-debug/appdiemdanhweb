using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace appdiemdanh.Migrations
{
    /// <inheritdoc />
    public partial class Add_All_Advanced_Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DependentCount",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SocialInsuranceBase",
                table: "Employees",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OvertimeRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Hours = table.Column<double>(type: "float(8)", precision: 8, scale: 2, nullable: false),
                    Multiplier = table.Column<double>(type: "float(5)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OvertimeRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalaryAdvances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryAdvances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryAdvances_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalaryAdvances_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeRecords_EmployeeId_WorkDate",
                table: "OvertimeRecords",
                columns: new[] { "EmployeeId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryAdvances_ApprovedById",
                table: "SalaryAdvances",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryAdvances_EmployeeId_RequestDate",
                table: "SalaryAdvances",
                columns: new[] { "EmployeeId", "RequestDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryAdvances_Status",
                table: "SalaryAdvances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimeRecords");

            migrationBuilder.DropTable(
                name: "SalaryAdvances");

            migrationBuilder.DropColumn(
                name: "DependentCount",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SocialInsuranceBase",
                table: "Employees");
        }
    }
}
