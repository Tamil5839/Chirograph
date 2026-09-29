using Chirograph.Domain.Common;

namespace Chirograph.Domain.Documents;

/// <summary>
/// The structured fields an issuer records for a letter. This is the complete list of personal data Chirograph
/// keeps about an employee; nothing else is extracted from the PDF.
/// </summary>
public sealed record DocumentDetails
{
    public const int EmployeeNameMaxLength = 200;
    public const int DesignationMaxLength = 200;
    private static readonly DateOnly EarliestPlausibleDate = new(1900, 1, 1);

    private DocumentDetails(
        DocumentType type,
        string employeeName,
        EmailAddress employeeEmail,
        string designation,
        DateOnly employmentStart,
        DateOnly employmentEnd)
    {
        Type = type;
        EmployeeName = employeeName;
        EmployeeEmail = employeeEmail;
        Designation = designation;
        EmploymentStart = employmentStart;
        EmploymentEnd = employmentEnd;
    }

    public DocumentType Type { get; }

    public string EmployeeName { get; }

    public EmailAddress EmployeeEmail { get; }

    public string Designation { get; }

    public DateOnly EmploymentStart { get; }

    public DateOnly EmploymentEnd { get; }

    public static DocumentDetails Create(
        DocumentType type,
        string? employeeName,
        EmailAddress employeeEmail,
        string? designation,
        DateOnly employmentStart,
        DateOnly employmentEnd)
    {
        ArgumentNullException.ThrowIfNull(employeeEmail);
        if (!Enum.IsDefined(type))
            throw new DomainException("document.invalid_type", "Choose a document type.");

        var name = Text.Required(employeeName, EmployeeNameMaxLength, "employee name");
        var role = Text.Required(designation, DesignationMaxLength, "role or designation");

        if (employmentStart < EarliestPlausibleDate)
            throw new DomainException("document.invalid_dates", "Enter a valid employment start date.");
        if (employmentEnd < employmentStart)
        {
            throw new DomainException(
                "document.invalid_dates",
                "The employment end date cannot be before the start date.");
        }

        return new DocumentDetails(type, name, employeeEmail, role, employmentStart, employmentEnd);
    }
}
