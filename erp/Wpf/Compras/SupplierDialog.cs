using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// Cadastro do fornecedor (laranja "Compra"): identificação, contato e como compro. Só o nome é obrigatório.
internal sealed class SupplierDialog : Window
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    public Supplier? Result;
    readonly Supplier? editing;
    readonly TextBox name = new() { Tag = "Ex.: Lavoro Papéis" }, document = new() { Tag = "CNPJ ou CPF" }, legalName = new() { Tag = "Como aparece na nota" };
    readonly TextBox contact = new() { Tag = "Ex.: Carla (vendas)" }, whatsapp = new() { Tag = "(11) 90000-0000" }, phone = new() { Tag = "(11) 0000-0000" }, email = new(), address = new();
    readonly TextBox supplies = new() { Tag = "Ex.: Papel, Adesivo, Vinil" }, leadTime = new() { Tag = "Ex.: 2 dias úteis" }, payment = new() { Tag = "Ex.: PIX ou boleto 28 dias" }, minimum = new() { Tag = "Opcional", TextAlignment = TextAlignment.Right };
    readonly TextBox notes = new() { Tag = "Ex.: pedir até 15h para entregar no dia seguinte" };
    readonly CheckBox active = new() { Content = "Ativo (aparece na lista ao criar uma compra)", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock sumName = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap }, sumSupplies = new() { TextWrapping = TextWrapping.Wrap }, sumLead = new(), sumPay = new() { TextWrapping = TextWrapping.Wrap }, sumWhats = new();
    readonly DialogKit.Field nameField, minimumField;

    public SupplierDialog(Supplier? existing = null, string? presetName = null)
    {
        editing = existing;
        Title = existing is null ? "Novo fornecedor" : "Editar fornecedor";
        Width = Math.Min(900, SystemParameters.WorkArea.Width - 32); SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        notes.Height = 64; notes.AcceptsReturn = true; notes.TextWrapping = TextWrapping.Wrap; notes.VerticalContentAlignment = VerticalAlignment.Top; notes.Padding = new Thickness(0, 6, 0, 6);
        nameField = DialogKit.LabeledField("Nome *", name); minimumField = DialogKit.LabeledField("Pedido mínimo (R$)", minimum);
        var body = new StackPanel();
        body.Children.Add(new SectionCard { Number = "1", Header = "Identificação", Hint = "o nome é o único obrigatório", Content = Row(new[] { 1.3, 1 }, nameField.Panel, DialogKit.LabeledField("CNPJ / CPF", document).Panel, DialogKit.LabeledField("Razão social", legalName).Panel) });
        var contactPanel = new StackPanel(); contactPanel.Children.Add(Row(new[] { 1d, 1, 1 }, DialogKit.LabeledField("Pessoa de contato", contact).Panel, DialogKit.LabeledField("WhatsApp", whatsapp).Panel, DialogKit.LabeledField("Telefone", phone).Panel));
        contactPanel.Children.Add(Row(new[] { 1d, 1 }, DialogKit.LabeledField("E-mail", email).Panel, DialogKit.LabeledField("Endereço", address).Panel));
        body.Children.Add(new SectionCard { Number = "2", Header = "Contato", IsOptional = true, Content = contactPanel, Margin = new Thickness(0, 12, 0, 0) });
        var buy = new StackPanel(); buy.Children.Add(DialogKit.LabeledField("O que fornece", supplies, "Separe por vírgula. Usado na busca e no filtro da tela Fornecedores.").Panel);
        buy.Children.Add(Row(new[] { 1d, 1.4, 1 }, DialogKit.LabeledField("Prazo de entrega", leadTime).Panel, DialogKit.LabeledField("Condição de pagamento", payment).Panel, minimumField.Panel));
        buy.Children.Add(DialogKit.LabeledField("Observação", notes).Panel); buy.Children.Add(active);
        body.Children.Add(new SectionCard { Number = "3", Header = "Como compro", Hint = "aparece na hora de comprar", Content = buy, Margin = new Thickness(0, 12, 0, 0) });

        var summary = new StackPanel();
        summary.Children.Add(DialogKit.Eyebrow("FORNECEDOR")); sumName.Margin = new Thickness(0, 4, 0, 2); summary.Children.Add(sumName);
        sumSupplies.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); summary.Children.Add(sumSupplies); summary.Children.Add(DialogKit.Rule());
        summary.Children.Add(DialogKit.Line("Entrega", sumLead, top: 0)); summary.Children.Add(DialogKit.Line("Pagamento", sumPay)); summary.Children.Add(DialogKit.Line("WhatsApp", sumWhats));
        summary.Children.Add(DialogKit.Hint("Inativo some da lista de escolha nas compras, mas o histórico de preços e gastos fica guardado."));

        Content = new DialogFrame { Kind = "Compra", IconName = "truck", Title = Title, Subtitle = "Enter avança · Ctrl+Enter salva · Esc fecha", Content = body, Aside = DialogKit.Summary(summary), AsideWidth = 250, Footer = DialogKit.Footer(this, existing is null ? "Salvar fornecedor" : "Salvar alterações", Save), ScrollBody = true };
        Fill(existing, presetName);
        foreach (var box in new[] { name, supplies, leadTime, payment, whatsapp }) box.TextChanged += (_, _) => Refresh();
        Refresh();
        KeyboardFlow.Attach(this, Save);
        Loaded += (_, _) => name.Focus();
    }

    // Linha de campos lado a lado; campos além do número de colunas descem para uma linha inteira.
    static FrameworkElement Row(double[] widths, params FrameworkElement[] fields)
    {
        var stack = new StackPanel(); var grid = new Grid(); stack.Children.Add(grid);
        for (var i = 0; i < widths.Length; i++) { if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], GridUnitType.Star) }); }
        for (var i = 0; i < fields.Length; i++) { if (i < widths.Length) { Grid.SetColumn(fields[i], i * 2); grid.Children.Add(fields[i]); } else stack.Children.Add(fields[i]); }
        return stack;
    }

    void Refresh()
    {
        sumName.Text = name.Text.Trim().Length == 0 ? "Nome do fornecedor" : name.Text.Trim(); sumName.Opacity = name.Text.Trim().Length == 0 ? .5 : 1;
        sumSupplies.Text = string.Join(" · ", SplitSupplies(supplies.Text));
        sumLead.Text = leadTime.Text.Trim().Length == 0 ? "Não informado" : leadTime.Text.Trim();
        sumPay.Text = payment.Text.Trim().Length == 0 ? "Não informado" : payment.Text.Trim();
        sumWhats.Text = whatsapp.Text.Trim().Length == 0 ? "Não informado" : whatsapp.Text.Trim();
    }

    static List<string> SplitSupplies(string text) => text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    void Fill(Supplier? s, string? presetName)
    {
        name.Text = s?.Name ?? presetName?.Trim() ?? ""; document.Text = s?.Document ?? ""; legalName.Text = s?.LegalName ?? "";
        contact.Text = s?.ContactPerson ?? ""; whatsapp.Text = s?.WhatsApp ?? ""; phone.Text = s?.Phone ?? ""; email.Text = s?.Email ?? ""; address.Text = s?.Address ?? "";
        supplies.Text = string.Join(", ", s?.Supplies ?? []); leadTime.Text = s?.LeadTime ?? ""; payment.Text = s?.PaymentTerms ?? "";
        minimum.Text = s?.MinimumOrder is decimal m ? m.ToString("N2", Pt) : ""; notes.Text = s?.Notes ?? ""; active.IsChecked = s?.Active ?? true;
    }

    void Save()
    {
        if (name.Text.Trim().Length == 0) { DialogKit.ShowError(nameField, name, "Informe o nome do fornecedor."); return; }
        decimal? min = null;
        if (minimum.Text.Trim().Length > 0)
        {
            if (!decimal.TryParse(minimum.Text.Replace("R$", "").Trim(), NumberStyles.Number, Pt, out var value) || value < 0) { DialogKit.ShowError(minimumField, minimum, "Informe um valor válido ou deixe em branco."); return; }
            min = decimal.Round(value, 2);
        }
        Result = new Supplier
        {
            Id = editing?.Id ?? Guid.NewGuid(), CreatedAt = editing?.CreatedAt ?? DateTime.Now,
            Name = name.Text.Trim(), Document = document.Text.Trim(), LegalName = legalName.Text.Trim(), ContactPerson = contact.Text.Trim(), WhatsApp = whatsapp.Text.Trim(), Phone = phone.Text.Trim(), Email = email.Text.Trim(), Address = address.Text.Trim(),
            Supplies = SplitSupplies(supplies.Text), LeadTime = leadTime.Text.Trim(), PaymentTerms = payment.Text.Trim(), MinimumOrder = min, Notes = notes.Text.Trim(), Active = active.IsChecked == true
        };
        DialogResult = true;
    }
}

