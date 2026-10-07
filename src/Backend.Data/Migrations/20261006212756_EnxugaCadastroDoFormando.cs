using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnxugaCadastroDoFormando : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "data_de_nascimento",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_bairro",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_cep",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_cidade",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_complemento",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_logradouro",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_numero",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "endereco_uf",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "matricula",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "nome_no_diploma",
                table: "perfis_de_formandos");

            migrationBuilder.DropColumn(
                name: "rg",
                table: "perfis_de_formandos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "data_de_nascimento",
                table: "perfis_de_formandos",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_bairro",
                table: "perfis_de_formandos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_cep",
                table: "perfis_de_formandos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_cidade",
                table: "perfis_de_formandos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_complemento",
                table: "perfis_de_formandos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_logradouro",
                table: "perfis_de_formandos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_numero",
                table: "perfis_de_formandos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_uf",
                table: "perfis_de_formandos",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "matricula",
                table: "perfis_de_formandos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nome_no_diploma",
                table: "perfis_de_formandos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rg",
                table: "perfis_de_formandos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
