using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GraficaCaixa.Wpf;

// Retrabalho e serviços terceirizados do pedido. Nada aqui muda valor, etapa ou prazo do pedido.
public sealed class OrderExtrasWindow : Window
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    readonly DataStore store; readonly Guid orderId;
    readonly DataGrid reworks = Grid(), outsourced = Grid();
    readonly EmptyState reworksEmpty = Empty("Nenhum retrabalho neste pedido"), outsourcedEmpty = Empty("Nenhum serviço terceirizado neste pedido");
    readonly DataGridTextColumn reworkCost = Column("Custo", nameof(ReworkLine.Cost), .8), outsourcedCost = Column("Custo", nameof(OutsourcedLine.Cost), .8);
    OrderExtrasView view = new([], [], [], false);

    static DataGrid Grid() => new() { IsReadOnly = true, AutoGenerateColumns = false, MinHeight = 120, SelectionMode = DataGridSelectionMode.Single };
    static EmptyState Empty(string title) => new() { IconName = "inbox", Title = title, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    static DataGridTextColumn Column(string header, string path, double width) => new() { Header = header, Binding = new Binding(path), Width = new DataGridLength(width, DataGridLengthUnitType.Star) };

    public OrderExtrasWindow(DataStore store, ScheduledOrder order)
    {
        this.store = store; orderId = order.Id;
        Title = $"Retrabalho e terceiros - Pedido #{order.Number}"; Width = Math.Min(980, SystemParameters.WorkArea.Width - 32); Height = Math.Min(680, SystemParameters.WorkArea.Height - 32); WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Button Small(string text, Func<Task> action, bool primary = false) { var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "SmallButton"), Margin = new Thickness(8, 0, 0, 0) }; if (primary) { b.MinHeight = 32; b.Padding = new Thickness(12, 4, 12, 4); b.FontSize = 13; } b.Click += async (_, _) => await Prompt12Ui.Run(this, action); return b; }
        reworks.Columns.Add(Column("Data", nameof(ReworkLine.Date), .7)); reworks.Columns.Add(Column("Motivo", nameof(ReworkLine.Reason), 2.2)); reworks.Columns.Add(reworkCost); reworks.Columns.Add(Column("Registrado por", nameof(ReworkLine.User), 1));
        outsourced.Columns.Add(Column("Serviço", nameof(OutsourcedLine.Service), 1.5)); outsourced.Columns.Add(Column("Fornecedor", nameof(OutsourcedLine.Supplier), 1.2)); outsourced.Columns.Add(Column("Enviado", nameof(OutsourcedLine.Sent), .7)); outsourced.Columns.Add(Column("Volta prevista", nameof(OutsourcedLine.Expected), .8)); outsourced.Columns.Add(Column("Situação", nameof(OutsourcedLine.Status), .9)); outsourced.Columns.Add(outsourcedCost);
        var reworkTools = new StackPanel { Orientation = Orientation.Horizontal }; reworkTools.Children.Add(Small("Excluir", DeleteRework)); reworkTools.Children.Add(Small("Editar", EditRework)); reworkTools.Children.Add(Small("Registrar retrabalho", () => ShowRework(null), true));
        var outsourcedTools = new StackPanel { Orientation = Orientation.Horizontal }; outsourcedTools.Children.Add(Small("Excluir", DeleteOutsourced)); outsourcedTools.Children.Add(Small("Editar", EditOutsourced)); outsourcedTools.Children.Add(Small("Marcar que voltou", ReturnOutsourced)); outsourcedTools.Children.Add(Small("Registrar envio", () => ShowOutsourced(null), true));
        var content = new StackPanel();
        content.Children.Add(new SectionCard { Header = "Retrabalho", Content = Host(reworks, reworksEmpty), HeaderTools = reworkTools, Hint = "O custo entra no resultado do mês em que foi registrado." });
        content.Children.Add(new SectionCard { Header = "Serviços terceirizados", Content = Host(outsourced, outsourcedEmpty), HeaderTools = outsourcedTools, Hint = "O custo não soma no pedido." });
        var footer = new StackPanel { Orientation = Orientation.Horizontal }; var close = new Button { Content = "Fechar", Style = (Style)FindResource("SoftButton") }; close.Click += (_, _) => Close(); footer.Children.Add(close);
        Content = new DialogFrame { Kind = "Neutro", IconName = "file-text", Title = $"Retrabalho e terceiros · Pedido #{order.Number}", Subtitle = "Não muda valor, etapa nem prazo do pedido · Esc fecha", Content = content, Footer = footer };
        Loaded += async (_, _) => await Prompt12Ui.Run(this, Reload);
    }
    Grid Host(DataGrid grid, EmptyState empty) { var host = new Grid { Height = 150 }; host.Children.Add(new Border { Style = (Style)FindResource("Panel"), Child = grid, ClipToBounds = true }); host.Children.Add(empty); return host; }
    async Task Reload()
    {
        view = store.IsRemote ? await store.QueryRemoteAsync<OrderExtrasView>($"desktop/order-extras/{orderId}") : OrderExtras.View(store.Load(), orderId, _ => true);
        reworkCost.Visibility = outsourcedCost.Visibility = view.CanSeeCost ? Visibility.Visible : Visibility.Collapsed;
        reworks.ItemsSource = view.Reworks.Select(x => new ReworkLine(x)).ToList(); outsourced.ItemsSource = view.Outsourced.Select(x => new OutsourcedLine(x)).ToList();
        reworksEmpty.Visibility = view.Reworks.Count == 0 ? Visibility.Visible : Visibility.Collapsed; outsourcedEmpty.Visibility = view.Outsourced.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    async Task Run(string action, object args, Action local)
    {
        var data = store.IsRemote ? await store.QueryRemoteAsync<AppData>($"desktop/context/order/{orderId}") : store.Load();
        var order = data.ScheduledOrders.FirstOrDefault(x => x.Id == orderId) ?? throw new InvalidOperationException("Pedido não encontrado. Atualize a tela.");
        await BusinessUi.ExecuteAsync(store, action, orderId, order, args, () => { local(); return 0; });
        await Reload();
    }
    Task ShowRework(OrderRework? old)
    {
        var dialog = new ReworkDialog(old, view.CanSeeCost) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not OrderRework result) return Task.CompletedTask;
        return Run("rework.save", new ReworkArgs(result), () => store.Update(d => { OrderExtras.SaveRework(d, orderId, result, true, App.CurrentUser); return 0; }));
    }
    Task ShowOutsourced(OutsourcedService? old)
    {
        var dialog = new OutsourcedDialog(old, view.Suppliers, view.CanSeeCost) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not OutsourcedService result) return Task.CompletedTask;
        return Run("outsourced.save", new OutsourcedArgs(result), () => store.Update(d => { OrderExtras.SaveOutsourced(d, orderId, result, true, App.CurrentUser); return 0; }));
    }
    OrderRework SelectedRework() => (reworks.SelectedItem as ReworkLine)?.Source ?? throw new InvalidOperationException("Selecione um retrabalho na lista.");
    OutsourcedService SelectedOutsourced() => (outsourced.SelectedItem as OutsourcedLine)?.Source ?? throw new InvalidOperationException("Selecione um serviço na lista.");
    Task EditRework() => ShowRework(SelectedRework());
    Task EditOutsourced() => ShowOutsourced(SelectedOutsourced());
    Task DeleteRework()
    {
        var item = SelectedRework(); if (!AlertWindow.Confirm(this, "Excluir retrabalho", $"Excluir \"{item.Reason}\"? O custo sai do resultado do mês.")) return Task.CompletedTask;
        return Run("rework.delete", new OrderExtraArgs(item.Id), () => store.Update(d => { OrderExtras.DeleteRework(d, orderId, item.Id, App.CurrentUser); return 0; }));
    }
    Task DeleteOutsourced()
    {
        var item = SelectedOutsourced(); if (!AlertWindow.Confirm(this, "Excluir serviço", $"Excluir \"{item.Service}\"?")) return Task.CompletedTask;
        return Run("outsourced.delete", new OrderExtraArgs(item.Id), () => store.Update(d => { OrderExtras.DeleteOutsourced(d, orderId, item.Id, App.CurrentUser); return 0; }));
    }
    Task ReturnOutsourced()
    {
        var item = SelectedOutsourced(); if (item.ReturnedAt != null) throw new InvalidOperationException("Este serviço já voltou.");
        return Run("outsourced.return", new OrderExtraArgs(item.Id), () => store.Update(d => { OrderExtras.ReturnOutsourced(d, orderId, item.Id, App.CurrentUser); return 0; }));
    }
    sealed class ReworkLine(OrderRework source)
    {
        public OrderRework Source => source;
        public string Date => source.Date.ToString("dd/MM/yyyy");
        public string Reason => source.Reason;
        public string Cost => source.Cost.ToString("C2", Pt);
        public string User => source.UserName;
    }
    sealed class OutsourcedLine(OutsourcedService source)
    {
        public OutsourcedService Source => source;
        public string Service => source.Service;
        public string Supplier => source.Supplier;
        public string Sent => source.SentAt.ToString("dd/MM/yyyy");
        public string Expected => source.ExpectedBack?.ToString("dd/MM/yyyy") ?? "—";
        public string Status => source.ReturnedAt is DateTime back ? $"Voltou {back:dd/MM}" : source.ExpectedBack?.Date < DateTime.Today ? "Atrasado" : "Fora";
        public string Cost => source.Cost.ToString("C2", Pt);
    }
}

