using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MyApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { new Guid("a1111111-1111-1111-1111-111111111111"), "Permissions.Products.View" },
                    { new Guid("a1111111-1111-1111-1111-111111111112"), "Permissions.Products.Create" },
                    { new Guid("a1111111-1111-1111-1111-111111111113"), "Permissions.Products.Edit" },
                    { new Guid("a1111111-1111-1111-1111-111111111114"), "Permissions.Products.Delete" },
                    { new Guid("a2222222-2222-2222-2222-222222222221"), "Permissions.Users.View" },
                    { new Guid("a2222222-2222-2222-2222-222222222222"), "Permissions.Users.Ban" },
                    { new Guid("a3333333-3333-3333-3333-333333333331"), "Permissions.Invoices.View" },
                    { new Guid("a3333333-3333-3333-3333-333333333332"), "Permissions.Invoices.Edit" },
                    { new Guid("a3333333-3333-3333-3333-333333333333"), "Permissions.Invoices.Delete" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("a1111111-1111-1111-1111-111111111111"), new Guid("11111111-1111-1111-1111-111111111111") },
                    { new Guid("a1111111-1111-1111-1111-111111111111"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a1111111-1111-1111-1111-111111111112"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a1111111-1111-1111-1111-111111111113"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a1111111-1111-1111-1111-111111111114"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a2222222-2222-2222-2222-222222222221"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a2222222-2222-2222-2222-222222222222"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a3333333-3333-3333-3333-333333333331"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a3333333-3333-3333-3333-333333333332"), new Guid("22222222-2222-2222-2222-222222222222") },
                    { new Guid("a3333333-3333-3333-3333-333333333333"), new Guid("22222222-2222-2222-2222-222222222222") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Name",
                table: "Permissions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "Permissions");
        }
    }
}
