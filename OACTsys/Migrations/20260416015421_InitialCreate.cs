using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OACTsys.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LastLogin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FieldName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    AcceptedFileTypes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxFileSize = table.Column<int>(type: "integer", nullable: false),
                    HelperText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MinLimit = table.Column<int>(type: "integer", nullable: true),
                    MaxLimit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentFields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GCashConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GCashNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QrCodePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PaymentDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GCashConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    StudentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StudentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Program = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentYearLevel = table.Column<int>(type: "integer", nullable: false),
                    CurrentSemester = table.Column<int>(type: "integer", nullable: false),
                    EnrollmentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EnrollmentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "NOW()"),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    TokenUsed = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "False"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    HasAccount = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PasswordHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.StudentId);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    SubjectId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Program = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CourseCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DescriptiveTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LectureHours = table.Column<int>(type: "integer", nullable: false),
                    LaboratoryHours = table.Column<int>(type: "integer", nullable: false),
                    Units = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    YearLevel = table.Column<int>(type: "integer", nullable: false),
                    Semester = table.Column<int>(type: "integer", nullable: false),
                    TotalHours = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.SubjectId);
                });

            migrationBuilder.CreateTable(
                name: "TuitionFees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Program = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StudentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    YearLevel = table.Column<int>(type: "integer", nullable: false),
                    Semester = table.Column<int>(type: "integer", nullable: false),
                    TuitionFees = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Miscellaneous = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Laboratory = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    HasDiscount = table.Column<bool>(type: "boolean", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    FinalTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DownPayment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OtherFees = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OtherFeesDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TotalPaymentUponEnrollment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NumberOfMonths = table.Column<int>(type: "integer", nullable: false),
                    PrelimPayment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MidtermPayment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SemiFinalPayment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    FinalPayment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Requirements = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TuitionFees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdminId = table.Column<int>(type: "integer", nullable: false),
                    PermissionName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminPermissions_Admins_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentFieldData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    EnrollmentFieldId = table.Column<int>(type: "integer", nullable: false),
                    FieldValue = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentFieldData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnrollmentFieldData_EnrollmentFields_EnrollmentFieldId",
                        column: x => x.EnrollmentFieldId,
                        principalTable: "EnrollmentFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EnrollmentFieldData_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentForms",
                columns: table => new
                {
                    EnrollmentFormId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    GradeSchool = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GradeSchoolAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    JuniorHighSchool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JuniorHighSchoolAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeniorHighSchool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SeniorHighSchoolAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SeniorHighDates = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PreviousCollege = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreviousCourse = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    YearsAttended = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Form138Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GoodMoralPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Form137Path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PSABirthCertPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IDPhotoPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TranscriptOfRecordsPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    HonorableDismissalPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    TermsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    AdminRemarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentForms", x => x.EnrollmentFormId);
                    table.ForeignKey(
                        name: "FK_EnrollmentForms_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    PaymentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProofOfPaymentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PaymentLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    VerifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_Payments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Enrollment",
                columns: table => new
                {
                    EnrollmentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    YearLevel = table.Column<int>(type: "integer", nullable: false),
                    Semester = table.Column<int>(type: "integer", nullable: false),
                    AcademicYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EnrolledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PrelimGrade = table.Column<decimal>(type: "numeric", nullable: true),
                    MidtermGrade = table.Column<decimal>(type: "numeric", nullable: true),
                    SemiFinalGrade = table.Column<decimal>(type: "numeric", nullable: true),
                    FinalGrade = table.Column<decimal>(type: "numeric", nullable: true),
                    FinalRating = table.Column<decimal>(type: "numeric", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollment", x => x.EnrollmentId);
                    table.ForeignKey(
                        name: "FK_Enrollment_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Enrollment_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubjectEnrollments",
                columns: table => new
                {
                    SubjectEnrollmentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    AcademicYear = table.Column<int>(type: "integer", nullable: false),
                    Semester = table.Column<int>(type: "integer", nullable: false),
                    YearLevel = table.Column<int>(type: "integer", nullable: false),
                    EnrolledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PrelimGrade = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    MidtermGrade = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    FinalGrade = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectEnrollments", x => x.SubjectEnrollmentId);
                    table.ForeignKey(
                        name: "FK_SubjectEnrollments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubjectEnrollments_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "SubjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminPermissions_AdminId_PermissionName",
                table: "AdminPermissions",
                columns: new[] { "AdminId", "PermissionName" });

            migrationBuilder.CreateIndex(
                name: "IX_Admins_Email",
                table: "Admins",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admins_Username",
                table: "Admins",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enrollment_StudentId",
                table: "Enrollment",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollment_SubjectId",
                table: "Enrollment",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentFieldData_EnrollmentFieldId",
                table: "EnrollmentFieldData",
                column: "EnrollmentFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentFieldData_StudentId",
                table: "EnrollmentFieldData",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentForms_StudentId",
                table: "EnrollmentForms",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StudentId_Status",
                table: "Payments",
                columns: new[] { "StudentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_Email",
                table: "Students",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Students_StudentNumber",
                table: "Students",
                column: "StudentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectEnrollments_StudentId",
                table: "SubjectEnrollments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectEnrollments_SubjectId",
                table: "SubjectEnrollments",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminPermissions");

            migrationBuilder.DropTable(
                name: "Enrollment");

            migrationBuilder.DropTable(
                name: "EnrollmentFieldData");

            migrationBuilder.DropTable(
                name: "EnrollmentForms");

            migrationBuilder.DropTable(
                name: "GCashConfigs");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "SubjectEnrollments");

            migrationBuilder.DropTable(
                name: "TuitionFees");

            migrationBuilder.DropTable(
                name: "Admins");

            migrationBuilder.DropTable(
                name: "EnrollmentFields");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.DropTable(
                name: "Subjects");
        }
    }
}
