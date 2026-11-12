using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace GraficaCaixa.Wpf;

// Ctrl+F nas telas do menu: o campo marcado com SearchShortcut.IsTarget recebe o cursor (com o texto selecionado).
// O campo mostra a etiqueta "Ctrl F" à direita (modelo do TextBox em App.xaml).
public static class SearchShortcut
{
    public static readonly DependencyProperty IsTargetProperty=DependencyProperty.RegisterAttached("IsTarget",typeof(bool),typeof(SearchShortcut),new PropertyMetadata(false));
    public static bool GetIsTarget(DependencyObject element)=>(bool)element.GetValue(IsTargetProperty);
    public static void SetIsTarget(DependencyObject element,bool value)=>element.SetValue(IsTargetProperty,value);
    public static bool Focus(DependencyObject root)
    {
        if(Find(root) is not TextBox box)return false;
        box.Focus();Keyboard.Focus(box);box.SelectAll();return true;
    }
    static TextBox? Find(DependencyObject root)
    {
        if(root is TextBox{IsVisible:true,IsEnabled:true} box&&GetIsTarget(box))return box;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())if(Find(child) is TextBox found)return found;
        return null;
    }
}
