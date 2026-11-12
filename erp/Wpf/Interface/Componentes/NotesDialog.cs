using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;

// Observações (lançamento e ficha do pedido): um único diálogo simples (padrão P3) com Cancelar e Salvar.
// Devolve o texto novo, ou null se o usuário cancelar.
public static class NotesDialog
{
    public static string? Edit(Window owner,string title,string hint,string text,string kind)
    {
        var editor=new TextBox{Text=text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalContentAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=200,Padding=new Thickness(0,8,0,8),Tag="Ex.: cliente retira às 17h; conferir a arte antes de imprimir"};
        var counter=new TextBlock{Style=(Style)owner.FindResource("Hint"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,6,0,0)};
        void Count()=>counter.Text=$"{editor.Text.Length} caracteres";editor.TextChanged+=(_,_)=>Count();Count();
        var body=new StackPanel();body.Children.Add(editor);body.Children.Add(counter);
        var dialog=new Window{Title=title,Owner=owner,Width=Math.Min(640,SystemParameters.WorkArea.Width-32),SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false};
        string? result=null;
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
        var cancel=new Button{Content="Cancelar",Style=(Style)owner.FindResource("GhostButton"),Margin=new Thickness(0,0,8,0)};cancel.Click+=(_,_)=>dialog.DialogResult=false;
        var save=new Button{Content="Salvar observação",Style=(Style)owner.FindResource("DialogPrimaryButton")};save.Click+=(_,_)=>{result=editor.Text;dialog.DialogResult=true;};
        buttons.Children.Add(cancel);buttons.Children.Add(save);
        dialog.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;result=editor.Text;dialog.DialogResult=true;}};
        dialog.Content=new DialogFrame{Kind=kind,IconName="message-circle",Title=title,Subtitle=hint+" · Ctrl+Enter salva · Esc cancela",Content=body,Footer=buttons,ScrollBody=false};
        dialog.Loaded+=(_,_)=>{editor.Focus();editor.CaretIndex=editor.Text.Length;};
        return dialog.ShowDialog()==true?result:null;
    }
}