// Gravação do fornecedor (servidor quando remoto, local quando não) e escolha na compra.
internal static class SupplierUi
{
    public static async Task<Supplier?> EditAsync(Window owner, DataStore store, Supplier? existing, string? presetName = null)
    {
        var dialog = new SupplierDialog(existing, presetName) { Owner = owner };
        if (dialog.ShowDialog() != true || dialog.Result is not Supplier supplier) return null;
        try
        {
            await BusinessUi.ExecuteAsync(store, "supplier.save", supplier.Id, existing, new SupplierArgs(supplier), () => store.Update(d =>
            {
                var text = SupplierBook.Save(d.Prompt12, supplier);
                AuditService.Record(d, App.CurrentUser, text.Before.Length == 0 ? "Fornecedor cadastrado" : "Fornecedor alterado", "Fornecedor", supplier.Id.ToString(), text.Before, text.After, "", supplier.Name);
                return 0;
            }));
            Toast.Show(owner, existing is null ? $"Fornecedor \"{supplier.Name}\" cadastrado." : "Fornecedor atualizado.", tone: "ok");
            return supplier;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException)
        { AlertWindow.Info(owner, "Não foi possível salvar o fornecedor", ex.Message); return null; }
    }
}

// Fornecedor na compra: lista dos cadastrados ativos com digitação livre. Nome igual a um cadastro liga automaticamente; nome novo salva como texto.
internal sealed class SupplierPicker : ComboBox
{
    public sealed record Option(Supplier? Supplier, string Name, string Label, bool IsCreate = false);
    readonly List<Option> all = [];
    readonly Prompt12State state;
    public Func<string, Task<Supplier?>>? CreateNew;
    public event Action? Changed;
    bool filtering;
    string lastTyped = "";

