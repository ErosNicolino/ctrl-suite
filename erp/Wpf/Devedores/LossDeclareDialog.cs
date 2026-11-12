using System;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// Declarar saldo como perda, no padrão dos diálogos (DialogFrame + DialogKit). Mesma regra: motivo obrigatório e confirmação.
public sealed class LossDeclareDialog : Window
{
	private readonly TextBox reason = new TextBox();

	private readonly TextBox notes = new TextBox
	{
		AcceptsReturn = true,
		Height = 90.0,
		TextWrapping = TextWrapping.Wrap,
		VerticalContentAlignment = VerticalAlignment.Top,
		Padding = new Thickness(0, 6, 0, 6)
	};

	private readonly DatePicker date = new DatePicker
	{
		SelectedDate = DateTime.Today
	};

	public string Reason => reason.Text.Trim();

	public string Notes => notes.Text.Trim();

	public DateTime Date => date.SelectedDate ?? DateTime.Today;

	public LossDeclareDialog(string customer, string order, decimal balance)
	{
		Title = "Declarar como perda";
		Width = Math.Min(560, SystemParameters.WorkArea.Width - 32);
		SizeToContent = SizeToContent.Height;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		var panel = new StackPanel();
		var reasonField = DialogKit.LabeledField("Motivo *", reason);
		panel.Children.Add(reasonField.Panel);
		panel.Children.Add(DialogKit.LabeledField("Data", date).Panel);
		panel.Children.Add(DialogKit.LabeledField("Observação", notes).Panel);
		panel.Children.Add(DialogKit.Hint("O pedido e o histórico ficam guardados; só o saldo sai da cobrança ativa.", 4));
		void Confirm()
		{
			if (Reason.Length == 0) { DialogKit.ShowError(reasonField, reason, "Informe por que o saldo está sendo classificado como perda."); return; }
			if (AlertWindow.Confirm(this, "Confirmar perda", "O pedido e o histórico serão preservados, mas o saldo sairá da cobrança ativa.", danger: true)) DialogResult = true;
		}
		Content = new DialogFrame { Kind = "Neutro", IconName = "triangle-alert", Title = customer, Subtitle = $"{order} · Saldo {balance:C2}", Content = panel, Footer = DialogKit.Footer(this, "Confirmar perda", Confirm), ScrollBody = false };
		KeyboardFlow.Attach(this, Confirm);
		Loaded += (_, _) => reason.Focus();
	}
}
