using AskMyData.Domain.Conversations;
using FluentAssertions;

namespace AskMyData.UnitTests.Domain;

public sealed class ConversationIdTests
{
    [Theory]
    [InlineData("3f2c9a8e4b7d4c1e9a0b6d5e4f3a2b1c")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("abcdefabcdefabcdefabcdefabcdef01")]
    public void TryParse_accepts_32_lowercase_hex_characters(string raw)
    {
        ConversationId.TryParse(raw, out var id).Should().BeTrue();
        id.Value.Should().Be(raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3F2C9A8E4B7D4C1E9A0B6D5E4F3A2B1C")]       // uppercase
    [InlineData("3f2c9a8e4b7d4c1e9a0b6d5e4f3a2b1")]        // 31 chars
    [InlineData("3f2c9a8e4b7d4c1e9a0b6d5e4f3a2b1c0")]      // 33 chars
    [InlineData("3f2c9a8e4b7d4c1e9a0b6d5e4f3a2b1g")]       // non-hex char
    [InlineData("3f2c9a8e-4b7d-4c1e-9a0b-6d5e4f3a2b1c")]   // GUID "D" format
    [InlineData(" 3f2c9a8e4b7d4c1e9a0b6d5e4f3a2b1")]       // leading space
    public void TryParse_rejects_other_input(string? raw)
    {
        ConversationId.TryParse(raw, out var id).Should().BeFalse();
        id.Should().Be(default(ConversationId));
    }

    [Fact]
    public void New_produces_parsable_distinct_ids()
    {
        var first = ConversationId.New();
        var second = ConversationId.New();

        ConversationId.TryParse(first.Value, out _).Should().BeTrue();
        first.Should().NotBe(second);
    }
}
