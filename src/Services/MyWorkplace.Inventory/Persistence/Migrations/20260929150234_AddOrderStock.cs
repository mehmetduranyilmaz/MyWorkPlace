using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyWorkplace.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_stock",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_stock", x => x.order_id);
                });

            // EN: Written by hand: orders issued before T-040 get their "Issued" row, so cancelling one of them later still
            //     returns its stock instead of being taken for "cancelled before issued".
            // TR: Elle yazıldı: T-040'tan önce çıkılmış siparişler "Issued" satırlarını alır; böylece bunlardan biri sonradan iptal edilirse
            //     "çıkılmadan önce iptal edildi" sanılmaz, stoğu yine geri verilir.
            migrationBuilder.Sql(
                """
                INSERT INTO order_stock (order_id, tenant_id, status, changed_at)
                SELECT order_id, MIN(tenant_id::text)::uuid, 'Issued', MIN(created_at)
                FROM stock_movements
                WHERE reason = 'Order' AND order_id IS NOT NULL
                GROUP BY order_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_stock");
        }
    }
}