public sealed class ReworkDialog : Window
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    readonly OrderRework? old; readonly bool canCost; readonly Guid id;
    readonly TextBox reason = new() { Height = 74, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 8, 0, 8), Tag = "Ex.: corte torto, impressão com cor errada" }, cost = new() { TextAlignment = TextAlignment.Right };
    readonly DialogKit.Field reasonField, costField;
    public OrderRework? Result { get; private set; }
    public ReworkDialog(OrderRework? old, bool canCost)
    {
        this.old = old; this.canCost = canCost; id = old?.Id ?? Guid.NewGuid();
        Title = "Retrabalho"; Width = Math.Min(560, SystemParameters.WorkArea.Width - 32); SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        reason.Text = old?.Reason ?? ""; cost.Text = old == null ? "" : old.Cost.ToString("0.00", Pt);
        var body = new StackPanel(); reasonField = DialogKit.LabeledField("Motivo", reason); body.Children.Add(reasonField.Panel);
        costField = DialogKit.LabeledField("Custo para refazer (R$)", cost, "Entra no resultado do mês em que foi registrado."); if (canCost) body.Children.Add(costField.Panel);
        Content = new DialogFrame { Kind = "Neutro", IconName = "file-text", Title = old == null ? "Registrar retrabalho" : "Editar retrabalho", Subtitle = "Esc fecha", Content = body, Footer = DialogKit.Footer(this, "Salvar", Save), ScrollBody = false };
        Loaded += (_, _) => reason.Focus(); UnsavedChanges.Attach(this, Save);
    }
    void Save()
    {
        if (reason.Text.Trim().Length < 3) { DialogKit.ShowError(reasonField, reason, "Informe o motivo do retrabalho."); return; }
        decimal value = old?.Cost ?? 0;
        if (canCost && !(decimal.TryParse(string.IsNullOrWhiteSpace(cost.Text) ? "0" : cost.Text, NumberStyles.Currency, Pt, out value) && value >= 0)) { DialogKit.ShowError(costField, cost, "Informe o custo com número."); return; }
        Result = new OrderRework { Id = id, Reason = reason.Text.Trim(), Cost = value }; DialogResult = true;
    }
}

