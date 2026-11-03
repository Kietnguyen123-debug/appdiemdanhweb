using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace appdiemdanh.Migrations
{
    /// <inheritdoc />
    public partial class AddEffectiveSalaryAndLatePenaltyPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeSalaryConfigs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    BaseSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAllowances = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InsuranceSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PenaltyPerLateMinute = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DependentCount = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSalaryConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeSalaryConfigs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LatePenaltyPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    AmountPerMinute = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatePenaltyPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LatePenaltyRanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyId = table.Column<int>(type: "int", nullable: false),
                    FromMinute = table.Column<int>(type: "int", nullable: false),
                    ToMinute = table.Column<int>(type: "int", nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatePenaltyRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LatePenaltyRanges_LatePenaltyPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "LatePenaltyPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSalaryConfigs_EmployeeId_EffectiveFrom",
                table: "EmployeeSalaryConfigs",
                columns: new[] { "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_LatePenaltyPolicies_IsActive",
                table: "LatePenaltyPolicies",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LatePenaltyRanges_PolicyId_FromMinute_ToMinute",
                table: "LatePenaltyRanges",
                columns: new[] { "PolicyId", "FromMinute", "ToMinute" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeSalaryConfigs");

            migrationBuilder.DropTable(
                name: "LatePenaltyRanges");

            migrationBuilder.DropTable(
                name: "LatePenaltyPolicies");
        }
    }
}
