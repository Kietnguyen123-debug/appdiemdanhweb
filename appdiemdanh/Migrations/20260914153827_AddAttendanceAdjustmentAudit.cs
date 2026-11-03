using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace appdiemdanh.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAdjustmentAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceId = table.Column<long>(type: "bigint", nullable: false),
                    AdminEmployeeId = table.Column<int>(type: "int", nullable: false),
                    PreviousCheckInTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousCheckOutTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewCheckInTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewCheckOutTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "AttendanceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_Employees_AdminEmployeeId",
                        column: x => x.AdminEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_AdminEmployeeId",
                table: "AttendanceAdjustments",
                column: "AdminEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_AttendanceId_CreatedAt",
                table: "AttendanceAdjustments",
                columns: new[] { "AttendanceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceAdjustments");
        }
    }
}
