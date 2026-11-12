using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GraficaCaixa.Wpf;

public sealed class OrderFiscalWindow : Window
{
    public const string NationalIssuerUrl = "https://www.nfse.gov.br/EmissorNacional/Login?ReturnUrl=%2FEmissorNacional";

    readonly DataStore store;
    readonly ScheduledOrder order;
    Guid? customerId;string documentToCopy="";AppData fiscalContext=new();readonly CancellationTokenSource lifetime=new();bool refreshing,registering;
    readonly TextBlock customerName = ValueText();
    readonly TextBlock customerDocument = ValueText();
    readonly TextBlock customerEmail = ValueText();
    readonly TextBox fiscalDescription = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 104,
        MaxHeight = 150,
        VerticalContentAlignment = VerticalAlignment.Top,
        Padding = new Thickness(12, 9, 12, 9)
    };
    SectionCard? customerCard, historyCard;
    readonly EmptyState emptyNotes = new() { IconName = "inbox", Title = "Nenhuma nota registrada", Hint = "Depois de emitir no portal, use “Registrar NF emitida”.", Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly DataGrid grid = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = false,
        MinHeight = 170
    };

    public OrderFiscalWindow(DataStore store, ScheduledOrder order)
    {
        this.store = store;
        this.order = order;
        Title = $"NF / documentos - Pedido #{order.Number}";
        Width = 1040;
        Height = 720;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (!App.CurrentUser.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase))
        {
            Loaded += (_, _) =>
            {
                AlertWindow.Info(this, "Acesso restrito", "Somente usuário autorizado pode acessar arquivos fiscais.");
                Close();
            };
            return;
        }

        grid.Columns.Add(Column("Número", nameof(FiscalDocumentRow.Number), 1));
        grid.Columns.Add(Column("Data", nameof(FiscalDocumentRow.Date), .8));
        grid.Columns.Add(Column("Valor", nameof(FiscalDocumentRow.Amount), .9));
        grid.Columns.Add(Column("Tipo", nameof(FiscalDocumentRow.Type), 1));
        grid.Columns.Add(Column("Observação", nameof(FiscalDocumentRow.Notes), 1.7));
        grid.Columns.Add(Column("PDF", nameof(FiscalDocumentRow.Pdf), 1.2));

        // UX 2.0 (pacote 3, mock-up 9): o ERP não emite NF — prepara os dados, abre o emissor nacional e registra a nota emitida.
        Width = Math.Min(1120, SystemParameters.WorkArea.Width - 32); Height = Math.Min(780, SystemParameters.WorkArea.Height - 32); ShowInTaskbar = false;
        Button Small(string text, Action action, bool primary = false) { var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "SmallButton"), Margin = new Thickness(8, 0, 0, 0) }; if (primary) { b.MinHeight = 32; b.Padding = new Thickness(12, 4, 12, 4); b.FontSize = 13; } b.Click += (_, _) => action(); return b; }
        var content = new StackPanel();
        var customerGrid = new Grid();
        customerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        customerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        AddLabeled(customerGrid, 0, "Nome / razão social", customerName);
        AddLabeled(customerGrid, 1, "CPF / CNPJ", customerDocument);
        AddLabeled(customerGrid, 2, "E-mail", customerEmail);
        var customerTools = new StackPanel { Orientation = Orientation.Horizontal }; customerTools.Children.Add(Small("Editar cliente", EditCustomer)); customerTools.Children.Add(Small("Copiar CPF/CNPJ", CopyDocument));
        customerCard = new SectionCard { Number = "1", Header = "Dados do cliente", Content = customerGrid, HeaderTools = customerTools };
        content.Children.Add(customerCard);
        var descriptionTools = new StackPanel { Orientation = Orientation.Horizontal };
        descriptionTools.Children.Add(Small("Preparar dados para NF", () => Prepare())); descriptionTools.Children.Add(Small("Copiar descrição", CopyDescription)); descriptionTools.Children.Add(Small("Copiar valor", CopyValue)); descriptionTools.Children.Add(Small("Abrir emissor", OpenIssuer, primary: true));
        var descriptionStack = new StackPanel(); descriptionStack.Children.Add(DialogKit.Hint("Somente quantidade, descrição e subtotal de cada item (desconto rateado).", 0)); fiscalDescription.Margin = new Thickness(0, 8, 0, 0); descriptionStack.Children.Add(fiscalDescription);
        content.Children.Add(new SectionCard { Number = "2", Header = "Descrição para a NF", Content = descriptionStack, HeaderTools = descriptionTools });
        var historyTools = new StackPanel { Orientation = Orientation.Horizontal }; historyTools.Children.Add(Small("Abrir PDF", OpenPdf)); historyTools.Children.Add(Small("Registrar NF emitida", Register, primary: true));
        var historyHost = new Grid { Height = 200 }; historyHost.Children.Add(new Border { Style = (Style)FindResource("Panel"), Child = grid, ClipToBounds = true }); historyHost.Children.Add(emptyNotes);
        historyCard = new SectionCard { Number = "3", Header = "Notas registradas", Content = historyHost, HeaderTools = historyTools };
        content.Children.Add(historyCard);
        var summary = new StackPanel(); summary.Children.Add(DialogKit.Eyebrow("TOTAL DO PEDIDO")); summary.Children.Add(DialogKit.BigValue(order.OrderTotal.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"))));
        summary.Children.Add(DialogKit.Line("Recebido", order.PaidAmount.ToString("C2", CultureInfo.GetCultureInfo("pt-BR")))); summary.Children.Add(DialogKit.Line("Saldo", order.RemainingAmount.ToString("C2", CultureInfo.GetCultureInfo("pt-BR")), brushKey: order.RemainingAmount > 0 ? "Danger" : null));
        summary.Children.Add(DialogKit.Hint("1 Confira o cliente · 2 copie a descrição e abra o emissor · 3 registre a nota emitida (número, data, PDF)."));
        var footer = new StackPanel { Orientation = Orientation.Horizontal }; var close = new Button { Content = "Fechar", Style = (Style)FindResource("SoftButton") }; close.Click += (_, _) => Close(); footer.Children.Add(close);
        Content = new DialogFrame { Kind = "Recibo", IconName = "file-text", Title = $"Nota fiscal · Pedido #{order.Number}", Subtitle = "Emissão no Emissor Nacional NFS-e · aqui você prepara e registra · Esc fecha", Content = content, Aside = DialogKit.Summary(summary), AsideWidth = 290, Footer = footer };        Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>lifetime.Cancel();
    }

    static DataGridTextColumn Column(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width, DataGridLengthUnitType.Star)
    };

    Border Card(Thickness? margin = null) => new()
    {
        Style = (Style)FindResource("Card"),
        Margin = margin ?? new Thickness(0)
    };

    static TextBlock Section(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold };
    static TextBlock ValueText() => new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    System.Windows.Media.Brush ResourceBrush(string key) => (System.Windows.Media.Brush)FindResource(key);

    static void AddLabeled(Grid grid, int column, string label, TextBlock value)
    {
        var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 14, 0, 0, 0) };
        var caption = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold }; caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); panel.Children.Add(caption);
        value.Margin = new Thickness(0, 5, 0, 0);
        panel.Children.Add(value);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    AppData FiscalData()=>store.IsRemote?fiscalContext:store.Load();
    async void Refresh()
    {
        if(refreshing)return;refreshing=true;try{
        if(store.IsRemote)fiscalContext=await store.QueryRemoteAsync<AppData>($"desktop/context/fiscal/{order.Id}",lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();Prepare(showConfirmation:false);
        grid.ItemsSource = FiscalData().Prompt12.FiscalDocuments
            .Where(x => x.OrderId == order.Id)
            .OrderByDescending(x => x.Date)
            .Select(x => new FiscalDocumentRow(x))
            .ToList();
        emptyNotes.Visibility = ((System.Collections.ICollection)grid.ItemsSource).Count == 0 ? Visibility.Visible : Visibility.Collapsed; if (historyCard != null) historyCard.IsDone = emptyNotes.Visibility != Visibility.Visible;
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Dados fiscais",ex.Message);}finally{refreshing=false;}
    }

    void Prepare(bool showConfirmation = true)
    {
        Prompt12Ui.Run(this, () =>
        {
            var data = FiscalData();
            var currentOrder = data.ScheduledOrders.FirstOrDefault(x => x.Id == order.Id) ?? order;
            var profile = (customerId.HasValue ? data.Customers.FirstOrDefault(x => x.Id == customerId.Value) : null)
                ?? CustomerDirectory.Resolve(data, currentOrder.CustomerName, currentOrder.Contact);
            customerId = profile?.Id;
            customerName.Text = string.IsNullOrWhiteSpace(profile?.Name) ? currentOrder.CustomerName : profile.Name;
            customerDocument.Text = Display(profile?.CpfCnpj); documentToCopy = CopyableDocument(profile?.CpfCnpj);
            customerEmail.Text = Display(profile?.Email);
            fiscalDescription.Text = BuildFiscalDescription(order);
            if (customerCard != null) customerCard.IsDone = !string.IsNullOrWhiteSpace(profile?.CpfCnpj);
            if (showConfirmation)
                Toast.Show(this, "Dados do cliente e descrição atualizados para conferência.", tone: "ok");
        });
    }

    void EditCustomer()
    {
        Prompt12Ui.Run(this, () =>
        {
            var data = FiscalData();
            var currentOrder = data.ScheduledOrders.FirstOrDefault(x => x.Id == order.Id) ?? order;
            var original = (customerId.HasValue ? data.Customers.FirstOrDefault(x => x.Id == customerId.Value) : null)
                ?? CustomerDirectory.Resolve(data, currentOrder.CustomerName, currentOrder.Contact);
            if (original is null) throw new InvalidOperationException("Cliente não encontrado no cadastro. Confira o cliente do pedido.");
            var editor = new CustomerEditor(original)
            {
                Owner = this,
                PersistAsync = async (name, phone, email, cpf, address) => { if(store.IsRemote)await store.RemoteDesktopWriteAsync($"desktop/customers/{original.Id}",new DesktopCustomerCommand(Guid.NewGuid(),DataStore.DesktopRevision(original),name,phone,email,cpf,address),lifetime.Token);else store.Update(d =>
                {
                    CustomerDirectory.Edit(d, original, name, phone);
                    var customer = d.Customers.First(x => x.Id == original.Id);
                    customer.Email = email;
                    customer.CpfCnpj = cpf;
                    customer.Address = address;
                    AuditService.Record(d, App.CurrentUser, "Dados fiscais do cliente atualizados", "Cliente", customer.Id.ToString(), "", "dados opcionais atualizados", "", "");
                    return 0;
                }); }
            };
            if (editor.ShowDialog() == true)
            {
                customerId = original.Id;
                Refresh();
            }
        });
    }

    void CopyDescription()
    {
        Prompt12Ui.Run(this, () =>
        {
            if (string.IsNullOrWhiteSpace(fiscalDescription.Text)) Prepare(showConfirmation: false);
            Clipboard.SetText(fiscalDescription.Text);
            Toast.Show(this, "Descrição para a NF copiada.", tone: "ok");
        });
    }

    void CopyDocument()
    {
        Prompt12Ui.Run(this, () =>
        {
            if (documentToCopy.Length == 0) throw new InvalidOperationException("Cliente sem CPF/CNPJ no cadastro. Use Editar cliente.");
            Clipboard.SetText(documentToCopy);
            Toast.Show(this, "CPF/CNPJ copiado.", tone: "ok");
        });
    }

    void CopyValue()
    {
        Prompt12Ui.Run(this, () =>
        {
            if (order.OrderTotal <= 0) throw new InvalidOperationException("Este pedido está sem valor.");
            Clipboard.SetText(CopyableValue(order.OrderTotal));
            Toast.Show(this, "Valor copiado.", tone: "ok");
        });
    }

    void OpenIssuer() => Prompt12Ui.Run(this, () => Process.Start(new ProcessStartInfo(NationalIssuerUrl) { UseShellExecute = true }));

    async void Register()
    {
        if(registering)return;registering=true;try{
        var dialog = new OrderFiscalEntryDialog(order) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var context=store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/fiscal/{order.Id}",lifetime.Token):store.Load();var current=context.ScheduledOrders.Single(x=>x.Id==order.Id);
        // Mesma nota já registrada neste pedido (ex.: registrou de novo porque a lista não mostrava): pergunta antes de duplicar.
        if(context.Prompt12.FiscalDocuments.Any(x=>x.OrderId==order.Id&&x.Number.Trim().Equals(dialog.Result.Number,StringComparison.OrdinalIgnoreCase)&&x.Type.Trim().Equals(dialog.Result.Type,StringComparison.OrdinalIgnoreCase))&&!AlertWindow.Confirm(this,"NF já registrada",$"A nota {dialog.Result.Number} ({dialog.Result.Type}) já está registrada neste pedido. Registrar de novo?"))return;
        await BusinessUi.ExecuteAsync(store,"fiscal.register",order.Id,current,new FiscalArgs(dialog.Result),()=>store.Update(d =>
        {
            d.Prompt12.FiscalDocuments.Add(dialog.Result);
            AuditService.Record(d, App.CurrentUser, "Documento fiscal informado", "Documento fiscal", dialog.Result.Id.ToString(), "", dialog.Result.Number, "", $"Pedido #{order.Number}");
            return 0;
        }));
        Refresh();
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível registrar NF",ex.Message);}finally{registering=false;}
    }

    void OpenPdf()
    {
        Prompt12Ui.Run(this, () =>
        {
            if (grid.SelectedItem is not FiscalDocumentRow row) throw new InvalidOperationException("Selecione um documento.");
            if (string.IsNullOrWhiteSpace(row.Source.PdfPath) || !File.Exists(row.Source.PdfPath))
                throw new FileNotFoundException("PDF não informado ou não encontrado.", row.Source.PdfPath);
            Process.Start(new ProcessStartInfo(row.Source.PdfPath) { UseShellExecute = true });
        });
    }

    static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "Não informado" : value.Trim();

    // Para colar no portal: sem pontuação. Mantém letras porque o CNPJ novo pode ser alfanumérico.
    public static string CopyableDocument(string? value) => new string((value ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
    public static string CopyableValue(decimal value) => value.ToString("F2", CultureInfo.GetCultureInfo("pt-BR"));

    public static string BuildFiscalDescription(ScheduledOrder order)
    {
        var items = order.Items.Count > 0
            ? order.Items
            : [new OrderItem { Quantity = 1, Product = order.Description }];
        var subtotals = FiscalSubtotals(order, items);
        return string.Join(Environment.NewLine, items.Select((item, index) =>
            $"{item.Quantity} - {item.Product.Trim()} - {(subtotals[index].HasValue ? subtotals[index]!.Value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR")) : "—")}"));
    }

    static IReadOnlyList<decimal?> FiscalSubtotals(ScheduledOrder order, IReadOnlyList<OrderItem> items)
    {
        if (items.Count == 1) return [order.OrderTotal];
        if (items.Any(x => !x.LineTotal.HasValue)) return items.Select(x => x.LineTotal).ToList();
        var original = items.Sum(x => x.LineTotal!.Value);
        if (original <= 0) return items.Select(x => x.LineTotal).ToList();
        var result = new List<decimal?>();
        decimal allocated = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var value = i == items.Count - 1
                ? order.OrderTotal - allocated
                : decimal.Round(order.OrderTotal * items[i].LineTotal!.Value / original, 2, MidpointRounding.AwayFromZero);
            result.Add(value);
            allocated += value;
        }
        return result;
    }

    sealed class FiscalDocumentRow
    {
        public FiscalDocument Source { get; }
        public string Number => Source.Number;
        public string Date => Source.Date.ToString("dd/MM/yyyy");
        public string Amount => Source.Amount.ToString("C2");
        public string Type => Source.Type;
        public string Notes => Source.Notes;
        public string Pdf => string.IsNullOrWhiteSpace(Source.PdfPath) ? "Não anexado" : Path.GetFileName(Source.PdfPath);
        public FiscalDocumentRow(FiscalDocument source) => Source = source;
    }
}
