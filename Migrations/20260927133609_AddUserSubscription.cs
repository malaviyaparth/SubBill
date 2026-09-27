using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SubBill.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "BillingCycle", "Description", "IsActive", "Name", "Price" },
                values: new object[,]
                {
                    { 1, 0, "Entry-level plan", true, "Basic", 9.99m },
                    { 2, 0, "For growing teams", true, "Pro", 29.99m },
                    { 3, 2, "Full feature set", true, "Enterprise", 299.99m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
