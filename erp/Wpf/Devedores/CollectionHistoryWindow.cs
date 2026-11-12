using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace GraficaCaixa.Wpf;

public sealed class CollectionHistoryWindow:Window
{
    bool writeBusy;
    AppData initialData=new();readonly DataStore store;readonly Guid debtId;readonly bool order;readonly ComboBox status=new();readonly TextBox notes=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=85,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    public CollectionHistoryWindow(DataStore store,Guid id,bool order,string customer,AppData? prepared=null)
    {
        this.store=store;debtId=id;this.order=order;Title="Histórico de cobrança";Width=850;Height=650;MinWidth=700;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new Grid{Margin=new Thickness(24)};Content=panel;foreach(var h in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})panel.RowDefinitions.Add(new RowDefinition{Height=h});
        panel.Children.Add(new TextBlock{Text=customer+" • Histórico de cobrança",Style=(Style)FindResource("SectionTitle"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        var data=initialData=prepared??(store.IsRemote?throw new InvalidOperationException("Carregue o histórico antes de abrir."):store.Load());var grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,ItemsSource=CollectionControl.History(data,id,order)};Grid.SetRow(grid,1);panel.Children.Add(grid);
        grid.Columns.Add(new DataGridTextColumn{Header="DATA/HORA",Binding=new Binding("CreatedAt"){StringFormat="dd/MM/yyyy HH:mm"},Width=145});
        grid.Columns.Add(new DataGridTextColumn{Header="RESULTADO",Binding=new Binding("Status"),Width=160});
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));grid.Columns.Add(new DataGridTextColumn{Header="OBSERVAÇÃO",Binding=new Binding("Notes"),Width=new DataGridLength(1,DataGridLengthUnitType.Star),ElementStyle=textStyle});
        var editor=new StackPanel{Margin=new Thickness(0,16,0,0)};Grid.SetRow(editor,2);panel.Children.Add(editor);
        if(CollectionControl.Balance(data,id,order)==0){editor.Children.Add(new TextBlock{Text="Pago • histórico preservado. O status acompanha os recebimentos registrados."});return;}
        editor.Children.Add(new TextBlock{Text="Novo contato — data/hora registradas ao salvar",Margin=new Thickness(0,0,0,8)});status.ItemsSource=CollectionControl.Statuses;status.SelectedItem=CollectionControl.Status(data,id,order);editor.Children.Add(status);editor.Children.Add(new TextBlock{Text="Observação do contato",Margin=new Thickness(0,8,0,0)});notes.Margin=new Thickness(0,8,0,10);editor.Children.Add(notes);
        var save=new Button{Content="Registrar contato",Style=(Style)FindResource("PrimaryButton"),HorizontalAlignment=HorizontalAlignment.Right};save.Click+=(_,_)=>Save();editor.Children.Add(save);UnsavedChanges.Attach(this,Save);
    }
    async void Save()
    {
        if(writeBusy)return;writeBusy=true;try{
        try{var contact=new CollectionContact{DebtId=debtId,IsOrder=order,Status=status.SelectedItem?.ToString()??"",Notes=notes.Text};await BusinessUi.ExecuteAsync(store,"collection.save",debtId,order?(object?)initialData.ScheduledOrders.FirstOrDefault(x=>x.Id==debtId):initialData.ManualDebts.FirstOrDefault(x=>x.Id==debtId),new CollectionArgs(contact),()=>store.Update(d=>{CollectionControl.Register(d,contact);return 0;}));DialogResult=true;}
        catch(Exception ex){AlertWindow.Info(this,"Confira o contato",ex.Message);}
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
}
