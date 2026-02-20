using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyCD.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileSystemItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<byte>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSystemItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileSystemItems_FileSystemItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "FileSystemItems",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 1, "Documents", (byte)1, null });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 2, "Pictures", (byte)1, null });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 3, "Music", (byte)1, null });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 4, "Work", (byte)1, 1 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 5, "Personal", (byte)1, 1 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 6, "Report.docx", (byte)0, 4 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 7, "Notes.txt", (byte)0, 4 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 8, "Resume.pdf", (byte)0, 5 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 9, "Vacation.jpg", (byte)0, 2 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 10, "Family.jpg", (byte)0, 2 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 11, "Song1.mp3", (byte)0, 3 });

            migrationBuilder.InsertData(
                table: "FileSystemItems",
                columns: new[] { "Id", "Name", "Type", "ParentId" },
                values: new object[] { 12, "Song2.mp3", (byte)0, 3 });

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemItems_ParentId",
                table: "FileSystemItems",
                column: "ParentId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileSystemItems");
        }
    }
}
