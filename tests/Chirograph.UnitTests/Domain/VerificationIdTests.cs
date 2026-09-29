using Chirograph.Domain.Common;
using Chirograph.Domain.Documents;

namespace Chirograph.UnitTests.Domain;

public class VerificationIdTests
{
    [Fact]
    public void New_ids_are_25_characters_from_the_Crockford_alphabet()
    {
        var id = VerificationId.New();

        Assert.Equal(VerificationId.Length, id.Value.Length);
        Assert.All(id.Value, c => Assert.Contains(c, VerificationId.Alphabet));
    }

    [Fact]
    public void New_ids_do_not_repeat()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => VerificationId.New().Value).ToHashSet();

        Assert.Equal(20_000, ids.Count);
    }

    [Fact]
    public void New_ids_draw_on_the_whole_alphabet_in_every_position()
    {
        var ids = Enumerable.Range(0, 4_000).Select(_ => VerificationId.New().Value).ToList();

        for (var position = 0; position < VerificationId.Length; position++)
        {
            var symbolsSeen = ids.Select(id => id[position]).Distinct().Count();
            Assert.Equal(VerificationId.Alphabet.Length, symbolsSeen);
        }
    }

    [Fact]
    public void Display_form_groups_characters_and_parses_back()
    {
        var id = VerificationId.New();

        var display = id.ToDisplayString();

        Assert.Matches("^[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$", display);
        Assert.Equal(id, VerificationId.Parse(display));
    }

    [Theory]
    [InlineData("7k3m9-qxab2-c4def-gh8jk-mnp2q")]
    [InlineData("7K3M9 QXAB2 C4DEF GH8JK MNP2Q")]
    [InlineData(" 7K3M9QXAB2C4DEFGH8JKMNP2Q ")]
    public void Parse_accepts_what_a_person_might_type(string typed)
    {
        Assert.Equal("7K3M9QXAB2C4DEFGH8JKMNP2Q", VerificationId.Parse(typed).Value);
    }

    [Fact]
    public void Parse_reads_confusable_letters_as_the_digits_they_resemble()
    {
        Assert.Equal("0000011111000001111100000", VerificationId.Parse("OOOOO-IIIII-ooooo-lllll-00000").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7K3M9-QXAB2-C4DEF-GH8JK-MNP2")]
    [InlineData("7K3M9-QXAB2-C4DEF-GH8JK-MNP2QQ")]
    [InlineData("7K3M9-QXAB2-C4DEF-GH8JK-MNP2U")]
    [InlineData("7K3M9-QXAB2-C4DEF-GH8JK-MNP2*")]
    [InlineData("7K3M9-QXAB2-C4DEF-GH8JK-MNP2ı")]
    [InlineData("７K3M9-QXAB2-C4DEF-GH8JK-MNP2Q")]
    [InlineData("7K3M9-QXAB2-C4DEF-GH8JK-MNP2Q---------------------------------------------")]
    public void TryParse_rejects_malformed_input(string? input)
    {
        Assert.False(VerificationId.TryParse(input, out _));
        Assert.Throws<DomainException>(() => VerificationId.Parse(input));
    }
}
