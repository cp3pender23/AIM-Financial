using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIM.Web.Migrations
{
    /// <summary>
    /// Role & access overhaul: add audit/lifecycle fields to AspNetUsers.
    ///
    /// Notes:
    /// - Scaffolder initially wanted to rewrite several timestamptz columns on
    ///   bsa_reports/audit_log to timestamp-without-tz (a side effect of the
    ///   legacy-timestamp-behavior switch flipped on at startup). Those
    ///   alterations are dropped here — the app has been running with tz-aware
    ///   columns just fine and converting them in place would risk data loss.
    /// - is_active defaults to true so existing seeded users (admin, analyst,
    ///   viewer, colin) don't become locked out the moment the migration runs.
    /// - created_at on existing rows is set to now() via RawSql so every
    ///   pre-migration user gets a sensible registration timestamp instead of
    ///   0001-01-01.
    /// </summary>
    public partial class AddUserAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "AspNetUsers",
                type: "timestamp without time zone",
                nullable: false,
                defaultValueSql: "(now() at time zone 'utc')");

            migrationBuilder.AddColumn<string>(
                name: "invited_by_user_id",
                table: "AspNetUsers",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at",
                table: "AspNetUsers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_user_review_at",
                table: "AspNetUsers",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "created_at", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "invited_by_user_id", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "is_active", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "last_login_at", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "last_user_review_at", table: "AspNetUsers");
        }
    }
}
