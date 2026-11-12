using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 3): diálogo simples azul-petróleo; erro junto do campo. Mesma busca (local ou no servidor) e mesmo resultado.
public sealed class CustomerLinkDialog : Window
{
	private readonly ComboBox customers = new ComboBox
	{
		DisplayMemberPath = "Name",
		MinWidth = 360.0
	};

	public CustomerProfile? Selected => customers.SelectedItem as CustomerProfile;

	public CustomerLinkDialog(DataStore store)
	{
		Title = "Vincular a cliente cadastrado";
		Width = Math.Min(560, SystemParameters.WorkArea.Width - 32);
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		var panel = new StackPanel();
		var field = DialogKit.LabeledField("Cliente", customers, store.IsRemote ? "Digite parte do nome para procurar." : null);
		panel.Children.Add(field.Panel);
		panel.Children.Add(DialogKit.Hint("O pedido passa a ser identificado por este cliente.", 0));
		if(!store.IsRemote)customers.ItemsSource=store.Load().Customers.OrderBy(x=>x.Name).ToList();
        else {customers.IsEditable=true;bool applying=false;CancellationTokenSource? pending=null;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};async Task Load(){pending?.Cancel();pending=new();var token=pending.Token;try{var text=customers.Text;var rows=await store.QueryRemoteAsync<List<CustomerProfile>>("desktop/lookup/customers?search="+Uri.EscapeDataString(text),token);token.ThrowIfCancellationRequested();applying=true;try{customers.ItemsSource=rows;customers.Text=text;}finally{applying=false;}}catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Clientes",ex.Message);}}
            Loaded+=async (_,_)=>{await Load();var edit=customers.Template.FindName("PART_EditableTextBox",customers) as TextBox;if(edit!=null)edit.TextChanged+=(_,_)=>{if(applying)return;pending?.Cancel();timer.Stop();timer.Start();};};timer.Tick+=async (_,_)=>{timer.Stop();await Load();};Closed+=(_,_)=>{pending?.Cancel();timer.Stop();};}
		customers.SelectionChanged += (_, _) => { field.Error.Visibility = Visibility.Collapsed; Ui.SetHasError(customers, false); };
		Content = new DialogFrame { Kind = "Cliente", IconName = "link", Title = "Vincular a cliente", Subtitle = "Escolha o cliente cadastrado", Content = panel, Footer = DialogKit.Footer(this, "Vincular", Link), ScrollBody = false };
		Loaded += (_, _) => customers.Focus();
		void Link()
		{
			if (Selected == null) { DialogKit.ShowError(field, customers, "Selecione um cliente."); return; }
			DialogResult = true;
		}
	}
}
