using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace appdiemdanh.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Branches_BranchId",
                table: "Attendances");

            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_EmployeeId",
                table: "Attendances");

            migrationBuilder.CreateTable(
                name: "EmployeeFcmTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    FcmToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BranchPublicIpId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeFcmTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeFcmTokens_BranchPublicIps_BranchPublicIpId",
                        column: x => x.BranchPublicIpId,
                        principalTable: "BranchPublicIps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EmployeeFcmTokens_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BranchPublicIpId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_BranchPublicIps_BranchPublicIpId",
                        column: x => x.BranchPublicIpId,
                        principalTable: "BranchPublicIps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeFcmTokens_BranchPublicIpId",
                table: "EmployeeFcmTokens",
                column: "BranchPublicIpId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeFcmTokens_EmployeeId_DeviceId",
                table: "EmployeeFcmTokens",
                columns: new[] { "EmployeeId", "DeviceId" },
                unique: true,
                filter: "[DeviceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_BranchPublicIpId",
                table: "NotificationLogs",
                column: "BranchPublicIpId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_EmployeeId_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "EmployeeId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Branches_BranchId",
                table: "Attendances",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Employees_EmployeeId",
                table: "Attendances",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Branches_BranchId",
                table: "Attendances");

            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Employees_EmployeeId",
                table: "Attendances");

            migrationBuilder.DropTable(
                name: "EmployeeFcmTokens");

            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Branches_BranchId",
                table: "Attendances",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Employees_EmployeeId",
                table: "Attendances",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
