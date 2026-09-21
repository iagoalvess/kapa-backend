using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaMeiosDeRecebimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "meio_escolhido",
                table: "informes_de_pagamento",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "tipo_de_chave",
                table: "contas_de_recebimento",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "nome_do_titular",
                table: "contas_de_recebimento",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "cidade",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "chave",
                table: "contas_de_recebimento",
                type: "character varying(77)",
                maxLength: 77,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(77)",
                oldMaxLength: 77);

            migrationBuilder.AddColumn<string>(
                name: "agencia",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "banco",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "conta",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dinheiro_com",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dinheiro_onde",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_de_conta",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "titular_da_conta",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "meio_escolhido",
                table: "informes_de_pagamento");

            migrationBuilder.DropColumn(
                name: "agencia",
                table: "contas_de_recebimento");

            migrationBuilder.DropColumn(
                name: "banco",
                table: "contas_de_recebimento");

            migrationBuilder.DropColumn(
                name: "conta",
                table: "contas_de_recebimento");

            migrationBuilder.DropColumn(
                name: "dinheiro_com",
                table: "contas_de_recebimento");

            migrationBuilder.DropColumn(
                name: "dinheiro_onde",
                table: "contas_de_recebimento");

            migrationBuilder.DropColumn(
                name: "tipo_de_conta",
                table: "contas_de_recebimento");

            migrationBuilder.DropColumn(
                name: "titular_da_conta",
                table: "contas_de_recebimento");

            migrationBuilder.AlterColumn<string>(
                name: "tipo_de_chave",
                table: "contas_de_recebimento",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "nome_do_titular",
                table: "contas_de_recebimento",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cidade",
                table: "contas_de_recebimento",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "chave",
                table: "contas_de_recebimento",
                type: "character varying(77)",
                maxLength: 77,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(77)",
                oldMaxLength: 77,
                oldNullable: true);
        }
    }
}
