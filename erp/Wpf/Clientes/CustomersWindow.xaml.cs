using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace GraficaCaixa.Wpf;

public partial class CustomersWindow : Window, IComponentConnector
{
    bool writeBusy;
	private readonly DataStore store;

	private readonly ObservableCollection<CustomerRow> rows = new ObservableCollection<CustomerRow>();

	private List<CustomerRow> all = new List<CustomerRow>();
    string? remoteSort;bool descending;readonly ModuleLoader remoteLoad;CancellationTokenSource? detailCancellation;

	public CustomersWindow(DataStore store)
	{
		InitializeComponent();
		this.store = store;remoteLoad=new(this,store,true,true);remoteLoad.Watch(Grid);Grid.Sorting+=(_,e)=>{if(!store.IsRemote)return;e.Handled=true;var column=e.Column.SortMemberPath;if(string.IsNullOrEmpty(column)&&e.Column is System.Windows.Controls.DataGridBoundColumn b&&b.Binding is System.Windows.Data.Binding binding)column=binding.Path.Path;descending=remoteSort==column&&!descending;remoteSort=column;foreach(var c in Grid.Columns)c.SortDirection=null;e.Column.SortDirection=descending?System.ComponentModel.ListSortDirection.Descending:System.ComponentModel.ListSortDirection.Ascending;remoteLoad.Reset();LoadData();};remoteLoad.Cleared+=()=>{detailCancellation?.Cancel();rows.Clear();all.Clear();UpdateDetail();};Closed+=(_,_)=>detailCancellation?.Cancel();
		Grid.ItemsSource = rows;if(store.IsRemote)foreach(var c in Grid.Columns.Skip(2))c.CanUserSort=false;
		RowActions.Attach(Grid, "", OpenOrdersButton, RepeatLastButton);
		Loaded += (object _, RoutedEventArgs _) =>
		{
			LoadData();
		};
	}

	private async void LoadData()
	{
		AppData? d=store.IsRemote?await remoteLoad.LoadAsync("clientes","&search="+ModuleLoader.Encode(SearchBox.Text)+(IncompleteBox.IsChecked==true?"&incomplete=true":"")+(DuplicatesBox.IsChecked==true?"&duplicates=true":"")+"&sort="+ModuleLoader.Encode(remoteSort)+"&descending="+(descending?"true":"false"),LoadData,true):store.Load();if(d==null)return;
        if(store.IsRemote){var counts=remoteLoad.Result?.Counts??[];noFullPhone=(int)counts.GetValueOrDefault("noFullPhone");duplicatePhones=(int)counts.GetValueOrDefault("duplicatePhones");UpdatePurge();long? Count(string key,Guid id)=>counts.TryGetValue(key+":"+id.ToString("N"),out var v)?v:null;all=d.Customers.Select(x=>new CustomerRow(x.Id,x.Name,x.Contact,(int?)Count("orders",x.Id),Count("received",x.Id)/100m,Count("pending",x.Id)/100m,Count("last",x.Id) is long last?DateTime.ParseExact(last.ToString(),"yyyyMMdd",null):null){PriceType=x.PriceType??""}).ToList();if(!remoteLoad.Appending)rows.Clear();var ids=rows.Select(x=>x.Id).ToHashSet();foreach(var row in all)if(ids.Add(row.Id))rows.Add(row);if(!remoteLoad.Appending)UpdateDetail();return;}
		noFullPhone=d.Customers.Count(x=>!CustomerBasicOperations.HasFullPhone(x));var groups=CustomerBasicOperations.PhoneDuplicates(d);duplicatePhones=groups.Count;UpdatePurge();
		var repeated=DuplicatesBox.IsChecked==true?groups.SelectMany(g=>g).Select(x=>x.Id).ToHashSet():null;
		all = (from x in d.Customers
			where (IncompleteBox.IsChecked != true || CustomerBasicOperations.IsIncomplete(x)) && (repeated == null || repeated.Contains(x.Id))
			select new CustomerRow(x.Name, x.Contact, d, x.Id) { PriceType = x.PriceType ?? "" } into x
			orderby repeated == null ? "" : CustomerBasicOperations.PhoneKey(x.Contact), x.Name
			select x).ToList();
		Apply();
	}

