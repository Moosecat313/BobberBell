using System.Linq;
using BobberBell.Core.Input;

namespace BobberBell.Tests;

public class KeyCatalogTests
{
    [Fact]
    public void Catalog_HasExpectedOrderAndSize()
    {
        var names = KeyCatalog.All.Select(k => k.Name).ToArray();
        Assert.Equal(6 + 10 + 2 + 26 + 12 + 6, names.Length);
        Assert.Equal(new[] { "Space", "Enter", "Escape", "Tab", "Mouse 4", "Mouse 5", "1" }, names.Take(7));
        Assert.Equal("Right", names[^1]);
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Theory]
    [InlineData("1", 0x31)]
    [InlineData("0", 0x30)]
    [InlineData("-", 0xBD)]
    [InlineData("=", 0xBB)]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("Mouse 4", 0x05)]
    [InlineData("Mouse 5", 0x06)]
    [InlineData("Left Ctrl", 0xA2)]
    [InlineData("Left Shift", 0xA0)]
    [InlineData("Up", 0x26)]
    public void Find_ReturnsVirtualKey(string name, int vk)
    {
        Assert.Equal(vk, KeyCatalog.Find(name)!.VirtualKey);
    }

    [Theory]
    [InlineData("F13")]
    [InlineData("Delete")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_RejectsUnknownKeys(string? name)
    {
        Assert.False(KeyCatalog.IsValid(name));
    }
}
