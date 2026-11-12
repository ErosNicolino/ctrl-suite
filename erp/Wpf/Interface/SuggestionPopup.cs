using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace GraficaCaixa.Wpf;

public sealed record SuggestionOption(string Title,string Detail,object Value);
public static class SuggestionPopup
{
    static Brush Color(string value)=>(Brush)new BrushConverter().ConvertFromString(value)!;
    public static ListBox CreateList()
    {
        var list=new ListBox{MaxHeight=320,Background=GraficaCaixa.Wpf.ThemeManager.Get("Surface"),BorderThickness=new Thickness(0),Padding=new Thickness(4),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        var stack=new FrameworkElementFactory(typeof(StackPanel));stack.SetValue(FrameworkElement.MarginProperty,new Thickness(12,9,12,9));
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding(nameof(SuggestionOption.Title)));title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);title.SetValue(TextBlock.FontSizeProperty,14d);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);title.SetValue(TextBlock.ForegroundProperty,GraficaCaixa.Wpf.ThemeManager.Get("Ink"));stack.AppendChild(title);
        var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding(nameof(SuggestionOption.Detail)));detail.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);detail.SetValue(TextBlock.FontSizeProperty,12d);detail.SetValue(TextBlock.ForegroundProperty,GraficaCaixa.Wpf.ThemeManager.Get("Muted"));detail.SetValue(FrameworkElement.MarginProperty,new Thickness(0,3,0,0));stack.AppendChild(detail);list.ItemTemplate=new DataTemplate{VisualTree=stack};
        var border=new FrameworkElementFactory(typeof(Border));border.Name="Row";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));border.SetValue(Border.BackgroundProperty,Brushes.Transparent);var content=new FrameworkElementFactory(typeof(ContentPresenter));border.AppendChild(content);
        var template=new ControlTemplate(typeof(ListBoxItem)){VisualTree=border};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,GraficaCaixa.Wpf.ThemeManager.Get("Hover"),"Row"));template.Triggers.Add(hover);
        var chosen=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};chosen.Setters.Add(new Setter(Border.BackgroundProperty,GraficaCaixa.Wpf.ThemeManager.Get("AccentSoft"),"Row"));template.Triggers.Add(chosen);
        var style=new Style(typeof(ListBoxItem));style.Setters.Add(new Setter(Control.TemplateProperty,template));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(2)));list.ItemContainerStyle=style;
        return list;
    }
    public static void Attach(TextBox text,string heading,Func<string,IReadOnlyList<SuggestionOption>> find,Action<SuggestionOption> selected)
        =>AttachCore(text,heading,(query,_)=>Task.FromResult(find(query)),selected,180);
    public static void AttachAsync(TextBox text,string heading,Func<string,CancellationToken,Task<IReadOnlyList<SuggestionOption>>> find,Action<SuggestionOption> selected)
        =>AttachCore(text,heading,find,selected,350);
    static void AttachCore(TextBox text,string heading,Func<string,CancellationToken,Task<IReadOnlyList<SuggestionOption>>> find,Action<SuggestionOption> selected,int delay)
    {
        var list=CreateList();var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=heading,FontSize=12,Foreground=GraficaCaixa.Wpf.ThemeManager.Get("Muted"),Margin=new Thickness(16,10,16,4)});panel.Children.Add(list);
        var popup=new Popup{PlacementTarget=text,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,VerticalOffset=5,Child=new Border{Child=panel,Background=GraficaCaixa.Wpf.ThemeManager.Get("Surface"),BorderBrush=GraficaCaixa.Wpf.ThemeManager.Get("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10)}};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(delay)};bool applying=false;CancellationTokenSource? pending=null;
        void Choose(SuggestionOption option){pending?.Cancel();applying=true;timer.Stop();popup.IsOpen=false;try{selected(option);}finally{applying=false;}text.Focus();text.CaretIndex=text.Text.Length;}
        timer.Tick+=async(_,_)=>{timer.Stop();if(!text.IsKeyboardFocusWithin||string.IsNullOrWhiteSpace(text.Text)){popup.IsOpen=false;return;}pending?.Cancel();var cancellation=pending=new();IReadOnlyList<SuggestionOption> options;try{options=await find(text.Text,cancellation.Token);if(cancellation.IsCancellationRequested||!text.IsKeyboardFocusWithin)return;}catch(OperationCanceledException){return;}catch{popup.IsOpen=false;return;}list.ItemsSource=options;list.SelectedIndex=-1;popup.Width=Math.Min(Math.Max(320,text.ActualWidth),SystemParameters.WorkArea.Width-32);popup.IsOpen=options.Count>0;};
        text.TextChanged+=(_,_)=>{pending?.Cancel();if(applying)return;timer.Stop();timer.Start();};
        text.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&popup.IsOpen){pending?.Cancel();popup.IsOpen=false;timer.Stop();e.Handled=true;}else if((e.Key==Key.Down||e.Key==Key.Up)&&popup.IsOpen){list.SelectedIndex=e.Key==Key.Down?0:list.Items.Count-1;list.Focus();e.Handled=true;}};
        list.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&list.SelectedItem is SuggestionOption option){Choose(option);e.Handled=true;}else if(e.Key==Key.Escape){popup.IsOpen=false;text.Focus();e.Handled=true;}else if(e.Key==Key.Tab){popup.IsOpen=false;text.Focus();}};
        list.PreviewMouseLeftButtonUp+=(_,e)=>{if(ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) is ListBoxItem{DataContext:SuggestionOption option}){Choose(option);e.Handled=true;}};
        text.Unloaded+=(_,_)=>{pending?.Cancel();timer.Stop();popup.IsOpen=false;};
    }
}

