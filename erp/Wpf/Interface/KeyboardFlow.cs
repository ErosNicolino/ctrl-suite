using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace GraficaCaixa.Wpf;

// Formulários de digitação rápida: Enter avança para o próximo campo; Ctrl+Enter ou Ctrl+S salvam.
public static class KeyboardFlow
{
    public static void Attach(Window window,Action save)
    {
        window.PreviewKeyDown+=(_,e)=>
        {
            if(e.Handled)return;
            var ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0;
            if(ctrl&&(e.Key==Key.Enter||e.Key==Key.S)){e.Handled=true;save();return;}
            if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.None&&e.OriginalSource is TextBox{AcceptsReturn:false} box){e.Handled=true;box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));}
        };
    }
}
