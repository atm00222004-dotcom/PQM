using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PQM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class init1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old composite primary key
            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        DROP CONSTRAINT PK_DeviceProfileSyncState;");

            // Drop the existing non-identity Id column
            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        DROP COLUMN Id;");

            // Re-add Id as a proper identity primary key
            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        ADD Id INT IDENTITY(1,1) NOT NULL;");

            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        ADD CONSTRAINT PK_DeviceProfileSyncState PRIMARY KEY (Id);");

            // Preserve the "no duplicate device+profile" rule
            migrationBuilder.Sql(@"
        CREATE UNIQUE INDEX IX_DeviceProfileSyncState_DeviceId_ProfileId
        ON DeviceProfileSyncState (DeviceId, ProfileId);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceProfileSyncState_DeviceId_ProfileId",
                table: "DeviceProfileSyncState");

            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        DROP CONSTRAINT PK_DeviceProfileSyncState;");

            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        DROP COLUMN Id;");

            migrationBuilder.Sql(@"
        ALTER TABLE DeviceProfileSyncState
        ADD Id INT NOT NULL DEFAULT 0;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeviceProfileSyncState",
                table: "DeviceProfileSyncState",
                columns: new[] { "DeviceId", "ProfileId" });
        }
    }
}
