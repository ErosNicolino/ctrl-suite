using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// Ações que dependem de uma linha selecionada: o botão só aparece quando está disponível (há seleção),
// em vez de ficar visível e desativado. O botão direito do mouse na lista abre as mesmas ações.
// Botões já ocultos pela tela (ex.: ações que não existem naquele modo) continuam ocultos.
public static class RowActions
{
    public static void Attach(Selector grid,string hint,params Button[] buttons)
    {
        var managed=buttons.Where(b=>b.Visibility==Visibility.Visible).ToArray();if(managed.Length==0)return;
        TextBlock? hintText=null;
        if(hint.Length>0&&managed[0].Parent is Panel panel)
        {
            hintText=new TextBlock{Text=hint,Foreground=ThemeManager.Get("Muted"),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,12,0)};
            panel.Children.Insert(panel.Children.IndexOf(managed[0]),hintText);
            if(panel is DockPanel)DockPanel.SetDock(hintText,DockPanel.GetDock(managed[0]));
        }
        void Sync(){foreach(var b in managed)b.Visibility=b.IsEnabled?Visibility.Visible:Visibility.Collapsed;if(hintText!=null)hintText.Visibility=managed.Any(b=>b.IsEnabled)?Visibility.Collapsed:Visibility.Visible;}
        foreach(var b in managed)b.IsEnabledChanged+=(_,_)=>Sync();
        Sync();
        grid.ContextMenu=new ContextMenu();
        grid.PreviewMouseRightButtonDown+=(_,e)=>
        {
            var container=FindRow(e.OriginalSource as DependencyObject);
            if(container is DataGridRow row&&!row.IsSelected){(grid as DataGrid)?.SelectedItems.Clear();row.IsSelected=true;row.Focus();}
            else if(container is ListBoxItem item&&!item.IsSelected){grid.SelectedItem=item.DataContext;item.Focus();}
        };
        grid.ContextMenuOpening+=(_,e)=>
        {
            var menu=grid.ContextMenu!;menu.Items.Clear();
            foreach(var b in managed.Where(x=>x.IsEnabled)){var target=b;var item=new MenuItem{Header=Label(b)};item.Click+=(_,_)=>target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,target));menu.Items.Add(item);}
            if(menu.Items.Count==0)e.Handled=true;
        };
    }
    static Control? FindRow(DependencyObject? element){while(element!=null&&element is not DataGridRow&&element is not ListBoxItem)element=element is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);return element as Control;}
    static string Label(Button button)
    {
        if(button.Content is string text)return ButtonLabelConverter.Clean(text);
        if(button.Content is DependencyObject content&&FindText(content) is string inner)return inner;
        return button.ToolTip?.ToString()??"Ação";
    }
    static string? FindText(DependencyObject element)
    {
        if(element is TextBlock block&&block.Text.Length>0)return block.Text;
        foreach(var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())if(FindText(child) is string found)return found;
        return null;
    }
}
