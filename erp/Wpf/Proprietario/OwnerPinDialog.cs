using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// PIN da Área do Proprietário no padrão dos diálogos (DialogFrame + DialogKit). Mesma regra: mínimo de 6 dígitos.
// "Esqueci o PIN" aparece só para Administrador conectado ao servidor (redefinição confirmada com a senha de login).
internal sealed class OwnerPinDialog : Window
{
	private readonly PasswordField pin = new PasswordField();

	public string Pin => pin.Password;

	// Verdadeiro quando o PIN foi redefinido: o servidor já deixou esta sessão desbloqueada.
	public bool WasReset { get; private set; }

	public OwnerPinDialog(bool create, DataStore? store = null)
	{
		Title = create ? "Criar PIN do proprietário" : "Área do proprietário";
		Width = Math.Min(460, SystemParameters.WorkArea.Width - 32);
		SizeToContent = SizeToContent.Height;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		var panel = new StackPanel();
		var field = DialogKit.LabeledField(create ? "Novo PIN (mínimo 6 dígitos)" : "PIN do proprietário", pin);
		panel.Children.Add(field.Panel);
		panel.Children.Add(DialogKit.Hint(create ? "Guarde o PIN: ele protege custos, lucros e despesas em todos os computadores." : "Custos, lucros e despesas ficam visíveis enquanto a área estiver desbloqueada.", 4));
		if (!create && store is { IsRemote: true } && App.CurrentUser.Role == "Administrador")
		{
			var forgot = new Button { Content = "Esqueci o PIN", Style = (Style)FindResource("QuietButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), ToolTip = "Criar um PIN novo confirmando a sua senha de login (só Administrador)" };
			forgot.Click += (_, _) => { if (new OwnerPinResetDialog(store) { Owner = this }.ShowDialog() == true) { WasReset = true; DialogResult = true; } };
			panel.Children.Add(forgot);
		}
		void Confirm()
		{
			if (pin.Password.Length < 6) { field.Error.Text = "Use no mínimo 6 dígitos."; field.Error.Visibility = Visibility.Visible; pin.MarkError(true); pin.Focus(); return; }
			DialogResult = true;
		}
		pin.Edited += () => { field.Error.Visibility = Visibility.Collapsed; pin.MarkError(false); };
		Content = new DialogFrame { Kind = "Neutro", IconName = "key-round", Title = create ? "Criar PIN" : "Área do proprietário", Subtitle = "Enter confirma · Esc fecha", Content = panel, Footer = DialogKit.Footer(this, create ? "Criar e entrar" : "Desbloquear", Confirm), ScrollBody = false };
		KeyboardFlow.Attach(this, Confirm);
		Loaded += (_, _) => pin.Focus();
	}
}

// Redefinir o PIN: senha de login de quem está conectado + PIN novo duas vezes. O servidor confere tudo e registra na auditoria.
internal sealed class OwnerPinResetDialog : Window
{
	private readonly PasswordField password = new PasswordField(), newPin = new PasswordField(), confirm = new PasswordField();
	private bool busy;

	public OwnerPinResetDialog(DataStore store)
	{
		Title = "Redefinir PIN do proprietário";
		Width = Math.Min(480, SystemParameters.WorkArea.Width - 32);
		SizeToContent = SizeToContent.Height;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		var panel = new StackPanel();
		var passwordField = DialogKit.LabeledField($"Sua senha de login ({App.CurrentUser.UserName})", password);
		var pinField = DialogKit.LabeledField("PIN novo (mínimo 6 dígitos, só números)", newPin);
		var confirmField = DialogKit.LabeledField("Repita o PIN novo", confirm);
		foreach (var f in new[] { passwordField, pinField, confirmField }) panel.Children.Add(f.Panel);
		panel.Children.Add(DialogKit.Hint("O PIN antigo deixa de valer em todos os computadores e a troca fica registrada na Auditoria.", 4));
		void Fail(DialogKit.Field field, PasswordField box, string message) { field.Error.Text = message; field.Error.Visibility = Visibility.Visible; box.MarkError(true); box.Focus(); }
		foreach (var (box, field) in new[] { (password, passwordField), (newPin, pinField), (confirm, confirmField) }) { var f = field; var b = box; b.Edited += () => { f.Error.Visibility = Visibility.Collapsed; b.MarkError(false); }; }
		async void Save()
		{
			if (busy) return;
			if (password.Password.Length == 0) { Fail(passwordField, password, "Informe a sua senha de login."); return; }
			if (newPin.Password.Length < 6 || !newPin.Password.All(char.IsAsciiDigit)) { Fail(pinField, newPin, "Use um PIN numérico com pelo menos 6 dígitos."); return; }
			if (confirm.Password != newPin.Password) { Fail(confirmField, confirm, "Os dois PINs não são iguais."); return; }
			busy = true;
			try { var (p, n) = (password.Password, newPin.Password); await Task.Run(() => store.ResetOwnerPin(p, n)); Toast.Show(Owner ?? this, "PIN redefinido.", tone: "ok"); DialogResult = true; }
			catch (UnauthorizedAccessException ex) { Fail(passwordField, password, ex.Message); }
			catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or TimeoutException) { AlertWindow.Info(this, "Não foi possível redefinir", ex.Message); }
			finally { busy = false; }
		}
		Content = new DialogFrame { Kind = "Neutro", IconName = "key-round", Title = "Redefinir PIN", Subtitle = "Enter avança · Esc fecha", Content = panel, Footer = DialogKit.Footer(this, "Redefinir PIN", Save), ScrollBody = false };
		KeyboardFlow.Attach(this, Save);
		Loaded += (_, _) => password.Focus();
	}
}
