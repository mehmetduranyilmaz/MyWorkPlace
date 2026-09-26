using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyWorkplace.Inventory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "entered_quantity",
                table: "stock_movements",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "factor",
                table: "stock_movements",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "stock_movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_code",
                table: "stock_movements",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // EN: Written by hand: movements recorded before T-030 are all order issues in the item's base unit, so they get
            //     that unit, factor 1 and the base quantity as the entered one — the history stays complete.
            // TR: Elle yazıldı: T-030'dan önce kaydedilen hareketlerin hepsi kalemin temel birimindeki sipariş çıkışlarıdır; bu yüzden o
            //     birimi, katsayı 1'i ve temel miktarı girilen miktar olarak alırlar — geçmiş eksiksiz kalır.
            migrationBuilder.Sql(
                """
                UPDATE stock_movements AS m
                SET unit_code = i.base_unit, factor = 1, entered_quantity = m.quantity
                FROM stock_items AS i
                WHERE i.id = m.stock_item_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entered_quantity",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "factor",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "note",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "unit_code",
                table: "stock_movements");
        }
    }
}