	private async void EditCustomer_Click(object s, RoutedEventArgs e)
	{
        if(writeBusy)return;writeBusy=true;try{
		object selectedItem = Grid.SelectedItem;
		CustomerRow? row = selectedItem as CustomerRow;
		if (row != null)
		{
			CustomerProfile? original = (await store.BusinessContextAsync("customer",row.Id)).Customers.FirstOrDefault((CustomerProfile x) => x.Id == row.Id);
			if (original == null)
			{
				LoadData();
			}
			else if (new CustomerEditor(original)
			{
				Owner = this,
				OpenExisting = ShowExisting,
				PersistAsync = async (string name, string phone, string email, string cpf, string address) =>
				{
					if(store.IsRemote)await store.RemoteDesktopWriteAsync($"desktop/customers/{original.Id}",new DesktopCustomerCommand(Guid.NewGuid(),DataStore.DesktopRevision(original),name,phone,email,cpf,address));else store.Update((AppData d) =>
					{
						CustomerDirectory.Edit(d, original, name, phone);
						CustomerProfile customerProfile = d.Customers.First((CustomerProfile x) => x.Id == original.Id);
						customerProfile.Email = email;
						customerProfile.CpfCnpj = cpf;
						customerProfile.Address = address;
						AuditService.Record(d, App.CurrentUser, "Dados fiscais do cliente atualizados", "Cliente", customerProfile.Id.ToString(), "", "dados opcionais atualizados", "", "");
						return 0;
					});
				}
			}.ShowDialog() == true)
			{
				LoadData();
			}
		}
	
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

	// Novo cliente: mesmo editor da edição; grava pelo servidor (cria o cadastro quando não há revisão esperada).
	private async void NewCustomer_Click(object s, RoutedEventArgs e)
	{
		if(writeBusy)return;writeBusy=true;
		try{
			var draft=new CustomerProfile{Id=Guid.NewGuid()};
			if(new CustomerEditor(draft,true){Owner=this,OpenExisting=ShowExisting,PersistAsync=async (string name,string phone,string email,string cpf,string address)=>{if(store.IsRemote)await store.RemoteDesktopWriteAsync($"desktop/customers/{draft.Id}",new DesktopCustomerCommand(Guid.NewGuid(),null!,name,phone,email,cpf,address));else store.Update(d=>{CustomerBasicOperations.Create(d,draft.Id,name,phone,email,cpf,address,App.CurrentUser);return 0;});}}.ShowDialog()==true){Toast.Show(this,"Cliente cadastrado.",tone:"ok");remoteLoad.Reset();LoadData();}
		}catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível cadastrar",ex.Message);}finally{writeBusy=false;}
	}
	void Incomplete_Click(object s,RoutedEventArgs e){if(s==IncompleteBox&&IncompleteBox.IsChecked==true)DuplicatesBox.IsChecked=false;else if(s==DuplicatesBox&&DuplicatesBox.IsChecked==true)IncompleteBox.IsChecked=false;remoteLoad.Reset();LoadData();}
	// Telefone já cadastrado: tira os filtros e pesquisa pelo número, mostrando o cliente existente.
	void ShowExisting(string phone){IncompleteBox.IsChecked=false;DuplicatesBox.IsChecked=false;SearchBox.Text=PhoneText.Digits(phone);remoteLoad.Reset();LoadData();}

	// Telefones repetidos: a lista vem agrupada pelo número; selecione 2 ou mais do mesmo número e escolha qual cadastro fica.
	async void Merge_Click(object s,RoutedEventArgs e)
	{
		if(writeBusy)return;
		var selected=Grid.SelectedItems.OfType<CustomerRow>().ToList();
		if(selected.Count<2||selected.Select(x=>CustomerBasicOperations.PhoneKey(x.Contact)).Distinct().Count()!=1||CustomerBasicOperations.PhoneKey(selected[0].Contact).Length==0){AlertWindow.Info(this,"Mesclar clientes","Selecione 2 ou mais cadastros com o mesmo telefone (Ctrl+clique).");return;}
		var keep=ChooseKeep(selected);if(keep==null)return;
		writeBusy=true;
		try
		{
			var original=(await store.BusinessContextAsync("customer",keep.Id)).Customers.FirstOrDefault(x=>x.Id==keep.Id);if(original==null){LoadData();return;}
			var remove=selected.Where(x=>x.Id!=keep.Id).Select(x=>x.Id).ToList();
			await BusinessUi.ExecuteAsync(store,"customer.merge",keep.Id,original,new CustomerMergeArgs(remove),()=>store.Update(d=>{CustomerBasicOperations.Merge(d,keep.Id,remove,App.CurrentUser);return 0;}));
			Toast.Show(this,$"Cadastros unidos em {keep.Name}.",tone:"ok");
		}
		catch(Exception ex){AlertWindow.Info(this,"Mesclar clientes",ex.Message);}
		finally{writeBusy=false;remoteLoad.Reset();LoadData();}
	}
	CustomerRow? ChooseKeep(List<CustomerRow> rows)
	{
		var window=new Window{Owner=this,Title="Mesclar clientes",Width=Math.Min(520,SystemParameters.WorkArea.Width-32),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
		var body=new System.Windows.Controls.StackPanel();var options=new List<(System.Windows.Controls.RadioButton Button,CustomerRow Row)>();
		body.Children.Add(DialogKit.Hint($"Telefone {rows[0].Contact}. Escolha o cadastro que fica:",0));
		foreach(var row in rows.OrderByDescending(x=>x.Orders).ThenByDescending(x=>x.Name.Length)){var radio=new System.Windows.Controls.RadioButton{Content=$"{row.Name}  ·  {row.Orders} pedido(s)",GroupName="keep",Margin=new Thickness(0,8,0,0),IsChecked=options.Count==0};options.Add((radio,row));body.Children.Add(radio);}
		body.Children.Add(DialogKit.Hint("O que fica recebe e-mail, CPF/CNPJ, endereço e tipo de preço que faltarem. Pedidos e dívidas dos outros passam para ele, com o nome dele. Lançamentos do caixa não mudam. Os outros cadastros saem (fica registrado na auditoria).",12));
		var footer=new System.Windows.Controls.StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal};
		var cancel=new System.Windows.Controls.Button{Content="Cancelar",IsCancel=true,Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,0,8,0)};footer.Children.Add(cancel);
		var ok=new System.Windows.Controls.Button{Content="Mesclar",Style=(Style)FindResource("DialogPrimaryButton")};ok.Click+=(_,_)=>window.DialogResult=true;footer.Children.Add(ok);
		window.Content=new DialogFrame{Kind="Neutro",IconName="users",Title="Mesclar clientes",Subtitle="Esc cancela",Content=body,Footer=footer,ScrollBody=false};
		return window.ShowDialog()==true?options.First(x=>x.Button.IsChecked==true).Row:null;
	}

	// Limpeza em lote: só aparece com "Só incompletos" marcado. O servidor confere a contagem antes de excluir.
	int noFullPhone,duplicatePhones;
	void UpdatePurge()
	{
		PurgeButton.Content=$"Excluir {noFullPhone} sem telefone completo";PurgeButton.Visibility=IncompleteBox.IsChecked==true&&noFullPhone>0?Visibility.Visible:Visibility.Collapsed;
		DuplicatesBox.Content=$"Telefones repetidos ({duplicatePhones})";MergeButton.Visibility=DuplicatesBox.IsChecked==true&&duplicatePhones>0?Visibility.Visible:Visibility.Collapsed;
	}
	async void Purge_Click(object s,RoutedEventArgs e)
	{
		if(writeBusy||noFullPhone==0)return;var count=noFullPhone;
		if(!AlertWindow.Confirm(this,"Excluir cadastros incompletos",$"Excluir {count} cadastro(s) sem telefone completo com DDD?\n\nQuem tem número completo fica, mesmo sem nome.\nPedidos, lançamentos e dívidas não são apagados: continuam com o nome que foi registrado neles.\nCada exclusão fica na auditoria e o cadastro não volta sozinho.",true))return;
		writeBusy=true;
		try{var done=await BusinessUi.ExecuteAsync(store,"customer.purge",Guid.Empty,null,new CustomerPurgeArgs(count),()=>store.Update(d=>CustomerBasicOperations.DeleteWithoutFullPhone(d,count,App.CurrentUser)));AlertWindow.Info(this,"Excluir cadastros incompletos",$"{done} cadastro(s) excluído(s).");}
		catch(Exception ex){AlertWindow.Info(this,"Excluir cadastros incompletos",ex.Message);}
		finally{writeBusy=false;remoteLoad.Reset();LoadData();}
	}

	// Exclui um ou vários cadastros selecionados (só o cadastro; pedidos e lançamentos mantêm o nome registrado).
	async void DeleteCustomer_Click(object s,RoutedEventArgs e)
	{
		if(writeBusy)return;
		var selected=Grid.SelectedItems.OfType<CustomerRow>().ToList();
		if(selected.Count==0){AlertWindow.Info(this,"Excluir cliente","Selecione um ou mais clientes (Ctrl ou Shift para vários).");return;}
		var names=string.Join("\n",selected.Take(8).Select(x=>$"• {x.Name} {(x.Contact=="Não informado"?"":x.Contact)}"))+(selected.Count>8?$"\n… e mais {selected.Count-8}":"");
		if(!AlertWindow.Confirm(this,"Excluir cliente",$"Excluir {selected.Count} cadastro(s)?\n\n{names}\n\nPedidos, lançamentos e dívidas não são apagados: continuam com o nome que foi registrado neles.",true))return;
		writeBusy=true;var done=0;
		try
		{
			foreach(var row in selected)
			{
				var original=(await store.BusinessContextAsync("customer",row.Id)).Customers.FirstOrDefault(x=>x.Id==row.Id);if(original==null)continue;
				await BusinessUi.ExecuteAsync(store,"customer.delete",row.Id,original,new EmptyArgs(),()=>store.Update(d=>{CustomerBasicOperations.Delete(d,row.Id,App.CurrentUser);return 0;}));
				done++;
			}
		}
		catch(Exception ex){AlertWindow.Info(this,"Excluir cliente",(done>0?$"{done} excluído(s). ":"")+ex.Message);}
		finally{writeBusy=false;remoteLoad.Reset();LoadData();}
	}

	private void Apply()
	{
		string term = SearchBox.Text.Trim();
		rows.Clear();
		foreach (CustomerRow item in all.Where((CustomerRow x) => string.IsNullOrWhiteSpace(term) || (x.Name + " " + x.Contact).Contains(term, StringComparison.CurrentCultureIgnoreCase) || PhoneText.Matches(x.Contact, term)))
		{
			rows.Add(item);
		}
		if (Grid.SelectedItem == null && rows.Count > 0)
		{
			Grid.SelectedItem = rows[0];
		}
		UpdateDetail();
	}

	private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
	{
        if(store.IsRemote){remoteLoad.Reset();LoadData();return;}
		Apply();
	}

	// Cards ⇄ Lista (UX 2.0): mesma fonte de dados, mesma seleção e as mesmas ações; a escolha fica gravada por usuário.
	private void Grid_SelectionChanged(object s, SelectionChangedEventArgs e)
	{
		UpdateDetail();
	}

	private void Grid_DoubleClick(object s, MouseButtonEventArgs e)
	{
		OpenSelectedOrders();
	}

	private async void UpdateDetail()
	{
		// Sem cliente escolhido: ficha vazia com "Novo cliente" (Editar e Excluir só aparecem com um cliente escolhido).
		DetailEmpty.Visibility = Grid.SelectedItem is CustomerRow ? Visibility.Collapsed : Visibility.Visible;
		DetailContent.Visibility = Grid.SelectedItem is CustomerRow ? Visibility.Visible : Visibility.Collapsed;
		if (!(Grid.SelectedItem is CustomerRow customerRow))
		{
			DetailName.Text = "Selecione um cliente";
			DetailContact.Text = "";
			TextBlock detailOrders = DetailOrders;
			TextBlock detailTotal = DetailTotal;
			string text = "—";ShowPending(text);
			string text3 = (detailTotal.Text = text);
			detailOrders.Text = text3;
			DetailHistory.Text = "Escolha uma linha para ver o histórico.";
			OpenOrdersButton.IsEnabled = false;
		}
		else
		{
			DetailName.Text = customerRow.Name;
			FillPriceType(customerRow);
			DetailContact.Text = ((customerRow.Contact == "Não informado") ? "Contato não informado" : customerRow.Contact);
            if(store.IsRemote){
                detailCancellation?.Cancel();var cancellation=detailCancellation=new();DetailOrders.Text=DetailTotal.Text="—";ShowPending("—");DetailHistory.Text="Carregando histórico…";OpenOrdersButton.IsEnabled=false;
                try{var summary=await store.QueryRemoteAsync<CustomerSummary>($"api/v1/clientes/{customerRow.Id}/resumo",cancellation.Token);if(cancellation.IsCancellationRequested||Grid.SelectedItem!=customerRow)return;
                    DetailOrders.Text=summary.Orders.ToString();DetailTotal.Text=summary.Total;ShowPending(summary.Pending);DetailHistory.Text=summary.Details.Length==0?"Nenhum pedido registrado para este cliente.":summary.Details;OpenOrdersButton.IsEnabled=summary.Orders>0;
                }catch(OperationCanceledException){}catch(Exception ex){DetailHistory.Text=ex.Message;}return;
            }
			DetailOrders.Text = customerRow.Orders.ToString();
			DetailTotal.Text = customerRow.Total;
			ShowPending(customerRow.Pending);
			DetailHistory.Text = (string.IsNullOrWhiteSpace(customerRow.Details) ? "Nenhum pedido registrado para este cliente." : customerRow.Details);
			OpenOrdersButton.IsEnabled = customerRow.Orders > 0;
		}
	}

	// "A receber" só fica vermelho quando há saldo (algum dígito diferente de zero).
	void ShowPending(string text){DetailPending.Text=text;DetailPending.SetResourceReference(ForegroundProperty,text.Any(c=>c is >='1' and <='9')?"Danger":"Ink");}

	// Tipo de preço na ficha: muda o desconto automático dos próximos lançamentos e pedidos (Configurações › Regras).
	sealed record PriceTypeOption(string Value, string Label) { public override string ToString() => Label; }
	bool settingPriceType;
	static bool Can(string key) => App.CurrentUser.Permissions?.GetValueOrDefault(key) ?? App.CurrentUser.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase);
	void FillPriceType(CustomerRow row)
	{
		settingPriceType = true;
		try
		{
			var r = BusinessRules.Current;
			PriceTypeBox.ItemsSource = new[] { new PriceTypeOption("", "Balcão (preço normal)"), new PriceTypeOption("Revenda", $"Revenda (−{r.ResellerDiscount:0.##}%)"), new PriceTypeOption("Empresa", $"Empresa (−{r.CompanyDiscount:0.##}%)") };
			PriceTypeBox.DisplayMemberPath = nameof(PriceTypeOption.Label);
			PriceTypeBox.SelectedIndex = row.PriceType switch { "Revenda" => 1, "Empresa" => 2, _ => 0 };
			var can = Can("customers.edit") && Can("products.edit");
			PriceTypeBox.IsEnabled = can;
			PriceTypeBox.ToolTip = can ? "Desconto automático no lançamento e no pedido (percentuais em Configurações › Regras)" : "Para mudar, é preciso poder alterar clientes e preços";
		}
		finally { settingPriceType = false; }
	}
	async void PriceType_Changed(object s, SelectionChangedEventArgs e)
	{
		if (settingPriceType || Grid.SelectedItem is not CustomerRow row || PriceTypeBox.SelectedItem is not PriceTypeOption option || option.Value == row.PriceType) return;
		try
		{
			var original = (await store.BusinessContextAsync("customer", row.Id)).Customers.FirstOrDefault(x => x.Id == row.Id) ?? throw new InvalidOperationException("Cliente removido. Atualize a lista.");
			await BusinessUi.ExecuteAsync(store, "customer.pricetype", row.Id, original, new CustomerPriceTypeArgs(option.Value), () => store.Update(d => { var c = d.Customers.First(x => x.Id == row.Id); c.PriceType = option.Value; c.UpdatedAt = DateTime.Now; return 0; }));
			row.PriceType = option.Value;
			Toast.Show(this, $"{row.Name}: {(option.Value.Length == 0 ? "preço normal (Balcão)" : "preço " + option.Value)}.", tone: "ok");
		}
		catch (Exception ex) { AlertWindow.Info(this, "Não foi possível mudar o tipo de preço", ex.Message); FillPriceType(row); }
	}

