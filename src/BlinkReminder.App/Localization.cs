using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace BlinkReminder.App;

public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources = new("BlinkReminder.App.Resources.Strings", typeof(Localizer).Assembly);
    public static Localizer Current { get; } = new();
    private CultureInfo culture = CultureInfo.GetCultureInfo("pt-BR");
    public string this[string key] => Resources.GetString(key, culture) ?? key;
    public string Language => culture.Name;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(string language)
    {
        culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new("Item[]"));
    }

    public string Format(string key, params object[] arguments) => string.Format(culture, this[key], arguments);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Localizer.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
