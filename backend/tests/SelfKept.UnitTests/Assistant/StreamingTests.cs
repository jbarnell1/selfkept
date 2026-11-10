using System.Text;
using FluentAssertions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Assistant;
using SelfKept.Infrastructure.Services.Interpret.Ai;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

public class StreamingTests
{
  [Fact]
  public void Push_PiecesAcrossSentences_ReturnsWholeSentences()
  {
    var buffer = new SentenceBuffer();

    buffer.Push("You slept 7.5 h").Should().BeEmpty();
    buffer.Push("ours. That is ").Should().Equal("You slept 7.5 hours.");
    buffer.Push("good, e.g. for recovery. Next").Should().Equal("That is good, e.g. for recovery.");
    buffer.Flush().Should().Be("Next");
  }

  [Fact]
  public void Push_LineBreak_EndsASentence()
  {
    new SentenceBuffer().Push("First line\nSecond").Should().Equal("First line");
  }

  [Fact]
  public async Task ReadStreamAsync_OpenAiChunks_YieldsContentWithoutThinking()
  {
    var body = string.Join("\n",
      "data: {\"choices\":[{\"delta\":{\"content\":\"<thi\"}}]}",
      "data: {\"choices\":[{\"delta\":{\"content\":\"nk>secret</think>Hello\"}}]}",
      ": keep-alive",
      "data: {\"choices\":[{\"delta\":{\"content\":\" there.\"}}]}",
      "data: [DONE]",
      "data: {\"choices\":[{\"delta\":{\"content\":\"ignored\"}}]}");
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));

    var pieces = new List<string>();
    await foreach (var piece in OpenAiCompatibleChatModel.ReadStreamAsync(stream)) pieces.Add(piece);

    string.Concat(pieces).Should().Be("Hello there.");
  }

  [Fact]
  public void Fold_SeveralTurns_WritesATranscript()
  {
    var (system, user) = ChatTurn.Fold([
      new ChatTurn(ChatTurn.System, "rules"),
      new ChatTurn(ChatTurn.User, "hi"),
      new ChatTurn(ChatTurn.Assistant, "hello"),
      new ChatTurn(ChatTurn.User, "how was my sleep"),
    ]);

    system.Should().Be("rules");
    user.Should().Be("User: hi\nAssistant: hello\nUser: how was my sleep");
  }
}
