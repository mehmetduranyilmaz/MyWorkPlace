using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyWorkplace.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "extra_permissions",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "roles",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            // EN: Data migration (ADR-022): every user created before roles existed signed their company up, so they
            //     become Owner — otherwise they would suddenly have no permissions at all. Tests start from an empty
            //     database and would never notice; existing databases would.
            // TR: Veri göçü (ADR-022): roller gelmeden önce oluşturulan her kullanıcı firmasını kendisi kaydetmişti, bu yüzden
            //     Sahip olur — aksi halde birden hiçbir izni kalmazdı. Testler boş veritabanıyla başladığı için bunu asla fark
            //     etmezdi; mevcut veritabanları ederdi.
            migrationBuilder.Sql("UPDATE users SET roles = ARRAY['Owner'] WHERE cardinality(roles) = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extra_permissions",
                table: "users");

            migrationBuilder.DropColumn(
                name: "roles",
                table: "users");
        }
    }
}
