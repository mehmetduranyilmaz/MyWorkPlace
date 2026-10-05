using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyWorkplace.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectSigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "private_key",
                table: "signing_keys",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activates_at",
                table: "signing_keys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<byte[]>(
                name: "encrypted_private_key",
                table: "signing_keys",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "public_key",
                table: "signing_keys",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            // EN: Written by hand: a key created before ADR-032 has signed since it was created. Its public key and the
            //     wiping of its plaintext private key need C# and happen at start-up (SigningKeyProvider); the plaintext
            //     column itself is dropped in a later release (T-073, expand / contract).
            // TR: Elle yazıldı: ADR-032'den önce üretilen bir anahtar, üretildiğinden beri imzalıyor. Açık anahtarı ve düz özel anahtarının
            //     silinmesi C# gerektirir ve açılışta olur (SigningKeyProvider); düz metin sütununun kendisi sonraki bir sürümde kaldırılır
            //     (T-073, genişlet / daralt).
            migrationBuilder.Sql("UPDATE signing_keys SET activates_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activates_at",
                table: "signing_keys");

            migrationBuilder.DropColumn(
                name: "encrypted_private_key",
                table: "signing_keys");

            migrationBuilder.DropColumn(
                name: "public_key",
                table: "signing_keys");

            migrationBuilder.AlterColumn<byte[]>(
                name: "private_key",
                table: "signing_keys",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}
