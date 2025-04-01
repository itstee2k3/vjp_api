using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vjp_api.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUrlToGroupMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "GroupMessages",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "GroupMessages",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "GroupMessages");
        }
    }
}
