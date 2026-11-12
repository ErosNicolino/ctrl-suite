using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace GraficaCaixa.Wpf;

public sealed partial class OwnerAreaWindow : Window
{
    bool writeBusy;
    readonly DataStore store;
    OwnerSession session;
    readonly TabControl tabs = new();
    readonly ComboBox period = new()
    {
        ItemsSource = new[] { "Hoje", "Semana", "Mês", "Período personalizado" },
        SelectedIndex = 2,
        MinWidth = 180
    };
    readonly DatePicker periodFrom = new() { SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Width = 175 };
    readonly DatePicker periodTo = new() { SelectedDate = DateTime.Today, Width = 175 };
    readonly TextBlock periodCaption = new();
    readonly TextBlock salesValue = MetricValue();
    readonly TextBlock receiptsValue = MetricValue();
    readonly TextBlock receivableValue = MetricValue();
    readonly TextBlock costsValue = MetricValue();
    readonly TextBlock expensesValue = MetricValue();
    readonly TextBlock resultValue = MetricValue();
    readonly TextBlock resultStatus = new();
    readonly TextBlock cashReceipts = FinanceValue();
    readonly TextBlock paidExpenses = FinanceValue();
    readonly TextBlock paidPurchases = FinanceValue();
    readonly TextBlock otherOutflows = FinanceValue();
    readonly TextBlock cashVariation = FinanceValue();

