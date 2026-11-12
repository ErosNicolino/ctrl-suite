using System.Collections.ObjectModel;

namespace GraficaCaixa.Wpf;

public partial class LostEntriesWindow : System.Windows.Window
{
    readonly GraficaCaixa.DataStore store;readonly CancellationTokenSource lifetime=new();
    readonly ObservableCollection<LostEntryRow> rows = [];
    List<LostEntryRow> allRows = [];

    public LostEntriesWindow(GraficaCaixa.DataStore store)
    {
        InitializeComponent();
        this.store = store;
        Grid.ItemsSource = rows;
        Loaded += (_, _) => {LoadData();SearchBox.Focus();};Closed+=(_,_)=>lifetime.Cancel();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.N&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;Add_Click(this,new System.Windows.RoutedEventArgs());}};
    }

    async void LoadData()
    {
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,"Muted");StatusText.Text="Carregando…";
        try{var data = await store.BusinessContextAsync("lost",cancellation:lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();
        data.LostEntries ??= [];
        allRows = data.LostEntries.OrderByDescending(x => x.EntryDate).Select(x => new LostEntryRow(x)).ToList();
        StatusText.Text="Se existir um fechamento na data, o lançamento entra nele e o PDF do fechamento é atualizado.";
        ApplyFilter();
        }catch(OperationCanceledException){}catch(Exception ex){StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,"Danger");StatusText.Text="Não foi possível carregar: "+ex.Message;}
    }
    void Grid_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)=>EditButton.IsEnabled=RemoveButton.IsEnabled=Grid.SelectedItem!=null;
    void Grid_PreviewKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(Grid.SelectedItem==null)return;
        if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;EditSelected();}
        else if(e.Key==System.Windows.Input.Key.Delete){e.Handled=true;Remove_Click(sender,new System.Windows.RoutedEventArgs());}
    }

    void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();
    void ApplyFilter()
    {
        if (SearchBox is null) return;
        var query = SearchSaleRow.Normalize(SearchBox.Text);
        var filtered = string.IsNullOrWhiteSpace(query) ? allRows : allRows.Where(x => x.SearchKey.Contains(query));
        rows.Clear();
        foreach (var row in filtered) rows.Add(row);
        SummaryText.Text = $"{rows.Count} registro(s)  •  Total {rows.Sum(x => x.Entry.Amount):C2}";
        EmptyView.Title = allRows.Count == 0 ? "Nenhum lançamento perdido" : "Nada encontrado";
        EmptyView.Hint = allRows.Count == 0 ? "Use Novo lançamento para registrar o primeiro." : "Tente outro nome, data, pagamento ou valor.";
        EmptyView.Visibility = rows.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    async void Add_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new LostEntryDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var sessionId=await BusinessUi.ExecuteAsync(store,"lost.add",dialog.Result!.Id,null,new LostSaveArgs(dialog.Result!),()=>store.Update(data => PlaceInClosingOrLost(data,dialog.Result!,"Lançamento perdido criado")));
            if(sessionId is Guid id){var data=await store.BusinessContextAsync("closed",id);var session=data.Sessions.First(x=>x.Id==id);if(!session.IsOpen)GraficaCaixa.ReportWriter.Write(session,data.CashClosureVersions.Where(x=>x.SessionId==id).OrderBy(x=>x.Version).LastOrDefault());Toast.Show(this,$"Lançamento incluído no fechamento de {session.OpenedAt:dd/MM/yyyy}. PDF atualizado.",tone:"ok");}else Toast.Show(this,"Lançamento guardado. Não há fechamento nessa data.",tone:"ok");
            LoadData();
        }
        catch (Exception ex) { AlertWindow.Info(this, "Não foi possível salvar", ex.Message); }
    }

    void Edit_Click(object sender, System.Windows.RoutedEventArgs e) => EditSelected();
    void Grid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => EditSelected();
    async void EditSelected()
    {
        if (Grid.SelectedItem is not LostEntryRow row) { AlertWindow.Info(this, "Selecione um lançamento", "Escolha uma linha para editar."); return; }
        var dialog = new LostEntryDialog(row.Entry) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await BusinessUi.ExecuteAsync(store,"lost.edit",row.Entry.Id,row.Entry,new LostSaveArgs(dialog.Result!),()=>store.Update(data => { data.LostEntries ??= []; var index = data.LostEntries.FindIndex(x => x.Id == row.Entry.Id); if (index < 0) throw new InvalidOperationException("O lançamento não existe mais."); data.LostEntries[index] = dialog.Result!; AddAudit(data, "Lançamento perdido editado", dialog.Result!); return 0; }));
            LoadData();Toast.Show(this,"Lançamento atualizado.",tone:"ok");
        }
        catch (Exception ex) { AlertWindow.Info(this, "Não foi possível editar", ex.Message); }
    }

    async void Remove_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not LostEntryRow row) { AlertWindow.Info(this, "Selecione um lançamento", "Escolha uma linha para remover."); return; }
        if (!AlertWindow.Confirm(this, "Remover lançamento perdido", $"Remover o registro de {row.Entry.CustomerName}, no valor de {row.Entry.Amount:C2}?", true)) return;
        try
        {
            await BusinessUi.ExecuteAsync(store,"lost.delete",row.Entry.Id,row.Entry,new EmptyArgs(),()=>store.Update(data => { data.LostEntries ??= []; var entry = data.LostEntries.FirstOrDefault(x => x.Id == row.Entry.Id) ?? throw new InvalidOperationException("O lançamento não existe mais."); data.LostEntries.Remove(entry); AddAudit(data, "Lançamento perdido removido", entry); return 0; }));
            LoadData();Toast.Show(this,"Lançamento removido.",tone:"ok");
        }
        catch (Exception ex) { AlertWindow.Info(this, "Não foi possível remover", ex.Message); }
    }

    static void AddAudit(GraficaCaixa.AppData data, string action, GraficaCaixa.LostEntry entry)
    {
        data.AuditTrail ??= [];
        data.AuditTrail.Add(new GraficaCaixa.AuditEntry { Action = action, Description = $"{entry.CustomerName} — {entry.PaymentType} — {entry.Amount:C2}",UserName=App.CurrentUser.UserName,EntityType="Lançamento perdido",EntityId=entry.Id.ToString() });
    }

    static Guid? PlaceInClosingOrLost(GraficaCaixa.AppData data,GraficaCaixa.LostEntry entry,string action)
    {
        var session=data.Sessions.Where(x=>x.OpenedAt.Date==entry.EntryDate.Date||x.ClosedAt?.Date==entry.EntryDate.Date).OrderByDescending(x=>x.OpenedAt).FirstOrDefault();
        if(session==null){data.LostEntries??=[];data.LostEntries.Add(entry);AddAudit(data,action+" (sem fechamento na data)",entry);return null;}
        var sale=new GraficaCaixa.Sale{CreatedAt=entry.EntryDate,CustomerName=entry.CustomerName,Contact=entry.Contact,Product=entry.Description,Units=1,PaymentType=entry.PaymentType,TotalValue=entry.Amount};GraficaCaixa.FinancialOperations.AddSale(data,sale,App.CurrentUser,session.IsOpen?null:session.Id,session.IsOpen?"":$"Lançamento perdido informado: {entry.Description}");return session.Id;
    }
}

public sealed class LostEntryRow
{
    public GraficaCaixa.LostEntry Entry { get; }
    public string Date => Entry.EntryDate.ToString("dd/MM/yyyy HH:mm");
    public string Customer => Entry.CustomerName;
    public string Contact => PhoneText.Format(Entry.Contact);
    public string Description => Entry.Description;
    public string Payment => Entry.PaymentType;
    public string Amount => Entry.Amount.ToString("C2");
    public string SearchKey { get; }
    public LostEntryRow(GraficaCaixa.LostEntry entry) { Entry = entry; SearchKey = SearchSaleRow.Normalize($"{Date} {Customer} {Contact} {Description} {Payment} {Amount} {entry.Amount:0.00}"); }
}
