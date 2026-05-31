using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HybridSlicer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResinPrinterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BedIndex",
                table: "PrintJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentJobId",
                table: "PrintJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SupportInfillDensityPct",
                table: "PrintJobs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportInfillPattern",
                table: "PrintJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AntiAliasing",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "BackBedEdgeOffsetMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "BedCount",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "BedPositionXMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BedPositionYMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Beds",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<double>(
                name: "BottomLiftDistanceMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BottomLiftSpeedMmPerMin",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BuildOffsetXMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BuildOffsetYMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "CncAxes",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "DefaultBottomExposureMs",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "DefaultBottomLayerCount",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultLayerHeightMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DefaultNormalExposureMs",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ExportFormat",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExtruderAxes",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "FrontBedEdgeOffsetMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LiftDistanceMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LiftSpeedMmPerMin",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LightOffDelayMs",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "MirrorX",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MirrorY",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MotionAssignmentEnabled",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotionAssignmentJson",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NozzleXOffsets",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "Orientation",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OriginMode",
                table: "MachineProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "BedCenter");

            migrationBuilder.AddColumn<double>(
                name: "OriginXMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "OriginYMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PixelPitchUm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ResolutionX",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResolutionY",
                table: "MachineProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "RestTimeAfterLiftMs",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RestTimeAfterRetractMs",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RetractDistanceMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RetractSpeedMmPerMin",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TravelXMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TravelYMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TravelZMm",
                table: "MachineProfiles",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BedIndex",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "ParentJobId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "SupportInfillDensityPct",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "SupportInfillPattern",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "AntiAliasing",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BackBedEdgeOffsetMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BedCount",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BedPositionXMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BedPositionYMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "Beds",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BottomLiftDistanceMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BottomLiftSpeedMmPerMin",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BuildOffsetXMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "BuildOffsetYMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "CncAxes",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultBottomExposureMs",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultBottomLayerCount",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultLayerHeightMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "DefaultNormalExposureMs",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "ExportFormat",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "ExtruderAxes",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "FrontBedEdgeOffsetMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "LiftDistanceMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "LiftSpeedMmPerMin",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "LightOffDelayMs",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "MirrorX",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "MirrorY",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "MotionAssignmentEnabled",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "MotionAssignmentJson",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "NozzleXOffsets",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "Orientation",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "OriginMode",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "OriginXMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "OriginYMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "PixelPitchUm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "ResolutionX",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "ResolutionY",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "RestTimeAfterLiftMs",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "RestTimeAfterRetractMs",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "RetractDistanceMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "RetractSpeedMmPerMin",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "TravelXMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "TravelYMm",
                table: "MachineProfiles");

            migrationBuilder.DropColumn(
                name: "TravelZMm",
                table: "MachineProfiles");
        }
    }
}
