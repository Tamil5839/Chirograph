namespace Chirograph.Domain.Documents;

public enum DocumentType
{
    ExperienceLetter,
    RelievingLetter,
}

public static class DocumentTypeExtensions
{
    public static string DisplayName(this DocumentType type) => type switch
    {
        DocumentType.ExperienceLetter => "Experience letter",
        DocumentType.RelievingLetter => "Relieving letter",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
