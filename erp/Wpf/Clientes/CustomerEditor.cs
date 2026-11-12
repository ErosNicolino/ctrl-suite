using System;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 3): diálogo azul-petróleo do cliente, com ficha ao vivo (iniciais, WhatsApp, o que falta).
// Mesmos campos (nome obrigatório; telefone, e-mail, CPF/CNPJ e endereço opcionais) e a mesma gravação (Persist/PersistAsync).
public sealed class CustomerEditor : Window
{
	private readonly TextBox name = new TextBox { Tag = "Nome ou razão social" };
	private readonly TextBox phone = new TextBox { Tag = "(11) 90000-0000" };
	private readonly TextBox email = new TextBox { Tag = "nome@exemplo.com" };
	private readonly TextBox cpf = new TextBox { Tag = "Somente se for emitir nota" };
	private readonly TextBox address = new TextBox { Tag = "Rua, número, bairro, cidade" };
	private readonly DialogKit.Field nameField;
	private readonly TextBlock avatar = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
	private readonly TextBlock cardName = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap };
	private readonly TextBlock cardPhone = new TextBlock();
	private readonly WhatsAppButton whatsapp = new WhatsAppButton { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
	private readonly StackPanel checks = new StackPanel();

	public string CustomerName => name.Text.Trim();
	public string Phone => PhoneText.Normalize(phone.Text);
	public string Email => email.Text.Trim();
	public string CpfCnpj => cpf.Text.Trim();
	public string Address => address.Text.Trim();

	public Func<string,string,string,string,string,Task>? PersistAsync {get;set;}
	public Action<string, string, string, string, string>? Persist { get; set; }
	// Telefone que já é de outro cliente: quem abriu o editor mostra o cadastro existente (recebe o telefone).
	public Action<string>? OpenExisting { get; set; }

	public CustomerEditor(CustomerProfile original,bool isNew=false)
	{
		Title = isNew ? "Novo cliente" : "Editar cliente";
		Width = Math.Min(860, SystemParameters.WorkArea.Width - 32);
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		var body = new StackPanel();
		nameField = DialogKit.LabeledField("Cliente *", name);
		var contact = new StackPanel(); contact.Children.Add(nameField.Panel); contact.Children.Add(DialogKit.LabeledField("Telefone / WhatsApp", phone).Panel);
		body.Children.Add(new SectionCard { Number = "1", Header = "Contato", Content = contact });
		var more = new StackPanel(); more.Children.Add(DialogKit.LabeledField("E-mail", email).Panel); more.Children.Add(DialogKit.LabeledField("CPF / CNPJ", cpf).Panel); more.Children.Add(DialogKit.LabeledField("Endereço", address).Panel);
		body.Children.Add(new SectionCard { Number = "2", Header = "Dados para nota e entrega", Hint = "opcionais", Content = more });
		name.Text = original.Name;
		phone.Text = CustomerDirectory.Clean(original.Contact);
		email.Text = original.Email;
		cpf.Text = original.CpfCnpj;
		address.Text = original.Address;
		PhoneInput.Attach(phone);
		// Ficha ao vivo.
		var card = new StackPanel();
		var circle = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Child = avatar, HorizontalAlignment = HorizontalAlignment.Left };
		circle.SetResourceReference(Border.BackgroundProperty, "KindCliente"); avatar.SetResourceReference(TextBlock.ForegroundProperty, "OnK");
		card.Children.Add(circle); cardName.Margin = new Thickness(0, 10, 0, 2); card.Children.Add(cardName); cardPhone.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); card.Children.Add(cardPhone); card.Children.Add(whatsapp);
		card.Children.Add(DialogKit.Rule()); card.Children.Add(checks);
		foreach (var box in new[] { name, phone, email, cpf, address }) box.TextChanged += (_, _) => RefreshCard();
		Content = new DialogFrame { Kind = "Cliente", IconName = "users", Title = Title, Subtitle = "Enter avança · Ctrl+Enter salva · Esc fecha", Content = body, Aside = DialogKit.Summary(card), AsideWidth = 290, Footer = DialogKit.Footer(this, "Salvar cliente", Save), ScrollBody = false };
		KeyboardFlow.Attach(this, Save);
		RefreshCard();
		UnsavedChanges.Attach(this, Save);
		Loaded += (_, _) => name.Focus();
	}

	private void RefreshCard()
	{
		var n = name.Text.Trim();
		avatar.Text = string.Concat(n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));
		if (avatar.Text.Length == 0) avatar.Text = "?";
		cardName.Text = n.Length == 0 ? "Nome do cliente" : n; cardName.Opacity = n.Length == 0 ? .5 : 1;
		cardPhone.Text = phone.Text.Trim().Length == 0 ? "Sem telefone" : phone.Text.Trim();
		whatsapp.Phone = phone.Text;
		checks.Children.Clear();
		Check(n.Length > 0, "Nome informado", "Informe o nome");
		Check(whatsapp.IsComplete, "Telefone completo (WhatsApp)", "Telefone incompleto", optional: true);
		Check(email.Text.Trim().Length > 0, "E-mail informado", "Sem e-mail", optional: true);
		Check(cpf.Text.Trim().Length > 0, "CPF/CNPJ informado", "Sem CPF/CNPJ (só para nota)", optional: true);
	}
	private void Check(bool ok, string okText, string pending, bool optional = false)
	{
		var key = ok ? "AccentInk" : optional ? "Muted" : "Warning";
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
		var icon = new UiIcon { IconName = ok ? "circle-check" : "circle-alert", Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) }; icon.SetResourceReference(UiIcon.ForegroundProperty, key);
		var text = new TextBlock { Text = ok ? okText : pending, FontSize = 13 }; text.SetResourceReference(TextBlock.ForegroundProperty, key);
		row.Children.Add(icon); row.Children.Add(text); checks.Children.Add(row);
	}

	private bool saving;
	private async void Save()
	{
        if(saving)return;
		if (CustomerName.Length == 0)
		{
			DialogKit.ShowError(nameField, name, "Informe o nome do cliente.");
			return;
		}
		saving=true;
		try
		{
			if(PersistAsync!=null)await PersistAsync(CustomerName,Phone,Email,CpfCnpj,Address);else Persist?.Invoke(CustomerName, Phone, Email, CpfCnpj, Address);
			DialogResult = true;
		}
		catch (Exception ex) when (ex.Message.StartsWith(CustomerBasicOperations.PhoneTakenPrefix))
		{
			// Telefone de outro cliente: não grava; oferece abrir o cadastro existente na lista.
			if (AlertWindow.Confirm(this, "Telefone já cadastrado", ex.Message + "\n\nAbrir o cadastro existente?")) { var phone = Phone; DialogResult = false; OpenExisting?.Invoke(phone); }
		}
		catch (Exception ex)
		{
			AlertWindow.Info(this, "Não foi possível salvar", ex.Message);
		}
        finally{saving=false;}
	}
}
