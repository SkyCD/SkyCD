using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCD.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogNodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastModifiedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogNodes_CatalogNodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CatalogNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CatalogNodes_Catalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogTags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogTags_Catalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogNodes_CatalogId_Kind",
                table: "CatalogNodes",
                columns: new[] { "CatalogId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogNodes_CatalogId_ParentId",
                table: "CatalogNodes",
                columns: new[] { "CatalogId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogNodes_CatalogId_ParentId_Name_Kind",
                table: "CatalogNodes",
                columns: new[] { "CatalogId", "ParentId", "Name", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogNodes_ParentId",
                table: "CatalogNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogTags_CatalogId_Name",
                table: "CatalogTags",
                columns: new[] { "CatalogId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogNodes");

            migrationBuilder.DropTable(
                name: "CatalogTags");

            migrationBuilder.DropTable(
                name: "Catalogs");
        }
    }
}
