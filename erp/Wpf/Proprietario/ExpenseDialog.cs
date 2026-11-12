using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

internal sealed class ExpenseDialog : Window
{
    public Expense? Result;
    readonly TextBox description = new();
    readonly TextBox category = new() { Text = "Outras" };
    readonly TextBox amount = new();
    readonly DatePicker competence = new() { SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) };
    readonly DatePicker due = new() { SelectedDate = DateTime.Today };
    readonly ComboBox paymentMethod = new()
    {
        IsEditable = true,
        ItemsSource = PaymentPolicy.Methods.Prepend("").ToArray()
    };
    readonly TextBox notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 76, VerticalContentAlignment = VerticalAlignment.Top };
    sealed record Repeat(int Months, string Label) { public override string ToString() => Label; }
    static readonly Repeat[] Repeats = [new(0, "Não repete"), new(1, "Mensal"), new(2, "Bimestral (a cada 2 meses)"), new(3, "Trimestral (a cada 3 meses)"), new(6, "Semestral (a cada 6 meses)"), new(12, "Anual")];
    readonly ComboBox recurrence = new() { ItemsSource = Repeats, SelectedIndex = 0 };

    public ExpenseDialog(Expense? original = null)
    {
        Title = original is null ? "Nova despesa" : "Editar despesa";
        // UX 2.0 (pacote 3): diálogo central (cor da marca), mesmos campos e validações (Prompt12Ui).
        Width = Math.Min(680, SystemParameters.WorkArea.Width - 32);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var form = new StackPanel();
        form.Children.Add(DialogKit.LabeledField("Descrição *", description).Panel);
        var pair = new Grid(); pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); pair.ColumnDefinitions.Add(new ColumnDefinition());
        pair.Children.Add(DialogKit.LabeledField("Categoria", category).Panel); var valueCell = DialogKit.LabeledField("Valor (R$) *", amount).Panel; Grid.SetColumn(valueCell, 2); pair.Children.Add(valueCell); form.Children.Add(pair);
        var dates = new Grid(); dates.ColumnDefinitions.Add(new ColumnDefinition()); dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); dates.ColumnDefinitions.Add(new ColumnDefinition());
        dates.Children.Add(DialogKit.LabeledField("Competência *", competence, "Mês a que a despesa pertence").Panel); var dueCell = DialogKit.LabeledField("Vencimento *", due).Panel; Grid.SetColumn(dueCell, 2); dates.Children.Add(dueCell); form.Children.Add(dates);
        form.Children.Add(DialogKit.LabeledField("Forma de pagamento", paymentMethod).Panel);
        notes.Padding = new Thickness(0, 8, 0, 8); form.Children.Add(DialogKit.LabeledField("Observações", notes).Panel);
        form.Children.Add(DialogKit.LabeledField("Recorrência", recurrence, "Use \"Lançar recorrentes\" na lista para criar as próximas (ex.: semestral cria uma a cada 6 meses).").Panel);
        amount.TextAlignment = TextAlignment.Right;

        if (original is not null)
        {
            description.Text = original.Description;
            category.Text = original.Category;
            amount.Text = original.Amount.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
            competence.SelectedDate = original.Competence;
            due.SelectedDate = original.DueDate;
            paymentMethod.Text = original.PaymentMethod;
            notes.Text = original.Notes;
            recurrence.SelectedItem = Repeats.FirstOrDefault(x => x.Months == original.EveryMonths) ?? Repeats[1];
        }
        Content = new DialogFrame { Kind = "Neutro", IconName = "banknote", Title = Title, Subtitle = "Área do Proprietário · Ctrl+Enter salva · Esc fecha", Content = new SectionCard { Number = "1", Header = "Dados financeiros da despesa", Content = form }, Footer = DialogKit.Footer(this, "Salvar despesa", Save), ScrollBody = false };
        KeyboardFlow.Attach(this, Save);
        Loaded += (_, _) => description.Focus();
    }
    static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
    static void AddField(Panel panel, string label, Control control)
    {
        var title = Label(label);
        title.Margin = new Thickness(0, 10, 0, 4);
        panel.Children.Add(title);
        panel.Children.Add(control);
    }

    void Save()
    {
        Prompt12Ui.Run(this, () =>
        {
            if (string.IsNullOrWhiteSpace(description.Text)) throw new InvalidOperationException("Informe a descrição.");
            var competenceDate = competence.SelectedDate ?? throw new InvalidOperationException("Informe a competência.");
            var dueDate = due.SelectedDate ?? throw new InvalidOperationException("Informe o vencimento.");
            Result = new Expense
            {
                Description = description.Text.Trim(),
                Category = string.IsNullOrWhiteSpace(category.Text) ? "Outras" : category.Text.Trim(),
                Amount = Prompt12Ui.Money(amount, "o valor"),
                Competence = new DateTime(competenceDate.Year, competenceDate.Month, 1),
                DueDate = dueDate,
                PaymentMethod = paymentMethod.Text.Trim(),
                Notes = notes.Text.Trim(),
                Recurring = (recurrence.SelectedItem as Repeat)?.Months > 0,
                RecurrenceMonths = (recurrence.SelectedItem as Repeat)?.Months ?? 0,
                RecurrenceKey = (recurrence.SelectedItem as Repeat)?.Months > 0 ? description.Text.Trim().ToLowerInvariant() : ""
            };
            DialogResult = true;
        });
    }
}
