using System;
using System.Windows;
using WpfDataGrid.Enhancements.Theming;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

[CollectionDefinition("ThemeManager", DisableParallelization = true)]
public sealed class ThemeManagerCollection;

/// <summary>DataGridThemeManager 静态状态测试：RegisterTheme / ApplyTheme / 替换 / 大小写。</summary>
[Collection("ThemeManager")]
public class RegisterThemeTests : IDisposable
{
    public void Dispose() => DataGridThemeManager.ClearTheme();

    [Fact]
    public void Register_ThenApplyTheme_Succeeds()
    {
        var dict = new ResourceDictionary();
        DataGridThemeManager.RegisterTheme("翡翠绿", dict);

        Assert.Contains("翡翠绿", DataGridThemeManager.RegisteredThemeNames);
        Assert.Same(dict, DataGridThemeManager.TryGetRegisteredTheme("翡翠绿"));

        Assert.True(DataGridThemeManager.ApplyTheme("翡翠绿"));
        Assert.True(DataGridThemeManager.IsCustomThemeApplied);
        Assert.Equal("翡翠绿", DataGridThemeManager.AppliedCustomThemeName);
    }

    [Fact]
    public void NameComparison_IsCaseInsensitive()
    {
        var dict = new ResourceDictionary();
        DataGridThemeManager.RegisterTheme("GreenTheme", dict);

        Assert.True(DataGridThemeManager.ApplyTheme("greentheme"));
        Assert.Equal("greentheme", DataGridThemeManager.AppliedCustomThemeName);
        Assert.Same(dict, DataGridThemeManager.TryGetRegisteredTheme("GREENTheme"));
    }

    [Fact]
    public void UnknownTheme_ApplyReturnsFalse_AndStateUnchanged()
    {
        var dict = new ResourceDictionary();
        DataGridThemeManager.RegisterTheme("A", dict);

        Assert.False(DataGridThemeManager.ApplyTheme("不存在的主题"));
        Assert.False(DataGridThemeManager.IsCustomThemeApplied);
        Assert.Null(DataGridThemeManager.AppliedCustomThemeName);
    }

    [Fact]
    public void ReRegister_SameName_ReplacesDictionary()
    {
        var first = new ResourceDictionary();
        var second = new ResourceDictionary();

        DataGridThemeManager.RegisterTheme("T", first);
        DataGridThemeManager.RegisterTheme("T", second);

        Assert.Same(second, DataGridThemeManager.TryGetRegisteredTheme("T"));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => DataGridThemeManager.RegisterTheme("", new ResourceDictionary()));
        Assert.Throws<ArgumentNullException>(() => DataGridThemeManager.RegisterTheme("T", null!));
    }
}
