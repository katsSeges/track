using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Settings;

namespace Track.Core.Tests.Display;

public sealed class AlertStateStoreTests
{
    private const string Path = @"C:\appdata\Track\alerts.json";

    [Fact]
    public void Missing_state_starts_with_no_fired_alerts()
    {
        var result = new AlertStateStore(new MockFileSystem(), Path).Load();

        Assert.True(result.Success);
        Assert.Empty(result.FiredKeys);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Save_and_load_round_trip_sorted_fired_keys()
    {
        var fs = new MockFileSystem();
        var store = new AlertStateStore(fs, Path);
        var keys = new HashSet<string>(["z", "a"], StringComparer.Ordinal);

        Assert.True(store.Save(keys).Success);

        var loaded = store.Load();
        Assert.True(loaded.Success);
        Assert.Equal(keys, loaded.FiredKeys);
        Assert.Equal("[\"a\",\"z\"]", fs.File.ReadAllText(Path));
        Assert.False(fs.File.Exists(Path + ".tmp"));
    }

    [Fact]
    public void Malformed_or_null_state_is_reported_as_unavailable()
    {
        foreach (var content in new[] { "{", "null" })
        {
            var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [Path] = new(content) });

            var result = new AlertStateStore(fs, Path).Load();

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Disk_read_trouble_is_reported(Type exceptionType)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(Path).Returns(true);
        fs.File.ReadAllText(Path).Throws((Exception)Activator.CreateInstance(exceptionType)!);

        var result = new AlertStateStore(fs, Path).Load();

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Disk_write_trouble_returns_failure(Type exceptionType)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.Path.GetDirectoryName(Path).Returns(@"C:\appdata\Track");
        fs.File.When(file => file.WriteAllText(Arg.Any<string>(), Arg.Any<string>()))
            .Throw((Exception)Activator.CreateInstance(exceptionType)!);

        var result = new AlertStateStore(fs, Path).Save(new HashSet<string>());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
