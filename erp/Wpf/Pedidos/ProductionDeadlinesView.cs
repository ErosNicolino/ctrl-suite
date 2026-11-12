using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace GraficaCaixa.Wpf;
public sealed class ProductionDeadlinesView:Grid
{
    public DataStore? Store{get;set;}readonly TextBox search=new(){Tag="Pesquisar produto ou prazo…"};readonly CheckBox inactive=new(){Content="Incluir inativos",VerticalAlignment=VerticalAlignment.Center};readonly DataGrid list=new();readonly TextBlock status=new();readonly Button edit,toggle;AppData? data;CancellationTokenSource? load;
    public ProductionDeadlinesView()
    {
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new());RowDefinitions.Add(new(){Height=GridLength.Auto});
        var notice=new Border{Style=TryFindResource("NoteCalm") as Style,Margin=new(0,0,0,14),Child=new TextBlock{Text="A contagem do prazo começa após a aprovação da arte.",FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap}};Children.Add(notice);
        var tools=new DockPanel{Margin=new(0,0,0,14)};SetRow(tools,1);Children.Add(tools);var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);tools.Children.Add(actions);
        Button Action(string text,Action action){var b=new Button{Content=text,Style=TryFindResource("SoftButton") as Style,Margin=new(8,0,0,0)};b.Click+=(_,_)=>Run(action);actions.Children.Add(b);return b;}
        Action("Adicionar",()=>Edit(null));edit=Action("Editar",()=>{if(list.SelectedItem is ProductionDeadline r)Edit(r);});toggle=Action("Inativar / reativar",()=>{if(list.SelectedItem is ProductionDeadline r){var copy=JsonSerializer.Deserialize<ProductionDeadline>(JsonSerializer.Serialize(r))!;copy.Active=!copy.Active;Save(copy,r);}});actions.Children.Add(inactive);tools.Children.Add(search);search.TextChanged+=(_,_)=>Filter();inactive.Click+=(_,_)=>Filter();
        list.AutoGenerateColumns=false;list.IsReadOnly=true;list.SelectionMode=DataGridSelectionMode.Single;list.Columns.Add(new DataGridTextColumn{Header="Produto / serviço",Binding=new Binding("Description"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});list.Columns.Add(new DataGridTextColumn{Header="Prazo",Binding=new Binding("DeadlineText"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});list.Columns.Add(new DataGridTextColumn{Header="Observação",Binding=new Binding("Note"),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});list.Columns.Add(StatusTag.Column("Status","Status",new DataGridLength(110)));list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is ProductionDeadline r)Run(()=>Edit(r));};list.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&list.SelectedItem is ProductionDeadline r){e.Handled=true;Run(()=>Edit(r));}};SetRow(list,2);Children.Add(list);SetRow(status,3);status.Margin=new(0,10,0,0);Children.Add(status);list.SelectionChanged+=(_,_)=>{edit.IsEnabled=toggle.IsEnabled=list.SelectedItem!=null;toggle.Content=(list.SelectedItem as ProductionDeadline)?.Active==false?"Reativar":"Inativar";};list.MouseDoubleClick+=(_,_)=>Run(()=>{if(list.SelectedItem is ProductionDeadline r)Edit(r);});Loaded+=(_,_)=>Refresh();Unloaded+=(_,_)=>load?.Cancel();
    }
    async void Refresh(){if(Store==null)return;load?.Cancel();var ct=load=new();try{status.Text="Carregando…";data=Store.IsRemote?(await Store.QueryRemoteAsync<ModulePage>("api/v1/orcamentos/configuracao?projection=desktop",ct.Token)).Data.Deserialize<AppData>():Store.Load();ct.Token.ThrowIfCancellationRequested();Filter();}catch(OperationCanceledException){}catch(Exception ex){status.Text=ex.Message;}}
    void Filter(){if(data==null)return;var term=ProductOperations.Normalize(search.Text);var rows=(data.ProductionDeadlines??[]).Where(x=>(x.Active||inactive.IsChecked==true)&&ProductOperations.Normalize(x.Description+" "+x.DeadlineText+" "+x.Note).Contains(term)).OrderBy(x=>x.Order).ThenBy(x=>x.Description).ToList();list.ItemsSource=rows;status.Text=rows.Count==0?"Nenhum prazo encontrado.":$"{rows.Count} prazo(s)";edit.IsEnabled=toggle.IsEnabled=list.SelectedItem!=null;}
    void Run(Action action){try{action();}catch(Exception ex){AlertWindow.Info(Window.GetWindow(this),"Prazos de produção",ex.Message);}}
    async void Save(ProductionDeadline row,ProductionDeadline? original){if(Store==null)return;IsEnabled=false;try{await BusinessUi.ExecuteAsync(Store,"deadline.save",row.Id,original,new DeadlineArgs(row),()=>Store.Update(d=>{d.ProductionDeadlines??=ProductionDeadlineDefaults.Create();var current=d.ProductionDeadlines.FirstOrDefault(x=>x.Id==row.Id);if(original!=null){if(current==null)throw new InvalidOperationException("Prazo removido. Atualize a lista.");DataRelations.RequireUnchanged(current,original);}else if(current!=null)throw new InvalidOperationException("Prazo já existe.");row.Order=current?.Order??checked(d.ProductionDeadlines.Select(x=>x.Order).DefaultIfEmpty(0).Max()+10);row.Revision=(current?.Revision??0)+1;if(current!=null)d.ProductionDeadlines.Remove(current);d.ProductionDeadlines.Add(row);AuditService.Record(d,App.CurrentUser,"Prazo de produção salvo","Prazo",row.Id.ToString(),original==null?"":JsonSerializer.Serialize(original),JsonSerializer.Serialize(row),"",row.Description);return 0;}));Refresh();Toast.Show(Window.GetWindow(this),"Prazo salvo.",tone:"ok");}catch(Exception ex){AlertWindow.Info(Window.GetWindow(this),"Prazo não salvo",ex.Message);}finally{IsEnabled=true;}}
    void Edit(ProductionDeadline? original)
    {
        var row=original==null?new ProductionDeadline{Order=((data?.ProductionDeadlines??[]).Select(x=>x.Order).DefaultIfEmpty(0).Max()+10)}:JsonSerializer.Deserialize<ProductionDeadline>(JsonSerializer.Serialize(original))!;
        var dialog=new Window{Title=original==null?"Adicionar prazo":"Editar prazo",Width=600,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Window.GetWindow(this)};
        var desc=new TextBox{Text=row.Description,Tag="Ex.: Cartão de visita"};var deadline=new TextBox{Text=row.DeadlineText,Tag="Ex.: 5 dias"};var note=new TextBox{Text=row.Note,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=76,VerticalContentAlignment=VerticalAlignment.Top,Tag="Opcional"};
        var descField=DialogKit.LabeledField("Produto / serviço *",desc);var deadlineField=DialogKit.LabeledField("Prazo *",deadline,"Texto que aparece para o cliente. A contagem começa após a aprovação da arte.");var noteField=DialogKit.LabeledField("Observação",note);
        var active=new CheckBox{Content="Ativo (aparece na lista de prazos)",IsChecked=row.Active};var body=new StackPanel{Children={descField.Panel,deadlineField.Panel,noteField.Panel,active}};
        void Confirm()
        {
            if(string.IsNullOrWhiteSpace(desc.Text)||desc.Text.Length>200){DialogKit.ShowError(descField,desc,"Informe o produto ou serviço (até 200 caracteres).");return;}
            if(string.IsNullOrWhiteSpace(deadline.Text)||deadline.Text.Length>200){DialogKit.ShowError(deadlineField,deadline,"Informe o prazo (até 200 caracteres).");return;}
            if(note.Text.Length>2000){DialogKit.ShowError(noteField,note,"A observação pode ter até 2.000 caracteres.");return;}
            row.Description=desc.Text.Trim();row.DeadlineText=deadline.Text.Trim();row.Note=note.Text.Trim();row.Active=active.IsChecked==true;dialog.DialogResult=true;
        }
        dialog.Content=new DialogFrame{Kind="Neutro",IconName="clock",Title=dialog.Title,Subtitle="Enter avança · Ctrl+Enter salva · Esc fecha",ScrollBody=false,Content=body,Footer=DialogKit.Footer(dialog,"Salvar prazo",Confirm)};
        KeyboardFlow.Attach(dialog,Confirm);dialog.Loaded+=(_,_)=>{desc.Focus();desc.SelectAll();};
        if(dialog.ShowDialog()==true)Save(row,original);
    }
}