    public SupplierPicker(Prompt12State? state)
    {
        this.state = state ?? new Prompt12State();
        SetResourceReference(StyleProperty, typeof(ComboBox)); // subclasse não herda o estilo implícito do tema
        IsEditable = true; IsTextSearchEnabled = false; StaysOpenOnEdit = true;
        TextSearch.SetTextPath(this, nameof(Option.Name)); DisplayMemberPath = nameof(Option.Label);
        foreach (var s in (this.state.Suppliers ?? []).Where(x => x.Active).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) all.Add(new(s, s.Name, s.Supplies.Count > 0 ? $"{s.Name} · {string.Join(", ", s.Supplies)}" : s.Name));
        all.Add(new(null, "", "+ Cadastrar novo fornecedor", true));
        ItemsSource = all.ToList();
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Filter()));
        SelectionChanged += async (_, _) =>
        {
            if (SelectedItem is not Option { IsCreate: true }) return;
            var typed = lastTyped; SelectedIndex = -1;
            if (CreateNew == null) return;
            var created = await CreateNew(typed);
            if (created == null) return;
            state?.Suppliers?.Add(created);
            all.Insert(0, new(created, created.Name, created.Name)); ItemsSource = all.ToList(); Text = created.Name; Changed?.Invoke();
        };
    }

    public string Value { get => (Text ?? "").Trim(); set => Text = value; }
    public Guid? LinkedId => SupplierBook.Find(state, null, Value)?.Id;

    void Filter()
    {
        if (filtering) return; filtering = true;
        try
        {
            if (SelectedItem is not Option { IsCreate: true } && (Text ?? "").Trim().Length > 0) lastTyped = Text!.Trim();
            var term = SupplierBook.Key(Text);
            var list = all.Where(x => x.IsCreate || term.Length == 0 || SupplierBook.Key(x.Label).Contains(term)).ToList();
            var text = Text ?? ""; var box = Template?.FindName("PART_EditableTextBox", this) as TextBox; var caret = box?.CaretIndex ?? text.Length;
            ItemsSource = list; SelectedIndex = -1; Text = text; if (box != null) box.CaretIndex = Math.Min(caret, text.Length);
            if (IsKeyboardFocusWithin && term.Length > 0 && list.Count > 1) IsDropDownOpen = true;
        }
        finally { filtering = false; }
        Changed?.Invoke();
    }
}
