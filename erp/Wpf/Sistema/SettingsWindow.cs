using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GraficaCaixa.Wpf;

public sealed partial class SettingsWindow : Window
{
    bool writeBusy;
	private readonly DataStore store;

	private readonly AppData original;

	private readonly TextBox root = new TextBox();

	private readonly TextBox logo = new TextBox();

	private readonly ComboBox stampMode = new ComboBox();

	private readonly TextBox stamp = new TextBox { Tag = "Caminho da imagem do carimbo" };

	private readonly Image stampPreview = new Image { Height = 70, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };

	// Prévia do carimbo original (recurso embutido no programa).
	private static ImageSource LoadEmbeddedStamp() { using var stream = typeof(App).Assembly.GetManifestResourceStream("GraficaCaixa.ReceiptStamp.png") ?? throw new FileNotFoundException(); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }

	private readonly Dictionary<string, TextBox> rates = new Dictionary<string, TextBox>();

	private readonly Image preview = new Image
	{
		Height = 100.0,
		Stretch = Stretch.Uniform
	};

	private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

	// Backup programado (feito pelo serviço do computador principal).
	private readonly CheckBox backupEnabled = new() { Content = "Fazer backup automático" };
	private readonly TextBox backupDestination = new() { Tag = @"Ex.: E:\Backup-ERP ou C:\Users\...\OneDrive\Backup-ERP" };
	private readonly ComboBox backupMode = new();
	private readonly TextBox backupHours = new() { Width = 90, TextAlignment = TextAlignment.Right }, backupTime = new() { Width = 90, Tag = "19:00", HorizontalAlignment = HorizontalAlignment.Left }, backupKeep = new() { Width = 90, TextAlignment = TextAlignment.Right };
	private readonly CheckBox[] backupDays = new[] { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" }.Select(x => new CheckBox { Content = x, Margin = new Thickness(0, 0, 14, 0) }).ToArray();
	private readonly TextBlock backupStatus = new() { TextWrapping = TextWrapping.Wrap };
	private static readonly string[] BackupModes = ["intervalo", "diario", "semanal"];

	void BuildBackupTab(StackPanel panel)
	{
		var schedule = original.Backup ?? new BackupSchedule();
		panel.Children.Add(new TextBlock { Text = "Os backups automáticos do ERP ficam na mesma pasta do banco. Esta cópia programada vai para outro lugar (HD externo, pendrive, OneDrive…), para não perder tudo se o disco falhar.", TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 4, 0, 12) });
		backupEnabled.IsChecked = schedule.Enabled; backupEnabled.Style = TryFindResource("Switch") as Style ?? backupEnabled.Style; panel.Children.Add(backupEnabled);
		backupDestination.Text = schedule.Destination;
		var destinationRow = new DockPanel(); var choose = new Button { Content = "Escolher", Style = (Style)FindResource("SoftButton"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Escolher a pasta (use este botão no computador principal)" }; DockPanel.SetDock(choose, Dock.Right); destinationRow.Children.Add(choose); destinationRow.Children.Add(backupDestination);
		choose.Click += (_, _) => { var dialog = new OpenFolderDialog { Title = "Pasta de destino do backup" }; if (dialog.ShowDialog(this) == true) backupDestination.Text = dialog.FolderName; };
		panel.Children.Add(new FrameworkElement { Height = 12 });
		panel.Children.Add(DialogKit.LabeledField("Pasta de destino (vista pelo computador principal)", destinationRow, "É o computador principal que grava: se escolher em outro PC, use um caminho que exista no principal.").Panel);
		backupMode.ItemsSource = new[] { "A cada algumas horas", "Todo dia", "Em dias da semana" }; backupMode.SelectedIndex = Math.Max(0, Array.IndexOf(BackupModes, schedule.Mode));
		panel.Children.Add(DialogKit.LabeledField("Frequência", backupMode).Panel);
		backupHours.Text = schedule.IntervalHours.ToString(); backupTime.Text = schedule.Time; backupKeep.Text = schedule.KeepCopies.ToString();
		for (var i = 0; i < 7; i++) backupDays[i].IsChecked = schedule.Days.Contains(i);
		var hoursRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { backupHours, new TextBlock { Text = "horas entre um backup e outro (1 a 168)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) } } };
		var hoursField = DialogKit.LabeledField("Intervalo", hoursRow).Panel; panel.Children.Add(hoursField);
		var timeField = DialogKit.LabeledField("Horário (HH:mm)", backupTime).Panel; panel.Children.Add(timeField);
		var daysRow = new WrapPanel(); foreach (var day in backupDays) daysRow.Children.Add(day);
		var daysField = DialogKit.LabeledField("Dias", daysRow).Panel; panel.Children.Add(daysField);
		var keepRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { backupKeep, new TextBlock { Text = "cópias mais recentes ficam no destino; as mais antigas são apagadas (1 a 365)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap } } };
		panel.Children.Add(DialogKit.LabeledField("Guardar", keepRow).Panel);
		void UpdateMode() { hoursField.Visibility = backupMode.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; timeField.Visibility = backupMode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible; daysField.Visibility = backupMode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed; }
		backupMode.SelectionChanged += (_, _) => UpdateMode(); UpdateMode();
		panel.Children.Add(new TextBlock { Text = "Se o computador principal estiver desligado no horário, o backup é feito assim que o serviço voltar. Cada cópia é verificada antes de substituir as antigas.", TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 0, 0, 12) });
		var statusCard = new Border { Style = (Style)FindResource("NoteCalm"), Child = backupStatus }; panel.Children.Add(statusCard);
		var runNow = new Button { Content = "Fazer backup agora", Style = (Style)FindResource("SoftButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), ToolTip = "Usa a configuração já salva. Salve antes se mudou a pasta." }; panel.Children.Add(runNow);
		runNow.Click += async (_, _) =>
		{
			runNow.IsEnabled = false; runNow.Content = "Fazendo backup…";
			try { var result = await store.RemoteReadPostAsync<BackupStatus>("desktop/backup/run", new { }); ShowBackupStatus(result); if (result.LastError.Length == 0) Toast.Show(this, "Backup concluído e verificado.", tone: "ok"); else AlertWindow.Info(this, "Backup não concluído", result.LastError); }
			catch (Exception ex) { AlertWindow.Info(this, "Backup não concluído", ex.Message); }
			finally { runNow.IsEnabled = true; runNow.Content = "Fazer backup agora"; }
		};
		backupStatus.Text = "Carregando a situação do backup…";
		Loaded += async (_, _) => { try { ShowBackupStatus(await store.QueryRemoteAsync<BackupStatus>("desktop/backup/status")); } catch (Exception ex) { backupStatus.Text = "Situação indisponível: " + ex.Message; } };
	}
	void ShowBackupStatus(BackupStatus s)
	{
		var lines = new List<string> { s.LastSuccessAt is DateTime ok ? $"Último backup: {ok:dd/MM/yyyy HH:mm} · {Path.GetFileName(s.LastFile)}" : "Nenhum backup programado feito ainda." };
		if (s.FilesTotal > 0) lines.Add($"Fotos e PDFs: {s.FilesTotal} arquivo(s) no backup, {s.FilesCopied} novo(s) ou alterado(s) nesta cópia.");
		if (s.FilesError.Length > 0) lines.Add($"Fotos e PDFs: {s.FilesError}");
		if (s.LastError.Length > 0) lines.Add($"Última tentativa falhou ({s.LastAttemptAt:dd/MM HH:mm}): {s.LastError}");
		lines.Add(s.NextRunAt is DateTime next ? $"Próximo: {(next <= DateTime.Now ? "assim que possível" : next.ToString("ddd, dd/MM 'às' HH:mm", Pt))}" : "Backup automático desligado.");
		backupStatus.Text = string.Join("\n", lines);
	}
	BackupSchedule ReadBackup()
	{
		if (!int.TryParse(backupHours.Text, out var hours) || hours is < 1 or > 168) { backupHours.Focus(); throw new InvalidOperationException("Informe o intervalo do backup entre 1 e 168 horas."); }
		if (!TimeSpan.TryParseExact(backupTime.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out _)) { backupTime.Focus(); throw new InvalidOperationException("Informe o horário do backup no formato 19:00."); }
		if (!int.TryParse(backupKeep.Text, out var keep) || keep is < 1 or > 365) { backupKeep.Focus(); throw new InvalidOperationException("Informe quantas cópias guardar, de 1 a 365."); }
		var days = Enumerable.Range(0, 7).Where(i => backupDays[i].IsChecked == true).ToList();
		if (backupMode.SelectedIndex == 2 && days.Count == 0) throw new InvalidOperationException("Marque ao menos um dia da semana para o backup.");
		var destination = backupDestination.Text.Trim();
		if (backupEnabled.IsChecked == true && destination.Length == 0) { backupDestination.Focus(); throw new InvalidOperationException("Escolha a pasta de destino do backup."); }
		return new BackupSchedule { Enabled = backupEnabled.IsChecked == true, Destination = destination, Mode = BackupModes[Math.Max(0, backupMode.SelectedIndex)], IntervalHours = hours, Time = backupTime.Text.Trim(), Days = days.Count > 0 ? days : [1, 2, 3, 4, 5, 6], KeepCopies = keep };
	}

	private readonly TextBox companyName = new() { Tag = "Nome que aparece nos PDFs" }, companyDocument = new() { Tag = "00.000.000/0000-00" }, companyAddress = new() { Tag = "Rua, número - bairro" }, companyPhone = new() { Tag = "11 90000-0000" }, companyEmail = new() { Tag = "contato@grafica.com" }, companyPix = new() { Tag = "CPF/CNPJ, e-mail, +55 telefone ou chave aleatória" }, companyReview = new() { Tag = "https://g.page/r/... (Perfil da Empresa no Google › Pedir avaliações)" }, companyHours = new() { Tag = "Segunda a sexta: 9h às 18h", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 58, VerticalContentAlignment = VerticalAlignment.Top }, companyCity = new() { Tag = "Cidade dos recibos" };

	public SettingsWindow(DataStore store,AppData? prepared=null,int startTab=0)
	{
		SettingsWindow settingsWindow = this;
		this.store = store;
		original = prepared??(store.IsRemote?throw new InvalidOperationException("Carregue as configurações antes de abrir."):store.Load());
		Title = "Configurações";
		// UX 2.0 (pacote 4): diálogo central (cabeçalho, abas com sublinhado, rodapé fixo Cancelar / Salvar). Mesmos campos e validações.
		Width = Math.Min(780.0, SystemParameters.WorkArea.Width - 32);
		Height = Math.Min(700.0, SystemParameters.WorkArea.Height - 32);
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		TabControl tabs = new TabControl();
		Content = new DialogFrame { Kind = "Neutro", IconName = "settings", Title = "Configurações", Subtitle = "Dados da gráfica, taxas, arquivos, backup e regras · valem para todos os computadores", Content = tabs, Footer = DialogKit.Footer(this, "Salvar configurações", () => settingsWindow.Save()), ScrollBody = false };
		StackPanel stackPanel = Tab("Geral");
		stackPanel.Children.Add(new TextBlock { Text = "Dados da gráfica", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 4, 0, 6) });
		Note(stackPanel, "Aparecem no cabeçalho dos recibos, orçamentos e fichas em PDF.");
		var company = original.Company ?? new CompanyProfile();
		companyName.Text = company.Name; companyDocument.Text = company.Document; companyAddress.Text = company.Address; companyPhone.Text = company.Phone; companyEmail.Text = company.Email; companyCity.Text = company.City; companyPix.Text = company.PixKey; companyReview.Text = company.ReviewUrl; companyHours.Text = (company.OpeningHours ?? "").Replace("\n", Environment.NewLine);
		Grid Pair(FrameworkElement a, FrameworkElement b) { var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(b, 2); g.Children.Add(a); g.Children.Add(b); return g; }
		stackPanel.Children.Add(Pair(DialogKit.LabeledField("Nome da gráfica *", companyName).Panel, DialogKit.LabeledField("CNPJ / CPF", companyDocument).Panel));
		stackPanel.Children.Add(DialogKit.LabeledField("Endereço", companyAddress).Panel);
		stackPanel.Children.Add(Pair(DialogKit.LabeledField("Telefone", companyPhone).Panel, DialogKit.LabeledField("E-mail", companyEmail).Panel));
		stackPanel.Children.Add(Pair(DialogKit.LabeledField("Cidade (recibos e orçamentos)", companyCity).Panel, DialogKit.LabeledField("Chave Pix (Pix copia e cola nas mensagens)", companyPix).Panel));
		stackPanel.Children.Add(Pair(DialogKit.LabeledField("Horário de funcionamento (uma linha por período)", companyHours).Panel, DialogKit.LabeledField("Link de avaliação no Google (aparece depois da entrega)", companyReview).Panel));
		stackPanel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 10) });
		stackPanel.Children.Add(new TextBlock { Text = "Logotipo", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 0, 0, 6) });
		Note(stackPanel, "Identidade visual — logotipo oficial da gráfica. Selecione uma imagem PNG ou JPEG dentro da pasta compartilhada do ERP.");
		logo.Text = original.LogoPath;
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Logotipo da gráfica",
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		});
		stackPanel.Children.Add(logo);
		Button button2 = new Button
		{
			Content = "Selecionar logotipo",
			Style = (Style)FindResource("SoftButton"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
		};
		var logoActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
		stackPanel.Children.Add(logoActions);
		button2.Margin = new Thickness(0, 0, 8, 0);
		logoActions.Children.Add(button2);
		button2.Click += (object _, RoutedEventArgs _) =>
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Selecionar logotipo da gráfica",
				Filter = "Imagens (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(settingsWindow) == true)
			{
				settingsWindow.logo.Text = openFileDialog.FileName;
				try
				{
					settingsWindow.Preview();
				}
				catch (Exception ex2)
				{
					AlertWindow.Info(settingsWindow, "Logotipo", ex2.Message);
				}
			}
		};
		stackPanel.Children.Add(preview);
		Button button3 = new Button
		{
			Content = "Visualizar logotipo",
			Style = (Style)FindResource("SoftButton"),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		logoActions.Children.Add(button3);
		button3.Click += (object _, RoutedEventArgs _) =>
		{
			try
			{
				settingsWindow.Preview();
			}
			catch (Exception ex2)
			{
				AlertWindow.Info(settingsWindow, "Logotipo", ex2.Message);
			}
		};
		try
		{
			Preview();
		}
		catch (Exception ex)
		{
			Note(stackPanel, ex.Message);
		}
		Button button4 = new Button
		{
			Content = "Área do Proprietário",
			Style = (Style)FindResource("SoftButton"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 10) });
		stackPanel.Children.Add(new TextBlock { Text = "Carimbo", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 0, 0, 6) });
		Note(stackPanel, "Aparece ao lado da assinatura nos recibos e orçamentos. Para uma imagem própria, use PNG (de preferência com fundo transparente) dentro da pasta compartilhada.");
		stampMode.ItemsSource = new[] { "Carimbo original do programa", "Imagem própria", "Sem carimbo" };
		stampMode.SelectedIndex = !company.ShowStamp ? 2 : string.IsNullOrWhiteSpace(company.StampPath) ? 0 : 1;
		stamp.Text = company.StampPath;
		stackPanel.Children.Add(DialogKit.LabeledField("Carimbo nos PDFs", stampMode).Panel);
		var stampChoose = new Button { Content = "Selecionar carimbo", Style = (Style)FindResource("SoftButton"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Escolher a imagem do carimbo (PNG ou JPEG)" };
		var stampRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(stampChoose, Dock.Right); stampRow.Children.Add(stampChoose); stampRow.Children.Add(stamp);
		stackPanel.Children.Add(stampRow); stackPanel.Children.Add(stampPreview);
		void StampPreview()
		{
			try
			{
				var file = stamp.Text.Trim();
				stampPreview.Source = stampMode.SelectedIndex == 2 ? null : stampMode.SelectedIndex == 0 || file.Length == 0 ? LoadEmbeddedStamp() : ProductPhotos.Read(DocumentPaths.ResolveStoredPath(original.DocumentsRoot, file));
			}
			catch { stampPreview.Source = null; }
			stampRow.Visibility = stampMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
			stampPreview.Visibility = stampPreview.Source == null ? Visibility.Collapsed : Visibility.Visible;
		}
		stampMode.SelectionChanged += (_, _) => StampPreview();
		stampChoose.Click += (_, _) => { var dialog = new OpenFileDialog { Title = "Selecionar imagem do carimbo", Filter = "Imagens (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", CheckFileExists = true }; if (dialog.ShowDialog(settingsWindow) == true) { stamp.Text = dialog.FileName; StampPreview(); } };
		StampPreview();
		stackPanel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 10) });
		stackPanel.Children.Add(new TextBlock { Text = "Outros ajustes", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 0, 0, 6) });
		var shortcuts = new StackPanel { Orientation = Orientation.Horizontal };
		stackPanel.Children.Add(shortcuts);
		button4.Margin = new Thickness(0, 0, 8, 0);
		shortcuts.Children.Add(button4);
		button4.Click += (object _, RoutedEventArgs _) =>
		{
			OwnerAreaWindow ownerAreaWindow = new OwnerAreaWindow(store);
			ownerAreaWindow.Owner = settingsWindow;
			ownerAreaWindow.ShowDialog();
		};
		Button button5 = new Button
		{
			Content = "Parâmetros de orçamento existentes",
			Style = (Style)FindResource("SoftButton"),
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		button5.Margin = new Thickness(0);button5.Content = "Parâmetros de orçamento";
		shortcuts.Children.Add(button5);
		button5.Click += async (object _, RoutedEventArgs _) =>
		{
			BudgetSettingsWindow budgetSettingsWindow = new BudgetSettingsWindow(store,await store.BusinessContextAsync("budget"));
			budgetSettingsWindow.Owner = settingsWindow;
			budgetSettingsWindow.ShowDialog();
		};
		StackPanel stackPanel2 = Tab("Taxas de pagamento");
		Note(stackPanel2, "Percentuais estimados (%). Pagamentos novos usam estas taxas; o histórico mantém as taxas registradas.");
		foreach (KeyValuePair<string, decimal> rate in PaymentPolicy.Rates)
		{
			Grid grid = new Grid
			{
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition());
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(110.0)
			});
			grid.Children.Add(new TextBlock
			{
				Text = rate.Key,
				VerticalAlignment = VerticalAlignment.Center
			});
			TextBox textBox = new TextBox
			{
				Text = rate.Value.ToString("0.####", Pt)
			};
			rates[rate.Key] = textBox;
			Grid.SetColumn(textBox, 1);
			grid.Children.Add(textBox);
			stackPanel2.Children.Add(grid);
		}
		StackPanel stackPanel3 = Tab("Arquivos e caminhos");
		Note(stackPanel3, "Pasta base compartilhada já utilizada pelo ERP:");
		root.Text = original.DocumentsRoot;
		stackPanel3.Children.Add(root);
		Note(stackPanel3, "PDFs de pedidos: Impressões\nRecibos e relatórios: Fechamentos\nFotos de produtos: Produtos/Imagens\nOs subdiretórios são relativos à pasta base para manter compatibilidade entre os PCs. Alterar a referência não move arquivos. O banco permanece no local atual.");
		Button test = new Button
		{
			Content = "Testar acesso",
			Style = (Style)FindResource("SoftButton"),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		stackPanel3.Children.Add(test);
		test.Click += async (object _, RoutedEventArgs _) =>
		{
			try
			{
				test.IsEnabled = false;
				var documentsRoot = settingsWindow.root.Text.Trim();
				await Task.Run(() => { DocumentPaths.Validate(documentsRoot); DocumentPaths.Validate(System.IO.Path.Combine(documentsRoot, "Impressões")); });
				Toast.Show(settingsWindow, "Pasta disponível: leitura e gravação confirmadas neste computador. Confira também no outro PC.", tone: "ok");
			}
			catch (Exception ex2)
			{
				AlertWindow.Info(settingsWindow, "Pasta indisponível", ex2.Message);
			}
			finally
			{
				test.IsEnabled = true;
			}
		};
		BuildBackupTab(Tab("Backup"));
		BuildRulesTab(Tab("Regras"));
		tabs.SelectedIndex = Math.Clamp(startTab, 0, tabs.Items.Count - 1);
		UnsavedChanges.Attach(this, Save);
		static void Note(StackPanel panel, string text)
		{
			panel.Children.Add(new TextBlock
			{
				Text = text,
				TextWrapping = TextWrapping.Wrap,
				Style = (Style)panel.FindResource("Hint"),
				Margin = new Thickness(0.0, 4.0, 0.0, 10.0)
			});
		}
		StackPanel Tab(string title)
		{
			StackPanel stackPanel4 = new StackPanel
			{
				Margin = new Thickness(2.0, 16.0, 8.0, 8.0)
			};
			tabs.Items.Add(new TabItem
			{
				Header = title,
				Content = new ScrollViewer
				{
					Content = stackPanel4,
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto
				}
			});
			return stackPanel4;
		}
	}

	private void Preview()
	{
		string text = logo.Text.Trim();
		if (text.Length > 0)
		{
			text = DocumentPaths.ResolveStoredPath(original.DocumentsRoot, text);
		}
		// Sem logotipo próprio: mostra o logo padrão vetorial do tema (claro/escuro), o mesmo do menu lateral.
		if (text.Length == 0) preview.Source = Application.Current.TryFindResource("Logo") as ImageSource ?? new BitmapImage(new Uri("pack://application:,,,/" + typeof(App).Assembly.GetName().Name + ";component/Assets/brand-logo.png"));
		else preview.Source = ProductPhotos.Read(text);
	}

	public static string Snapshot(AppData d)
	{
		return JsonSerializer.Serialize(new
		{
			DocumentsRoot = d.DocumentsRoot,
			LogoPath = d.LogoPath,
			Company = d.Company,
			Backup = d.Backup,
			Rates = d.PaymentRates.OrderBy((KeyValuePair<string, decimal> x) => x.Key)
		});
	}

	private async void Save()
	{
        if(writeBusy)return;writeBusy=true;try{
		try
		{
			if (!App.CurrentUser.Role.Equals("Administrador", StringComparison.OrdinalIgnoreCase))
			{
				throw new UnauthorizedAccessException("Somente administradores podem alterar configurações.");
			}
			string path = root.Text.Trim();
			string logoPath = logo.Text.Trim();
			if (path != original.DocumentsRoot)
			{
				if ((store.IsRemote || App.PrincipalConnection != null) && !path.StartsWith("\\\\"))
				{
					throw new InvalidOperationException("Use um caminho de rede compartilhado: \\\\computador\\compartilhamento\\ERP.");
				}
				path = DocumentPaths.Validate(path);
			}
			string effectiveRoot = ((path == original.DocumentsRoot) ? DocumentPaths.Root(path) : path);
			// Logotipo e carimbo: PNG/JPEG existente, dentro da pasta compartilhada, gravado no formato canônico (vale nos dois PCs).
			string SharedImage(string raw, string what)
			{
				if (raw.Length == 0) return "";
				if (!new ReadOnlySpan<string>(new string[3] { ".png", ".jpg", ".jpeg" }).Contains(Path.GetExtension(raw), StringComparer.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException($"Selecione uma imagem PNG ou JPEG para o {what}.");
				}
				if (!Path.IsPathFullyQualified(raw))
				{
					throw new InvalidOperationException($"O {what} deve apontar para uma imagem existente em caminho completo.");
				}
				if ((store.IsRemote || App.PrincipalConnection != null) && !raw.StartsWith("\\\\"))
				{
					throw new InvalidOperationException($"Use uma referência de rede para o {what} nos dois PCs.");
				}
				string path2 = DocumentRootResolver.MapToEffective(raw, path, effectiveRoot);
				if (!DocumentRootResolver.IsWithinEquivalentRoot(path2, path, effectiveRoot))
				{
					throw new InvalidOperationException($"O {what} deve estar dentro da pasta compartilhada configurada para o ERP.");
				}
				if (!File.Exists(path2))
				{
					throw new InvalidOperationException($"O arquivo do {what} não foi encontrado na pasta compartilhada.");
				}
				ProductPhotos.Read(path2);
				return DocumentRootResolver.MapToCanonical(path2, path, effectiveRoot);
			}
			logoPath = SharedImage(logoPath, "logotipo");
			if (companyName.Text.Trim().Length == 0) { companyName.Focus(); throw new InvalidOperationException("Informe o nome da gráfica (aparece nos PDFs)."); }
			if (PixBrCode.Validate(companyPix.Text) is string pixError) { companyPix.Focus(); throw new InvalidOperationException(pixError); }
			if (companyReview.Text.Trim().Length > 0 && OrderTracking.ReviewLink(companyReview.Text) == null) { companyReview.Focus(); throw new InvalidOperationException("Link de avaliação: cole o endereço completo, começando com https://"); }
			if (stampMode.SelectedIndex == 1 && stamp.Text.Trim().Length == 0) { stamp.Focus(); throw new InvalidOperationException("Selecione a imagem do carimbo ou escolha outra opção."); }
			var backup = ReadBackup();
			var stampPath = stampMode.SelectedIndex == 1 ? SharedImage(stamp.Text.Trim(), "carimbo") : "";
			var profile = new CompanyProfile { Name = companyName.Text.Trim(), Document = companyDocument.Text.Trim(), Address = companyAddress.Text.Trim(), Phone = companyPhone.Text.Trim(), Email = companyEmail.Text.Trim(), City = companyCity.Text.Trim().Length > 0 ? companyCity.Text.Trim() : "São Paulo", ShowStamp = stampMode.SelectedIndex != 2, StampPath = stampPath, PixKey = PixBrCode.NormalizeKey(companyPix.Text), ReviewUrl = companyReview.Text.Trim(), OpeningHours = string.Join("\n", companyHours.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) };
			Dictionary<string, decimal> values = new Dictionary<string, decimal>();
			foreach (KeyValuePair<string, TextBox> rate in rates)
			{
				if (!decimal.TryParse(rate.Value.Text, NumberStyles.Number, Pt, out var result) || result < 0m || result > 100m || decimal.Round(result, 4) != result)
				{
					throw new InvalidOperationException("Informe taxas de 0% a 100%, com até quatro casas decimais.");
				}
				values[rate.Key] = result;
			}
			var rules = ReadRules();
			await BusinessUi.ExecuteAsync(store,"settings.save",Guid.Empty,new SettingsArgs(original.DocumentsRoot,original.LogoPath,original.PaymentRates,original.Company,original.Backup),new SettingsArgs(path,logoPath,values,profile,backup),()=>store.Update((AppData d) =>
			{
				if (Snapshot(d) != Snapshot(original))
				{
					throw new InvalidOperationException("Configuração alterada em outro computador. Reabra a janela.");
				}
				string oldValue = Snapshot(d);
				d.DocumentsRoot = path;
				d.LogoPath = logoPath;
				d.PaymentRates = values;
				d.Company = profile;
				d.Backup = backup;
				AuditService.Record(d, App.CurrentUser, "Configurações alteradas", "Configurações", "global", oldValue, Snapshot(d), "", "Dados da gráfica, taxas, logotipo e referência de arquivos.");
				return 0;
			}));
			await SaveRulesAsync(rules);
			PaymentPolicy.Configure(values);
			ReportWriter.Company = profile;
			ReportWriter.StampPath = profile.StampPath.Length == 0 ? "" : DocumentPaths.ResolveStoredPath(path, profile.StampPath);
			DialogResult = true;
		}
		catch (Exception ex)
		{
			AlertWindow.Info(this, "Confira as configurações", ex.Message);
		}
	
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
}