	private void OpenOrders_Click(object s, RoutedEventArgs e)
	{
		OpenSelectedOrders();
	}

	private async void RepeatLast_Click(object s, RoutedEventArgs e)
	{
		if (Grid.SelectedItem is not CustomerRow row) return;
		try
		{
			var id = store.IsRemote ? await store.QueryRemoteAsync<Guid?>($"desktop/customer-last-order/{row.Id}") : OrderRepeat.LastOf(store.Load(), row.Id)?.Id;
			if (id == null) { AlertWindow.Info(this, "Repetir pedido", "Nenhum pedido vinculado a este cadastro ou ao telefone dele."); return; }
			var dialog = await OrderRepeatUi.OpenAsync(this, store, id.Value);
			if (dialog == null) return;
			await BusinessUi.SaveOrderAsync(store, dialog.Result!);
			LoadData();
		}
		catch (Exception ex) { AlertWindow.Info(this, "Não foi possível repetir o pedido", ex.Message); }
	}

	private void OpenSelectedOrders()
	{
		if (Grid.SelectedItem is CustomerRow customerRow && (store.IsRemote||customerRow.Orders>0))
		{
			ScheduledOrdersWindow scheduledOrdersWindow = new ScheduledOrdersWindow(store, null, PhoneText.Digits(customerRow.Contact).Length >= 10 ? PhoneText.Digits(customerRow.Contact) : customerRow.Name);
			scheduledOrdersWindow.Owner = this;
			scheduledOrdersWindow.ShowDialog();
		}
	}
}
