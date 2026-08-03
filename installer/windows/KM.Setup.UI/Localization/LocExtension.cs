// SPDX-License-Identifier: GPL-3.0-only

using System.Windows.Markup;

namespace KM.Setup.UI.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return LocalizationService.Current[key];
    }
}