    public OwnerAreaWindow(DataStore store)
    {
        this.store = store;
        Title = "Área do Proprietário";
        // Telas do proprietário: indicadores, despesas e fluxo previsto (abas).
        Width = 1180;
        Height = 760;
        MinWidth = 930;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (!OwnerAuthentication.Ensure(this, store, allowSetup: true))
        {
            session = new OwnerSession(TimeSpan.Zero);
            Loaded += (_, _) => Close();
            return;
        }
        session = OwnerAuthentication.Session!;

        var root = new Grid { Margin = new Thickness(24, 20, 24, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.RowDefinitions.Add(new RowDefinition());
        Content = root;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Área do Proprietário", Style = (Style)FindResource("Title") });
        heading.Children.Add(new TextBlock { Text = "Indicadores gerenciais, despesas e fluxo previsto", Foreground = Brush("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        header.Children.Add(heading);
        var lockButton = Prompt12Ui.Button(this, "Bloquear", (_, _) => { session.Lock(); Close(); });
        lockButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lockButton, 1);
        header.Children.Add(lockButton);
        root.Children.Add(header);

        Grid.SetRow(tabs, 2);
        root.Children.Add(tabs);
        Build();
        Closed += (_, _) => session.Lock();
    }

    void Build()
    {
        tabs.Items.Clear();
        tabs.Items.Add(new TabItem { Header = "Visão geral", Content = OverviewTab() });
        tabs.Items.Add(new TabItem { Header = "Despesas", Content = ExpensesTab() });
        tabs.Items.Add(new TabItem { Header = "Fluxo previsto", Content = ForecastTab() });
        tabs.Items.Add(new TabItem { Header = "Limite do MEI", Content = meiView });
        tabs.Items.Add(new TabItem { Header = "Retrabalho", Content = reworkView });
        Refresh();
    }

    UIElement OverviewTab()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        scroll.Content = content;

        var filtersCard = Card();
        var filters = new Grid();
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filtersCard.Child = filters;
        var filterText = new StackPanel();
        filterText.Children.Add(Section("Período de análise"));
        periodCaption.Foreground = Brush("Muted");
        periodCaption.Margin = new Thickness(0, 3, 0, 0);
        filterText.Children.Add(periodCaption);
        filters.Children.Add(filterText);
        var controls = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        period.Margin = new Thickness(4);
        periodFrom.Margin = new Thickness(4);
        periodTo.Margin = new Thickness(4);
        controls.Children.Add(period);
        controls.Children.Add(periodFrom);
        controls.Children.Add(periodTo);
        controls.Children.Add(Prompt12Ui.Button(this, "Atualizar indicadores", (_, _) => Refresh(), primary: true));
        Grid.SetRow(controls, 1);
        filters.Children.Add(controls);
        content.Children.Add(filtersCard);

        var metricGrid = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(0, 12, 0, 0) };
        metricGrid.Children.Add(MetricCard("Faturamento", "Pedidos e lançamentos de balcão do período", salesValue, new Thickness(0, 0, 10, 10)));
        metricGrid.Children.Add(MetricCard("Recebido", "Entradas registradas", receiptsValue, new Thickness(0, 0, 10, 10)));
        metricGrid.Children.Add(MetricCard("A receber", "Saldo ativo de pedidos", receivableValue, new Thickness(0, 0, 0, 10)));
        metricGrid.Children.Add(MetricCard("Custos diretos", "Custos dos itens (pedidos e balcão)", costsValue, new Thickness(0, 0, 10, 0)));
        metricGrid.Children.Add(MetricCard("Despesas", "Competência no período", expensesValue, new Thickness(0, 0, 10, 0)));
        metricGrid.Children.Add(MetricCard("Lucro / resultado", "Resultado gerencial", resultValue));
        content.Children.Add(metricGrid);

        var statusCard = Card(new Thickness(0, 12, 0, 0));
        var statusStack = new StackPanel();
        statusCard.Child = statusStack;
        statusStack.Children.Add(Section("Leitura do resultado"));
        resultStatus.TextWrapping = TextWrapping.Wrap;
        resultStatus.Margin = new Thickness(0, 7, 0, 0);
        statusStack.Children.Add(resultStatus);
        content.Children.Add(statusCard);

        var financialCard = Card(new Thickness(0, 12, 0, 0));
        var financialStack = new StackPanel();
        financialCard.Child = financialStack;
        financialStack.Children.Add(Section("Resultado financeiro"));
        financialStack.Children.Add(new TextBlock { Text = "Movimentação efetiva dentro do período selecionado", Foreground = Brush("Muted"), FontSize = 12, Margin = new Thickness(0, 3, 0, 10) });
        var financialGrid = new UniformGrid { Columns = 5 };
        financialGrid.Children.Add(FinanceBlock("Entradas", cashReceipts));
        financialGrid.Children.Add(FinanceBlock("Despesas pagas", paidExpenses));
        financialGrid.Children.Add(FinanceBlock("Compras", paidPurchases));
        financialGrid.Children.Add(FinanceBlock("Demais saídas", otherOutflows));
        financialGrid.Children.Add(FinanceBlock("Resultado financeiro", cashVariation, accent: true));
        financialStack.Children.Add(financialGrid);
        content.Children.Add(financialCard);
        return scroll;
    }

    async void Refresh()
    {
        RefreshForecast();
        RefreshIndicators();
        RefreshSummary();
    }
    async void RefreshSummary()
    {
        await Prompt12Ui.Run(this,async () =>
        {
            var today = DateTime.Today;
            var range = period.SelectedIndex switch
            {
                0 => (today, today),
                1 => (today.AddDays((0 - ((int)today.DayOfWeek + 6)) % 7), today),
                2 => (new DateTime(today.Year, today.Month, 1), today),
                _ => (periodFrom.SelectedDate ?? today, periodTo.SelectedDate ?? today)
            };
            if (range.Item2 < range.Item1) throw new InvalidOperationException("O fim do período não pode ser anterior ao início.");
            OwnerSecurity.Require(session);
            var summary=store.IsRemote?await store.QueryRemoteAsync<OwnerSummary>($"desktop/owner/summary?from={range.Item1:yyyy-MM-dd}&to={range.Item2:yyyy-MM-dd}"):LocalSummary();
            OwnerSummary LocalSummary(){var data=store.Load();return new(PrivateFinance.Result(data,range.Item1,range.Item2,session),PrivateFinance.CashFlow(data,range.Item1,range.Item2,session),data.ScheduledOrders.Where(x=>LossOperations.IsNormalReceivable(data,x.Id)).Sum(x=>x.RemainingAmount));}
            var management=summary.Management;var cash=summary.Cash;var activeReceivable=summary.ActiveReceivable;

            periodCaption.Text = $"{range.Item1:dd/MM/yyyy} a {range.Item2:dd/MM/yyyy}";
            salesValue.Text = management.Sales.ToString("C2");
            receiptsValue.Text = cash.Receipts.ToString("C2");
            receivableValue.Text = activeReceivable.ToString("C2");
            costsValue.Text = management.DirectCosts.ToString("C2");
            expensesValue.Text = management.OperatingExpenses.ToString("C2");
            resultValue.Text = management.Result.ToString("C2");
            resultValue.Foreground = management.Result < 0 ? Brush("Danger") : Brush("Accent");
            resultStatus.Text = management.IsComplete
                ? $"Apuração completa no período. Lucro bruto: {management.GrossProfit:C2}. Perdas registradas: {management.Losses:C2}. Retrabalho: {management.Rework:C2}."
                : $"Resultado parcial: existem custos diretos ainda não conferidos. Lucro bruto apurado até aqui: {management.GrossProfit:C2}. Perdas registradas: {management.Losses:C2}. Retrabalho: {management.Rework:C2}.";
            resultStatus.Foreground = management.IsComplete ? Brush("Muted") : Brush("Warning");

            cashReceipts.Text = cash.Receipts.ToString("C2");
            paidExpenses.Text = cash.PaidExpenses.ToString("C2");
            paidPurchases.Text = cash.PaidPurchases.ToString("C2");
            otherOutflows.Text = cash.OtherOutflows.ToString("C2");
            cashVariation.Text = cash.Variation.ToString("C2");
            cashVariation.Foreground = cash.Variation < 0 ? Brush("Danger") : Brush("Accent");
        });
    }

    UIElement ExpensesTab()
    {
        var root = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition());

        var toolbarCard = Card();
        var toolbar = new Grid();
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbarCard.Child = toolbar;
        var searchArea = new StackPanel();
        searchArea.Children.Add(Section("Despesas"));
        var searchRow = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var search = new TextBox { Width = 310, ToolTip = "Pesquisar descrição, categoria ou observação" };
        var filter = new ComboBox
        {
            ItemsSource = new[] { "Todas (despesas + compras)", "Pendentes", "Pagas", "Vencidas", "Recorrentes", "Inativas", "Compras de material", "Só despesas" },
            SelectedIndex = 0,
            MinWidth = 150,
            Margin = new Thickness(8, 0, 0, 0)
        };
        searchRow.Children.Add(search);
        searchRow.Children.Add(filter);
        var listSummary = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), FontSize = 12.5 }; listSummary.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); searchRow.Children.Add(listSummary);
        var filterKeys = new[] { "all", "pending", "paid", "overdue", "recurring", "inactive", "purchases", "expenses" };
        void Summarize(IEnumerable<ExpenseRow> rows) { var list = rows.ToList(); var purchases = list.Where(x => x.Source.FromPurchase).Sum(x => x.Source.Amount); var pending = list.Where(x => !x.Source.FromPurchase && x.Source.Active && x.Source.Status == ExpenseStatus.Pending).Sum(x => x.Source.Amount); listSummary.Text = $"{list.Count} lançamento(s) · total {list.Sum(x => x.Source.Amount):C2}" + (pending > 0 ? $" · a pagar {pending:C2}" : "") + (purchases > 0 ? $" · compras de material {purchases:C2}" : ""); }
        searchArea.Children.Add(searchRow);
        toolbar.Children.Add(searchArea);

        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        CancellationTokenSource? expenseLoad=null;bool expenseBusy=false,expenseEnd=false;int expenseOffset=0;var expenseRows=new System.Collections.ObjectModel.ObservableCollection<ExpenseRow>();
        Button edit = null!, markPaid = null!, deactivate = null!;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        var add = Prompt12Ui.Button(this, "Nova despesa", (_, _) => AddExpense(), primary: true);
        edit = Prompt12Ui.Button(this, "Editar", (_, _) => EditExpense());
        markPaid = Prompt12Ui.Button(this, "Marcar paga", (_, _) => MarkPaid());
        deactivate = Prompt12Ui.Button(this, "Inativar", (_, _) => Deactivate());
        var recurringButton = Prompt12Ui.Button(this, "Lançar recorrentes", (_, _) => LaunchRecurring());
        recurringButton.ToolTip = "Cria as próximas despesas recorrentes (mensal, bimestral, trimestral, semestral, anual) que já venceram a competência";
        actions.Children.Add(add);
        actions.Children.Add(edit);
        actions.Children.Add(markPaid);
        actions.Children.Add(deactivate);
        actions.Children.Add(recurringButton);
        Grid.SetRow(actions, 1);
        toolbar.Children.Add(actions);
        root.Children.Add(toolbarCard);

        var gridCard = Card();
        gridCard.Padding = new Thickness(0);
        Grid.SetRow(gridCard, 2);
        root.Children.Add(gridCard);
        grid.Columns.Add(Column("Descrição", nameof(ExpenseRow.Description), 1.5, 180));
        grid.Columns.Add(Column("Categoria", nameof(ExpenseRow.Category), 1, 120));
        grid.Columns.Add(Column("Valor", nameof(ExpenseRow.Amount), .8, 100));
        grid.Columns.Add(Column("Competência", nameof(ExpenseRow.Competence), .85, 110));
        grid.Columns.Add(Column("Vencimento", nameof(ExpenseRow.DueDate), .85, 110));
        grid.Columns.Add(Column("Status", nameof(ExpenseRow.Status), .8, 100));
        grid.Columns.Add(Column("Pagamento", nameof(ExpenseRow.PaidAt), .9, 120));
        grid.Columns.Add(Column("Forma", nameof(ExpenseRow.PaymentMethod), 1, 120));
        grid.Columns.Add(Column("Recorrência", nameof(ExpenseRow.Recurrence), .8, 100));
        grid.Columns.Add(Column("Observações", nameof(ExpenseRow.Notes), 1.4, 160));
        gridCard.Child = grid;

        Expense? Selected() => (grid.SelectedItem as ExpenseRow)?.Source;
        void UpdateActions()
        {
            var selected = Selected();
            // Compras de material aparecem só para consulta: editar e pagar ficam em Compras.
            edit.IsEnabled = selected is { FromPurchase: false };
            markPaid.IsEnabled = selected is { FromPurchase: false, Active: true, Status: ExpenseStatus.Pending };
            deactivate.IsEnabled = selected is { FromPurchase: false, Active: true, Status: ExpenseStatus.Pending };
        }
        async void Load()
        {
            if(store.IsRemote){expenseLoad?.Cancel();expenseLoad=new();expenseOffset=0;expenseEnd=false;expenseRows.Clear();grid.ItemsSource=expenseRows;var reloadToken=expenseLoad.Token;while(expenseBusy){await Task.Delay(25);if(reloadToken.IsCancellationRequested)return;}await PageExpenses(reloadToken);UpdateActions();return;}
            var rows = ExpenseList.Filter(store.Load().Prompt12, filterKeys[Math.Max(0, filter.SelectedIndex)], search.Text.Trim()).Select(x => new ExpenseRow(x)).ToList();
            grid.ItemsSource = rows; Summarize(rows);
            UpdateActions();
        }
        async Task PageExpenses(CancellationToken ct){
            if(expenseBusy||expenseEnd)return;expenseBusy=true;
            try{var page=await store.QueryRemoteAsync<List<Expense>>($"desktop/owner/expenses?offset={expenseOffset}&filter={filterKeys[Math.Max(0,filter.SelectedIndex)]}&search={Uri.EscapeDataString(search.Text.Trim())}",ct);ct.ThrowIfCancellationRequested();expenseOffset+=page.Count;expenseEnd=page.Count<50;foreach(var row in page)expenseRows.Add(new ExpenseRow(row));Summarize(expenseRows);}
            catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Despesas",ex.Message);}finally{expenseBusy=false;}
        }
        grid.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler(async (_,e)=>{if(store.IsRemote&&expenseLoad!=null&&e.VerticalChange>0&&e.VerticalOffset+e.ViewportHeight>=e.ExtentHeight-60)await PageExpenses(expenseLoad.Token);}));
        Closed+=(_,_)=>expenseLoad?.Cancel();
        async void AddExpense()
        {
        if(writeBusy)return;writeBusy=true;try{
            var dialog = new ExpenseDialog { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            await BusinessUi.ExecuteAsync(store,"expense.add",dialog.Result.Id,null,new ExpenseArgs(dialog.Result),()=>store.Update(d => { PrivateFinance.AddExpense(d, dialog.Result, session, App.CurrentUser); return 0; }));
            Load(); Refresh();
        
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
        async void EditExpense()
        {
        if(writeBusy)return;writeBusy=true;try{
            var selected = Selected();
            if (selected is null) return;
            var dialog = new ExpenseDialog(selected) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null) return;
            await BusinessUi.ExecuteAsync(store,"expense.edit",selected.Id,selected,new ExpenseArgs(dialog.Result),()=>store.Update(d =>
            {
                OwnerSecurity.Require(session);
                var current = d.Prompt12.Expenses.Single(x => x.Id == selected.Id);
                var result = dialog.Result;
                result.Id = current.Id;
                result.Status = current.Status;
                result.PaidAt = current.PaidAt;
                result.Active = current.Active;
                d.Prompt12.Expenses[d.Prompt12.Expenses.IndexOf(current)] = result;
                AuditService.Record(d, App.CurrentUser, "Despesa alterada", "Despesa", result.Id.ToString(), "", "atualizada", "", result.Description);
                return 0;
            }));
            Load(); Refresh();
        
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
        // Recorrentes: mostra o que será criado (próximas competências de cada série) e grava uma por uma pelo servidor.
        async void LaunchRecurring()
        {
            if(writeBusy)return;writeBusy=true;
            try{
                var state=new Prompt12State();
                if(store.IsRemote){for(var offset=0;;){var page=await store.QueryRemoteAsync<List<Expense>>($"desktop/owner/expenses?offset={offset}&filter=recurring&search=");state.Expenses.AddRange(page);offset+=page.Count;if(page.Count<50)break;}}
                else state=store.Load().Prompt12;
                var due=ExpenseList.DueRecurring(state,DateTime.Today);
                if(due.Count==0){AlertWindow.Info(this,"Lançar recorrentes","Nenhuma despesa recorrente para lançar agora. As próximas aparecem quando chegar o mês da competência.");return;}
                var lines=string.Join("\n",due.Take(12).Select(x=>$"• {x.Description} — {x.Competence:MM/yyyy} — {x.Amount:C2} ({x.RecurrenceLabel})"))+(due.Count>12?$"\n… e mais {due.Count-12}":"");
                if(!AlertWindow.Confirm(this,"Lançar recorrentes",$"Criar {due.Count} despesa(s) pendente(s)?\n\n{lines}"))return;
                var created=0;
                foreach(var expense in due){try{await BusinessUi.ExecuteAsync(store,"expense.add",expense.Id,null,new ExpenseArgs(expense),()=>store.Update(d=>{PrivateFinance.AddExpense(d,expense,session,App.CurrentUser);return 0;}));created++;}catch(InvalidOperationException){}}
                Toast.Show(this,$"{created} despesa(s) recorrente(s) lançada(s).",tone:"ok");Load();Refresh();
            }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
        }
        async void MarkPaid()
        {
        if(writeBusy)return;writeBusy=true;try{
            var selected = Selected();
            if (selected is null) return;
            await Prompt12Ui.Run(this,async () =>
            {
                await BusinessUi.ExecuteAsync(store,"expense.pay",selected.Id,selected,new EmptyArgs(),()=>store.Update(d =>
                {
                    OwnerSecurity.Require(session);
                    var current = d.Prompt12.Expenses.Single(x => x.Id == selected.Id);
                    if (!current.Active) throw new InvalidOperationException("Despesa inativa.");
                    current.Status = ExpenseStatus.Paid;
                    current.PaidAt = DateTime.Now;
                    AuditService.Record(d, App.CurrentUser, "Despesa paga", "Despesa", current.Id.ToString(), "Pendente", "Paga", "", current.Description);
                    return 0;
                }));
                Load(); Refresh();
            });
        
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
        async void Deactivate()
        {
        if(writeBusy)return;writeBusy=true;try{
            var selected = Selected();
            if (selected is null) return;
            await Prompt12Ui.Run(this,async () =>
            {
                if (selected.Status == ExpenseStatus.Paid) throw new InvalidOperationException("Despesa paga deve permanecer no histórico.");
                await BusinessUi.ExecuteAsync(store,"expense.deactivate",selected.Id,selected,new EmptyArgs(),()=>store.Update(d =>
                {
                    OwnerSecurity.Require(session);
                    var current = d.Prompt12.Expenses.Single(x => x.Id == selected.Id);
                    current.Active = false;
                    AuditService.Record(d, App.CurrentUser, "Despesa inativada", "Despesa", current.Id.ToString(), "Ativa", "Inativa", "", current.Description);
                    return 0;
                }));
                Load(); Refresh();
            });
        
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

        search.TextChanged += (_, _) => Load();
        filter.SelectionChanged += (_, _) => Load();
        grid.SelectionChanged += (_, _) => UpdateActions();
        grid.MouseDoubleClick += (_, e) => { if (e.ChangedButton == MouseButton.Left) EditExpense(); };
        Load();
        return root;
    }

    DataGridTextColumn Column(string header, string path, double width, double minWidth) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width, DataGridLengthUnitType.Star),
        MinWidth = minWidth
    };

    Border Card(Thickness? margin = null) => new() { Style = (Style)FindResource("Card"), Margin = margin ?? new Thickness(0) };
    Border MetricCard(string title, string hint, TextBlock value, Thickness? margin = null)
    {
        var card = new Border { Style = (Style)FindResource("MetricCard"), Margin = margin ?? new Thickness(0) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brush("Muted"), FontSize = 11, FontWeight = FontWeights.SemiBold });
        value.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(value);
        stack.Children.Add(new TextBlock { Text = hint, Foreground = Brush("Muted"), FontSize=11, Margin = new Thickness(0, 4, 0, 0) });
        card.Child = stack;
        return card;
    }
    Border FinanceBlock(string title, TextBlock value, bool accent = false)
    {
        var border = new Border { BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 4, 12, 4) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Foreground = Brush("Muted"), FontSize=11, TextWrapping = TextWrapping.Wrap });
        if (accent) value.Foreground = Brush("Accent");
        stack.Children.Add(value);
        border.Child = stack;
        return border;
    }
    static TextBlock Section(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };
    static TextBlock MetricValue() => new() { Text = "R$ 0,00", FontSize = 22, FontWeight = FontWeights.SemiBold };
    static TextBlock FinanceValue() => new() { Text = "R$ 0,00", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
    System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);

    sealed class ExpenseRow
    {
        public Expense Source { get; }
        public string Description => Source.Description;
        public string Category => Source.Category;
        public string Amount => Source.Amount.ToString("C2");
        public string Competence => Source.Competence.ToString("MM/yyyy");
        public string DueDate => Source.DueDate.ToString("dd/MM/yyyy");
        public string Status => !Source.Active ? "Inativa" : Source.Status == ExpenseStatus.Paid ? "Paga" : Source.DueDate < DateTime.Today ? "Vencida" : "Pendente";
        public string PaidAt => Source.PaidAt?.ToString("dd/MM/yyyy") ?? "—";
        public string PaymentMethod => string.IsNullOrWhiteSpace(Source.PaymentMethod) ? "—" : Source.PaymentMethod;
        public string Recurrence => Source.FromPurchase ? "Compra" : Source.RecurrenceLabel;
        public string Notes => Source.Notes;
        public ExpenseRow(Expense source) => Source = source;
    }
}
