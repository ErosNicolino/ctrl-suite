using System.Globalization;

namespace GraficaCaixa.Wpf;

public partial class LostEntryDialog : System.Windows.Window
{
    readonly GraficaCaixa.LostEntry? editing;
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    public GraficaCaixa.LostEntry? Result { get; private set; }

    public LostEntryDialog(GraficaCaixa.LostEntry? entry = null)
    {
        InitializeComponent();UnsavedChanges.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()));
        KeyboardFlow.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()));
        PhoneInput.Attach(ContactBox);
        editing = entry;
        PaymentBox.ItemsSource = PaymentPolicy.Methods;
        PaymentBox.SelectedIndex = 0;
        var date = entry?.EntryDate ?? DateTime.Now;
        DateBox.SelectedDate = date.Date;
        TimeBox.Text = date.ToString("HH:mm");
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        if (entry is null) return;
        TitleText.Text = "Editar lançamento perdido";
        SaveButton.Content = "Salvar alterações";
        NameBox.Text = entry.CustomerName;
        ContactBox.Text = entry.Contact == "Não informado" ? "" : entry.Contact;
        DescriptionBox.Text = entry.Description == "Não informado" ? "" : entry.Description;
        PaymentBox.SelectedItem = entry.PaymentType;
        AmountBox.Text = entry.Amount.ToString("N2");
    }

    // Erro junto do campo (padrão UX): mensagem embaixo, borda de erro e foco no campo; some ao corrigir.
    static void Error(System.Windows.Controls.Control control, System.Windows.Controls.TextBlock label, string message)
    {
        label.Text = message; label.Visibility = System.Windows.Visibility.Visible; Ui.SetHasError(control, true); control.Focus();
        if (control is System.Windows.Controls.TextBox box) { box.SelectAll(); void Changed(object? s, System.Windows.Controls.TextChangedEventArgs e) { box.TextChanged -= Changed; label.Visibility = System.Windows.Visibility.Collapsed; Ui.SetHasError(box, false); } box.TextChanged += Changed; }
        else if (control is System.Windows.Controls.DatePicker picker) { void Picked(object? s, System.Windows.Controls.SelectionChangedEventArgs e) { picker.SelectedDateChanged -= Picked; label.Visibility = System.Windows.Visibility.Collapsed; Ui.SetHasError(picker, false); } picker.SelectedDateChanged += Picked; }
    }

    void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DateBox.SelectedDate is null) { Error(DateBox, DateError, "Escolha a data do lançamento."); return; }
        if (!TimeSpan.TryParse(TimeBox.Text, Pt, out var time)) { Error(TimeBox, TimeError, "Informe o horário no formato 08:30."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Error(NameBox, NameError, "Informe quem realizou o pagamento."); return; }
        if (!PhoneText.IsCompleteOrEmpty(ContactBox.Text) && PhoneText.Digits(ContactBox.Text) != PhoneText.Digits(editing?.Contact)) { AlertWindow.Info(this, "Telefone", PhoneText.IncompleteMessage); ContactBox.Focus(); return; }
        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Currency, Pt, out var amount) || amount <= 0) { Error(AmountBox, AmountError, "Informe um valor maior que zero."); return; }
        Result = new GraficaCaixa.LostEntry
        {
            Id = editing?.Id ?? Guid.NewGuid(),
            EntryDate = DateBox.SelectedDate.Value.Date.Add(time),
            CustomerName = NameBox.Text.Trim(),
            Contact = string.IsNullOrWhiteSpace(ContactBox.Text) ? "Não informado" : PhoneText.Normalize(ContactBox.Text),
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? "Não informado" : DescriptionBox.Text.Trim(),
            PaymentType = PaymentBox.SelectedItem?.ToString() ?? "Dinheiro",
            Amount = amount
        };
        DialogResult = true;
    }

    void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) => DialogResult = false;
}
