using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyWorkplace.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);
        }
    }
}