public sealed class OutsourcedDialog : Window
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    readonly OutsourcedService? old; readonly bool canCost; readonly Guid id;
    readonly TextBox service = new() { Tag = "Ex.: laminação, corte especial" }, cost = new() { TextAlignment = TextAlignment.Right };
    readonly ComboBox supplier = new() { IsEditable = true };
    readonly DatePicker expected = new();
    readonly DialogKit.Field serviceField, costField;
    public OutsourcedService? Result { get; private set; }
    public OutsourcedDialog(OutsourcedService? old, List<string> suppliers, bool canCost)
    {
        this.old = old; this.canCost = canCost; id = old?.Id ?? Guid.NewGuid();
        Title = "Serviço terceirizado"; Width = Math.Min(560, SystemParameters.WorkArea.Width - 32); SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        supplier.ItemsSource = suppliers; supplier.Text = old?.Supplier ?? ""; service.Text = old?.Service ?? ""; expected.SelectedDate = old?.ExpectedBack; cost.Text = old == null ? "" : old.Cost.ToString("0.00", Pt);
        var body = new StackPanel(); serviceField = DialogKit.LabeledField("Serviço", service); body.Children.Add(serviceField.Panel);
        body.Children.Add(DialogKit.LabeledField("Fornecedor", supplier).Panel);
        body.Children.Add(DialogKit.LabeledField("Volta prevista", expected, "Com data, o atraso aparece na fila Hoje.").Panel);
        costField = DialogKit.LabeledField("Custo (R$)", cost, "Só para registro: não soma no custo do pedido."); if (canCost) body.Children.Add(costField.Panel);
        Content = new DialogFrame { Kind = "Neutro", IconName = "file-text", Title = old == null ? "Registrar envio" : "Editar serviço", Subtitle = "Esc fecha", Content = body, Footer = DialogKit.Footer(this, "Salvar", Save), ScrollBody = false };
        Loaded += (_, _) => service.Focus(); UnsavedChanges.Attach(this, Save);
    }
    void Save()
    {
        if (service.Text.Trim().Length < 2) { DialogKit.ShowError(serviceField, service, "Informe o serviço terceirizado."); return; }
        decimal value = old?.Cost ?? 0;
        if (canCost && !(decimal.TryParse(string.IsNullOrWhiteSpace(cost.Text) ? "0" : cost.Text, NumberStyles.Currency, Pt, out value) && value >= 0)) { DialogKit.ShowError(costField, cost, "Informe o custo com número."); return; }
        Result = new OutsourcedService { Id = id, Service = service.Text.Trim(), Supplier = supplier.Text.Trim(), ExpectedBack = expected.SelectedDate, Cost = value }; DialogResult = true;
    }
}
