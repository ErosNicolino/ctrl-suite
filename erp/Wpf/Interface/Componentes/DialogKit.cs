using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// Peças repetidas dos diálogos montados em código (UX 2.0): linha rótulo/valor, campo com erro junto, rodapé.
public static class DialogKit
{
    static Style S(string key)=>(Style)Application.Current.FindResource(key);
    public static Grid Line(string label,TextBlock value,bool strong=false,double top=4)
    {
        var grid=new Grid{Margin=new Thickness(0,top,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var name=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center};if(strong)name.FontWeight=FontWeights.SemiBold;grid.Children.Add(name);
        value.FontWeight=FontWeights.SemiBold;if(strong)value.FontSize=18;value.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(value,1);grid.Children.Add(value);return grid;
    }
    public static Grid Line(string label,string value,bool strong=false,string? brushKey=null,double top=4)
    {
        var text=new TextBlock{Text=value};if(brushKey!=null)text.SetResourceReference(TextBlock.ForegroundProperty,brushKey);return Line(label,text,strong,top);
    }
    // Campo: rótulo + controle + mensagem de erro (oculta até ser usada).
    public sealed record Field(StackPanel Panel,TextBlock Error);
    public static Field LabeledField(string label,FrameworkElement control,string? hint=null)
    {
        var panel=new StackPanel{Margin=new Thickness(0,0,0,12)};panel.Children.Add(new TextBlock{Text=label,Style=S("FieldLabel")});panel.Children.Add(control);
        if(hint!=null)panel.Children.Add(new TextBlock{Text=hint,Style=S("Hint"),Margin=new Thickness(0,4,0,0)});
        var error=new TextBlock{Style=S("FieldError"),Visibility=Visibility.Collapsed};panel.Children.Add(error);return new(panel,error);
    }
    public static void ShowError(Field field,Control control,string message)
    {
        field.Error.Text=message;field.Error.Visibility=Visibility.Visible;Ui.SetHasError(control,true);control.Focus();if(control is TextBox box)box.SelectAll();
        void Clear(){field.Error.Visibility=Visibility.Collapsed;Ui.SetHasError(control,false);}
        if(control is TextBox t){void Changed(object? s,TextChangedEventArgs e){t.TextChanged-=Changed;Clear();}t.TextChanged+=Changed;}
        else if(control is DatePicker d){void Picked(object? s,SelectionChangedEventArgs e){d.SelectedDateChanged-=Picked;Clear();}d.SelectedDateChanged+=Picked;}
    }
    public static StackPanel Footer(Window window,string primary,Action save,string cancel="Cancelar",string? cancelTip="Esc")
    {
        var footer=new StackPanel{Orientation=Orientation.Horizontal};
        var no=new Button{Content=cancel,Style=S("GhostButton"),Margin=new Thickness(0,0,8,0),ToolTip=cancelTip};no.Click+=(_,_)=>window.DialogResult=false;footer.Children.Add(no);
        var yes=new Button{Content=primary,Style=S("DialogPrimaryButton"),IsDefault=true};yes.Click+=(_,_)=>save();footer.Children.Add(yes);return footer;
    }
    public static Border Summary(UIElement content)=>new(){Style=S("SummaryCard"),Child=content,VerticalAlignment=VerticalAlignment.Top};
    public static TextBlock Eyebrow(string text)=>new(){Text=text,Style=S("Eyebrow"),Margin=new Thickness(0)};
    public static TextBlock BigValue(string text="")=>new(){Text=text,Style=S("SummaryValue")};
    public static TextBlock Hint(string text,double top=8)=>new(){Text=text,Style=S("Hint"),Margin=new Thickness(0,top,0,0)};
    public static Separator Rule()=>new(){Margin=new Thickness(0,8,0,8)};
}
