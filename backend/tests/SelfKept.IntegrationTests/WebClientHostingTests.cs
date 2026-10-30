using FluentAssertions;
using SelfKept.Api.Extensions;
using Xunit;

namespace SelfKept.IntegrationTests;

public class WebClientHostingTests
{
  [Theory]
  [InlineData("assets/shaders/ink_sparkle.frag")]
  [InlineData("canvaskit/canvaskit.symbols")]
  [InlineData("main.dart.js.map")]
  [InlineData("assets/AssetManifest.bin")]
  [InlineData("assets/assets/fonts/Inter.ttf")]
  [InlineData("canvaskit/canvaskit.wasm")]
  public void ContentTypes_ServeEveryFileAFlutterWebBuildShips(string path)
  {
    WebClientHosting.ContentTypes.TryGetContentType(path, out var type).Should().BeTrue(path);
    type.Should().NotBeNullOrEmpty();
  }
}
