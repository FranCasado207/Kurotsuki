using Avalonia.Markup.Xaml;
using Kurotsuki.Core.I18n;

namespace Kurotsuki.App.Services;

/// <summary>XAML text from the message bundles: <c>Text="{svc:T 'next.save'}"</c>. Resolved when the view loads (the language needs a restart).</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Messages.Get(Key);
}
