using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
public sealed class PermissionsWindow : Window
{
    readonly Dictionary<string,ComboBox> choices=[];
    public PermissionsWindow(DataStore store,Guid? userId,string role,string name)
    {
        Title="Permissões — "+name;Width=800;Height=690;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(30)};Content=root;var header=new StackPanel{Margin=new Thickness(0,0,0,20)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text=name,Style=(Style)FindResource("Title")});header.Children.Add(new TextBlock{Text=userId.HasValue?$"Perfil base: {role} · Escolha uma exceção apenas onde precisar.":"Usuários herdam este perfil. Exceções individuais continuam valendo.",Foreground=ThemeManager.Get("Muted"),Margin=new Thickness(0,8,0,0)});
        var footer=new StackPanel{Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);var feedback=new TextBlock{Foreground=ThemeManager.Get("Muted"),Margin=new Thickness(0,0,0,12),TextWrapping=TextWrapping.Wrap};footer.Children.Add(feedback);var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);var cancel=new Button{Content="Fechar",IsCancel=true,Style=(Style)FindResource("SoftButton")};var save=new Button{Content="Salvar permissões",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(10,0,0,0)};buttons.Children.Add(cancel);buttons.Children.Add(save);
        var groups=new StackPanel();root.Children.Add(new ScrollViewer{Content=groups,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var states=store.PermissionPanel(userId,role);
        foreach(var group in states.GroupBy(x=>x.Group))
        {
            var rows=new StackPanel{Margin=new Thickness(14,8,14,10)};var expander=new Expander{Header=group.Key,Content=rows,IsExpanded=group.Key=="Visualização",Margin=new Thickness(0,0,0,12),Padding=new Thickness(8),Background=ThemeManager.Get("Surface")};groups.Children.Add(expander);
            foreach(var state in group)
            {
                var grid=new Grid{Margin=new Thickness(0,7,0,7)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});rows.Children.Add(grid);var text=new StackPanel();text.Children.Add(new TextBlock{Text=state.Label,FontWeight=FontWeights.SemiBold});var origin=new TextBlock{Text=state.Origin,Foreground=ThemeManager.Get("Muted"),FontSize=12,Margin=new Thickness(0,5,10,0)};text.Children.Add(origin);grid.Children.Add(text);
                var combo=new ComboBox{ItemsSource=userId.HasValue?new[]{"Usar perfil","Permitir só este usuário","Bloquear este usuário"}:new[]{"Padrão do perfil","Permitir","Negar"},SelectedIndex=userId.HasValue?(state.Exception==null?0:state.Exception.Value?1:2):(state.Allowed==(role=="Administrador"||GraficaCaixa.Permissions.Catalog.Single(p=>p.Key==state.Key).Financeiro)?0:state.Allowed?1:2),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(combo,1);grid.Children.Add(combo);choices[state.Key]=combo;
                combo.SelectionChanged+=(_,_)=>origin.Text=combo.SelectedIndex==0?((userId.HasValue?state.Profile:role=="Administrador"||GraficaCaixa.Permissions.Catalog.Single(p=>p.Key==state.Key).Financeiro)?"✓ Permitido pelo perfil":"✕ Negado pelo perfil"):combo.SelectedIndex==1?(userId.HasValue?"✓ Exceção individual":"✓ Permitido pelo perfil"):(userId.HasValue?"✕ Bloqueado para este usuário":"✕ Negado pelo perfil");
            }
        }
        save.Click+=(_,_)=>{try{store.SavePermissions(new(userId,userId.HasValue?null:role,choices.ToDictionary(x=>x.Key,x=>(bool?)(x.Value.SelectedIndex==0?null:x.Value.SelectedIndex==1))));feedback.Text=new System.Resources.ResourceManager("GraficaCaixa.Wpf.Resources.Texts",typeof(PermissionsWindow).Assembly).GetString("PermissionsSaved",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));}catch(Exception ex){feedback.Text=ex.Message;}};
    }
}


