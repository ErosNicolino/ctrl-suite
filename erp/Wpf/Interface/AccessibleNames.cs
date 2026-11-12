using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
namespace GraficaCaixa.Wpf;

// Nome para leitor de tela em todo botão (B6): o WPF só usa o conteúdo quando ele é texto simples, então botões com ícone + texto
// e botões só com ícone ficavam sem nome. Aqui o nome passa a ser o texto visível do botão (acompanha mudanças) ou, se for só
// ícone, o texto de ajuda (ToolTip). Quem já define AutomationProperties.Name não é alterado.
public static class AccessibleNames
{
    // Gatilho: o primeiro desenho do botão (SizeChanged). O "Loaded" não serve: o WPF só o dispara para quem tem tratador próprio.
    public static void Register()=>EventManager.RegisterClassHandler(typeof(ButtonBase),FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler((sender,_)=>Apply((ButtonBase)sender)));
    static void Apply(ButtonBase button)
    {
        // Texto com letras ou números já serve de nome; símbolo solto ("×", "+") conta como ícone.
        if(button.Content is string content&&content.Any(char.IsLetterOrDigit)||button.ReadLocalValue(AutomationProperties.NameProperty)!=DependencyProperty.UnsetValue||BindingOperations.IsDataBound(button,AutomationProperties.NameProperty))return;
        var tip=button.ToolTip as string;
        if(FirstText(button.Content as DependencyObject) is TextBlock text)
            BindingOperations.SetBinding(button,AutomationProperties.NameProperty,new Binding(nameof(TextBlock.Text)){Source=text,Mode=BindingMode.OneWay});
        else if(!string.IsNullOrWhiteSpace(tip))
            button.SetValue(AutomationProperties.NameProperty,tip.Split('\n')[0].Trim());
        if(!string.IsNullOrWhiteSpace(tip)&&button.ReadLocalValue(AutomationProperties.HelpTextProperty)==DependencyProperty.UnsetValue)button.SetValue(AutomationProperties.HelpTextProperty,tip);
    }
    static TextBlock? FirstText(DependencyObject? root)
    {
        if(root==null)return null;
        // Ignora o selo da tecla de atalho ("Ctrl K", "F2", "N"): o nome é o rótulo, a tecla fica no texto de ajuda.
        if(root is TextBlock text&&!string.IsNullOrWhiteSpace(text.Text)&&text.Visibility==Visibility.Visible&&!IsShortcut(text.Text))return text;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())if(FirstText(child) is TextBlock found)return found;
        return null;
    }
    static bool IsShortcut(string text)=>System.Text.RegularExpressions.Regex.IsMatch(text.Trim(),@"^((Ctrl|Alt|Shift)\s?\+?\s?)*([A-Z]|F\d{1,2}|Enter|Esc|Delete)$");
}
