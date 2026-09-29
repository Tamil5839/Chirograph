using Chirograph.Domain.Common;

namespace Chirograph.UnitTests.Domain;

public class TextTests
{
    [Fact]
    public void Normalize_trims_and_collapses_whitespace()
    {
        Assert.Equal("Anita Desai", Text.Normalize("  Anita \t\n  Desai  "));
    }

    [Fact]
    public void Normalize_strips_bidirectional_overrides_that_could_disguise_text()
    {
        // U+202E (right-to-left override) would render "Engineer" reversed after it.
        Assert.Equal("Senior Engineer", Text.Normalize("Senior ‮Engineer‬"));
        Assert.Equal("abc", Text.Normalize("a⁦b⁩c‏"));
    }

    [Fact]
    public void Normalize_strips_control_characters()
    {
        Assert.Equal("AnitaDesai", Text.Normalize("Anita\u0000\u0007Desai"));
    }

    [Fact]
    public void Normalize_keeps_joiners_needed_by_Indic_scripts()
    {
        const string name = "க்‍ஷ"; // Tamil with a zero-width joiner

        Assert.Equal(name, Text.Normalize(name));
    }

    [Fact]
    public void Required_rejects_blank_and_overlong_values()
    {
        Assert.Equal("validation.required", Assert.Throws<DomainException>(() => Text.Required(" ‮ ", 10, "name")).Code);
        Assert.Equal("validation.too_long", Assert.Throws<DomainException>(() => Text.Required("12345678901", 10, "name")).Code);
        Assert.Equal("1234567890", Text.Required(" 1234567890 ", 10, "name"));
    }

    [Fact]
    public void Optional_returns_null_for_blank_and_truncates_long_values()
    {
        Assert.Null(Text.Optional("   ", 10));
        Assert.Null(Text.Optional(null, 10));
        Assert.Equal("Infosys Ba", Text.Optional("Infosys Background Checks", 10));
    }
}
