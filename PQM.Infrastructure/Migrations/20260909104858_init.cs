using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PQM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceSyncHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProfilesRead = table.Column<int>(type: "int", nullable: true),
                    RowsWritten = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSyncHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceSyncSchedule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ScheduledTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    RepeatMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunStatus = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSyncSchedule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeterType",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PORT = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsumerNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClientAddress = table.Column<int>(type: "int", nullable: true),
                    ServerAddress = table.Column<int>(type: "int", nullable: true),
                    AuthenticationTypeId = table.Column<int>(type: "int", nullable: true),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timeout = table.Column<int>(type: "int", nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MeterTypeId = table.Column<int>(type: "int", nullable: true),
                    DeviceSyncScheduleId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_DeviceSyncSchedule_DeviceSyncScheduleId",
                        column: x => x.DeviceSyncScheduleId,
                        principalTable: "DeviceSyncSchedule",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Devices_MeterType_MeterTypeId",
                        column: x => x.MeterTypeId,
                        principalTable: "MeterType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    ProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObisCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MeterTypeId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.ProfileId);
                    table.ForeignKey(
                        name: "FK_Profiles_MeterType_MeterTypeId",
                        column: x => x.MeterTypeId,
                        principalTable: "MeterType",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeviceProfileSyncState",
                columns: table => new
                {
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    LastReadTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReadEntryIndex = table.Column<int>(type: "int", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceProfileSyncState", x => new { x.DeviceId, x.ProfileId });
                    table.ForeignKey(
                        name: "FK_DeviceProfileSyncState_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceProfileSyncState_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "ProfileId");
                });

            migrationBuilder.CreateTable(
                name: "Parameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObisCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ObjectType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttributeIndex = table.Column<int>(type: "int", nullable: true),
                    IsHistorical = table.Column<bool>(type: "bit", nullable: false),
                    IsVisible = table.Column<bool>(type: "bit", nullable: false),
                    Scaler = table.Column<int>(type: "int", nullable: true),
                    UnitCode = table.Column<int>(type: "int", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AggregationType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    MeterTypeId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parameters_MeterType_MeterTypeId",
                        column: x => x.MeterTypeId,
                        principalTable: "MeterType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Parameters_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "ProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    ReadTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntryTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingSessions_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingSessions_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "ProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    ParameterId = table.Column<int>(type: "int", nullable: false),
                    EventTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EventCode = table.Column<int>(type: "int", nullable: false),
                    RawClock = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReadTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceEvent_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceEvent_Parameters_ParameterId",
                        column: x => x.ParameterId,
                        principalTable: "Parameters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceLatestReadings",
                columns: table => new
                {
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    ParameterId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceLatestReadings", x => new { x.DeviceId, x.ParameterId });
                    table.ForeignKey(
                        name: "FK_DeviceLatestReadings_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceLatestReadings_Parameters_ParameterId",
                        column: x => x.ParameterId,
                        principalTable: "Parameters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingValues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<long>(type: "bigint", nullable: true),
                    ParameterId = table.Column<int>(type: "int", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValueNumeric = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingValues_Parameters_ParameterId",
                        column: x => x.ParameterId,
                        principalTable: "Parameters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReadingValues_ReadingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ReadingSessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvent_DeviceId",
                table: "DeviceEvent",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvent_ParameterId",
                table: "DeviceEvent",
                column: "ParameterId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceLatestReadings_ParameterId",
                table: "DeviceLatestReadings",
                column: "ParameterId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceProfileSyncState_ProfileId",
                table: "DeviceProfileSyncState",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceSyncScheduleId",
                table: "Devices",
                column: "DeviceSyncScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_MeterTypeId",
                table: "Devices",
                column: "MeterTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Parameters_MeterTypeId",
                table: "Parameters",
                column: "MeterTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Parameters_ProfileId",
                table: "Parameters",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_MeterTypeId",
                table: "Profiles",
                column: "MeterTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_Device_Profile_Timestamp",
                table: "ReadingSessions",
                columns: new[] { "DeviceId", "ProfileId", "EntryTimestampUtc" },
                unique: true,
                filter: "[EntryTimestampUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_ProfileId",
                table: "ReadingSessions",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingValues_ParameterId",
                table: "ReadingValues",
                column: "ParameterId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingValues_SessionId",
                table: "ReadingValues",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceEvent");

            migrationBuilder.DropTable(
                name: "DeviceLatestReadings");

            migrationBuilder.DropTable(
                name: "DeviceProfileSyncState");

            migrationBuilder.DropTable(
                name: "DeviceSyncHistory");

            migrationBuilder.DropTable(
                name: "ReadingValues");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "Parameters");

            migrationBuilder.DropTable(
                name: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "DeviceSyncSchedule");

            migrationBuilder.DropTable(
                name: "MeterType");
        }
    }
}
